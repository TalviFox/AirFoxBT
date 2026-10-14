<#
.SYNOPSIS
    AirFoxBT Minimal Standalone Test Proxy
    Emulates the ESPHome Bluetooth Proxy wire protocol on TCP 6053.
    Streams a brand-new simulated BTHome v2 sensor ('FoxTest') into Home Assistant.
#>

$ErrorActionPreference = "Continue"

$Port = 6053
$LocalIp = "10.1.0.218"

# Sensor MAC: AA:BB:CC:11:22:33
$SensorMacUlong = [Convert]::ToUInt64("AABBCC112233", 16)

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

# ----------------- BTHOME SENSOR BUILDER -----------------
function Build-BTHomeRawBle {
    # BTHome v2 packet for "FoxTest":
    # 0x40 = Header (v2, unencrypted)
    # 0x02 = Temp (22.50 C = 2250 = 0x08CA)
    # 0x03 = Humidity (50.00 % = 5000 = 0x1388)
    # 0x01 = Battery (100% = 0x64)
    $bthomePayload = [byte[]]@(
        0x40,
        0x02, 0xCA, 0x08,
        0x03, 0x88, 0x13,
        0x01, 0x64
    )

    $ms = [System.IO.MemoryStream]::new()
    # Service Data 16-bit UUID (0x16) -> 0xFCD2 + payload
    $serviceData = [byte[]]@(0xD2, 0xFC) + $bthomePayload
    $ms.WriteByte([byte]($serviceData.Length + 1))
    $ms.WriteByte(0x16)
    $ms.Write($serviceData, 0, $serviceData.Length)

    # Name section (0x09) -> "FoxTest"
    $nameBytes = [System.Text.Encoding]::ASCII.GetBytes("FoxTest")
    $ms.WriteByte([byte]($nameBytes.Length + 1))
    $ms.WriteByte(0x09)
    $ms.Write($nameBytes, 0, $nameBytes.Length)

    return $ms.ToArray()
}

function Build-BleAdvFrame {
    $rawBle = Build-BTHomeRawBle

    # ZigZag encode -60 dBm -> 119
    [uint64]$zigzagRssi = [uint64]119

    $subMs = [System.IO.MemoryStream]::new()
    Write-ProtoFieldVarInt $subMs 1 $SensorMacUlong
    Write-ProtoFieldVarInt $subMs 2 $zigzagRssi
    Write-ProtoFieldVarInt $subMs 3 0 # Address type: public
    Write-ProtoFieldBytes $subMs 4 $rawBle

    $msgMs = [System.IO.MemoryStream]::new()
    Write-ProtoFieldBytes $msgMs 1 $subMs.ToArray()

    # Message Type 93 (BluetoothLEAdvertisementResponse)
    return Frame-Message 93 $msgMs.ToArray()
}

# ----------------- MAIN SERVER -----------------
Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "  AirFoxBT Standalone Test Proxy (Simulated Sensor 'FoxTest')" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "[-] Gateway IP  : $LocalIp" -ForegroundColor Yellow
Write-Host "[-] Port        : $Port" -ForegroundColor Yellow
Write-Host "[-] Virtual MAC : AA:BB:CC:11:22:33 (Name: FoxTest)" -ForegroundColor Yellow
Write-Host "[-] Telemetry   : 72.5°F (22.5°C), 50% RH, 100% Battery" -ForegroundColor Yellow
Write-Host "----------------------------------------------------------" -ForegroundColor DarkGray

$listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Any, $Port)
try {
    $listener.Start()
} catch {
    Write-Host "[!] Port $Port is in use! Stop AirFoxBT first." -ForegroundColor Red
    exit 1
}

Write-Host "[+] Listening for Home Assistant on 0.0.0.0:$Port..." -ForegroundColor Green

$advFrame = Build-BleAdvFrame

