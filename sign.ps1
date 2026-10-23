param(
    [string]$Exe,
    [switch]$TrustOnly,
    [string]$Subject = 'CN=CRITTIof'
)

# Self-signed code-signing certificate for MirrorpondHUD.
# Subject defaults to the GitHub account name of this machine (gh auth status).
# Everything here uses pure .NET: the Cert: drive and the Security module are
# not available in every PowerShell session on this machine.

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$exePath = if ($Exe) { $Exe } else { Join-Path $root 'bin\Release\MirrorpondHUD.exe' }

$certDir = Join-Path $root 'certs'
New-Item -ItemType Directory -Force -Path $certDir | Out-Null
$pfx = Join-Path $certDir 'mirrorpondhud.pfx'
$cer = Join-Path $certDir 'mirrorpondhud.cer'
$password = 'MirrorpondHUDDev!2026'
$oldSubject = 'CN=MirrorpondHUD Dev'

function New-SigningCertificate([string]$name) {
    try {
        $rsa = [System.Security.Cryptography.RSA]::Create(3072)

        $basic = New-Object System.Security.Cryptography.X509Certificates.X509BasicConstraintsExtension($false, $false, 0, $true)
        $usage = New-Object System.Security.Cryptography.X509Certificates.X509KeyUsageExtension(
            [System.Security.Cryptography.X509Certificates.X509KeyUsageFlags]::DigitalSignature, $true)
        $oids = New-Object System.Security.Cryptography.OidCollection
        $null = $oids.Add((New-Object System.Security.Cryptography.Oid('1.3.6.1.5.5.7.3.3')))
        $eku = New-Object System.Security.Cryptography.X509Certificates.X509EnhancedKeyUsageExtension($oids, $true)

        $req = New-Object System.Security.Cryptography.X509Certificates.CertificateRequest(
            $name,
            $rsa,
            [System.Security.Cryptography.HashAlgorithmName]::SHA256,
            [System.Security.Cryptography.RSASignaturePadding]::Pkcs1)

        $req.CertificateExtensions.Add($basic)
        $req.CertificateExtensions.Add($usage)
        $req.CertificateExtensions.Add($eku)
        $req.CertificateExtensions.Add(
            (New-Object System.Security.Cryptography.X509Certificates.X509SubjectKeyIdentifierExtension($req.PublicKey, $false)))

        $created = $req.CreateSelfSigned([DateTimeOffset]::Now.AddDays(-1), [DateTimeOffset]::Now.AddYears(10))
        Write-Host "[OK] created certificate $($created.Thumbprint) subject=$name (CertificateRequest)"
        return $created
    }
    catch {
        Write-Host "[WARN] CertificateRequest failed: $($_.Exception.Message)"
    }

    if (Get-Command New-SelfSignedCertificate -ErrorAction SilentlyContinue) {
        $secure = New-Object System.Security.SecureString
        foreach ($ch in $password.ToCharArray()) { $secure.AppendChar($ch) }
        $secure.MakeReadOnly()

        $created = New-SelfSignedCertificate -Type CodeSigningCert -Subject $name `
            -KeyUsage DigitalSignature -FriendlyName 'MirrorpondHUD (self-signed)' `
            -CertStoreLocation Cert:\CurrentUser\My -NotAfter (Get-Date).AddYears(10)
        Export-PfxCertificate -Cert $created -FilePath $pfx -Password $secure | Out-Null
        Export-Certificate -Cert $created -FilePath $cer | Out-Null
        Write-Host "[OK] created certificate $($created.Thumbprint) subject=$name (New-SelfSignedCertificate)"
        return $created
    }

    throw "Unable to create a certificate in this PowerShell session ($name)."
}

function Remove-OldCertificate([string]$storeName, [string]$subject) {
    try {
        $store = New-Object System.Security.Cryptography.X509Certificates.X509Store($storeName, 'CurrentUser')
        $store.Open('ReadWrite')
        $dead = @()
        foreach ($c in $store.Certificates) {
            if ($c.Subject -eq $subject) { $dead += $c }
        }
        foreach ($c in $dead) {
            $store.Remove($c)
            Write-Host "[OK] removed $subject from CurrentUser\$storeName"
        }
        $store.Close()
    }
    catch {
        Write-Host "[WARN] cleanup of CurrentUser\$storeName failed: $($_.Exception.Message)"
    }
}

# ---------------------------------------------------------------- 1) load/create
$cert = $null
if (Test-Path $pfx) {
    try {
        $loaded = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($pfx, $password)
        if ($loaded.Subject -eq $Subject) {
            $cert = $loaded
            Write-Host "[OK] loaded certificate $($cert.Thumbprint) subject=$($cert.Subject)"
        }
        else {
            Write-Host "[INFO] pfx subject is '$($loaded.Subject)', expected '$Subject'"
        }
    }
    catch {
        Write-Host "[WARN] cannot read existing pfx: $($_.Exception.Message)"
    }
}

if (-not $cert) {
    Remove-Item $pfx, $cer -ErrorAction SilentlyContinue
    $cert = New-SigningCertificate $Subject

    if ($cert.Subject -ne $Subject) {
        throw "certificate subject is '$($cert.Subject)', expected '$Subject'"
    }

    try { $cert.FriendlyName = 'MirrorpondHUD (self-signed)' } catch { }

    $pfxBytes = $cert.Export([System.Security.Cryptography.X509Certificates.X509ContentType]::Pfx, $password)
    [System.IO.File]::WriteAllBytes($pfx, $pfxBytes)
    $cerBytes = $cert.Export([System.Security.Cryptography.X509Certificates.X509ContentType]::Cert)
    [System.IO.File]::WriteAllBytes($cer, $cerBytes)
    Write-Host "[OK] exported $pfx"
}

# ---------------------------------------------------------------- 2) clean old name
if ($oldSubject -ne $Subject) {
    foreach ($storeName in @('My', 'Root', 'TrustedPublisher')) {
        Remove-OldCertificate $storeName $oldSubject
    }
}

# ---------------------------------------------------------------- 3) trust for current user
$publicCert = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($cer)
foreach ($storeName in @('Root', 'TrustedPublisher')) {
    $store = New-Object System.Security.Cryptography.X509Certificates.X509Store($storeName, 'CurrentUser')
    $store.Open('ReadWrite')
    $exists = $store.Certificates | Where-Object { $_.Thumbprint -eq $publicCert.Thumbprint }
    if (-not $exists) {
        $store.Add($publicCert)
        Write-Host "[OK] added $($publicCert.Subject) to CurrentUser\$storeName"
    }
    else {
        Write-Host "[SKIP] already in CurrentUser\$storeName"
    }
    $store.Close()
}

if ($TrustOnly) { return }

# ---------------------------------------------------------------- 4) sign the exe
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
}
else {
    Write-Host "[OK] signed and verified: $exePath"
}
