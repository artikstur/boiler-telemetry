. "$PSScriptRoot\_common.ps1"
Init-Test 'F-02' 'Валидация: невалидные запросы возвращают 400'
Require-Api

$base = 'http://localhost:18080'

function Expect-400 {
    param([string]$Label, [scriptblock]$Action)
    try {
        & $Action | Out-Null
        Bad "$Label : запрос НЕ упал — ожидался 400"
    } catch {
        $code = [int]$_.Exception.Response.StatusCode
        if ($code -eq 400) { Good "$Label : $code (как и ожидалось)" }
        else { Bad "$Label : $code (ожидался 400)" }
    }
}

Section '1. POST /boilers с пустыми полями и отрицательными порогами'
$bad1 = @{ name = ''; location = ''; temperatureThreshold = -1; pressureThreshold = 0 } | ConvertTo-Json
Expect-400 'POST /boilers (пустое имя, отрицательные пороги)' {
    Invoke-RestMethod "$base/api/v1/boilers" -Method POST -Body $bad1 -ContentType 'application/json'
}

Section '2. POST /telemetry с отрицательной температурой и давлением'
$bad2 = @{ boilerId = '00000000-0000-0000-0000-000000000000'; temperature = -50; pressure = -1; timestamp = (Get-Date).ToString('o') } | ConvertTo-Json
Expect-400 'POST /telemetry (отриц. значения)' {
    Invoke-RestMethod "$base/api/v1/telemetry" -Method POST -Body $bad2 -ContentType 'application/json'
}

Section '3. GET /telemetry/{id} с from > to'
$arr = Invoke-RestMethod "$base/api/v1/boilers"
if ($null -eq $arr -or ($arr -is [array] -and $arr.Count -eq 0)) {
    Info "В базе нет бойлеров — создаю временный..."
    $tmp = Invoke-RestMethod "$base/api/v1/boilers" -Method POST `
        -Body (@{ name = "F02-tmp-$(Get-Random)"; location = 'X'; temperatureThreshold = 85; pressureThreshold = 10 } | ConvertTo-Json) `
        -ContentType 'application/json'
    $id = $tmp.id
} elseif ($arr -is [array]) {
    $id = $arr[0].id
} else {
    $id = $arr.id
}
Info "boilerId = $id"
$from = [uri]::EscapeDataString('2026-12-31T00:00:00Z')
$to = [uri]::EscapeDataString('2026-01-01T00:00:00Z')
Expect-400 'GET /telemetry (from > to)' {
    Invoke-RestMethod "$base/api/v1/telemetry/$id`?from=$from&to=$to"
}

Footer
