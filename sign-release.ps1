param(
    [string]$FileName = 'ForHonorQuickActions.exe'
)

$ErrorActionPreference = 'Stop'
$certificate = Get-ChildItem 'Cert:\CurrentUser\My\D3ADAA1B142B51EECA9349675C70902D4CF2A43B' -ErrorAction SilentlyContinue
if ($null -eq $certificate) {
    throw 'The local For Honor Quick Actions signing certificate was not found.'
}

$file = Join-Path $PSScriptRoot (Join-Path 'release' $FileName)
if (-not (Test-Path -LiteralPath $file)) {
    throw "Release file not found: $file"
}

$signTool = 'C:\Program Files (x86)\Windows Kits\10\bin\10.0.19041.0\x64\signtool.exe'
& $signTool sign /fd SHA256 /sha1 $certificate.Thumbprint /d 'For Honor Quick Actions' $file
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
