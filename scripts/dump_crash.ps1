$events = Get-WinEvent -FilterHashtable @{LogName='Application'; ProviderName='.NET Runtime'} -MaxEvents 10
foreach ($evt in $events) {
    if ($evt.Message -like "*AirFoxBT*") {
        Write-Output "=== .NET Runtime Event at $($evt.TimeCreated) ==="
        Write-Output $evt.Message
        Write-Output "================================================"
    }
}
