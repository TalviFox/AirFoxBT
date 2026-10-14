# =============================================================
#  🦊 AirFoxBT Integrity Auditor & Verification Script
#  https://github.com/TalviFox/AirFoxBT
#  Cryptographically verifies local AirFoxBT.exe against official release hashes.
# =============================================================

[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [string]$Path
)

[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$ErrorActionPreference = "Stop"

$repo = "TalviFox/AirFoxBT"
$targetExe = $null

if ($Path -and (Test-Path $Path)) {
    $targetExe = (Resolve-Path $Path).Path
}
elseif (Test-Path "$env:LOCALAPPDATA\Programs\AirFoxBT\AirFoxBT.exe") {
    $targetExe = "$env:LOCALAPPDATA\Programs\AirFoxBT\AirFoxBT.exe"
}
elseif (Test-Path "$env:ProgramFiles\AirFoxBT\AirFoxBT.exe") {
    $targetExe = "$env:ProgramFiles\AirFoxBT\AirFoxBT.exe"
}
elseif ($PSScriptRoot -and (Test-Path (Join-Path $PSScriptRoot "publish\AirFoxBT.exe"))) {
    $targetExe = Join-Path $PSScriptRoot "publish\AirFoxBT.exe"
}
elseif ($PSScriptRoot -and (Test-Path (Join-Path $PSScriptRoot "AirFoxBT.exe"))) {
    $targetExe = Join-Path $PSScriptRoot "AirFoxBT.exe"
}

if (-not $targetExe) {
    Write-Host "[X] Could not locate AirFoxBT.exe to verify." -ForegroundColor Red
    return
}

Write-Host "[*] Auditing binary: $targetExe" -ForegroundColor Cyan
$actualHash = (Get-FileHash -Path $targetExe -Algorithm SHA256).Hash.ToLowerInvariant()
Write-Host "[+] Local SHA-256:   $actualHash" -ForegroundColor White

# 1. Attempt to query latest release from GitHub
Write-Host "[*] Querying latest release from $repo..." -ForegroundColor Cyan
$expectedHash = $null
$sourceInfo = $null

try {
    $release = Invoke-RestMethod -Uri "https://api.github.com/repos/$repo/releases/latest" -Headers @{ "User-Agent" = "AirFoxBT-Verifier" }
    $sourceInfo = "GitHub Release $($release.tag_name)"

    $sumsAsset = $release.assets | Where-Object { $_.name -match "^(SHA256SUMS|AirFoxBT.*)\.(txt|sha256)$" -or $_.name -eq "AirFoxBT.exe.sha256" } | Select-Object -First 1
    if ($sumsAsset) {
        $checksumText = Invoke-RestMethod -Uri $sumsAsset.browser_download_url -Headers @{ "User-Agent" = "AirFoxBT-Verifier" }
        if ($checksumText -match "([a-fA-F0-9]{64})\s+.*AirFoxBT\.exe") {
            $expectedHash = $matches[1].ToLowerInvariant()
        }
        elseif ($checksumText -match "\b([a-fA-F0-9]{64})\b") {
            $expectedHash = $matches[1].ToLowerInvariant()
        }
    }

    if (-not $expectedHash -and $release.body -match "\b([a-fA-F0-9]{64})\b") {
        $expectedHash = $matches[1].ToLowerInvariant()
    }
}
catch {
    Write-Host "[!] Could not reach GitHub API or release not yet published: $_" -ForegroundColor Yellow
}

# 2. Check for local checksum file companion (offline verification fallback)
if (-not $expectedHash) {
    $parentDir = Split-Path -Path $targetExe -Parent
    $localSha = Join-Path $parentDir "AirFoxBT.exe.sha256"
    if (Test-Path $localSha) {
        $localText = Get-Content -Path $localSha -Raw
        if ($localText -match "\b([a-fA-F0-9]{64})\b") {
            $expectedHash = $matches[1].ToLowerInvariant()
            $sourceInfo = "Local Checksum ($localSha)"
        }
    }
}

if ($expectedHash) {
    Write-Host "[+] Known SHA-256:    $expectedHash ($sourceInfo)" -ForegroundColor Cyan
    if ($actualHash -eq $expectedHash) {
        Write-Host "`n[+] VERIFICATION PASSED: Binary is 100% authentic and unaltered!" -ForegroundColor Green
    }
    else {
        Write-Host "`n[X] VERIFICATION FAILED: Local hash does not match expected release checksum!" -ForegroundColor Red
    }
}
else {
    Write-Host "[!] Could not extract reference checksum from release or local signature." -ForegroundColor Yellow
    Write-Host "    Current binary SHA-256: $actualHash" -ForegroundColor Cyan
}
