@echo off
REM Start MySQL if not running, then Jadeer app
set MYSQLD="C:\Program Files\MySQL\MySQL Server 8.4\bin\mysqld.exe"
set DATADIR=C:\ProgramData\MySQL\MySQL Server 8.4\Data
set BASEDIR=C:\Program Files\MySQL\MySQL Server 8.4

tasklist /FI "IMAGENAME eq mysqld.exe" 2>NUL | find /I "mysqld.exe" >NUL
if errorlevel 1 (
  echo Starting MySQL...
  start "" /B %MYSQLD% --datadir="%DATADIR%" --basedir="%BASEDIR%" --port=3306 --bind-address=127.0.0.1
  timeout /t 5 /nobreak >NUL
)

cd /d "%~dp0"
dotnet run --urls http://127.0.0.1:5088
