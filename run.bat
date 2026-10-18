@echo off
setlocal
cd /d "%~dp0"

rem ------------------------------------------------------------------
rem  Why deploy before running?
rem  When the program is started from a restricted folder (AI workspace,
rem  sandbox, some managed locations), Windows can drop the process to
rem  Low integrity. Tray icon, global hotkeys and keyboard hooks are then
rem  rejected by the OS. Running from %LOCALAPPDATA%\Programs avoids this.
rem ------------------------------------------------------------------

set "SRC=%~dp0bin\Release"
set "DEST=%LOCALAPPDATA%\Programs\WindowOverlay"

if not exist "%SRC%\WindowOverlay.exe" (
    where dotnet >nul 2>nul
    if errorlevel 1 (
        echo [ERROR] Not built yet, and dotnet was not found.
        echo         Install .NET 8 SDK: https://dotnet.microsoft.com/download/dotnet/8.0
        pause
        exit /b 1
    )
    echo [INFO] First run, building...
    dotnet build "WindowOverlay.csproj" -c Release -nologo
    if errorlevel 1 (
        echo [ERROR] Build failed.
        pause
        exit /b 1
    )
)

if not exist "%DEST%" mkdir "%DEST%" >nul 2>&1

copy /y "%SRC%\WindowOverlay.exe"                "%DEST%\" >nul 2>&1
copy /y "%SRC%\WindowOverlay.dll"                "%DEST%\" >nul 2>&1
copy /y "%SRC%\WindowOverlay.deps.json"          "%DEST%\" >nul 2>&1
copy /y "%SRC%\WindowOverlay.runtimeconfig.json" "%DEST%\" >nul 2>&1

if not exist "%DEST%\WindowOverlay.exe" (
    echo [ERROR] Failed to copy to "%DEST%".
    pause
    exit /b 1
)

start "" "%DEST%\WindowOverlay.exe" %*
