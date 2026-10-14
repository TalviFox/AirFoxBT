<#
.SYNOPSIS
    Builds and publishes AirFoxBT as a single-file, zero-dependency, self-contained Windows executable.
.DESCRIPTION
    Compiles the project with all .NET runtime dependencies, BCL libraries, and native DLLs
    bundled into a single standalone AirFoxBT.exe binary.
.PARAMETER Configuration
    Build configuration: "Release" (default) or "Debug".
.PARAMETER RuntimeIdentifier
    Target runtime identifier: "win-x64" (default) or "win-arm64".
.PARAMETER OutputDir
    Destination folder for published binaries. Default is "publish/".
.EXAMPLE
    .\Publish-SelfContained.ps1
.EXAMPLE
    .\Publish-SelfContained.ps1 -Configuration Release -OutputDir ".\publish"
#>

[CmdletBinding()]
param (
    [string]$Configuration = "Release",
    [string]$RuntimeIdentifier = "win-x64",
    [string]$OutputDir = ""
)

$ErrorActionPreference = "Stop"

$ScriptDir = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $MyInvocation.MyCommand.Definition }
if ([string]::IsNullOrWhiteSpace($OutputDir)) {
    $OutputDir = Join-Path $ScriptDir "..\publish"
}

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "  🦊 AirFoxBT -- Self-Contained Single-File Packager" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

# Locate dotnet (prefer portable .dotnet in repo, AppData dotnet, then system PATH)
$LocalDotnet = Join-Path $ScriptDir "..\.dotnet\dotnet.exe"
$AppDataDotnet = "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe"

if (Test-Path $LocalDotnet) {
    $DotnetCmd = (Resolve-Path $LocalDotnet).Path
    Write-Host "[-] Using portable SDK : $DotnetCmd" -ForegroundColor Gray
} elseif (Test-Path $AppDataDotnet) {
    $DotnetCmd = $AppDataDotnet
    Write-Host "[-] Using AppData SDK  : $DotnetCmd" -ForegroundColor Gray
} else {
    $DotnetCmd = "dotnet"
    Write-Host "[-] Using system SDK   : dotnet (from PATH)" -ForegroundColor Gray
}

$ProjectRoot = [System.IO.Path]::GetFullPath((Join-Path $ScriptDir ".."))
$Csproj = Get-ChildItem -Path $ProjectRoot -Filter "*.csproj" -Recurse | Select-Object -First 1

if (-not $Csproj) {
    Write-Warning "No .csproj found yet in $ProjectRoot. Create your project first!"
    return
}

$ResolvedOutputDir = [System.IO.Path]::GetFullPath($OutputDir)
Write-Host "[-] Project            : $($Csproj.FullName)" -ForegroundColor Yellow
Write-Host "[-] Configuration      : $Configuration" -ForegroundColor Yellow
Write-Host "[-] Runtime Identifier : $RuntimeIdentifier" -ForegroundColor Yellow
Write-Host "[-] Output Directory   : $ResolvedOutputDir" -ForegroundColor Yellow

# Terminate any running instances so the single-file binary isn't locked
$running = Get-Process -Name "AirFoxBT" -ErrorAction SilentlyContinue
if ($running) {
    Write-Host "[-] Closing running AirFoxBT instance(s)..." -ForegroundColor Yellow
    $running | Stop-Process -Force
    Start-Sleep -Milliseconds 800
}

$PublishArgs = @(
    "publish",
    $Csproj.FullName,
    "-c", $Configuration,
    "-r", $RuntimeIdentifier,
    "--self-contained", "true",
    "-p:PublishSingleFile=true",
    "-p:IncludeNativeLibrariesForSelfExtract=true",
    "-p:EnableCompressionInSingleFile=true",
    "-o", $ResolvedOutputDir
)

$ArgString = $PublishArgs -join " "
Write-Host "`n[-] Running: $DotnetCmd $ArgString..." -ForegroundColor Cyan

& $DotnetCmd @PublishArgs

if ($LASTEXITCODE -eq 0) {
    Write-Host "`n[+] Self-contained build succeeded!" -ForegroundColor Green
    $Exe = Join-Path $ResolvedOutputDir "AirFoxBT.exe"
    if (Test-Path $Exe) {
        $Item = Get-Item $Exe
        $SizeMb = [Math]::Round(($Item.Length / 1MB), 2)
        Write-Host "[+] Standalone binary : $Exe ($SizeMb MB)" -ForegroundColor Green

        # Generate official SHA-256 checksum files for release & integrity audit
        $hash = (Get-FileHash -Path $Exe -Algorithm SHA256).Hash.ToLowerInvariant()
        $shaFile = Join-Path $ResolvedOutputDir "AirFoxBT.exe.sha256"
        $sumsFile = Join-Path $ResolvedOutputDir "SHA256SUMS"

        Set-Content -Path $shaFile -Value "$hash *AirFoxBT.exe" -Encoding UTF8
        Set-Content -Path $sumsFile -Value "$hash *AirFoxBT.exe" -Encoding UTF8

        # Deploy companion release tools matching FoxDen standard (inFaux & WireFox)
        $companionScripts = @("install.ps1", "uninstall.ps1", "verify.ps1", "release_notes.md")
        foreach ($script in $companionScripts) {
            $src = Join-Path $ScriptDir $script
            if (Test-Path $src) {
                Copy-Item -Path $src -Destination (Join-Path $ResolvedOutputDir $script) -Force
            }
        }

        # Clean up any loose subfolders in publish
        Get-ChildItem -Path $ResolvedOutputDir -Directory | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue

        Write-Host "[+] SHA-256 Checksum  : $hash" -ForegroundColor Cyan
        Write-Host "`n==========================================================" -ForegroundColor Green
        Write-Host "  FoxDen Release Package Contents:" -ForegroundColor Green
        Write-Host "==========================================================" -ForegroundColor Green
        Get-ChildItem -Path $ResolvedOutputDir | Format-Table Name, Length, LastWriteTime -AutoSize | Out-String | Write-Host -ForegroundColor Gray
        Write-Host "[+] Ready to distribute or upload to GitHub Releases!" -ForegroundColor Green
    }
} else {
    Write-Error "Build failed with exit code $LASTEXITCODE"
}
