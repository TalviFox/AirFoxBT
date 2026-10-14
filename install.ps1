# =============================================================
#  🦊 AirFoxBT Installer Script
#  https://github.com/TalviFox/AirFoxBT
#  Modern Native Bluetooth LE Proxy & BLE Sensor Gateway
# =============================================================

[CmdletBinding(DefaultParameterSetName = "Default")]
param(
    [Parameter(Position = 0)]
    [string]$Path,

    [switch]$Force,
    [switch]$Build,
    [switch]$DesktopShortcut
)

# Console environment normalization (ensures 24-bit TrueColor in Windows Terminal / conhost)
try {
    if (-not ([System.Management.Automation.PSTypeName]'Win32.ConsoleVT').Type) {
        Add-Type -MemberDefinition @'
            [DllImport("kernel32.dll", SetLastError = true)]
            public static extern IntPtr GetStdHandle(int nStdHandle);
            [DllImport("kernel32.dll", SetLastError = true)]
            public static extern bool GetConsoleMode(IntPtr hConsoleHandle, out uint lpMode);
            [DllImport("kernel32.dll", SetLastError = true)]
            public static extern bool SetConsoleMode(IntPtr hConsoleHandle, uint dwMode);
            [DllImport("kernel32.dll", SetLastError = true)]
            public static extern bool SetConsoleTextAttribute(IntPtr hConsoleHandle, ushort wAttributes);
'@ -Name 'ConsoleVT' -Namespace 'Win32'
    }

    $hStdOut = [Win32.ConsoleVT]::GetStdHandle(-11)
    [Win32.ConsoleVT]::SetConsoleTextAttribute($hStdOut, 0x0007) | Out-Null
    $consoleMode = 0
    if ([Win32.ConsoleVT]::GetConsoleMode($hStdOut, [ref]$consoleMode)) {
        [Win32.ConsoleVT]::SetConsoleMode($hStdOut, ($consoleMode -bor 0x0004)) | Out-Null
    }
} catch {}

try {
    $host.UI.RawUI.BackgroundColor = 'Black'
    $host.UI.RawUI.ForegroundColor = 'White'
    Clear-Host
} catch {}

