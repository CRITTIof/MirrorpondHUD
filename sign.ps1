param(
    [string]$Exe,
    [switch]$TrustOnly
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$exePath = if ($Exe) { $Exe } else { Join-Path $root 'bin\Release\WindowOverlay.exe' }

$certDir = Join-Path $root 'certs'
New-Item -ItemType Directory -Force -Path $certDir | Out-Null
$pfx = Join-Path $certDir 'windowoverlay.pfx'
$cer = Join-Path $certDir 'windowoverlay.cer'
$password = 'WindowOverlayDev!2026'

# Build a SecureString without the (possibly unavailable) Security module.
$secure = New-Object System.Security.SecureString
foreach ($ch in $password.ToCharArray()) { $secure.AppendChar($ch) }
$secure.MakeReadOnly()

# 1) Load the existing pfx, or create a new self-signed cert (needs the Cert: drive)
if (Test-Path $pfx) {
    $cert = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($pfx, $password)
    Write-Host "[OK] loaded certificate $($cert.Thumbprint) from pfx"
} else {
    if (-not (Get-Command New-SelfSignedCertificate -ErrorAction SilentlyContinue)) {
        throw 'No certificate found and New-SelfSignedCertificate is unavailable. Run sign.ps1 once from a normal PowerShell session.'
    }
    try {
        $cert = New-SelfSignedCertificate -Type CodeSigningCert -Subject 'CN=WindowOverlay Dev' -KeyUsage DigitalSignature -FriendlyName 'WindowOverlay (self-signed)' -CertStoreLocation Cert:\CurrentUser\My -NotAfter (Get-Date).AddYears(10)
        Export-PfxCertificate -Cert $cert -FilePath $pfx -Password $secure | Out-Null
        Export-Certificate -Cert $cert -FilePath $cer | Out-Null
        Write-Host "[OK] created certificate $($cert.Thumbprint)"
    } catch {
        throw "Unable to create a certificate in this PowerShell session: $($_.Exception.Message). Run sign.ps1 once from a normal PowerShell window."
    }
}

# 2) Trust it for the current user (pure .NET, no Cert: drive required)
foreach ($storeName in @('Root', 'TrustedPublisher')) {
    $store = New-Object System.Security.Cryptography.X509Certificates.X509Store($storeName, 'CurrentUser')
    $store.Open('ReadWrite')
    $exists = $store.Certificates | Where-Object { $_.Thumbprint -eq $cert.Thumbprint }
    if (-not $exists) {
        $store.Add($cert)
        Write-Host "[OK] added to CurrentUser\$storeName"
    } else {
        Write-Host "[SKIP] already in CurrentUser\$storeName"
    }
    $store.Close()
}

if ($TrustOnly) { return }

# 3) Sign the exe
if (-not (Test-Path $exePath)) { throw "exe not found: $exePath" }

$signtool = Get-ChildItem 'C:\Program Files (x86)\Windows Kits\10\bin' -Recurse -Filter signtool.exe -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -match '\\x64\\' } |
            Sort-Object FullName -Descending |
            Select-Object -First 1

if (-not $signtool) { throw 'signtool.exe not found (Windows SDK required)' }

& $signtool.FullName sign /fd SHA256 /f $pfx /p $password $exePath
if ($LASTEXITCODE -ne 0) { throw "sign failed with exit code $LASTEXITCODE" }

& $signtool.FullName verify /pa $exePath | Out-Null
if ($LASTEXITCODE -ne 0) {
    Write-Host '[WARN] signature written but verify failed (certificate may not be trusted yet)'
} else {
    Write-Host "[OK] signed and verified: $exePath"
}
