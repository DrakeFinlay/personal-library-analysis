param([switch]$Publish)
. (Join-Path $PSScriptRoot 'dotnet.ps1')
Push-Location $projectRoot
try {
    if ($Publish) {
        & $LibraryDotnet publish src/PersonalLibrary.App -c Release -r win-x64 --self-contained true -p:UseSharedCompilation=false -m:1 -o artifacts/PersonalLibrary
        if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
        Copy-Item THIRD-PARTY-NOTICES.md artifacts/PersonalLibrary -Force
        Copy-Item LICENSES artifacts/PersonalLibrary -Recurse -Force
    } else {
        & $LibraryDotnet build src/PersonalLibrary.App -c Release -p:UseSharedCompilation=false -m:1
        if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
    }
} finally { Pop-Location }
