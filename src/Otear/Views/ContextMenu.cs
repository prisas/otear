using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Otear.Controls;
using Otear.Mpv;
using Otear.Player;
using Windows.System;

namespace Otear.Views;

/// <summary>Right-click menu on the video.</summary>
internal static class ContextMenu
{
    public static MenuFlyout Build(PlayerController p, IPlayerHost host, PlayerView view)
    {
        var menu = new MenuFlyout();
        var items = menu.Items;

        items.Add(Item("Open file…", "", () => _ = host.OpenFilesAsync(), VirtualKey.O, VirtualKeyModifiers.Control));
        items.Add(Item("Open URL…", "", () => _ = host.OpenUrlAsync(), VirtualKey.U, VirtualKeyModifiers.Control));
        items.Add(new MenuFlyoutSeparator());

        items.Add(Item(p.IsPaused ? "Play" : "Pause", p.IsPaused ? "" : "", p.TogglePause, VirtualKey.Space));

        var video = new MenuFlyoutSubItem { Text = "Video", Icon = new FontIcon { Glyph = "" } };
        TrackMenus.FillTracks(video.Items, p, MpvTrackType.Video, includeOff: false);
        video.Items.Add(new MenuFlyoutSeparator());
        var aspect = new MenuFlyoutSubItem { Text = "Aspect ratio" };
        foreach (var (label, value) in new[] { ("Default", "no"), ("16:9", "16:9"), ("4:3", "4:3"), ("21:9", "2.333"), ("2.39:1", "2.39"), ("1:1", "1:1") })
        {
            var r = new RadioMenuFlyoutItem { Text = label, GroupName = "aspect", IsChecked = p.AspectOverride == value || (value == "no" && p.AspectOverride is "no" or "-1") };
            string v = value;
            r.Click += (_, _) => p.SetAspectOverride(v);
            aspect.Items.Add(r);
        }
        video.Items.Add(aspect);
        video.Items.Add(Item("Rotate 90°", "", () => p.SetRotation(p.Rotation + 90)));
        items.Add(video);

        var audio = new MenuFlyoutSubItem { Text = "Audio", Icon = new FontIcon { Glyph = "" } };
        TrackMenus.FillTracks(audio.Items, p, MpvTrackType.Audio, includeOff: false);
        items.Add(audio);

        var subs = new MenuFlyoutSubItem { Text = "Subtitles", Icon = new FontIcon { Glyph = "" } };
        TrackMenus.FillSubtitles(subs.Items, p, () => _ = host.OpenSubtitleAsync());
        items.Add(subs);

        var speed = new MenuFlyoutSubItem { Text = "Speed", Icon = new FontIcon { Glyph = "" } };
        TrackMenus.FillSpeeds(speed.Items, p, [0.25, 0.5, 0.75, 1, 1.25, 1.5, 1.75, 2, 3, 4]);
        items.Add(speed);

        items.Add(new MenuFlyoutSeparator());
        items.Add(Item("Take screenshot", "", () => _ = p.ScreenshotAsync(withSubtitles: true), VirtualKey.S));
        items.Add(Item("Statistics", "", p.ToggleStats, VirtualKey.I));
        items.Add(Item("Playlist & chapters", "", view.TogglePlaylist, VirtualKey.P));
        items.Add(Item("Video, audio & subtitle settings", "", view.ToggleQuickSettings, VirtualKey.Q));

        var loop = new ToggleMenuFlyoutItem { Text = "Loop", IsChecked = p.LoopFile != "no", Icon = new FontIcon { Glyph = "" } };
        loop.Click += (_, _) => p.SetLoopFile(loop.IsChecked);
        items.Add(loop);

        var onTop = new ToggleMenuFlyoutItem { Text = "Always on top", IsChecked = host.IsAlwaysOnTop, Icon = new FontIcon { Glyph = "" } };
        onTop.Click += (_, _) => host.IsAlwaysOnTop = onTop.IsChecked;
        items.Add(onTop);

        items.Add(Item(host.IsFullscreen ? "Exit full screen" : "Full screen", host.IsFullscreen ? "" : "", host.ToggleFullscreen, VirtualKey.F));
        items.Add(new MenuFlyoutSeparator());
        items.Add(Item("Settings", "", host.ShowSettings, (VirtualKey)188, VirtualKeyModifiers.Control));
        items.Add(Item("Close file", "", host.CloseMedia, VirtualKey.W, VirtualKeyModifiers.Control));
        return menu;
    }

    private static MenuFlyoutItem Item(string text, string glyph, Action action, VirtualKey key = VirtualKey.None, VirtualKeyModifiers mods = VirtualKeyModifiers.None)
    {
        var item = new MenuFlyoutItem { Text = text, Icon = new FontIcon { Glyph = glyph } };
        item.Click += (_, _) => action();
        if (key != VirtualKey.None)
        {
            // Shown as a hint only; the window handles the actual shortcuts.
            item.KeyboardAcceleratorTextOverride = Shortcut(key, mods);
        }
        return item;
    }

    private static string Shortcut(VirtualKey key, VirtualKeyModifiers mods)
    {
        string k = key switch
        {
            VirtualKey.Space => "Space",
            (VirtualKey)188 => ",",
            _ => key.ToString(),
        };
        return mods.HasFlag(VirtualKeyModifiers.Control) ? "Ctrl+" + k : k;
    }
}
