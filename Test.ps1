<#
.SYNOPSIS
Compiles and runs the MousePilot tests without changing application settings.
.DESCRIPTION
The default gesture suite is suitable for Windows CI and needs no desktop session.
Use -IncludeNativeHook from an interactive Windows desktop to also check native
hook installation and disposal. The smoke tests never synthesize input or launch
actions. All generated files are kept in the ignored .test-bin directory.
#>
[CmdletBinding()]
param(
    [switch]$IncludeNativeHook
)

$ErrorActionPreference = 'Stop'
if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) {
    throw 'MousePilot tests require Windows and .NET Framework 4 or later.'
}

$frameworkCompiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $frameworkCompiler)) {
    $frameworkCompiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
if (-not (Test-Path -LiteralPath $frameworkCompiler)) {
    throw 'The .NET Framework C# compiler was not found. Install .NET Framework 4 or later.'
}

$testOutput = Join-Path $PSScriptRoot '.test-bin'
$engineSource = Join-Path $PSScriptRoot 'src\GestureEngine.cs'
New-Item -ItemType Directory -Path $testOutput -Force | Out-Null

function Invoke-CompiledTest {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string]$Source,
        [string]$Platform = 'anycpu',
        [string[]]$References = @()
    )

    $testExecutable = Join-Path $testOutput ($Name + '.exe')
    $compilerArguments = @('/nologo', '/target:exe', ('/platform:' + $Platform), ('/out:' + $testExecutable))
    foreach ($reference in $References) {
        $compilerArguments += '/reference:' + $reference
    }
    $compilerArguments += @($engineSource, $Source)
    & $frameworkCompiler @compilerArguments
    if ($LASTEXITCODE -ne 0) { throw "Compilation failed for $Name (exit $LASTEXITCODE)." }

    & $testExecutable
    if ($LASTEXITCODE -ne 0) { throw "$Name failed (exit $LASTEXITCODE)." }
}

Invoke-CompiledTest -Name 'GestureEngineTests' -Source (Join-Path $PSScriptRoot 'tests\GestureEngineTests.cs')

if ($IncludeNativeHook) {
    $nativeTestSource = Join-Path $PSScriptRoot 'tests\NativeHookSmoke.cs'
    Invoke-CompiledTest -Name 'NativeHookSmoke-x86' -Source $nativeTestSource -Platform 'x86' -References @('System.Windows.Forms.dll')
    if ([Environment]::Is64BitOperatingSystem) {
        Invoke-CompiledTest -Name 'NativeHookSmoke-x64' -Source $nativeTestSource -Platform 'x64' -References @('System.Windows.Forms.dll')
    }
} else {
    Write-Host 'Native hook smoke skipped. Use -IncludeNativeHook from an interactive Windows desktop to run it.'
}
