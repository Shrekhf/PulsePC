# Pulse PC

A Windows desktop dashboard for hardware, temperatures, drive health, performance history, and process management.

## Download and install

Get `PulsePC-1.4.0-windows-x64.zip` from [Releases](https://github.com/Shrekhf/PulsePC/releases/latest), extract the entire ZIP, and run `Pulse.exe`. No compiling is needed. Run `Install Pulse.cmd` for a per-user Start menu installation.

Sensor libraries are included in the ready-to-run ZIP. For low-level CPU/motherboard readings, run `Set up sensors.cmd` once to download the verified official PawnIO installer, then restart Pulse as administrator. This step needs internet access and Windows approval. Hardware without a supported sensor still shows unavailable values. See `START-HERE.txt` in the ZIP.

## Build and run

Windows 10/11 x64 with .NET Framework 4.8 is the supported target. No NuGet packages or separate SDK are needed for the basic build.

```powershell
.\build.ps1
.\bin\Pulse.exe
```

Basic monitoring runs with normal user permissions. Optional CPU and motherboard sensors require LibreHardwareMonitor, PawnIO, and an administrator restart. Missing sensors remain unavailable; hardware support varies. See [sensor setup](THIRD-PARTY.md).

## Features

- Overview, hardware inventory, thermals, system controls, storage, apps, and settings.
- CPU and memory load, network throughput, disk activity, and NVIDIA telemetry when available.
- Physical-drive health, temperature, and wear indicators when reported by Windows or the optional sensor library.
- Grouped process view with search, sorting, details, normal close and confirmed kill actions. Process identity and critical-process checks protect against targeting a reused PID.
- Local history, CSV and text exports, configurable threshold notifications, compact widget, and optional launch at login.
- Existing Windows power plans and shortcuts to Windows settings. ASUS shortcuts require Armoury Crate. Direct fan, RGB, voltage and clock control are not implemented.

## Install and update

Build first, then run `bin\Install Pulse.cmd` for a per-user Start menu installation. Run the installer from a new build to update; existing destination settings and history are preserved. The app is unsigned and there is no automatic updater.

## Privacy

Readings and preferences stay in `Data/` and `settings.ini` beside the executable. No telemetry service is included. Reports, screenshots, CSV exports and history can reveal hardware and process details; review them before sharing. The process menu's **Search online** opens Bing with the selected process name only when clicked.

Keep portable installations in a writable folder. Each copy has separate data; run only one instance per folder. Normal closing saves history. Abrupt termination can lose recent samples.

## Verification

```powershell
.\test.ps1
```

The smoke test launches a separate instance, captures seven views, and exercises telemetry, alerts, preferences, history export, sorting, process protections, and drive-health logic. Results go to ignored `test-results/`. Driver installation, process termination, power-plan changes and Windows notification delivery require separate manual verification. The included Windows CI workflow runs the same test.

## Performance and limitations

Maintainers can run `.\package.ps1` to create a clean release ZIP and checksum file in `dist/`. The script downloads the pinned sensor release and includes its license notices. Run from a source checkout without a local `Sensors/` folder.

Sampling runs off the UI thread with no overlapping reads. Missing external WMI providers retry once per minute; reflection metadata is cached for sensor polling. Builds enable compiler optimization. Slow hardware providers can still delay samples.

History retains 1,800 samples. Missing readings are not a health guarantee, and wear percentages are not a failure prediction. NVIDIA telemetry uses the first GPU. Network rates include active virtual adapters. Memory working sets can share pages. Capacities labelled GB use GiB for memory and volumes.

## Contributing and license

See [CONTRIBUTING.md](CONTRIBUTING.md). Pulse PC is licensed under the [MIT License](LICENSE). Third-party components retain their own licenses; see [THIRD-PARTY.md](THIRD-PARTY.md).
