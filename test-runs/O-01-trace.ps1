. "$PSScriptRoot\_common.ps1"
Init-Test 'O-01' 'Trace одного запроса через 3 сервиса (Jaeger)'
Require-Api

$base = 'http://localhost:18080'

Section '1. Создаём бойлер'
$body = @{ name = "O01-$(Get-Random)"; location = 'X'; temperatureThreshold = 85; pressureThreshold = 10 } | ConvertTo-Json
$id = (Invoke-RestMethod "$base/api/v1/boilers" -Method POST -Body $body -ContentType 'application/json').id
Good "boilerId = $id"

Section '2. Шлём аномальную телеметрию, ловим X-Trace-Id'
$t = @{ boilerId = $id; temperature = 99; pressure = 15; timestamp = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ss.fffZ') } | ConvertTo-Json
$resp = Invoke-WebRequest "$base/api/v1/telemetry" -Method POST -Body $t -ContentType 'application/json' -UseBasicParsing
$trace = $resp.Headers['X-Trace-Id']
if ($trace -is [array]) { $trace = $trace[0] }
Good "X-Trace-Id = $trace"

Info "Жду 7 сек чтобы все spans долетели в Jaeger..."
Start-Sleep 7

Section '3. Запрашиваем trace из Jaeger API'
try {
    $jt = Invoke-RestMethod "http://localhost:16686/api/traces/$trace" -TimeoutSec 10
    $spans = $jt.data[0].spans
    $svcs = ($jt.data[0].processes.PSObject.Properties.Value.serviceName) | Sort-Object -Unique
    Info ("Spans найдено: {0}" -f $spans.Count)
    Info ("Сервисов в трейсе: {0}" -f ($svcs -join ', '))
    if ($spans.Count -ge 8) { Good "Spans >= 8 (как и ожидалось)" } else { Warn "Spans = $($spans.Count), ожидалось >= 8" }
    if ($svcs.Count -ge 3) { Good "Сервисов >= 3 — цепочка api -> anomaly -> notification" } else { Warn "Сервисов = $($svcs.Count)" }

    Info ""
    Info "Tag-и одного спана (последний):"
    $sp = $spans[-1]
    $sp.tags | Where-Object { $_.key -in @('k8s.pod.name','messaging.kafka.partition','messaging.kafka.offset','messaging.kafka.topic') } | ForEach-Object {
        Write-Host ("      {0} = {1}" -f $_.key, $_.value)
    }
} catch {
    Bad "Запрос к Jaeger упал: $($_.Exception.Message)"
}

Section '4. Открыть в браузере'
Write-Host "    http://localhost:16686/trace/$trace" -ForegroundColor Cyan

Footer
