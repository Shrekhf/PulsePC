param([string]$Destination = (Join-Path $env:LOCALAPPDATA 'Programs\PulsePC'), [switch]$Portable)
$ErrorActionPreference = 'Stop'
if (-not (Test-Path -LiteralPath (Join-Path $PSScriptRoot 'Pulse.exe'))) {
    throw 'Build the app first, then run the installer from the bin folder.'
}
$installPath = [IO.Path]::GetFullPath($Destination)
$appPath = Join-Path $installPath 'Pulse.exe'
$running = Get-Process Pulse -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $appPath }
foreach ($app in $running) {
    if (-not $app.CloseMainWindow() -or -not $app.WaitForExit(10000)) { throw 'Close the installed Pulse app, then run the installer again.' }
}
New-Item -ItemType Directory -Force -Path $installPath | Out-Null
if ([IO.Path]::GetFullPath($PSScriptRoot) -ne [IO.Path]::GetFullPath($installPath)) {
    foreach ($item in Get-ChildItem -LiteralPath $PSScriptRoot) {
        $destination = Join-Path $installPath $item.Name
        if ($item.Name -in @('Data','settings.ini') -and (Test-Path -LiteralPath $destination)) { continue }
        if ($item.PSIsContainer) { Copy-Item -LiteralPath $item.FullName -Destination $installPath -Recurse -Force }
        else { Copy-Item -LiteralPath $item.FullName -Destination $destination -Force }
    }
}
if (-not $Portable) {
$shell = New-Object -ComObject WScript.Shell
$shortcutPath = Join-Path ([Environment]::GetFolderPath('StartMenu')) 'Programs\Pulse PC.lnk'
$shortcut = $shell.CreateShortcut($shortcutPath)
$shortcut.TargetPath = $appPath
$shortcut.WorkingDirectory = $installPath
$shortcut.IconLocation = Join-Path $installPath 'Pulse.ico'
$shortcut.Save()
# Update an existing Pulse login entry only; installation does not opt users into autostart.
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
if ((Get-ItemProperty -LiteralPath $runKey -Name PulsePC -ErrorAction SilentlyContinue).PulsePC) {
    Set-ItemProperty -LiteralPath $runKey -Name PulsePC -Value ('"' + $appPath + '"')
}
}
Write-Host "Installed Pulse PC to $installPath. Launch it from the Start menu."
Write-Host 'Run this installer from a newer Pulse package to update without replacing saved settings or history.'
