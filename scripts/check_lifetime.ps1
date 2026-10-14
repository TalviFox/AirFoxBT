Write-Output "--- Starting dist\AirFoxBT.exe ---"
$proc = Start-Process -FilePath ".\dist\AirFoxBT.exe" -PassThru
Start-Sleep -Seconds 1

Write-Output "HasExited: $($proc.HasExited)"
if ($proc.HasExited) {
    Write-Output "ExitCode: $($proc.ExitCode)"
} else {
    Write-Output "Running! PID=$($proc.Id)"
    Get-Process -Id $proc.Id | Format-List *
    Stop-Process -Id $proc.Id -Force
}

Write-Output "--- Checking latest Windows Application Error / .NET Runtime logs ---"
$events = Get-WinEvent -FilterHashtable @{LogName='Application'; StartTime=(Get-Date).AddMinutes(-5)} -ErrorAction SilentlyContinue
foreach ($evt in $events) {
    if ($evt.ProviderName -in @('Application Error', '.NET Runtime', 'Windows Error Reporting') -or $evt.Message -like '*AirFoxBT*') {
        Write-Output "[$($evt.TimeCreated)] [$($evt.ProviderName)] $($evt.Message)"
        Write-Output "--------------------------------------------------"
    }
}
