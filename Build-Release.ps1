<#
.SYNOPSIS
    Master build script for STX1 System Monitor release.

.DESCRIPTION
    This script performs a complete release build:
    1. Builds and publishes the application
    2. Signs the executables (optional)
    3. Builds the installer (requires Inno Setup)
    4. Signs the installer (optional)
    5. Creates a portable ZIP

.PARAMETER SignCode
    Sign the application and installer with a code signing certificate.

.PARAMETER CertificatePath
    Path to the PFX certificate file for signing.

.PARAMETER CertificatePassword
    Password for the PFX certificate.

.PARAMETER UseSelfSignedCert
    Use a self-signed certificate for testing (will show SmartScreen warnings).

.PARAMETER SkipInstaller
    Skip building the installer (only create portable ZIP).

.EXAMPLE
    # Full release build without signing:
    .\Build-Release.ps1

.EXAMPLE
    # Full release build with self-signed certificate (for testing):
    .\Build-Release.ps1 -SignCode -UseSelfSignedCert

.EXAMPLE
    # Full release build with commercial certificate:
    .\Build-Release.ps1 -SignCode -CertificatePath ".\cert.pfx" -CertificatePassword "password"
#>

param(
    [switch]$SignCode,
    [string]$CertificatePath,
    [string]$CertificatePassword,
    [switch]$UseSelfSignedCert,
    [switch]$SkipInstaller
)

$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path

# Configuration
$AppName = "STX1 System Monitor"
$AppVersion = "1.0.0"
$PublishDir = Join-Path $ScriptDir "publish\installer-build"
$InstallerDir = Join-Path $ScriptDir "publish\installer"
$TimestampServer = "http://timestamp.digicert.com"

Write-Host ""
Write-Host "================================================================" -ForegroundColor Cyan
Write-Host "       $AppName - Release Build Script" -ForegroundColor Cyan
Write-Host "                    Version $AppVersion" -ForegroundColor Cyan
Write-Host "================================================================" -ForegroundColor Cyan
Write-Host ""

$StartTime = Get-Date

# ============================================================================
# Step 1: Clean and Publish
# ============================================================================
Write-Host "[1/5] Publishing application..." -ForegroundColor Yellow

if (Test-Path $PublishDir) {
    Remove-Item $PublishDir -Recurse -Force
}

$publishResult = dotnet publish "$ScriptDir\src\SysMonitor.App\SysMonitor.App.csproj" `
    -c Release `
    -r win-x64 `
    -p:Platform=x64 `
    --self-contained true `
    -o $PublishDir 2>&1

if ($LASTEXITCODE -ne 0) {
    Write-Host "ERROR: Publish failed!" -ForegroundColor Red
    Write-Host $publishResult -ForegroundColor Red
    exit 1
}

Write-Host "      Published successfully!" -ForegroundColor Green

# ============================================================================
# Step 2: Sign Application (Optional)
# ============================================================================
if ($SignCode) {
    Write-Host ""
    Write-Host "[2/5] Signing application files..." -ForegroundColor Yellow

    # Find SignTool
    $SignTool = $null
    $SignToolPaths = @(
        "${env:ProgramFiles(x86)}\Windows Kits\10\bin\10.0.22621.0\x64\signtool.exe",
        "${env:ProgramFiles(x86)}\Windows Kits\10\bin\10.0.19041.0\x64\signtool.exe"
    )

    foreach ($path in $SignToolPaths) {
        if (Test-Path $path) {
            $SignTool = $path
            break
        }
    }

    if (-not $SignTool) {
        $cmd = Get-Command signtool.exe -ErrorAction SilentlyContinue
        if ($cmd) { $SignTool = $cmd.Source }
    }

    if (-not $SignTool) {
        Write-Host "      WARNING: signtool.exe not found. Skipping signing." -ForegroundColor Yellow
    }
    else {
        $FilesToSign = @(
            (Join-Path $PublishDir "SysMonitor.App.exe"),
            (Join-Path $PublishDir "SysMonitor.Core.dll")
        )

        $Certificate = $null

        if ($UseSelfSignedCert) {
            # Create or get self-signed certificate
            $CertName = "STX1 System Monitor (Development)"
            $CertStore = "Cert:\CurrentUser\My"
            $Certificate = Get-ChildItem $CertStore | Where-Object { $_.Subject -like "*$CertName*" } | Select-Object -First 1

            if (-not $Certificate) {
                $Certificate = New-SelfSignedCertificate `
                    -Type CodeSigningCert `
                    -Subject "CN=$CertName, O=Rocky Stack" `
                    -KeyUsage DigitalSignature `
                    -CertStoreLocation $CertStore `
                    -NotAfter (Get-Date).AddYears(3)
                Write-Host "      Created self-signed certificate" -ForegroundColor Green
            }
        }

        foreach ($file in $FilesToSign) {
            if (Test-Path $file) {
                $fileName = Split-Path $file -Leaf
                Write-Host "      Signing: $fileName" -ForegroundColor Gray

                try {
                    if ($UseSelfSignedCert -and $Certificate) {
                        & $SignTool sign /sha1 $Certificate.Thumbprint /fd SHA256 /t $TimestampServer "$file" 2>&1 | Out-Null
                    }
                    elseif ($CertificatePath) {
                        & $SignTool sign /f "$CertificatePath" /p "$CertificatePassword" /fd SHA256 /t $TimestampServer "$file" 2>&1 | Out-Null
                    }

                    if ($LASTEXITCODE -eq 0) {
                        Write-Host "        Signed!" -ForegroundColor Green
                    }
                }
                catch {
                    Write-Host "        Warning: $($_.Exception.Message)" -ForegroundColor Yellow
                }
            }
        }
    }
}
else {
    Write-Host ""
    Write-Host "[2/5] Skipping code signing (use -SignCode to enable)" -ForegroundColor DarkGray
}

