# Общие хелперы для всех тест-скриптов.
# Подключается через:  . "$PSScriptRoot\_common.ps1"

$ErrorActionPreference = 'Continue'
$env:Path = "$env:LOCALAPPDATA\bin;$env:Path"

# Корень проекта = на уровень выше папки test-runs
$ProjectRoot = (Resolve-Path "$PSScriptRoot\..").Path

# ── Загрузка .env (или .env.example, если .env ещё не создан) ────────────────
function Import-DotEnv {
    $envPath = Join-Path $ProjectRoot '.env'
    if (-not (Test-Path $envPath)) {
        $envPath = Join-Path $ProjectRoot '.env.example'
        Write-Host "[i] .env не найден, читаю .env.example. Скопируй .env.example в .env." -ForegroundColor Yellow
    }
    if (-not (Test-Path $envPath)) { return }
    Get-Content $envPath | ForEach-Object {
        $line = $_.Trim()
        if (-not $line -or $line.StartsWith('#')) { return }
        $eq = $line.IndexOf('=')
        if ($eq -lt 1) { return }
        $name = $line.Substring(0, $eq).Trim()
        $val  = $line.Substring($eq + 1).Trim().Trim('"').Trim("'")
        Set-Item -Path "Env:$name" -Value $val
    }
}
Import-DotEnv

# Шорткаты для удобства
$PG_USER     = $env:POSTGRES_USER
$PG_PASSWORD = $env:POSTGRES_PASSWORD
$PG_DB       = $env:POSTGRES_DB
$INFLUX_TOKEN = $env:INFLUX_ADMIN_TOKEN
$INFLUX_ORG   = $env:INFLUX_ORG
$INFLUX_BUCKET= $env:INFLUX_BUCKET

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
