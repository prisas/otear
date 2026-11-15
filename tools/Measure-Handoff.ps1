<#
.SYNOPSIS
  Measures the "double-click a file while Otear is open" path: a second Otear process hands the file to the
  running window (single instance). Reports the second process's lifetime and the time until the running
  window logs that the new file is playing.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string]$Exe,
    [int]$Runs = 5
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$exePath = (Resolve-Path (Join-Path $root $Exe)).Path
$first = (Resolve-Path (Join-Path $root 'testmedia/h264_720p_120s.mp4')).Path
$clips = @('testmedia/hevc_1080p.mkv', 'testmedia/hevc_2160p60_main10_hdr10.mkv') | ForEach-Object { (Resolve-Path (Join-Path $root $_)).Path }
$log = Join-Path $env:LOCALAPPDATA 'Otear\logs\otear.log'
$culture = [Globalization.CultureInfo]::InvariantCulture

Get-Process Otear -ErrorAction SilentlyContinue | Stop-Process -Force
$main = Start-Process $exePath -ArgumentList "`"$first`"" -PassThru
Start-Sleep -Seconds 3

$rows = foreach ($i in 1..$Runs) {
    $clip = $clips[$i % $clips.Count]
    $before = (Get-Content $log).Count
    $t0 = Get-Date
    $second = Start-Process $exePath -ArgumentList "`"$clip`"" -PassThru
    $second.WaitForExit(10000) | Out-Null
    $secondMs = ($second.ExitTime - $second.StartTime).TotalMilliseconds
    $deadline = (Get-Date).AddSeconds(10)
    $playing = $null
    Start-Sleep -Milliseconds 1500
    while (-not $playing -and (Get-Date) -lt $deadline -and (Select-String -Path $log -Pattern 'handoff received' -Quiet)) {
        Start-Sleep -Milliseconds 100
        $playing = Get-Content $log | Select-Object -Skip $before | Where-Object { $_ -match 'handoff playing' } | Select-Object -First 1
    }
    $recent = Join-Path $env:LOCALAPPDATA 'Otear\recent.json'
    $loadedMs = ((Get-Item $recent).LastWriteTime - $second.StartTime).TotalMilliseconds
    $playMs = if ($playing) { ([datetime]::ParseExact($playing.Substring(0, 23), 'yyyy-MM-dd HH:mm:ss.fff', $culture) - $second.StartTime).TotalMilliseconds } else { $null }
    [pscustomobject]@{ run = $i; file = Split-Path $clip -Leaf; secondProcessMs = [int]$secondMs; launchToLoadedMs = [int]$loadedMs; launchToPlayingMs = $(if ($playMs) { [int]$playMs } else { $null }) }
    Start-Sleep -Seconds 2
}
$null = $main.CloseMainWindow(); $main.WaitForExit(5000) | Out-Null
$rows | Format-Table -AutoSize
