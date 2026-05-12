. "$PSScriptRoot\_common.ps1"
Init-Test 'S-01' 'HPA добавляет реплики под нагрузкой (~3 минуты)'
Require-Api

$base = 'http://localhost:18080'

Section '1. Стартовое состояние HPA + кол-во подов API'
kubectl get hpa -n boiler boiler-api
$pre = (kubectl get pods -n boiler -l app.kubernetes.io/component=api --no-headers | Measure-Object).Count
Info "Реплик API в старте: $pre"

Section '2. Запускаем 10 параллельных потоков на /api/v1/boilers (3 минуты)'
$jobs = 1..10 | ForEach-Object {
    Start-Job -ScriptBlock {
        $deadline = (Get-Date).AddMinutes(3)
        $i = 0
        while ((Get-Date) -lt $deadline) {
            try {
                Invoke-RestMethod 'http://localhost:18080/api/v1/boilers' -TimeoutSec 2 | Out-Null
                $i++
            } catch {}
        }
        return $i
    }
}
Good "Стартовано $($jobs.Count) job'ов нагрузки"

Section '3. Каждые 30 сек смотрим HPA и кол-во подов (6 проходов)'
1..6 | ForEach-Object {
    Start-Sleep 30
    Write-Host ""
    Write-Host "--- проход $_ / 6 (через $($_ * 30) сек) ---" -ForegroundColor Cyan
    kubectl get hpa -n boiler boiler-api
    $cnt = (kubectl get pods -n boiler -l app.kubernetes.io/component=api --no-headers | Measure-Object).Count
    Write-Host "Реплик API: $cnt" -ForegroundColor White
}

Section '4. Останавливаем нагрузку и собираем статистику'
$total = ($jobs | Wait-Job | Receive-Job | Measure-Object -Sum).Sum
$jobs | Remove-Job
Info "Всего запросов прошло: $total"

$post = (kubectl get pods -n boiler -l app.kubernetes.io/component=api --no-headers | Measure-Object).Count
Info "Реплик API в конце: $post"
if ($post -gt $pre) { Good "HPA отскейлил вверх ($pre -> $post)" }
else { Warn "Реплик не прибавилось (но HPA мог не успеть среагировать или нагрузка слабая)" }

Info ""
Info "В Grafana -> Overview -> CPU usage по сервисам должен быть пик."
Info "После остановки нагрузки HPA уменьшит реплики не сразу (cooldown ~5 минут)."

Footer