# ============================================================================
# Step 3: Build Installer
# ============================================================================
if (-not $SkipInstaller) {
    Write-Host ""
    Write-Host "[3/5] Building installer..." -ForegroundColor Yellow

    # Find Inno Setup
    $ISCC = $null
    $ISCCPaths = @(
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "${env:ProgramFiles}\Inno Setup 6\ISCC.exe"
    )

    foreach ($path in $ISCCPaths) {
        if (Test-Path $path) {
            $ISCC = $path
            break
        }
    }

    if (-not $ISCC) {
        Write-Host "      WARNING: Inno Setup not found. Skipping installer." -ForegroundColor Yellow
        Write-Host "      Download from: https://jrsoftware.org/isdl.php" -ForegroundColor Gray
    }
    else {
        if (-not (Test-Path $InstallerDir)) {
            New-Item -ItemType Directory -Path $InstallerDir -Force | Out-Null
        }

        $issFile = Join-Path $ScriptDir "installer\SysMonitorSetup.iss"
        & $ISCC "$issFile" 2>&1 | Out-Null

        if ($LASTEXITCODE -eq 0) {
            Write-Host "      Installer built successfully!" -ForegroundColor Green

            # Sign installer if requested
            if ($SignCode -and $SignTool) {
                Write-Host ""
                Write-Host "[4/5] Signing installer..." -ForegroundColor Yellow

                $installerPath = Join-Path $InstallerDir "STX1-SystemMonitor-Setup-$AppVersion.exe"

                if (Test-Path $installerPath) {
                    try {
                        if ($UseSelfSignedCert -and $Certificate) {
                            & $SignTool sign /sha1 $Certificate.Thumbprint /fd SHA256 /t $TimestampServer "$installerPath" 2>&1 | Out-Null
                        }
                        elseif ($CertificatePath) {
                            & $SignTool sign /f "$CertificatePath" /p "$CertificatePassword" /fd SHA256 /t $TimestampServer "$installerPath" 2>&1 | Out-Null
                        }

                        if ($LASTEXITCODE -eq 0) {
                            Write-Host "      Installer signed!" -ForegroundColor Green
                        }
                    }
                    catch {
                        Write-Host "      Warning: $($_.Exception.Message)" -ForegroundColor Yellow
                    }
                }
            }
            else {
                Write-Host ""
                Write-Host "[4/5] Skipping installer signing" -ForegroundColor DarkGray
            }
        }
        else {
            Write-Host "      WARNING: Installer build failed!" -ForegroundColor Yellow
        }
    }
}
else {
    Write-Host ""
    Write-Host "[3/5] Skipping installer (use without -SkipInstaller to build)" -ForegroundColor DarkGray
    Write-Host "[4/5] Skipping installer signing" -ForegroundColor DarkGray
}

# ============================================================================
# Step 5: Create Portable ZIP
# ============================================================================
Write-Host ""
Write-Host "[5/5] Creating portable ZIP..." -ForegroundColor Yellow

$ZipPath = Join-Path $ScriptDir "publish\STX1-SystemMonitor-$AppVersion-Portable-x64.zip"

if (Test-Path $ZipPath) {
    Remove-Item $ZipPath -Force
}

Compress-Archive -Path "$PublishDir\*" -DestinationPath $ZipPath -Force
Write-Host "      Portable ZIP created!" -ForegroundColor Green

# ============================================================================
# Summary
# ============================================================================
$EndTime = Get-Date
$Duration = $EndTime - $StartTime

Write-Host ""
Write-Host "================================================================" -ForegroundColor Green
Write-Host "                    BUILD COMPLETE!" -ForegroundColor Green
Write-Host "================================================================" -ForegroundColor Green
Write-Host ""
Write-Host "  Duration: $($Duration.ToString('mm\:ss'))" -ForegroundColor Cyan
Write-Host ""
Write-Host "  Output Files:" -ForegroundColor Cyan

$ZipFile = Get-Item $ZipPath -ErrorAction SilentlyContinue
if ($ZipFile) {
    Write-Host "    - Portable ZIP: $([math]::Round($ZipFile.Length / 1MB, 1)) MB" -ForegroundColor White
    Write-Host "      $ZipPath" -ForegroundColor Gray
}

$InstallerFile = Get-Item (Join-Path $InstallerDir "STX1-SystemMonitor-Setup-$AppVersion.exe") -ErrorAction SilentlyContinue
if ($InstallerFile) {
    Write-Host "    - Installer: $([math]::Round($InstallerFile.Length / 1MB, 1)) MB" -ForegroundColor White
    Write-Host "      $($InstallerFile.FullName)" -ForegroundColor Gray
}

Write-Host ""

if ($SignCode) {
    if ($UseSelfSignedCert) {
        Write-Host "  NOTE: Self-signed certificate used - SmartScreen warnings expected" -ForegroundColor Yellow
    }
    else {
        Write-Host "  Code signed with commercial certificate" -ForegroundColor Green
    }
}
else {
    Write-Host "  NOTE: Code not signed - use -SignCode for signed builds" -ForegroundColor Yellow
}

Write-Host ""
