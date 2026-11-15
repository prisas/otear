<#
.SYNOPSIS
  Draws the keyboard shortcut map used in the README: docs/keyboard-light.svg and docs/keyboard-dark.svg.
.DESCRIPTION
  A compact (75 %) US keyboard with every shortcut key coloured by category and labelled with its action, plus
  the Ctrl / Shift combinations and mouse actions below it. The shortcuts themselves live in
  src/Otear/MainWindow.xaml.cs (HandleKey) and src/Otear/Views/PlayerView.xaml.cs (mouse input); when they change,
  update the tables below and run: pwsh tools/New-KeyboardMap.ps1
#>
[CmdletBinding()]
param([string]$OutDir)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if (-not $OutDir) { $OutDir = Join-Path $root 'docs' }
New-Item -ItemType Directory -Force $OutDir | Out-Null

# ---------------------------------------------------------------- shortcuts
$categories = [ordered]@{
    play   = 'Playback'
    seek   = 'Seek'
    volume = 'Volume'
    subs   = 'Audio & subtitles'
    view   = 'Window & panels'
    files  = 'Files & screenshots'
}

# Key cap legend => category, action label (one or two lines, at most ~8 characters each).
$actions = @{
    'Esc'       = 'view',   'Exit full', 'screen'
    '1'         = 'seek',   '10 %'
    '2'         = 'seek',   '20 %'
    '3'         = 'seek',   '30 %'
    '4'         = 'seek',   '40 %'
    '5'         = 'seek',   '50 %'
    '6'         = 'seek',   '60 %'
    '7'         = 'seek',   '70 %'
    '8'         = 'seek',   '80 %'
    '9'         = 'seek',   '90 %'
    '0'         = 'seek',   'Start'
    'Backspace' = 'play',   'Normal', 'speed'
    'Home'      = 'seek',   'Start'
    'Q'         = 'view',   'Quick', 'settings'
    'I'         = 'view',   'Stats'
    'P'         = 'view',   'Playlist'
    '['         = 'play',   'Slower'
    ']'         = 'play',   'Faster'
    'PgUp'      = 'files',  'Prev.', 'file'
    'A'         = 'subs',   'Audio', 'track'
    'S'         = 'files',  'Screen-', 'shot'
    'F'         = 'view',   'Full', 'screen'
    'J'         = 'seek',   '−10 s'
    'K'         = 'play',   'Play', 'pause'
    'L'         = 'seek',   '+10 s'
    'Enter'     = 'view',   'Full', 'screen'
    'PgDn'      = 'files',  'Next', 'file'
    'Z'         = 'subs',   'Subs', '−0.1 s'
    'X'         = 'subs',   'Subs', '+0.1 s'
    'C'         = 'subs',   'Subs', 'track'
    'V'         = 'subs',   'Subs', 'on/off'
    'M'         = 'volume', 'Mute'
    ','         = 'play',   'Frame', 'back'
    '.'         = 'play',   'Frame', 'next'
    '↑'         = 'volume', 'Volume', '+5'
    '↓'         = 'volume', 'Volume', '−5'
    '←'         = 'seek',   '−5 s'
    '→'         = 'seek',   '+5 s'
    'Space'     = 'play',   'Play / pause'
}
$modifiers = @('Ctrl', 'Shift')

# Combinations and mouse actions: each entry is "keys|description"; keys are separated by '+', '/' between
# alternatives. Mouse actions use (round) pill caps.
$combos = [ordered]@{
    'With Ctrl'  = @('O|Open files', 'Shift+O|Open folder', 'U|Open URL', 'W|Close file', ',|Settings', 'L|Loop on/off', '←/→|Previous / next chapter')
    'With Shift' = @('←/→|Seek 1 s', 'S|Screenshot without subtitles')
    'Mouse'      = @('(Click)|Play / pause (see Settings)', '(Double-click)|Full screen', '(Drag)|Move the window', '(Wheel)|Volume', 'Shift+(Wheel)|Seek 5 s', 'Ctrl+(Wheel)|Speed', '(Right-click)|Menu')
}

