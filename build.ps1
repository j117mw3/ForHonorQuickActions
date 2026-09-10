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

$logo = Join-Path $PSScriptRoot 'Logo.png'
$icon = Join-Path $PSScriptRoot 'ForHonorQuickActions\Logo.ico'
& (Join-Path $PSScriptRoot 'create-icon.ps1') -SourcePath $logo -DestinationPath $icon

$manifest = Join-Path $PSScriptRoot 'ForHonorQuickActions\app.manifest'
$output = Join-Path $releaseDirectory $OutputName
& $compiler /nologo /target:winexe /optimize+ "/win32manifest:$manifest" "/win32icon:$icon" "/out:$output" (Join-Path $PSScriptRoot 'ForHonorQuickActions\Program.cs') (Join-Path $PSScriptRoot 'ForHonorQuickActions\MainForm.cs') (Join-Path $PSScriptRoot 'ForHonorQuickActions\GameOverlay.cs') (Join-Path $PSScriptRoot 'ForHonorQuickActions\ProcessActions.cs') (Join-Path $PSScriptRoot 'ForHonorQuickActions\GameLocator.cs')
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "Built $output"