try {
    while ($true) {
        $client = $listener.AcceptTcpClient()
        $ep = $client.Client.RemoteEndPoint.ToString()
        Write-Host "[TCP] Home Assistant connected from $ep!" -ForegroundColor Green

        $stream = $client.GetStream()

        try {
            while ($client.Connected) {
                $pre = $stream.ReadByte()
                if ($pre -lt 0) { break }
                if ($pre -ne 0x00) { continue }

                $lenRes = Read-VarInt $stream
                $len = $lenRes.Value
                if ($len -gt 65536) { continue }

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
                        Write-Host "[TCP] HelloRequest -> Sending HelloResponse" -ForegroundColor Gray
                        $resMs = [System.IO.MemoryStream]::new()
                        Write-ProtoFieldVarInt $resMs 1 1  # api_major = 1
                        Write-ProtoFieldVarInt $resMs 2 10 # api_minor = 10
                        Write-ProtoFieldString $resMs 3 "AirFoxBT Test Proxy"
                        Write-ProtoFieldString $resMs 4 "airfoxbt"
                        $frame = Frame-Message 2 $resMs.ToArray()
                        $stream.Write($frame, 0, $frame.Length)
                    }

                    3 { # AuthRequest
                        $resMs = [System.IO.MemoryStream]::new()
                        Write-ProtoFieldVarInt $resMs 1 0
                        $frame = Frame-Message 4 $resMs.ToArray()
                        $stream.Write($frame, 0, $frame.Length)
                    }

                    9 { # DeviceInfoRequest
                        Write-Host "[TCP] DeviceInfoRequest -> Sending DeviceInfo" -ForegroundColor Gray
                        $resMs = [System.IO.MemoryStream]::new()
                        Write-ProtoFieldVarInt $resMs 1 0
                        Write-ProtoFieldString $resMs 2 "airfoxbt"
                        Write-ProtoFieldString $resMs 3 "50:EB:F6:B2:37:17"
                        Write-ProtoFieldString $resMs 4 "2024.12.0"
                        Write-ProtoFieldString $resMs 5 "Oct 06 2026"
                        Write-ProtoFieldString $resMs 6 "AirFoxBT Proxy"
                        Write-ProtoFieldString $resMs 8 "FoxDen.AirFoxBT"
                        Write-ProtoFieldString $resMs 9 "1.0.0"
                        Write-ProtoFieldVarInt $resMs 11 1 # legacy_bluetooth_proxy_version: 1
                        Write-ProtoFieldString $resMs 12 "FoxDen"
                        Write-ProtoFieldString $resMs 13 "AirFoxBT"
                        Write-ProtoFieldVarInt $resMs 15 3 # bluetooth_proxy_feature_flags: 3 (PASSIVE | ACTIVE)
                        Write-ProtoFieldString $resMs 18 "50:EB:F6:B2:37:17"
                        $frame = Frame-Message 10 $resMs.ToArray()
                        $stream.Write($frame, 0, $frame.Length)
                    }

                    11 { # ListEntitiesRequest
                        $frame = Frame-Message 19 ([byte[]]@())
                        $stream.Write($frame, 0, $frame.Length)
                    }

                    20 { # SubscribeStatesRequest
                    }

                    66 { # SubscribeBluetoothLEAdvertisementsRequest
                        Write-Host "[TCP] *** HA SUBSCRIBED TO BLE ADVERTISEMENTS ***" -ForegroundColor Magenta
                        
                        # Send scanner state RUNNING (Type 126)
                        $stateMs = [System.IO.MemoryStream]::new()
                        Write-ProtoFieldVarInt $stateMs 1 2 # state=RUNNING
                        Write-ProtoFieldVarInt $stateMs 2 1 # mode=ACTIVE
                        Write-ProtoFieldVarInt $stateMs 3 1 # configured_mode=ACTIVE
                        $stateFrame = Frame-Message 126 $stateMs.ToArray()
                        $stream.Write($stateFrame, 0, $stateFrame.Length)

                        # Stream virtual BTHome sensor packets every 4 seconds
                        $pktNum = 1
                        while ($client.Connected) {
                            $stream.Write($advFrame, 0, $advFrame.Length)
                            Write-Host "[TCP] -> Sent FoxTest sensor beacon #$pktNum to Home Assistant" -ForegroundColor Green
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
            Write-Host "[TCP] HA disconnected: $($_.Exception.Message)" -ForegroundColor DarkGray
        } finally {
            $client.Close()
        }
    }
} finally {
    $listener.Stop()
    Write-Host "[+] Test proxy stopped." -ForegroundColor Yellow
}
