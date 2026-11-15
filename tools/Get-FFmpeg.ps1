<#
.SYNOPSIS
  Development-only: downloads the gyan.dev FFmpeg "release essentials" build (includes libx264/libx265)
  into .tools/ffmpeg, verifying the published SHA-256. Used by New-TestMedia.ps1 to make test clips.
#>
[CmdletBinding()]
param([switch]$Force)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$root = Split-Path -Parent $PSScriptRoot
$dest = Join-Path $root '.tools/ffmpeg'
$exe = Join-Path $dest 'ffmpeg.exe'
if (-not $Force -and (Test-Path $exe)) { Write-Host "ffmpeg already present: $exe"; return }

$url = 'https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip'
$dlDir = Join-Path $root '.tools/downloads/ffmpeg'
if (Test-Path $dlDir) { Remove-Item -Recurse -Force $dlDir }
New-Item -ItemType Directory -Force $dlDir | Out-Null
$zip = Join-Path $dlDir 'ffmpeg-release-essentials.zip'

Write-Host "Downloading $url"
Invoke-WebRequest -Uri $url -OutFile $zip -UseBasicParsing
$expected = (Invoke-WebRequest -Uri "$url.sha256" -UseBasicParsing).Content
if ($expected -is [byte[]]) { $expected = [Text.Encoding]::ASCII.GetString($expected) }
$expected = ($expected -split '\s+')[0].Trim().ToLowerInvariant()
$actual = (Get-FileHash -Algorithm SHA256 $zip).Hash.ToLowerInvariant()
if ($expected -ne $actual) { throw "SHA-256 mismatch: expected $expected, got $actual" }
Write-Host "SHA-256 OK ($actual)"

$extract = Join-Path $dlDir 'extract'
Expand-Archive -Path $zip -DestinationPath $extract -Force
$bin = Get-ChildItem -Path $extract -Recurse -Filter 'ffmpeg.exe' | Select-Object -First 1
if (-not $bin) { throw 'ffmpeg.exe not found in archive' }

if (Test-Path $dest) { Remove-Item -Recurse -Force $dest }
New-Item -ItemType Directory -Force $dest | Out-Null
Copy-Item (Join-Path $bin.DirectoryName '*') $dest
Write-Host "ffmpeg installed to $dest"
& $exe -hide_banner -version | Select-Object -First 1
