@echo off
setlocal
cd /d "%~dp0"

where dotnet >nul 2>nul
if errorlevel 1 (
    echo [ERROR] dotnet not found.
    echo         Install .NET 8 SDK: https://dotnet.microsoft.com/download/dotnet/8.0
    pause
    exit /b 1
)

dotnet build "MirrorpondHUD.csproj" -c Release -nologo
if errorlevel 1 (
    echo.
    echo [ERROR] Build failed.
    pause
    exit /b 1
)

rem Sign the exe so Windows shows a publisher name instead of "unknown publisher".
rem First run creates a self-signed code-signing cert in the current user's store.
if exist "%~dp0sign.ps1" (
    echo.
    echo [INFO] Signing...
    powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0sign.ps1"
    if errorlevel 1 (
        echo [WARN] Signing failed. The app still runs, but Windows may show "unknown publisher".
    )
)

echo.
echo [OK] Output: %~dp0bin\Release\
echo      Double-click run.bat to start.
if not "%~1"=="-auto" pause
