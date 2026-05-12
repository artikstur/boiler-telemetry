. "$PSScriptRoot\_common.ps1"
Init-Test 'O-02' 'Логи в OpenSearch связаны с trace по TraceId'
Require-Api

$base = 'http://localhost:18080'

Section '1. Создаём бойлер и шлём аномальную телеметрию (как в O-01)'
$body = @{ name = "O02-$(Get-Random)"; location = 'X'; temperatureThreshold = 85; pressureThreshold = 10 } | ConvertTo-Json
$id = (Invoke-RestMethod "$base/api/v1/boilers" -Method POST -Body $body -ContentType 'application/json').id

$t = @{ boilerId = $id; temperature = 99; pressure = 15; timestamp = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ss.fffZ') } | ConvertTo-Json
$resp = Invoke-WebRequest "$base/api/v1/telemetry" -Method POST -Body $t -ContentType 'application/json' -UseBasicParsing
$trace = $resp.Headers['X-Trace-Id']
if ($trace -is [array]) { $trace = $trace[0] }
Good "X-Trace-Id = $trace"

Info "Жду 10 сек чтобы логи долетели в OpenSearch..."
Start-Sleep 10

Section '2. Ищем логи по trace_id через OpenSearch console proxy'
$q = @{
    size  = 50
    query = @{ term = @{ 'fields.TraceId.keyword' = $trace } }
    sort  = @(@{ '@timestamp' = 'asc' })
} | ConvertTo-Json -Depth 5

try {
    $r = Invoke-RestMethod 'http://localhost:5601/api/console/proxy?path=boiler-telemetry-*/_search&method=POST' `
        -Method POST -Headers @{ 'osd-xsrf' = 'true' } -ContentType 'application/json' -Body $q -TimeoutSec 15
    $total = $r.hits.total.value
    Info "Логов с этим TraceId: $total"
    if ($total -ge 20) { Good "Найдено >= 20 логов (как и ожидалось)" }
    elseif ($total -gt 0) { Warn "Логов = $total, ожидалось >= 20 (мб не все spans успели долететь)" }
    else { Bad "Логов нет — проверь что Serilog OpenSearch sink работает" }

    Section '3. Группировка по сервису и поду'
    $entries = $r.hits.hits | ForEach-Object {
        [pscustomobject]@{
            Service = $_._source.fields.Service
            Pod     = $_._source.fields.Pod
        }
    }
    $entries | Group-Object Service, Pod | ForEach-Object {
        Write-Host ("    {0,-50}  ->  {1} логов" -f $_.Name, $_.Count)
    }
} catch {
    Bad "Запрос к OpenSearch упал: $($_.Exception.Message)"
}

Section '4. Открыть в браузере'
Write-Host "    http://localhost:5601 -> Discover -> фильтр: fields.TraceId:`"$trace`"" -ForegroundColor Cyan
Write-Host "    Из Jaeger по этому trace в Grafana кнопка 'Logs for this span' должна вести в OpenSearch с тем же фильтром." -ForegroundColor Cyan

Footer
