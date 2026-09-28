# Publish portable ZIP for CleanSlate (no installer, as requested)
# Self-contained so users do not need a matching .NET runtime installed.
# Usage: pwsh tools/publish-portable.ps1 [-Configuration Release] [-Output dist] [-Runtime win-x64]
param(
    [string]$Configuration = "Release",
    [string]$Output = "dist",
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

$publishArgs = @(
    "-c", $Configuration,
    "-r", $Runtime,
    "--self-contained", "true",
    "-p:PublishSingleFile=false",
    "-p:IncludeNativeLibrariesForSelfExtract=true"
)

function Publish-One($project, $outDir) {
    if (Test-Path "tools\dotnet-env.ps1") {
        & powershell -NoProfile -ExecutionPolicy Bypass -File "tools\dotnet-env.ps1" publish $project -o $outDir @publishArgs
    } else {
        dotnet publish $project -o $outDir @publishArgs
    }
    if ($LASTEXITCODE -ne 0) { throw "publish failed: $project" }
}

Publish-One "src\CleanSlate.App\CleanSlate.App.csproj" "$Output\App"
Publish-One "src\CleanSlate.Cli\CleanSlate.Cli.csproj" "$Output\Cli"
Publish-One "src\CleanSlate.ElevatedHelper\CleanSlate.ElevatedHelper.csproj" "$Output\Helper"

# Bundle rules + docs
New-Item -ItemType Directory -Force -Path "$Output\rules", "$Output\docs" | Out-Null
Copy-Item docs\api\agent-rules.json "$Output\rules\" -Force -ErrorAction SilentlyContinue
Copy-Item README.md "$Output\docs\" -Force -ErrorAction SilentlyContinue
Copy-Item docs\PRIVACY.md "$Output\docs\" -Force -ErrorAction SilentlyContinue
Copy-Item docs\LICENSE.txt "$Output\docs\" -Force -ErrorAction SilentlyContinue

# Copy ONLY product host files next to the GUI — never overwrite shared WPF/runtime DLLs.
# (Copying entire Cli/Helper folders would replace WindowsBase/PresentationFramework and crash the UI.)
function Copy-HostOnly($srcDir) {
    if (-not (Test-Path $srcDir)) { return }
    $prefixes = @('CleanSlate.Cli', 'CleanSlate.ElevatedHelper')
    Get-ChildItem $srcDir -File | ForEach-Object {
        foreach ($p in $prefixes) {
            if ($_.Name.StartsWith($p, [System.StringComparison]::OrdinalIgnoreCase)) {
                Copy-Item $_.FullName -Destination "$Output\App\" -Force
            }
        }
    }
}
Copy-HostOnly "$Output\Cli"
Copy-HostOnly "$Output\Helper"

# Sanity: WPF core must remain large/valid
$wpf = Join-Path "$Output\App" "WindowsBase.dll"
if (Test-Path $wpf) {
    $len = (Get-Item $wpf).Length
    if ($len -lt 50KB) {
        throw "WindowsBase.dll looks corrupted ($len bytes) — publish output invalid."
    }
} else {
    throw "WindowsBase.dll missing from App output — WPF publish incomplete."
}

$stamp = Get-Date -Format "yyyyMMdd-HHmmss"
$zip = Join-Path $Output "CleanSlate-portable-$Runtime-$stamp.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path "$Output\App", "$Output\rules", "$Output\docs" -DestinationPath $zip
Write-Host "Portable package: $zip"
Write-Host "Main entry: $Output\App\CleanSlate.exe"
