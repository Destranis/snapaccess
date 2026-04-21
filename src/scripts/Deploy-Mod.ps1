param(
    [string]$Configuration = "Debug"
)

$PSScriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Definition
$BuildScript = Join-Path $PSScriptRoot "Build-Mod.ps1"
$GamePath = "C:\Program Files (x86)\Steam\steamapps\common\MARVEL SNAP"
$ModsFolder = Join-Path $GamePath "Mods"
$DllName = "SnapAccess.dll"
$SourcePath = Join-Path $PSScriptRoot "..\bin\$Configuration\net6.0\$DllName"

# Run Build
& $BuildScript -Configuration $Configuration

if ($LASTEXITCODE -ne 0) {
    Write-Host "Deployment cancelled due to build failure." -ForegroundColor Red
    exit 1
}

# Ensure Mods folder exists
if (-not (Test-Path $ModsFolder)) {
    Write-Host "Creating Mods folder at $ModsFolder..." -ForegroundColor Cyan
    New-Item -ItemType Directory -Path $ModsFolder | Out-Null
}

# Copy DLL
Write-Host "Deploying $DllName to $ModsFolder..." -ForegroundColor Cyan
Copy-Item $SourcePath $ModsFolder -Force

if ($LASTEXITCODE -eq 0) {
    Write-Host "Deployment Successful!" -ForegroundColor Green
} else {
    Write-Host "Deployment FAILED!" -ForegroundColor Red
}
