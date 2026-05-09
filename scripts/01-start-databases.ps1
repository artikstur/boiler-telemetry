# 01 - Запуск БД (Postgres + InfluxDB) в Docker на хосте.
# Эти БД сидят ВНЕ Kubernetes; приложения в кластере коннектятся к ним через
# host.minikube.internal (см. helm/.../templates/external-db.yaml).

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

Write-Host "==> Поднимаю Postgres + InfluxDB в Docker..." -ForegroundColor Cyan
docker compose -f "$root\infra\databases\docker-compose.yml" up -d
if ($LASTEXITCODE -ne 0) { throw "docker compose up failed" }

Write-Host ""
Write-Host "==> Жду готовности контейнеров..." -ForegroundColor Cyan
$deadline = (Get-Date).AddSeconds(60)
while ((Get-Date) -lt $deadline) {
    $pg = docker inspect --format '{{.State.Health.Status}}' boiler-postgres 2>$null
    $ix = docker inspect --format '{{.State.Health.Status}}' boiler-influxdb 2>$null
    Write-Host ("  postgres: {0}, influxdb: {1}" -f $pg, $ix)
    if ($pg -eq 'healthy' -and $ix -eq 'healthy') { break }
    Start-Sleep -Seconds 3
}

Write-Host ""
Write-Host "==> БД доступны на хосте:" -ForegroundColor Green
Write-Host "    postgres:  localhost:5432   (db=boiler_telemetry user=postgres pass=postgres)"
Write-Host "    influxdb:  http://localhost:8086 (token=dev-token org=boiler-org bucket=telemetry)"
