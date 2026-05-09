# UI и доступы

## ⚡ Быстрый старт

Запусти один скрипт — он поднимет port-forward'ы на все UI:

```powershell
.\scripts\04-port-forward.ps1
```

После этого открывай в браузере:

| Сервис                     | URL                       | Креды                                | Что внутри                                                |
|----------------------------|---------------------------|--------------------------------------|------------------------------------------------------------|
| **API (через nginx)**      | http://localhost:18080    | —                                    | `/health`, `/api/v1/boilers`, `/api/v1/telemetry`          |
| **OpenSearch Dashboards**  | http://localhost:5601     | без авторизации                      | Логи всех сервисов                                         |
| **Grafana**                | http://localhost:3000     | `admin` / `admin`                    | Логи (через OpenSearch datasource) и трейсы (Jaeger)       |
| **Jaeger UI**              | http://localhost:16686    | без авторизации                      | Распределённые трейсы запросов                             |
| **Kafka UI**               | http://localhost:8085     | без авторизации                      | Топики, сообщения, группы консьюмеров                      |
| **Prometheus**             | http://localhost:9090     | без авторизации                      | Метрики приложений и инфраструктуры                        |
| **InfluxDB UI** (на хосте) | http://localhost:28086    | `admin` / `adminpassword`            | Точки телеметрии, токен `dev-token`                        |
| **PostgreSQL** (на хосте)  | `localhost:25432`         | `postgres` / `postgres` / `boiler_telemetry` | Таблицы `boilers`, `notifications`                  |

> ⚠️ **Важно:** на Windows с minikube driver=docker, NodePort'ы НЕ доступны по `minikube ip` — это особенность Docker Desktop. Только через port-forward (что и делает скрипт выше). Не трать время на попытки открыть `http://192.168.49.2:30601` из браузера — не получится.

## 📊 OpenSearch Dashboards: первая настройка

При первом открытии http://localhost:5601 → Discover будет говорить "Create index pattern". **Это нужно сделать один раз руками** (через API не работает из-за изоляции workspace в 2.x):

1. Слева **☰ → Stack Management → Index patterns** (или прямо в Discover нажми **Create index pattern**)
2. Index pattern name: `boiler-telemetry-*` → **Next step**
3. Time field: `@timestamp` → **Create index pattern**
4. Слева **☰ → Discover** — теперь видны логи

В правом верхнем углу подкрути диапазон времени — по умолчанию там **Last 15 minutes**, ставь **Last 1 hour** или **Last 24 hours**.

Полезные фильтры в строке поиска (KQL):
- `Service: "boiler-telemetry-api"` — только API
- `Service: "boiler-telemetry-anomaly"` — только anomaly worker
- `@l: "Error"` — только ошибки
- `@mt: "Anomaly detected*"` — только события обнаружения аномалий

## 📈 Grafana

http://localhost:3000 → `admin` / `admin` (если не пускает — `.\scripts\05-reset-grafana-admin.ps1`).

**Готовый дашборд:** слева **☰ → Dashboards → Boiler Telemetry → Boiler Telemetry — Overview**.

12 панелей:
- RPS API, Аномалий обнаружено, Уведомлений отправлено, Подов в кластере (стат-карточки)
- API: запросы по статус-кодам (200/400/500)
- API: latency p50/p95/p99
- Аномалии по типу (`temperature_exceeded` / `pressure_exceeded`)
- Telemetry events обработано
- CPU usage по подам
- Memory по подам
- Последние логи (все сервисы) — datasource OpenSearch
- Только аномалии — фильтр `@mt:"Anomaly detected*"`

**3 datasource** настроены автоматически:
- **Prometheus** — метрики (default)
- **OpenSearch-Logs** — логи
- **Jaeger-Traces** — трейсы

В **Explore** можешь крутить любые ad-hoc запросы:
- PromQL: `rate(boiler_anomalies_detected_total[1m])`
- OpenSearch: `Service: "boiler-telemetry-api" AND @l: "Error"`
- Jaeger: service `boiler-telemetry-api` → Run

## 📊 Prometheus

http://localhost:9090

**Status → Targets** — должно быть 7 endpoints в состоянии **UP** (2× api, 2× anomaly, 2× notification, prometheus self).

