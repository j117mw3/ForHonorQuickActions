param([switch]$LiveReport)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
$output = Join-Path $taskRoot 'test-output'
New-Item -ItemType Directory -Force -Path $output | Out-Null
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$fixture = Join-Path $output 'fhqa-test-fixture.exe'
& $compiler /nologo /target:winexe "/out:$fixture" (Join-Path $PSScriptRoot 'ProcessFixture.cs')
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
$sources = Get-ChildItem -LiteralPath (Join-Path $taskRoot 'ForHonorQuickActions') -Filter '*.cs' | ForEach-Object { $_.FullName }
$harness = Join-Path $output 'recovery-tests.exe'
& $compiler /nologo /target:exe /main:RecoveryTests /r:System.Management.dll /r:System.ServiceProcess.dll /r:System.Web.Extensions.dll /r:System.Net.Http.dll "/out:$harness" $sources (Join-Path $PSScriptRoot 'RecoveryTests.cs')
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
if ($LiveReport) { & $harness $fixture $output (Join-Path $output 'live-worker-smoke.json') }
else { & $harness $fixture $output }
exit $LASTEXITCODE
