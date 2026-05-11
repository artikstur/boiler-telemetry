# Test Cases

## Подготовка для всех тестов

В **PowerShell** в корне проекта:

```powershell
Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass
$env:Path = "$env:LOCALAPPDATA\bin;$env:Path"

# Если кластер ещё не поднят:
.\windows\02-up.ps1

# Если port-forward'ы отвалились:
.\windows\04-ports.ps1
```

После этого UI доступны на: API `:18080`, Grafana `:3000`, OpenSearch `:5601`, Jaeger `:16686`, Kafka UI `:8085`, Prometheus `:9090`.

---

# Функциональные тесты

## F-01. End-to-end: телеметрия → аномалия → уведомление

**Цель:** проверить полный путь сообщения через 3 сервиса и 2 топика Kafka.

**Требование:** UC1, UC2, UC3 из README.

**Сценарий:**

```powershell
$base = 'http://localhost:18080'

# 1. Создаём бойлер с порогами 85°C / 10 bar
$b = @{ name="F01-$(Get-Random)"; location='Цех-1'; temperatureThreshold=85; pressureThreshold=10 } | ConvertTo-Json
$boiler = Invoke-RestMethod "$base/api/v1/boilers" -Method POST -Body $b -ContentType 'application/json'
$id = $boiler.id
Write-Host "Создан бойлер: $id"

# 2. Шлём аномальную телеметрию (99°C / 15 bar — оба порога превышены)
$t = @{ boilerId=$id; temperature=99; pressure=15; timestamp=(Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ss.fffZ') } | ConvertTo-Json
$resp = Invoke-WebRequest "$base/api/v1/telemetry" -Method POST -Body $t -ContentType 'application/json' -UseBasicParsing
Write-Host "Telemetry status: $($resp.StatusCode), trace: $($resp.Headers['X-Trace-Id'])"

Start-Sleep 6

# 3. Проверяем что в InfluxDB лежит точка (через HTTP API, чтобы обойти ад экранирования в docker exec на Windows)
$flux = "from(bucket:`"telemetry`") |> range(start: -5m) |> filter(fn: (r) => r.boilerId == `"$id`")"
Invoke-RestMethod 'http://localhost:28086/api/v2/query?org=boiler-org' -Method POST `
    -Headers @{ Authorization='Token dev-token'; 'Content-Type'='application/vnd.flux'; Accept='application/csv' } `
    -Body $flux

# 4. Проверяем что в Postgres легло 2 уведомления (temperature + pressure)
docker exec boiler-postgres psql -U postgres -d boiler_telemetry -c "SELECT message, created_at FROM notifications WHERE boiler_id='$id';"
```

**Ожидаемый результат:**
- POST `/api/v1/telemetry` → `202 Accepted`
- InfluxDB: 1 запись с `temperature=99, pressure=15`
- Postgres: 2 строки в `notifications` — `temperature_exceeded: value=99, threshold=85` и `pressure_exceeded: value=15, threshold=10`

## F-02. Валидация: невалидные запросы возвращают 400

**Цель:** API отклоняет некорректные данные на трёх endpoint'ах.

**Сценарий:**

```powershell
$base = 'http://localhost:18080'

# Бойлер с пустым именем и отрицательными порогами
$bad1 = @{ name=''; location=''; temperatureThreshold=-1; pressureThreshold=0 } | ConvertTo-Json
try { Invoke-RestMethod "$base/api/v1/boilers" -Method POST -Body $bad1 -ContentType 'application/json' }
catch { Write-Host "POST /boilers (пустой): $([int]$_.Exception.Response.StatusCode)" }

# Телеметрия с отрицательной температурой
$bad2 = @{ boilerId='00000000-0000-0000-0000-000000000000'; temperature=-50; pressure=-1; timestamp=(Get-Date).ToString('o') } | ConvertTo-Json
try { Invoke-RestMethod "$base/api/v1/telemetry" -Method POST -Body $bad2 -ContentType 'application/json' }
catch { Write-Host "POST /telemetry (отриц): $([int]$_.Exception.Response.StatusCode)" }

# История с from > to 
$boilers = @(Invoke-RestMethod "$base/api/v1/boilers")
if ($boilers.Count -eq 0) {
    $tmp = Invoke-RestMethod "$base/api/v1/boilers" -Method POST `
        -Body (@{name="F02-tmp";location='X';temperatureThreshold=85;pressureThreshold=10}|ConvertTo-Json) `
        -ContentType 'application/json'
    $id = $tmp.id
} else {
    $id = $boilers[0].id
}
$from = [uri]::EscapeDataString('2026-12-31T00:00:00Z')
$to   = [uri]::EscapeDataString('2026-01-01T00:00:00Z')
try { Invoke-RestMethod "$base/api/v1/telemetry/$id`?from=$from&to=$to" }
catch { Write-Host "GET /telemetry (from>to): $([int]$_.Exception.Response.StatusCode)" }
```

**Ожидаемый результат:** все три выводят `400`.

## F-03. Дубликат бойлера — 409 Conflict

```powershell
$base = 'http://localhost:18080'
$body = @{ name="F03-Duplicate"; location='X'; temperatureThreshold=85; pressureThreshold=10 } | ConvertTo-Json
Invoke-RestMethod "$base/api/v1/boilers" -Method POST -Body $body -ContentType 'application/json' | Out-Null

try { Invoke-RestMethod "$base/api/v1/boilers" -Method POST -Body $body -ContentType 'application/json' }
catch {
    Write-Host "Status: $([int]$_.Exception.Response.StatusCode)"
    Write-Host "Body:   $($_.ErrorDetails.Message)"
}
```

**Ожидаемый результат:** `409 Conflict`, body содержит `Boiler with name 'F03-Duplicate' already exists`.

---

# Отказоустойчивость

## R-01. Падение реплики приложения — система остаётся доступной

**Цель:** при kill одного из подов API запросы продолжают проходить через вторую реплику.

**Требование:** При отказе одного экземпляра сервиса восстановление обработки должно происходить в течение 30 секунд.

**Сценарий:**

```powershell
$base = 'http://localhost:18080'

# 1. Видим что есть 2 реплики API
kubectl get pods -n boiler -l app.kubernetes.io/component=api

# 2. Прибиваем один pod
$pod1 = (kubectl get pods -n boiler -l app.kubernetes.io/component=api -o jsonpath='{.items[0].metadata.name}')
Write-Host "Убиваю $pod1..."
kubectl delete pod -n boiler $pod1 --grace-period=0 --force

# 3. Сразу шлём 20 запросов на /health
$ok = 0; $fail = 0
1..20 | ForEach-Object {
    try { Invoke-RestMethod "$base/health" -TimeoutSec 3 | Out-Null; $ok++ }
    catch { $fail++ }
    Start-Sleep -Milliseconds 200
}
Write-Host "Health-чеков: $ok успешных / $fail упавших"

# 4. Через 30 сек проверяем что кубер поднял замену
Start-Sleep 30
kubectl get pods -n boiler -l app.kubernetes.io/component=api
```

**Ожидаемый результат:**
- `ok = 20, fail = 0` — ни один запрос не упал (PodDisruptionBudget + 2 реплики → вторая обработала всё).
- Через ~30 сек снова **2 реплики Running** (Deployment пересоздал убитый pod).

## R-02. Падение Postgres контейнера — авто-восстановление + данные на месте

**Цель:** при отключении Postgres-контейнера Docker рестартует его (`restart: unless-stopped`), данные сохраняются (volume), приложения переподключаются.

**Сценарий:**

```powershell
# 1. Создадим бойлер, чтобы было что проверить
$id = (Invoke-RestMethod 'http://localhost:18080/api/v1/boilers' -Method POST `
       -Body (@{name="R02-$(Get-Random)";location='X';temperatureThreshold=85;pressureThreshold=10}|ConvertTo-Json) `
       -ContentType 'application/json').id
Write-Host "Создан бойлер: $id"

# 2. Считаем сколько бойлеров есть до краша
$before = @(Invoke-RestMethod 'http://localhost:18080/api/v1/boilers').Count
Write-Host "Бойлеров до падения: $before"

# 3. Убиваем Postgres
Write-Host "Убиваю Postgres..."
docker kill boiler-postgres

# 4. Проверяем что докер сам его поднял
Start-Sleep 15
docker ps --filter "name=boiler-postgres" --format "{{.Names}} -> {{.Status}}"

# 5. Ждём пока станет healthy
$deadline = (Get-Date).AddSeconds(60)
while ((Get-Date) -lt $deadline) {
    $s = docker inspect --format '{{.State.Health.Status}}' boiler-postgres 2>$null
    Write-Host "  postgres: $s"
    if ($s -eq 'healthy') { break }
    Start-Sleep 3
}

# 6. Тот же запрос — данные должны быть на месте
$after = @(Invoke-RestMethod 'http://localhost:18080/api/v1/boilers').Count
Write-Host "Бойлеров после восстановления: $after"
Invoke-RestMethod "http://localhost:18080/api/v1/boilers/$id"
```

**Ожидаемый результат:**
- После `docker kill` контейнер автоматически рестартует, через ~15 сек снова `Up (healthy)`.
- `before == after` — данные не потеряны (Docker volume).
- GET по `id` возвращает 200 (бойлер на месте).
- В Grafana → **Database Health** → "Postgres up" на ~10 сек проваливается в `DOWN`, потом снова `UP`.

## R-03. Бэкап → удаление таблицы → восстановление

**Цель:** проверить что бэкапы Postgres работают и восстановление возможно.

**Сценарий:**

```powershell
# 1. Создадим бойлер с маркером
$body = @{name="R03-$(Get-Random)";location='X';temperatureThreshold=85;pressureThreshold=10}|ConvertTo-Json
Invoke-RestMethod 'http://localhost:18080/api/v1/boilers' -Method POST -Body $body -ContentType 'application/json' | Out-Null

# 2. Делаем бэкап прямо сейчас
docker exec boiler-postgres-backup /backup.sh
docker exec boiler-postgres-backup ls -la /backups/last/

# 3. Дропаем таблицу 
docker exec boiler-postgres psql -U postgres -d boiler_telemetry -c "DROP TABLE boilers CASCADE;"

# 4. Проверяем что API сломался (500)
try { Invoke-RestMethod 'http://localhost:18080/api/v1/boilers' }
catch { Write-Host "API status after drop: $([int]$_.Exception.Response.StatusCode)" }

# 5. Восстанавливаем из последнего бэкапа
docker exec boiler-postgres psql -U postgres -d boiler_telemetry -c "DROP SCHEMA public CASCADE; CREATE SCHEMA public;"
docker exec -e PGPASSWORD=postgres boiler-postgres-backup sh -c "gunzip -c /backups/last/boiler_telemetry-latest.sql.gz | psql -h postgres -U postgres -d boiler_telemetry"

# 6. Рестартуем приложения чтобы EF Core переподключился к свежей схеме
kubectl rollout restart deploy/boiler-api deploy/boiler-notification-worker -n boiler
kubectl -n boiler rollout status deploy/boiler-api --timeout=3m
kubectl -n boiler rollout status deploy/boiler-notification-worker --timeout=3m

# 7. Проверяем что бойлер вернулся
@(Invoke-RestMethod 'http://localhost:18080/api/v1/boilers') | Where-Object { $_.name -like 'R03-*' }
```

**Ожидаемый результат:**
- После dropa API возвращает 500 (таблицы нет).
- После restore — все бойлеры из бэкапа восстановлены, API снова отвечает 200.

---

# Масштабируемость

## S-01. HPA добавляет реплики под нагрузкой

**Цель:** HorizontalPodAutoscaler срабатывает при росте CPU.

**Требование:** "CRUD Service, Anomaly Detection Service и Notification Worker должны поддерживать горизонтальное масштабирование путём увеличения числа реплик" (README, нефункциональные, п.2).

**Сценарий:**

```powershell
# 1. Стартовое состояние: 2 реплики, CPU низкий
kubectl get hpa -n boiler boiler-api

# 2. отправляем API ~500 запросами в 10 параллельных потоков
$jobs = 1..10 | ForEach-Object {
    Start-Job -ScriptBlock {
        $deadline = (Get-Date).AddMinutes(3)
        $i = 0
        while ((Get-Date) -lt $deadline) {
            try {
                Invoke-RestMethod 'http://localhost:18080/api/v1/boilers' -TimeoutSec 2 | Out-Null
                $i++
            } catch {}
        }
        return $i
    }
}

# 3. Каждые 30 секунд смотрим HPA + кол-во подов
1..6 | ForEach-Object {
    Start-Sleep 30
    Write-Host "--- проход $_ ---"
    kubectl get hpa -n boiler boiler-api
    kubectl get pods -n boiler -l app.kubernetes.io/component=api --no-headers | Measure-Object | ForEach-Object { Write-Host "Реплик API: $($_.Count)" }
}

$total = ($jobs | Wait-Job | Receive-Job | Measure-Object -Sum).Sum
Write-Host "Всего запросов: $total"
$jobs | Remove-Job
```

**Ожидаемый результат:**
- В первые 30-60 сек CPU API растёт > 70% (можно увидеть в Grafana → Overview → CPU usage).
- HPA через 30-90 сек масштабирует до **4-6 реплик** (зависит от нагрузки).
- После прекращения нагрузки реплики не сразу уменьшаются (cooldown ~5 мин по умолчанию).

## S-02. Ручное масштабирование без downtime

**Цель:** `kubectl scale` добавляет реплики без потери трафика.

**Сценарий:**

```powershell
# 1. В одном окне запустить непрерывные запросы
$job = Start-Job -ScriptBlock {
    $ok = 0; $fail = 0
    1..120 | ForEach-Object {
        try { Invoke-RestMethod 'http://localhost:18080/health' -TimeoutSec 2 | Out-Null; $ok++ }
        catch { $fail++ }
        Start-Sleep -Milliseconds 500
    }
    "ok=$ok fail=$fail"
}

# 2. Параллельно масштабируем туда-сюда
kubectl scale deploy -n boiler boiler-api --replicas=5
Start-Sleep 20
kubectl get pods -n boiler -l app.kubernetes.io/component=api
kubectl scale deploy -n boiler boiler-api --replicas=2
Start-Sleep 20

# 3. Смотрим итог
$job | Wait-Job | Receive-Job
$job | Remove-Job
```

**Ожидаемый результат:**
- Во время скейла 2 → 5 и 5 → 2 ни один из 120 health-чеков не падает.
- `fail = 0` — zero-downtime подтверждён.

---

# Observability — логи, метрики, трейсы

## O-01. Трейс одного запроса проходит через 3 сервиса

**Цель:** один HTTP-запрос порождает связный trace через api → kafka → anomaly → kafka → notification, видимый в Jaeger.

**Сценарий:**

```powershell
# 1. Создать бойлер
$id = (Invoke-RestMethod 'http://localhost:18080/api/v1/boilers' -Method POST `
       -Body (@{name="O01-$(Get-Random)";location='X';temperatureThreshold=85;pressureThreshold=10}|ConvertTo-Json) `
       -ContentType 'application/json').id

# 2. Шлём аномальную телеметрию, ловим X-Trace-Id из ответа
$t = @{ boilerId=$id; temperature=99; pressure=15; timestamp=(Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ss.fffZ') } | ConvertTo-Json
$resp = Invoke-WebRequest 'http://localhost:18080/api/v1/telemetry' -Method POST -Body $t -ContentType 'application/json' -UseBasicParsing
$trace = $resp.Headers['X-Trace-Id']
Write-Host "X-Trace-Id: $trace" -ForegroundColor Yellow

Start-Sleep 7

# 3. Из Jaeger API запрашиваем этот trace, считаем spans и сервисы
$jt = Invoke-RestMethod "http://localhost:16686/api/traces/$trace"
$spans = $jt.data[0].spans
$svcs = ($jt.data[0].processes.PSObject.Properties.Value.serviceName) | Sort-Object -Unique
Write-Host "Spans: $($spans.Count), Сервисов: $($svcs -join ', ')"

# 4. Открыть UI:
Write-Host "Открывай: http://localhost:16686/trace/$trace"
```

**Ожидаемый результат:**
- В трейсе **минимум 8 spans** через **3 сервиса**: `boiler-telemetry-api`, `boiler-telemetry-anomaly`, `boiler-telemetry-notification`.
- В Jaeger UI у каждого span в Process tags виден `k8s.pod.name`, у Kafka spans — `messaging.kafka.partition`/`offset`.

## O-02. Логи в OpenSearch и связь с trace

**Цель:** все логи централизованы и связаны с трейсами по trace_id.

**Сценарий:**

```powershell
# Используем $trace из O-01

# 1. Из OpenSearch query API — поиск логов по trace_id
$q = @{ size=50; query=@{ term=@{ 'fields.TraceId.keyword'=$trace } }; sort=@(@{ '@timestamp'='asc' }) } | ConvertTo-Json -Depth 5
$r = Invoke-RestMethod 'http://localhost:5601/api/console/proxy?path=boiler-telemetry-*/_search&method=POST' `
     -Method POST -Headers @{'osd-xsrf'='true'} -ContentType 'application/json' -Body $q -TimeoutSec 10
Write-Host "Логов с этим TraceId: $($r.hits.total.value)"

# 2. Группировка по сервисам и подам
$r.hits.hits | ForEach-Object {
    [pscustomobject]@{ Service = $_._source.fields.Service; Pod = $_._source.fields.Pod }
} | Group-Object Service, Pod | ForEach-Object {
    Write-Host ("  {0}  →  {1} логов" -f $_.Name, $_.Count)
}

# 3. Открыть Discover в UI
Write-Host "Открывай: http://localhost:5601 → Discover → фильтр: fields.TraceId:`"$trace`""
```

**Ожидаемый результат:**
- Найдено **>= 20 логов** через все 3 сервиса.
- Каждый лог содержит `fields.TraceId`, `fields.SpanId`, `fields.Pod`, `fields.Service` — видно из какой реплики какого сервиса пришла запись.
- В Discover работает фильтр `fields.TraceId:"<id>"`.
- В Grafana → Explore → datasource Jaeger → spany имеют кнопку "Logs for this span" которая открывает OpenSearch с тем же фильтром.

## O-03. Метрики в Prometheus + дашборды в Grafana

**Цель:** счётчики работают и видны на дашборде.

**Сценарий:**

```powershell
# 1. Снимем стартовое значение
$q = 'sum(boiler_anomalies_detected_total)'
$before = [int]((Invoke-RestMethod "http://localhost:9090/api/v1/query?query=$([uri]::EscapeDataString($q))").data.result[0].value[1])
Write-Host "Аномалий до: $before"

# 2. Шлём 5 аномальных запросов
$id = (Invoke-RestMethod 'http://localhost:18080/api/v1/boilers' -Method POST `
       -Body (@{name="O03-$(Get-Random)";location='X';temperatureThreshold=85;pressureThreshold=10}|ConvertTo-Json) `
       -ContentType 'application/json').id

1..5 | ForEach-Object {
    $t = @{ boilerId=$id; temperature=99; pressure=15; timestamp=(Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ss.fffZ') } | ConvertTo-Json
    Invoke-WebRequest 'http://localhost:18080/api/v1/telemetry' -Method POST -Body $t -ContentType 'application/json' -UseBasicParsing | Out-Null
}
Start-Sleep 15

# 3. Снова значение метрики
$after = [int]((Invoke-RestMethod "http://localhost:9090/api/v1/query?query=$([uri]::EscapeDataString($q))").data.result[0].value[1])
Write-Host "Аномалий после: $after (ожидаем +10 — по 2 на каждый из 5 запросов)"

# 4. Проверим что в Prometheus есть все targets
$tg = Invoke-RestMethod 'http://localhost:9090/api/v1/targets?state=active'
$tg.data.activeTargets | Group-Object scrapePool | ForEach-Object {
    $up = ($_.Group | Where-Object { $_.health -eq 'up' }).Count
    Write-Host ("  {0,-22} {1}/{2} up" -f $_.Name, $up, $_.Group.Count)
}
```

**Ожидаемый результат:**
- `after - before = 10` (5 запросов × 2 типа аномалии каждый).
- В Prometheus → Status → Targets все 7 endpoint'ов `up` (2× api, 2× anomaly, 2× notification, prometheus self).
- В Grafana → Boiler Telemetry — Overview → панель "Аномалий обнаружено" увеличилась на 10.