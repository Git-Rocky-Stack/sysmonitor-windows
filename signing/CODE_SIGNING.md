# Code Signing Guide for STX1 System Monitor

This guide explains how to sign the STX1 System Monitor application for distribution.

## Why Sign Your Code?

Code signing provides:
- **Trust**: Users see your company name instead of "Unknown Publisher"
- **No SmartScreen warnings**: Windows won't block or warn about your app
- **Integrity**: Ensures the code hasn't been tampered with
- **Professional appearance**: Required for enterprise deployments

## Retired Certificates — Action Required for Sideload Users

The self-signed certificates below were used for sideloaded builds, and their key material was
published in this repository's history. They are **retired**: no build will be signed with them again,
and the signing scripts refuse to use them.

| Subject | Thumbprint |
|---------|------------|
| `CN=Rocky Stack` | `52E78D80039EF8DC26D9EFD64A381B93133A8154` |
| `CN=Strategia-X` | `0724EBBA9D97D28CB1E784218E29B870C442312D` |

If you installed one of these certificates to sideload an older build, remove it so that nothing signed
with it is trusted on your machine:

```powershell
# Elevated PowerShell — machine-wide Trusted People store
Get-ChildItem Cert:\LocalMachine\TrustedPeople |
    Where-Object Thumbprint -in '52E78D80039EF8DC26D9EFD64A381B93133A8154', '0724EBBA9D97D28CB1E784218E29B870C442312D' |
    Remove-Item

# Current user's Trusted People store
Get-ChildItem Cert:\CurrentUser\TrustedPeople |
    Where-Object Thumbprint -in '52E78D80039EF8DC26D9EFD64A381B93133A8154', '0724EBBA9D97D28CB1E784218E29B870C442312D' |
    Remove-Item
```

Microsoft Store builds are not affected: the package identity uses the Store-assigned publisher
(`src/SysMonitor.App/Package.appxmanifest`).

## Quick Start

The build scripts only sign with a certificate that is installed in the Windows certificate store
(`CurrentUser\My` or `LocalMachine\My`), selected by thumbprint. They never read or write `.pfx` files
or passwords, and they stop with an error if signing fails.

### Option 1: Development Certificate (Testing Only)

```powershell
# Run from repository root. Creates "CN=STX1 System Monitor (Development)" in CurrentUser\My
# the first time; it is never exported.
.\Build-Release.ps1 -SignCode -UseSelfSignedCert
```

> **Warning**: Self-signed certificates will still trigger SmartScreen warnings. Use only for internal testing.

### Option 2: Commercial Certificate (Production)

```powershell
# Run from repository root, with the certificate installed in your certificate store
.\Build-Release.ps1 -SignCode -CertificateThumbprint <thumbprint>

# or set it once per session
$env:SYSMONITOR_SIGNING_THUMBPRINT = '<thumbprint>'
.\Build-Release.ps1 -SignCode
```

## Getting a Code Signing Certificate

### Recommended Certificate Authorities

