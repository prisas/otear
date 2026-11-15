<#
.SYNOPSIS
  Development-only: generates synthetic test clips (H.264/H.265, MP4/MKV, SDR/HDR10/HLG, multi-track,
  subtitles, chapters) into testmedia/ using the ffmpeg from .tools/ffmpeg (see Get-FFmpeg.ps1).
#>
[CmdletBinding()]
param([switch]$Force)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$ffmpeg = Join-Path $root '.tools/ffmpeg/ffmpeg.exe'
if (-not (Test-Path $ffmpeg)) { & (Join-Path $PSScriptRoot 'Get-FFmpeg.ps1') }
$out = Join-Path $root 'testmedia'
$work = Join-Path $out '_src'
New-Item -ItemType Directory -Force $out, $work | Out-Null

$font = 'C\:/Windows/Fonts/segoeui.ttf'
function Stamp([string]$label, [int]$size = 64) {
    "drawtext=fontfile='$font':text='$label  %{pts\:hms}':fontsize=$($size):fontcolor=white:x=40:y=40:box=1:boxcolor=black@0.55:boxborderw=12"
}

# --- Sidecar subtitle sources --------------------------------------------------------------
function Write-Srt([string]$path, [string[]]$lines) {
    $sb = [Text.StringBuilder]::new()
    for ($i = 0; $i -lt $lines.Count; $i++) {
        $s = [TimeSpan]::FromSeconds($i * 2); $e = [TimeSpan]::FromSeconds($i * 2 + 1.8)
        [void]$sb.AppendLine($i + 1)
        [void]$sb.AppendLine(('{0:hh\:mm\:ss\,fff} --> {1:hh\:mm\:ss\,fff}' -f $s, $e))
        [void]$sb.AppendLine($lines[$i]); [void]$sb.AppendLine()
    }
    [IO.File]::WriteAllText($path, $sb.ToString(), [Text.UTF8Encoding]::new($false))
}
Write-Srt (Join-Path $work 'eng.srt') @('English subtitle one', 'English subtitle two', 'English subtitle three', 'English subtitle four', 'English subtitle five')
Write-Srt (Join-Path $work 'spa.srt') @('Subtítulo en español uno', 'Subtítulo dos', 'Subtítulo tres', 'Subtítulo cuatro', 'Subtítulo cinco')
Write-Srt (Join-Path $out 'hevc_1080p.ext.srt') @('External sidecar subtitle', 'Loaded from a separate file', 'Line three', 'Line four', 'Line five')

$ass = @'
[Script Info]
ScriptType: v4.00+
PlayResX: 1920
PlayResY: 1080

[V4+ Styles]
Format: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding
Style: Sign,Segoe UI,72,&H0000FFFF,&H000000FF,&H00000000,&H80000000,-1,0,0,0,100,100,0,0,1,4,2,8,40,40,80,1

[Events]
Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text
Dialogue: 0,0:00:00.50,0:00:04.00,Sign,,0,0,0,,{\fad(300,300)}ASS styled sign (top)
Dialogue: 0,0:00:05.00,0:00:09.50,Sign,,0,0,0,,{\c&H00FF00&}Green ASS sign
'@
[IO.File]::WriteAllText((Join-Path $work 'signs.ass'), $ass, [Text.UTF8Encoding]::new($false))

$chapters = @'
;FFMETADATA1
title=Otear multi-track test
[CHAPTER]
TIMEBASE=1/1000
START=0
END=3000
title=Opening
[CHAPTER]
TIMEBASE=1/1000
START=3000
END=7000
title=Middle part
[CHAPTER]
TIMEBASE=1/1000
START=7000
END=10000
title=Ending
'@
[IO.File]::WriteAllText((Join-Path $work 'chapters.txt'), $chapters, [Text.UTF8Encoding]::new($false))

