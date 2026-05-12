. "$PSScriptRoot\_common.ps1"
Init-Test 'F-01' 'End-to-end: телеметрия -> аномалия -> уведомление'
Require-Api

$base = 'http://localhost:18080'

Section '1. Создаём бойлер с порогами 85 C / 10 bar'
$body = @{ name = "F01-$(Get-Random)"; location = 'Цех-1'; temperatureThreshold = 85; pressureThreshold = 10 } | ConvertTo-Json
$boiler = Invoke-RestMethod "$base/api/v1/boilers" -Method POST -Body $body -ContentType 'application/json'
$id = $boiler.id
Good "boilerId = $id"

Section '2. Шлём аномальную телеметрию: 99 C / 15 bar (оба порога превышены)'
$t = @{ boilerId = $id; temperature = 99; pressure = 15; timestamp = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ss.fffZ') } | ConvertTo-Json
$resp = Invoke-WebRequest "$base/api/v1/telemetry" -Method POST -Body $t -ContentType 'application/json' -UseBasicParsing
Info ("HTTP {0}" -f $resp.StatusCode)
Info ("X-Trace-Id: {0}" -f $resp.Headers['X-Trace-Id'])
if ($resp.StatusCode -eq 202) { Good "POST /telemetry -> 202 Accepted (ожидаемо)" } else { Bad "Ожидался 202, получили $($resp.StatusCode)" }

Info "Жду 6 сек чтобы Kafka протолкнула сообщения через 3 сервиса..."
Start-Sleep 6

Section '3. Проверяем что в InfluxDB есть точка'
$flux = "from(bucket:`"telemetry`") |> range(start: -5m) |> filter(fn: (r) => r.boiler_id == `"$id`")"
try {
    $csv = Invoke-RestMethod 'http://localhost:28086/api/v2/query?org=boiler-org' `
        -Method POST `
        -Headers @{ Authorization = 'Token dev-token'; 'Content-Type' = 'application/vnd.flux'; Accept = 'application/csv' } `
        -Body $flux -TimeoutSec 10
    Write-Host $csv
    if ($csv -match 'temperature' -and $csv -match '99') { Good "Influx: точка найдена" } else { Warn "Influx ответил, но 99 C не нашёл в CSV — проверь вывод выше" }
} catch {
    Bad "Influx запрос упал: $_"
}

Section '4. Проверяем что в Postgres два уведомления (temperature + pressure)'
$sql = "SELECT message, created_at FROM notifications WHERE boiler_id='$id' ORDER BY created_at;"
docker exec boiler-postgres psql -U postgres -d boiler_telemetry -c $sql

$cnt = (docker exec boiler-postgres psql -U postgres -d boiler_telemetry -tAc "SELECT COUNT(*) FROM notifications WHERE boiler_id='$id';") -as [int]
if ($cnt -eq 2) { Good "В Postgres ровно 2 уведомления (как и ожидалось)" }
elseif ($cnt -gt 0) { Warn "В Postgres $cnt уведомлений (ожидалось 2)" }
else { Bad "В Postgres 0 уведомлений — цепочка не дошла до notification-worker" }

Footer
