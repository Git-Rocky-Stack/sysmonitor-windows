<#
.SYNOPSIS
    Signs the STX1 System Monitor application files.

.DESCRIPTION
    Signs executables and DLLs with a code-signing certificate installed in the Windows
    certificate store (CurrentUser\My or LocalMachine\My), selected by thumbprint.
    No certificate files or passwords are read or written.

    The self-signed certificates CN=Rocky Stack (52E78D80...) and CN=Strategia-X (0724EBBA...)
    are retired; this script refuses to sign with them.

.PARAMETER CertificateThumbprint
    SHA-1 thumbprint of the signing certificate. Defaults to $env:SYSMONITOR_SIGNING_THUMBPRINT.

.PARAMETER UseSelfSigned
    Create (or reuse) a local self-signed development certificate in CurrentUser\My.
    It is never exported. Files signed with it are for local testing only.

.PARAMETER TimestampServer
    RFC 3161 timestamp server URL. Default: http://timestamp.digicert.com

.PARAMETER PublishPath
    Path to the published application files, relative to this script.

.EXAMPLE
    # Local testing with a development certificate:
    .\Sign-Application.ps1 -UseSelfSigned

.EXAMPLE
    # Release signing with a certificate installed in the certificate store:
    .\Sign-Application.ps1 -CertificateThumbprint 0123456789ABCDEF0123456789ABCDEF01234567
#>

param(
    [string]$CertificateThumbprint = $env:SYSMONITOR_SIGNING_THUMBPRINT,
    [switch]$UseSelfSigned,
    [string]$TimestampServer = "http://timestamp.digicert.com",
    [string]$PublishPath = "..\publish\installer-build"
)

$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RootDir = Split-Path -Parent $ScriptDir
. (Join-Path $ScriptDir "SigningCommon.ps1")

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

# Select certificate
try {
    if ($UseSelfSigned) {
        $Certificate = Get-DevelopmentSigningCertificate
        Write-Host "Using development certificate: $($Certificate.Thumbprint)" -ForegroundColor Yellow
    }
    elseif ($CertificateThumbprint) {
        $Certificate = Resolve-SigningCertificate -Thumbprint $CertificateThumbprint
        Write-Host "Using certificate: $($Certificate.Subject) ($($Certificate.Thumbprint))" -ForegroundColor Green
    }
    else {
        Write-Host "ERROR: Specify -UseSelfSigned or -CertificateThumbprint (or set SYSMONITOR_SIGNING_THUMBPRINT)." -ForegroundColor Red
        Write-Host ""
        Write-Host "Usage:" -ForegroundColor Yellow
        Write-Host "  Testing:    .\Sign-Application.ps1 -UseSelfSigned" -ForegroundColor Gray
        Write-Host "  Production: .\Sign-Application.ps1 -CertificateThumbprint <thumbprint>" -ForegroundColor Gray
        exit 1
    }
}
catch {
    Write-Host "ERROR: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}

Write-Host ""

$SignTool = Find-SignTool
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
            Invoke-CodeSign -SignTool $SignTool -Thumbprint $Certificate.Thumbprint `
                -TimestampServer $TimestampServer -FilePath $filePath
            Write-Host "    Signed successfully" -ForegroundColor Green
            $SignedCount++
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
    Write-Host "IMPORTANT: A self-signed development certificate was used." -ForegroundColor Yellow
    Write-Host "These files are for local testing only and will show SmartScreen warnings." -ForegroundColor Yellow
    Write-Host ""
}

if ($ErrorCount -gt 0) { exit 1 }
exit 0
