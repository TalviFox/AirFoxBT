# =============================================================
#  🦊 AirFoxBT Uninstaller Script
#  https://github.com/TalviFox/AirFoxBT
#  Cleanly removes AirFoxBT background tasks, shortcuts, registry entries, and program files.
# =============================================================


# Console environment normalization (ensures 24-bit TrueColor)
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
$ErrorActionPreference = "SilentlyContinue"

$OutputEncoding = [Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$esc = [char]27
$u = [char]0x2580
$d = [char]0x2584

$banner = @"

$esc[0m $esc[0m $esc[38;2;32;86;238m$esc[49m$d$esc[38;2;20;50;137m$esc[49m$d$esc[38;2;27;77;213m$esc[48;2;15;32;81m$u$esc[38;2;23;59;164m$esc[48;2;13;24;56m$u$esc[38;2;21;54;147m$esc[48;2;12;22;50m$u$esc[38;2;21;54;145m$esc[48;2;12;22;50m$u$esc[38;2;21;54;145m$esc[48;2;12;22;50m$u$esc[38;2;21;54;145m$esc[48;2;12;22;50m$u$esc[38;2;21;54;145m$esc[48;2;12;22;50m$u$esc[38;2;21;54;145m$esc[48;2;12;22;50m$u$esc[38;2;21;54;145m$esc[48;2;12;22;50m$u$esc[38;2;21;54;145m$esc[48;2;12;22;50m$u$esc[38;2;21;54;145m$esc[48;2;12;22;50m$u$esc[38;2;21;54;145m$esc[48;2;12;22;50m$u$esc[38;2;21;54;145m$esc[48;2;12;22;50m$u$esc[38;2;21;54;145m$esc[48;2;12;22;50m$u$esc[38;2;21;54;145m$esc[48;2;12;22;50m$u$esc[38;2;21;54;145m$esc[48;2;12;22;50m$u$esc[38;2;21;54;145m$esc[48;2;12;22;50m$u$esc[38;2;21;54;147m$esc[48;2;12;22;50m$u$esc[38;2;23;59;163m$esc[48;2;13;24;56m$u$esc[38;2;27;77;213m$esc[48;2;15;32;80m$u$esc[38;2;20;50;136m$esc[49m$d$esc[38;2;32;89;238m$esc[49m$d$esc[0m $esc[0m $esc[0m
$esc[0m $esc[38;2;30;87;228m$esc[48;2;19;48;131m$u$esc[38;2;18;44;116m$esc[48;2;11;18;39m$u$esc[38;2;11;18;39m$esc[48;2;11;18;41m$u$esc[38;2;11;17;35m$esc[48;2;9;17;42m$u$esc[38;2;8;16;39m$esc[48;2;16;24;48m$u$esc[38;2;0;6;31m$esc[48;2;62;70;90m$u$esc[38;2;9;17;41m$esc[48;2;9;17;41m$u$esc[38;2;11;19;43m$esc[48;2;9;17;41m$u$esc[38;2;11;19;42m$esc[48;2;11;19;43m$u$esc[38;2;11;19;42m$esc[48;2;10;18;42m$u$esc[38;2;11;19;42m$esc[48;2;0;8;33m$u$esc[38;2;11;18;42m$esc[48;2;1;9;33m$u$esc[38;2;9;17;40m$esc[48;2;11;19;43m$u$esc[38;2;9;17;40m$esc[48;2;11;19;43m$u$esc[38;2;11;18;42m$esc[48;2;1;9;33m$u$esc[38;2;11;19;42m$esc[48;2;0;8;33m$u$esc[38;2;11;19;42m$esc[48;2;10;18;42m$u$esc[38;2;11;19;42m$esc[48;2;11;19;43m$u$esc[38;2;11;19;43m$esc[48;2;9;17;41m$u$esc[38;2;9;17;41m$esc[48;2;9;17;41m$u$esc[38;2;0;6;31m$esc[48;2;64;72;92m$u$esc[38;2;8;16;39m$esc[48;2;17;25;49m$u$esc[38;2;11;17;35m$esc[48;2;9;17;41m$u$esc[38;2;11;18;38m$esc[48;2;11;18;41m$u$esc[38;2;18;44;115m$esc[48;2;11;18;38m$u$esc[38;2;30;86;229m$esc[48;2;19;49;130m$u$esc[0m $esc[0m   $([char]::ConvertFromUtf32(0x1F98A)) $esc[1;33mAirFoxBT Uninstaller$esc[0m
$esc[38;2;28;74;203m$esc[48;2;21;57;157m$u$esc[38;2;15;32;80m$esc[48;2;13;23;55m$u$esc[38;2;10;16;35m$esc[48;2;11;19;42m$u$esc[38;2;11;19;43m$esc[48;2;11;19;43m$u$esc[38;2;0;2;28m$esc[48;2;0;5;30m$u$esc[38;2;96;103;121m$esc[48;2;161;167;180m$u$esc[38;2;255;255;255m$esc[48;2;251;255;255m$u$esc[38;2;106;113;130m$esc[48;2;226;232;240m$u$esc[38;2;0;0;25m$esc[48;2;36;44;66m$u$esc[38;2;9;17;41m$esc[48;2;2;6;31m$u$esc[38;2;1;9;34m$esc[48;2;118;125;141m$u$esc[38;2;61;69;89m$esc[48;2;185;192;203m$u$esc[38;2;134;141;156m$esc[48;2;113;120;137m$u$esc[38;2;159;166;179m$esc[48;2;56;64;85m$u$esc[38;2;159;166;179m$esc[48;2;56;64;84m$u$esc[38;2;134;142;156m$esc[48;2;112;120;136m$u$esc[38;2;62;70;90m$esc[48;2;185;192;203m$u$esc[38;2;1;9;34m$esc[48;2;119;126;142m$u$esc[38;2;9;17;41m$esc[48;2;2;7;32m$u$esc[38;2;0;0;25m$esc[48;2;35;43;65m$u$esc[38;2;105;112;129m$esc[48;2;225;231;239m$u$esc[38;2;255;255;255m$esc[48;2;250;255;255m$u$esc[38;2;98;105;123m$esc[48;2;162;169;181m$u$esc[38;2;0;2;27m$esc[48;2;0;5;30m$u$esc[38;2;11;19;43m$esc[48;2;11;19;43m$u$esc[38;2;10;16;35m$esc[48;2;11;19;42m$u$esc[38;2;15;32;80m$esc[48;2;13;23;55m$u$esc[38;2;28;73;203m$esc[48;2;21;56;154m$u$esc[0m   $esc[90mClean & Complete System Removal$esc[0m
$esc[38;2;21;54;144m$esc[48;2;21;54;145m$u$esc[38;2;12;22;50m$esc[48;2;12;22;50m$u$esc[38;2;11;19;42m$esc[48;2;11;19;42m$u$esc[38;2;6;14;39m$esc[48;2;0;6;31m$u$esc[38;2;21;29;52m$esc[48;2;66;74;93m$u$esc[38;2;210;216;226m$esc[48;2;251;254;255m$u$esc[38;2;227;234;242m$esc[48;2;128;135;150m$u$esc[38;2;245;251;255m$esc[48;2;147;154;168m$u$esc[38;2;165;172;185m$esc[48;2;255;255;255m$u$esc[38;2;10;18;42m$esc[48;2;89;97;115m$u$esc[38;2;131;138;153m$esc[48;2;0;0;7m$u$esc[38;2;38;45;67m$esc[48;2;96;103;121m$u$esc[38;2;14;22;46m$esc[48;2;233;239;246m$u$esc[38;2;77;84;104m$esc[48;2;215;221;230m$u$esc[38;2;77;85;104m$esc[48;2;215;221;230m$u$esc[38;2;15;23;46m$esc[48;2;233;239;246m$u$esc[38;2;37;44;66m$esc[48;2;97;104;122m$u$esc[38;2;131;138;154m$esc[48;2;0;0;7m$u$esc[38;2;11;19;43m$esc[48;2;88;95;114m$u$esc[38;2;163;170;183m$esc[48;2;255;255;255m$u$esc[38;2;246;251;255m$esc[48;2;148;155;169m$u$esc[38;2;227;234;242m$esc[48;2;127;134;149m$u$esc[38;2;211;218;227m$esc[48;2;252;254;255m$u$esc[38;2;21;30;53m$esc[48;2;67;75;94m$u$esc[38;2;6;14;38m$esc[48;2;0;5;30m$u$esc[38;2;11;19;42m$esc[48;2;11;19;42m$u$esc[38;2;12;22;49m$esc[48;2;12;22;50m$u$esc[38;2;21;54;144m$esc[48;2;21;54;144m$u$esc[0m   
$esc[38;2;21;54;145m$esc[48;2;21;54;145m$u$esc[38;2;12;22;50m$esc[48;2;12;22;50m$u$esc[38;2;11;19;42m$esc[48;2;9;17;41m$u$esc[38;2;0;1;26m$esc[48;2;4;11;36m$u$esc[38;2;124;131;147m$esc[48;2;186;192;204m$u$esc[38;2;255;255;255m$esc[48;2;226;233;240m$u$esc[38;2;63;71;91m$esc[48;2;27;35;58m$u$esc[38;2;22;28;51m$esc[48;2;0;0;19m$u$esc[38;2;229;234;240m$esc[48;2;102;110;128m$u$esc[38;2;221;226;233m$esc[48;2;255;255;255m$u$esc[38;2;20;26;47m$esc[48;2;153;160;174m$u$esc[38;2;112;118;134m$esc[48;2;0;0;10m$u$esc[38;2;108;114;131m$esc[48;2;0;0;12m$u$esc[38;2;11;17;41m$esc[48;2;0;0;24m$u$esc[38;2;11;17;41m$esc[48;2;0;0;24m$u$esc[38;2;107;114;130m$esc[48;2;0;0;12m$u$esc[38;2;113;119;135m$esc[48;2;0;0;9m$u$esc[38;2;19;25;47m$esc[48;2;152;159;173m$u$esc[38;2;219;225;232m$esc[48;2;255;255;255m$u$esc[38;2;230;235;240m$esc[48;2;104;112;129m$u$esc[38;2;23;29;52m$esc[48;2;0;0;19m$u$esc[38;2;62;69;90m$esc[48;2;26;34;57m$u$esc[38;2;255;255;255m$esc[48;2;225;232;240m$u$esc[38;2;126;133;148m$esc[48;2;187;193;205m$u$esc[38;2;0;1;26m$esc[48;2;4;12;37m$u$esc[38;2;11;19;42m$esc[48;2;9;17;41m$u$esc[38;2;12;22;50m$esc[48;2;12;22;50m$u$esc[38;2;21;54;144m$esc[48;2;21;54;144m$u$esc[0m   $esc[1;37mComponents To Be Removed:$esc[0m
$esc[38;2;21;54;145m$esc[48;2;21;54;145m$u$esc[38;2;12;22;50m$esc[48;2;12;22;50m$u$esc[38;2;3;11;35m$esc[48;2;0;2;27m$u$esc[38;2;36;44;66m$esc[48;2;87;94;113m$u$esc[38;2;236;242;248m$esc[48;2;255;255;255m$u$esc[38;2;172;178;190m$esc[48;2;110;117;134m$u$esc[38;2;1;7;32m$esc[48;2;0;1;26m$u$esc[38;2;7;15;40m$esc[48;2;11;19;44m$u$esc[38;2;4;10;35m$esc[48;2;0;5;31m$u$esc[38;2;180;187;198m$esc[48;2;47;54;76m$u$esc[38;2;248;253;255m$esc[48;2;226;232;240m$u$esc[38;2;145;152;166m$esc[48;2;252;255;255m$u$esc[38;2;127;134;149m$esc[48;2;250;255;255m$u$esc[38;2;131;138;153m$esc[48;2;250;255;255m$u$esc[38;2;131;138;153m$esc[48;2;250;255;255m$u$esc[38;2;127;134;149m$esc[48;2;250;255;255m$u$esc[38;2;144;151;166m$esc[48;2;252;255;255m$u$esc[38;2;248;253;255m$esc[48;2;227;233;241m$u$esc[38;2;181;188;199m$esc[48;2;48;55;77m$u$esc[38;2;4;10;35m$esc[48;2;0;5;31m$u$esc[38;2;7;15;40m$esc[48;2;11;19;44m$u$esc[38;2;1;7;32m$esc[48;2;0;1;26m$u$esc[38;2;170;177;189m$esc[48;2;109;116;133m$u$esc[38;2;237;243;248m$esc[48;2;255;255;255m$u$esc[38;2;37;45;67m$esc[48;2;88;96;114m$u$esc[38;2;3;11;35m$esc[48;2;0;2;27m$u$esc[38;2;12;22;50m$esc[48;2;12;22;50m$u$esc[38;2;21;54;144m$esc[48;2;21;54;144m$u$esc[0m     $esc[31m*$esc[0m Application files (%LOCALAPPDATA%\Programs\AirFoxBT)
$esc[38;2;21;54;145m$esc[48;2;21;54;145m$u$esc[38;2;12;22;50m$esc[48;2;7;17;46m$u$esc[38;2;0;2;27m$esc[48;2;21;29;52m$u$esc[38;2;152;159;173m$esc[48;2;220;226;235m$u$esc[38;2;249;253;255m$esc[48;2;198;205;215m$u$esc[38;2;54;62;83m$esc[48;2;9;17;41m$u$esc[38;2;1;8;33m$esc[48;2;1;9;43m$u$esc[38;2;2;14;44m$esc[48;2;127;88;27m$u$esc[38;2;0;11;46m$esc[48;2;186;128;18m$u$esc[38;2;0;1;38m$esc[48;2;88;64;33m$u$esc[38;2;36;45;71m$esc[48;2;8;13;34m$u$esc[38;2;62;69;89m$esc[48;2;0;0;35m$u$esc[38;2;58;65;86m$esc[48;2;0;5;33m$u$esc[38;2;58;65;86m$esc[48;2;0;8;32m$u$esc[38;2;58;65;86m$esc[48;2;0;8;32m$u$esc[38;2;58;65;86m$esc[48;2;0;5;33m$u$esc[38;2;62;69;89m$esc[48;2;0;0;35m$u$esc[38;2;36;46;71m$esc[48;2;7;13;34m$u$esc[38;2;0;1;38m$esc[48;2;87;64;33m$u$esc[38;2;0;11;45m$esc[48;2;186;127;18m$u$esc[38;2;2;14;44m$esc[48;2;129;89;27m$u$esc[38;2;1;8;33m$esc[48;2;1;9;43m$u$esc[38;2;53;61;82m$esc[48;2;9;16;40m$u$esc[38;2;248;252;255m$esc[48;2;197;203;214m$u$esc[38;2;154;161;174m$esc[48;2;221;227;236m$u$esc[38;2;0;3;28m$esc[48;2;22;30;53m$u$esc[38;2;12;22;50m$esc[48;2;6;16;45m$u$esc[38;2;21;54;144m$esc[48;2;21;54;144m$u$esc[0m     $esc[31m*$esc[0m Start Menu & Desktop shortcuts
$esc[38;2;21;54;145m$esc[48;2;21;53;145m$u$esc[38;2;0;7;37m$esc[48;2;0;4;34m$u$esc[38;2;82;90;108m$esc[48;2;147;154;167m$u$esc[38;2;255;255;255m$esc[48;2;255;255;255m$u$esc[38;2;111;118;135m$esc[48;2;129;136;151m$u$esc[38;2;0;0;24m$esc[48;2;0;2;28m$u$esc[38;2;2;12;46m$esc[48;2;3;11;36m$u$esc[38;2;123;85;28m$esc[48;2;0;13;46m$u$esc[38;2;255;169;8m$esc[48;2;32;30;40m$u$esc[38;2;255;180;6m$esc[48;2;110;78;29m$u$esc[38;2;232;151;13m$esc[48;2;207;136;16m$u$esc[38;2;139;95;25m$esc[48;2;255;176;7m$u$esc[38;2;29;29;41m$esc[48;2;84;62;33m$u$esc[38;2;7;16;44m$esc[48;2;0;10;45m$u$esc[38;2;7;16;44m$esc[48;2;0;10;45m$u$esc[38;2;28;28;41m$esc[48;2;82;61;33m$u$esc[38;2;139;95;25m$esc[48;2;255;176;7m$u$esc[38;2;231;151;13m$esc[48;2;208;136;16m$u$esc[38;2;255;180;6m$esc[48;2;111;78;29m$u$esc[38;2;255;169;8m$esc[48;2;32;31;40m$u$esc[38;2;124;86;27m$esc[48;2;0;13;46m$u$esc[38;2;2;12;46m$esc[48;2;4;11;36m$u$esc[38;2;0;0;24m$esc[48;2;0;2;27m$u$esc[38;2;110;117;134m$esc[48;2;128;135;150m$u$esc[38;2;255;255;255m$esc[48;2;255;255;255m$u$esc[38;2;84;91;109m$esc[48;2;148;155;168m$u$esc[38;2;0;7;36m$esc[48;2;0;4;33m$u$esc[38;2;21;54;144m$esc[48;2;21;54;144m$u$esc[0m     $esc[31m*$esc[0m Windows Installed Apps registration
$esc[38;2;21;54;145m$esc[48;2;21;54;145m$u$esc[38;2;5;14;43m$esc[48;2;13;23;51m$u$esc[38;2;51;57;77m$esc[48;2;0;5;29m$u$esc[38;2;203;210;219m$esc[48;2;21;28;51m$u$esc[38;2;255;255;255m$esc[48;2;169;176;188m$u$esc[38;2;158;165;178m$esc[48;2;255;255;255m$u$esc[38;2;15;21;45m$esc[48;2;196;203;213m$u$esc[38;2;0;5;30m$esc[48;2;41;47;69m$u$esc[38;2;4;15;45m$esc[48;2;0;0;25m$u$esc[38;2;0;7;46m$esc[48;2;11;19;43m$u$esc[38;2;7;17;43m$esc[48;2;9;18;44m$u$esc[38;2;43;39;38m$esc[48;2;2;13;44m$u$esc[38;2;14;21;43m$esc[48;2;9;18;43m$u$esc[38;2;10;18;43m$esc[48;2;11;19;43m$u$esc[38;2;10;18;43m$esc[48;2;11;19;43m$u$esc[38;2;14;21;43m$esc[48;2;9;18;43m$u$esc[38;2;43;39;38m$esc[48;2;2;13;44m$u$esc[38;2;7;17;43m$esc[48;2;9;18;44m$u$esc[38;2;0;7;46m$esc[48;2;11;19;43m$u$esc[38;2;4;15;45m$esc[48;2;0;0;26m$u$esc[38;2;0;5;30m$esc[48;2;40;47;68m$u$esc[38;2;14;21;44m$esc[48;2;195;202;212m$u$esc[38;2;157;164;177m$esc[48;2;255;255;255m$u$esc[38;2;255;255;255m$esc[48;2;171;177;189m$u$esc[38;2;204;211;220m$esc[48;2;22;28;52m$u$esc[38;2;51;57;78m$esc[48;2;0;5;29m$u$esc[38;2;5;14;42m$esc[48;2;13;23;51m$u$esc[38;2;21;54;144m$esc[48;2;21;54;144m$u$esc[0m     $esc[31m*$esc[0m Startup autoruns & background tasks
$esc[38;2;21;54;145m$esc[48;2;21;54;145m$u$esc[38;2;12;22;50m$esc[48;2;12;22;50m$u$esc[38;2;12;20;43m$esc[48;2;11;19;42m$u$esc[38;2;2;9;34m$esc[48;2;12;20;44m$u$esc[38;2;3;9;33m$esc[48;2;7;15;39m$u$esc[38;2;128;135;150m$esc[48;2;0;0;25m$u$esc[38;2;255;255;255m$esc[48;2;87;94;112m$u$esc[38;2;228;235;242m$esc[48;2;236;242;249m$u$esc[38;2;76;82;102m$esc[48;2;251;255;255m$u$esc[38;2;0;0;25m$esc[48;2;116;123;139m$u$esc[38;2;8;16;40m$esc[48;2;0;5;30m$u$esc[38;2;11;20;43m$esc[48;2;3;11;36m$u$esc[38;2;11;19;43m$esc[48;2;12;20;44m$u$esc[38;2;11;19;43m$esc[48;2;11;19;43m$u$esc[38;2;11;19;43m$esc[48;2;11;19;43m$u$esc[38;2;11;19;43m$esc[48;2;12;20;44m$u$esc[38;2;11;20;43m$esc[48;2;4;11;36m$u$esc[38;2;8;16;40m$esc[48;2;0;5;30m$u$esc[38;2;0;0;24m$esc[48;2;115;122;138m$u$esc[38;2;75;81;101m$esc[48;2;251;255;255m$u$esc[38;2;227;234;241m$esc[48;2;237;243;250m$u$esc[38;2;255;255;255m$esc[48;2;88;95;113m$u$esc[38;2;130;136;151m$esc[48;2;0;0;25m$u$esc[38;2;3;9;34m$esc[48;2;7;14;39m$u$esc[38;2;2;9;34m$esc[48;2;12;20;44m$u$esc[38;2;12;20;43m$esc[48;2;11;19;42m$u$esc[38;2;12;22;50m$esc[48;2;12;22;50m$u$esc[38;2;21;54;144m$esc[48;2;21;54;144m$u$esc[0m   
$esc[38;2;21;54;145m$esc[48;2;21;54;144m$u$esc[38;2;12;22;50m$esc[48;2;12;22;50m$u$esc[38;2;11;19;42m$esc[48;2;11;19;42m$u$esc[38;2;11;19;43m$esc[48;2;11;19;43m$u$esc[38;2;11;19;43m$esc[48;2;11;19;43m$u$esc[38;2;10;18;42m$esc[48;2;11;19;43m$u$esc[38;2;0;0;25m$esc[48;2;11;20;44m$u$esc[38;2;50;57;78m$esc[48;2;0;4;29m$u$esc[38;2;206;213;222m$esc[48;2;21;28;51m$u$esc[38;2;255;255;255m$esc[48;2;169;176;188m$u$esc[38;2;158;165;177m$esc[48;2;255;255;255m$u$esc[38;2;15;21;45m$esc[48;2;196;203;213m$u$esc[38;2;0;5;30m$esc[48;2;41;48;70m$u$esc[38;2;12;20;44m$esc[48;2;0;1;26m$u$esc[38;2;12;20;44m$esc[48;2;0;0;26m$u$esc[38;2;0;5;30m$esc[48;2;40;47;69m$u$esc[38;2;14;21;44m$esc[48;2;195;202;212m$u$esc[38;2;157;163;176m$esc[48;2;255;255;255m$u$esc[38;2;255;255;255m$esc[48;2;170;177;189m$u$esc[38;2;207;214;223m$esc[48;2;22;28;52m$u$esc[38;2;51;58;79m$esc[48;2;0;3;28m$u$esc[38;2;0;0;25m$esc[48;2;11;20;44m$u$esc[38;2;10;18;42m$esc[48;2;11;19;43m$u$esc[38;2;11;19;43m$esc[48;2;11;19;43m$u$esc[38;2;11;19;43m$esc[48;2;11;19;43m$u$esc[38;2;11;19;42m$esc[48;2;11;19;42m$u$esc[38;2;12;22;50m$esc[48;2;12;22;49m$u$esc[38;2;21;54;144m$esc[48;2;21;54;145m$u$esc[0m   $esc[90mZero residual drivers or background services.$esc[0m
$esc[38;2;23;58;157m$esc[48;2;28;74;206m$u$esc[38;2;13;23;55m$esc[48;2;15;32;80m$u$esc[38;2;11;19;42m$esc[48;2;10;16;35m$u$esc[38;2;11;19;43m$esc[48;2;11;19;43m$u$esc[38;2;11;19;43m$esc[48;2;11;19;43m$u$esc[38;2;11;19;43m$esc[48;2;11;19;43m$u$esc[38;2;11;19;43m$esc[48;2;11;19;43m$u$esc[38;2;12;20;44m$esc[48;2;11;19;43m$u$esc[38;2;2;9;34m$esc[48;2;12;20;44m$u$esc[38;2;2;9;34m$esc[48;2;7;15;39m$u$esc[38;2;128;135;150m$esc[48;2;0;0;25m$u$esc[38;2;255;255;255m$esc[48;2;87;94;112m$u$esc[38;2;228;234;242m$esc[48;2;235;241;248m$u$esc[38;2;68;75;95m$esc[48;2;242;248;255m$u$esc[38;2;68;74;94m$esc[48;2;242;248;255m$u$esc[38;2;227;234;241m$esc[48;2;236;241;249m$u$esc[38;2;255;255;255m$esc[48;2;88;94;113m$u$esc[38;2;129;136;151m$esc[48;2;0;0;26m$u$esc[38;2;3;9;34m$esc[48;2;7;15;39m$u$esc[38;2;2;9;34m$esc[48;2;12;20;44m$u$esc[38;2;12;20;44m$esc[48;2;11;19;43m$u$esc[38;2;11;19;43m$esc[48;2;11;19;43m$u$esc[38;2;11;19;43m$esc[48;2;11;19;43m$u$esc[38;2;11;19;43m$esc[48;2;11;19;43m$u$esc[38;2;11;19;43m$esc[48;2;11;19;43m$u$esc[38;2;11;19;42m$esc[48;2;10;16;35m$u$esc[38;2;13;23;55m$esc[48;2;15;32;79m$u$esc[38;2;23;57;157m$esc[48;2;28;72;201m$u$esc[0m   
$esc[0m $esc[38;2;19;49;130m$esc[48;2;29;85;229m$u$esc[38;2;11;18;38m$esc[48;2;18;43;114m$u$esc[38;2;11;18;41m$esc[48;2;11;18;38m$u$esc[38;2;11;19;43m$esc[48;2;10;16;35m$u$esc[38;2;11;19;43m$esc[48;2;11;19;42m$u$esc[38;2;11;19;43m$esc[48;2;11;19;42m$u$esc[38;2;11;19;43m$esc[48;2;11;19;42m$u$esc[38;2;11;19;43m$esc[48;2;11;19;42m$u$esc[38;2;11;19;43m$esc[48;2;11;19;42m$u$esc[38;2;10;18;42m$esc[48;2;11;19;42m$u$esc[38;2;0;0;25m$esc[48;2;11;19;43m$u$esc[38;2;51;58;79m$esc[48;2;0;6;31m$u$esc[38;2;206;212;222m$esc[48;2;21;29;51m$u$esc[38;2;207;213;223m$esc[48;2;21;29;52m$u$esc[38;2;52;58;79m$esc[48;2;0;6;31m$u$esc[38;2;0;0;25m$esc[48;2;11;19;43m$u$esc[38;2;10;18;42m$esc[48;2;11;19;42m$u$esc[38;2;11;19;43m$esc[48;2;11;19;42m$u$esc[38;2;11;19;43m$esc[48;2;11;19;42m$u$esc[38;2;11;19;43m$esc[48;2;11;19;42m$u$esc[38;2;11;19;43m$esc[48;2;11;19;42m$u$esc[38;2;11;19;43m$esc[48;2;11;19;42m$u$esc[38;2;11;19;43m$esc[48;2;10;16;35m$u$esc[38;2;11;18;41m$esc[48;2;11;18;38m$u$esc[38;2;11;18;38m$esc[48;2;18;43;113m$u$esc[38;2;20;48;130m$esc[48;2;29;83;229m$u$esc[0m $esc[0m   $esc[90mVersion 1.0.0 `• FoxDen Software$esc[0m
$esc[0m $esc[0m $esc[38;2;29;87;234m$esc[49m$u$esc[38;2;20;49;134m$esc[49m$u$esc[38;2;15;31;79m$esc[48;2;28;74;209m$u$esc[38;2;13;23;54m$esc[48;2;23;59;160m$u$esc[38;2;12;21;49m$esc[48;2;22;53;146m$u$esc[38;2;12;21;49m$esc[48;2;20;53;146m$u$esc[38;2;12;22;50m$esc[48;2;20;53;146m$u$esc[38;2;12;22;50m$esc[48;2;20;53;146m$u$esc[38;2;12;22;50m$esc[48;2;20;53;146m$u$esc[38;2;12;22;50m$esc[48;2;20;53;146m$u$esc[38;2;13;22;50m$esc[48;2;20;53;146m$u$esc[38;2;7;17;45m$esc[48;2;20;53;146m$u$esc[38;2;7;17;45m$esc[48;2;20;53;146m$u$esc[38;2;13;22;50m$esc[48;2;20;53;146m$u$esc[38;2;12;22;50m$esc[48;2;20;53;146m$u$esc[38;2;12;22;50m$esc[48;2;20;53;146m$u$esc[38;2;12;22;50m$esc[48;2;20;53;146m$u$esc[38;2;12;22;50m$esc[48;2;20;53;146m$u$esc[38;2;12;21;49m$esc[48;2;20;53;146m$u$esc[38;2;12;21;49m$esc[48;2;22;53;146m$u$esc[38;2;13;23;54m$esc[48;2;23;59;160m$u$esc[38;2;15;31;78m$esc[48;2;28;76;206m$u$esc[38;2;20;49;134m$esc[49m$u$esc[38;2;32;86;234m$esc[49m$u$esc[0m $esc[0m $esc[0m
"@

$banner = $banner.Replace("$esc[49m", "$esc[48;2;0;0;0m")
Write-Host $banner

# 1. Confirmation Prompt
if (-not ($Force -or $Silent)) {
    Write-Host ""
    Write-Host "[?] Are you sure you want to completely uninstall AirFoxBT?" -ForegroundColor Yellow
    $confirm = Read-Host "    Type 'YES' to proceed with uninstall, or press Enter to cancel"
    if ($confirm -ne "YES") {
        Write-Host "`n[*] Uninstall cancelled. AirFoxBT remains untouched." -ForegroundColor Cyan
        Start-Sleep -Seconds 2
        return
    }
    Write-Host ""
}

# 2. Stop running AirFoxBT processes
Write-Host "[*] Terminating running AirFoxBT processes..." -ForegroundColor Cyan
Get-Process -Name "AirFoxBT" -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 800

# 3. Remove Task Scheduler background tasks
Write-Host "[*] Removing Task Scheduler background tasks..." -ForegroundColor Cyan
Unregister-ScheduledTask -TaskName "AirFoxBT" -Confirm:$false -ErrorAction SilentlyContinue | Out-Null
schtasks.exe /Delete /TN "AirFoxBT" /F *>$null 2>$null

# 4. Remove Start Menu & Desktop shortcuts
Write-Host "[*] Removing shortcuts..." -ForegroundColor Cyan
$userShortcut = "$env:APPDATA\Microsoft\Windows\Start Menu\Programs\AirFoxBT.lnk"
$legacyShortcut = "$env:ProgramData\Microsoft\Windows\Start Menu\Programs\AirFoxBT.lnk"
$desktopDir = [Environment]::GetFolderPath([Environment+SpecialFolder]::Desktop)
$desktopShortcut = Join-Path $desktopDir "AirFoxBT.lnk"

if (Test-Path $userShortcut) { Remove-Item -Path $userShortcut -Force -ErrorAction SilentlyContinue }
if (Test-Path $legacyShortcut) { Remove-Item -Path $legacyShortcut -Force -ErrorAction SilentlyContinue }
if (Test-Path $desktopShortcut) { Remove-Item -Path $desktopShortcut -Force -ErrorAction SilentlyContinue }

# 4b. Remove Windows Defender Firewall rules (if elevated)
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if ($isAdmin) {
    try {
        Get-NetFirewallRule -DisplayName "AirFoxBT*" -ErrorAction SilentlyContinue | Remove-NetFirewallRule -ErrorAction SilentlyContinue
    } catch { }
}

# 5. Remove Windows Installed Apps entry
Write-Host "[*] Removing Windows Installed Application entry..." -ForegroundColor Cyan
Remove-Item -Path "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\AirFoxBT" -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -Path "HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\AirFoxBT" -Recurse -Force -ErrorAction SilentlyContinue

# 6. Remove User AppData Programs directory
$installDir = "$env:LOCALAPPDATA\Programs\AirFoxBT"
Write-Host "[*] Removing files from $installDir..." -ForegroundColor Cyan

# Change PowerShell current directory to TEMP to release directory handle
Set-Location $env:TEMP

$thisScript = if ($PSCommandPath) { (Resolve-Path $PSCommandPath).Path } else { "" }
$installedScript = Join-Path $installDir "uninstall.ps1"

if (Test-Path $installDir) {
    if ($thisScript -eq $installedScript) {
        Get-ChildItem -Path $installDir -Exclude "uninstall.ps1" -ErrorAction SilentlyContinue | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue

        $tempBatch = Join-Path $env:TEMP "AirFoxBT_uninstall_cleanup.cmd"
@"
@echo off
:retry
ping 127.0.0.1 -n 2 >nul
rd /s /q "$installDir" 2>nul
if exist "$installDir" goto retry
del /f /q "%~f0" 2>nul
"@ | Set-Content -Path $tempBatch -Encoding ASCII

        Start-Process -FilePath "cmd.exe" -ArgumentList "/c `"$tempBatch`"" -WindowStyle Hidden
    }
    else {
        Remove-Item -Path $installDir -Recurse -Force -ErrorAction SilentlyContinue
    }
}

# 7. Optionally purge user configuration & sensor cache
$appDataDir = Join-Path $env:APPDATA "FoxDen\AirFoxBT"
if ($PurgeData) {
    Write-Host "[*] Purging user configuration and sensor data..." -ForegroundColor Yellow
    if (Test-Path $appDataDir) {
        Remove-Item -Path $appDataDir -Recurse -Force -ErrorAction SilentlyContinue
    }
}

Write-Host @"

  ======================================================
     ✅ AirFoxBT has been uninstalled successfully.
  ======================================================
  Configurations & sensor mappings are preserved in:
  $env:APPDATA\FoxDen\AirFoxBT (safe for future installs)
  ======================================================
"@ -ForegroundColor Green

Write-Host "`n[*] Exiting uninstaller in 3 seconds (or press any key to close now)..." -ForegroundColor DarkGray
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
