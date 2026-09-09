$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $compiler /nologo /target:winexe /optimize+ /out:"$PSScriptRoot\..\LaunchDeck.exe" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Xml.Linq.dll /reference:System.Web.Extensions.dll "$PSScriptRoot\LaunchDeck.cs"
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
