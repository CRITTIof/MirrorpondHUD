@echo off
setlocal
cd /d "%~dp0"

rem Build once (if needed) and run in place.
rem Settings and log live next to the exe:
rem   bin\Release\settings.json
rem   bin\Release\overlay.log
set "EXE=%~dp0bin\Release\WindowOverlay.exe"

if not exist "%EXE%" (
    where dotnet >nul 2>nul
    if errorlevel 1 (
        echo [ERROR] Not built yet, and dotnet was not found.
        echo         Install .NET 8 SDK: https://dotnet.microsoft.com/download/dotnet/8.0
        pause
        exit /b 1
    )
    echo [INFO] First run, building...
    call "%~dp0build.bat" -auto
    if errorlevel 1 (
        echo [ERROR] Build failed.
        pause
        exit /b 1
    )
)

if not exist "%EXE%" (
    echo [ERROR] "%EXE%" not found.
    pause
    exit /b 1
)

start "" "%EXE%" %*
