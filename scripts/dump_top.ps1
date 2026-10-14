$events = Get-WinEvent -FilterHashtable @{LogName='Application'; ProviderName='.NET Runtime'} -MaxEvents 5
foreach ($evt in $events) {
    if ($evt.Message -like "*AirFoxBT*") {
        Write-Output "=== Time: $($evt.TimeCreated) ==="
        $lines = $evt.Message -split "`r?`n"
        $lines[0..40] | ForEach-Object { Write-Output $_ }
        break
    }
}
