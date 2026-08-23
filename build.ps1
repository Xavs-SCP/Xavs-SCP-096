<#
.SYNOPSIS
    Builds Scp096ChaseMusic.dll against a local SCP:SL dedicated server install.

.DESCRIPTION
    The plugin is a plain class library, so it is compiled with Roslyn directly against the server's own
    assemblies. That keeps the build honest (the references are exactly what the server will load at runtime)
    and means no .NET SDK or NuGet restore is required.

.PARAMETER ServerPath
    Root of the dedicated server install, i.e. the folder containing SCPSL_Data.

.PARAMETER Install
    Also copy the plugin and the audio file into the LabAPI plugins folder.

.PARAMETER PluginDirectory
    Where -Install copies to. Defaults to the LabAPI 'global' plugin folder.
#>
[CmdletBinding()]
param(
    [string]$ServerPath = "$env:USERPROFILE\Downloads\sl-dedicated-server",
    [switch]$Install,
    [string]$PluginDirectory = "$env:APPDATA\SCP Secret Laboratory\LabAPI\plugins\global"
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$managed = Join-Path $ServerPath 'SCPSL_Data\Managed'

if (-not (Test-Path $managed)) {
    throw "Could not find server assemblies at '$managed'. Point -ServerPath at your dedicated server install."
}

$csc = Get-Item -ErrorAction SilentlyContinue @(
    'C:\Program Files\Microsoft Visual Studio\*\*\MSBuild\Current\Bin\Roslyn\csc.exe'
    'C:\Program Files (x86)\Microsoft Visual Studio\*\*\MSBuild\Current\Bin\Roslyn\csc.exe'
) | Select-Object -First 1 -ExpandProperty FullName

if (-not $csc) {
    throw 'Could not find the Roslyn compiler (csc.exe). Install Visual Studio or MSBuild.'
}

# Referencing the server's own mscorlib rather than the machine's keeps the output binary compatible with the
# Unity Mono runtime the server actually runs on.
$references = @(
    'mscorlib.dll'
    'System.dll'
    'System.Core.dll'
    'netstandard.dll'
    'Assembly-CSharp.dll'
    'Assembly-CSharp-firstpass.dll'
    'LabApi.dll'
    'Pooling.dll'
    'UnityEngine.dll'
    'UnityEngine.CoreModule.dll'
    'Mirror.dll'
) | ForEach-Object {
    $path = Join-Path $managed $_
    if (-not (Test-Path $path)) { throw "Missing reference assembly: $path" }
    "-r:`"$path`""
}

$sources = Get-ChildItem (Join-Path $root 'src') -Filter '*.cs' | ForEach-Object { "`"$($_.FullName)`"" }
if (-not $sources) { throw 'No sources found in src\.' }

$outputDirectory = Join-Path $root 'build'
New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null
$output = Join-Path $outputDirectory 'Scp096ChaseMusic.dll'

$arguments = @(
    '-nologo'
    '-nostdlib+'
    '-target:library'
    '-langversion:9.0'
    '-optimize+'
    '-warnaserror-'
    "-out:`"$output`""
) + $references + $sources

Write-Host "Compiling $($sources.Count) source files -> $output"
& $csc @arguments
if ($LASTEXITCODE -ne 0) { throw "Compilation failed with exit code $LASTEXITCODE." }

Write-Host "Built $output ($((Get-Item $output).Length) bytes)" -ForegroundColor Green

if ($Install) {
    $audio = Join-Path $root 'audio\blind_rage.wav'
    if (-not (Test-Path $audio)) { throw "Missing audio file: $audio. See README.md for how to regenerate it." }

    New-Item -ItemType Directory -Force -Path $PluginDirectory | Out-Null
    Copy-Item $output $PluginDirectory -Force
    Copy-Item $audio $PluginDirectory -Force

    $bass = Join-Path $root 'audio\blind_rage_bass.wav'
    if (Test-Path $bass) { Copy-Item $bass $PluginDirectory -Force }

    Write-Host "Installed to $PluginDirectory" -ForegroundColor Green
}

