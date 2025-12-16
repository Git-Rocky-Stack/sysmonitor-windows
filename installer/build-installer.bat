@echo off
setlocal enabledelayedexpansion

echo ============================================================
echo          SysMonitor Installer Build Script
echo ============================================================
echo.

:: Check for Inno Setup installation
set "ISCC_PATH="

:: Common Inno Setup installation paths
if exist "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" (
    set "ISCC_PATH=C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
)
if exist "C:\Program Files\Inno Setup 6\ISCC.exe" (
    set "ISCC_PATH=C:\Program Files\Inno Setup 6\ISCC.exe"
)

if "%ISCC_PATH%"=="" (
    echo [ERROR] Inno Setup 6 not found!
    echo.
    echo Please install Inno Setup 6 from:
    echo https://jrsoftware.org/isdl.php
    echo.
    echo After installation, run this script again.
    pause
    exit /b 1
)

echo [OK] Found Inno Setup at: %ISCC_PATH%
echo.

:: Check for required files
echo Checking required files...

if not exist "LICENSE.rtf" (
    echo [ERROR] LICENSE.rtf not found!
    pause
    exit /b 1
)
echo [OK] LICENSE.rtf

if not exist "README_BEFORE.txt" (
    echo [ERROR] README_BEFORE.txt not found!
    pause
    exit /b 1
)
echo [OK] README_BEFORE.txt

if not exist "README_AFTER.txt" (
    echo [ERROR] README_AFTER.txt not found!
    pause
    exit /b 1
)
echo [OK] README_AFTER.txt

if not exist "installer_icon.ico" (
    echo [WARNING] installer_icon.ico not found - using default icon
    echo          Create a 256x256 .ico file for a custom installer icon
)

:: Check if build exists
set "BUILD_PATH=..\src\SysMonitor.App\bin\x64\Release\net8.0-windows10.0.22621.0\win-x64"
if not exist "%BUILD_PATH%\SysMonitor.App.exe" (
    echo.
    echo [WARNING] Release build not found at:
    echo          %BUILD_PATH%
    echo.
    echo Building application first...
    echo.

    pushd ..
    dotnet publish src\SysMonitor.App\SysMonitor.App.csproj -c Release -r win-x64 --self-contained true
    if errorlevel 1 (
        echo [ERROR] Build failed!
        popd
        pause
        exit /b 1
    )
    popd
    echo.
    echo [OK] Build completed successfully
)

echo [OK] Application build found
echo.

:: Create output directory
if not exist "output" mkdir output

:: Compile the installer
echo ============================================================
echo Compiling installer...
echo ============================================================
echo.

"%ISCC_PATH%" SysMonitor.iss

if errorlevel 1 (
    echo.
    echo [ERROR] Installer compilation failed!
    pause
    exit /b 1
)

echo.
echo ============================================================
echo [SUCCESS] Installer created successfully!
echo.
echo Output: %CD%\output\SysMonitor_Setup_1.0.0.exe
echo ============================================================
echo.

:: Open output folder
explorer output

pause
