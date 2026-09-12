param([string]$OutputDirectory = (Join-Path $PSScriptRoot 'bin'))
$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw 'The 64-bit .NET Framework compiler is required.' }
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$sources = @(Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.cs' | Sort-Object Name | ForEach-Object FullName)
& $compiler /nologo /optimize+ /platform:x64 /target:winexe "/out:$OutputDirectory\Pulse.exe" "/win32icon:$PSScriptRoot\Pulse.ico" "/win32manifest:$PSScriptRoot\Pulse.manifest" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Management.dll /reference:System.Web.Extensions.dll $sources
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
foreach ($name in @('Pulse.ico','README.md','THIRD-PARTY.md','Install.ps1','Install Pulse.cmd')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination $OutputDirectory -Force
}
if (Test-Path -LiteralPath "$PSScriptRoot\Sensors") {
    Copy-Item -LiteralPath "$PSScriptRoot\Sensors" -Destination $OutputDirectory -Recurse -Force
}
Write-Host "Built $OutputDirectory\Pulse.exe"
