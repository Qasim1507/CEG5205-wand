@echo off
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Start-Quest-Simulator.ps1"
if errorlevel 1 pause
