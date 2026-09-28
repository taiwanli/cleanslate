# Build CleanSlate installer (Inno Setup)
# Prerequisites: Inno Setup 6 (ISCC.exe). Publish output at dist\App (self-contained).
# Usage: pwsh tools/build-installer.ps1 [-Version 0.1.0] [-SkipPublish]
param(
    [string]$Version = "0.1.0",
    [switch]$SkipPublish
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

$isscc = @(
    "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
    "C:\Program Files\Inno Setup 6\ISCC.exe",
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1

if (-not $isscc) {
    throw "ISCC.exe not found. Install Inno Setup 6 first."
}

if (-not $SkipPublish) {
    Write-Host "Publishing self-contained app..."
    & powershell -NoProfile -ExecutionPolicy Bypass -File "tools\publish-portable.ps1" -Configuration Release -Output dist -Runtime win-x64
}

if (-not (Test-Path "dist\App\CleanSlate.exe")) {
    throw "dist\App\CleanSlate.exe missing — publish first."
}

New-Item -ItemType Directory -Force -Path "dist\installer" | Out-Null
Write-Host "Compiling installer with $isscc ..."
& $isscc "/DMyAppVersion=$Version" "installer\CleanSlate.iss"
if ($LASTEXITCODE -ne 0) { throw "ISCC failed with exit $LASTEXITCODE" }

Get-ChildItem "dist\installer\*.exe" | ForEach-Object {
    Write-Host "Installer: $($_.FullName) ($([math]::Round($_.Length/1MB,1)) MB)"
}
