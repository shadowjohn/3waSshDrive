@echo off
setlocal EnableExtensions

set "PROJECT_ROOT=%~dp0"
set "NO_PAUSE="
if /I "%~1"=="--no-pause" set "NO_PAUSE=1"
set "MSBUILD_VERSION_ARGS="
if defined THREEWA_DISPLAY_VERSION if defined THREEWA_PACKAGE_VERSION if defined THREEWA_ASSEMBLY_VERSION (
    set "MSBUILD_VERSION_ARGS=/p:ThreeWaDisplayVersion=%THREEWA_DISPLAY_VERSION% /p:ThreeWaPackageVersion=%THREEWA_PACKAGE_VERSION% /p:ThreeWaAssemblyVersion=%THREEWA_ASSEMBLY_VERSION%"
)

pushd "%PROJECT_ROOT%" >nul || goto :failed

where git >nul 2>nul
if errorlevel 1 (
    echo [3waSshDrive] Git was not found in PATH.
    goto :failed
)

where dotnet >nul 2>nul
if errorlevel 1 (
    echo [3waSshDrive] dotnet was not found in PATH.
    goto :failed
)

echo [3waSshDrive] Initializing pinned source dependencies...
git submodule update --init
if errorlevel 1 goto :failed

echo [3waSshDrive] Restoring packages...
dotnet restore ".\3waSshDrive.sln" --nologo
if errorlevel 1 goto :failed

echo [3waSshDrive] Running Release tests...
dotnet test ".\3waSshDrive.sln" --configuration Release --no-restore --nologo --logger "console;verbosity=normal" %MSBUILD_VERSION_ARGS%
if errorlevel 1 goto :failed

echo [3waSshDrive] Building the WinForms application...
dotnet build ".\src\3waSshDrive.App\3waSshDrive.App.csproj" --configuration Release --no-restore --nologo %MSBUILD_VERSION_ARGS%
if errorlevel 1 goto :failed

echo.
echo [3waSshDrive] Build succeeded.
echo [3waSshDrive] Run: run.bat
popd >nul
if not defined CI if not defined NO_PAUSE pause
exit /b 0

:failed
echo.
echo [3waSshDrive] Build failed. Review the error above.
popd >nul 2>nul
if not defined CI if not defined NO_PAUSE pause
exit /b 1
