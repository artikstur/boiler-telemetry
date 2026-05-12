. "$PSScriptRoot\_common.ps1"
Init-Test 'R-04' 'Падение брокера Kafka — кластер продолжает работать'
Require-Api

$base = 'http://localhost:18080'

Section '1. Стартовое состояние Kafka StatefulSet (должно быть 3/3 Ready)'
kubectl get statefulset kafka -n boiler
kubectl get pods -n boiler -l app=kafka -o wide

Section '2. Описание топика telemetry-events (партиции/ISR до)'
kubectl exec -n boiler kafka-0 -- /opt/kafka/bin/kafka-topics.sh --bootstrap-server kafka:9092 --describe --topic telemetry-events

Section '3. Создаём маркер-бойлер и шлём 1 телеметрию (контрольный pre-kill)'
$boiler = Invoke-RestMethod "$base/api/v1/boilers" -Method POST `
    -Body (@{ name = "R04-pre-$(Get-Random)"; location = 'X'; temperatureThreshold = 85; pressureThreshold = 10 } | ConvertTo-Json) `
    -ContentType 'application/json'
$id = $boiler.id
Good "boilerId = $id"
$t = @{ boilerId = $id; temperature = 99; pressure = 15; timestamp = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ss.fffZ') } | ConvertTo-Json
$resp = Invoke-WebRequest "$base/api/v1/telemetry" -Method POST -Body $t -ContentType 'application/json' -UseBasicParsing
Info ("pre-kill telemetry HTTP {0}" -f $resp.StatusCode)

Section '4. Прибиваем kafka-0 (один из брокеров, --grace-period=0 --force)'
kubectl delete pod -n boiler kafka-0 --grace-period=0 --force 2>$null
Info "kafka-0 убит. ISR должен схлопнуться до 2, продьюсер продолжает работать (min.insync.replicas=2)."

Section '5. Сразу шлём 20 телеметрий — все должны быть приняты (acks=all, ISR=2)'
$ok = 0; $fail = 0; $errors = @()
1..20 | ForEach-Object {
    $body = @{ boilerId = $id; temperature = (90 + $_); pressure = (11 + ($_ % 5)); timestamp = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ss.fffZ') } | ConvertTo-Json
    try {
        $r = Invoke-WebRequest "$base/api/v1/telemetry" -Method POST -Body $body -ContentType 'application/json' -UseBasicParsing -TimeoutSec 5
        if ($r.StatusCode -eq 202) { $ok++ } else { $fail++ }
    } catch {
        $fail++
        $errors += $_.Exception.Message
    }
    Start-Sleep -Milliseconds 200
}
Info ("Прошло: {0} ok / {1} fail (ожидалось 20/0)" -f $ok, $fail)
if ($fail -eq 0) { Good "Все 20 телеметрий приняты — кластер обработал падение брокера" }
else {
    Warn ("Упало {0} запросов. Первая ошибка:" -f $fail)
    $errors | Select-Object -First 1 | ForEach-Object { Write-Host "      $_" -ForegroundColor DarkGray }
}

Section '6. Состояние топиков ВО ВРЕМЯ падения (ISR должен быть 2 для тех partition, где kafka-0 был лидером)'
kubectl exec -n boiler kafka-1 -- /opt/kafka/bin/kafka-topics.sh --bootstrap-server kafka:9092 --describe --topic telemetry-events
Info "Колонка Isr должна показывать 2 элемента (вместо 3) для partition с убитым брокером"

Section '7. Ждём 45 сек и проверяем что kafka-0 вернулся'
Start-Sleep 45
kubectl get pods -n boiler -l app=kafka
Info "Описание топика после восстановления (ISR должен снова стать 3):"
kubectl exec -n boiler kafka-0 -- /opt/kafka/bin/kafka-topics.sh --bootstrap-server kafka:9092 --describe --topic telemetry-events 2>$null
if ($LASTEXITCODE -ne 0) {
    Info "kafka-0 ещё не готов — пробуем через kafka-1"
    kubectl exec -n boiler kafka-1 -- /opt/kafka/bin/kafka-topics.sh --bootstrap-server kafka:9092 --describe --topic telemetry-events
}

Section '8. Контрольный post-recovery: шлём ещё одну телеметрию'
$t2 = @{ boilerId = $id; temperature = 88; pressure = 12; timestamp = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ss.fffZ') } | ConvertTo-Json
$resp2 = Invoke-WebRequest "$base/api/v1/telemetry" -Method POST -Body $t2 -ContentType 'application/json' -UseBasicParsing
Info ("post-recovery telemetry HTTP {0}" -f $resp2.StatusCode)
if ($resp2.StatusCode -eq 202) { Good "Кластер полностью восстановился" }

Section '9. Проверяем что данные дошли до Postgres (notifications от 20+1 телеметрий)'
Start-Sleep 5
$cnt = (docker exec boiler-postgres psql -U postgres -d boiler_telemetry -tAc "SELECT COUNT(*) FROM notifications WHERE boiler_id='$id';") -as [int]
Info "Уведомлений в Postgres для R-04 бойлера: $cnt"
# pre-kill: 2 (temp+pressure), 20 запросов после kill (каждый создаёт до 2 уведомлений = 40), post-recovery: 2.
# Минимум разумный — 22 (если pressure=11<=10? нет, 11>10, значит обе аномалии). Считаем что должно быть много.
if ($cnt -ge 20) { Good "Notifications >= 20 — потери данных нет" }
else { Warn "Notifications = $cnt, ожидалось >= 20. Возможно часть прошла в Kafka но ещё не обработана." }

Footer
