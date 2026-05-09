# UI и доступы

`<minikube-ip>` — узнай через `minikube ip` (обычно `192.168.49.2`).

## 🌐 Веб-интерфейсы (NodePort на minikube)

| Сервис                  | URL                                  | Креды             | Что показывает                                            |
|-------------------------|--------------------------------------|-------------------|------------------------------------------------------------|
| **API (через nginx)**   | `http://<minikube-ip>:30080`         | —                 | Endpoints `/health`, `/api/v1/boilers`, `/api/v1/telemetry`, Swagger недоступен (Production) |
| **Grafana**             | `http://<minikube-ip>:30300`         | `admin` / `admin` | Логи (datasource OpenSearch) и трейсы (datasource Jaeger). Anonymous режим = Viewer |
| **OpenSearch Dashboards** | `http://<minikube-ip>:30601`       | без авторизации   | Просмотр и поиск логов всех сервисов в индексе `boiler-telemetry-*` |
| **Jaeger UI**           | `http://<minikube-ip>:30686`         | без авторизации   | Распределённые трейсы запросов через api → anomaly → notification |
| **Kafka UI**            | `http://<minikube-ip>:30808`         | без авторизации   | Топики (`telemetry-events`, `anomaly-events`), сообщения, группы консьюмеров |

## 💾 Базы данных (Docker на хосте, доступны напрямую)

| Сервис      | Адрес              | Креды                                     | Что внутри                                |
|-------------|--------------------|-------------------------------------------|--------------------------------------------|
| **PostgreSQL** | `localhost:25432`  | user `postgres`, pass `postgres`, db `boiler_telemetry` | Таблицы `boilers`, `notifications`         |
| **InfluxDB**   | `http://localhost:28086` | user `admin`, pass `adminpassword`, **token `dev-token`**, org `boiler-org`, bucket `telemetry` | Точки телеметрии (temperature, pressure)   |

InfluxDB также имеет **встроенный UI** — открой `http://localhost:28086` в браузере, логинься `admin` / `adminpassword`. Перейди в Data Explorer → bucket `telemetry` → measurement `boiler_readings`.

PostgreSQL смотрим через `psql`:
```powershell
docker exec -it boiler-postgres psql -U postgres -d boiler_telemetry
\dt                                  -- список таблиц
SELECT * FROM boilers;
SELECT * FROM notifications ORDER BY created_at DESC LIMIT 20;
\q
```

Или через любой GUI-клиент (DBeaver, pgAdmin) — host `localhost`, port `25432`.

## 🛠 Доступ к API локально

NodePort `30080` доступен только когда minikube driver=docker используется на Mac/Linux. На Windows проще через port-forward:

```powershell
$env:Path = "$env:LOCALAPPDATA\bin;$env:Path"
kubectl port-forward -n boiler svc/boiler-nginx 18080:8080
```

После этого все запросы Postman-коллекции работают по `http://localhost:18080`.

## 🔍 Что искать в каждом UI

### Kafka UI (http://`<ip>`:30808)
1. Brokers — должно показать 1 брокер `kafka:9092`.
2. Topics:
   - `telemetry-events` — все показания датчиков (нормальные + аномальные)
   - `anomaly-events` — только аномалии
   - Кликни по топику → "Messages" → "Live mode" чтобы видеть события в реальном времени.
3. Consumers:
   - `anomaly-service-group` (читает telemetry-events)
   - `notification-worker-group` (читает anomaly-events)

### OpenSearch Dashboards (http://`<ip>`:30601)
1. При первом запуске → "Explore on my own".
2. Слева → **Discover**.
3. Создать index pattern: `boiler-telemetry-*` → time field `@timestamp` → Create.
4. Готово — увидишь поток логов от всех 3 сервисов. Фильтр `Service: "boiler-telemetry-anomaly"` покажет только anomaly worker.

### Jaeger UI (http://`<ip>`:30686)
1. В выпадашке Service выбери `boiler-telemetry-api`.
2. Find Traces.
3. Кликни на трейс — увидишь полный путь запроса: API → внутренние HTTP-вызовы → ответ. Anomaly Service и Notification Worker появляются в трейсах когда обращаются к API за бойлером (через CrudApi HttpClient).

### Grafana (http://`<ip>`:30300)
1. Логин `admin`/`admin`, при входе предложит сменить пароль (можно пропустить — Skip).
2. Слева → **Connections → Data sources** — должны быть `OpenSearch-Logs` и `Jaeger-Traces`.
3. Слева → **Explore**:
   - Datasource `OpenSearch-Logs` → выбрать индекс `boiler-telemetry-*` → ввести поиск (например, `"anomaly"`)
   - Datasource `Jaeger-Traces` → query type "Search" → service `boiler-telemetry-api`

## 🔧 Полезные команды

```powershell
# Все поды
kubectl get pods -n boiler

# Логи api в реальном времени
kubectl logs -n boiler -l app.kubernetes.io/component=api --tail=20 -f

# HPA
kubectl get hpa -n boiler

# Топики Kafka
kubectl exec -n boiler deploy/kafka -- /opt/kafka/bin/kafka-topics.sh --bootstrap-server kafka:9092 --list

# Прочитать сообщения из топика (Ctrl+C для выхода)
kubectl exec -n boiler deploy/kafka -- /opt/kafka/bin/kafka-console-consumer.sh --bootstrap-server kafka:9092 --topic telemetry-events --from-beginning
```
