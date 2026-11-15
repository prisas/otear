using Microsoft.UI.Xaml.Controls;
using Otear.Mpv;
using Otear.Player;

namespace Otear.Controls;

/// <summary>Builds the track / speed menus shared by the control bar and the right-click menu.</summary>
internal static class TrackMenus
{
    public static void FillTracks(IList<MenuFlyoutItemBase> items, PlayerController p, MpvTrackType type, bool includeOff)
    {
        string group = "tracks-" + type;
        long? selected = type switch
        {
            MpvTrackType.Video => p.VideoId,
            MpvTrackType.Audio => p.AudioId,
            _ => p.SubtitleId,
        };
        if (includeOff)
        {
            var off = new RadioMenuFlyoutItem { Text = "Off", GroupName = group, IsChecked = selected is null };
            off.Click += (_, _) => p.SelectTrack(type, null);
            items.Add(off);
        }
        var tracks = p.TracksOf(type).Where(t => type != MpvTrackType.Video || !t.IsAlbumArt).ToList();
        foreach (var t in tracks)
        {
            var item = new RadioMenuFlyoutItem { Text = t.DisplayName, GroupName = group, IsChecked = t.Id == selected };
            long id = t.Id;
            item.Click += (_, _) => p.SelectTrack(type, id);
            items.Add(item);
        }
        if (tracks.Count == 0 && !includeOff)
            items.Add(new MenuFlyoutItem { Text = "No tracks", IsEnabled = false });
    }

    public static void FillSubtitles(IList<MenuFlyoutItemBase> items, PlayerController p, Action openFile)
    {
        FillTracks(items, p, MpvTrackType.Sub, includeOff: true);

        var subs = p.TracksOf(MpvTrackType.Sub).ToList();
        if (subs.Count > 1)
        {
            var secondary = new MenuFlyoutSubItem { Text = "Secondary subtitles" };
            var off = new RadioMenuFlyoutItem { Text = "Off", GroupName = "secondary-sub", IsChecked = p.SecondarySubtitleId is null };
            off.Click += (_, _) => p.SelectSecondarySubtitle(null);
            secondary.Items.Add(off);
            foreach (var t in subs)
            {
                var item = new RadioMenuFlyoutItem { Text = t.DisplayName, GroupName = "secondary-sub", IsChecked = t.Id == p.SecondarySubtitleId };
                long id = t.Id;
                item.Click += (_, _) => p.SelectSecondarySubtitle(id);
                secondary.Items.Add(item);
            }
            items.Add(secondary);
        }

        items.Add(new MenuFlyoutSeparator());
        var load = new MenuFlyoutItem { Text = "Load subtitle file…", Icon = new FontIcon { Glyph = "" } };
        load.Click += (_, _) => openFile();
        items.Add(load);
    }

    public static void FillSpeeds(IList<MenuFlyoutItemBase> items, PlayerController p, IEnumerable<double> speeds)
    {
        foreach (var s in speeds)
        {
            var item = new RadioMenuFlyoutItem
            {
                Text = s == 1 ? "Normal" : $"{s:0.##}×",
                GroupName = "speed",
                IsChecked = Math.Abs(p.Speed - s) < 0.001,
            };
            double v = s;
            item.Click += (_, _) => p.SetSpeed(v);
            items.Add(item);
        }
    }
}
