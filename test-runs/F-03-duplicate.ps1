. "$PSScriptRoot\_common.ps1"
Init-Test 'F-03' 'Дубликат бойлера -> 409 Conflict'
Require-Api

$base = 'http://localhost:18080'
$name = "F03-Duplicate-$(Get-Random)"
$body = @{ name = $name; location = 'X'; temperatureThreshold = 85; pressureThreshold = 10 } | ConvertTo-Json

Section '1. Создаём первого бойлера (должно быть 201)'
$first = Invoke-RestMethod "$base/api/v1/boilers" -Method POST -Body $body -ContentType 'application/json'
Good "Создан id = $($first.id), name = $name"

Section "2. Создаём ещё одного с тем же именем '$name' (ожидаем 409)"
try {
    $second = Invoke-RestMethod "$base/api/v1/boilers" -Method POST -Body $body -ContentType 'application/json'
    Bad "Второй POST прошёл (id=$($second.id)) — ожидался 409"
} catch {
    $code = [int]$_.Exception.Response.StatusCode
    $msg = $_.ErrorDetails.Message
    Write-Host "    Status: $code"
    Write-Host "    Body:   $msg"
    if ($code -eq 409) { Good "Получили 409 Conflict (как и ожидалось)" }
    else { Bad "Получили $code, ожидался 409" }
    if ($msg -match "already exists" -or $msg -match "уже существует") { Good "В теле есть пояснение о дубликате" }
}

Footer
