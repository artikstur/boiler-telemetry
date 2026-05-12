@echo off
cd /d "%~dp0"
powershell -NoExit -ExecutionPolicy Bypass -File "%~dp0S-02-manual-scale.ps1"
