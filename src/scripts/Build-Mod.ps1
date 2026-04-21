param(
    [string]$Configuration = "Debug"
)

$PSScriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Definition
$ProjectFile = Join-Path $PSScriptRoot "..\SnapAccess.csproj"

Write-Host "Building SnapAccess ($Configuration)..." -ForegroundColor Cyan

dotnet build $ProjectFile -c $Configuration

if ($LASTEXITCODE -ne 0) {
    Write-Host "Build FAILED!" -ForegroundColor Red
    exit 1
}

Write-Host "Build Successful!" -ForegroundColor Green
