<#
.SYNOPSIS
    Builds Hands Are High, checks its version, packs the release zip, and optionally installs it.

.DESCRIPTION
    Run this through PowerShell, not Bash: Bash mangles 'C:\HUH' into 'C:HUH'.

    The zip is unpacked over the SPT root, so it carries the full path BepInEx/plugins/... .
    Entries are written by hand with forward slashes: PowerShell 5.1's Compress-Archive writes
    backslashes, which some extractors turn into file names instead of folders.

    BigBrain is a dependency the player installs separately; it is never packed.

.PARAMETER SPTPath
    The SPT install root to compile against (and install into, with -Install). It must have been
    started through the SPT Launcher once, so its Assembly-CSharp is the patched one.

.PARAMETER BigBrainDll
    DrakiaXYZ-BigBrain.dll to compile against, when the install doesn't have BigBrain.

.PARAMETER Install
    Also copy the built plugin into the install's BepInEx\plugins.

.EXAMPLE
    scripts\pack.ps1 -SPTPath C:\HUH
    scripts\pack.ps1 -SPTPath C:\HUH -BigBrainDll D:\BigBrain\DrakiaXYZ-BigBrain.dll -Install
#>
param(
    [Parameter(Mandatory = $true)][string] $SPTPath,
    [string] $BigBrainDll,
    [switch] $Install
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'src\HandsAreHigh\HandsAreHigh.csproj'
$source = Join-Path $root 'src\HandsAreHigh\HandsAreHighPlugin.cs'

if (-not (Test-Path (Join-Path $SPTPath 'BepInEx\core\BepInEx.dll'))) {
    throw "SPTPath '$SPTPath' is not an SPT install root: no BepInEx\core\BepInEx.dll."
}

# ---- version agreement -----------------------------------------------------------
$csprojVersion = ([xml](Get-Content $project)).Project.PropertyGroup.Version | Where-Object { $_ }
$sourceVersion = (Select-String -Path $source -Pattern 'PluginVersion\s*=\s*"([^"]+)"').Matches[0].Groups[1].Value
if ($csprojVersion -ne $sourceVersion) {
    throw "Version mismatch: csproj says '$csprojVersion', HandsAreHighPlugin.PluginVersion says '$sourceVersion'."
}
$version = $csprojVersion
Write-Host "Version $version" -ForegroundColor Cyan

# ---- config keys -----------------------------------------------------------------
# BepInEx throws on = \ " ' [ ] in a section or key name, and a throw in Awake kills the
# whole plugin without a line in LogOutput.log (0.0.2-0.1.0 shipped "You've left beyond").
# 0.16.0: every Settings*.cs (Settings.Realism.cs holds the newer sections), and section names
# written inline in a Bind call too.
$badChars = '[=\\"''\[\]\t]'
$bad = @()
foreach ($settings in Get-ChildItem (Join-Path $root 'src\HandsAreHigh') -Filter 'Settings*.cs') {
    $text = Get-Content $settings.FullName -Raw
    foreach ($m in [regex]::Matches($text, 'c\.Bind\(\s*(\w+|"(?:[^"\\]|\\.)*")\s*,\s*"((?:[^"\\]|\\.)*)"')) {
        if ($m.Groups[2].Value -match $badChars) { $bad += $m.Groups[2].Value }
        $sec = $m.Groups[1].Value
        if ($sec.StartsWith('"') -and $sec.Trim('"') -match $badChars) { $bad += $sec }
    }
    foreach ($m in [regex]::Matches($text, 'const string \w+ = "((?:[^"\\]|\\.)*)"')) {
        if ($m.Groups[1].Value -match $badChars) { $bad += $m.Groups[1].Value }
    }
}
if ($bad) { throw "Config names BepInEx rejects: $($bad -join ' | ')" }

# ---- build -----------------------------------------------------------------------
$buildArgs = @($project, '-c', 'Release', "-p:SPTPath=$SPTPath", '--nologo')
if ($BigBrainDll) { $buildArgs += "-p:BigBrainDll=$BigBrainDll" }
dotnet build @buildArgs
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
$built = Join-Path $root 'src\HandsAreHigh\bin\Release\HandsAreHigh.dll'

# ---- zip -------------------------------------------------------------------------
# The DLL only: Joel's releases never carry a README.
$entries = [ordered]@{
    'BepInEx/plugins/HandsAreHigh.dll' = $built
}

$dist = Join-Path $root 'dist'
New-Item -ItemType Directory -Force -Path $dist | Out-Null
$zip = Join-Path $dist "HandsAreHigh-$version.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }

$archive = [System.IO.Compression.ZipFile]::Open($zip, [System.IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($e in $entries.GetEnumerator()) {
        [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $e.Value, $e.Key, [System.IO.Compression.CompressionLevel]::Optimal) | Out-Null
    }
}
finally {
    $archive.Dispose()
}

$check = [System.IO.Compression.ZipFile]::OpenRead($zip)
try {
    $names = $check.Entries | ForEach-Object { $_.FullName }
}
finally {
    $check.Dispose()
}
$missing = $entries.Keys | Where-Object { $names -notcontains $_ }
if ($missing) { throw "Zip is missing: $($missing -join ', ')" }
Write-Host "Packed $zip" -ForegroundColor Green
$names | ForEach-Object { Write-Host "  $_" }

# ---- install ---------------------------------------------------------------------
if ($Install) {
    $dest = Join-Path $SPTPath 'BepInEx\plugins'
    Copy-Item $built $dest -Force
    Write-Host "Installed to $dest" -ForegroundColor Green
}
