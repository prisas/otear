<#
.SYNOPSIS
  Builds a release of Otear: NativeAOT publish (self-contained, single folder) and, when Inno Setup 6 is
  installed, the setup program artifacts/Otear-<version>-<arch>-setup.exe.
.EXAMPLE
  ./tools/Build-Release.ps1
  ./tools/Build-Release.ps1 -Arch arm64
  ./tools/Build-Release.ps1 -SkipInstaller
#>
[CmdletBinding()]
param(
    [ValidateSet('x64', 'arm64')]
    [string]$Arch = 'x64',
    [switch]$SkipInstaller,
    [switch]$NoAot
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$rid = "win-$Arch"
$publishDir = Join-Path $root "artifacts/publish/$rid"

[xml]$props = Get-Content (Join-Path $root 'Directory.Build.props')
$version = ($props.Project.PropertyGroup | ForEach-Object { $_.Version } | Where-Object { $_ }) | Select-Object -First 1
Write-Host "Otear $version ($rid)"

& (Join-Path $PSScriptRoot 'Get-LibMpv.ps1') -Rid $rid

# The NativeAOT linker step locates MSVC through vswhere, which lives in the VS Installer folder.
$vsInstaller = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer'
if (Test-Path $vsInstaller) { $env:PATH = "$vsInstaller;$env:PATH" }
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'

if (Test-Path $publishDir) { Remove-Item -Recurse -Force $publishDir }
$publishArgs = @('publish', (Join-Path $root 'src/Otear/Otear.csproj'), '-c', 'Release', '-r', $rid, '-o', $publishDir)
if ($NoAot) { $publishArgs += @('-p:PublishAot=false', '-p:PublishTrimmed=false', '-p:PublishReadyToRun=true') }
& dotnet @publishArgs
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)" }
Get-ChildItem $publishDir -Filter *.pdb | Remove-Item

# Third-party notices next to the binaries
$licDir = Join-Path $publishDir 'licenses'
New-Item -ItemType Directory -Force $licDir | Out-Null
Copy-Item (Join-Path $root 'LICENSE') (Join-Path $licDir 'Otear-LICENSE.txt')
Copy-Item (Join-Path $root 'THIRD-PARTY-NOTICES.md') $licDir
Get-ChildItem (Join-Path $root "lib/mpv/$rid/licenses") -File -ErrorAction SilentlyContinue | ForEach-Object { Copy-Item $_.FullName (Join-Path $licDir "libmpv-$($_.Name)") }

$size = (Get-ChildItem $publishDir -Recurse -File | Measure-Object Length -Sum).Sum / 1MB
Write-Host ("Published to {0} ({1:0} MB)" -f $publishDir, $size)
if ($SkipInstaller) { return }

# ---- Installer ---------------------------------------------------------------------------------
$iscc = @(
    (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
    (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
    (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe')
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) {
    Write-Warning 'Inno Setup 6 not found (winget install JRSoftware.InnoSetup). Skipping the installer.'
    return
}

$video = 'mkv mp4 m4v mov webm avi wmv asf flv ts m2ts mts m2t mpg mpeg m2v vob ogv 3gp 3g2 rm rmvb divx'.Split(' ')
$audio = 'mp3 flac m4a aac ogg oga opus wav wma ape aiff aif mka dts ac3 eac3 wv'.Split(' ')
$lists = 'm3u m3u8 pls'.Split(' ')

$lines = [System.Collections.Generic.List[string]]::new()
foreach ($group in @(@{ ProgId = 'Otear.Video'; Exts = $video }, @{ ProgId = 'Otear.Audio'; Exts = $audio }, @{ ProgId = 'Otear.Playlist'; Exts = $lists })) {
    foreach ($ext in $group.Exts) {
        $lines.Add("Root: HKA; Subkey: ""Software\Classes\.$ext\OpenWithProgids""; ValueType: string; ValueName: ""$($group.ProgId)""; ValueData: """"; Flags: uninsdeletevalue; Tasks: fileassoc")
        $lines.Add("Root: HKA; Subkey: ""Software\Otear\Capabilities\FileAssociations""; ValueType: string; ValueName: "".$ext""; ValueData: ""$($group.ProgId)""; Tasks: fileassoc")
        $lines.Add("Root: HKA; Subkey: ""Software\Classes\Applications\Otear.exe\SupportedTypes""; ValueType: string; ValueName: "".$ext""; ValueData: """"")
    }
}
$objDir = Join-Path $root 'installer/obj'
New-Item -ItemType Directory -Force $objDir | Out-Null
$assoc = Join-Path $objDir 'Associations.iss'
Set-Content -Path $assoc -Value $lines -Encoding UTF8

& $iscc /Qp "/DAppVersion=$version" "/DArch=$Arch" "/DSourceDir=$publishDir" "/DAssocInclude=$assoc" (Join-Path $root 'installer/Otear.iss')
if ($LASTEXITCODE -ne 0) { throw "ISCC failed ($LASTEXITCODE)" }
Get-Item (Join-Path $root "artifacts/Otear-$version-$Arch-setup.exe") | Select-Object FullName, @{ n = 'MB'; e = { [math]::Round($_.Length / 1MB, 1) } }
