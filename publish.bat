@echo off
rem Build four single-file exes:
rem   publish\x64, publish\arm64
rem       self-contained: .NET 8 runtime is bundled, target machine needs nothing.
rem   publish\x64-requires-dotnet, publish\arm64-requires-dotnet
rem       framework-dependent: small exe, needs .NET 8 Desktop Runtime installed.
rem
rem X-5：旧版在发布前 rd /s /q bin obj，把用户 settings.json / 日志 / 便携插件
rem 先搬到 %TEMP% 再恢复。中途失败或断电时用户数据只剩 TEMP 一份；应用正在运行时
rem 删除还会静默失败（2>nul），下一轮发布就可能混架构。
rem 现在：
rem   - 用户数据始终留在 bin\Release，不再经过 TEMP。
rem   - 每个 RID 发布前只清理构建产物（obj + bin\Release 里的 exe/dll/pdb/json），
rem     避免上一架构的 apphost / 缓存被复用。
rem   - 发布前检测 MirrorpondHUD.exe 是否在运行；在运行就明确报错退出，
rem     不再静默删除失败后继续发布。
setlocal
cd /d "%~dp0"

where dotnet >nul 2>nul
if errorlevel 1 (
    echo [ERROR] dotnet not found. Install .NET 8 SDK first.
    pause
    exit /b 1
)

rem 发布要清理 bin\Release 的构建产物；程序在运行时文件被锁，绝不能继续。
call :checkrunning
if errorlevel 1 exit /b 1

rem Self-contained first, framework-dependent last.
call :build win-arm64 arm64 true
if errorlevel 1 goto :fail

call :build win-x64 x64 true
if errorlevel 1 goto :fail

call :build win-arm64 arm64-requires-dotnet false
if errorlevel 1 goto :fail

call :build win-x64 x64-requires-dotnet false
if errorlevel 1 goto :fail

rem 发布过程清掉了 bin\Release 的构建产物；恢复一份正常 x64 构建给 run.bat。
rem 用户 settings.json / 日志 / 插件目录从未被删除，不需要备份恢复。
echo.
echo [INFO] Restoring x64 build in bin\Release for run.bat ...
call :cleanbuild
dotnet build MirrorpondHUD.csproj -c Release -nologo
if errorlevel 1 goto :fail
if exist "%~dp0sign.ps1" (
    powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0sign.ps1"
)

echo.
echo [OK] Four single-file exes:
echo      publish\arm64\MirrorpondHUD.exe                   self-contained, no .NET needed
echo      publish\x64\MirrorpondHUD.exe                     self-contained, no .NET needed
echo      publish\arm64-requires-dotnet\MirrorpondHUD.exe   needs .NET 8 Desktop Runtime
echo      publish\x64-requires-dotnet\MirrorpondHUD.exe     needs .NET 8 Desktop Runtime
pause
exit /b 0

:checkrunning
tasklist /FI "IMAGENAME eq MirrorpondHUD.exe" 2>nul | find /i "MirrorpondHUD.exe" >nul
if not errorlevel 1 (
    echo.
    echo [ERROR] MirrorpondHUD.exe is running. Publish needs to replace build outputs.
    echo         Close the running window first, then publish again.
    exit /b 1
)
exit /b 0

:cleanbuild
rem 只删构建产物：用户数据（settings.json / settings.json.bak / mirrorpond.log*）
rem 和 plugins\cards 下用户自己的插件目录都不匹配，保持原地不动。
if exist "%~dp0obj" rd /s /q "%~dp0obj" >nul 2>&1
if exist "%~dp0bin\Release" (
    del /q "%~dp0bin\Release\*.exe" 2>nul
    del /q "%~dp0bin\Release\*.dll" 2>nul
    del /q "%~dp0bin\Release\*.pdb" 2>nul
    del /q "%~dp0bin\Release\MirrorpondHUD.*.json" 2>nul
)
exit /b 0

:build
echo.
echo [INFO] Publishing %~1 (self-contained=%~3) ...
call :cleanbuild

if /i "%~3"=="true" (
    dotnet publish MirrorpondHUD.csproj -c Release -r %~1 --self-contained true ^
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true ^
        -p:EnableCompressionInSingleFile=true -p:DebugType=None -p:DebugSymbols=false ^
        -o "publish\%~2"
) else (
    dotnet publish MirrorpondHUD.csproj -c Release -r %~1 --self-contained false ^
        -p:PublishSingleFile=true -p:DebugType=None -p:DebugSymbols=false ^
        -o "publish\%~2"
)
if errorlevel 1 exit /b 1

rem Sign the single-file exe as well (same self-signed cert as build.bat).
if exist "%~dp0sign.ps1" (
    echo [INFO] Signing publish\%~2\MirrorpondHUD.exe ...
    powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0sign.ps1" -Exe "%~dp0publish\%~2\MirrorpondHUD.exe"
)
exit /b 0

:fail
echo.
echo [ERROR] Publish failed.
pause
exit /b 1
