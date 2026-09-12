# Optional sensor dependencies

The source repository does not redistribute sensor DLLs or driver installers.

For extra readings, obtain [LibreHardwareMonitor v0.9.6](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/releases/tag/v0.9.6). Place its runtime DLLs and supplied license/notices in a Sensors folder beside build.ps1; the build copies this folder next to the executable. LibreHardwareMonitor uses MPL-2.0. Preserve upstream license and third-party notices when distributing dependencies, and provide the corresponding upstream source link.

For low-level CPU/motherboard access, install [PawnIO 2.2.0](https://github.com/namazso/PawnIO.Setup/releases/tag/2.2.0). Review the upstream installer and license. Installation requires administrator approval. Pulse does not automatically install drivers. If you place PawnIO_setup.exe in Sensors, the explicit Sensor status menu action opens its installer; otherwise run the official installer yourself. Restart Pulse as administrator using Sensor status after installation.

No ASUS or NVIDIA binaries are redistributed. ASUS buttons open an existing Armoury Crate installation; NVIDIA readings use the installed nvidia-smi utility.
