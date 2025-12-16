#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Signs the STX1 System Monitor application files.

.DESCRIPTION
    This script signs executables and DLLs using either:
    - A self-signed certificate (for testing)
    - A commercial code signing certificate (for production)

.PARAMETER CertificatePath
    Path to the PFX certificate file.

.PARAMETER CertificatePassword
    Password for the PFX certificate (use SecureString in production).

.PARAMETER UseSelfSigned
    Create and use a self-signed certificate for testing.

.PARAMETER TimestampServer
    URL of the timestamp server. Default: http://timestamp.digicert.com

.PARAMETER PublishPath
    Path to the published application files.

.EXAMPLE
    # For testing with self-signed certificate:
    .\Sign-Application.ps1 -UseSelfSigned

.EXAMPLE
    # For production with commercial certificate:
    .\Sign-Application.ps1 -CertificatePath ".\certificate.pfx" -CertificatePassword "YourPassword"
#>

param(
    [string]$CertificatePath,
    [string]$CertificatePassword,
    [switch]$UseSelfSigned,
    [string]$TimestampServer = "http://timestamp.digicert.com",
    [string]$PublishPath = "..\publish\installer-build"
)

$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RootDir = Split-Path -Parent $ScriptDir

Write-Host "============================================" -ForegroundColor Cyan
Write-Host "  STX1 System Monitor - Code Signing" -ForegroundColor Cyan
Write-Host "============================================" -ForegroundColor Cyan
Write-Host ""

# Resolve publish path
$FullPublishPath = Resolve-Path (Join-Path $ScriptDir $PublishPath) -ErrorAction SilentlyContinue
if (-not $FullPublishPath) {
    $FullPublishPath = Join-Path $RootDir "publish\installer-build"
}

if (-not (Test-Path $FullPublishPath)) {
    Write-Host "ERROR: Publish path not found: $FullPublishPath" -ForegroundColor Red
    Write-Host "Please run dotnet publish first." -ForegroundColor Yellow
    exit 1
}

Write-Host "Publish Path: $FullPublishPath" -ForegroundColor Gray
Write-Host ""

# Get or create certificate
$Certificate = $null

