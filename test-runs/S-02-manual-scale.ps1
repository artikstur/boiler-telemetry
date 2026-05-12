. "$PSScriptRoot\_common.ps1"
Init-Test 'S-02' 'Ручной scale без downtime'
Require-Api

$base = 'http://localhost:18080'

Section '1. Запускаем фоновую долбёжку /health (120 запросов, по 500мс)'
$job = Start-Job -ScriptBlock {
    $ok = 0; $fail = 0
    1..120 | ForEach-Object {
        try { Invoke-RestMethod 'http://localhost:18080/health' -TimeoutSec 2 | Out-Null; $ok++ }
        catch { $fail++ }
        Start-Sleep -Milliseconds 500
    }
    [pscustomobject]@{ ok = $ok; fail = $fail }
}
Good "Background job стартовал (заняет ~60 сек)"

Section '2. Масштабируем boiler-api 2 -> 5'
kubectl scale deploy -n boiler boiler-api --replicas=5 | Out-Null
Start-Sleep 20
kubectl get pods -n boiler -l app.kubernetes.io/component=api

Section '3. Масштабируем обратно 5 -> 2'
kubectl scale deploy -n boiler boiler-api --replicas=2 | Out-Null
Start-Sleep 20
kubectl get pods -n boiler -l app.kubernetes.io/component=api

Section '4. Дожидаемся background job и смотрим итог'
$result = $job | Wait-Job | Receive-Job
$job | Remove-Job
Info ("ok = {0}, fail = {1}" -f $result.ok, $result.fail)
if ($result.fail -eq 0) { Good "fail = 0 — zero-downtime подтверждён" }
else { Warn "$($result.fail) запросов упало — мог быть rolling restart хвостом" }

Footer
