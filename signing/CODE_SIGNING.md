# Code Signing Guide for STX1 System Monitor

This guide explains how to sign the STX1 System Monitor application for distribution.

## Why Sign Your Code?

Code signing provides:
- **Trust**: Users see your company name instead of "Unknown Publisher"
- **No SmartScreen warnings**: Windows won't block or warn about your app
- **Integrity**: Ensures the code hasn't been tampered with
- **Professional appearance**: Required for enterprise deployments

## Quick Start

### Option 1: Self-Signed Certificate (Testing Only)

```powershell
# Run from repository root
.\Build-Release.ps1 -SignCode -UseSelfSignedCert
```

> **Warning**: Self-signed certificates will still trigger SmartScreen warnings. Use only for internal testing.

### Option 2: Commercial Certificate (Production)

```powershell
# Run from repository root
.\Build-Release.ps1 -SignCode -CertificatePath "path\to\certificate.pfx" -CertificatePassword "your-password"
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

2. **Code Signing Certificate** (.pfx file)

### Sign Individual Files

```powershell
# Sign an executable
signtool sign /f "certificate.pfx" /p "password" /fd SHA256 /t http://timestamp.digicert.com /d "STX1 System Monitor" "SysMonitor.App.exe"

# Verify signature
signtool verify /pa "SysMonitor.App.exe"
```

### Sign with Certificate from Store

```powershell
# List certificates
Get-ChildItem Cert:\CurrentUser\My -CodeSigningCert

# Sign using thumbprint
signtool sign /sha1 "THUMBPRINT" /fd SHA256 /t http://timestamp.digicert.com "SysMonitor.App.exe"
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

# With commercial certificate
.\Build-Release.ps1 -SignCode -CertificatePath "cert.pfx" -CertificatePassword "pass"

# Portable only (no installer)
.\Build-Release.ps1 -SkipInstaller
```

### Sign-Only Script

```powershell
cd signing

# Self-signed (creates certificate if needed)
.\Sign-Application.ps1 -UseSelfSigned

# Commercial certificate
.\Sign-Application.ps1 -CertificatePath "..\cert.pfx" -CertificatePassword "pass"
```

## CI/CD Integration

### GitHub Actions Example

```yaml
- name: Sign Application
  env:
    CERTIFICATE_BASE64: ${{ secrets.CODE_SIGNING_CERT }}
    CERTIFICATE_PASSWORD: ${{ secrets.CODE_SIGNING_PASSWORD }}
  run: |
    # Decode certificate
    $certBytes = [Convert]::FromBase64String($env:CERTIFICATE_BASE64)
    [IO.File]::WriteAllBytes("cert.pfx", $certBytes)

    # Sign
    .\Build-Release.ps1 -SignCode -CertificatePath "cert.pfx" -CertificatePassword $env:CERTIFICATE_PASSWORD

    # Cleanup
    Remove-Item "cert.pfx" -Force
```

### Azure DevOps Example

```yaml
- task: PowerShell@2
  inputs:
    targetType: 'inline'
    script: |
      .\Build-Release.ps1 -SignCode -CertificatePath "$(certPath)" -CertificatePassword "$(certPassword)"
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

Ensure your certificate has the "Code Signing" purpose. Check with:
```powershell
Get-PfxCertificate -FilePath "cert.pfx" | Format-List *
```

### SmartScreen Still Blocking

EV certificates get immediate reputation. OV certificates build reputation over time based on:
- Number of downloads
- Time since first signing
- User feedback (not clicking "Don't run")

## Security Best Practices

1. **Protect your certificate** - Store PFX files securely, use strong passwords
2. **Use hardware tokens** - EV certificates require them; consider for OV too
3. **Rotate certificates** - Don't wait until expiration
4. **Timestamp everything** - Signatures remain valid after certificate expires
5. **Sign all executables** - Including DLLs that could be loaded
6. **Verify after signing** - Use `signtool verify /pa`

## Resources

- [Microsoft Code Signing Docs](https://docs.microsoft.com/en-us/windows/win32/seccrypto/cryptography-tools)
- [SignTool Reference](https://docs.microsoft.com/en-us/windows/win32/seccrypto/signtool)
- [SmartScreen & Reputation](https://docs.microsoft.com/en-us/windows/security/threat-protection/microsoft-defender-smartscreen/microsoft-defender-smartscreen-overview)
