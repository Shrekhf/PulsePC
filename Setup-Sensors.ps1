$ErrorActionPreference = 'Stop'
Write-Host 'This downloads the official PawnIO 2.2.0 installer for low-level sensor access.'
Write-Host 'Review its installer options and approve the Windows prompt to install.'
$folder = Join-Path ([IO.Path]::GetTempPath()) ('Pulse-sensor-setup-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $folder | Out-Null
$installer = Join-Path $folder 'PawnIO_setup.exe'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
Invoke-WebRequest 'https://github.com/namazso/PawnIO.Setup/releases/download/2.2.0/PawnIO_setup.exe' -OutFile $installer -UseBasicParsing
if ((Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash -ne '1F519A22E47187F70A1379A48CA604981C4FCF694F4E65B734AAA74A9FBA3032') {
    throw 'Installer checksum mismatch. The downloaded file will not be run.'
}
if ((Get-AuthenticodeSignature -LiteralPath $installer).Status -ne 'Valid') {
    throw 'The installer signature could not be verified. The downloaded file will not be run.'
}
$process = Start-Process -FilePath $installer -Verb RunAs -PassThru -Wait
if ($process.ExitCode -notin @(0,3010)) { throw "Sensor installer exited with code $($process.ExitCode)." }
Write-Host 'Restart Pulse as administrator from Thermals > Sensor status. Restart Windows if requested by the installer.'
