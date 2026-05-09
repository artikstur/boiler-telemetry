# Деплой Boiler Telemetry на Windows + minikube

Пошаговая инструкция полного запуска с нуля. Базы создаются автоматически — руками ничего в SQL писать не нужно.

## 0. Что должно быть установлено

- **Docker Desktop** (с WSL2-бэкендом, проверь что он запущен — иконка кита в трее).
- **kubectl**: `winget install Kubernetes.kubectl`.
- **minikube**: положи бинарник в любую директорию из PATH. Если не в PATH — скачай в `%LOCALAPPDATA%\bin`:
  ```powershell
  Invoke-WebRequest -UseBasicParsing -Uri 'https://github.com/kubernetes/minikube/releases/latest/download/minikube-windows-amd64.exe' -OutFile "$env:LOCALAPPDATA\bin\minikube.exe"
  ```
- **helm**: то же самое:
  ```powershell
  $z = "$env:TEMP\helm.zip"
  Invoke-WebRequest -UseBasicParsing -Uri 'https://get.helm.sh/helm-v3.16.2-windows-amd64.zip' -OutFile $z
  Expand-Archive $z "$env:TEMP\helm-extract" -Force
  Copy-Item "$env:TEMP\helm-extract\windows-amd64\helm.exe" "$env:LOCALAPPDATA\bin\helm.exe"
  ```
- Добавить `%LOCALAPPDATA%\bin` в PATH (или в каждой сессии: `$env:Path = "$env:LOCALAPPDATA\bin;$env:Path"`).

Проверка:
```powershell
docker --version
kubectl version --client
minikube version
helm version --short
```

## 1. Освободить порты 5432 / 8086 на хосте (если заняты)

На Windows 11 c WSL2-mirror networking, контейнерные порты конфликтуют с нативными службами. Если на хосте уже стоит PostgreSQL — он перехватит 5432. Поэтому compose-файл публикует БД на нестандартных портах: **25432** (postgres) и **28086** (influx). Менять ничего не нужно — просто проверь что эти порты свободны:

```powershell
netstat -ano | findstr ":25432 :28086"
```

Если что-то слушает — поправь `infra/databases/docker-compose.yml` и `helm/boiler-telemetry/values.yaml` (`externalDatabases.postgres.port`, `externalDatabases.influx.url`).

## 2. Запуск БД в Docker (ВНЕ кластера)

PostgreSQL и InfluxDB живут на хосте в обычном Docker — без k8s. Они автоматически создают:
- БД `boiler_telemetry`, юзер `postgres/postgres`
- InfluxDB org `boiler-org`, bucket `telemetry`, token `dev-token`

```powershell
docker compose -f infra\databases\docker-compose.yml up -d

# Дождаться healthy:
docker ps --filter "name=boiler-" --format "table {{.Names}}\t{{.Status}}"
```

Должно показать `Up X seconds (healthy)` для обоих контейнеров.

## 3. Запуск minikube

```powershell
minikube start --cpus=4 --memory=6144 --driver=docker --addons=metrics-server
```

`metrics-server` нужен чтобы HPA работал. Минимально требуется ~6 ГБ RAM (OpenSearch ест 1 ГБ, остальное — приложения).

## 4. Сборка docker-образов и заливка в minikube

```powershell
$app = "$PWD\app"
docker build -t app-boiler-telemetry-api:latest -f "$app\src\BoilerTelemetry.Api\Dockerfile" $app
docker build -t app-anomaly-service:latest      -f "$app\src\BoilerTelemetry.AnomalyService\Dockerfile" $app
docker build -t app-notification-worker:latest  -f "$app\src\BoilerTelemetry.NotificationWorker\Dockerfile" $app

minikube image load app-boiler-telemetry-api:latest
minikube image load app-anomaly-service:latest
minikube image load app-notification-worker:latest
```

> ⚠️ При **повторной** сборке `minikube image load` НЕ перезаписывает существующий образ (известный баг). Если меняешь код и собираешь заново, сначала удали образ из minikube:
> ```powershell
> kubectl scale deploy -n boiler boiler-notification-worker --replicas=0   # отвязать pod от образа
> minikube ssh "docker rmi -f docker.io/library/app-notification-worker:latest"
> minikube image load app-notification-worker:latest
> kubectl scale deploy -n boiler boiler-notification-worker --replicas=2
> ```
> (То же для остальных образов.)

