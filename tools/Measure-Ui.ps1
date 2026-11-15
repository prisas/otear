<#
.SYNOPSIS
  Runs Otear's UI responsiveness benchmark (panels, menus, settings window, slider drag, UI-thread latency).
.EXAMPLE
  ./tools/Measure-Ui.ps1 -Exe artifacts/publish/win-x64/Otear.exe -Label after
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string]$Exe,
    [string]$Clip = 'testmedia/hevc_1080p.mkv',
    [string]$Label = 'run'
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$exePath = (Resolve-Path (Join-Path $root $Exe)).Path
$clipPath = (Resolve-Path (Join-Path $root $Clip)).Path
$tmp = [IO.Path]::GetTempFileName()
Get-Process Otear -ErrorAction SilentlyContinue | Stop-Process -Force
$env:OTEAR_BENCH_UI = $tmp
$env:OTEAR_BENCH_LABEL = $Label
$p = Start-Process $exePath -ArgumentList "`"$clipPath`"" -PassThru
Remove-Item Env:OTEAR_BENCH_UI, Env:OTEAR_BENCH_LABEL
if (-not $p.WaitForExit(120000)) { $p | Stop-Process -Force; throw 'UI benchmark timed out' }
$line = Get-Content $tmp -Raw
Remove-Item $tmp
if (-not $line) { throw 'no result (see %LOCALAPPDATA%\Otear\logs\otear.log)' }
$line | ConvertFrom-Json
