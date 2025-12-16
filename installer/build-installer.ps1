# STX1 System Monitor - Installer Build Script
# This script builds the installer using Inno Setup

param(
    [switch]$InstallInnoSetup,
    [switch]$SkipPublish
)

$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RootDir = Split-Path -Parent $ScriptDir

Write-Host "============================================" -ForegroundColor Cyan
Write-Host "  STX1 System Monitor - Installer Builder" -ForegroundColor Cyan
Write-Host "============================================" -ForegroundColor Cyan
Write-Host ""

# Step 1: Publish the application if not skipped
if (-not $SkipPublish) {
    Write-Host "Step 1: Publishing application..." -ForegroundColor Yellow
    Push-Location $RootDir
    try {
        dotnet publish src/SysMonitor.App/SysMonitor.App.csproj `
            -c Release `
            -r win-x64 `
            -p:Platform=x64 `
            --self-contained true `
            -o publish/installer-build
        
        if ($LASTEXITCODE -ne 0) {
            throw "Publish failed!"
        }
        Write-Host "  Published successfully!" -ForegroundColor Green
    }
    finally {
        Pop-Location
    }
}
else {
    Write-Host "Step 1: Skipping publish (using existing build)" -ForegroundColor Yellow
}

# Step 2: Check for Inno Setup
Write-Host ""
Write-Host "Step 2: Checking for Inno Setup..." -ForegroundColor Yellow

$InnoSetupPaths = @(
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles}\Inno Setup 6\ISCC.exe"
)

$ISCC = $null
foreach ($path in $InnoSetupPaths) {
    if (Test-Path $path) {
        $ISCC = $path
        break
    }
}

if (-not $ISCC) {
    Write-Host "  Inno Setup 6 not found!" -ForegroundColor Red
    
    if ($InstallInnoSetup) {
        Write-Host "  Downloading Inno Setup..." -ForegroundColor Yellow
        $installerUrl = "https://jrsoftware.org/download.php/is.exe"
        $installerPath = "$env:TEMP\innosetup_installer.exe"
        
        Invoke-WebRequest -Uri $installerUrl -OutFile $installerPath
        Write-Host "  Installing Inno Setup (requires admin)..." -ForegroundColor Yellow
        Start-Process -FilePath $installerPath -ArgumentList "/VERYSILENT", "/NORESTART" -Wait -Verb RunAs
        Remove-Item $installerPath -Force
        
        # Check again
        foreach ($path in $InnoSetupPaths) {
            if (Test-Path $path) {
                $ISCC = $path
                break
            }
        }
    }
    
    if (-not $ISCC) {
        Write-Host ""
        Write-Host "Please install Inno Setup 6 from: https://jrsoftware.org/isdl.php" -ForegroundColor Yellow
        Write-Host "Or run this script with -InstallInnoSetup parameter" -ForegroundColor Yellow
        exit 1
    }
}

Write-Host "  Found: $ISCC" -ForegroundColor Green

# Step 3: Build the installer
Write-Host ""
Write-Host "Step 3: Building installer..." -ForegroundColor Yellow

$issFile = Join-Path $ScriptDir "SysMonitorSetup.iss"
& $ISCC $issFile

if ($LASTEXITCODE -eq 0) {
    Write-Host ""
    Write-Host "============================================" -ForegroundColor Green
    Write-Host "  Installer built successfully!" -ForegroundColor Green
    Write-Host "============================================" -ForegroundColor Green
    Write-Host ""
    
    $outputFile = Join-Path $RootDir "publish\installer\STX1-SystemMonitor-Setup-1.0.0.exe"
    if (Test-Path $outputFile) {
        $fileInfo = Get-Item $outputFile
        Write-Host "Output: $outputFile" -ForegroundColor Cyan
        Write-Host "Size: $([math]::Round($fileInfo.Length / 1MB, 2)) MB" -ForegroundColor Cyan
    }
}
else {
    Write-Host ""
    Write-Host "ERROR: Installer build failed!" -ForegroundColor Red
    exit 1
}