function Invoke-FF([string]$name, [string[]]$ffArgs) {
    $target = Join-Path $out $name
    if (-not $Force -and (Test-Path $target)) { Write-Host "skip  $name (exists)"; return }
    Write-Host "make  $name"
    & $ffmpeg -hide_banner -loglevel error -y @ffArgs $target
    if ($LASTEXITCODE -ne 0) { throw "ffmpeg failed for $name" }
}

$hdr10 = 'hdr10=1:repeat-headers=1:colorprim=bt2020:transfer=smpte2084:colormatrix=bt2020nc:' +
         'master-display=G(13250,34500)B(7500,3000)R(34000,16000)WP(15635,16450)L(10000000,1):max-cll=1000,400'

# 1. H.264 1080p30 MP4, AAC stereo, mov_text subtitles
Invoke-FF 'h264_1080p30.mp4' @(
    '-f', 'lavfi', '-i', 'testsrc2=size=1920x1080:rate=30:duration=10',
    '-f', 'lavfi', '-i', 'sine=frequency=440:duration=10:sample_rate=48000',
    '-i', (Join-Path $work 'eng.srt'),
    '-map', '0:v', '-map', '1:a', '-map', '2:s',
    '-vf', (Stamp 'H.264 1080p30'),
    '-c:v', 'libx264', '-preset', 'veryfast', '-crf', '23', '-pix_fmt', 'yuv420p', '-g', '30',
    '-c:a', 'aac', '-b:a', '128k', '-ac', '2',
    '-c:s', 'mov_text', '-metadata:s:s:0', 'language=eng',
    '-metadata:s:a:0', 'language=eng', '-movflags', '+faststart')

# 2. HEVC 1080p MKV: 2 audio tracks, 3 subtitle tracks, chapters
Invoke-FF 'hevc_1080p.mkv' @(
    '-f', 'lavfi', '-i', 'testsrc2=size=1920x1080:rate=24:duration=10',
    '-f', 'lavfi', '-i', 'sine=frequency=440:duration=10:sample_rate=48000',
    '-f', 'lavfi', '-i', 'sine=frequency=880:duration=10:sample_rate=48000',
    '-i', (Join-Path $work 'eng.srt'), '-i', (Join-Path $work 'spa.srt'), '-i', (Join-Path $work 'signs.ass'),
    '-i', (Join-Path $work 'chapters.txt'),
    '-map', '0:v', '-map', '1:a', '-map', '2:a', '-map', '3:s', '-map', '4:s', '-map', '5:s', '-map_metadata', '6', '-map_chapters', '6',
    '-vf', (Stamp 'HEVC 1080p24 multi-track'),
    '-c:v', 'libx265', '-preset', 'fast', '-crf', '26', '-pix_fmt', 'yuv420p', '-g', '24', '-x265-params', 'log-level=error',
    '-c:a:0', 'aac', '-b:a:0', '128k', '-ac', '2', '-c:a:1', 'libopus', '-b:a:1', '96k',
    '-c:s:0', 'srt', '-c:s:1', 'srt', '-c:s:2', 'ass',
    '-metadata:s:a:0', 'language=eng', '-metadata:s:a:0', 'title=English (440 Hz)',
    '-metadata:s:a:1', 'language=jpn', '-metadata:s:a:1', 'title=Japanese (880 Hz)',
    '-metadata:s:s:0', 'language=eng', '-metadata:s:s:0', 'title=English',
    '-metadata:s:s:1', 'language=spa', '-metadata:s:s:1', 'title=Español',
    '-metadata:s:s:2', 'language=eng', '-metadata:s:s:2', 'title=Signs (ASS)',
    '-disposition:s:0', 'default')

