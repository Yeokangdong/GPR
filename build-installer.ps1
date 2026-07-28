param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [switch]$FrameworkDependent
)

$ErrorActionPreference = "Stop"
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

$repoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$appProject = Join-Path $repoRoot "GprPrediction.Wpf\\GprPrediction.Wpf.csproj"
$installerProject = Join-Path $repoRoot "installer\\GprPrediction.Setup.wixproj"
$artifactsRoot = Join-Path $repoRoot "artifacts"
$publishDir = Join-Path (Join-Path $artifactsRoot "publish") $Runtime
$installerOutDir = Join-Path (Join-Path $artifactsRoot "installer") $Configuration

if (Test-Path -LiteralPath $publishDir) {
    Remove-Item -LiteralPath $publishDir -Recurse -Force
}

if (Test-Path -LiteralPath $installerOutDir) {
    Remove-Item -LiteralPath $installerOutDir -Recurse -Force
}

New-Item -ItemType Directory -Force -Path $publishDir | Out-Null
New-Item -ItemType Directory -Force -Path $installerOutDir | Out-Null

$selfContained = if ($FrameworkDependent) { "false" } else { "true" }

Write-Host "[1/2] Publishing application..." -ForegroundColor Cyan
dotnet publish $appProject `
    -c $Configuration `
    -r $Runtime `
    --self-contained $selfContained `
    -p:PublishSingleFile=false `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -o $publishDir

Write-Host "[2/2] Building MSI..." -ForegroundColor Cyan
$publishDirWithSlash = $publishDir.TrimEnd('\') + '\'
dotnet build $installerProject `
    -c $Configuration `
    -p:PublishDir=$publishDirWithSlash `
    -p:OutputPath=$installerOutDir

Write-Host ""
Write-Host "Done:" -ForegroundColor Green
Write-Host "  Publish: $publishDir"
Write-Host "  Installer: $installerOutDir"