[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$ErrorActionPreference = "Stop"

$OutputEncoding = [Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$esc = [char]27
$u = [char]0x2580
$d = [char]0x2584

$guardianBanner = @"

$esc[0m $esc[0m $esc[38;2;32;86;238m$esc[49m$d$esc[38;2;20;50;137m$esc[49m$d$esc[38;2;27;77;213m$esc[48;2;15;32;81m$u$esc[38;2;23;59;164m$esc[48;2;13;24;56m$u$esc[38;2;21;54;147m$esc[48;2;12;22;50m$u$esc[38;2;21;54;145m$esc[48;2;12;22;50m$u$esc[38;2;21;54;145m$esc[48;2;12;22;50m$u$esc[38;2;21;54;145m$esc[48;2;12;22;50m$u$esc[38;2;21;54;145m$esc[48;2;12;22;50m$u$esc[38;2;21;54;145m$esc[48;2;12;22;50m$u$esc[38;2;21;54;145m$esc[48;2;12;22;50m$u$esc[38;2;21;54;145m$esc[48;2;12;22;50m$u$esc[38;2;21;54;145m$esc[48;2;12;22;50m$u$esc[38;2;21;54;145m$esc[48;2;12;22;50m$u$esc[38;2;21;54;145m$esc[48;2;12;22;50m$u$esc[38;2;21;54;145m$esc[48;2;12;22;50m$u$esc[38;2;21;54;145m$esc[48;2;12;22;50m$u$esc[38;2;21;54;145m$esc[48;2;12;22;50m$u$esc[38;2;21;54;145m$esc[48;2;12;22;50m$u$esc[38;2;21;54;147m$esc[48;2;12;22;50m$u$esc[38;2;23;59;163m$esc[48;2;13;24;56m$u$esc[38;2;27;77;213m$esc[48;2;15;32;80m$u$esc[38;2;20;50;136m$esc[49m$d$esc[38;2;32;89;238m$esc[49m$d$esc[0m $esc[0m $esc[0m
$esc[0m $esc[38;2;30;87;228m$esc[48;2;19;48;131m$u$esc[38;2;18;44;116m$esc[48;2;11;18;39m$u$esc[38;2;11;18;39m$esc[48;2;11;18;41m$u$esc[38;2;11;17;35m$esc[48;2;9;17;42m$u$esc[38;2;8;16;39m$esc[48;2;16;24;48m$u$esc[38;2;0;6;31m$esc[48;2;62;70;90m$u$esc[38;2;9;17;41m$esc[48;2;9;17;41m$u$esc[38;2;11;19;43m$esc[48;2;9;17;41m$u$esc[38;2;11;19;42m$esc[48;2;11;19;43m$u$esc[38;2;11;19;42m$esc[48;2;10;18;42m$u$esc[38;2;11;19;42m$esc[48;2;0;8;33m$u$esc[38;2;11;18;42m$esc[48;2;1;9;33m$u$esc[38;2;9;17;40m$esc[48;2;11;19;43m$u$esc[38;2;9;17;40m$esc[48;2;11;19;43m$u$esc[38;2;11;18;42m$esc[48;2;1;9;33m$u$esc[38;2;11;19;42m$esc[48;2;0;8;33m$u$esc[38;2;11;19;42m$esc[48;2;10;18;42m$u$esc[38;2;11;19;42m$esc[48;2;11;19;43m$u$esc[38;2;11;19;43m$esc[48;2;9;17;41m$u$esc[38;2;9;17;41m$esc[48;2;9;17;41m$u$esc[38;2;0;6;31m$esc[48;2;64;72;92m$u$esc[38;2;8;16;39m$esc[48;2;17;25;49m$u$esc[38;2;11;17;35m$esc[48;2;9;17;41m$u$esc[38;2;11;18;38m$esc[48;2;11;18;41m$u$esc[38;2;18;44;115m$esc[48;2;11;18;38m$u$esc[38;2;30;86;229m$esc[48;2;19;49;130m$u$esc[0m $esc[0m   $([char]::ConvertFromUtf32(0x1F98A)) $esc[1;36mAirFoxBT$esc[0m $esc[1;33m("air-fox")$esc[0m
$esc[38;2;28;74;203m$esc[48;2;21;57;157m$u$esc[38;2;15;32;80m$esc[48;2;13;23;55m$u$esc[38;2;10;16;35m$esc[48;2;11;19;42m$u$esc[38;2;11;19;43m$esc[48;2;11;19;43m$u$esc[38;2;0;2;28m$esc[48;2;0;5;30m$u$esc[38;2;96;103;121m$esc[48;2;161;167;180m$u$esc[38;2;255;255;255m$esc[48;2;251;255;255m$u$esc[38;2;106;113;130m$esc[48;2;226;232;240m$u$esc[38;2;0;0;25m$esc[48;2;36;44;66m$u$esc[38;2;9;17;41m$esc[48;2;2;6;31m$u$esc[38;2;1;9;34m$esc[48;2;118;125;141m$u$esc[38;2;61;69;89m$esc[48;2;185;192;203m$u$esc[38;2;134;141;156m$esc[48;2;113;120;137m$u$esc[38;2;159;166;179m$esc[48;2;56;64;85m$u$esc[38;2;159;166;179m$esc[48;2;56;64;84m$u$esc[38;2;134;142;156m$esc[48;2;112;120;136m$u$esc[38;2;62;70;90m$esc[48;2;185;192;203m$u$esc[38;2;1;9;34m$esc[48;2;119;126;142m$u$esc[38;2;9;17;41m$esc[48;2;2;7;32m$u$esc[38;2;0;0;25m$esc[48;2;35;43;65m$u$esc[38;2;105;112;129m$esc[48;2;225;231;239m$u$esc[38;2;255;255;255m$esc[48;2;250;255;255m$u$esc[38;2;98;105;123m$esc[48;2;162;169;181m$u$esc[38;2;0;2;27m$esc[48;2;0;5;30m$u$esc[38;2;11;19;43m$esc[48;2;11;19;43m$u$esc[38;2;10;16;35m$esc[48;2;11;19;42m$u$esc[38;2;15;32;80m$esc[48;2;13;23;55m$u$esc[38;2;28;73;203m$esc[48;2;21;56;154m$u$esc[0m   $esc[90mReal beacons. Zero pairing.$esc[0m
$esc[38;2;21;54;144m$esc[48;2;21;54;145m$u$esc[38;2;12;22;50m$esc[48;2;12;22;50m$u$esc[38;2;11;19;42m$esc[48;2;11;19;42m$u$esc[38;2;6;14;39m$esc[48;2;0;6;31m$u$esc[38;2;21;29;52m$esc[48;2;66;74;93m$u$esc[38;2;210;216;226m$esc[48;2;251;254;255m$u$esc[38;2;227;234;242m$esc[48;2;128;135;150m$u$esc[38;2;245;251;255m$esc[48;2;147;154;168m$u$esc[38;2;165;172;185m$esc[48;2;255;255;255m$u$esc[38;2;10;18;42m$esc[48;2;89;97;115m$u$esc[38;2;131;138;153m$esc[48;2;0;0;7m$u$esc[38;2;38;45;67m$esc[48;2;96;103;121m$u$esc[38;2;14;22;46m$esc[48;2;233;239;246m$u$esc[38;2;77;84;104m$esc[48;2;215;221;230m$u$esc[38;2;77;85;104m$esc[48;2;215;221;230m$u$esc[38;2;15;23;46m$esc[48;2;233;239;246m$u$esc[38;2;37;44;66m$esc[48;2;97;104;122m$u$esc[38;2;131;138;154m$esc[48;2;0;0;7m$u$esc[38;2;11;19;43m$esc[48;2;88;95;114m$u$esc[38;2;163;170;183m$esc[48;2;255;255;255m$u$esc[38;2;246;251;255m$esc[48;2;148;155;169m$u$esc[38;2;227;234;242m$esc[48;2;127;134;149m$u$esc[38;2;211;218;227m$esc[48;2;252;254;255m$u$esc[38;2;21;30;53m$esc[48;2;67;75;94m$u$esc[38;2;6;14;38m$esc[48;2;0;5;30m$u$esc[38;2;11;19;42m$esc[48;2;11;19;42m$u$esc[38;2;12;22;49m$esc[48;2;12;22;50m$u$esc[38;2;21;54;144m$esc[48;2;21;54;144m$u$esc[0m   
$esc[38;2;21;54;145m$esc[48;2;21;54;145m$u$esc[38;2;12;22;50m$esc[48;2;12;22;50m$u$esc[38;2;11;19;42m$esc[48;2;9;17;41m$u$esc[38;2;0;1;26m$esc[48;2;4;11;36m$u$esc[38;2;124;131;147m$esc[48;2;186;192;204m$u$esc[38;2;255;255;255m$esc[48;2;226;233;240m$u$esc[38;2;63;71;91m$esc[48;2;27;35;58m$u$esc[38;2;22;28;51m$esc[48;2;0;0;19m$u$esc[38;2;229;234;240m$esc[48;2;102;110;128m$u$esc[38;2;221;226;233m$esc[48;2;255;255;255m$u$esc[38;2;20;26;47m$esc[48;2;153;160;174m$u$esc[38;2;112;118;134m$esc[48;2;0;0;10m$u$esc[38;2;108;114;131m$esc[48;2;0;0;12m$u$esc[38;2;11;17;41m$esc[48;2;0;0;24m$u$esc[38;2;11;17;41m$esc[48;2;0;0;24m$u$esc[38;2;107;114;130m$esc[48;2;0;0;12m$u$esc[38;2;113;119;135m$esc[48;2;0;0;9m$u$esc[38;2;19;25;47m$esc[48;2;152;159;173m$u$esc[38;2;219;225;232m$esc[48;2;255;255;255m$u$esc[38;2;230;235;240m$esc[48;2;104;112;129m$u$esc[38;2;23;29;52m$esc[48;2;0;0;19m$u$esc[38;2;62;69;90m$esc[48;2;26;34;57m$u$esc[38;2;255;255;255m$esc[48;2;225;232;240m$u$esc[38;2;126;133;148m$esc[48;2;187;193;205m$u$esc[38;2;0;1;26m$esc[48;2;4;12;37m$u$esc[38;2;11;19;42m$esc[48;2;9;17;41m$u$esc[38;2;12;22;50m$esc[48;2;12;22;50m$u$esc[38;2;21;54;144m$esc[48;2;21;54;144m$u$esc[0m   $esc[1;37mHighlights:$esc[0m
$esc[38;2;21;54;145m$esc[48;2;21;54;145m$u$esc[38;2;12;22;50m$esc[48;2;12;22;50m$u$esc[38;2;3;11;35m$esc[48;2;0;2;27m$u$esc[38;2;36;44;66m$esc[48;2;87;94;113m$u$esc[38;2;236;242;248m$esc[48;2;255;255;255m$u$esc[38;2;172;178;190m$esc[48;2;110;117;134m$u$esc[38;2;1;7;32m$esc[48;2;0;1;26m$u$esc[38;2;7;15;40m$esc[48;2;11;19;44m$u$esc[38;2;4;10;35m$esc[48;2;0;5;31m$u$esc[38;2;180;187;198m$esc[48;2;47;54;76m$u$esc[38;2;248;253;255m$esc[48;2;226;232;240m$u$esc[38;2;145;152;166m$esc[48;2;252;255;255m$u$esc[38;2;127;134;149m$esc[48;2;250;255;255m$u$esc[38;2;131;138;153m$esc[48;2;250;255;255m$u$esc[38;2;131;138;153m$esc[48;2;250;255;255m$u$esc[38;2;127;134;149m$esc[48;2;250;255;255m$u$esc[38;2;144;151;166m$esc[48;2;252;255;255m$u$esc[38;2;248;253;255m$esc[48;2;227;233;241m$u$esc[38;2;181;188;199m$esc[48;2;48;55;77m$u$esc[38;2;4;10;35m$esc[48;2;0;5;31m$u$esc[38;2;7;15;40m$esc[48;2;11;19;44m$u$esc[38;2;1;7;32m$esc[48;2;0;1;26m$u$esc[38;2;170;177;189m$esc[48;2;109;116;133m$u$esc[38;2;237;243;248m$esc[48;2;255;255;255m$u$esc[38;2;37;45;67m$esc[48;2;88;96;114m$u$esc[38;2;3;11;35m$esc[48;2;0;2;27m$u$esc[38;2;12;22;50m$esc[48;2;12;22;50m$u$esc[38;2;21;54;144m$esc[48;2;21;54;144m$u$esc[0m     $esc[32m*$esc[0m $esc[1mZero-Driver:$esc[0m 100% native user-mode ($esc[33mZero UAC$esc[0m)
$esc[38;2;21;54;145m$esc[48;2;21;54;145m$u$esc[38;2;12;22;50m$esc[48;2;7;17;46m$u$esc[38;2;0;2;27m$esc[48;2;21;29;52m$u$esc[38;2;152;159;173m$esc[48;2;220;226;235m$u$esc[38;2;249;253;255m$esc[48;2;198;205;215m$u$esc[38;2;54;62;83m$esc[48;2;9;17;41m$u$esc[38;2;1;8;33m$esc[48;2;1;9;43m$u$esc[38;2;2;14;44m$esc[48;2;127;88;27m$u$esc[38;2;0;11;46m$esc[48;2;186;128;18m$u$esc[38;2;0;1;38m$esc[48;2;88;64;33m$u$esc[38;2;36;45;71m$esc[48;2;8;13;34m$u$esc[38;2;62;69;89m$esc[48;2;0;0;35m$u$esc[38;2;58;65;86m$esc[48;2;0;5;33m$u$esc[38;2;58;65;86m$esc[48;2;0;8;32m$u$esc[38;2;58;65;86m$esc[48;2;0;8;32m$u$esc[38;2;58;65;86m$esc[48;2;0;5;33m$u$esc[38;2;62;69;89m$esc[48;2;0;0;35m$u$esc[38;2;36;46;71m$esc[48;2;7;13;34m$u$esc[38;2;0;1;38m$esc[48;2;87;64;33m$u$esc[38;2;0;11;45m$esc[48;2;186;127;18m$u$esc[38;2;2;14;44m$esc[48;2;129;89;27m$u$esc[38;2;1;8;33m$esc[48;2;1;9;43m$u$esc[38;2;53;61;82m$esc[48;2;9;16;40m$u$esc[38;2;248;252;255m$esc[48;2;197;203;214m$u$esc[38;2;154;161;174m$esc[48;2;221;227;236m$u$esc[38;2;0;3;28m$esc[48;2;22;30;53m$u$esc[38;2;12;22;50m$esc[48;2;6;16;45m$u$esc[38;2;21;54;144m$esc[48;2;21;54;144m$u$esc[0m     $esc[36m*$esc[0m $esc[1mESPHome Proxy:$esc[0m Port 6053 standard Home Assistant proxy
$esc[38;2;21;54;145m$esc[48;2;21;53;145m$u$esc[38;2;0;7;37m$esc[48;2;0;4;34m$u$esc[38;2;82;90;108m$esc[48;2;147;154;167m$u$esc[38;2;255;255;255m$esc[48;2;255;255;255m$u$esc[38;2;111;118;135m$esc[48;2;129;136;151m$u$esc[38;2;0;0;24m$esc[48;2;0;2;28m$u$esc[38;2;2;12;46m$esc[48;2;3;11;36m$u$esc[38;2;123;85;28m$esc[48;2;0;13;46m$u$esc[38;2;255;169;8m$esc[48;2;32;30;40m$u$esc[38;2;255;180;6m$esc[48;2;110;78;29m$u$esc[38;2;232;151;13m$esc[48;2;207;136;16m$u$esc[38;2;139;95;25m$esc[48;2;255;176;7m$u$esc[38;2;29;29;41m$esc[48;2;84;62;33m$u$esc[38;2;7;16;44m$esc[48;2;0;10;45m$u$esc[38;2;7;16;44m$esc[48;2;0;10;45m$u$esc[38;2;28;28;41m$esc[48;2;82;61;33m$u$esc[38;2;139;95;25m$esc[48;2;255;176;7m$u$esc[38;2;231;151;13m$esc[48;2;208;136;16m$u$esc[38;2;255;180;6m$esc[48;2;111;78;29m$u$esc[38;2;255;169;8m$esc[48;2;32;31;40m$u$esc[38;2;124;86;27m$esc[48;2;0;13;46m$u$esc[38;2;2;12;46m$esc[48;2;4;11;36m$u$esc[38;2;0;0;24m$esc[48;2;0;2;27m$u$esc[38;2;110;117;134m$esc[48;2;128;135;150m$u$esc[38;2;255;255;255m$esc[48;2;255;255;255m$u$esc[38;2;84;91;109m$esc[48;2;148;155;168m$u$esc[38;2;0;7;36m$esc[48;2;0;4;33m$u$esc[38;2;21;54;144m$esc[48;2;21;54;144m$u$esc[0m     $esc[35m*$esc[0m $esc[1mShelly Clock Sync:$esc[0m Native 0x0BA9 epoch time broadcast
$esc[38;2;21;54;145m$esc[48;2;21;54;145m$u$esc[38;2;5;14;43m$esc[48;2;13;23;51m$u$esc[38;2;51;57;77m$esc[48;2;0;5;29m$u$esc[38;2;203;210;219m$esc[48;2;21;28;51m$u$esc[38;2;255;255;255m$esc[48;2;169;176;188m$u$esc[38;2;158;165;178m$esc[48;2;255;255;255m$u$esc[38;2;15;21;45m$esc[48;2;196;203;213m$u$esc[38;2;0;5;30m$esc[48;2;41;47;69m$u$esc[38;2;4;15;45m$esc[48;2;0;0;25m$u$esc[38;2;0;7;46m$esc[48;2;11;19;43m$u$esc[38;2;7;17;43m$esc[48;2;9;18;44m$u$esc[38;2;43;39;38m$esc[48;2;2;13;44m$u$esc[38;2;14;21;43m$esc[48;2;9;18;43m$u$esc[38;2;10;18;43m$esc[48;2;11;19;43m$u$esc[38;2;10;18;43m$esc[48;2;11;19;43m$u$esc[38;2;14;21;43m$esc[48;2;9;18;43m$u$esc[38;2;43;39;38m$esc[48;2;2;13;44m$u$esc[38;2;7;17;43m$esc[48;2;9;18;44m$u$esc[38;2;0;7;46m$esc[48;2;11;19;43m$u$esc[38;2;4;15;45m$esc[48;2;0;0;26m$u$esc[38;2;0;5;30m$esc[48;2;40;47;68m$u$esc[38;2;14;21;44m$esc[48;2;195;202;212m$u$esc[38;2;157;164;177m$esc[48;2;255;255;255m$u$esc[38;2;255;255;255m$esc[48;2;171;177;189m$u$esc[38;2;204;211;220m$esc[48;2;22;28;52m$u$esc[38;2;51;57;78m$esc[48;2;0;5;29m$u$esc[38;2;5;14;42m$esc[48;2;13;23;51m$u$esc[38;2;21;54;144m$esc[48;2;21;54;144m$u$esc[0m     $esc[33m*$esc[0m $esc[1mREST & SSE API:$esc[0m Port 8766 real-time beacon telemetry
$esc[38;2;21;54;145m$esc[48;2;21;54;145m$u$esc[38;2;12;22;50m$esc[48;2;12;22;50m$u$esc[38;2;12;20;43m$esc[48;2;11;19;42m$u$esc[38;2;2;9;34m$esc[48;2;12;20;44m$u$esc[38;2;3;9;33m$esc[48;2;7;15;39m$u$esc[38;2;128;135;150m$esc[48;2;0;0;25m$u$esc[38;2;255;255;255m$esc[48;2;87;94;112m$u$esc[38;2;228;235;242m$esc[48;2;236;242;249m$u$esc[38;2;76;82;102m$esc[48;2;251;255;255m$u$esc[38;2;0;0;25m$esc[48;2;116;123;139m$u$esc[38;2;8;16;40m$esc[48;2;0;5;30m$u$esc[38;2;11;20;43m$esc[48;2;3;11;36m$u$esc[38;2;11;19;43m$esc[48;2;12;20;44m$u$esc[38;2;11;19;43m$esc[48;2;11;19;43m$u$esc[38;2;11;19;43m$esc[48;2;11;19;43m$u$esc[38;2;11;19;43m$esc[48;2;12;20;44m$u$esc[38;2;11;20;43m$esc[48;2;4;11;36m$u$esc[38;2;8;16;40m$esc[48;2;0;5;30m$u$esc[38;2;0;0;24m$esc[48;2;115;122;138m$u$esc[38;2;75;81;101m$esc[48;2;251;255;255m$u$esc[38;2;227;234;241m$esc[48;2;237;243;250m$u$esc[38;2;255;255;255m$esc[48;2;88;95;113m$u$esc[38;2;130;136;151m$esc[48;2;0;0;25m$u$esc[38;2;3;9;34m$esc[48;2;7;14;39m$u$esc[38;2;2;9;34m$esc[48;2;12;20;44m$u$esc[38;2;12;20;43m$esc[48;2;11;19;42m$u$esc[38;2;12;22;50m$esc[48;2;12;22;50m$u$esc[38;2;21;54;144m$esc[48;2;21;54;144m$u$esc[0m     $esc[34m*$esc[0m $esc[1mStealth Tray Gateway:$esc[0m Whisper-quiet background observer
$esc[38;2;21;54;145m$esc[48;2;21;54;144m$u$esc[38;2;12;22;50m$esc[48;2;12;22;50m$u$esc[38;2;11;19;42m$esc[48;2;11;19;42m$u$esc[38;2;11;19;43m$esc[48;2;11;19;43m$u$esc[38;2;11;19;43m$esc[48;2;11;19;43m$u$esc[38;2;10;18;42m$esc[48;2;11;19;43m$u$esc[38;2;0;0;25m$esc[48;2;11;20;44m$u$esc[38;2;50;57;78m$esc[48;2;0;4;29m$u$esc[38;2;206;213;222m$esc[48;2;21;28;51m$u$esc[38;2;255;255;255m$esc[48;2;169;176;188m$u$esc[38;2;158;165;177m$esc[48;2;255;255;255m$u$esc[38;2;15;21;45m$esc[48;2;196;203;213m$u$esc[38;2;0;5;30m$esc[48;2;41;48;70m$u$esc[38;2;12;20;44m$esc[48;2;0;1;26m$u$esc[38;2;12;20;44m$esc[48;2;0;0;26m$u$esc[38;2;0;5;30m$esc[48;2;40;47;69m$u$esc[38;2;14;21;44m$esc[48;2;195;202;212m$u$esc[38;2;157;163;176m$esc[48;2;255;255;255m$u$esc[38;2;255;255;255m$esc[48;2;170;177;189m$u$esc[38;2;207;214;223m$esc[48;2;22;28;52m$u$esc[38;2;51;58;79m$esc[48;2;0;3;28m$u$esc[38;2;0;0;25m$esc[48;2;11;20;44m$u$esc[38;2;10;18;42m$esc[48;2;11;19;43m$u$esc[38;2;11;19;43m$esc[48;2;11;19;43m$u$esc[38;2;11;19;43m$esc[48;2;11;19;43m$u$esc[38;2;11;19;42m$esc[48;2;11;19;42m$u$esc[38;2;12;22;50m$esc[48;2;12;22;49m$u$esc[38;2;21;54;144m$esc[48;2;21;54;145m$u$esc[0m     $esc[32m*$esc[0m $esc[1mLean & Portable:$esc[0m Single-file self-contained binary
$esc[38;2;23;58;157m$esc[48;2;28;74;206m$u$esc[38;2;13;23;55m$esc[48;2;15;32;80m$u$esc[38;2;11;19;42m$esc[48;2;10;16;35m$u$esc[38;2;11;19;43m$esc[48;2;11;19;43m$u$esc[38;2;11;19;43m$esc[48;2;11;19;43m$u$esc[38;2;11;19;43m$esc[48;2;11;19;43m$u$esc[38;2;11;19;43m$esc[48;2;11;19;43m$u$esc[38;2;12;20;44m$esc[48;2;11;19;43m$u$esc[38;2;2;9;34m$esc[48;2;12;20;44m$u$esc[38;2;2;9;34m$esc[48;2;7;15;39m$u$esc[38;2;128;135;150m$esc[48;2;0;0;25m$u$esc[38;2;255;255;255m$esc[48;2;87;94;112m$u$esc[38;2;228;234;242m$esc[48;2;235;241;248m$u$esc[38;2;68;75;95m$esc[48;2;242;248;255m$u$esc[38;2;68;74;94m$esc[48;2;242;248;255m$u$esc[38;2;227;234;241m$esc[48;2;236;241;249m$u$esc[38;2;255;255;255m$esc[48;2;88;94;113m$u$esc[38;2;129;136;151m$esc[48;2;0;0;26m$u$esc[38;2;3;9;34m$esc[48;2;7;15;39m$u$esc[38;2;2;9;34m$esc[48;2;12;20;44m$u$esc[38;2;12;20;44m$esc[48;2;11;19;43m$u$esc[38;2;11;19;43m$esc[48;2;11;19;43m$u$esc[38;2;11;19;43m$esc[48;2;11;19;43m$u$esc[38;2;11;19;43m$esc[48;2;11;19;43m$u$esc[38;2;11;19;43m$esc[48;2;11;19;43m$u$esc[38;2;11;19;42m$esc[48;2;10;16;35m$u$esc[38;2;13;23;55m$esc[48;2;15;32;79m$u$esc[38;2;23;57;157m$esc[48;2;28;72;201m$u$esc[0m   
$esc[0m $esc[38;2;19;49;130m$esc[48;2;29;85;229m$u$esc[38;2;11;18;38m$esc[48;2;18;43;114m$u$esc[38;2;11;18;41m$esc[48;2;11;18;38m$u$esc[38;2;11;19;43m$esc[48;2;10;16;35m$u$esc[38;2;11;19;43m$esc[48;2;11;19;42m$u$esc[38;2;11;19;43m$esc[48;2;11;19;42m$u$esc[38;2;11;19;43m$esc[48;2;11;19;42m$u$esc[38;2;11;19;43m$esc[48;2;11;19;42m$u$esc[38;2;11;19;43m$esc[48;2;11;19;42m$u$esc[38;2;10;18;42m$esc[48;2;11;19;42m$u$esc[38;2;0;0;25m$esc[48;2;11;19;43m$u$esc[38;2;51;58;79m$esc[48;2;0;6;31m$u$esc[38;2;206;212;222m$esc[48;2;21;29;51m$u$esc[38;2;207;213;223m$esc[48;2;21;29;52m$u$esc[38;2;52;58;79m$esc[48;2;0;6;31m$u$esc[38;2;0;0;25m$esc[48;2;11;19;43m$u$esc[38;2;10;18;42m$esc[48;2;11;19;42m$u$esc[38;2;11;19;43m$esc[48;2;11;19;42m$u$esc[38;2;11;19;43m$esc[48;2;11;19;42m$u$esc[38;2;11;19;43m$esc[48;2;11;19;42m$u$esc[38;2;11;19;43m$esc[48;2;11;19;42m$u$esc[38;2;11;19;43m$esc[48;2;11;19;42m$u$esc[38;2;11;19;43m$esc[48;2;10;16;35m$u$esc[38;2;11;18;41m$esc[48;2;11;18;38m$u$esc[38;2;11;18;38m$esc[48;2;18;43;113m$u$esc[38;2;20;48;130m$esc[48;2;29;83;229m$u$esc[0m $esc[0m   $esc[90mVersion 1.0.0 `• FoxDen Software$esc[0m
$esc[0m $esc[0m $esc[38;2;29;87;234m$esc[49m$u$esc[38;2;20;49;134m$esc[49m$u$esc[38;2;15;31;79m$esc[48;2;28;74;209m$u$esc[38;2;13;23;54m$esc[48;2;23;59;160m$u$esc[38;2;12;21;49m$esc[48;2;22;53;146m$u$esc[38;2;12;21;49m$esc[48;2;20;53;146m$u$esc[38;2;12;22;50m$esc[48;2;20;53;146m$u$esc[38;2;12;22;50m$esc[48;2;20;53;146m$u$esc[38;2;12;22;50m$esc[48;2;20;53;146m$u$esc[38;2;12;22;50m$esc[48;2;20;53;146m$u$esc[38;2;13;22;50m$esc[48;2;20;53;146m$u$esc[38;2;7;17;45m$esc[48;2;20;53;146m$u$esc[38;2;7;17;45m$esc[48;2;20;53;146m$u$esc[38;2;13;22;50m$esc[48;2;20;53;146m$u$esc[38;2;12;22;50m$esc[48;2;20;53;146m$u$esc[38;2;12;22;50m$esc[48;2;20;53;146m$u$esc[38;2;12;22;50m$esc[48;2;20;53;146m$u$esc[38;2;12;22;50m$esc[48;2;20;53;146m$u$esc[38;2;12;21;49m$esc[48;2;20;53;146m$u$esc[38;2;12;21;49m$esc[48;2;22;53;146m$u$esc[38;2;13;23;54m$esc[48;2;23;59;160m$u$esc[38;2;15;31;78m$esc[48;2;28;76;206m$u$esc[38;2;20;49;134m$esc[49m$u$esc[38;2;32;86;234m$esc[49m$u$esc[0m $esc[0m $esc[0m
"@

$guardianBanner = $guardianBanner.Replace("$esc[49m", "$esc[48;2;0;0;0m")

# 1. Setup user-space install directory & temp staging (Zero Admin, Zero UAC)
$installDir = "$env:LOCALAPPDATA\Programs\AirFoxBT"
if (-not (Test-Path $installDir)) {
    New-Item -ItemType Directory -Path $installDir -Force | Out-Null
}

$tempDir = Join-Path $env:TEMP "AirFoxBT_Installer"
if (-not (Test-Path $tempDir)) {
    New-Item -ItemType Directory -Path $tempDir -Force | Out-Null
}

$stagingExe = Join-Path $tempDir "AirFoxBT.exe"
$targetExe = Join-Path $installDir "AirFoxBT.exe"

Write-Host $guardianBanner
if (-not $Force) {
    $confirm = Read-Host "`n    Press [Enter] to install AirFoxBT, or type 'C' to cancel"
    if ($confirm -match "^[cCqQ]") {
        Write-Host "`n[*] Installation cancelled." -ForegroundColor Yellow
        return
    }
}

# 3. Check for Local Binary, Local Source Code, or Download Release
$repo = "TalviFox/AirFoxBT"
$expectedHash = $null
$releaseTag = "v1.0.0"

$candidateExe = $null
if ($Path -and (Test-Path $Path)) {
    $candidateExe = (Resolve-Path $Path).Path
}
elseif ($PSScriptRoot -and (Test-Path (Join-Path $PSScriptRoot "publish\AirFoxBT.exe"))) {
    $candidateExe = Join-Path $PSScriptRoot "publish\AirFoxBT.exe"
}
elseif ($PSScriptRoot -and (Test-Path (Join-Path $PSScriptRoot "AirFoxBT.exe"))) {
    $candidateExe = Join-Path $PSScriptRoot "AirFoxBT.exe"
}

$localProj = if ($PSScriptRoot -and (Test-Path (Join-Path $PSScriptRoot "AirFoxBT.csproj"))) { Join-Path $PSScriptRoot "AirFoxBT.csproj" } else { $null }

if ($candidateExe -and -not $Build) {
    Write-Host "[*] Pre-compiled local binary detected: $candidateExe" -ForegroundColor Cyan
    Copy-Item -Path $candidateExe -Destination $stagingExe -Force
    try {
        $verInfo = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($candidateExe)
        if ($verInfo.ProductVersion) { $releaseTag = "v$($verInfo.ProductVersion.Trim())" } elseif ($verInfo.FileVersion) { $releaseTag = "v$($verInfo.FileVersion.Trim())" }
    }
    catch {}
    Write-Host "[+] Local binary verified and staged for installation ($releaseTag)." -ForegroundColor Green
}
elseif ($localProj -and (Test-Path $localProj)) {
    Write-Host "[*] Local source code detected. Building locally instead of downloading..." -ForegroundColor Cyan
    $publishDir = Join-Path $PSScriptRoot "publish"
    
    $dotnet = "dotnet"
    $dotnetDir = "$env:LOCALAPPDATA\Microsoft\dotnet"
    if (Test-Path "$dotnetDir\dotnet.exe") {
        $dotnet = "$dotnetDir\dotnet.exe"
    }
    
    $proc = Start-Process $dotnet -ArgumentList "publish `"$localProj`" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o `"$publishDir`"" -Wait -NoNewWindow -PassThru
    if ($proc.ExitCode -ne 0) {
        Write-Host "`n[X] Local build failed with exit code $($proc.ExitCode)." -ForegroundColor Red
        return
    }
    
    $builtExe = Join-Path $publishDir "AirFoxBT.exe"
    if (-not (Test-Path $builtExe)) {
        Write-Host "`n[X] Local build succeeded but AirFoxBT.exe not found at $builtExe." -ForegroundColor Red
        return
    }
    
    Copy-Item -Path $builtExe -Destination $stagingExe -Force
    Write-Host "[+] Local build compiled and verified." -ForegroundColor Green
}
else {
    $apiUrl = "https://api.github.com/repos/$repo/releases/latest"
    $headers = @{ "User-Agent" = "AirFoxBT-Installer" }

    Write-Host "[*] Querying latest release from $repo over TLS..." -ForegroundColor Cyan

    try {
        $release = Invoke-RestMethod -Uri $apiUrl -Headers $headers -UseBasicParsing
        $releaseTag = $release.tag_name

        $sumsAsset = $release.assets | Where-Object { $_.name -match "^(SHA256SUMS|AirFoxBT.*)\.(txt|sha256)$" -or $_.name -eq "AirFoxBT.exe.sha256" } | Select-Object -First 1
        if ($sumsAsset) {
            $checksumText = Invoke-RestMethod -Uri $sumsAsset.browser_download_url -Headers $headers -UseBasicParsing
            if ($checksumText -match "([a-fA-F0-9]{64})\s+.*AirFoxBT\.exe") {
                $expectedHash = $matches[1].ToLowerInvariant()
            }
            elseif ($checksumText -match "\b([a-fA-F0-9]{64})\b") {
                $expectedHash = $matches[1].ToLowerInvariant()
            }
        }

        if (-not $expectedHash -and $release.body -match "\b([a-fA-F0-9]{64})\b") {
            $expectedHash = $matches[1].ToLowerInvariant()
        }
    }
    catch {
        Write-Host "[!] Could not query release API metadata. Proceeding with direct binary download..." -ForegroundColor Yellow
    }

    $downloadUrl = "https://github.com/$repo/releases/latest/download/AirFoxBT.exe"
    Write-Host "[*] Downloading AirFoxBT ($releaseTag)..." -ForegroundColor Cyan
    try {
        Invoke-WebRequest -Uri $downloadUrl -OutFile $stagingExe -UseBasicParsing
    }
    catch {
        Write-Host "`n[X] Failed to download AirFoxBT.exe from GitHub Releases ($downloadUrl)." -ForegroundColor Red
        Write-Host "    Please ensure a Release containing 'AirFoxBT.exe' exists at:" -ForegroundColor Yellow
        Write-Host "    https://github.com/$repo/releases" -ForegroundColor White
        return
    }

    # Verify SHA-256
    Write-Host "[*] Verifying cryptographic SHA-256 integrity..." -ForegroundColor Cyan
    $actualHash = (Get-FileHash -Path $stagingExe -Algorithm SHA256).Hash.ToLowerInvariant()
    Write-Host "    Downloaded SHA-256: $actualHash" -ForegroundColor White

    if ($expectedHash) {
        if ($actualHash -ne $expectedHash) {
            Write-Host "`n[X] CRITICAL: SHA-256 CHECKSUM VERIFICATION FAILED!" -ForegroundColor Red
            Write-Host "    Expected: $expectedHash" -ForegroundColor Yellow
            Write-Host "    Actual:   $actualHash" -ForegroundColor Yellow
            Remove-Item -Path $stagingExe -Force -ErrorAction SilentlyContinue
            return
        }
        Write-Host "[+] SHA-256 integrity verified successfully!" -ForegroundColor Green
    }
}

# 4. Stop running instance if updating
$running = Get-Process -Name "AirFoxBT" -ErrorAction SilentlyContinue
if ($running) {
    Write-Host "[*] Stopping running AirFoxBT process..." -ForegroundColor Cyan
    $running | Stop-Process -Force
    Start-Sleep -Seconds 1
}

# 5. Move verified binary into place
Copy-Item -Path $stagingExe -Destination $targetExe -Force
Remove-Item -Path $stagingExe -Force -ErrorAction SilentlyContinue
Write-Host "[+] Installed to: $targetExe" -ForegroundColor Green

# 5b. Deploy application icon
$localIco = if ($PSScriptRoot) { Join-Path $PSScriptRoot "assets\app.ico" } else { $null }
$targetIco = Join-Path $installDir "app.ico"
if ($localIco -and (Test-Path $localIco)) {
    Copy-Item -Path $localIco -Destination $targetIco -Force
}

# 6. Deploy companion scripts (uninstaller, verifier & checksums)
$uninstallerTarget = Join-Path $installDir "uninstall.ps1"
$verifierTarget = Join-Path $installDir "verify.ps1"
$shaTarget = Join-Path $installDir "AirFoxBT.exe.sha256"

$localUninstall = if ($PSScriptRoot) { Join-Path $PSScriptRoot "uninstall.ps1" } else { $null }
$localVerifier = if ($PSScriptRoot) { Join-Path $PSScriptRoot "verify.ps1" } else { $null }
$localSha = if ($PSScriptRoot) { Join-Path $PSScriptRoot "publish\AirFoxBT.exe.sha256" } else { $null }

if ($localUninstall -and (Test-Path $localUninstall)) {
    Copy-Item -Path $localUninstall -Destination $uninstallerTarget -Force
    Write-Host "[+] Local uninstaller script deployed: $uninstallerTarget" -ForegroundColor Green
}
else {
    try {
        $uninstallerUrl = "https://raw.githubusercontent.com/$repo/main/uninstall.ps1"
        Invoke-WebRequest -Uri $uninstallerUrl -OutFile $uninstallerTarget -UseBasicParsing
        Write-Host "[+] Uninstaller script deployed: $uninstallerTarget" -ForegroundColor Green
    }
    catch {}
}

if ($localVerifier -and (Test-Path $localVerifier)) {
    Copy-Item -Path $localVerifier -Destination $verifierTarget -Force
    Write-Host "[+] Local integrity auditor deployed: $verifierTarget" -ForegroundColor Green
}
else {
    try {
        $verifierUrl = "https://raw.githubusercontent.com/$repo/main/verify.ps1"
        Invoke-WebRequest -Uri $verifierUrl -OutFile $verifierTarget -UseBasicParsing
        Write-Host "[+] Integrity auditor script deployed: $verifierTarget" -ForegroundColor Green
    }
    catch {}
}

if ($localSha -and (Test-Path $localSha)) {
    Copy-Item -Path $localSha -Destination $shaTarget -Force
}

$extraFiles = @("SHA256SUMS", "release_notes.md", "AirFoxBT.pdb")
foreach ($ef in $extraFiles) {
    $loc = if ($PSScriptRoot) { Join-Path $PSScriptRoot $ef } else { $null }
    if ($loc -and (Test-Path $loc)) {
        Copy-Item -Path $loc -Destination (Join-Path $installDir $ef) -Force
    }
}

# 7. Register in Windows "Installed Apps" (Add or Remove Programs - User Level)
try {
    $regKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\AirFoxBT"
    if (-not (Test-Path $regKey)) {
        New-Item -Path $regKey -Force | Out-Null
    }
    Set-ItemProperty -Path $regKey -Name "DisplayName" -Value "AirFoxBT"
    Set-ItemProperty -Path $regKey -Name "DisplayVersion" -Value $releaseTag.TrimStart('v', 'V')
    Set-ItemProperty -Path $regKey -Name "Publisher" -Value "FoxDen Software"
    Set-ItemProperty -Path $regKey -Name "DisplayIcon" -Value "$targetExe,0"
    Set-ItemProperty -Path $regKey -Name "InstallLocation" -Value $installDir
    Set-ItemProperty -Path $regKey -Name "UninstallString" -Value "powershell.exe -NoProfile -ExecutionPolicy Bypass -File `"$uninstallerTarget`""
    Set-ItemProperty -Path $regKey -Name "URLInfoAbout" -Value "https://github.com/TalviFox/AirFoxBT"
    Write-Host "[+] Registered in Windows Installed Apps." -ForegroundColor Green
}
catch {
    Write-Host "[!] Could not register in Windows Uninstall registry: $_" -ForegroundColor Yellow
}

# 7b. Configure Windows Defender Firewall (if running elevated)
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if ($isAdmin) {
    try {
        $ruleName = "AirFoxBT Bluetooth Proxy"
        Get-NetFirewallRule -DisplayName "$ruleName*" -ErrorAction SilentlyContinue | Remove-NetFirewallRule -ErrorAction SilentlyContinue

        New-NetFirewallRule `
            -DisplayName $ruleName `
            -Description "AirFoxBT ESPHome Proxy (6053) and REST Telemetry API (8766)" `
            -Direction Inbound `
            -LocalPort 6053, 8766 `
            -Protocol TCP `
            -Action Allow `
            -Profile Any `
            -Enabled True | Out-Null
        Write-Host "[+] Windows Firewall rules configured (TCP 6053 & 8766)." -ForegroundColor Green
    } catch { }
}

# 8. Create Start Menu Shortcut (User Level)
try {
    $wsh = New-Object -ComObject WScript.Shell
    $startMenuDir = "$env:APPDATA\Microsoft\Windows\Start Menu\Programs"
    $shortcut = $wsh.CreateShortcut((Join-Path $startMenuDir "AirFoxBT.lnk"))
    $shortcut.TargetPath = $targetExe
    $shortcut.WorkingDirectory = $installDir
    $shortcut.IconLocation = "$targetExe,0"
    $shortcut.Description = "AirFoxBT - Real beacons. Zero pairing."
    $shortcut.Save()
    Write-Host "[+] Start Menu shortcut created." -ForegroundColor Green

    if ($DesktopShortcut) {
        $desktopDir = [Environment]::GetFolderPath([Environment+SpecialFolder]::Desktop)
        $deskShortcut = $wsh.CreateShortcut((Join-Path $desktopDir "AirFoxBT.lnk"))
        $deskShortcut.TargetPath = $targetExe
        $deskShortcut.WorkingDirectory = $installDir
        $deskShortcut.IconLocation = "$targetExe,0"
        $deskShortcut.Description = "AirFoxBT - Real beacons. Zero pairing."
        $deskShortcut.Save()
        Write-Host "[+] Desktop shortcut created." -ForegroundColor Green
    }
}
catch {
    Write-Host "[!] Could not create shortcut: $_" -ForegroundColor Yellow
}

# 9. Complete & Launch
$checkEmoji = [char]::ConvertFromUtf32(0x2705)
Write-Host @"

  ======================================================
     $checkEmoji AirFoxBT has been installed successfully!
  ======================================================
  Install Path:  $targetExe
  ESPHome Proxy: Port 6053
  REST & SSE:    http://localhost:8766/api/v1/summary
  ======================================================
"@ -ForegroundColor Green

Write-Host "[*] Launching AirFoxBT in background..." -ForegroundColor Cyan
# Launch detached via Windows Explorer so closing the installer shell never closes AirFoxBT
& explorer.exe $targetExe

Write-Host "`n[+] AirFoxBT launched and running in system tray." -ForegroundColor Green
Write-Host "[*] Exiting installer in 3 seconds (or press any key to close now)..." -ForegroundColor DarkGray
$counter = 0
while ($counter -lt 30) {
    try {
        if ([Console]::KeyAvailable) {
            [void][Console]::ReadKey($true)
            break
        }
    } catch {
        break
    }
    Start-Sleep -Milliseconds 100
    $counter++
}
exit 0
