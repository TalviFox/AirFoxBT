<#
.SYNOPSIS
    AirFoxBT Standalone Proof-of-Concept in pure PowerShell.
    Emulates the ESPHome Bluetooth Proxy wire protocol (TCP port 6053).
    Streams real BTHome v2 binary packets to Home Assistant.
    Announces _esphomelib._tcp.local via mDNS without port 5353 collision.
#>

$ErrorActionPreference = "Stop"

$Port = 6053
$MdnsAddr = [System.Net.IPAddress]::Parse("224.0.0.251")
$MdnsPort = 5353

# Detect local LAN IPv4 address
$LocalIp = (Get-NetIPAddress -AddressFamily IPv4 | Where-Object { 
    $_.InterfaceAlias -notmatch 'Loopback|vEthernet|Virtual|WSL' -and $_.IPAddress -notlike '169.254*' 
} | Select-Object -First 1).IPAddress

if (-not $LocalIp) { $LocalIp = "127.0.0.1" }

$MacAddrUlong = [Convert]::ToUInt64("AA0011223344", 16)

# ----------------- WINDOWS FIREWALL HELPER -----------------
$IsAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if ($IsAdmin) {
    try {
        netsh advfirewall firewall add rule name="AirFoxBT ESPHome Proxy (Port 6053)" dir=in action=allow protocol=TCP localport=$Port | Out-Null
        Write-Host "[+] Windows Firewall rule active for TCP port $Port." -ForegroundColor Green
    } catch { }
} else {
    Write-Host "[!] Note: If Home Assistant cannot connect, run this once in Admin PowerShell:" -ForegroundColor DarkYellow
    Write-Host "    netsh advfirewall firewall add rule name=`"AirFoxBT ESPHome Proxy`" dir=in action=allow protocol=TCP localport=$Port" -ForegroundColor Cyan
}

# ----------------- VARINT & PROTOBUF HELPERS -----------------
function Write-VarInt([System.IO.Stream]$Stream, [uint64]$Value) {
    while ($Value -gt 0x7F) {
        $Stream.WriteByte([byte](($Value -band 0x7F) -bor 0x80))
        $Value = $Value -shr 7
    }
    $Stream.WriteByte([byte]($Value -band 0x7F))
}

function Read-VarInt([System.IO.Stream]$Stream) {
    [uint64]$res = 0
    [int]$shift = 0
    [int]$count = 0
    while ($true) {
        $b = $Stream.ReadByte()
        if ($b -lt 0) { return @{ Value = [uint64]0; Count = 0 } }
        $count++
        $res = $res -bor ([uint64]($b -band 0x7F) -shl $shift)
        if (($b -band 0x80) -eq 0) { break }
        $shift += 7
    }
    return @{ Value = $res; Count = $count }
}

function Write-ProtoFieldVarInt([System.IO.Stream]$Stream, [int]$Field, [uint64]$Val) {
    Write-VarInt $Stream (($Field -shl 3) -bor 0)
    Write-VarInt $Stream $Val
}

function Write-ProtoFieldBytes([System.IO.Stream]$Stream, [int]$Field, [byte[]]$Data) {
    Write-VarInt $Stream (($Field -shl 3) -bor 2)
    Write-VarInt $Stream ([uint64]$Data.Length)
    $Stream.Write($Data, 0, $Data.Length)
}

function Write-ProtoFieldString([System.IO.Stream]$Stream, [int]$Field, [string]$Str) {
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($Str)
    Write-ProtoFieldBytes $Stream $Field $bytes
}

function Frame-Message([uint64]$Type, [byte[]]$Payload) {
    $lenMs = [System.IO.MemoryStream]::new()
    Write-VarInt $lenMs ([uint64]$Payload.Length)
    $lenBytes = $lenMs.ToArray()

    $typeMs = [System.IO.MemoryStream]::new()
    Write-VarInt $typeMs $Type
    $typeBytes = $typeMs.ToArray()

    $outMs = [System.IO.MemoryStream]::new()
    $outMs.WriteByte(0x00) # Preamble
    $outMs.Write($lenBytes, 0, $lenBytes.Length)
    $outMs.Write($typeBytes, 0, $typeBytes.Length)
    if ($Payload.Length -gt 0) {
        $outMs.Write($Payload, 0, $Payload.Length)
    }
    return $outMs.ToArray()
}

# ----------------- BTHOME PACKET BUILDER -----------------
function Build-BTHomeRawBle {
    # BTHome v2 packet:
    # 0x40 = Header (v2, unencrypted)
    # 0x02 = Temp (21.50 C = 2150 = 0x0866)
    # 0x03 = Humidity (48.00 % = 4800 = 0x12C0)
    # 0x01 = Battery (95% = 0x5F)
    $bthomePayload = [byte[]]@(
        0x40,
        0x02, 0x66, 0x08,
        0x03, 0xC0, 0x12,
        0x01, 0x5F
    )

    $ms = [System.IO.MemoryStream]::new()
    # Service Data 16-bit UUID (0x16) -> 0xFCD2 + payload
    $serviceData = [byte[]]@(0xD2, 0xFC) + $bthomePayload
    $ms.WriteByte([byte]($serviceData.Length + 1))
    $ms.WriteByte(0x16)
    $ms.Write($serviceData, 0, $serviceData.Length)

    # Name section (0x09) -> "FoxAir"
    $nameBytes = [System.Text.Encoding]::ASCII.GetBytes("FoxAir")
    $ms.WriteByte([byte]($nameBytes.Length + 1))
    $ms.WriteByte(0x09)
    $ms.Write($nameBytes, 0, $nameBytes.Length)

    return $ms.ToArray()
}

function Build-BleAdvFrame {
    $rawBle = Build-BTHomeRawBle

    # ZigZag encode -65 dBm -> 129
    [uint64]$zigzagRssi = [uint64]129

    $subMs = [System.IO.MemoryStream]::new()
    Write-ProtoFieldVarInt $subMs 1 $MacAddrUlong
    Write-ProtoFieldVarInt $subMs 2 $zigzagRssi
    Write-ProtoFieldVarInt $subMs 3 0 # Address type: public
    Write-ProtoFieldBytes $subMs 4 $rawBle

    $msgMs = [System.IO.MemoryStream]::new()
    Write-ProtoFieldBytes $msgMs 1 $subMs.ToArray()

    return Frame-Message 93 $msgMs.ToArray()
}

# ----------------- mDNS ANNOUNCER -----------------
function Start-MdnsBroadcaster() {
    $job = [powershell]::Create().AddScript({
        param($LocalIp, $Port)
        $client = [System.Net.Sockets.UdpClient]::new()
        $client.Client.SetSocketOption([System.Net.Sockets.SocketOptionLevel]::IP, [System.Net.Sockets.SocketOptionName]::MulticastTimeToLive, 255)
        $target = [System.Net.IPEndPoint]::new([System.Net.IPAddress]::Parse("224.0.0.251"), 5353)

        function Write-DnsName([System.IO.BinaryWriter]$bw, [string]$name) {
            foreach ($part in $name.Split('.')) {
                if ($part) {
                    $bytes = [System.Text.Encoding]::ASCII.GetBytes($part)
                    $bw.Write([byte]$bytes.Length)
                    $bw.Write($bytes)
                }
            }
            $bw.Write([byte]0)
        }

        while ($true) {
            try {
                $ms = [System.IO.MemoryStream]::new()
                $bw = [System.IO.BinaryWriter]::new($ms)

                # Header: ID=0, Flags=0x8400 (Authoritative), QD=0, AN=4, NS=0, AR=0
                $bw.Write([ushort]0)
                $bw.Write([ushort][System.Net.IPAddress]::HostToNetworkOrder([short]0x8400))
                $bw.Write([ushort]0)
                $bw.Write([ushort][System.Net.IPAddress]::HostToNetworkOrder([short]4))
                $bw.Write([ushort]0)
                $bw.Write([ushort]0)

                # 1. PTR: _esphomelib._tcp.local
                Write-DnsName $bw "_esphomelib._tcp.local"
                $bw.Write([ushort][System.Net.IPAddress]::HostToNetworkOrder([short]12)) # PTR
                $bw.Write([ushort][System.Net.IPAddress]::HostToNetworkOrder([short]0x8001)) # IN + flush
                $bw.Write([int][System.Net.IPAddress]::HostToNetworkOrder([int]4500))
                
                $ptrDataMs = [System.IO.MemoryStream]::new()
                $ptrBw = [System.IO.BinaryWriter]::new($ptrDataMs)
                Write-DnsName $ptrBw "airfoxbt._esphomelib._tcp.local"
                $ptrBytes = $ptrDataMs.ToArray()
                $bw.Write([ushort][System.Net.IPAddress]::HostToNetworkOrder([short]$ptrBytes.Length))
                $bw.Write($ptrBytes)

                # 2. SRV: airfoxbt._esphomelib._tcp.local -> port 6053
                Write-DnsName $bw "airfoxbt._esphomelib._tcp.local"
                $bw.Write([ushort][System.Net.IPAddress]::HostToNetworkOrder([short]33)) # SRV
                $bw.Write([ushort][System.Net.IPAddress]::HostToNetworkOrder([short]0x8001))
                $bw.Write([int][System.Net.IPAddress]::HostToNetworkOrder([int]4500))
                
                $srvDataMs = [System.IO.MemoryStream]::new()
                $srvBw = [System.IO.BinaryWriter]::new($srvDataMs)
                $srvBw.Write([ushort]0) # Priority
                $srvBw.Write([ushort]0) # Weight
                $srvBw.Write([ushort][System.Net.IPAddress]::HostToNetworkOrder([short]$Port))
                Write-DnsName $srvBw "airfoxbt.local"
                $srvBytes = $srvDataMs.ToArray()
                $bw.Write([ushort][System.Net.IPAddress]::HostToNetworkOrder([short]$srvBytes.Length))
                $bw.Write($srvBytes)

                # 3. TXT:
                Write-DnsName $bw "airfoxbt._esphomelib._tcp.local"
                $bw.Write([ushort][System.Net.IPAddress]::HostToNetworkOrder([short]16)) # TXT
                $bw.Write([ushort][System.Net.IPAddress]::HostToNetworkOrder([short]0x8001))
                $bw.Write([int][System.Net.IPAddress]::HostToNetworkOrder([int]4500))
                $txt = [System.Text.Encoding]::ASCII.GetBytes("`x0fmac=aa0011223344`x13friendly_name=AirFoxBT`x15project_name=airfoxbt")
                $bw.Write([ushort][System.Net.IPAddress]::HostToNetworkOrder([short]$txt.Length))
                $bw.Write($txt)

                # 4. A record: airfoxbt.local -> LocalIp
                Write-DnsName $bw "airfoxbt.local"
                $bw.Write([ushort][System.Net.IPAddress]::HostToNetworkOrder([short]1)) # A
                $bw.Write([ushort][System.Net.IPAddress]::HostToNetworkOrder([short]0x8001))
                $bw.Write([int][System.Net.IPAddress]::HostToNetworkOrder([int]4500))
                $ipBytes = [System.Net.IPAddress]::Parse($LocalIp).GetAddressBytes()
                $bw.Write([ushort][System.Net.IPAddress]::HostToNetworkOrder([short]4))
                $bw.Write($ipBytes)

                $packet = $ms.ToArray()
                $client.Send($packet, $packet.Length, $target) | Out-Null
            } catch { }

            [System.Threading.Thread]::Sleep(12000)
        }
    }).AddArgument($LocalIp).AddArgument($Port)

    $async = $job.BeginInvoke()
    return $job
}

