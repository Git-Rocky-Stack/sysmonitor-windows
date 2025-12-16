@echo off
echo ============================================
echo   STX1 System Monitor - Installer Builder
echo ============================================
echo.

:: Check for Inno Setup
set ISCC=
if exist "%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe" (
    set "ISCC=%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe"
) else if exist "%ProgramFiles%\Inno Setup 6\ISCC.exe" (
    set "ISCC=%ProgramFiles%\Inno Setup 6\ISCC.exe"
) else (
    echo ERROR: Inno Setup 6 is not installed!
    echo.
    echo Please download and install Inno Setup from:
    echo https://jrsoftware.org/isdl.php
    echo.
    pause
    exit /b 1
)

echo Found Inno Setup at: %ISCC%
echo.

:: Build the installer
echo Building installer...
"%ISCC%" "%~dp0SysMonitorSetup.iss"

if %ERRORLEVEL% EQU 0 (
    echo.
    echo ============================================
    echo   Installer built successfully!
    echo ============================================
    echo.
    echo Output: ..\publish\installer\STX1-SystemMonitor-Setup-1.0.0.exe
    echo.
) else (
    echo.
    echo ERROR: Installer build failed!
    echo.
)

pause
