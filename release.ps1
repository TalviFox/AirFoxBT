# AirFoxBT Automated Release Script
# https://github.com/TalviFox/AirFoxBT
# Usage: .\release.ps1 -Version "1.0.1"

[CmdletBinding()]
param(
    [Parameter(Position = 0, Mandatory = $false)]
    [string]$Version,

    [Parameter()]
    [switch]$DryRun,

    [Parameter()]
    [switch]$SkipGit
)

[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$OutputEncoding = [System.Text.Encoding]::UTF8
$ErrorActionPreference = "Stop"

$foxEmoji = [char]::ConvertFromUtf32(0x1F98A)
$tadaEmoji = [char]::ConvertFromUtf32(0x1F389)
$clipboardEmoji = [char]::ConvertFromUtf32(0x1F4CB)
$lockEmoji = [char]::ConvertFromUtf32(0x1F512)
$rocketEmoji = [char]::ConvertFromUtf32(0x1F680)
$packageEmoji = [char]::ConvertFromUtf32(0x1F4E6)

Write-Host @"
  =============================================================
     $foxEmoji AirFoxBT Release Builder & Hash Signer
     FoxDen Software
  =============================================================
"@ -ForegroundColor DarkCyan

$root = $PSScriptRoot
$csprojPath = Join-Path $root "AirFoxBT.csproj"
$publishDir = Join-Path $root "publish"

# 1. Determine Target Version
[xml]$projXml = Get-Content $csprojPath
$currentVersionNode = $projXml.Project.PropertyGroup.Version
$currentVersion = if ($currentVersionNode) { $currentVersionNode.Trim() } else { "1.0.1" }

if (-not $Version) {
    $Version = $currentVersion
}

$cleanVersion = $Version.Trim().TrimStart('v', 'V')
Write-Host "[*] Target Release Version: v$cleanVersion" -ForegroundColor Cyan

# 2. Update AirFoxBT.csproj Version & Companion Script Semvers
if (-not $DryRun) {
    Write-Host "[*] Synchronizing version in AirFoxBT.csproj..." -ForegroundColor Cyan
    $csprojContent = Get-Content $csprojPath -Raw
    $csprojContent = [regex]::Replace($csprojContent, "<Version>.*?</Version>", "<Version>$cleanVersion</Version>")
    $csprojContent = [regex]::Replace($csprojContent, "<AssemblyVersion>.*?</AssemblyVersion>", "<AssemblyVersion>$cleanVersion.0</AssemblyVersion>")
    $csprojContent = [regex]::Replace($csprojContent, "<FileVersion>.*?</FileVersion>", "<FileVersion>$cleanVersion.0</FileVersion>")
    Set-Content -Path $csprojPath -Value $csprojContent -Encoding UTF8

    Write-Host "[*] Synchronizing semver across installer & uninstaller scripts..." -ForegroundColor Cyan
    $installerFiles = @(
        (Join-Path $root "install.ps1"),
        (Join-Path $root "scripts\install.ps1")
    )
    foreach ($inst in $installerFiles) {
        if (Test-Path $inst) {
            $content = [System.IO.File]::ReadAllText($inst, [System.Text.Encoding]::UTF8)
            $content = [regex]::Replace($content, 'Version \d+\.\d+\.\d+.*FoxDen Software', "Version $cleanVersion `$([char]0x2022) FoxDen Software")
            $content = [regex]::Replace($content, '\$releaseTag = "v\d+\.\d+\.\d+"', "`$releaseTag = `"v$cleanVersion`"")
            [System.IO.File]::WriteAllText($inst, $content, [System.Text.Encoding]::UTF8)
        }
    }

    $uninstallerFiles = @(
        (Join-Path $root "uninstall.ps1"),
        (Join-Path $root "scripts\uninstall.ps1")
    )
    foreach ($uninst in $uninstallerFiles) {
        if (Test-Path $uninst) {
            $content = [System.IO.File]::ReadAllText($uninst, [System.Text.Encoding]::UTF8)
            $content = [regex]::Replace($content, 'Version \d+\.\d+\.\d+.*FoxDen Software', "Version $cleanVersion `$([char]0x2022) FoxDen Software")
            [System.IO.File]::WriteAllText($uninst, $content, [System.Text.Encoding]::UTF8)
        }
    }
}

# 3. Locate .NET SDK (prefer portable .dotnet in repo, AppData dotnet, then system PATH)
$localDotnet = Join-Path $root ".dotnet\dotnet.exe"
$appDataDotnet = "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe"

if (Test-Path $localDotnet) {
    $dotnetPath = (Resolve-Path $localDotnet).Path
    Write-Host "[*] Using workspace portable SDK : $dotnetPath" -ForegroundColor Gray
} elseif (Test-Path $appDataDotnet) {
    $dotnetPath = $appDataDotnet
    Write-Host "[*] Using AppData SDK           : $dotnetPath" -ForegroundColor Gray
} else {
    $dotnetPath = "dotnet"
    Write-Host "[*] Using system SDK            : dotnet (from PATH)" -ForegroundColor Gray
}

# 4. Clean and Publish Single-File Release
Write-Host "[*] Publishing single-file release executable (win-x64)..." -ForegroundColor Cyan

# Terminate running AirFoxBT instances so the single-file binary isn't locked
$running = Get-Process -Name "AirFoxBT" -ErrorAction SilentlyContinue
if ($running) {
    Write-Host "[-] Closing running AirFoxBT instance(s)..." -ForegroundColor Yellow
    $running | Stop-Process -Force
    Start-Sleep -Milliseconds 800
}

if (Test-Path $publishDir) {
    Get-ChildItem -Path $publishDir | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
} else {
    New-Item -ItemType Directory -Path $publishDir -Force | Out-Null
}

$publishArgs = @(
    "publish",
    $csprojPath,
    "-c", "Release",
    "-r", "win-x64",
    "--self-contained", "true",
    "-p:PublishSingleFile=true",
    "-p:IncludeNativeLibrariesForSelfExtract=true",
    "-p:EnableCompressionInSingleFile=true",
    "-o", $publishDir
)

& $dotnetPath $publishArgs
if ($LASTEXITCODE -ne 0) {
    Write-Host "`n[X] Build failed with exit code $LASTEXITCODE!" -ForegroundColor Red
    return
}

$targetExe = Join-Path $publishDir "AirFoxBT.exe"
if (-not (Test-Path $targetExe)) {
    Write-Host "`n[X] Published executable was not found at $targetExe!" -ForegroundColor Red
    return
}

# 5. Calculate SHA-256 Hash and generate checksum manifests
Write-Host "[*] Calculating cryptographic SHA-256 hashes..." -ForegroundColor Cyan
$hash = (Get-FileHash -Path $targetExe -Algorithm SHA256).Hash.ToLowerInvariant()
$fileSizeMb = [math]::Round(((Get-Item $targetExe).Length / 1MB), 2)

# Copy companion scripts to publish directory
$installSrc = Join-Path $root "install.ps1"
$uninstallSrc = Join-Path $root "uninstall.ps1"
$verifySrc = Join-Path $root "verify.ps1"
$notesSrc = Join-Path $root "scripts\release_notes.md"

$checksumEntries = [System.Collections.Generic.List[string]]::new()
$checksumEntries.Add("$hash *AirFoxBT.exe")

$installHash = $null
if (Test-Path $installSrc) {
    $dest = Join-Path $publishDir "install.ps1"
    Copy-Item $installSrc -Destination $dest -Force
    $installHash = (Get-FileHash -Path $dest -Algorithm SHA256).Hash.ToLowerInvariant()
    $checksumEntries.Add("$installHash *install.ps1")
}

$uninstallHash = $null
if (Test-Path $uninstallSrc) {
    $dest = Join-Path $publishDir "uninstall.ps1"
    Copy-Item $uninstallSrc -Destination $dest -Force
    $uninstallHash = (Get-FileHash -Path $dest -Algorithm SHA256).Hash.ToLowerInvariant()
    $checksumEntries.Add("$uninstallHash *uninstall.ps1")
}

$verifyHash = $null
if (Test-Path $verifySrc) {
    $dest = Join-Path $publishDir "verify.ps1"
    Copy-Item $verifySrc -Destination $dest -Force
    $verifyHash = (Get-FileHash -Path $dest -Algorithm SHA256).Hash.ToLowerInvariant()
    $checksumEntries.Add("$verifyHash *verify.ps1")
}

# Write checksum files
$sumsFile = Join-Path $publishDir "SHA256SUMS"
Set-Content -Path $sumsFile -Value ($checksumEntries -join "`r`n") -Encoding UTF8
Copy-Item $sumsFile -Destination (Join-Path $publishDir "SHA256SUMS.txt") -Force

$hashFile = Join-Path $publishDir "AirFoxBT.exe.sha256"
Set-Content -Path $hashFile -Value "$hash *AirFoxBT.exe" -Encoding UTF8

# Update release_notes.md with computed checksums
if (Test-Path $notesSrc) {
    $destNotes = Join-Path $publishDir "release_notes.md"
    Copy-Item $notesSrc -Destination $destNotes -Force

    $tableMarkdown = @"
| File | SHA-256 Checksum |
| :--- | :--- |
| ``AirFoxBT.exe`` | ``$hash`` |
| ``install.ps1`` | ``$installHash`` |
| ``uninstall.ps1`` | ``$uninstallHash`` |
| ``verify.ps1`` | ``$verifyHash`` |
"@

    $notesContent = Get-Content $destNotes -Raw
    $updatedNotes = [regex]::Replace($notesContent, "\| File \| SHA-256 Checksum \|[\s\S]*?(?=\r?\n\r?\nVerify integrity)", $tableMarkdown.Trim())
    Set-Content -Path $destNotes -Value $updatedNotes -Encoding UTF8
    Set-Content -Path $notesSrc -Value $updatedNotes -Encoding UTF8
}

# Clean loose subdirectories in publish
Get-ChildItem -Path $publishDir -Directory | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue

Write-Host "[+] Binary SHA-256:      $hash" -ForegroundColor Green
if ($installHash) { Write-Host "[+] Installer SHA-256:   $installHash" -ForegroundColor Green }
if ($uninstallHash) { Write-Host "[+] Uninstaller SHA-256: $uninstallHash" -ForegroundColor Green }
if ($verifyHash) { Write-Host "[+] Verifier SHA-256:    $verifyHash" -ForegroundColor Green }
Write-Host "[+] Checksums file:      $sumsFile" -ForegroundColor Green

# 6. Git Tagging guidance
Write-Host "`n=============================================================" -ForegroundColor Green
Write-Host "  $tadaEmoji RELEASE v$cleanVersion BUILT SUCCESSFULLY!" -ForegroundColor Green
Write-Host "=============================================================" -ForegroundColor Green
Write-Host "  Output Binary:    $targetExe ($fileSizeMb MB)"
Write-Host "  SHA-256 Hash:     $hash"
Write-Host "  Publish Folder:   $publishDir"

Write-Host @"

  -------------------------------------------------------------
  $packageEmoji GITHUB RELEASE ASSETS TO UPLOAD:
  -------------------------------------------------------------
  1. publish\AirFoxBT.exe
  2. publish\AirFoxBT.exe.sha256
  3. publish\SHA256SUMS
  4. publish\install.ps1
  5. publish\uninstall.ps1
  6. publish\verify.ps1
  7. publish\release_notes.md
"@

