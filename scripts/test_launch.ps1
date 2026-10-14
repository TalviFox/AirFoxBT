$proc = Start-Process -FilePath ".\dist\AirFoxBT.exe" -PassThru
Start-Sleep -Seconds 3
Get-Process -Id $proc.Id | Select-Object Id, ProcessName, MainWindowTitle, MainWindowHandle, Responding
Stop-Process -Id $proc.Id -Force
