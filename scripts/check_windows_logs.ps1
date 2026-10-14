$startTime = (Get-Date).AddHours(-2)
Write-Output "--- Checking Windows Application Logs since $startTime ---"

$appEvents = Get-WinEvent -FilterHashtable @{LogName='Application'; StartTime=$startTime} -ErrorAction SilentlyContinue | 
    Where-Object { 
        $_.Message -like '*AirFoxBT*' -or 
        $_.ProviderName -in @('Application Error', '.NET Runtime', 'Windows Error Reporting') 
    }

if ($appEvents) {
    $appEvents | Select-Object -First 10 | Format-List TimeCreated, ProviderName, Id, Message
} else {
    Write-Output "NO_MATCHING_APPLICATION_EVENTS: No crashes or errors recorded in Windows Application log."
}

Write-Output "--- Checking Windows Error Reporting (WER) ReportArchive ---"
$werPath = "$env:LOCALAPPDATA\CrashDumps"
if (Test-Path $werPath) {
    Get-ChildItem $werPath -Filter "*AirFoxBT*" | Format-Table Name, Length, LastWriteTime
} else {
    Write-Output "No CrashDumps folder found."
}

$werReports = "$env:LOCALAPPDATA\Microsoft\Windows\WER\ReportArchive"
if (Test-Path $werReports) {
    Get-ChildItem $werReports -Recurse -Filter "*AirFoxBT*" -ErrorAction SilentlyContinue | Format-Table FullName, LastWriteTime
}

Write-Output "--- Checking Any Debug / App Log Files ---"
$localAppData = "$env:LOCALAPPDATA\AirFoxBT"
if (Test-Path $localAppData) {
    Get-ChildItem $localAppData -Recurse | Format-Table FullName, Length, LastWriteTime
} else {
    Write-Output "No $localAppData folder."
}

$appData = "$env:APPDATA\AirFoxBT"
if (Test-Path $appData) {
    Get-ChildItem $appData -Recurse | Format-Table FullName, Length, LastWriteTime
} else {
    Write-Output "No $appData folder."
}
