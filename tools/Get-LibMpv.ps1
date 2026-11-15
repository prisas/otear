<#
.SYNOPSIS
  Downloads the official libmpv Windows build (GPL) from the mpv-player/mpv GitHub release,
  verifies its SHA-256 and extracts libmpv-2.dll, headers and licenses into lib/mpv/<rid>.
.EXAMPLE
  ./tools/Get-LibMpv.ps1                 # win-x64
  ./tools/Get-LibMpv.ps1 -Rid win-arm64
#>
[CmdletBinding()]
param(
    [ValidateSet('win-x64', 'win-arm64')]
    [string]$Rid = 'win-x64',
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$version = '0.42.0'
$builds = @{
    'win-x64'   = @{ Arch = 'x86_64';  Sha256 = 'b99e0d183024ae479d1b36a93fa262c0297dfb025b99715b98da377ffb6c2913' }
    'win-arm64' = @{ Arch = 'aarch64'; Sha256 = '__PIN_ARM64__' }
}
$build = $builds[$Rid]
$zipName = "libmpv-v$version-$($build.Arch)-w64-mingw32-gpl.zip"
$url = "https://github.com/mpv-player/mpv/releases/download/v$version/$zipName"

$root = Split-Path -Parent $PSScriptRoot
$dest = Join-Path $root "lib/mpv/$Rid"
$marker = Join-Path $dest ".version"

if (-not $Force -and (Test-Path $marker) -and ((Get-Content $marker -Raw).Trim() -eq "$version-gpl")) {
    Write-Host "libmpv $version ($Rid) already present in $dest"
    return
}

# Downloads go into their own fresh directory (untrusted data, never executed).
$dlDir = Join-Path $root ".tools/downloads/libmpv-$version-$Rid"
$extractDir = Join-Path $dlDir 'extract'
if (Test-Path $dlDir) { Remove-Item -Recurse -Force $dlDir }
New-Item -ItemType Directory -Force $extractDir | Out-Null

$zipPath = Join-Path $dlDir $zipName
Write-Host "Downloading $url"
Invoke-WebRequest -Uri $url -OutFile $zipPath -UseBasicParsing

$hash = (Get-FileHash -Algorithm SHA256 $zipPath).Hash.ToLowerInvariant()
if ($build.Sha256 -like '__PIN_*') {
    Write-Warning "No pinned SHA-256 for $zipName yet. Downloaded file hash: $hash"
} elseif ($hash -ne $build.Sha256) {
    throw "SHA-256 mismatch for $zipName. Expected $($build.Sha256), got $hash"
} else {
    Write-Host "SHA-256 OK ($hash)"
}

Expand-Archive -Path $zipPath -DestinationPath $extractDir -Force

$dll = Get-ChildItem -Path $extractDir -Recurse -Filter 'libmpv-2.dll' | Select-Object -First 1
if (-not $dll) { throw "libmpv-2.dll not found in $zipName" }

if (Test-Path $dest) { Remove-Item -Recurse -Force $dest }
New-Item -ItemType Directory -Force $dest | Out-Null
Copy-Item $dll.FullName $dest

$include = Get-ChildItem -Path $extractDir -Recurse -Directory -Filter 'mpv' |
    Where-Object { Test-Path (Join-Path $_.FullName 'client.h') } | Select-Object -First 1
if ($include) {
    New-Item -ItemType Directory -Force (Join-Path $dest 'include') | Out-Null
    Copy-Item $include.FullName (Join-Path $dest 'include') -Recurse
}

$licenses = Get-ChildItem -Path $extractDir -Recurse -File |
    Where-Object { $_.Name -match '^(LICENSE|COPYING|Copyright)' -or $_.Directory.Name -match '^licen[cs]es?$' }
if ($licenses) {
    $licDir = Join-Path $dest 'licenses'
    New-Item -ItemType Directory -Force $licDir | Out-Null
    foreach ($l in $licenses) {
        $rel = $l.FullName.Substring($extractDir.Length).TrimStart('\', '/') -replace '[\\/]', '_'
        Copy-Item $l.FullName (Join-Path $licDir $rel)
    }
}

Set-Content -Path $marker -Value "$version-gpl" -NoNewline
Write-Host "libmpv $version ($Rid) installed to $dest"
