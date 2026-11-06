@echo off
setlocal
cd /d "%~dp0"
where py >nul 2>nul
if errorlevel 1 (
    python setup_wand.py %*
) else (
    py -3 setup_wand.py %*
)
set "setup_status=%errorlevel%"
if not "%setup_status%"=="0" echo Setup failed. Install Python 3.12, 3.13 or 3.14 from https://www.python.org/downloads/ and see README.md.
pause
exit /b %setup_status%
