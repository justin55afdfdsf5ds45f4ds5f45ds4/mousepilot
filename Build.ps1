param([string]$OutputDirectory = $PSScriptRoot)
$ErrorActionPreference = 'Stop'
if (!(Test-Path -LiteralPath $OutputDirectory)) { New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null }
$OutputDirectory = (Resolve-Path -LiteralPath $OutputDirectory).Path
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (!(Test-Path -LiteralPath $compiler)) { $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
$sources = Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src') -Filter '*.cs' | ForEach-Object { $_.FullName }
if (!(Test-Path -LiteralPath $compiler)) { throw 'The Windows .NET Framework compiler was not found.' }
& $compiler /nologo /target:winexe /optimize+ /platform:anycpu /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll "/win32icon:$PSScriptRoot\MousePilot.ico" "/out:$OutputDirectory\MousePilot.exe" $sources
if ($LASTEXITCODE -ne 0) { throw 'MousePilot build failed.' }
Write-Output 'Built MousePilot.exe'
