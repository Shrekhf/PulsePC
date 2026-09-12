param([string]$OutputDirectory = (Join-Path $PSScriptRoot 'test-results'))
$ErrorActionPreference = 'Stop'
& "$PSScriptRoot\build.ps1"
$run = Join-Path ([IO.Path]::GetFullPath($OutputDirectory)) ([Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $run | Out-Null
$preview = Join-Path $run 'preview.png'
$process = Start-Process "$PSScriptRoot\bin\Pulse.exe" -ArgumentList @('--snapshot', ('"' + $preview + '"')) -WindowStyle Hidden -PassThru
if (-not $process.WaitForExit(180000)) {
    $process.Kill()
    throw 'Smoke test timed out after 180 seconds.'
}
$process.Refresh()
$errorFile = [IO.Path]::ChangeExtension($preview, '.error.txt')
if (Test-Path -LiteralPath $errorFile) { throw (Get-Content -LiteralPath $errorFile -Raw) }
if ($process.ExitCode -ne 0) { throw "Smoke test exited with $($process.ExitCode)." }
$report = [IO.Path]::ChangeExtension($preview, '.txt')
if (-not (Test-Path -LiteralPath $report) -or -not (Select-String -LiteralPath $report -SimpleMatch 'pause/resume passed.')) { throw 'Verification report missing or incomplete.' }
Write-Host 'PASS: dashboard rendering, telemetry, persistence, alerts, sorting, process protections and drive-health checks.'
