@echo off
REM Double-click: start the isolated BGP lab and the detector stack, then open the page.
setlocal
cd /d "%~dp0"
set "PAGE=http://localhost:8080"
set "DOCKER_DESKTOP=%ProgramFiles%\Docker\Docker\Docker Desktop.exe"

docker info >NUL 2>&1
if not errorlevel 1 goto docker_ready
if not exist "%DOCKER_DESKTOP%" goto no_docker
echo Pornesc Docker Desktop...
start "" "%DOCKER_DESKTOP%"
set /a tries=0

:wait_docker
ping -n 4 127.0.0.1 >NUL
docker info >NUL 2>&1
if not errorlevel 1 goto docker_ready
set /a tries+=1
if %tries% lss 60 goto wait_docker
goto no_docker

:docker_ready
echo [1/3] Pornesc laboratorul: trei rutere FRR, izolate de internet...
docker compose -f lab\docker-compose.yml up -d
if errorlevel 1 goto failed

echo [2/3] Pornesc detectorul: statia BMP, Postgres, pagina web. Prima pornire poate dura 5 minute...
docker compose up -d --build --remove-orphans
if errorlevel 1 goto failed

echo [3/3] Verific pagina...
set /a tries=0

:wait_page
curl -s -f -o NUL "%PAGE%/api/stats"
if not errorlevel 1 goto page_ready
set /a tries+=1
if %tries% geq 60 goto page_down
ping -n 3 127.0.0.1 >NUL
goto wait_page

:page_ready
echo Gata. Deschid %PAGE%
start "" "%PAGE%"
exit /b 0

:no_docker
echo.
echo Docker nu este pornit sau nu este instalat.
echo Deschide Docker Desktop, apoi dublu-clic din nou pe start.bat.
pause
exit /b 1

:failed
echo.
echo Eroare la pornire. Mesajul Docker este mai sus.
pause
exit /b 1

:page_down
echo.
echo Pagina nu a pornit. Jurnalul ei:
docker compose logs --tail 30 app
pause
exit /b 1
