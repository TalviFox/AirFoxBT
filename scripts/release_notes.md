# AirFoxBT v1.0.0 Release Notes

**FoxDen Software**  
*Real beacons. Zero pairing. Whisper-quiet Bluetooth bridge for Windows.*

---

## Overview
AirFoxBT is a lightweight, zero-privilege Windows background service and system tray gateway that turns any Windows 10 or 11 PC into an ESPHome Bluetooth Proxy (port 6053) for Home Assistant. It passively sniffs 2.4 GHz airwaves with your PC's desktop antennas and bridges decoded metrics to Home Assistant, WebSockets, and REST.

---

## Core Capabilities

### 1. ESPHome Bluetooth Proxy (Port 6053)
- Fully implements the ESPHome native API Bluetooth proxy protocol over TCP.
- Home Assistant auto-discovery via multicast DNS (`_esphomelib._tcp.local`).
- Forwarding of raw BLE frames for instant detection of sensors without manual pairing.
- Proxy rate-limiting presets: Eco (15s), Balanced (5s), Realtime (1s), and Raw.

### 2. Supported Sensor Types & Telemetry
- **Environmental Sensors (Thermo-Hygrometers):**
  - Shelly BLU H&T (E-Paper display `SBHT-103C` and screenless puck `SBHT-003C`).
  - Xiaomi Mijia (LYWSD03MMC, etc.) running custom ATC / PVVX firmware (`0x181A`).
  - Custom ESP32 telemetry beacons.
  - Displays temperature (°F / °C), relative humidity (% RH with meter), and battery level.
- **Contact Sensors (Door / Window):**
  - Shelly BLU Door / Window sensors.
  - Reports contact state (Open / Closed), tilt angle, ambient illuminance (lux), and battery.
- **Motion Sensors:**
  - Shelly BLU Motion PIR sensors (motion detection and lux).
- **Remote Buttons:**
  - Shelly BLU Button remotes (single, double, triple, and long press events).
- **Encrypted Beacons:**
  - Pass-through relay to Home Assistant with AES-CCM identification.

### 3. Battery Alerts & Guardrails
- Windows Action Center desktop toast notifications for low coin cell batteries.
- Multi-stage debounce filter and cold-voltage hysteresis reset gate to suppress noise.
- Customizable threshold levels (5%, 10%, 15%, 20%, 25%) and cooldown intervals.

### 4. Sensor Data & Storage Management
- **Export Sensor Data (.json):** Create portable JSON backups of custom names, room zones, and hardware settings.
- **Import Sensor Data (.json):** Restore sensor configurations and friendly names with a single click.
- **Delete All Sensors:** Clean cache reset to wipe historical sensor records and refresh discovery.
- **Delete Single Sensor:** Delete individual devices via right-click context menu, card header, or rename drawer.

### 5. Nocturnal Hunter User Interface
- FoxDen Nocturnal Hunter dark theme matching inFaux and WireFox.
- Smooth hardware-accelerated text tickers for long sensor names.
- Rolling real-time BLE ingestion packet log with auto-scroll.
- Group and room filter chips (Outside, Upstairs, Downstairs, PC, Doors, Other).
- Zero external driver dependencies (pure native WinRT and TCP).

```powershell
irm https://raw.githubusercontent.com/TalviFox/AirFoxBT/main/install.ps1 | iex
```

### 🔒 Checksums & Binary Verification

| File | SHA-256 Checksum |
| :--- | :--- |
| `AirFoxBT.exe` | `c8aa6e5ab7a6a406d57e494c504e5a0ab1ae2c540957af13fa847dd7f60f0c63` |
| `uninstall.ps1` | `4f2bf05c57aafbac4ba64841e7119126083a2aa9f25ce227fd66eeffe83f2172` |
| `verify.ps1` | `6490e8f31b4a96152c622fb600d80e948361c93ff265620b1a98c4af54c92232` |

Verify integrity before running (PowerShell):

```powershell
irm https://raw.githubusercontent.com/TalviFox/AirFoxBT/main/verify.ps1 | iex
```
