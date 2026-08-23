<#
.SYNOPSIS
    Runs the offline checks for the timeline, the WAV reader and the section contents.

.DESCRIPTION
    These sources have no dependency on the game, so they compile and run standalone. Everything that touches
    LabAPI or Unity (the session and the plugin entry point) needs a live server and is not covered here.
#>
[CmdletBinding()]
param(
    [string]$AudioFile,
    [string]$ServerPath = "$env:USERPROFILE\Downloads\sl-dedicated-server"
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent

if (-not $AudioFile) {
    $AudioFile = Join-Path $root 'audio\blind_rage.wav'
}

$csc = Get-Item -ErrorAction SilentlyContinue @(
    'C:\Program Files\Microsoft Visual Studio\*\*\MSBuild\Current\Bin\Roslyn\csc.exe'
    'C:\Program Files (x86)\Microsoft Visual Studio\*\*\MSBuild\Current\Bin\Roslyn\csc.exe'
) | Select-Object -First 1 -ExpandProperty FullName

if (-not $csc) { throw 'Could not find the Roslyn compiler (csc.exe). Install Visual Studio or MSBuild.' }

# Only the game-independent sources.
$sources = @(
    'src\ChaseTimeline.cs'
    'src\VolumeMath.cs'
    'src\WavAudioFile.cs'
    'src\ChaseMusicLibrary.cs'
    'src\Scp096ChaseMusicConfig.cs'
    'tests\TimelineTests.cs'
    'tests\ConfigYamlTests.cs'
) | ForEach-Object {
    $path = Join-Path $root $_
    if (-not (Test-Path $path)) { throw "Missing source: $path" }
    "`"$path`""
}

# The config round-trip check needs the same YAML library the server uses. Without it those checks are skipped
# rather than failing, so the rest of the suite still runs on a machine with no server install.
$extra = @()
$yamlDotNet = Join-Path $ServerPath 'SCPSL_Data\Managed\YamlDotNet.dll'
if (Test-Path $yamlDotNet) {
    $extra += "-r:`"$yamlDotNet`""
    $extra += '-define:YAML'
    $copyYamlDotNet = $yamlDotNet
}
else {
    Write-Warning "YamlDotNet.dll not found under '$ServerPath'; skipping the config round-trip checks."
}

$outputDirectory = Join-Path $root 'build'
New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null
$output = Join-Path $outputDirectory 'TimelineTests.exe'

if ($copyYamlDotNet) { Copy-Item $copyYamlDotNet $outputDirectory -Force }

& $csc -nologo -target:exe -langversion:9.0 "-out:$output" @extra @sources
if ($LASTEXITCODE -ne 0) { throw "Test build failed with exit code $LASTEXITCODE." }

& $output $AudioFile
exit $LASTEXITCODE

