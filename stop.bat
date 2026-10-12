@echo off
REM Double-click: stop the detector stack and the lab. The Postgres volume stays.
setlocal
cd /d "%~dp0"

echo [1/2] Stopping the detector...
docker compose down --remove-orphans
if errorlevel 1 goto failed

echo [2/2] Stopping the lab...
docker compose -f lab\docker-compose.yml down
if errorlevel 1 goto failed

echo Stopped. The Postgres volume (routes, alerts) is intact.
ping -n 4 127.0.0.1 >NUL
exit /b 0

:failed
echo.
echo The stop failed. The Docker message is above.
pause
exit /b 1
