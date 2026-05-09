# 05 - Сброс пароля Grafana к admin/admin.
# Нужен если переменная GF_SECURITY_ADMIN_PASSWORD не подхватилась
# (бывает при upgrade pod'а).

$env:Path = "$env:LOCALAPPDATA\bin;$env:Path"
$gpod = (kubectl get pods -n boiler -l app=grafana -o jsonpath='{.items[0].metadata.name}')
if (-not $gpod) { Write-Host "Grafana pod не найден"; exit 1 }
Write-Host "Pod: $gpod"
kubectl exec -n boiler $gpod -- grafana-cli admin reset-admin-password admin
Write-Host ""
Write-Host "Логин: admin / admin  ->  http://localhost:3000" -ForegroundColor Green