# ----------------- MAIN TCP SERVER -----------------
Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "  🦊 AirFoxBT Pure PowerShell ESPHome / BTHome Proxy POC" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "[-] LAN IP  : $LocalIp" -ForegroundColor Yellow
Write-Host "[-] Port    : $Port (ESPHome Protocol)" -ForegroundColor Yellow
Write-Host "[-] Payload : Native BTHome v2 Beacon Stream" -ForegroundColor Yellow
Write-Host "----------------------------------------------------------" -ForegroundColor DarkGray

$mdnsJob = Start-MdnsBroadcaster
Write-Host "[+] mDNS Broadcaster active on ephemeral port (no 5353 collision)!" -ForegroundColor Green

$listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Any, $Port)
$listener.Start()

Write-Host "[+] Listening for Home Assistant on 0.0.0.0:$Port..." -ForegroundColor Green
Write-Host ""
Write-Host "👉 Test in Home Assistant:" -ForegroundColor Cyan
Write-Host "   Option A: Check HA Notifications for auto-discovered 'AirFoxBT'." -ForegroundColor Gray
Write-Host "   Option B: Settings -> Devices & Services -> Add Integration -> ESPHome:" -ForegroundColor Gray
Write-Host "             Host: $LocalIp, Port: $Port, Password: (leave empty)" -ForegroundColor Gray
Write-Host ""
Write-Host "Waiting for connection... (Press Ctrl+C to stop)`n" -ForegroundColor DarkYellow

