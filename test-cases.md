# Test Cases

Документ описывает системные тесты на уровне сценариев. Для воспроизведения вживую — готовые скрипты в локальной (не коммитится) папке `test-runs/`: на каждый кейс — `<код>.cmd`, двойной клик в Проводнике открывает консоль и прогоняет тест.

Перед прогоном — поднять кластер: `./windows/02-up.ps1` (или `./windows/04-ports.ps1`, если упали port-forward'ы).

UI после `up`: API `:18080`, Grafana `:3000`, OpenSearch `:5601`, Jaeger `:16686`, Kafka UI `:8085`, Prometheus `:9090`.

---

# Функциональные тесты

## F-01. End-to-end: телеметрия → аномалия → уведомление

**Цель:** проверить полный путь сообщения через 3 сервиса и 2 топика Kafka.
**Требование:** UC1, UC2, UC3 из README.

**Что делаем:**
1. Создаём бойлер с порогами 85 °C / 10 bar.
2. Шлём в него телеметрию 99 °C / 15 bar (оба порога превышены).
3. Запрашиваем точку из InfluxDB по `boiler_id`.
4. Смотрим записи в Postgres-таблице `notifications` для этого бойлера.

**Ожидаемый результат:**
- POST `/api/v1/telemetry` → `202 Accepted`, в ответе есть `X-Trace-Id`.
- В InfluxDB ровно одна запись с `temperature=99` и `pressure=15`.
- В Postgres две строки в `notifications`: `temperature_exceeded: value=99, threshold=85` и `pressure_exceeded: value=15, threshold=10`.

**Скрипт:** `test-runs/F-01-end-to-end.cmd`

## F-02. Валидация: невалидные запросы возвращают 400

**Цель:** API отклоняет некорректные данные на трёх endpoint'ах.

**Что делаем:**
1. POST `/boilers` с пустым именем и отрицательными порогами.
2. POST `/telemetry` с отрицательной температурой и давлением.
3. GET `/telemetry/{id}?from=...&to=...`, где `from > to`.

**Ожидаемый результат:** все три запроса возвращают `400 Bad Request`.

**Скрипт:** `test-runs/F-02-validation.cmd`

## F-03. Дубликат бойлера — 409 Conflict

**Цель:** уникальное имя бойлера защищено на уровне API.

**Что делаем:**
1. Создаём бойлер с именем `F03-Duplicate-*`.
2. Пытаемся создать ещё одного с тем же именем.

**Ожидаемый результат:** второй запрос возвращает `409 Conflict`, тело — `Boiler with name '...' already exists`.

**Скрипт:** `test-runs/F-03-duplicate.cmd`

---

# Отказоустойчивость

## R-01. Падение реплики приложения — система остаётся доступной

**Цель:** kill одного из подов API не ломает обслуживание.
**Требование:** при отказе одного экземпляра восстановление обработки ≤ 30 секунд.

**Что делаем:**
1. Смотрим, что API имеет 2 реплики (`kubectl get pods -l app.kubernetes.io/component=api`).
2. Делаем `kubectl delete pod ... --grace-period=0 --force` по одной из них.
3. Сразу шлём 20 запросов на `/health` шаг каждые 200 мс.
4. Ждём 30 секунд и проверяем число реплик в статусе `Running`.

**Ожидаемый результат:**
- Все 20 health-чеков успешны (`ok=20, fail=0`). PDB + вторая реплика держат трафик.
- Через ~30 секунд `Running`-реплик снова ≥ 2 — Deployment пересоздал убитую.

**Скрипт:** `test-runs/R-01-pod-kill.cmd`

## R-02. Падение Postgres — авто-восстановление, данные на месте

**Цель:** убитый Postgres-контейнер поднимается, данные не теряются (volume), приложения переподключаются.

**Что делаем:**
1. Создаём «маркер»-бойлер, запоминаем его id и общее число бойлеров `before`.
2. `docker kill boiler-postgres`.
3. Ждём, пока контейнер вернётся (`restart: unless-stopped`).
4. Ждём healthcheck `healthy`.
5. Считаем `after` через `/api/v1/boilers` и пробуем GET по сохранённому id.

**Ожидаемый результат:**
- Контейнер возвращается в `Up (healthy)`.
- `before == after` — данные на диске (volume) не потеряны.
- GET `/boilers/{id}` отдаёт бойлера (200).
- В Grafana → **Database Health** → панель «Postgres up» проваливается в `DOWN` на ~10–60 сек и возвращается в `UP`.

**Скрипт:** `test-runs/R-02-postgres-kill.cmd`

## R-03. Бэкап → drop table → восстановление

**Цель:** бэкап-контейнер реально пишет дампы, восстановление из последнего дампа возвращает данные.

**Что делаем:**
1. Создаём маркер-бойлер `R03-*`.
2. Запускаем разовый бэкап через `boiler-postgres-backup` (`/backup.sh`), смотрим `/backups/last/`.
3. Дропаем таблицу `boilers` (`DROP TABLE boilers CASCADE`).
4. Проверяем, что API теперь отдаёт `500` на `/boilers` (таблицы нет).
5. Чистим схему и заливаем последний дамп.
6. Делаем `kubectl rollout restart` для `boiler-api` и `boiler-notification-worker`, ждём `rollout status`.
7. Запрашиваем `/boilers`, ищем имя `R03-*`.

**Ожидаемый результат:**
- После drop API возвращает 5xx.
- После restore + restart деплоев API снова отдаёт 200, маркер-бойлер на месте.

**Скрипт:** `test-runs/R-03-backup-restore.cmd`

## R-04. Падение брокера Kafka — кластер продолжает работать

**Цель:** при отказе одного из трёх брокеров Kafka прдьюсеры/консьюмероы не теряют сообщения, ISR схлопывается с 3 до 2 и потом восстанавливается.
**Требование:** «Kafka должна быть развернута с replication factor больше единицы», «Потеря телеметрических данных недопустима» (README, нефункциональные, п.3).

**Конфигурация под этот тест:**
- Kafka развёрнута как StatefulSet из 3 нод (`kafka-0`, `kafka-1`, `kafka-2`), KRaft, headless service `kafka-headless`, отдельный PVC на каждую ноду.
- Топики `telemetry-events` и `anomaly-events`: `partitions=3`, `replication-factor=3`, `min.insync.replicas=2`.
- Producer'ы в API и AnomalyService: `Acks=All`, `EnableIdempotence=true`.
- PDB `minAvailable=2` — k8s не даст добровольно отключить более одной ноды.

**Что делаем:**
1. Видим, что StatefulSet `kafka` имеет 3/3 Ready реплики.
2. Через `kafka-topics --describe` смотрим, что у `telemetry-events` Replicas=3 и Isr=3 на каждом partition.
3. Шлём контрольную телеметрию и убеждаемся, что HTTP 202.
4. `kubectl delete pod kafka-0 --grace-period=0 --force` — убиваем один брокер.
5. Сразу шлём 20 телеметрий — все должны быть приняты.
6. Через `kafka-topics --describe` (с уцелевшего `kafka-1`) смотрим, что ISR стал 2 элемента для тех partition, где `kafka-0` был лидером.
7. Ждём 45 секунд — StatefulSet поднимает `kafka-0` заново; ISR возвращается к 3.
8. Шлём контрольную телеметрию — снова 202.
9. Проверяем в Postgres-таблице `notifications`, что для маркер-бойлера накопилось ≥ 20 уведомлений — данные не потерялись.

**Ожидаемый результат:**
- Все 20 запросов во время падения приняты (`ok=20, fail=0`).
- В описании топика во время падения Isr=2/3, после восстановления Isr=3/3.
- Кластер `kafka` снова 3/3 Ready через ~45 секунд.
- В Postgres ≥ 20 уведомлений по маркер-бойлеру (потеря данных не допущена).
- В Kafka UI (`http://localhost:8085`) во время падения видно, что у двух нод статус ONLINE, у одной — DOWN; partition'ы перебалансируются.

**Скрипт:** `test-runs/R-04-kafka-broker-kill.cmd`

---

# Масштабируемость

## S-01. HPA добавляет реплики под нагрузкой

**Цель:** HorizontalPodAutoscaler срабатывает при росте CPU.
**Требование:** «CRUD Service, Anomaly Detection Service и Notification Worker должны поддерживать горизонтальное масштабирование путём увеличения числа реплик» (README, нефункциональные, п.2).

**Что делаем:**
1. Смотрим стартовый HPA и число подов API.
2. Запускаем 10 параллельных PowerShell-job'ов, каждый 3 минуты стучится в ручку `/api/v1/boilers`.
3. Каждые 30 секунд (6 проходов) смотрим HPA + число подов.
4. Считаем суммарное число прошедших запросов.

**Ожидаемый результат:**
- В первые 30–90 секунд CPU API растёт.
- HPA скейлит до 4–6 реплик (потолок `maxReplicas=6`).
- После окончания нагрузки число реплик уменьшается не сразу (HPA cooldown ~5 минут).

**Скрипт:** `test-runs/S-01-hpa-load.cmd`

## S-02. Ручное масштабирование без downtime

**Цель:** `kubectl scale` добавляет/убирает реплики без потери трафика.

**Что делаем:**
1. Запускаем фоновый job — 120 health-чеков с шагом 500 мс (≈60 секунд).
2. Параллельно `kubectl scale boiler-api --replicas=5`, ждём 20 секунд.
3. Возвращаем `--replicas=2`, ждём ещё 20 секунд.
4. Дожидаемся завершения job'а и считаем `ok / fail`.

**Ожидаемый результат:** `fail = 0`.

**Скрипт:** `test-runs/S-02-manual-scale.cmd`

---

# Observability — логи, метрики, трейсы

## O-01. Trace одного запроса через 3 сервиса

**Цель:** один HTTP-запрос порождает связный trace через цепочку api → kafka → anomaly → kafka → notification, видимый в Jaeger.

**Что делаем:**
1. Создаём бойлер.
2. Шлём аномальную телеметрию, ловим `X-Trace-Id` из ответа.
3. Через 7 секунд запрашиваем трейс из Jaeger API: `GET /api/traces/{trace-id}`.
4. Считаем количество span'ов и уникальных `serviceName` в `processes`.
5. У одного span смотрим тэги — должен быть `k8s.pod.name`; у Kafka-спанов — `messaging.kafka.partition` и `messaging.kafka.offset`.

**Ожидаемый результат:**
- ≥ 8 span'ов в трейсе.
- 3 сервиса: `boiler-telemetry-api`, `boiler-telemetry-anomaly`, `boiler-telemetry-notification`.
- В Process tags каждого span есть `k8s.pod.name`; в Kafka-спанах виден partition/offset.
- В Jaeger UI (`http://localhost:16686/trace/<id>`) — связное дерево спанов.

**Скрипт:** `test-runs/O-01-trace.cmd`

## O-02. Логи в OpenSearch связаны с trace по TraceId

**Цель:** все логи централизованы и связаны с трейсами через `fields.TraceId`.

**Что делаем:**
1. Делаем тот же сценарий, что в O-01 — получаем `X-Trace-Id`.
2. Через OpenSearch Console Proxy ищем в индексе `boiler-telemetry-*` логи с `fields.TraceId.keyword == <id>`.
3. Группируем найденные логи по `(fields.Service, fields.Pod)` — видно, из какой реплики какого сервиса пришла запись.

**Ожидаемый результат:**
- Найдено ≥ 20 логов через все 3 сервиса.
- В каждой записи есть `fields.TraceId`, `fields.SpanId`, `fields.Pod`, `fields.Service`.
- В OpenSearch Dashboards → Discover работает фильтр `fields.TraceId:"<id>"`.
- В Grafana → Explore → datasource Jaeger → у span'а есть кнопка «Logs for this span», которая открывает OpenSearch с тем же фильтром.

**Скрипт:** `test-runs/O-02-logs.cmd`

## O-03. Метрики в Prometheus + дашборды

**Цель:** счётчики аномалий растут на каждом проходе, Prometheus собирает все targets.

**Что делаем:**
1. Снимаем `sum(boiler_anomalies_detected_total)` — назовём `before`.
2. Шлём 5 аномальных запросов на `/telemetry` (каждый порождает 2 аномалии).
3. Ждём 15 секунд (scrape interval) и снова читаем сумму — `after`.
4. Запрашиваем `/api/v1/targets?state=active`, группируем по `scrapePool`, смотрим `up/total`.

**Ожидаемый результат:**
- `after - before == 10` (5 запросов × 2 типа аномалии: temperature + pressure).
- Все scrape pool'ы `up/total`: `boiler-pods 6/6`, `prometheus 1/1`, `postgres-exporter 1/1`, `influxdb 1/1`.
- В Grafana → **Boiler Telemetry — Overview** → панель «Аномалий обнаружено» увеличилась на 10.

**Скрипт:** `test-runs/O-03-metrics.cmd`
