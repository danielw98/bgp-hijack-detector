@echo off
REM The detector's command line, run inside the stack that start.bat started:
REM   bgpwatch prefixes list | add PREFIX ASN... [--description TEXT] | remove PREFIX
REM   bgpwatch check PREFIX | replay FILE... | notify test | help
REM Double-click shows the help. Files for replay are container paths, such as
REM /data/incidents/youtube-2008/updates.20080224.1845.gz.
setlocal
cd /d "%~dp0"
docker inspect -f "{{.State.Running}}" app-bgpwatch 2>NUL | findstr /b true >NUL
if not errorlevel 1 goto running
echo The detector is not running. Double-click start.bat first.
if "%~1"=="" pause
exit /b 1

:running
if "%~1"=="" goto help
docker compose exec app dotnet BGPWatch.dll %*
exit /b %errorlevel%

:help
docker compose exec app dotnet BGPWatch.dll help
pause
exit /b 1
