@echo off
REM bgp-watch: the detector's command line. Runs inside the stack that start.bat started,
REM or, without the stack, the local build. Double-click shows the manual.
REM   bgp-watch help [COMMAND]       the manual, or one command's section
REM   bgp-watch prefixes | check | watch | replay | notify test
REM Files for replay inside the stack are container paths, such as
REM /data/incidents/youtube-2008/updates.20080224.1845.gz.
setlocal
cd /d "%~dp0"
set "LOCAL=src\BGPWatch.Web\bin\Debug\net10.0\bgp-watch.exe"
if not exist "%LOCAL%" set "LOCAL=src\BGPWatch.Web\bin\Release\net10.0\bgp-watch.exe"

docker inspect -f "{{.State.Running}}" app-bgpwatch 2>NUL | findstr /b true >NUL
if errorlevel 1 goto local
if "%~1"=="" goto help_stack
docker compose exec app dotnet bgp-watch.dll %*
exit /b %errorlevel%

:help_stack
docker compose exec app dotnet bgp-watch.dll help
pause
exit /b 0

:local
if not exist "%LOCAL%" goto nothing
if "%~1"=="" goto help_local
"%LOCAL%" %*
exit /b %errorlevel%

:help_local
"%LOCAL%" help
pause
exit /b 0

:nothing
echo The detector is not running and there is no local build.
echo Double-click start.bat, or build with: dotnet build BGPWatch.slnx
if "%~1"=="" pause
exit /b 1