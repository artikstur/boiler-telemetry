. "$PSScriptRoot\_common.ps1"
Init-Test 'R-03' 'Бэкап Postgres -> drop table -> restore'
Require-Api

$base = 'http://localhost:18080'
$markerName = "R03-$(Get-Random)"

Section "1. Создаём маркер-бойлер: $markerName"
$body = @{ name = $markerName; location = 'X'; temperatureThreshold = 85; pressureThreshold = 10 } | ConvertTo-Json
Invoke-RestMethod "$base/api/v1/boilers" -Method POST -Body $body -ContentType 'application/json' | Out-Null
Good "Создан"

Section '2. Делаем форсированный бэкап через postgres-backup контейнер'
docker exec boiler-postgres-backup /backup.sh
Start-Sleep 2
Info "Содержимое /backups/last/ :"
docker exec boiler-postgres-backup ls -la /backups/last/

Section '3. Дропаем таблицу boilers'
docker exec boiler-postgres psql -U postgres -d boiler_telemetry -c "DROP TABLE boilers CASCADE;"

Section '4. Проверяем что API теперь пятисотин'
try {
    Invoke-RestMethod "$base/api/v1/boilers" | Out-Null
    Warn "API вернул 200 мб EF закешировал."
} catch {
    $code = [int]$_.Exception.Response.StatusCode
    Info "API после drop: HTTP $code"
    if ($code -ge 500) { Good "Получили 5xx (как и ожидалось — таблицы нет)" }
}

Section '5. Восстанавливаем схему из последнего бэкапа'
docker exec boiler-postgres psql -U postgres -d boiler_telemetry -c "DROP SCHEMA public CASCADE; CREATE SCHEMA public;"
docker exec -e PGPASSWORD=postgres boiler-postgres-backup sh -c "gunzip -c /backups/last/boiler_telemetry-latest.sql.gz | psql -h postgres -U postgres -d boiler_telemetry" | Out-Null
Good "Restore выполнен"

Section '6. Рестартуем приложения (чтобы EF Core переподключился к свежей схеме)'
kubectl rollout restart deploy/boiler-api deploy/boiler-notification-worker -n boiler | Out-Null
kubectl -n boiler rollout status deploy/boiler-api --timeout=3m
kubectl -n boiler rollout status deploy/boiler-notification-worker --timeout=3m

Section '7. Проверяем что маркер-бойлер вернулся'
Start-Sleep 5
$arr = Invoke-RestMethod "$base/api/v1/boilers"
$found = $arr | Where-Object { $_.name -eq $markerName }
if ($found) { Good "Бойлер '$markerName' восстановлен из бэкапа" }
else { Bad "Бойлера '$markerName' нет в выдаче — бэкап/restore не сработал" }

Footer
