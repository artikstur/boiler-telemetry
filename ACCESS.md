# UI и доступы

После `make up` (или `make ports`, если port-forward'ы упали) всё открывается на localhost.

> ⚠️ minikube driver=docker не пускает в NodePort'ы из браузера напрямую — поэтому port-forward'ы. Работает одинаково на Linux/Mac/Windows.

## 🌐 Веб-интерфейсы

| Сервис                     | URL                       | Креды                     | Что внутри                                            |
|----------------------------|---------------------------|---------------------------|--------------------------------------------------------|
| **API (через nginx)**      | http://localhost:18080    | —                         | `/health`, `/api/v1/boilers`, `/api/v1/telemetry`      |
| **Grafana**                | http://localhost:3000     | `admin` / `admin`         | Метрики, логи, трейсы. Готовый дашборд внутри          |
| **OpenSearch Dashboards**  | http://localhost:5601     | —                         | Логи всех сервисов (`boiler-telemetry-*`)              |
| **Jaeger UI**              | http://localhost:16686    | —                         | Распределённые трейсы запросов                         |
| **Kafka UI**               | http://localhost:8085     | —                         | Топики, сообщения, группы консьюмеров                  |
| **Prometheus**             | http://localhost:9090     | —                         | Сырые метрики, targets                                 |
| **InfluxDB UI** (на хосте) | http://localhost:28086    | `admin` / `adminpassword` | Точки телеметрии, токен `dev-token`                    |
| **PostgreSQL** (на хосте)  | `localhost:25432`         | `postgres` / `postgres`   | DB `boiler_telemetry`, таблицы `boilers`, `notifications` |

## 📈 Grafana

http://localhost:3000 → `admin` / `admin`. Если не пускает — `make reset-grafana`.

**Готовый дашборд:** ☰ → **Dashboards → Boiler Telemetry → Boiler Telemetry — Overview** (12 панелей):
- RPS API, Аномалий обнаружено, Уведомлений отправлено, Подов в кластере (стат-карточки)
- Запросы по статус-кодам, latency p50/p95/p99
- Аномалии по типу, throughput телеметрии
- CPU и Memory по подам
- Поток логов из OpenSearch + фильтр только аномалий

**3 datasource** настроены автоматически: `Prometheus` (default), `OpenSearch-Logs`, `Jaeger-Traces`.

В **Explore** можно крутить ad-hoc запросы:
- PromQL: `rate(boiler_anomalies_detected_total[1m])`
- OpenSearch: `Service:"boiler-telemetry-api" AND @l:"Error"`
- Jaeger: service `boiler-telemetry-api` → Run

## 📊 Prometheus

http://localhost:9090

**Status → Targets** — должно быть 7 endpoints `UP` (2× api, 2× anomaly, 2× notification, prometheus self).

Полезные запросы во вкладке Graph:
- `sum(rate(http_requests_received_total[1m]))` — RPS API
- `sum(boiler_anomalies_detected_total)` — всего аномалий
- `sum by (type) (boiler_anomalies_detected_total)` — по типу
- `histogram_quantile(0.95, sum by (le) (rate(http_request_duration_seconds_bucket[5m])))` — p95 latency
- `process_working_set_bytes{job="boiler-pods"}` — память по подам

## 📝 OpenSearch Dashboards

http://localhost:5601

При первом открытии **Discover** будет говорить "Create index pattern" — настройка одноразовая:

1. ☰ → **Stack Management → Index patterns** → **Create index pattern**
2. Pattern: `boiler-telemetry-*` → Next
3. Time field: `@timestamp` → **Create**
4. Иди в **Discover**, в правом верхнем углу подкрути диапазон на **Last 1 hour**

Полезные KQL-фильтры:
- `Service:"boiler-telemetry-api"` — только API
- `Service:"boiler-telemetry-anomaly"` — только anomaly worker
- `@l:"Error"` — только ошибки
- `@mt:"Anomaly detected*"` — события обнаружения аномалий

## 🔍 Jaeger UI

http://localhost:16686 → выбираешь Service → Find Traces → кликаешь на трейс — видишь полный путь запроса с временами по спанам.

Сервисы: `boiler-telemetry-api`, `boiler-telemetry-anomaly`, `boiler-telemetry-notification`.

## 🟪 Kafka UI

http://localhost:8085

- **Brokers** — 1 брокер `kafka:9092`
- **Topics** → `telemetry-events` (3 partition'а), `anomaly-events` → клик → **Messages** → **Live mode** для real-time
- **Consumers** → `anomaly-service-group`, `notification-worker-group`

## 💾 Бэкапы и восстановление

Бэкапы делаются автоматически в `infra/databases/backups/`:
- **Postgres** — каждый час через `prodrigestivill/postgres-backup-local`. Ротация: последние 7 дней + 4 недели + 6 месяцев. Файлы вида `boiler_telemetry-YYYYMMDD-HHMMSS.sql.gz`.
- **InfluxDB** — каждый час `influx backup` пишет в подкаталог `YYYY-MM-DD-HH-MM/`. Ротация: последние 24 копии.

```bash
make backup-now         # принудительный бэкап обеих БД
make list-backups       # показать что есть

# Восстановить Postgres из конкретного файла:
make restore-postgres FILE=last/boiler_telemetry-latest.sql.gz
# (путь относительно infra/databases/backups/postgres/)
```

Дашборд в Grafana **Database Health** (`☰ → Dashboards → Boiler Telemetry → Database Health`) показывает:
- Postgres up / InfluxDB up (DOWN→красный)
- Размер БД, открытые соединения
- TPS (commits/rollbacks/sec)
- DML rate (inserted/updated/deleted)
- InfluxDB API requests, write errors

## 💾 Прямой доступ к БД

```bash
# Postgres через psql в контейнере
docker exec -it boiler-postgres psql -U postgres -d boiler_telemetry
\dt
SELECT * FROM boilers;
SELECT * FROM notifications ORDER BY created_at DESC LIMIT 20;
\q
```

GUI (DBeaver/pgAdmin): host `localhost`, port `25432`, user `postgres`, pass `postgres`, db `boiler_telemetry`.

InfluxDB: открой http://localhost:28086, логинься `admin`/`adminpassword`, **Data Explorer** → bucket `telemetry` → measurement `boiler_readings`.

## 🛠 Полезные команды

```bash
make status                       # поды, реплики, HPA
make logs SVC=api                 # tail -f логов сервиса
make logs SVC=anomaly-service
make logs SVC=notification-worker

# Сырые kubectl
kubectl get pods -n boiler
kubectl get hpa -n boiler

# Топики Kafka
kubectl exec -n boiler deploy/kafka -- /opt/kafka/bin/kafka-topics.sh --bootstrap-server kafka:9092 --list

# Чтение топика
kubectl exec -n boiler deploy/kafka -- /opt/kafka/bin/kafka-console-consumer.sh --bootstrap-server kafka:9092 --topic telemetry-events --from-beginning --max-messages 10
```

## 🧪 Прогон сценария

1. `make up` (если ещё не запущено)
2. Импортируй в Postman: `boiler-telemetry.postman_collection.json`
3. Выполни запросы 1→9 по порядку:
   - 1 — Health
   - 2 — Create boiler (сохранит `boilerId`)
   - 4 — нормальная телеметрия (75°C / 8 bar)
   - 5 — **аномальная телеметрия** (95°C / 12 bar)
4. Проверь:
   - Grafana дашборд → Аномалий обнаружено и Уведомлений отправлено выросли
   - Kafka UI → топик `anomaly-events` → новое сообщение
   - OpenSearch Dashboards → Discover → `@mt:"Anomaly detected*"`
   - Postgres: `docker exec boiler-postgres psql -U postgres -d boiler_telemetry -c "SELECT * FROM notifications ORDER BY created_at DESC LIMIT 5;"`
   - Jaeger UI → service `boiler-telemetry-api` → Find Traces
