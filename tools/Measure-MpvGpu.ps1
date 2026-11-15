<#
.SYNOPSIS
  Runs the headless MpvBench with option overrides and reports mpv's CPU plus GPU 3D / video-decode
  utilisation (per-process GPU engine counters). Used to compare renderer settings.
.EXAMPLE
  ./tools/Measure-MpvGpu.ps1 -Options 'hdr-compute-peak=no'
#>
[CmdletBinding()]
param(
    [string]$Clip = 'testmedia/hevc_2160p60_main10_hdr10.mkv',
    [string[]]$Options = @(),
    [int]$Seconds = 10
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$bench = Join-Path $root 'tools/MpvBench/bin/x64/Release/net10.0-windows/MpvBench.exe'
$clipPath = (Resolve-Path (Join-Path $root $Clip)).Path
$out = [IO.Path]::GetTempFileName()
$args = @("`"$clipPath`"", "$Seconds") + ($Options | ForEach-Object { "`"$_`"" })
$p = Start-Process $bench -ArgumentList $args -PassThru -RedirectStandardOutput $out -WindowStyle Hidden
Start-Sleep -Seconds 5
$data = Get-Counter -Counter "\GPU Engine(pid_$($p.Id)_*)\Utilization Percentage" -SampleInterval 1 -MaxSamples ([Math]::Max(3, $Seconds - 2)) -ErrorAction SilentlyContinue
$p.WaitForExit(60000) | Out-Null
$gpu = @{}
foreach ($set in $data) {
    $per = @{}
    foreach ($s in $set.CounterSamples) {
        if ($s.InstanceName -match 'engtype_(.+)$') { $k = $Matches[1]; $per[$k] = ($per[$k] ?? 0) + $s.CookedValue }
    }
    foreach ($k in $per.Keys) { $gpu[$k] = ($gpu[$k] ?? @()) + $per[$k] }
}
$first = (Get-Content $out | Select-Object -First 1)
Remove-Item $out
$g3d = [math]::Round((($gpu['3D'] ?? @(0)) | Measure-Object -Average).Average, 1)
$dec = [math]::Round((($gpu['VideoDecode'] ?? @(0)) | Measure-Object -Average).Average, 1)
"gpu3d={0,5}% decode={1,5}%  {2}" -f $g3d, $dec, $first
