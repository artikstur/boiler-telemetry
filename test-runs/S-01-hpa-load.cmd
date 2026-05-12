@echo off
cd /d "%~dp0"
powershell -NoExit -ExecutionPolicy Bypass -File "%~dp0S-01-hpa-load.ps1"
