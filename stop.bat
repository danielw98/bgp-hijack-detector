@echo off
REM Double-click: stop the detector stack and the lab. The Postgres volume stays.
setlocal
cd /d "%~dp0"

echo [1/2] Opresc detectorul...
docker compose down --remove-orphans
if errorlevel 1 goto failed

echo [2/2] Opresc laboratorul...
docker compose -f lab\docker-compose.yml down
if errorlevel 1 goto failed

echo Oprit. Volumul Postgres (rute, alerte) este intact.
ping -n 4 127.0.0.1 >NUL
exit /b 0

:failed
echo.
echo Eroare la oprire. Mesajul Docker este mai sus.
pause
exit /b 1
