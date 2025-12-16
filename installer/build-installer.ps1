# SysMonitor Installer Build Script (PowerShell)
# ============================================

$ErrorActionPreference = "Stop"

Write-Host "============================================================" -ForegroundColor Cyan
Write-Host "         SysMonitor Installer Build Script" -ForegroundColor Cyan
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host ""

# Find Inno Setup - check multiple possible locations
$isccPaths = @(
    "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
    "C:\Program Files\Inno Setup 6\ISCC.exe",
    "C:\Program Files (x86)\Inno Setup 5\ISCC.exe",
    "C:\Program Files\Inno Setup 5\ISCC.exe",
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "$env:USERPROFILE\scoop\apps\innosetup\current\ISCC.exe",
    "$env:ProgramData\chocolatey\lib\InnoSetup\tools\ISCC.exe"
)

Write-Host "Searching for Inno Setup..." -ForegroundColor White

$isccPath = $null
foreach ($path in $isccPaths) {
    Write-Host "  Checking: $path" -ForegroundColor Gray
    if (Test-Path $path) {
        $isccPath = $path
        break
    }
}

# If not found in standard locations, search Program Files
if (-not $isccPath) {
    Write-Host "  Searching Program Files..." -ForegroundColor Gray
    $searchPaths = @("C:\Program Files", "C:\Program Files (x86)")
    foreach ($searchPath in $searchPaths) {
        $found = Get-ChildItem -Path $searchPath -Filter "ISCC.exe" -Recurse -ErrorAction SilentlyContinue -Depth 3 | Select-Object -First 1
        if ($found) {
            $isccPath = $found.FullName
            break
        }
    }
}

if (-not $isccPath) {
    Write-Host ""
    Write-Host "[ERROR] Inno Setup not found!" -ForegroundColor Red
    Write-Host ""
    Write-Host "Please install Inno Setup 6 from:" -ForegroundColor Yellow
    Write-Host "https://jrsoftware.org/isdl.php" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "Or via winget:" -ForegroundColor Yellow
    Write-Host "winget install JRSoftware.InnoSetup" -ForegroundColor White
    Write-Host ""
    Write-Host "After installation, you may need to restart your terminal." -ForegroundColor Yellow
    Write-Host ""
    Read-Host "Press Enter to exit"
    exit 1
}

Write-Host "[OK] Found Inno Setup at: $isccPath" -ForegroundColor Green
Write-Host ""

# Check required files
Write-Host "Checking required files..." -ForegroundColor White

$requiredFiles = @("LICENSE.rtf", "README_BEFORE.txt", "README_AFTER.txt", "SysMonitor.iss")
foreach ($file in $requiredFiles) {
    if (-not (Test-Path $file)) {
        Write-Host "[ERROR] $file not found!" -ForegroundColor Red
        Read-Host "Press Enter to exit"
        exit 1
    }
    Write-Host "[OK] $file" -ForegroundColor Green
}

if (-not (Test-Path "installer_icon.ico")) {
    Write-Host "[WARNING] installer_icon.ico not found - using default icon" -ForegroundColor Yellow
    Write-Host "         Create a 256x256 .ico file for a custom installer icon" -ForegroundColor Yellow
}

# Check/build application
$buildPath = "..\src\SysMonitor.App\bin\x64\Release\net8.0-windows10.0.22621.0\win-x64"
$exePath = Join-Path $buildPath "SysMonitor.App.exe"

if (-not (Test-Path $exePath)) {
    Write-Host ""
    Write-Host "[WARNING] Release build not found!" -ForegroundColor Yellow
    Write-Host "Building application first..." -ForegroundColor White
    Write-Host ""

    Push-Location ..
    try {
        & dotnet publish src\SysMonitor.App\SysMonitor.App.csproj -c Release -r win-x64 --self-contained true
        if ($LASTEXITCODE -ne 0) {
            throw "Build failed"
        }
    }
    catch {
        Write-Host "[ERROR] Build failed!" -ForegroundColor Red
        Pop-Location
        Read-Host "Press Enter to exit"
        exit 1
    }
    Pop-Location

    Write-Host ""
    Write-Host "[OK] Build completed successfully" -ForegroundColor Green
}

Write-Host "[OK] Application build found" -ForegroundColor Green
Write-Host ""

# Create output directory
if (-not (Test-Path "output")) {
    New-Item -ItemType Directory -Path "output" | Out-Null
}

# Compile installer
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host "Compiling installer..." -ForegroundColor White
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host ""

& $isccPath "SysMonitor.iss"

if ($LASTEXITCODE -ne 0) {
    Write-Host ""
    Write-Host "[ERROR] Installer compilation failed!" -ForegroundColor Red
    Read-Host "Press Enter to exit"
    exit 1
}

Write-Host ""
Write-Host "============================================================" -ForegroundColor Green
Write-Host "[SUCCESS] Installer created successfully!" -ForegroundColor Green
Write-Host ""
Write-Host "Output: $PWD\output\SysMonitor_Setup_1.0.0.exe" -ForegroundColor White
Write-Host "============================================================" -ForegroundColor Green
Write-Host ""

# Open output folder
Start-Process explorer.exe -ArgumentList "output"

Read-Host "Press Enter to exit"
