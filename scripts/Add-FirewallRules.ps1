#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Configures inbound Windows Defender Firewall rules for AirFoxBT.
.DESCRIPTION
    Allows Home Assistant native ESPHome proxy connections (TCP port 6053)
    and embedded REST/SSE telemetry access (TCP port 8766).
    Designed for Windows Server and non-admin setups where an administrator
    must grant inbound network access.
#>

$RuleName = "AirFoxBT Bluetooth Proxy"
$EsphomePort = 6053
$ApiPort = 8766

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "  🦊 AirFoxBT | Windows Firewall Configuration Script" -ForegroundColor Yellow
Write-Host "==========================================================" -ForegroundColor Cyan

# Remove any obsolete or duplicate rules
Get-NetFirewallRule -DisplayName "$RuleName*" -ErrorAction SilentlyContinue | Remove-NetFirewallRule -ErrorAction SilentlyContinue

try {
    # Add modern NetSecurity rule
    New-NetFirewallRule `
        -DisplayName $RuleName `
        -Description "AirFoxBT ESPHome Proxy (6053) and REST Telemetry API (8766)" `
        -Direction Inbound `
        -LocalPort $EsphomePort, $ApiPort `
        -Protocol TCP `
        -Action Allow `
        -Profile Any `
        -Enabled True | Out-Null

    Write-Host "[+] Inbound firewall rule '$RuleName' created successfully!" -ForegroundColor Green
    Write-Host "[+] Allowed Ports: TCP $EsphomePort (ESPHome) & TCP $ApiPort (REST)" -ForegroundColor Green
}
catch {
    Write-Warning "[-] PowerShell NetSecurity cmdlet failed, falling back to netsh..."
    netsh advfirewall firewall add rule name="$RuleName" dir=in action=allow protocol=TCP localport="$EsphomePort,$ApiPort" | Out-Null
    Write-Host "[+] Netsh firewall rule added." -ForegroundColor Green
}

# Reserve REST API port for non-admin user execution across network interfaces
try {
    netsh http add urlacl url="http://*:$ApiPort/" user=Everyone | Out-Null
    Write-Host "[+] REST API urlacl reserved (http://*:$ApiPort/)" -ForegroundColor Green
} catch {
    # If already exists, ignore
}

Write-Host "`nReady for incoming Home Assistant connections!" -ForegroundColor Gray
