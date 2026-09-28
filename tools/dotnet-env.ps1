# Sets Windows env vars NuGet requires (process-level), then runs dotnet.
# Usage: .\tools\dotnet-env.ps1 restore|build|test|... [args]
$ErrorActionPreference = "Stop"

# PowerShell Env: provider mishandles names like ProgramFiles(x86) — use the .NET API.
[Environment]::SetEnvironmentVariable("ProgramFiles", "C:\Program Files", "Process")
[Environment]::SetEnvironmentVariable("ProgramFiles(x86)", "C:\Program Files (x86)", "Process")
[Environment]::SetEnvironmentVariable("ProgramW6432", "C:\Program Files", "Process")
if (-not [Environment]::GetEnvironmentVariable("NUGET_PACKAGES")) {
    [Environment]::SetEnvironmentVariable("NUGET_PACKAGES", (Join-Path $env:USERPROFILE ".nuget\packages"), "Process")
}
$env:MSBUILDDISABLENODEREUSE = "1"

# Prefer side-by-side .NET 8 (needed for net8.0 testhost when system only has .NET 10)
if (Test-Path "C:\dotnet8\dotnet.exe") {
    [Environment]::SetEnvironmentVariable("DOTNET_ROOT", "C:\dotnet8", "Process")
    $env:PATH = "C:\dotnet8;" + $env:PATH
    $dotnet = "C:\dotnet8\dotnet.exe"
} else {
    $dotnet = "dotnet"
}

& $dotnet @args
exit $LASTEXITCODE
