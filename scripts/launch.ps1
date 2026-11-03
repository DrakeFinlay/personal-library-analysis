param([switch]$Demo, [switch]$BuildOnly)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$log = Join-Path $projectRoot '.tools/launch.log'
New-Item -ItemType Directory -Path (Split-Path -Parent $log) -Force | Out-Null
try {
    & (Join-Path $PSScriptRoot 'build.ps1') -Publish *> $log
    if ($BuildOnly) { Write-Output 'Build ready.'; return }
    $info = New-Object System.Diagnostics.ProcessStartInfo
    $info.FileName = Join-Path $projectRoot 'artifacts/PersonalLibrary/PersonalLibrary.exe'
    $info.WorkingDirectory = $projectRoot
    $info.UseShellExecute = $false
    $info.EnvironmentVariables.Remove('PERSONAL_LIBRARY_DATA_DIR')
    if ($Demo) {
        $info.EnvironmentVariables['PERSONAL_LIBRARY_DATA_DIR'] = Join-Path $projectRoot '.local/demo'
        $info.Arguments = '--demo'
    }
    [System.Diagnostics.Process]::Start($info) | Out-Null
} catch {
    if ($BuildOnly) { throw }
    Add-Type -AssemblyName PresentationFramework
    [System.Windows.MessageBox]::Show("Personal Library could not build or start. Close other copies and check the build log.`n`n$($_.Exception.Message)`n`n$log", 'Personal Library') | Out-Null
    exit 1
}
