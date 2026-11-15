# Third-party software in Otear

Otear is licensed under the GNU General Public License, version 3 or later (see `LICENSE`).
It bundles or uses the following components:

| Component | Use | License |
| --- | --- | --- |
| [mpv / libmpv](https://mpv.io) 0.42 (official Windows GPL build) | Playback engine (`libmpv-2.dll`) | GPL-2.0-or-later (GPL build) |
| [FFmpeg](https://ffmpeg.org) (inside libmpv-2.dll) | Demuxing and decoding | GPL-2.0-or-later in this build |
| [libplacebo](https://code.videolan.org/videolan/libplacebo) (inside libmpv-2.dll) | GPU rendering, tone mapping, Dolby Vision | LGPL-2.1-or-later |
| [libass](https://github.com/libass/libass) (inside libmpv-2.dll) | Subtitle rendering | ISC |
| [Windows App SDK / WinUI 3](https://github.com/microsoft/WindowsAppSDK) | User interface | MIT |
| [.NET](https://dotnet.microsoft.com) runtime | Application runtime | MIT |
| [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) | MVVM helpers | MIT |
| [CommunityToolkit.WinUI.Controls](https://github.com/CommunityToolkit/Windows) | Settings controls | MIT |

The libmpv build used is published by the mpv project at
<https://github.com/mpv-player/mpv/releases> (`libmpv-v0.42.0-*-w64-mingw32-gpl.zip`); the corresponding
source code is available from the same release page and from the upstream projects listed above.

Otear's design is inspired by [IINA](https://iina.io) for macOS. No IINA code, name or artwork is used.
