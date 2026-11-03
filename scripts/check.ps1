. (Join-Path $PSScriptRoot 'dotnet.ps1')
Push-Location $projectRoot
try {
    & $LibraryDotnet run --project tests/PersonalLibrary.Checks -c Release -p:UseSharedCompilation=false
    if ($LASTEXITCODE -ne 0) { throw 'Checks failed.' }
} finally { Pop-Location }