# 3. HEVC Main10 2160p60 HDR10 MKV with E-AC3 5.1
Invoke-FF 'hevc_2160p60_main10_hdr10.mkv' @(
    '-f', 'lavfi', '-i', 'testsrc2=size=3840x2160:rate=60:duration=10',
    '-f', 'lavfi', '-i', 'sine=frequency=330:duration=10:sample_rate=48000',
    '-map', '0:v', '-map', '1:a',
    '-vf', ((Stamp 'HEVC Main10 2160p60 HDR10' 96) + ',format=yuv420p10le'),
    '-c:v', 'libx265', '-preset', 'ultrafast', '-crf', '22', '-g', '60', '-x265-params', "log-level=error:$hdr10",
    '-color_primaries', 'bt2020', '-color_trc', 'smpte2084', '-colorspace', 'bt2020nc', '-color_range', 'tv',
    '-c:a', 'eac3', '-b:a', '384k', '-ac', '6',
    '-metadata:s:a:0', 'language=eng')

# 4. HEVC Main10 2160p30 HLG MP4 (hvc1 tag)
Invoke-FF 'hevc_2160p_hlg.mp4' @(
    '-f', 'lavfi', '-i', 'testsrc2=size=3840x2160:rate=30:duration=6',
    '-f', 'lavfi', '-i', 'sine=frequency=550:duration=6:sample_rate=48000',
    '-map', '0:v', '-map', '1:a',
    '-vf', ((Stamp 'HEVC 2160p30 HLG' 96) + ',format=yuv420p10le'),
    '-c:v', 'libx265', '-preset', 'ultrafast', '-crf', '24', '-g', '30',
    '-x265-params', 'log-level=error:colorprim=bt2020:transfer=arib-std-b67:colormatrix=bt2020nc:repeat-headers=1',
    '-color_primaries', 'bt2020', '-color_trc', 'arib-std-b67', '-colorspace', 'bt2020nc',
    '-tag:v', 'hvc1', '-c:a', 'aac', '-b:a', '128k', '-ac', '2', '-movflags', '+faststart')

# 5. H.264 2160p30 MP4
Invoke-FF 'h264_2160p30.mp4' @(
    '-f', 'lavfi', '-i', 'testsrc2=size=3840x2160:rate=30:duration=10',
    '-f', 'lavfi', '-i', 'sine=frequency=660:duration=10:sample_rate=48000',
    '-map', '0:v', '-map', '1:a',
    '-vf', (Stamp 'H.264 2160p30' 96),
    '-c:v', 'libx264', '-preset', 'ultrafast', '-crf', '23', '-pix_fmt', 'yuv420p', '-g', '30', '-profile:v', 'high', '-level', '5.1',
    '-c:a', 'aac', '-b:a', '128k', '-ac', '2', '-movflags', '+faststart')

# 6. Long 720p clip for seeking / thumbnail tests (2 minutes, keyframe every second)
Invoke-FF 'h264_720p_120s.mp4' @(
    '-f', 'lavfi', '-i', 'testsrc=size=1280x720:rate=30:duration=120',
    '-f', 'lavfi', '-i', 'sine=frequency=500:duration=120:sample_rate=48000',
    '-map', '0:v', '-map', '1:a',
    '-vf', (Stamp 'Seek test' 48),
    '-c:v', 'libx264', '-preset', 'veryfast', '-crf', '28', '-pix_fmt', 'yuv420p', '-g', '30',
    '-c:a', 'aac', '-b:a', '96k', '-ac', '2', '-movflags', '+faststart')

# 7. Audio-only file with metadata
Invoke-FF 'audio_only.m4a' @(
    '-f', 'lavfi', '-i', 'sine=frequency=392:duration=20:sample_rate=48000',
    '-c:a', 'aac', '-b:a', '128k', '-ac', '2',
    '-metadata', 'title=Test Tone in G', '-metadata', 'artist=Otear Test Suite', '-metadata', 'album=Synthetic Signals')

Write-Host "Test media ready in $out"
Get-ChildItem $out -File | Select-Object Name, @{n = 'MB'; e = { [math]::Round($_.Length / 1MB, 2) } } | Format-Table -AutoSize
