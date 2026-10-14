<#
.SYNOPSIS
    Verifies the cryptographic integrity of AirFoxBT release binaries.
.DESCRIPTION
    Computes the SHA-256 hash of AirFoxBT.exe and compares it against
    the official SHA256SUMS manifest and AirFoxBT.exe.sha256 checksum.
#>

[CmdletBinding()]
param (
    [string]$TargetExe = ""
)

$ErrorActionPreference = "Stop"
$ScriptDir = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $MyInvocation.MyCommand.Definition }

if ([string]::IsNullOrWhiteSpace($TargetExe)) {
    $TargetExe = Join-Path $ScriptDir "AirFoxBT.exe"
}

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "  FoxDen Software - Cryptographic Integrity Verifier" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

if (-not (Test-Path $TargetExe)) {
    Write-Host "`n[ERROR] Target binary not found: $TargetExe" -ForegroundColor Red
    exit 1
}

Write-Host "`n[-] Target Binary   : $TargetExe" -ForegroundColor Gray

# 1. Compute local SHA-256
$computedHash = (Get-FileHash -Path $TargetExe -Algorithm SHA256).Hash.ToLowerInvariant()
Write-Host "[-] Computed SHA-256: $computedHash" -ForegroundColor Yellow

# 2. Look for expected hash in manifest files
$expectedHash = $null
$manifestFound = $false

$shaFile = Join-Path $ScriptDir "AirFoxBT.exe.sha256"
$sumsFileNoExt = Join-Path $ScriptDir "SHA256SUMS"
$sumsFileTxt = Join-Path $ScriptDir "SHA256SUMS.txt"

if (Test-Path $shaFile) {
    $content = (Get-Content $shaFile -Raw).Trim()
    if ($content -match "^([a-fA-F0-9]{64})") {
        $expectedHash = $matches[1].ToLowerInvariant()
        $manifestFound = $true
        Write-Host "[-] Manifest File   : AirFoxBT.exe.sha256" -ForegroundColor Gray
    }
}

if (-not $manifestFound -and (Test-Path $sumsFileNoExt)) {
    $lines = Get-Content $sumsFileNoExt
    foreach ($line in $lines) {
        if ($line -match "^([a-fA-F0-9]{64})\s+\*?AirFoxBT\.exe") {
            $expectedHash = $matches[1].ToLowerInvariant()
            $manifestFound = $true
            Write-Host "[-] Manifest File   : SHA256SUMS" -ForegroundColor Gray
            break
        }
    }
}

if (-not $manifestFound -and (Test-Path $sumsFileTxt)) {
    $lines = Get-Content $sumsFileTxt
    foreach ($line in $lines) {
        if ($line -match "^([a-fA-F0-9]{64})\s+\*?AirFoxBT\.exe") {
            $expectedHash = $matches[1].ToLowerInvariant()
            $manifestFound = $true
            Write-Host "[-] Manifest File   : SHA256SUMS.txt" -ForegroundColor Gray
            break
        }
    }
}

if (-not $manifestFound) {
    Write-Host "`n[WARNING] No checksum manifest found (AirFoxBT.exe.sha256 or SHA256SUMS)." -ForegroundColor Yellow
    Write-Host "[INFO] Local binary hash is: $computedHash" -ForegroundColor Cyan
    exit 0
}

Write-Host "[-] Expected SHA-256: $expectedHash" -ForegroundColor Yellow

# 3. Compare
Write-Host ""
if ($computedHash -eq $expectedHash) {
    Write-Host "==========================================================" -ForegroundColor Green
    Write-Host "  [SUCCESS] SHA-256 Cryptographic Match Verified!" -ForegroundColor Green
    Write-Host "  The AirFoxBT executable is authentic and unaltered." -ForegroundColor Green
    Write-Host "==========================================================" -ForegroundColor Green
    exit 0
} else {
    Write-Host "==========================================================" -ForegroundColor Red
    Write-Host "  [FAILED] SHA-256 Checksum Mismatch!" -ForegroundColor Red
    Write-Host "  The executable does not match the release manifest." -ForegroundColor Red
    Write-Host "==========================================================" -ForegroundColor Red
    exit 1
}
