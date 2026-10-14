<#
.SYNOPSIS
    Downloads and installs a portable or system-level .NET SDK on demand.
.DESCRIPTION
    Uses Microsoft's official dotnet-install script to download and unpack any version
    of the .NET SDK (e.g., 10.0, 9.0, 8.0, or LTS) without requiring admin rights.
    Can install into a local project folder, user profile, or system PATH.
.PARAMETER Channel
    The .NET version channel: "10.0", "9.0", "8.0", "LTS", or "STS". Default is "10.0".
.PARAMETER InstallDir
    Target directory. Default is a portable ".dotnet" folder inside the project root.
.PARAMETER AddToUserPath
    If specified, adds the installation directory to the persistent User PATH environment variable.
.EXAMPLE
    .\Install-DotNet.ps1
    Installs .NET 10.0 SDK into .\.dotnet and sets up PATH for the current session.
.EXAMPLE
    .\Install-DotNet.ps1 -Channel 9.0 -AddToUserPath
    Installs .NET 9.0 SDK and permanently adds it to the user's PATH.
#>

[CmdletBinding()]
param (
    [string]$Channel = "10.0",
    [string]$InstallDir = "",
    [switch]$AddToUserPath
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($InstallDir)) {
    $ScriptDir = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $MyInvocation.MyCommand.Definition }
    $InstallDir = Join-Path $ScriptDir "..\.dotnet"
}

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "  🦊 FoxDen .NET SDK Portable Bootstrapper" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

$ResolvedInstallDir = [System.IO.Path]::GetFullPath($InstallDir)
Write-Host "[*] Target Channel   : $Channel" -ForegroundColor Yellow
Write-Host "[*] Target Directory : $ResolvedInstallDir" -ForegroundColor Yellow

if (-not (Test-Path $ResolvedInstallDir)) {
    New-Item -ItemType Directory -Path $ResolvedInstallDir -Force | Out-Null
}

$ScriptUrl = "https://dot.net/v1/dotnet-install.ps1"
$TempScript = Join-Path $env:TEMP "dotnet-install-$([Guid]::NewGuid().ToString('N')).ps1"

try {
    Write-Host "[*] Downloading official Microsoft dotnet-install script..." -ForegroundColor Gray
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12 -bor [Net.SecurityProtocolType]::Tls13
    Invoke-WebRequest -Uri $ScriptUrl -OutFile $TempScript -UseBasicParsing

    Write-Host "[*] Installing .NET SDK (Channel: $Channel, Architecture: x64)..." -ForegroundColor Gray
    
    # Run installer script
    & $TempScript -Channel $Channel -Architecture x64 -InstallDir $ResolvedInstallDir -NoPath

    if ($LASTEXITCODE -ne 0 -and $null -ne $LASTEXITCODE) {
        Write-Warning "Installation finished with non-zero exit code: $LASTEXITCODE"
    }

    $DotnetExe = Join-Path $ResolvedInstallDir "dotnet.exe"
    if (Test-Path $DotnetExe) {
        Write-Host "`n[+] .NET SDK successfully installed!" -ForegroundColor Green
        
        # Test invocation
        $Version = & $DotnetExe --version
        Write-Host "[+] Installed Version: $Version" -ForegroundColor Green

        # Configure session PATH
        if ($env:PATH -notlike "*$ResolvedInstallDir*") {
            $env:PATH = "$ResolvedInstallDir;$env:PATH"
            Write-Host "[+] Added to current PowerShell session PATH." -ForegroundColor Gray
        }

        # Optional persistent User PATH
        if ($AddToUserPath) {
            $CurrentPath = [Environment]::GetEnvironmentVariable("PATH", "User")
            if ($CurrentPath -notlike "*$ResolvedInstallDir*") {
                [Environment]::SetEnvironmentVariable("PATH", "$ResolvedInstallDir;$CurrentPath", "User")
                Write-Host "[+] Added permanently to User PATH!" -ForegroundColor Green
            }
        }
    } else {
        throw "Could not locate dotnet.exe in $ResolvedInstallDir"
    }
}
catch {
    Write-Error "Failed to install .NET SDK: $_"
    # Fallback suggestion if 10.0 channel had an issue
    if ($Channel -eq "10.0") {
        Write-Host "`nTip: If .NET 10 is in preview or not yet published on the generic 10.0 channel, try: .\Install-DotNet.ps1 -Channel 9.0" -ForegroundColor DarkCyan
    }
}
finally {
    if (Test-Path $TempScript) {
        Remove-Item $TempScript -Force -ErrorAction SilentlyContinue
    }
}
