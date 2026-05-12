# Прогоняет все тесты подряд в текущем окне.
# Деструктивные (R-02, R-03) можно пропустить флагом -SkipDestructive.
param(
    [switch]$SkipDestructive,
    [switch]$SkipLongRunning   # пропустить S-01 (3 минуты)
)

. "$PSScriptRoot\_common.ps1"

$tests = @(
    @{ code = 'F-01'; file = 'F-01-end-to-end.ps1';    long = $false; destructive = $false }
    @{ code = 'F-02'; file = 'F-02-validation.ps1';    long = $false; destructive = $false }
    @{ code = 'F-03'; file = 'F-03-duplicate.ps1';     long = $false; destructive = $false }
    @{ code = 'R-01'; file = 'R-01-pod-kill.ps1';      long = $false; destructive = $true  }
    @{ code = 'R-02'; file = 'R-02-postgres-kill.ps1'; long = $false; destructive = $true  }
    @{ code = 'R-03'; file = 'R-03-backup-restore.ps1';long = $true;  destructive = $true  }
    @{ code = 'R-04'; file = 'R-04-kafka-broker-kill.ps1'; long = $false; destructive = $true }
    @{ code = 'S-01'; file = 'S-01-hpa-load.ps1';      long = $true;  destructive = $false }
    @{ code = 'S-02'; file = 'S-02-manual-scale.ps1';  long = $true;  destructive = $false }
    @{ code = 'O-01'; file = 'O-01-trace.ps1';         long = $false; destructive = $false }
    @{ code = 'O-02'; file = 'O-02-logs.ps1';          long = $false; destructive = $false }
    @{ code = 'O-03'; file = 'O-03-metrics.ps1';       long = $false; destructive = $false }
)

foreach ($t in $tests) {
    if ($SkipDestructive -and $t.destructive) { Write-Host "Пропускаю $($t.code) (destructive)" -ForegroundColor DarkGray; continue }
    if ($SkipLongRunning -and $t.long)       { Write-Host "Пропускаю $($t.code) (long)" -ForegroundColor DarkGray; continue }

    Write-Host ""
    Write-Host ("##" * 39) -ForegroundColor Magenta
    Write-Host ("## Запускаю {0} -> {1}" -f $t.code, $t.file) -ForegroundColor Magenta
    Write-Host ("##" * 39) -ForegroundColor Magenta
    & "$PSScriptRoot\$($t.file)"
}

Write-Host ""
Write-Host "Все выбранные тесты прогнаны." -ForegroundColor Green
