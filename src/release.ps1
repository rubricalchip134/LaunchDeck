$ErrorActionPreference = 'Stop'
& "$PSScriptRoot\build.ps1"
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
$exe = Resolve-Path "$PSScriptRoot\..\LaunchDeck.exe"
$hash = (Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -LiteralPath "$PSScriptRoot\..\LaunchDeck.exe.sha256" -Value "$hash  LaunchDeck.exe" -Encoding ASCII