# ---------------------------------------------------------------- layout (75 % layout, units of one key)
$rows = @(
    @{ y = 0;    keys = @(, @('Esc', 1)) + (1..12 | ForEach-Object { , @("F$_", 1) }) + @(@('PrtSc', 1), @('Ins', 1), @('Del', 1)) }
    @{ y = 1.25; keys = @(@('`', 1), @('1', 1), @('2', 1), @('3', 1), @('4', 1), @('5', 1), @('6', 1), @('7', 1), @('8', 1), @('9', 1), @('0', 1), @('-', 1), @('=', 1), @('Backspace', 2), @('Home', 1)) }
    @{ y = 2.25; keys = @(@('Tab', 1.5), @('Q', 1), @('W', 1), @('E', 1), @('R', 1), @('T', 1), @('Y', 1), @('U', 1), @('I', 1), @('O', 1), @('P', 1), @('[', 1), @(']', 1), @('\', 1.5), @('PgUp', 1)) }
    @{ y = 3.25; keys = @(@('Caps', 1.75), @('A', 1), @('S', 1), @('D', 1), @('F', 1), @('G', 1), @('H', 1), @('J', 1), @('K', 1), @('L', 1), @(';', 1), @("'", 1), @('Enter', 2.25), @('PgDn', 1)) }
    @{ y = 4.25; keys = @(@('Shift', 2.25), @('Z', 1), @('X', 1), @('C', 1), @('V', 1), @('B', 1), @('N', 1), @('M', 1), @(',', 1), @('.', 1), @('/', 1), @('Shift', 1.75), @('↑', 1), @('End', 1)) }
    @{ y = 5.25; keys = @(@('Ctrl', 1.25), @('Win', 1.25), @('Alt', 1.25), @('Space', 6.25), @('Alt', 1), @('Fn', 1), @('Ctrl', 1), @('←', 1), @('↓', 1), @('→', 1)) }
)

$U = 64; $G = 6; $M = 28; $R = 8
$kbWidth = 16 * $U - $G
$width = $kbWidth + 2 * $M
$kbBottom = $M + 6.25 * $U - $G

$themes = @{
    light = @{
        bg = '#f6f8fa'; border = '#d0d7de'; key = '#ffffff'; keyStroke = '#d0d7de'; keyShadow = '#d8dee4'
        legend = '#1f2328'; dim = '#8c959f'; text = '#1f2328'; faint = '#59636e'; fill = 0.10
        play = '#0969da'; seek = '#1a7f37'; volume = '#bc4c00'; subs = '#8250df'; view = '#bf3989'; files = '#1b7c83'
    }
    dark  = @{
        bg = '#161b22'; border = '#30363d'; key = '#21262d'; keyStroke = '#373e47'; keyShadow = '#0d1117'
        legend = '#e6edf3'; dim = '#6e7681'; text = '#e6edf3'; faint = '#8b949e'; fill = 0.16
        play = '#58a6ff'; seek = '#3fb950'; volume = '#f0883e'; subs = '#bc8cff'; view = '#f778ba'; files = '#39c5cf'
    }
}

function Esc([string]$s) { [System.Security.SecurityElement]::Escape($s) }

# Rough text width for laying out the small caps (Segoe UI-like proportions).
function TextWidth([string]$s, [double]$size) {
    $w = 0.0
    foreach ($c in $s.ToCharArray()) {
        $w += switch -CaseSensitive -Regex ([string]$c) {
            '[il.,:;''|!]' { 0.28; break }
            '[ ]' { 0.28; break }
            '[mwMW]' { 0.85; break }
            '[A-Z]' { 0.64; break }
            '[←→↑↓]' { 0.9; break }
            default { 0.53 }
        }
    }
    $w * $size
}

function Cap([System.Text.StringBuilder]$sb, $t, [double]$x, [double]$y, [double]$w, [double]$h, [double]$rx, [string]$cat, [string]$strokeColor) {
    [void]$sb.AppendLine("<rect x='$x' y='$($y + 2)' width='$w' height='$h' rx='$rx' fill='$($t.keyShadow)'/>")
    [void]$sb.AppendLine("<rect x='$x' y='$y' width='$w' height='$h' rx='$rx' fill='$($t.key)' stroke='$strokeColor'/>")
    if ($cat) {
        $c = $t[$cat]
        [void]$sb.AppendLine("<rect x='$x' y='$y' width='$w' height='$h' rx='$rx' fill='$c' fill-opacity='$($t.fill)' stroke='$c' stroke-opacity='.75'/>")
    }
}

# Small inline caps for the combination list; returns the x after the drawn caps.
function InlineCaps([System.Text.StringBuilder]$sb, $t, [string]$keys, [double]$x, [double]$y) {
    $first = $true
    foreach ($alt in $keys -split '/') {
        if (-not $first) { [void]$sb.AppendLine("<text x='$($x + 2)' y='$($y + 16.5)' font-size='13' fill='$($t.faint)'>/</text>"); $x += 12 }
        $first = $false
        $parts = $alt -split '\+'
        for ($i = 0; $i -lt $parts.Count; $i++) {
            $p = $parts[$i]; $pill = $p.StartsWith('(')
            $label = $p.Trim('(', ')')
            $w = [Math]::Max(26, [Math]::Ceiling((TextWidth $label 12.5) + 16))
            Cap $sb $t $x $y $w 24 ($(if ($pill) { 12 } else { 6 })) $null $t.keyStroke
            [void]$sb.AppendLine("<text x='$($x + $w / 2)' y='$($y + 16.5)' font-size='12.5' font-weight='600' text-anchor='middle' fill='$($t.legend)'>$(Esc $label)</text>")
            $x += $w
            if ($i -lt $parts.Count - 1) { [void]$sb.AppendLine("<text x='$($x + 5)' y='$($y + 16.5)' font-size='13' fill='$($t.faint)'>+</text>"); $x += 17 }
        }
    }
    $x
}

foreach ($name in $themes.Keys) {
    $t = $themes[$name]
    $sb = [System.Text.StringBuilder]::new()

    # keyboard
    foreach ($row in $rows) {
        $x = 0.0
        foreach ($k in $row.keys) {
            $legend = $k[0]; $span = [double]$k[1]
            $px = $M + $x * $U; $py = $M + $row.y * $U; $w = $span * $U - $G; $h = $U - $G
            $a = $actions[$legend]
            $isMod = $modifiers -contains $legend
            $cat = if ($a) { $a[0] } else { $null }
            Cap $sb $t $px $py $w $h $R $cat ($(if ($isMod) { $t.faint } else { $t.keyStroke }))
            $legendColor = if ($a -or $isMod) { $t.legend } else { $t.dim }
            $legendSize = if ($legend.Length -gt 1 -and $legend -notmatch '^F\d+$') { 11.5 } else { 14 }
            [void]$sb.AppendLine("<text x='$($px + 8)' y='$($py + 17)' font-size='$legendSize' font-weight='600' fill='$legendColor'>$(Esc $legend)</text>")
            if ($a) {
                $lines = @($a | Select-Object -Skip 1)
                $baseline = $py + $h - 8 - 15 * ($lines.Count - 1)
                foreach ($line in $lines) {
                    [void]$sb.AppendLine("<text x='$($px + 8)' y='$baseline' font-size='13' font-weight='600' fill='$($t[$cat])'>$(Esc $line)</text>")
                    $baseline += 15
                }
            }
            $x += $span
        }
    }

    # category legend
    $ly = $kbBottom + 30; $lx = $M
    foreach ($key in $categories.Keys) {
        $c = $t[$key]
        [void]$sb.AppendLine("<rect x='$lx' y='$($ly - 11)' width='14' height='14' rx='4' fill='$($t.key)'/><rect x='$lx' y='$($ly - 11)' width='14' height='14' rx='4' fill='$c' fill-opacity='$($t.fill * 2)' stroke='$c'/>")
        [void]$sb.AppendLine("<text x='$($lx + 21)' y='$ly' font-size='13.5' fill='$($t.text)'>$(Esc $categories[$key])</text>")
        $lx += 21 + (TextWidth $categories[$key] 13.5) + 30
    }

    # combinations and mouse
    $sepY = $ly + 22
    [void]$sb.AppendLine("<line x1='$M' y1='$sepY' x2='$($M + $kbWidth)' y2='$sepY' stroke='$($t.border)'/>")
    $columns = @(@{ x = $M; caps = 92 }, @{ x = $M + 340; caps = 70 }, @{ x = $M + 660; caps = 128 })
    $ci = 0; $maxY = 0
    foreach ($title in $combos.Keys) {
        $col = $columns[$ci++]
        $y = $sepY + 30
        [void]$sb.AppendLine("<text x='$($col.x)' y='$y' font-size='12' font-weight='700' letter-spacing='.6' fill='$($t.faint)'>$(Esc $title.ToUpperInvariant())</text>")
        $y += 14
        foreach ($entry in $combos[$title]) {
            $keys, $desc = $entry -split '\|', 2
            [void](InlineCaps $sb $t $keys $col.x $y)
            [void]$sb.AppendLine("<text x='$($col.x + $col.caps + 12)' y='$($y + 16.5)' font-size='14' fill='$($t.text)'>$(Esc $desc)</text>")
            $y += 32
        }
        $maxY = [Math]::Max($maxY, $y)
    }
    $height = [Math]::Ceiling($maxY + $M - 8)

    $svg = @"
<svg xmlns="http://www.w3.org/2000/svg" width="$width" height="$height" viewBox="0 0 $width $height" font-family="'Segoe UI Variable Text','Segoe UI',-apple-system,BlinkMacSystemFont,'Helvetica Neue',Arial,sans-serif">
<title>Otear keyboard shortcuts</title>
<rect x=".5" y=".5" width="$($width - 1)" height="$($height - 1)" rx="14" fill="$($t.bg)" stroke="$($t.border)"/>
$($sb.ToString())</svg>
"@
    $path = Join-Path $OutDir "keyboard-$name.svg"
    [System.IO.File]::WriteAllText($path, $svg, [System.Text.UTF8Encoding]::new($false))
    Write-Host "$path  ${width}x$height"
}
