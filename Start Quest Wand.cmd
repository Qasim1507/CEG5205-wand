@echo off
setlocal
cd /d "%~dp0"
if not exist ".venv\Scripts\python.exe" (
    echo Run Setup Quest Wand.cmd first.
    pause
    exit /b 1
)
".venv\Scripts\python.exe" -u python\quest_wand.py %*
set "run_status=%errorlevel%"
pause
exit /b %run_status%
