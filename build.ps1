param(
    [string]$OutputName = 'ForHonorQuickActions.exe'
)

$ErrorActionPreference = 'Stop'
if ([IO.Path]::GetFileName($OutputName) -ne $OutputName) {
    throw 'OutputName must be a file name, not a path.'
}

$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) {
    throw 'The Windows .NET Framework compiler was not found.'
}

$releaseDirectory = Join-Path $PSScriptRoot 'release'
New-Item -ItemType Directory -Force -Path $releaseDirectory | Out-Null

$icon = Join-Path $PSScriptRoot 'ForHonorQuickActions\Logo.ico'

$manifest = Join-Path $PSScriptRoot 'ForHonorQuickActions\app.manifest'
$output = Join-Path $releaseDirectory $OutputName
$sources = Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'ForHonorQuickActions') -Filter '*.cs' | ForEach-Object { $_.FullName }
& $compiler /nologo /target:winexe /optimize+ /r:System.Management.dll /r:System.ServiceProcess.dll /r:System.Web.Extensions.dll /r:System.Net.Http.dll "/win32manifest:$manifest" "/win32icon:$icon" "/out:$output" $sources
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "Built $output"
