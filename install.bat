@echo off
REM Double-click: install Docker Desktop, the .NET 10 SDK, MSYS2 gcc and Python with Playwright.
setlocal
cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\install-windows.ps1"
if errorlevel 1 goto failed
pause
exit /b 0

:failed
echo.
echo The installation stopped. The error message is above.
pause
exit /b 1
