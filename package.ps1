param([string]$OutputDirectory = (Join-Path $PSScriptRoot 'dist'))
$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$stage = Join-Path ([IO.Path]::GetTempPath()) ('Pulse-package-' + [Guid]::NewGuid().ToString('N'))
$app = Join-Path $stage 'PulsePC'
New-Item -ItemType Directory -Force -Path $stage,$OutputDirectory | Out-Null
$download = Join-Path $stage 'LibreHardwareMonitor.zip'
Invoke-WebRequest 'https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/releases/download/v0.9.6/LibreHardwareMonitor.zip' -OutFile $download -UseBasicParsing
if ((Get-FileHash $download -Algorithm SHA256).Hash -ne '086D9F1B5A99E643EDC2CFAAAC16051685B551E4C5AC0B32A57C58C0E529C001') {
    throw 'Sensor dependency checksum mismatch.'
}
$upstream = Join-Path $stage 'upstream'
Expand-Archive -LiteralPath $download -DestinationPath $upstream
& "$PSScriptRoot\build.ps1" -OutputDirectory $app
$sensors = Join-Path $app 'Sensors'
if (Test-Path $sensors) { throw 'Package from a clean source checkout without a local Sensors directory.' }
New-Item -ItemType Directory -Path $sensors | Out-Null
Get-ChildItem -LiteralPath $upstream -Filter '*.dll' | Copy-Item -Destination $sensors
Invoke-WebRequest 'https://raw.githubusercontent.com/LibreHardwareMonitor/LibreHardwareMonitor/v0.9.6/LICENSE' -OutFile (Join-Path $sensors 'LICENSE.txt') -UseBasicParsing
Invoke-WebRequest 'https://raw.githubusercontent.com/LibreHardwareMonitor/LibreHardwareMonitor/v0.9.6/THIRD-PARTY-NOTICES.txt' -OutFile (Join-Path $sensors 'THIRD-PARTY-NOTICES.txt') -UseBasicParsing
$zip = Join-Path ([IO.Path]::GetFullPath($OutputDirectory)) 'PulsePC-1.4.1-windows-x64.zip'
if (Test-Path -LiteralPath $zip) { throw 'Output already exists. Choose a new output directory.' }
Compress-Archive -LiteralPath $app -DestinationPath $zip -CompressionLevel Optimal
$checksum = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + [IO.Path]::GetFileName($zip)
[IO.File]::WriteAllText((Join-Path ([IO.Path]::GetFullPath($OutputDirectory)) 'SHA256SUMS.txt'), $checksum + [Environment]::NewLine)
Write-Host "Packaged $zip"
