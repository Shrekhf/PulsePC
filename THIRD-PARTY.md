# Optional sensor dependencies

The source repository does not redistribute sensor DLLs or driver installers.

The ready-to-run 1.4.0 ZIP bundles unmodified runtime DLLs from the .NET Framework LibreHardwareMonitor v0.9.6 release. Its MPL-2.0 license and third-party notices are included under `Sensors/`. Corresponding source is available at the release tag linked below. Pulse's MIT license does not replace these upstream licenses.

PawnIO is downloaded directly from its official release by `Setup-Sensors.ps1`, only when the user runs sensor setup. The script checks the pinned SHA-256 and Authenticode signature before opening the interactive installer. The PawnIO installer is not redistributed inside Pulse's ZIP.

For extra readings, obtain [LibreHardwareMonitor v0.9.6](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/releases/tag/v0.9.6). Place its runtime DLLs and supplied license/notices in a Sensors folder beside build.ps1; the build copies this folder next to the executable. LibreHardwareMonitor uses MPL-2.0. Preserve upstream license and third-party notices when distributing dependencies, and provide the corresponding upstream source link.

For low-level CPU/motherboard access, install [PawnIO 2.2.0](https://github.com/namazso/PawnIO.Setup/releases/tag/2.2.0). Review the upstream installer and license. Installation requires administrator approval. Pulse does not automatically install drivers at launch. The Sensor status menu opens the setup helper; you can also use the official installer yourself. Restart Pulse as administrator using Sensor status after installation.

No ASUS or NVIDIA binaries are redistributed. ASUS buttons open an existing Armoury Crate installation; NVIDIA readings use the installed nvidia-smi utility.
