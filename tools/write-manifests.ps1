param(
    [Parameter(Mandatory=$true)][string]$Version,
    [Parameter(Mandatory=$true)][string]$Source,
    [Parameter(Mandatory=$true)][string]$Dest
)

# X-4：manifest 的 version 由 Directory.Build.props 的 $(Version) 统一生成，
# 源码里的 manifest 只保留 id/name/apiVersion/entryType/assembly/author。
$ErrorActionPreference = 'Stop'
$count = 0
Get-ChildItem -Path $Source -Directory | ForEach-Object {
    $manifest = Join-Path $_.FullName 'manifest.json'
    if (-not (Test-Path $manifest)) { return }

    $data = Get-Content $manifest -Raw -Encoding UTF8 | ConvertFrom-Json
    $data.version = $Version

    $targetDir = Join-Path $Dest $data.id
    New-Item -ItemType Directory -Force -Path $targetDir | Out-Null
    $json = $data | ConvertTo-Json -Depth 8
    Set-Content -Path (Join-Path $targetDir 'manifest.json') -Value $json -Encoding UTF8
    $count++
}
Write-Host "[OK] generated $count card manifest(s) version=$Version"
