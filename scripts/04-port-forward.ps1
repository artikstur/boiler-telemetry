# 04 - Поднимает port-forward на все UI-сервисы.
# Запускает kubectl port-forward в фоне отдельными процессами — они переживают
# закрытие этой консоли, но не переживут перезагрузку компа.
#
# minikube driver=docker на Windows: NodePort'ы НЕ доступны по minikube ip из
# браузера (особенность Docker Desktop), поэтому пробрасываем на localhost.

$ErrorActionPreference = 'Stop'
$env:Path = "$env:LOCALAPPDATA\bin;$env:Path"

# Прибьём существующие kubectl port-forward процессы
Write-Host "==> Останавливаю старые port-forward'ы..." -ForegroundColor Yellow
Get-Process kubectl -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowTitle -eq '' } | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep 2

$services = @(
    @{ name = 'boiler-nginx';          local = 18080; remote = 8080;  label = 'API (через nginx)' }
    @{ name = 'opensearch-dashboards'; local = 5601;  remote = 5601;  label = 'OpenSearch Dashboards' }
    @{ name = 'grafana';               local = 3000;  remote = 3000;  label = 'Grafana' }
    @{ name = 'jaeger-ui';             local = 16686; remote = 16686; label = 'Jaeger UI' }
    @{ name = 'kafka-ui';              local = 8085;  remote = 8080;  label = 'Kafka UI' }
    @{ name = 'prometheus';            local = 9090;  remote = 9090;  label = 'Prometheus' }
)

Write-Host "==> Запускаю port-forward'ы..." -ForegroundColor Cyan
foreach ($s in $services) {
    Start-Process -FilePath "kubectl" `
        -ArgumentList @('port-forward','-n','boiler',"svc/$($s.name)","$($s.local):$($s.remote)") `
        -WindowStyle Hidden | Out-Null
}
Start-Sleep 5

Write-Host ""
Write-Host "Открывай в браузере:" -ForegroundColor Green
Write-Host ""
foreach ($s in $services) {
    $ok = Test-NetConnection -ComputerName 'localhost' -Port $s.local -InformationLevel Quiet -WarningAction SilentlyContinue
    $mark = if ($ok) { '[OK]  ' } else { '[FAIL]' }
    Write-Host ("  {0} {1,-22} http://localhost:{2}" -f $mark, $s.label, $s.local)
}
Write-Host ""
Write-Host "Бонус (запущены в Docker, не в k8s):" -ForegroundColor Cyan
Write-Host "  InfluxDB UI            http://localhost:28086 (admin/adminpassword)"
Write-Host "  Postgres               localhost:25432       (postgres/postgres)"
Write-Host ""
Write-Host "Чтобы остановить все port-forward'ы:" -ForegroundColor Yellow
Write-Host "  Get-Process kubectl | Stop-Process -Force"
