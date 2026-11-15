<#
.SYNOPSIS
  Launches Otear repeatedly (optionally with a file), lets it close itself once started, and reports startup
  milestones (from the app log) and shutdown time (log "shutdown begin" → process exit).
.EXAMPLE
  ./tools/Measure-Startup.ps1 -Exe artifacts/publish/win-x64/Otear.exe -Runs 5
  ./tools/Measure-Startup.ps1 -Exe artifacts/publish/win-x64/Otear.exe -Clip testmedia/hevc_2160p60_main10_hdr10.mkv
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string]$Exe,
    [string]$Clip,
    [int]$Runs = 5,
    [int]$AutoCloseMs = 1500,
    [string]$Label = 'run',
    [switch]$PassThru
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$exePath = (Resolve-Path (Join-Path $root $Exe)).Path
$clipArg = if ($Clip) { "`"$((Resolve-Path (Join-Path $root $Clip)).Path)`"" } else { $null }
$log = Join-Path $env:LOCALAPPDATA 'Otear\logs\otear.log'
$culture = [Globalization.CultureInfo]::InvariantCulture

$rows = foreach ($i in 1..$Runs) {
    Get-Process Otear -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Milliseconds 800
    $before = if (Test-Path $log) { (Get-Content $log).Count } else { 0 }
    $env:OTEAR_AUTOCLOSE_MS = "$AutoCloseMs"
    $p = if ($clipArg) { Start-Process $exePath -ArgumentList $clipArg -PassThru } else { Start-Process $exePath -PassThru }
    Remove-Item Env:OTEAR_AUTOCLOSE_MS
    if (-not $p.WaitForExit(60000)) { $p | Stop-Process -Force; Write-Warning "run $i timed out"; continue }
    $exit = $p.ExitTime
    $lines = Get-Content $log | Select-Object -Skip $before

    $row = [ordered]@{ run = $i }
    $startup = $lines | Where-Object { $_ -match '\[INFO\] startup:' } | Select-Object -Last 1
    if ($startup) {
        foreach ($m in [regex]::Matches($startup, '(\w+)=(\d+)')) { $row[$m.Groups[1].Value] = [int]$m.Groups[2].Value }
    }
    $request = $lines | Where-Object { $_ -match 'close requested|shutdown begin' } | Select-Object -First 1
    $hidden = $lines | Where-Object { $_ -match 'window hidden' } | Select-Object -Last 1
    if ($request) {
        $t = [datetime]::ParseExact($request.Substring(0, 23), 'yyyy-MM-dd HH:mm:ss.fff', $culture)
        $row['shutdownTotal'] = [int]($exit - $t).TotalMilliseconds
        if ($hidden) { $row['windowHiddenAfter'] = [int]([datetime]::ParseExact($hidden.Substring(0, 23), 'yyyy-MM-dd HH:mm:ss.fff', $culture) - $t).TotalMilliseconds }
    }
    $shutdown = $lines | Where-Object { $_ -match '\[INFO\] shutdown:' } | Select-Object -Last 1
    if ($shutdown) {
        foreach ($m in [regex]::Matches($shutdown, '(\w+)=(\d+)')) { $row["sd_$($m.Groups[1].Value)"] = [int]$m.Groups[2].Value }
    }
    [pscustomobject]$row
}

if ($PassThru) { return $rows }
$rows | Format-Table -AutoSize | Out-String -Width 300
"median ($Label):"
$props = $rows[0].PSObject.Properties.Name | Where-Object { $_ -ne 'run' }
$median = [ordered]@{ label = $Label }
foreach ($prop in $props) {
    $vals = @($rows | ForEach-Object { $_.$prop } | Where-Object { $_ -ne $null } | Sort-Object)
    if ($vals.Count) { $median[$prop] = $vals[[int][math]::Floor($vals.Count / 2)] }
}
[pscustomobject]$median | Format-List