| Provider | Type | Approx. Cost | Link |
|----------|------|--------------|------|
| **SSL.com** | OV | ~$250/year | [ssl.com](https://www.ssl.com/certificates/code-signing/) |
| **Sectigo** | OV | ~$300/year | [sectigo.com](https://sectigo.com/ssl-certificates-tls/code-signing) |
| **DigiCert** | EV | ~$500/year | [digicert.com](https://www.digicert.com/signing/code-signing-certificates) |
| **SignPath** | Free (OSS) | Free | [signpath.io](https://signpath.io/) |

### Certificate Types

1. **Standard/OV (Organization Validated)**
   - Validates your organization exists
   - Shows company name in signatures
   - Good for most applications
   - ~$250-400/year

2. **EV (Extended Validation)**
   - Higher trust level
   - Immediate SmartScreen reputation
   - Required for kernel drivers
   - ~$400-700/year
   - Requires hardware token (USB)

### Requirements for Obtaining a Certificate

You'll need to provide:
- Business registration documents
- Government-issued ID
- Phone verification
- Domain ownership (for some CAs)

## Manual Signing Process

### Prerequisites

1. **Windows SDK** - Provides `signtool.exe`
   - Install via Visual Studio Installer, or
   - Download [Windows SDK](https://developer.microsoft.com/en-us/windows/downloads/windows-sdk/)

2. **Code Signing Certificate** installed in the Windows certificate store (import it once with the
   certificate vendor's tooling, or with `Import-PfxCertificate` from a location **outside** this repository)

### Sign Individual Files

```powershell
# List code-signing certificates that have a private key
Get-ChildItem Cert:\CurrentUser\My -CodeSigningCert

# Sign using the thumbprint (SHA-256 digest, RFC 3161 timestamp)
signtool sign /sha1 "THUMBPRINT" /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 /d "STX1 System Monitor" "SysMonitor.App.exe"

# Verify signature
signtool verify /pa "SysMonitor.App.exe"
```

## Timestamp Servers

Always use a timestamp server! This ensures signatures remain valid after certificate expires.

| Provider | URL |
|----------|-----|
| DigiCert | `http://timestamp.digicert.com` |
| Sectigo | `http://timestamp.sectigo.com` |
| GlobalSign | `http://timestamp.globalsign.com/tsa/r6advanced1` |
| SSL.com | `http://ts.ssl.com` |

## Build Scripts Reference

### Full Release Build

```powershell
# Without signing
.\Build-Release.ps1

# With self-signed (testing)
.\Build-Release.ps1 -SignCode -UseSelfSignedCert

# With a certificate from the certificate store
.\Build-Release.ps1 -SignCode -CertificateThumbprint <thumbprint>

# Portable only (no installer)
.\Build-Release.ps1 -SkipInstaller
```

### Sign-Only Script

```powershell
cd signing

# Development certificate (created in CurrentUser\My if needed, never exported)
.\Sign-Application.ps1 -UseSelfSigned

# Certificate from the certificate store
.\Sign-Application.ps1 -CertificateThumbprint <thumbprint>
```

## CI/CD Integration

Keep the certificate in the CI system's secret store, import it into the runner's certificate store
for the duration of the job, and sign by thumbprint. Never commit certificate files.

### GitHub Actions Example

```yaml
- name: Sign Application
  env:
    CERTIFICATE_BASE64: ${{ secrets.CODE_SIGNING_CERT }}
    CERTIFICATE_PASSWORD: ${{ secrets.CODE_SIGNING_PASSWORD }}
  run: |
    $pfx = Join-Path $env:RUNNER_TEMP 'signing.pfx'
    [IO.File]::WriteAllBytes($pfx, [Convert]::FromBase64String($env:CERTIFICATE_BASE64))
    $password = ConvertTo-SecureString $env:CERTIFICATE_PASSWORD -AsPlainText -Force
    $cert = Import-PfxCertificate -FilePath $pfx -CertStoreLocation Cert:\CurrentUser\My -Password $password
    Remove-Item $pfx -Force
    try {
      .\Build-Release.ps1 -SignCode -CertificateThumbprint $cert.Thumbprint
    }
    finally {
      Remove-Item "Cert:\CurrentUser\My\$($cert.Thumbprint)" -DeleteKey
    }
```

## Troubleshooting

### "SignTool not found"

Install Windows SDK:
```powershell
winget install Microsoft.WindowsSDK
```

Or install via Visual Studio Installer (Windows SDK component).

### "The specified timestamp server could not be reached"

Try a different timestamp server from the list above.

### "Certificate not valid for code signing"

Ensure your certificate has the "Code Signing" purpose and a private key. It must appear in:
```powershell
Get-ChildItem Cert:\CurrentUser\My -CodeSigningCert
```

### SmartScreen Still Blocking

EV certificates get immediate reputation. OV certificates build reputation over time based on:
- Number of downloads
- Time since first signing
- User feedback (not clicking "Don't run")

## Security Best Practices

1. **Protect your certificate** - Keep private keys in the certificate store, a hardware token, or a cloud signing service; never commit `.pfx`/`.p12` files (they are ignored by `.gitignore`)
2. **Use hardware tokens** - EV certificates require them; consider for OV too
3. **Rotate certificates** - Don't wait until expiration
4. **Timestamp everything** - Signatures remain valid after certificate expires
5. **Sign all executables** - Including DLLs that could be loaded
6. **Verify after signing** - Use `signtool verify /pa`

## Resources

- [Microsoft Code Signing Docs](https://docs.microsoft.com/en-us/windows/win32/seccrypto/cryptography-tools)
- [SignTool Reference](https://docs.microsoft.com/en-us/windows/win32/seccrypto/signtool)
- [SmartScreen & Reputation](https://docs.microsoft.com/en-us/windows/security/threat-protection/microsoft-defender-smartscreen/microsoft-defender-smartscreen-overview)
