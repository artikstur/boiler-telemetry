# 03 - Установка Helm-чарта в minikube.
# Перед этим должен быть запущен minikube (minikube start), а БД уже подняты
# скриптом 01. Чарт подтягивает bitnami/redis и bitnami/kafka как subcharts.

$ErrorActionPreference = 'Stop'
$root  = Split-Path -Parent $PSScriptRoot
$chart = "$root\helm\boiler-telemetry"

# externalHost: host.minikube.internal — DNS внутри подов minikube,
# указывающий на хост-машину Windows. На Docker Desktop K8s/kind поменяйте на host.docker.internal.
$externalHost = if ($env:EXTERNAL_HOST) { $env:EXTERNAL_HOST } else { 'host.minikube.internal' }

Write-Host "==> helm dependency build (redis, kafka subcharts)" -ForegroundColor Cyan
helm dependency build $chart
if ($LASTEXITCODE -ne 0) { throw "helm dep build failed" }

Write-Host "==> helm upgrade --install boiler $chart" -ForegroundColor Cyan
helm upgrade --install boiler $chart `
    --namespace boiler --create-namespace `
    --set externalHost=$externalHost `
    --wait --timeout 10m
if ($LASTEXITCODE -ne 0) { throw "helm install failed" }

Write-Host ""
Write-Host "==> Установлено. Доступ:" -ForegroundColor Green
Write-Host "    API:                  minikube service -n boiler boiler-nginx --url"
Write-Host "    OpenSearch Dashboards: http://<minikube-ip>:30601"
Write-Host "    Jaeger UI:            http://<minikube-ip>:30686"
Write-Host ""
Write-Host "    minikube ip"