Полезные запросы (вкладка Graph):
- `sum(rate(http_requests_received_total[1m]))` — RPS API
- `sum(boiler_anomalies_detected_total)` — всего аномалий
- `sum by (type) (boiler_anomalies_detected_total)` — по типу
- `histogram_quantile(0.95, sum by (le) (rate(http_request_duration_seconds_bucket[5m])))` — p95 latency
- `process_working_set_bytes{job="boiler-pods"}` — память по подам

## 🔍 Jaeger UI

http://localhost:16686

1. В выпадашке **Service** выбери `boiler-telemetry-api`
2. **Find Traces**
3. Кликни на трейс — увидишь полный путь запроса с временами

Сервисы в Jaeger:
- `boiler-telemetry-api` — входящие HTTP запросы + исходящие в Postgres/Influx/Kafka
- `boiler-telemetry-anomaly` — HTTP-вызовы AnomalyService → API за бойлером
- `boiler-telemetry-notification` — HTTP-вызовы NotificationWorker

## 🟪 Kafka UI

http://localhost:8085

1. **Brokers** — должно показать 1 брокер `kafka:9092`.
2. **Topics**:
   - `telemetry-events` — все показания датчиков (~3 partition'а)
   - `anomaly-events` — только аномалии
3. Кликни по топику → **Messages** → **Live mode** для real-time просмотра.
4. **Consumers**:
   - `anomaly-service-group` (потребляет `telemetry-events`)
   - `notification-worker-group` (потребляет `anomaly-events`)

## 💾 Прямой доступ к БД

PostgreSQL — через `psql` в контейнере:
```powershell
docker exec -it boiler-postgres psql -U postgres -d boiler_telemetry

# В psql:
\dt                                         -- список таблиц
SELECT * FROM boilers;
SELECT * FROM notifications ORDER BY created_at DESC LIMIT 20;
\q
```

Или GUI-клиентом (DBeaver, pgAdmin) — host `localhost`, port `25432`, user `postgres`, pass `postgres`, db `boiler_telemetry`.

InfluxDB — через UI (`http://localhost:28086`):
1. Login `admin` / `adminpassword`
2. **Data Explorer** → bucket `telemetry` → measurement `boiler_readings`
3. Submit — увидишь графики temperature/pressure

## 🔧 Полезные команды

```powershell
# Все поды
kubectl get pods -n boiler

# Логи API в реальном времени
kubectl logs -n boiler -l app.kubernetes.io/component=api --tail=20 -f

# HPA (показывает текущую нагрузку CPU)
kubectl get hpa -n boiler

# Топики Kafka из консоли
kubectl exec -n boiler deploy/kafka -- /opt/kafka/bin/kafka-topics.sh --bootstrap-server kafka:9092 --list

# Прочитать сообщения из топика (Ctrl+C для выхода)
kubectl exec -n boiler deploy/kafka -- /opt/kafka/bin/kafka-console-consumer.sh --bootstrap-server kafka:9092 --topic telemetry-events --from-beginning --max-messages 10

# Проверка trip Postgres → API
docker exec boiler-postgres psql -U postgres -d boiler_telemetry -c "SELECT count(*) FROM notifications;"

# Прибить все port-forward'ы
Get-Process kubectl | Stop-Process -Force
```

## 🧪 Прогон сценария end-to-end

1. Запусти port-forward'ы: `.\scripts\04-port-forward.ps1`
2. Импортируй в Postman: **`boiler-telemetry.postman_collection.json`**
3. Прогоняй запросы 1→9 по порядку:
   - 1 — Health
   - 2 — Create boiler (сохранит `boilerId` в переменную коллекции)
   - 4 — нормальная телеметрия
   - 5 — **аномальная телеметрия** (95C/12bar)
4. Открой Kafka UI → топик `anomaly-events` → Messages — увидишь новое сообщение
5. Открой OpenSearch Dashboards → Discover — увидишь логи `Anomaly detected`
6. Проверь PostgreSQL:
   ```powershell
   docker exec boiler-postgres psql -U postgres -d boiler_telemetry -c "SELECT * FROM notifications ORDER BY created_at DESC LIMIT 5;"
   ```
7. Открой Jaeger UI → service `boiler-telemetry-api` → Find Traces → выбери последний и посмотри как запрос растёкся.
