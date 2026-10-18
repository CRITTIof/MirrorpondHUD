@echo off
rem Build four single-file exes:
rem   publish\x64, publish\arm64
rem       self-contained: .NET 8 runtime is bundled, target machine needs nothing.
rem   publish\x64-requires-dotnet, publish\arm64-requires-dotnet
rem       framework-dependent: small exe, needs .NET 8 Desktop Runtime installed.
setlocal
cd /d "%~dp0"

where dotnet >nul 2>nul
if errorlevel 1 (
    echo [ERROR] dotnet not found. Install .NET 8 SDK first.
    pause
    exit /b 1
)

rem bin\Release is wiped below; keep the user's settings and log across the build.
set "DATA_BACKUP=%TEMP%\windowoverlay-publish-data"
if exist "%DATA_BACKUP%" rd /s /q "%DATA_BACKUP%" >nul 2>&1
mkdir "%DATA_BACKUP%" >nul 2>&1
for %%F in (settings.json settings.json.bak overlay.log) do (
    if exist "%~dp0bin\Release\%%F" copy /y "%~dp0bin\Release\%%F" "%DATA_BACKUP%\%%F" >nul 2>&1
)

rem Self-contained first, framework-dependent last.
call :build win-arm64 arm64 true
if errorlevel 1 goto :fail

call :build win-x64 x64 true
if errorlevel 1 goto :fail

call :build win-arm64 arm64-requires-dotnet false
if errorlevel 1 goto :fail

call :build win-x64 x64-requires-dotnet false
if errorlevel 1 goto :fail

rem publish leaves a self-contained build in bin\Release; restore the normal
rem x64 build there so run.bat keeps deploying a small framework-dependent copy.
echo.
echo [INFO] Restoring x64 build in bin\Release for run.bat ...
if exist "%~dp0bin" rd /s /q "%~dp0bin" >nul 2>&1
if exist "%~dp0obj" rd /s /q "%~dp0obj" >nul 2>&1
dotnet build WindowOverlay.csproj -c Release -nologo
if errorlevel 1 goto :fail
if exist "%~dp0sign.ps1" (
    powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0sign.ps1"
)

rem Put the user's settings and log back into the rebuilt bin\Release.
for %%F in (settings.json settings.json.bak overlay.log) do (
    if exist "%DATA_BACKUP%\%%F" copy /y "%DATA_BACKUP%\%%F" "%~dp0bin\Release\%%F" >nul 2>&1
)

echo.
echo [OK] Four single-file exes:
echo      publish\arm64\WindowOverlay.exe                   self-contained, no .NET needed
echo      publish\x64\WindowOverlay.exe                     self-contained, no .NET needed
echo      publish\arm64-requires-dotnet\WindowOverlay.exe   needs .NET 8 Desktop Runtime
echo      publish\x64-requires-dotnet\WindowOverlay.exe     needs .NET 8 Desktop Runtime
pause
exit /b 0

:build
echo.
echo [INFO] Publishing %~1 (self-contained=%~3) ...

rem All RIDs share bin\Release / obj (AppendRuntimeIdentifierToOutputPath=false),
rem so a previous architecture's apphost could be reused. Wipe both first.
if exist "%~dp0bin" rd /s /q "%~dp0bin" >nul 2>&1
if exist "%~dp0obj" rd /s /q "%~dp0obj" >nul 2>&1

if /i "%~3"=="true" (
    dotnet publish WindowOverlay.csproj -c Release -r %~1 --self-contained true ^
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true ^
        -p:EnableCompressionInSingleFile=true -p:DebugType=None -p:DebugSymbols=false ^
        -o "publish\%~2"
) else (
    dotnet publish WindowOverlay.csproj -c Release -r %~1 --self-contained false ^
        -p:PublishSingleFile=true -p:DebugType=None -p:DebugSymbols=false ^
        -o "publish\%~2"
)
if errorlevel 1 exit /b 1

rem Sign the single-file exe as well (same self-signed cert as build.bat).
if exist "%~dp0sign.ps1" (
    echo [INFO] Signing publish\%~2\WindowOverlay.exe ...
    powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0sign.ps1" -Exe "%~dp0publish\%~2\WindowOverlay.exe"
)
exit /b 0

:fail
echo.
echo [ERROR] Publish failed.
pause
exit /b 1
