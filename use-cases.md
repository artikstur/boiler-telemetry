**http://localhost:18080/swagger** 

## Use Case 1 - Отправка телеметрии датчиком

**Цель:** датчик шлёт показания → API сохраняет в InfluxDB и публикует в Kafka.

### UC1.A - Основной сценарий: нормальные данные → 202

**POST `/api/v1/Telemetry`**

```json
{
  "boilerId": "<id из UC5.A>",
  "temperature": 75,
  "pressure": 8,
  "timestamp": "2026-05-10T10:00:00Z"
}
```

**Ожидаемый ответ:** `202 Accepted`, body: `{ "message": "accepted" }`.

**Где проверить:**
- **InfluxDB UI** (http://localhost:28086 → admin/adminpassword) → Data Explorer → bucket `telemetry` → measurement `boiler_readings` — должна появиться новая точка.
- **Kafka UI** (http://localhost:8085) → Topics → `telemetry-events` → Messages → увидеть сообщение с этим boilerId.

### UC1.B - Альтернативный: невалидные данные 400

POST `/api/v1/Telemetry`**

```json
{
  "boilerId": "00000000-0000-0000-0000-000000000000",
  "temperature": -50,
  "pressure": -1,
  "timestamp": "2026-05-10T10:00:00Z"
}
```

**Ожидаемый ответ:** `400 Bad Request`, body содержит массив `errors` с описанием — отрицательная температура и давление, либо несуществующий boilerId.

**Где проверить:** в OpenSearch Dashboards (http://localhost:5601) → Discover → `@l:"Warning" AND @mt:"Validation failed*"

---

## Use Case 2 - Обнаружение аномалии

**Цель:** AnomalyService читает события из Kafka, сравнивает с порогами бойлера, при превышении публикует событие аномалии.

### UC2.A - Основной сценарий: показания выше порогов, то происходит событие аномалии

```json
{
  "boilerId": "<id>",
  "temperature": 99,
  "pressure": 15,
  "timestamp": "2026-05-10T10:01:00Z"
}
```

**Ожидаемый ответ:** `202 Accepted`

**Где проверить:**
- **Kafka UI** → Topics → `anomaly-events` → Messages → должно появиться **2 сообщения** (по одному на каждое превышение: `temperature_exceeded`, `pressure_exceeded`).
- **OpenSearch Dashboards** → Discover → фильтр `@mt:"Anomaly detected*"` будет 2 строки лога:
  ```
  Anomaly detected: temperature_exceeded on boiler ..., value=99, threshold=90
  Anomaly detected: pressure_exceeded on boiler ..., value=15, threshold=12
  ```
- **Grafana** (http://localhost:3000 → admin/admin) → Dashboards → Boiler Telemetry → **Boiler Telemetry — Overview** график "Аномалий обнаружено" увеличилась на 2.
- **Prometheus** (http://localhost:9090): запрос `boiler_anomalies_detected_total` — счётчик с метками `type="temperature_exceeded"` и `type="pressure_exceeded"`.

### UC2.B Альтернативный: показания в норме → событий нет

**POST `/api/v1/Telemetry`**:

```json
{
  "boilerId": "<id>",
  "temperature": 75,
  "pressure": 8,
  "timestamp": "2026-05-10T10:02:00Z"
}
```

**Ожидаемый результат:** `202 Accepted`, в `anomaly-events` **новых сообщений НЕ появилось**, счётчик в Grafana не вырос.

---

## Use Case 3 - Отправка уведомления

**Цель:** NotificationWorker читает события аномалий из Kafka, формирует уведомление и сохраняет его в PostgreSQL.

### UC3.A - Основной сценарий

После UC2.A подожди 2-5 секунд. Затем:

```powershell
docker exec boiler-postgres psql -U postgres -d boiler_telemetry -c "SELECT id, channel, status, message, created_at FROM notifications ORDER BY created_at DESC LIMIT 5;"
```

**Ожидаемый результат:** 2 свежих строки:
```
 channel | status |                    message                    |          created_at
---------+--------+-----------------------------------------------+---------------------------
 Log     | sent   | temperature_exceeded: value=99, threshold=85  | ...
 Log     | sent   | pressure_exceeded: value=15, threshold=10     | ...
```

**Как проверить:**
- **OpenSearch Dashboards** → Discover → `Service:"boiler-telemetry-notification" AND @mt:"*notification*"` — лог самого worker'а.
- **Grafana** → Overview дашборд → "Уведомлений отправлено" увеличилось на 2.
- **Prometheus**: `boiler_notifications_sent_total{channel="Log",status="sent"}` — счётчик растёт.

---

## Use Case 4 - Получение истории показаний

**Цель:** пользователь запрашивает показания за временной интервал из InfluxDB.

### UC4.A - Основной сценарий: данные есть → 200 + массив

В Swagger найди **GET `/api/v1/Telemetry/{boilerId}`** → Try it out:

| Параметр   | Значение                            |
|------------|-------------------------------------|
| `boilerId` | id бойлера, по которому слал телеметрию |
| `from`     | `2026-05-10T00:00:00Z`              |
| `to`       | `2026-05-10T23:59:59Z`              |

**Ожидаемый ответ:** `200 OK`, body — массив объектов:
```json
[
  { "boilerId": "...", "temperature": 75, "pressure": 8, "timestamp": "..." },
  { "boilerId": "...", "temperature": 99, "pressure": 15, "timestamp": "..." }
]
```

### UC4.B - Альтернативный: некорректные параметры → 400

Тот же endpoint, но `from > to`:
| `from` | `2026-05-10T23:00:00Z` |
| `to`   | `2026-05-10T01:00:00Z` |

**Ожидаемый ответ:** `400 Bad Request`, body:
```json
{ "error": "'from' must be earlier than 'to'" }
```

### UC4.C - Альтернативный: данных за период нет → 200 + пустой массив

Создать новый бойлер через UC5.A (на него ещё не было телеметрии) и сразу запрашивай его историю:
| `from` | `2026-05-10T00:00:00Z` |
| `to`   | `2026-05-10T23:59:59Z` |

**Ожидаемый ответ:** `200 OK`, body: `[]`.

---

## Use Case 5 - Управление бойлерами (CRUD)

**Цель:** админ создаёт/обновляет/удаляет бойлеры в PostgreSQL.

### UC5.A — Создание: 201 + объект

**POST `/api/v1/Boilers`**

```json
{
  "name": "UC-Boiler-1",
  "location": "UC-Workshop",
  "temperatureThreshold": 85,
  "pressureThreshold": 10
}
```

**Ожидаемый ответ:** `201 Created`. В body — созданный объект с `id` (Guid).

**Где проверить:** `docker exec boiler-postgres psql -U postgres -d boiler_telemetry -c "SELECT * FROM boilers;"` — новая строка.

### UC5.B — Дубликат имени → 409

Повтори тот же POST с тем же `name`:

**Ожидаемый ответ:** `409 Conflict`, body:
```json
{ "errors": ["Boiler with name 'UC-Boiler-1' already exists"] }
```

### UC5.C — Невалидный body → 400

POST с пустыми/отрицательными полями:
```json
{ "name": "", "location": "", "temperatureThreshold": -1, "pressureThreshold": 0 }
```

**Ожидаемый ответ:** `400 Bad Request`, body:
```json
{ "errors": [
  "field 'name' is required",
  "field 'location' is required",
  "field 'temperature_threshold' must be > 0",
  "field 'pressure_threshold' must be > 0"
]}
```

### UC5.D - Список всех → 200 + массив

**GET `/api/v1/Boilers`** → Execute. Ответ: `200 OK`, массив объектов.

### UC5.E - Получить по id → 200 + объект

**GET `/api/v1/Boilers/{id}`** → подставь сохранённый id → Execute. Ответ: `200 OK` с объектом.

### UC5.F - Несуществующий id → 404

**GET `/api/v1/Boilers/00000000-0000-0000-0000-000000000000`** → `404 Not Found`, body:
```json
{ "error": "boiler not found" }
```

### UC5.G - Обновление → 200

**PUT `/api/v1/Boilers/{id}`** \подставить сохранённый id, в body:
```json
{ "temperatureThreshold": 90, "pressureThreshold": 12 }
```
*(в `UpdateBoilerDto` все поля nullable — можно передать только те, что меняешь)*

**Ожидаемый ответ:** `200 OK` + обновлённый объект, новые threshold'ы видны.

### UC5.H- Удаление → 200

**DELETE `/api/v1/Boilers/{id}`** → подставь id → Execute. Ответ: `200 OK`.

### UC5.I — Удаление несуществующего → 404

Тот же DELETE ещё раз — `404 Not Found`.