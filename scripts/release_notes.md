# AirFoxBT v1.0.1 Release Notes

FoxDen Software  
Real beacons. Zero pairing. Whisper-quiet Bluetooth bridge for Windows.

---

## Overview

AirFoxBT 1.0.1 is a stability, legibility, and continuity update for the zero-driver Windows BLE passive gateway. This release resolves graph dropout gaps in Home Assistant for sleeping thermo-hygrometers, delivers high-contrast MAC address identification across the dashboard, cleans up Windows event log warnings, and adds automated crash diagnostics.

---

## Key Improvements in v1.0.1

### Smart Relay Keepalive Buffer
- Eliminates unavailable dropouts and dashed line segments in Home Assistant history graphs for sleeping environmental sensors.
- Monitors decoded telemetry and periodically refreshes the Home Assistant ESPHome proxy with the last valid environmental reading for sleeping sensors that have been quiet for more than 8 minutes.
- Implements a 40-minute stale cutoff and a 20 percent battery safety guard to prevent ghost data from expired or battery-depleted hardware.
- Fully controllable via a toggle switch under Settings with live persistence.

### High-Visibility Sensor Cards & MAC Identification
- Hardware MAC addresses are now rendered in a dedicated 11.5pt bold Consolas badge in crisp Cyan on dark navy pills.
- Dedicated Hardware MAC section added inside the sensor edit drawer with a one-click Copy MAC button.
- Prominent amber NEW badge displayed on unconfigured sensors to quickly identify newly discovered beacons in dense RF environments.
- Larger click targets for edit, delete, and rename controls.

### Reliability & Diagnostic Logging
- Piped runtime ingestion and connection logs to persistent rolling disk storage at `%AppData%\FoxDen\AirFoxBT\logs\airfoxbt.log` with 5MB auto-rotation.
- Added comprehensive unhandled exception handlers writing structured crash traces to `%AppData%\FoxDen\AirFoxBT\logs\crash.log`.
- Updated the local REST API initialization to verify Windows administrative privileges before binding wildcard HTTP prefixes, eliminating Windows HttpService Event 113 warnings.

### Phantom Card Guardrails & Decoder Filtering
- Resolved an issue where stray 2.4 GHz packets from nearby non-sensor IoT hardware (smart plugs, switches) could spawn empty dashboard cards with dashed telemetry.
- Hardened the MiBeacon decoder to require valid decoded telemetry objects or recognized sensor hardware models before accepting frames.
- Added a dashboard safeguard in the UI controller that suppresses unconfigured card creation for any frames lacking telemetry data, while preserving raw packet forwarding to Home Assistant.

### Single-Instance Process Enforcement & Bring-to-Front
- Added named Windows Mutex enforcement (`Local\FoxDen_AirFoxBT_SingleInstance_Mutex`) to prevent duplicate processes from launching when clicking Start Menu or desktop shortcuts.
- Second launch attempts automatically signal the primary instance via an event wait handle to restore from the system tray, un-minimize, and activate to the foreground before exiting cleanly with zero port collisions.

### Per-Device Smart Keepalive Controls
- Added a dedicated "Smart Keepalive Relay" toggle with descriptive tooltip directly inside each sensor card's edit drawer.
- Allows users to selectively enable or disable keepalive repeating on a per-sensor basis while retaining the global master switch under Settings.

---

## Checksums & Binary Verification

| File | SHA-256 Checksum |
| :--- | :--- |
| `AirFoxBT.exe` | `321e0c9a8b7026ab2f50e5a276dbb2c0ae21c22728653114dd432d9756f50f7c` |
| `install.ps1` | `926b9e37be16e35a37d2f332036ca9ca0e43b14fc86a517eecd3569ee54dc79c` |
| `uninstall.ps1` | `e653de36494572fa3745684cffcd928eb481d90a29c8fdaad2896de4ed0dfbe3` |
| `verify.ps1` | `760c62d5bd2c003743a61ea684b3bd14224a4a8ad89573ead9dca6541a69c986` |

Verify integrity before running (PowerShell):

```powershell
irm https://raw.githubusercontent.com/TalviFox/AirFoxBT/main/verify.ps1 | iex
```





