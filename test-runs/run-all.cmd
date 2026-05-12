@echo off
cd /d "%~dp0"
powershell -NoExit -ExecutionPolicy Bypass -File "%~dp0run-all.ps1"
