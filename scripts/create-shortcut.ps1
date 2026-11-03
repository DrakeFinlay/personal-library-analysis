$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$shortcutShell = New-Object -ComObject WScript.Shell
foreach ($demoMode in @($false, $true)) {
    $label = if ($demoMode) { 'Personal Library - Demo' } else { 'Personal Library' }
    $shortcut = $shortcutShell.CreateShortcut((Join-Path $projectRoot "$label.lnk"))
    $shortcut.TargetPath = 'powershell.exe'
    $shortcut.Arguments = '-NoProfile -ExecutionPolicy Bypass -File "' + (Join-Path $PSScriptRoot 'launch.ps1') + '"' + $(if ($demoMode) { ' -Demo' } else { '' })
    $shortcut.WorkingDirectory = $projectRoot
    $shortcut.WindowStyle = 7
    $shortcut.Description = if ($demoMode) { 'Open the isolated synthetic library' } else { 'Build and open Personal Library' }
    $shortcut.Save()
}
