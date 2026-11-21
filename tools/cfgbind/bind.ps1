# Loads a COPY of a Hands Are High cfg through a built HandsAreHigh.dll (Settings.Bind, as the plugin does
# in Awake), twice, and reports keys added / values changed / dropped / changed on the second load.
# Never point -Cfg at a file you want untouched: it is copied into -Work first.
# Usage (PowerShell 5.1): powershell -NoProfile -ExecutionPolicy Bypass -File tools\cfgbind\bind.ps1 `
#   -Dll src\HandsAreHigh\bin\Release\HandsAreHigh.dll -Spt H:\SPT4.1.X `
#   -Cfg H:\SPT4.1.X\BepInEx\config\com.joelhauser.handsarehigh.cfg -Work <scratchpad>\cfgbind
param([string]$Dll, [string]$Spt, [string]$Cfg, [string]$Work)
$ErrorActionPreference = 'Stop'
$managed = Join-Path $Spt 'EscapeFromTarkov_Data\Managed'
$bep = Join-Path $Spt 'BepInEx\core'
[AppDomain]::CurrentDomain.add_AssemblyResolve({
    param($s, $e)
    $n = (New-Object System.Reflection.AssemblyName($e.Name)).Name
    foreach ($d in @($bep, $managed, (Join-Path $Spt 'BepInEx\plugins'), (Split-Path $Dll))) {
        $p = Join-Path $d "$n.dll"
        if (Test-Path $p) { return [System.Reflection.Assembly]::LoadFrom($p) }
    }
    return $null
})
[void][System.Reflection.Assembly]::LoadFrom((Join-Path $bep 'BepInEx.dll'))
$asm = [System.Reflection.Assembly]::LoadFrom($Dll)

function Read-Entries([string]$path) {
    $map = @{}; $sec = ''
    foreach ($l in Get-Content $path) {
        if ($l -match '^\[(.+)\]$') { $sec = $Matches[1]; continue }
        if ($l -match '^([^#=][^=]*?) = (.*)$') { $map["$sec|$($Matches[1])"] = $Matches[2] }
    }
    return $map
}

function Invoke-Bind([string]$path) {
    $cfg = New-Object BepInEx.Configuration.ConfigFile($path, $true)
    $plugin = $asm.GetType('HandsAreHigh.HandsAreHighPlugin')
    $log = New-Object BepInEx.Logging.ManualLogSource('test')
    $plugin.GetField('Log', [System.Reflection.BindingFlags]'NonPublic,Static').SetValue($null, $log)
    $settings = $asm.GetType('HandsAreHigh.Settings')
    $argv = New-Object 'object[]' 1; $argv[0] = $cfg.PSObject.BaseObject
    [void]$settings.GetMethod('Bind', [System.Reflection.BindingFlags]'NonPublic,Static').Invoke($null, $argv)
    $cfg.Save()
}

New-Item -ItemType Directory -Force $Work | Out-Null
$a = Join-Path $Work 'joel.cfg'
Copy-Item $Cfg $a -Force
$before = Read-Entries $a
Invoke-Bind $a
$after1 = Read-Entries $a
Invoke-Bind $a
$after2 = Read-Entries $a
"entries before {0}, after {1}, after a second load {2}" -f $before.Count, $after1.Count, $after2.Count
"--- added ---"
$after1.Keys | Where-Object { -not $before.ContainsKey($_) } | Sort-Object | ForEach-Object { "  $_ = $($after1[$_])" }
"--- existing values changed ---"
$changed = @($before.Keys | Where-Object { $after1.ContainsKey($_) -and $after1[$_] -ne $before[$_] })
if ($changed.Count -eq 0) { "  none" } else { $changed | ForEach-Object { "  $_ : $($before[$_]) -> $($after1[$_])" } }
"--- dropped ---"
$gone = @($before.Keys | Where-Object { -not $after1.ContainsKey($_) })
if ($gone.Count -eq 0) { "  none" } else { $gone }
"--- second load changed ---"
$c2 = @($after1.Keys | Where-Object { $after2[$_] -ne $after1[$_] })
if ($c2.Count -eq 0) { "  none" } else { $c2 }
