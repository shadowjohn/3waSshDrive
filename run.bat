@echo off
setlocal EnableExtensions

set "PROJECT_ROOT=%~dp0"
set "APP_PATH=%PROJECT_ROOT%src\3waSshDrive.App\bin\Release\net472\3waSshDrive.exe"

if not exist "%APP_PATH%" (
    echo [3waSshDrive] Release build not found. Building it now...
    call "%PROJECT_ROOT%build.bat" --no-pause
    if errorlevel 1 goto :failed
)

if not exist "%APP_PATH%" (
    echo [3waSshDrive] Build completed, but the application was not found:
    echo %APP_PATH%
    goto :failed
)

if /I "%~1"=="--check" (
    echo [3waSshDrive] Running self-check: %APP_PATH%
    start "" /wait "%APP_PATH%" --self-check
    if errorlevel 1 goto :failed
    echo [3waSshDrive] Self-check passed.
    exit /b 0
)

pushd "%PROJECT_ROOT%" >nul || goto :failed
start "" "%APP_PATH%"
if errorlevel 1 (
    popd >nul
    goto :failed
)
popd >nul
exit /b 0

:failed
echo.
echo [3waSshDrive] Unable to start the application.
if not defined CI pause
exit /b 1
