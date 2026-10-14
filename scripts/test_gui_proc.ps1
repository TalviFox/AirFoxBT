try {
    Write-Output "Loading assembly..."
    $bytes = [System.IO.File]::ReadAllBytes("C:\Users\Talvi\Documents\IDE-Scratch\AirFoxBT\bin\Release\net10.0-windows10.0.19041.0\win-x64\AirFoxBT.dll")
    $asm = [System.Reflection.Assembly]::Load($bytes)
    Write-Output "Assembly loaded: $($asm.FullName)"
    
    # Or run the executable and capture stdout/stderr directly
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = "C:\Users\Talvi\Documents\IDE-Scratch\AirFoxBT\dist\AirFoxBT.exe"
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.CreateNoWindow = $true

    $p = [System.Diagnostics.Process]::Start($psi)
    Start-Sleep -Seconds 3

    Write-Output "Is running: $(-not $p.HasExited)"
    if ($p.HasExited) {
        Write-Output "Exit Code: $($p.ExitCode)"
        Write-Output "StdOut: $($p.StandardOutput.ReadToEnd())"
        Write-Output "StdErr: $($p.StandardError.ReadToEnd())"
    } else {
        Write-Output "Process is alive. MainWindowHandle: $($p.MainWindowHandle)"
        $p.Kill()
    }
} catch {
    Write-Output "Error: $_"
}
