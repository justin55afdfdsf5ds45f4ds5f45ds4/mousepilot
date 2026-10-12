param([ValidatePattern('^[0-9]+\.[0-9]+\.[0-9]+(?:-[A-Za-z0-9.-]+)?$')][string]$Version = '1.0.0')
$ErrorActionPreference = 'Stop'
$artifactRoot = Join-Path $PSScriptRoot '.artifacts'
New-Item -ItemType Directory -Path $artifactRoot -Force | Out-Null
# A fresh staging directory prevents old files or local state leaking into releases.
$stage = Join-Path $artifactRoot ('stage-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $stage | Out-Null
& (Join-Path $PSScriptRoot 'Build.ps1') -OutputDirectory $stage
foreach ($name in @('MousePilot.exe.config', 'MousePilot.ico', 'Read me.txt', 'LICENSE', 'README.md')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination (Join-Path $stage $name)
}
New-Item -ItemType Directory -Path (Join-Path $stage 'web') | Out-Null
foreach ($name in @('index.html', 'style.css', 'app.js')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot ('web\' + $name)) -Destination (Join-Path $stage ('web\' + $name))
}
$zip = Join-Path $artifactRoot ('MousePilot-' + $Version + '-windows.zip')
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip -Force
$hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText((Join-Path $artifactRoot 'SHA256SUMS.txt'), $hash + '  ' + [IO.Path]::GetFileName($zip) + [Environment]::NewLine, (New-Object Text.UTF8Encoding $false))
Write-Output ('Packaged ' + $zip)