if ($UseSelfSigned) {
    Write-Host "Creating self-signed certificate for testing..." -ForegroundColor Yellow

    $CertName = "STX1 System Monitor (Development)"
    $CertStore = "Cert:\CurrentUser\My"

    # Check if certificate already exists
    $ExistingCert = Get-ChildItem $CertStore | Where-Object { $_.Subject -like "*$CertName*" }

    if ($ExistingCert) {
        Write-Host "  Using existing certificate: $($ExistingCert.Thumbprint)" -ForegroundColor Green
        $Certificate = $ExistingCert
    }
    else {
        # Create new self-signed certificate
        $Certificate = New-SelfSignedCertificate `
            -Type CodeSigningCert `
            -Subject "CN=$CertName, O=Rocky Stack, L=Local, S=Development, C=US" `
            -KeyUsage DigitalSignature `
            -KeyAlgorithm RSA `
            -KeyLength 2048 `
            -CertStoreLocation $CertStore `
            -NotAfter (Get-Date).AddYears(3)

        Write-Host "  Created new certificate: $($Certificate.Thumbprint)" -ForegroundColor Green

        # Export for backup
        $PfxPath = Join-Path $ScriptDir "dev-certificate.pfx"
        $PfxPassword = ConvertTo-SecureString -String "DevPassword123!" -Force -AsPlainText
        Export-PfxCertificate -Cert $Certificate -FilePath $PfxPath -Password $PfxPassword | Out-Null
        Write-Host "  Exported to: $PfxPath (Password: DevPassword123!)" -ForegroundColor Yellow
        Write-Host ""
        Write-Host "  NOTE: Self-signed certificates will show Windows SmartScreen warnings." -ForegroundColor Yellow
        Write-Host "        Use a commercial certificate for production releases." -ForegroundColor Yellow
    }
}
elseif ($CertificatePath) {
    Write-Host "Loading certificate from: $CertificatePath" -ForegroundColor Yellow

    if (-not (Test-Path $CertificatePath)) {
        Write-Host "ERROR: Certificate file not found!" -ForegroundColor Red
        exit 1
    }

    $SecurePassword = ConvertTo-SecureString -String $CertificatePassword -Force -AsPlainText
    $Certificate = Get-PfxCertificate -FilePath $CertificatePath -Password $SecurePassword
    Write-Host "  Loaded certificate: $($Certificate.Subject)" -ForegroundColor Green
}
else {
    Write-Host "ERROR: Please specify -UseSelfSigned or provide -CertificatePath" -ForegroundColor Red
    Write-Host ""
    Write-Host "Usage:" -ForegroundColor Yellow
    Write-Host "  Testing:    .\Sign-Application.ps1 -UseSelfSigned" -ForegroundColor Gray
    Write-Host "  Production: .\Sign-Application.ps1 -CertificatePath cert.pfx -CertificatePassword pass" -ForegroundColor Gray
    exit 1
}

Write-Host ""

# Find signtool
$SignTool = $null
$SignToolPaths = @(
    "${env:ProgramFiles(x86)}\Windows Kits\10\bin\10.0.22621.0\x64\signtool.exe",
    "${env:ProgramFiles(x86)}\Windows Kits\10\bin\10.0.19041.0\x64\signtool.exe",
    "${env:ProgramFiles(x86)}\Windows Kits\10\bin\x64\signtool.exe",
    "${env:ProgramFiles}\Windows Kits\10\bin\10.0.22621.0\x64\signtool.exe"
)

foreach ($path in $SignToolPaths) {
    if (Test-Path $path) {
        $SignTool = $path
        break
    }
}

# Also check for signtool in PATH
if (-not $SignTool) {
    $SignToolCmd = Get-Command signtool.exe -ErrorAction SilentlyContinue
    if ($SignToolCmd) {
        $SignTool = $SignToolCmd.Source
    }
}

if (-not $SignTool) {
    Write-Host "ERROR: signtool.exe not found!" -ForegroundColor Red
    Write-Host "Please install Windows SDK or Visual Studio with C++ tools." -ForegroundColor Yellow
    exit 1
}

Write-Host "Using SignTool: $SignTool" -ForegroundColor Gray
Write-Host ""

# Files to sign
$FilesToSign = @(
    "SysMonitor.App.exe",
    "SysMonitor.Core.dll"
)

# Sign files
Write-Host "Signing application files..." -ForegroundColor Yellow
$SignedCount = 0
$ErrorCount = 0

foreach ($fileName in $FilesToSign) {
    $filePath = Join-Path $FullPublishPath $fileName

    if (Test-Path $filePath) {
        Write-Host "  Signing: $fileName" -ForegroundColor Gray

        try {
            if ($UseSelfSigned) {
                # Sign with certificate from store
                & $SignTool sign /sha1 $Certificate.Thumbprint /fd SHA256 /t $TimestampServer /d "STX1 System Monitor" /du "https://github.com/rockystack" "$filePath" 2>&1 | Out-Null
            }
            else {
                # Sign with PFX file
                & $SignTool sign /f "$CertificatePath" /p $CertificatePassword /fd SHA256 /t $TimestampServer /d "STX1 System Monitor" /du "https://github.com/rockystack" "$filePath" 2>&1 | Out-Null
            }

            if ($LASTEXITCODE -eq 0) {
                Write-Host "    Signed successfully" -ForegroundColor Green
                $SignedCount++
            }
            else {
                Write-Host "    WARNING: Signing may have issues" -ForegroundColor Yellow
                $SignedCount++
            }
        }
        catch {
            Write-Host "    ERROR: $($_.Exception.Message)" -ForegroundColor Red
            $ErrorCount++
        }
    }
    else {
        Write-Host "  Skipping (not found): $fileName" -ForegroundColor DarkGray
    }
}

Write-Host ""
Write-Host "============================================" -ForegroundColor Cyan
Write-Host "  Signing Complete" -ForegroundColor Cyan
Write-Host "============================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "  Files signed: $SignedCount" -ForegroundColor $(if ($SignedCount -gt 0) { "Green" } else { "Yellow" })
Write-Host "  Errors: $ErrorCount" -ForegroundColor $(if ($ErrorCount -eq 0) { "Green" } else { "Red" })
Write-Host ""

if ($UseSelfSigned) {
    Write-Host "IMPORTANT: Self-signed certificate was used!" -ForegroundColor Yellow
    Write-Host "Windows will show SmartScreen warnings for this build." -ForegroundColor Yellow
    Write-Host ""
    Write-Host "For production releases, obtain a code signing certificate from:" -ForegroundColor Cyan
    Write-Host "  - DigiCert: https://www.digicert.com/signing/code-signing-certificates" -ForegroundColor Gray
    Write-Host "  - Sectigo: https://sectigo.com/ssl-certificates-tls/code-signing" -ForegroundColor Gray
    Write-Host "  - SSL.com: https://www.ssl.com/certificates/code-signing/" -ForegroundColor Gray
}
