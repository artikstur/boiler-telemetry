. "$PSScriptRoot\_common.ps1"
Init-Test 'R-01' 'Падение реплики API — система остаётся доступной'
Require-Api

$base = 'http://localhost:18080'

Section '1. Стартовое состояние подов API'
kubectl get pods -n boiler -l app.kubernetes.io/component=api

Section '2. Прибиваем один pod API (--grace-period=0 --force)'
$pod1 = (kubectl get pods -n boiler -l app.kubernetes.io/component=api -o jsonpath='{.items[0].metadata.name}')
if (-not $pod1) {
    Bad "Не нашёл pod'ов API."
    Footer; exit 1
}
Info "Убиваю $pod1"
kubectl delete pod -n boiler $pod1 --grace-period=0 --force 2>$null

Section '3. Сразу шлём 20 health-чеков — должны пройти через вторую реплику'
$ok = 0; $fail = 0; $errs = @()
1..20 | ForEach-Object {
    try {
        Invoke-RestMethod "$base/health" -TimeoutSec 3 | Out-Null
        $ok++
    } catch {
        $fail++
        $errs += $_.Exception.Message
    }
    Start-Sleep -Milliseconds 200
}
Write-Host ("    Health-чеков: {0} успешных / {1} упавших" -f $ok, $fail)
if ($fail -eq 0) { Good "Ни один запрос не упал — PDB + 2 реплики сработали" }
else {
    Warn "$fail запросов упало. Последние ошибки:"
    $errs | Select-Object -First 3 | ForEach-Object { Write-Host "      $_" -ForegroundColor DarkGray }
}

Section '4. Ждём 30 сек и проверяем что k8s поднял замену'
Start-Sleep 30
kubectl get pods -n boiler -l app.kubernetes.io/component=api
$running = (kubectl get pods -n boiler -l app.kubernetes.io/component=api --no-headers | Where-Object { $_ -match '\bRunning\b' } | Measure-Object).Count
Info ("Running реплик: {0}" -f $running)
if ($running -ge 2) { Good "Снова >= 2 реплики Running — самовосстановление подтверждено" }
else { Warn "Реплик < 2 — попробуй повторно через минуту" }

Footer
