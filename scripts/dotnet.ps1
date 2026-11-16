$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$env:DOTNET_CLI_HOME = Join-Path $projectRoot '.tools/cli'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:NUGET_PACKAGES = Join-Path $projectRoot '.tools/packages'
$candidates = @(
    $env:PERSONAL_LIBRARY_DOTNET,
    (Join-Path $projectRoot '.tools/dotnet/dotnet.exe'),
    (Join-Path (Split-Path -Parent $projectRoot) 'anki-stats/.tools/dotnet/dotnet.exe'),
    (Join-Path (Split-Path -Parent $projectRoot) '.old/anki-stats/.tools/dotnet/dotnet.exe'),
    'dotnet'
)
$script:LibraryDotnet = $null
foreach ($candidate in $candidates) {
    if (!$candidate) { continue }
    if (!(Get-Command $candidate -ErrorAction SilentlyContinue)) { continue }
    $sdkList = & $candidate --list-sdks 2>$null
    if ($sdkList -match '^10\.') { $script:LibraryDotnet = $candidate; break }
}
if (!$script:LibraryDotnet) { throw 'Install the .NET 10 SDK or set PERSONAL_LIBRARY_DOTNET to its dotnet.exe.' }
