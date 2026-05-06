# Boiler Telemetry — Запуск в Minikube

## Требования

- Docker
- Minikube
- kubectl
- Helm 3

---

## 1. Запустить Minikube

```bash
minikube start --driver=docker --memory=4096 --cpus=3
```

> ⚠️ После каждого старта виртуалки нужно восстановить системные лимиты (см. шаг 1.1).

### 1.1 Исправить системные лимиты (обязательно после каждого перезапуска VM)

Без этого поды падают с ошибкой `inotify instances reached` и Elasticsearch не стартует.

```bash
# Лимит inotify — нужен для .NET приложений
minikube ssh "sudo sysctl -w fs.inotify.max_user_instances=1024"

# Лимит памяти для Elasticsearch
minikube ssh "sudo sysctl -w vm.max_map_count=262144"
```

---

## 2. Собрать Docker образы

```bash
cd app

docker build -f src/BoilerTelemetry.Api/Dockerfile -t app-boiler-telemetry-api:latest . > /tmp/b-api.log 2>&1 &
docker build -f src/BoilerTelemetry.AnomalyService/Dockerfile -t app-anomaly-service:latest . > /tmp/b-anomaly.log 2>&1 &
docker build -f src/BoilerTelemetry.NotificationWorker/Dockerfile -t app-notification-worker:latest . > /tmp/b-notif.log 2>&1 &
wait && echo "Все образы собраны"
```

> Первая сборка занимает 3–5 минут на каждый сервис (скачиваются NuGet пакеты). Повторные сборки быстрые — используется Docker кэш.

---

## 3. Загрузить образы в Minikube

```bash
minikube image load app-boiler-telemetry-api:latest
minikube image load app-anomaly-service:latest
minikube image load app-notification-worker:latest
```

> Образы загружаются последовательно — параллельная загрузка блокирует терминал.

---

## 4. Задеплоить через Helm

```bash
cd helm
helm upgrade --install boiler-telemetry ./boiler-telemetry --namespace default
```

---

## 5. Проверить что всё запущено

```bash
kubectl get pods
```

Ожидаемый результат — все поды `1/1 Running`:

| Под | Назначение |
|-----|-----------|
| `boiler-telemetry-api` ×2 | REST API (2 реплики, Redis кэш) |
| `boiler-telemetry-anomaly-service` ×2 | Детектор аномалий (Kafka consumer) |
| `boiler-telemetry-notification-worker` ×2 | Обработчик уведомлений (Kafka consumer) |
| `boiler-telemetry-nginx` | Reverse proxy |
| `postgres` | База данных |
| `redis` | Кэш между репликами API |
| `kafka` + `zookeeper` | Очередь сообщений |
| `influxdb` | Time-series метрики |
| `elasticsearch` | Хранилище логов |
| `kibana` | UI для логов и алертов |

> Elasticsearch и Kibana поднимаются дольше всех (~3–5 минут, скачиваются образы ~1.5GB).

---

## 6. Доступ к сервисам

### API (через nginx)

```bash
# Узнать IP minikube
minikube ip  # обычно 192.168.49.2

# Адрес API
http://$(minikube ip):30080/api/v1/boilers
```

### Kibana (просмотр логов и алерты)

```bash
# NodePort — открыть в браузере:
http://$(minikube ip):30601

# Или через port-forward:
kubectl port-forward svc/kibana 5601:5601
# затем открыть http://localhost:5601
```

> Логи всех сервисов пишутся в JSON формате (Serilog) и отправляются напрямую в Elasticsearch через `Serilog.Sinks.Elasticsearch`. Индекс: `boiler-telemetry-YYYY.MM`.

### Kafka топики

Создаются автоматически при старте, либо вручную:

```bash
KAFKA_POD=$(kubectl get pod -l app=kafka -o jsonpath='{.items[0].metadata.name}')
kubectl exec $KAFKA_POD -- kafka-topics --create --topic telemetry-events --bootstrap-server kafka-broker:9092 --partitions 3 --replication-factor 1
kubectl exec $KAFKA_POD -- kafka-topics --create --topic anomaly-events --bootstrap-server kafka-broker:9092 --partitions 3 --replication-factor 1
kubectl exec $KAFKA_POD -- kafka-topics --list --bootstrap-server kafka-broker:9092
```

---

## 7. Тестовые запросы

```bash
BASE=http://$(minikube ip):30080/api/v1

# Создать котёл
curl -X POST $BASE/boilers \
  -H "Content-Type: application/json" \
  -d '{"name":"Boiler-1","location":"Room-1","temperatureThreshold":100.0,"pressureThreshold":5.0}'

# Список котлов
curl $BASE/boilers

# Отправить нормальную телеметрию
curl -X POST $BASE/telemetry \
  -H "Content-Type: application/json" \
  -d '{"boilerId":"<ID>","temperature":85.0,"pressure":3.2,"flowRate":1.8,"timestamp":"2026-05-05T10:00:00Z"}'

# Отправить аномальную телеметрию (превышение порогов)
curl -X POST $BASE/telemetry \
  -H "Content-Type: application/json" \
  -d '{"boilerId":"<ID>","temperature":155.0,"pressure":9.5,"flowRate":0.1,"timestamp":"2026-05-05T10:00:00Z"}'

# Невалидный запрос (проверка логирования ошибок валидации)
curl -X POST $BASE/telemetry \
  -H "Content-Type: application/json" \
  -d '{"boilerId":"<ID>","temperature":-50,"pressure":-1,"flowRate":-5}'
```

---

## 8. Просмотр логов

### Через kubectl (сырые JSON логи)

```bash
# Логи API
kubectl logs -l app.kubernetes.io/component=api --tail=50

# Логи anomaly-service
kubectl logs -l app.kubernetes.io/component=anomaly-service --tail=50

# Логи notification-worker
kubectl logs -l app.kubernetes.io/component=notification-worker --tail=50
```

### Через Kibana (удобный UI)

1. Открыть `http://$(minikube ip):30601`
2. Перейти в **Discover**
3. Создать Data View с паттерном `boiler-telemetry-*`
4. Фильтровать по полям: `@l` (уровень), `RequestPath`, `StatusCode`, `SourceContext`

---

## Остановить всё

```bash
helm uninstall boiler-telemetry
minikube stop
```

---

## Пересобрать и обновить один сервис

```bash
# Пересобрать образ
cd app
docker build -f src/BoilerTelemetry.Api/Dockerfile -t app-boiler-telemetry-api:latest .

# Загрузить в minikube
minikube image load app-boiler-telemetry-api:latest

# Рестартовать поды (подхватят новый образ)
kubectl rollout restart deployment/boiler-telemetry-api
```

---

## Известные проблемы

| Проблема | Причина | Решение |
|----------|---------|---------|
| Поды падают с `inotify instances reached` | Лимит inotify не установлен после перезапуска VM | Выполнить шаг 1.1 |
| Elasticsearch не стартует | `vm.max_map_count` слишком маленький | Выполнить шаг 1.1 |
| `nc: bad address 'kafka'` в initContainers | Сервис Kafka называется `kafka-broker` (переименован во избежание конфликта с K8s env `KAFKA_PORT`) | Уже исправлено в манифестах |
| Образы не найдены в minikube (`ErrImageNeverPull`) | Образы не загружены через `minikube image load` | Выполнить шаг 3 |
