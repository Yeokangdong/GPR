param(
    [Parameter(Mandatory = $true)]
    [string] $SourcePythonDirectory,

    [string] $ProjectDirectory = (Resolve-Path "$PSScriptRoot\..").Path
)

$ErrorActionPreference = 'Stop'

$source = Resolve-Path -LiteralPath $SourcePythonDirectory
$target = Join-Path $ProjectDirectory 'GprPrediction.Wpf\runtime\python'

if (Test-Path -LiteralPath $target) {
    Remove-Item -LiteralPath $target -Recurse -Force
}

New-Item -ItemType Directory -Force -Path $target | Out-Null
Copy-Item -LiteralPath (Join-Path $source '*') -Destination $target -Recurse -Force

Write-Host "Python runtime copied to: $target"
Write-Host "Install algorithm packages into this runtime before release:"
Write-Host "  .\GprPrediction.Wpf\runtime\python\python.exe -m pip install -r <algorithm>\requirements.txt"
