# Общие хелперы для всех тест-скриптов.
# Подключается через:  . "$PSScriptRoot\_common.ps1"

$ErrorActionPreference = 'Continue'
$env:Path = "$env:LOCALAPPDATA\bin;$env:Path"

# Корень проекта = на уровень выше папки test-runs
$ProjectRoot = (Resolve-Path "$PSScriptRoot\..").Path

function Init-Test {
    param([string]$Code, [string]$Title)
    try { $Host.UI.RawUI.WindowTitle = "[$Code] $Title" } catch {}
    Write-Host ""
    Write-Host ("=" * 78) -ForegroundColor Cyan
    Write-Host (" {0} — {1}" -f $Code, $Title) -ForegroundColor Cyan
    Write-Host ("=" * 78) -ForegroundColor Cyan
    Write-Host ""
}

function Section {
    param([string]$Name)
    Write-Host ""
    Write-Host ("── {0} " -f $Name).PadRight(78, '─') -ForegroundColor Yellow
}

function Info($msg)    { Write-Host "    $msg" -ForegroundColor Gray }
function Good($msg)    { Write-Host "[OK]   $msg" -ForegroundColor Green }
function Bad($msg)     { Write-Host "[FAIL] $msg" -ForegroundColor Red }
function Warn($msg)    { Write-Host "[WARN] $msg" -ForegroundColor Yellow }

function Footer {
    Write-Host ""
    Write-Host ("=" * 78) -ForegroundColor Green
    Write-Host " Тест завершён. Окно остаётся открытым — можно листать вывод." -ForegroundColor Green
    Write-Host " Закрыть: крестик окна или 'exit'." -ForegroundColor Green
    Write-Host ("=" * 78) -ForegroundColor Green
}

# Быстрая проверка что API доступен. Если нет — намёк что делать.
function Require-Api {
    try {
        Invoke-RestMethod 'http://localhost:18080/health' -TimeoutSec 3 | Out-Null
    } catch {
        Bad "API недоступен на http://localhost:18080"
        Info "Запусти из корня проекта:  .\windows\04-ports.ps1"
        Info "Или полностью подними кластер:  .\windows\02-up.ps1"
        Footer
        exit 1
    }
}
