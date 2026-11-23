@echo off
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Update-NightCoop.ps1" -Launch %*
if errorlevel 1 pause
