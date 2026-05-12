. "$PSScriptRoot\_common.ps1"
Init-Test 'O-03' 'Метрики в Prometheus + дашборды'
Require-Api

$base = 'http://localhost:18080'

function Query-Prom([string]$expr) {
    $url = "http://localhost:9090/api/v1/query?query=" + [uri]::EscapeDataString($expr)
    $r = Invoke-RestMethod $url -TimeoutSec 10
    if ($r.data.result.Count -gt 0) { return [int]$r.data.result[0].value[1] } else { return 0 }
}

Section '1. Стартовое значение метрики boiler_anomalies_detected_total'
$before = Query-Prom 'sum(boiler_anomalies_detected_total)'
Info "До: $before"

Section '2. Шлём 5 аномальных запросов'
$body = @{ name = "O03-$(Get-Random)"; location = 'X'; temperatureThreshold = 85; pressureThreshold = 10 } | ConvertTo-Json
$id = (Invoke-RestMethod "$base/api/v1/boilers" -Method POST -Body $body -ContentType 'application/json').id
Good "boilerId = $id"

1..5 | ForEach-Object {
    $t = @{ boilerId = $id; temperature = 99; pressure = 15; timestamp = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ss.fffZ') } | ConvertTo-Json
    Invoke-WebRequest "$base/api/v1/telemetry" -Method POST -Body $t -ContentType 'application/json' -UseBasicParsing | Out-Null
    Write-Host "    Послан запрос $_" -ForegroundColor Gray
}
Info "Жду 15 сек чтобы Prometheus собрал свежие значения..."
Start-Sleep 15

Section '3. Значение метрики после'
$after = Query-Prom 'sum(boiler_anomalies_detected_total)'
$delta = $after - $before
Info "После: $after (delta = $delta, ожидалось +10)"
if ($delta -eq 10) { Good "delta = 10 (5 запросов x 2 типа аномалии) — метрика работает" }
elseif ($delta -gt 0) { Warn "delta = $delta, ожидалось 10. Возможно scrape interval ещё не догнал — повтори через минуту." }
else { Bad "delta = 0 — счётчик не двигается, проверь /metrics на AnomalyService" }

Section '4. Состояние Prometheus targets'
$tg = Invoke-RestMethod 'http://localhost:9090/api/v1/targets?state=active' -TimeoutSec 10
$tg.data.activeTargets | Group-Object scrapePool | ForEach-Object {
    $up = ($_.Group | Where-Object { $_.health -eq 'up' } | Measure-Object).Count
    $total = ($_.Group | Measure-Object).Count
    $mark = if ($up -eq $total) { '[OK]  ' } else { '[WARN]' }
    Write-Host ("    {0} {1,-25} {2}/{3} up" -f $mark, $_.Name, $up, $total)
}

Section '5. Открыть Grafana'
Write-Host "    http://localhost:3000 -> Boiler Telemetry - Overview -> 'Аномалий обнаружено'" -ForegroundColor Cyan

Footer