try {
    while ($true) {
        $client = $listener.AcceptTcpClient()
        $ep = $client.Client.RemoteEndPoint.ToString()
        Write-Host "[TCP] ⚡ Home Assistant connected from $ep!" -ForegroundColor Green

        $stream = $client.GetStream()
        $subscribed = $false

        try {
            while ($client.Connected) {
                # 1. Preamble
                $pre = $stream.ReadByte()
                if ($pre -lt 0) { break }
                if ($pre -ne 0x00) { continue }

                # 2. Length (size of payload object; type is not part of this)
                $lenRes = Read-VarInt $stream
                $len = $lenRes.Value
                if ($len -gt 65536) { continue }

                # 3. Type
                $typeRes = Read-VarInt $stream
                $type = $typeRes.Value
                
                $payload = [byte[]]::new([int]$len)
                if ($len -gt 0) {
                    $readTotal = 0
                    while ($readTotal -lt [int]$len) {
                        $r = $stream.Read($payload, $readTotal, [int]$len - $readTotal)
                        if ($r -le 0) { break }
                        $readTotal += $r
                    }
                }

                switch ($type) {
                    1 { # HelloRequest
                        Write-Host "[TCP] Received HelloRequest -> Replying HelloResponse" -ForegroundColor Gray
                        $resMs = [System.IO.MemoryStream]::new()
                        Write-ProtoFieldVarInt $resMs 1 1  # api_major = 1
                        Write-ProtoFieldVarInt $resMs 2 10 # api_minor = 10
                        Write-ProtoFieldString $resMs 3 "AirFoxBT Bluetooth Proxy"
                        Write-ProtoFieldString $resMs 4 "airfoxbt"
                        $frame = Frame-Message 2 $resMs.ToArray()
                        $stream.Write($frame, 0, $frame.Length)
                    }

                    3 { # AuthRequest
                        Write-Host "[TCP] Received AuthRequest -> Replying AuthOK" -ForegroundColor Gray
                        $resMs = [System.IO.MemoryStream]::new()
                        Write-ProtoFieldVarInt $resMs 1 0 # invalid_password = false
                        $frame = Frame-Message 4 $resMs.ToArray()
                        $stream.Write($frame, 0, $frame.Length)
                    }

                    9 { # DeviceInfoRequest
                        Write-Host "[TCP] Received DeviceInfoRequest -> Replying as BT Proxy" -ForegroundColor Gray
                        $resMs = [System.IO.MemoryStream]::new()
                        Write-ProtoFieldVarInt $resMs 1 0
                        Write-ProtoFieldString $resMs 2 "airfoxbt"
                        Write-ProtoFieldString $resMs 3 "AA:00:11:22:33:44"
                        Write-ProtoFieldString $resMs 4 "2024.12.0"
                        Write-ProtoFieldString $resMs 5 "Oct 06 2026"
                        Write-ProtoFieldString $resMs 6 "AirFoxBT Proxy"
                        Write-ProtoFieldString $resMs 8 "FoxDen.AirFoxBT"
                        Write-ProtoFieldString $resMs 9 "1.0.0"
                        Write-ProtoFieldString $resMs 12 "FoxDen"
                        Write-ProtoFieldString $resMs 13 "AirFoxBT"
                        Write-ProtoFieldVarInt $resMs 15 3 # Flag 3: Bluetooth Proxy Enabled!
                        Write-ProtoFieldString $resMs 18 "AA:00:11:22:33:44"
                        $frame = Frame-Message 10 $resMs.ToArray()
                        $stream.Write($frame, 0, $frame.Length)
                    }

                    11 { # ListEntitiesRequest
                        $frame = Frame-Message 19 ([byte[]]@()) # ListEntitiesDone
                        $stream.Write($frame, 0, $frame.Length)
                    }

                    20 { # SubscribeStatesRequest
                        # No states to send
                    }

                    66 { # SubscribeBluetoothLEAdvertisementsRequest
                        Write-Host "`n[TCP] 🎯🎯🎯 Home Assistant SUBSCRIBED to BLE Advertisement Stream!" -ForegroundColor Magenta
                        Write-Host "[TCP] Streaming BTHome packets (Temp: 21.5°C, Hum: 48%, Bat: 95%) directly into HA..." -ForegroundColor Cyan
                        $subscribed = $true

                        # Send immediately, then loop
                        $advFrame = Build-BleAdvFrame
                        $pktNum = 1
                        
                        while ($client.Connected -and $subscribed) {
                            $stream.Write($advFrame, 0, $advFrame.Length)
                            Write-Host "[TCP] 📡 Streamed BTHome packet #$pktNum to Home Assistant" -ForegroundColor Green
                            $pktNum++
                            [System.Threading.Thread]::Sleep(4000)
                        }
                    }

                    7 { # PingRequest
                        $frame = Frame-Message 8 ([byte[]]@())
                        $stream.Write($frame, 0, $frame.Length)
                    }
                }
            }
        } catch {
            Write-Host "[TCP] Client disconnected ($($_.Exception.Message))" -ForegroundColor DarkGray
        } finally {
            $client.Close()
        }
    }
} finally {
    $listener.Stop()
    if ($mdnsJob) { $mdnsJob.Dispose() }
    Write-Host "`n[+] Proxy stopped cleanly." -ForegroundColor Yellow
}
