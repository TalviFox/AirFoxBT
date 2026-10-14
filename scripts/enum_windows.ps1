Add-Type @"
  using System;
  using System.Runtime.InteropServices;
  using System.Text;

  public class WinEnum {
      public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

      [DllImport("user32.dll")]
      public static extern bool EnumWindows(EnumWindowsProc enumProc, IntPtr lParam);

      [DllImport("user32.dll", SetLastError = true)]
      public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

      [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
      public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

      [DllImport("user32.dll")]
      public static extern bool IsWindowVisible(IntPtr hWnd);
  }
"@

$proc = Start-Process -FilePath ".\dist\AirFoxBT.exe" -PassThru
Start-Sleep -Seconds 2

Write-Output "PID: $($proc.Id)"

[WinEnum]::EnumWindows({
    param($hwnd, $lparam)
    [uint32]$wPid = 0
    [WinEnum]::GetWindowThreadProcessId($hwnd, [ref]$wPid)
    if ($wPid -eq $proc.Id) {
        $sb = New-Object System.Text.StringBuilder 256
        [WinEnum]::GetWindowText($hwnd, $sb, 256)
        $vis = [WinEnum]::IsWindowVisible($hwnd)
        Write-Output "Found HWND $hwnd | Title: '$($sb.ToString())' | Visible: $vis"
    }
    return $true
}, [IntPtr]::Zero)

Start-Sleep -Seconds 1
Stop-Process -Id $proc.Id -Force
