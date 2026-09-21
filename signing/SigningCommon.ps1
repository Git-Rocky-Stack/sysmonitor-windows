# Shared helpers for Build-Release.ps1 and signing\Sign-Application.ps1.
#
# Signing certificates are always taken from the Windows certificate store by thumbprint.
# Nothing in these scripts reads, writes, or passes certificate files or passwords.

# Self-signed certificates whose key material was published in this repository's history.
# They are retired and must never be used to sign again.
$script:RetiredSigningThumbprints = @(
    '52E78D80039EF8DC26D9EFD64A381B93133A8154', # CN=Rocky Stack
    '0724EBBA9D97D28CB1E784218E29B870C442312D'  # CN=Strategia-X
)

$script:DevelopmentCertificateSubject = 'CN=STX1 System Monitor (Development)'

function Resolve-SigningCertificate {
    <#
    .SYNOPSIS
        Returns the code-signing certificate with the given thumbprint from CurrentUser\My or LocalMachine\My.
        Throws if the thumbprint is malformed, retired, or not installed with a private key.
    #>
    param([Parameter(Mandatory)][string]$Thumbprint)

    $normalized = ($Thumbprint -replace '[^0-9A-Fa-f]', '').ToUpperInvariant()
    if ($normalized.Length -ne 40) {
        throw "Invalid certificate thumbprint '$Thumbprint' (expected 40 hex characters)."
    }
    if ($script:RetiredSigningThumbprints -contains $normalized) {
        throw "Certificate $normalized is retired (its key material was published) and must not be used for signing."
    }

    foreach ($store in 'Cert:\CurrentUser\My', 'Cert:\LocalMachine\My') {
        # -CodeSigningCert returns only certificates with the Code Signing usage and a private key.
        $cert = Get-ChildItem -Path $store -CodeSigningCert -ErrorAction SilentlyContinue |
            Where-Object { $_.Thumbprint -eq $normalized } |
            Select-Object -First 1
        if ($cert) { return $cert }
    }

    throw "No code-signing certificate with a private key and thumbprint $normalized was found in CurrentUser\My or LocalMachine\My."
}

function Get-DevelopmentSigningCertificate {
    <#
    .SYNOPSIS
        Returns the local self-signed development certificate, creating it in CurrentUser\My if needed.
        The certificate is never exported. Builds signed with it are for local testing only.
    #>
    $existing = Get-ChildItem -Path 'Cert:\CurrentUser\My' -CodeSigningCert |
        Where-Object { $_.Subject -eq $script:DevelopmentCertificateSubject -and $_.NotAfter -gt (Get-Date) } |
        Sort-Object NotAfter -Descending |
        Select-Object -First 1
    if ($existing) { return $existing }

    return New-SelfSignedCertificate `
        -Type CodeSigningCert `
        -Subject $script:DevelopmentCertificateSubject `
        -KeyUsage DigitalSignature `
        -KeyAlgorithm RSA `
        -KeyLength 3072 `
        -CertStoreLocation 'Cert:\CurrentUser\My' `
        -NotAfter (Get-Date).AddYears(2)
}

function Find-SignTool {
    <#
    .SYNOPSIS
        Returns the path of the newest x64 signtool.exe from the Windows SDK, or from PATH. Returns $null if none is found.
    #>
    $kitsBin = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
    if (Test-Path $kitsBin) {
        $candidate = Get-ChildItem -Path $kitsBin -Directory -ErrorAction SilentlyContinue |
            Where-Object { $_.Name -match '^\d+(\.\d+){3}$' } |
            Sort-Object { [version]$_.Name } -Descending |
            ForEach-Object { Join-Path $_.FullName 'x64\signtool.exe' } |
            Where-Object { Test-Path $_ } |
            Select-Object -First 1
        if ($candidate) { return $candidate }
    }

    $fromPath = Get-Command signtool.exe -ErrorAction SilentlyContinue
    if ($fromPath) { return $fromPath.Source }
    return $null
}

function Invoke-CodeSign {
    <#
    .SYNOPSIS
        Signs one file with SHA-256 and an RFC 3161 timestamp. Throws with signtool's output if signing fails.
    #>
    param(
        [Parameter(Mandatory)][string]$SignTool,
        [Parameter(Mandatory)][string]$Thumbprint,
        [Parameter(Mandatory)][string]$TimestampServer,
        [Parameter(Mandatory)][string]$FilePath,
        [string]$Description = 'STX1 System Monitor'
    )

    $output = & $SignTool sign /sha1 $Thumbprint /fd SHA256 /tr $TimestampServer /td SHA256 /d $Description $FilePath 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "signtool failed (exit $LASTEXITCODE) for ${FilePath}: $($output -join ' ')"
    }
}