## 5. Установка Helm-чарта

Чарт ставит в кластер: API ×2, AnomalyService ×2, NotificationWorker ×2, Kafka, Redis, OpenSearch, OpenSearch Dashboards, Jaeger, Grafana, nginx, плюс HPA + PodDisruptionBudget. Топики Kafka создаются Helm post-install hook'ом автоматически.

```powershell
helm upgrade --install boiler helm\boiler-telemetry `
    --namespace boiler --create-namespace `
    --set externalHost=host.minikube.internal `
    --timeout 6m
```

Параметр `externalHost` — это DNS-имя, по которому поды видят хост-машину Windows. Для minikube driver=docker это `host.minikube.internal`. Для Docker Desktop K8s или kind используй `host.docker.internal`.

Подожди примерно 2–3 минуты пока всё стартует:
```powershell
kubectl get pods -n boiler -w
```

Когда все READY = 1/1 — готово. Должно быть:
```
boiler-anomaly-service-...      1/1
boiler-anomaly-service-...      1/1
boiler-api-...                  1/1
boiler-api-...                  1/1
boiler-nginx-...                1/1
boiler-notification-worker-...  1/1
boiler-notification-worker-...  1/1
grafana-...                     1/1
jaeger-...                      1/1
kafka-...                       1/1
kafka-topic-init-...            0/1     Completed   ← так и должно быть
opensearch-...                  1/1
opensearch-dashboards-...       1/1
redis-...                       1/1
```

## 6. Проверка что всё работает

См. `ACCESS.md` — там URL и креды для Grafana, OpenSearch Dashboards, Jaeger UI и API.

Быстрая проверка из консоли:
```powershell
$env:Path = "$env:LOCALAPPDATA\bin;$env:Path"
Start-Process kubectl -ArgumentList 'port-forward','-n','boiler','svc/boiler-nginx','18080:8080' -WindowStyle Hidden
Start-Sleep 3
Invoke-RestMethod http://localhost:18080/health
# -> Healthy
```

Дальше используй Postman-коллекцию `boiler-telemetry.postman_collection.json` для прогонки сценариев (создание бойлера, отправка телеметрии, проверка истории, аномалии).

## 7. Полный сброс

```powershell
helm uninstall boiler -n boiler
kubectl delete namespace boiler
docker compose -f infra\databases\docker-compose.yml down -v   # -v снесёт данные БД
minikube delete                                                # снесёт всё в k8s
```

## Что куда пишется

| Источник                    | Куда                                                |
|-----------------------------|-----------------------------------------------------|
| Boilers (CRUD)              | PostgreSQL (на хосте, порт 25432) → таблица `boilers` |
| Телеметрия (показания)      | InfluxDB (на хосте, порт 28086) → bucket `telemetry`  |
| События телеметрии          | Kafka (в кластере) → топик `telemetry-events`         |
| События аномалий            | Kafka (в кластере) → топик `anomaly-events`           |
| Уведомления                 | PostgreSQL → таблица `notifications`                  |
| Логи всех сервисов          | OpenSearch → индекс `boiler-telemetry-YYYY.MM.DD`     |
| Трейсы запросов             | Jaeger (in-memory)                                    |

## Автоматическое создание схемы БД

При старте подов:
- `boiler-api`: вызывает `db.Database.EnsureCreated()` — создаёт таблицу `boilers`.
- `boiler-notification-worker`: выполняет `CREATE TABLE IF NOT EXISTS notifications (...)`. Idempotent — безопасно при повторных стартах.
- `infra/databases/docker-compose.yml`: PostgreSQL автоматически создаёт БД `boiler_telemetry` через `POSTGRES_DB`. InfluxDB через `DOCKER_INFLUXDB_INIT_MODE=setup` сам создаёт org/bucket/token при первом запуске.
- `kafka-topic-init` Job (helm hook `post-install,post-upgrade`) создаёт топики `telemetry-events` и `anomaly-events`.

Никаких ручных SQL/CLI команд не требуется.
