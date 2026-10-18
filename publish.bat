@echo off
rem Publish a self-contained single-file exe (target machine needs no .NET).
setlocal
cd /d "%~dp0"

where dotnet >nul 2>nul
if errorlevel 1 (
    echo [ERROR] dotnet not found. Install .NET 8 SDK first.
    pause
    exit /b 1
)

dotnet publish WindowOverlay.csproj -c Release -r win-x64 --self-contained true ^
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true ^
    -p:EnableCompressionInSingleFile=true ^
    -o publish

if errorlevel 1 (
    echo [ERROR] Publish failed.
    pause
    exit /b 1
)

echo.
echo [OK] Single-file exe: %~dp0publish\WindowOverlay.exe
echo      Keep it outside restricted folders (see README).
pause
