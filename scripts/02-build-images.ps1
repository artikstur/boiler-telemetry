# 02 - Сборка docker-образов приложений и загрузка их в minikube.
# minikube image load кладёт образ во встроенный docker-демон minikube,
# поэтому imagePullPolicy=IfNotPresent его подхватит без registry.

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$app  = "$root\app"

$images = @(
    @{ Name = 'app-boiler-telemetry-api:latest'; Dockerfile = 'src/BoilerTelemetry.Api/Dockerfile' }
    @{ Name = 'app-anomaly-service:latest';      Dockerfile = 'src/BoilerTelemetry.AnomalyService/Dockerfile' }
    @{ Name = 'app-notification-worker:latest';  Dockerfile = 'src/BoilerTelemetry.NotificationWorker/Dockerfile' }
)

foreach ($img in $images) {
    Write-Host "==> docker build $($img.Name)" -ForegroundColor Cyan
    docker build -t $img.Name -f "$app\$($img.Dockerfile)" $app
    if ($LASTEXITCODE -ne 0) { throw "build failed: $($img.Name)" }
}

foreach ($img in $images) {
    Write-Host "==> minikube image load $($img.Name)" -ForegroundColor Cyan
    minikube image load $img.Name
    if ($LASTEXITCODE -ne 0) { throw "minikube image load failed: $($img.Name)" }
}

Write-Host ""
Write-Host "==> Образы собраны и загружены в minikube." -ForegroundColor Green
