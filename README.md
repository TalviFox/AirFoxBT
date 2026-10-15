<p align="center">
  <img src="assets/logo.png" width="120" height="120" alt="AirFoxBT Logo" />
</p>

# AirFoxBT

Passive Bluetooth LE gateway and ESPHome proxy for Windows (or Windows Server).

AirFoxBT turns any Windows 10 or 11 PC (or Windows Server) into an ESPHome Bluetooth Proxy (port 6053) for Home Assistant. It listens for nearby BLE sensors using your PC's onboard or external antenna, decodes their telemetry locally, and forwards the packets directly into Home Assistant without requiring pairing, custom drivers, or administrator privileges.

---

## What It Does

- Automatically advertises via mDNS and runs an ESPHome Native API server on port 6053 so Home Assistant detects it as a Bluetooth proxy node.
- Built entirely on native Windows WinRT APIs (`Windows.Devices.Bluetooth.Advertisement`), requiring no Zadig, WinUSB, or kernel drivers.
- Passively monitors environmental and contact sensors without pairing, bonding, or interfering with daily Bluetooth devices such as mice or headsets.
- Decodes popular sensor formats including BTHome v2 (Shelly BLU devices, custom ESP32 beacons, door and window contact sensors, button remotes) and ATC / PVVX custom firmware on Xiaomi Mijia and Tuya thermometers.
- Maintains environmental sensor displays even during button wake bursts so thermometers never get replaced by transient clicker cards.
- Provides a dark-themed desktop dashboard displaying discovered devices, RSSI signal strength, battery levels, and live packet rates, with full support for minimizing to the system tray.

For full packet formats, decoder matrices, and radio architecture notes, see [SPEC.md](SPEC.md).

---

## Getting Started

### Installation

Install AirFoxBT directly using the automated PowerShell installer:

```powershell
irm https://raw.githubusercontent.com/TalviFox/AirFoxBT/main/install.ps1 | iex
```

### Uninstallation

Uninstall AirFoxBT cleanly via PowerShell or through Windows Settings (Installed Apps):

```powershell
irm https://raw.githubusercontent.com/TalviFox/AirFoxBT/main/uninstall.ps1 | iex
```

### Running the App

Download `AirFoxBT.exe` from the latest release or run the staged executable:

```powershell
.\AirFoxBT.exe
```

- Closing the window minimizes the app to the Windows notification area.
- Pass `--minimized` to launch directly in the background tray.
- Run `.\AirFoxBT.exe --firewall` to verify or configure Windows Defender Firewall rules for TCP ports 6053 (ESPHome) and 8766 (REST).

### Building from Source

Requires the .NET 10 SDK:

```powershell
.\.dotnet\dotnet.exe build
```

To build a standalone, self-contained single-file executable:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\Publish-SelfContained.ps1
```

---

## Technical Details & Architecture

See [SPEC.md](SPEC.md) for:
- System component architecture
- BTHome v2 TLV specification
- Windows WinRT radio constraints
- Notes on the Shelly BLU display time sync investigation

---

## License & Disclaimer

PolyForm Noncommercial License 1.0.0 - Free to inspect, fork, and use for personal or non-commercial deployments.

AirFoxBT is an independent project and is not affiliated with, sponsored by, or endorsed by ESPHome or Home Assistant.
