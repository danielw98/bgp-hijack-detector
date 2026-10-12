@echo off
REM Double-click: start the isolated BGP lab and the detector stack, then open the page.
REM The page keeps the port of a stack already running, or takes the first free local
REM port from FIRST_PORT to LAST_PORT.
setlocal
cd /d "%~dp0"
set "DOCKER_DESKTOP=%ProgramFiles%\Docker\Docker\Docker Desktop.exe"
set "FIRST_PORT=8080"
set "LAST_PORT=8099"

docker info >NUL 2>&1
if not errorlevel 1 goto docker_ready
if not exist "%DOCKER_DESKTOP%" goto no_docker
echo Starting Docker Desktop...
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
call :choose_port
if not defined BGP_PORT goto no_port
set "PAGE=http://localhost:%BGP_PORT%"

echo [1/3] Starting the lab: three FRR routers, isolated from the Internet...
docker compose -f lab\docker-compose.yml up -d
if errorlevel 1 goto failed

echo [2/3] Starting the detector on port %BGP_PORT%: BMP station, Postgres, web page. The first start can take 5 minutes...
docker compose up -d --build --remove-orphans
if errorlevel 1 goto failed

echo [3/3] Checking the page...
set /a tries=0

:wait_page
curl -s -f -o NUL "%PAGE%/api/stats"
if not errorlevel 1 goto page_ready
set /a tries+=1
if %tries% geq 60 goto page_down
ping -n 3 127.0.0.1 >NUL
goto wait_page

:page_ready
echo Ready. Opening %PAGE%
start "" "%PAGE%"
exit /b 0

:choose_port
REM A stack started earlier keeps its published port.
for /f "tokens=2 delims=:" %%p in ('docker compose port app 8080 2^>NUL') do set "BGP_PORT=%%p"
if defined BGP_PORT exit /b 0
set /a candidate=%FIRST_PORT%

:next_port
netstat -ano -p tcp | findstr /r /c:":%candidate% .*LISTENING" >NUL
if not errorlevel 1 goto port_taken
set "BGP_PORT=%candidate%"
exit /b 0

:port_taken
echo Port %candidate% is taken, trying the next one...
set /a candidate+=1
if %candidate% leq %LAST_PORT% goto next_port
exit /b 0

:no_docker
echo.
echo Docker is not running or not installed.
echo Open Docker Desktop, or double-click install.bat, then double-click start.bat again.
pause
exit /b 1

:no_port
echo.
echo Every local port from %FIRST_PORT% to %LAST_PORT% is taken.
echo Free one of them, then double-click start.bat again.
pause
exit /b 1

:failed
echo.
echo The start failed. The Docker message is above.
pause
exit /b 1

:page_down
echo.
echo The page did not start. Its log:
docker compose logs --tail 30 app
pause
exit /b 1
