<#
.SYNOPSIS
  Plays a clip in Otear's benchmark mode and reports playback cost: app CPU, UI-thread work, mpv frame
  drops/delays, and GPU engine utilisation of Otear and of the Windows compositor (dwm), sampled per second.
.EXAMPLE
  ./tools/Measure-Playback.ps1 -Exe src/Otear/bin/x64/Release/net10.0-windows10.0.26100.0/win-x64/Otear.exe -Label baseline
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string]$Exe,
    [string]$Clip = 'testmedia/hevc_2160p60_main10_hdr10.mkv',
    [int]$Seconds = 20,
    [string]$Label = 'run',
    [ValidateSet('auto', 'visible')] [string]$Controls = 'auto',
    [switch]$Fullscreen,
    [string]$Out = 'artifacts/bench/results.jsonl'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
    $exePath = (Resolve-Path $Exe).Path
    $clipPath = (Resolve-Path $Clip).Path
    New-Item -ItemType Directory -Force (Split-Path $Out) | Out-Null
    $outPath = Join-Path $root $Out
    $tmp = [IO.Path]::GetTempFileName()

    Get-Process Otear -ErrorAction SilentlyContinue | Stop-Process -Force
    $env:OTEAR_BENCH_SECONDS = "$Seconds"
    $env:OTEAR_BENCH_OUT = $tmp
    $env:OTEAR_BENCH_LABEL = $Label
    $env:OTEAR_BENCH_CONTROLS = $Controls
    $env:OTEAR_BENCH_FULLSCREEN = $(if ($Fullscreen) { '1' } else { '0' })
    $p = Start-Process -FilePath $exePath -ArgumentList "`"$clipPath`"" -PassThru
    Remove-Item Env:OTEAR_BENCH_SECONDS, Env:OTEAR_BENCH_OUT, Env:OTEAR_BENCH_LABEL, Env:OTEAR_BENCH_CONTROLS, Env:OTEAR_BENCH_FULLSCREEN

    # The app warms up ~4 s after the file loads; sample inside the measured window.
    Start-Sleep -Seconds 6
    $dwm = (Get-Process dwm -ErrorAction SilentlyContinue | Select-Object -First 1).Id
    $samples = [Math]::Max(3, $Seconds - 4)
    $counters = @("\GPU Engine(pid_$($p.Id)_*)\Utilization Percentage")
    if ($dwm) { $counters += "\GPU Engine(pid_$($dwm)_*)\Utilization Percentage" }
    if (-not ([System.Management.Automation.PSTypeName]'OtearThreadTimes').Type) { Add-Type -Path (Join-Path $PSScriptRoot 'ThreadTimes.cs') }
    $t0 = [OtearThreadTimes]::Take($p.Id); $tw = [Diagnostics.Stopwatch]::StartNew()
    $data = Get-Counter -Counter $counters -SampleInterval 1 -MaxSamples $samples -ErrorAction SilentlyContinue
    $t1 = [OtearThreadTimes]::Take($p.Id); $wall = $tw.Elapsed.TotalSeconds
    $threads = [OtearThreadTimes]::Diff($t0, $t1).GetEnumerator() | Sort-Object Value -Descending | Select-Object -First 10 |
        ForEach-Object { [pscustomobject]@{ Thread = $_.Key; CpuPercent = [math]::Round($_.Value / $wall / 10, 1) } }

    $gpu = @{}
    foreach ($set in $data) {
        $perSample = @{}
        foreach ($s in $set.CounterSamples) {
            if ($s.InstanceName -notmatch '^pid_(\d+)_.*engtype_(.+)$') { continue }
            $who = if ([int]$Matches[1] -eq $p.Id) { 'otear' } else { 'dwm' }
            $key = "gpu_${who}_$($Matches[2] -replace '[^A-Za-z0-9]', '')"
            $perSample[$key] = ($perSample[$key] ?? 0) + $s.CookedValue
        }
        foreach ($k in $perSample.Keys) { $gpu[$k] = ($gpu[$k] ?? @()) + $perSample[$k] }
    }

    if (-not $p.WaitForExit(($Seconds + 30) * 1000)) { $p | Stop-Process -Force; throw 'benchmark did not finish' }
    $line = Get-Content $tmp -Raw
    Remove-Item $tmp
    if (-not $line) { throw 'no benchmark result (see %LOCALAPPDATA%\Otear\logs\otear.log)' }
    $result = $line | ConvertFrom-Json
    foreach ($k in ($gpu.Keys | Sort-Object)) {
        $avg = ($gpu[$k] | Measure-Object -Average).Average
        if ($avg -ge 0.05 -or $k -match '3D|VideoDecode') { $result | Add-Member -NotePropertyName $k -NotePropertyValue ([math]::Round($avg, 2)) }
    }
    $result | Add-Member -NotePropertyName threads -NotePropertyValue (($threads | ForEach-Object { "$($_.Thread)=$($_.CpuPercent)" }) -join '; ')
    ($result | ConvertTo-Json -Compress) | Add-Content $outPath
    $result
}
finally {
    Pop-Location
}
