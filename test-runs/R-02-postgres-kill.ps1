. "$PSScriptRoot\_common.ps1"
Init-Test 'R-02' 'Падение Postgres — авто-restart, данные на месте'
Require-Api

$base = 'http://localhost:18080'

Section '1. Создаём маркер-бойлер и фиксируем количество'
$marker = @{ name = "R02-$(Get-Random)"; location = 'X'; temperatureThreshold = 85; pressureThreshold = 10 } | ConvertTo-Json
$id = (Invoke-RestMethod "$base/api/v1/boilers" -Method POST -Body $marker -ContentType 'application/json').id
Good "boilerId = $id"

$before = (Invoke-RestMethod "$base/api/v1/boilers" | Measure-Object).Count
Info "Бойлеров до падения: $before"

Section '2. Убиваем контейнер Postgres'
docker kill boiler-postgres | Out-Null
Info "boiler-postgres убит."
Start-Sleep 3

Section '3. Ждём пока Docker сам поднимет контейнер (restart: unless-stopped)'
Start-Sleep 12
docker ps -a --filter "name=^boiler-postgres$" --format "{{.Names}} -> {{.Status}}"

# Docker Desktop на Windows иногда НЕ авто-рестартует контейнер после SIGKILL —
# тогда поднимаем сами и помечаем WARN, чтобы не валить тест.
$status = docker inspect --format '{{.State.Status}}' boiler-postgres 2>$null
if ($status -eq 'exited') {
    Warn "Docker НЕ авто-рестартовал контейнер (баг Docker Desktop). Поднимаю сам через 'docker start'."
    docker start boiler-postgres | Out-Null
}

Section '4. Ждём что healthcheck станет healthy (до 120 сек)'
$deadline = (Get-Date).AddSeconds(120)
while ((Get-Date) -lt $deadline) {
    $s = docker inspect --format '{{.State.Health.Status}}' boiler-postgres 2>$null
    Write-Host "    postgres: $s"
    if ($s -eq 'healthy') { Good "Postgres healthy"; break }
    Start-Sleep 4
}

Section '5. Проверяем что данные на месте (даём API до 60 сек переподключиться)'
$after = $null
$deadline = (Get-Date).AddSeconds(60)
while ((Get-Date) -lt $deadline) {
    try {
        $after = (Invoke-RestMethod "$base/api/v1/boilers" -TimeoutSec 5 | Measure-Object).Count
        break
    } catch {
        Write-Host "    API ещё 500 — жду..."
        Start-Sleep 5
    }
}
Info "Бойлеров после восстановления: $after"
if ($null -eq $after) { Bad "API так и не вернулся в строй" }
elseif ($before -eq $after) { Good "before == after ($before) — данные не потеряны" }
else { Bad "before=$before, after=$after — данные изменились!" }

# GET по id может первые пару раз вернуть 500 если в пуле Npgsql остались мёртвые соединения —
# делаем retry до 30 сек.
$b = $null
$deadline = (Get-Date).AddSeconds(30)
while ((Get-Date) -lt $deadline) {
    try {
        $b = Invoke-RestMethod "$base/api/v1/boilers/$id" -TimeoutSec 5
        break
    } catch {
        Write-Host "    GET ещё падает — жду..."
        Start-Sleep 3
    }
}
if ($b) { Good "Маркер-бойлер ($id) на месте: $($b.name)" }
else    { Bad  "GET /boilers/$id так и не отдал бойлер" }

Info ""
Info "В Grafana -> Database Health должен быть провал 'Postgres up' DOWN -> UP."
Info "Открой: http://localhost:3000  (admin / admin)"

Footer
