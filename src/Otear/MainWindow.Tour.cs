#if DEBUG
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;
using Otear.Services;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace Otear;

/// <summary>
/// Debug-only UI tour: when OTEAR_TOUR_DIR is set, steps through the main screens and writes PNG
/// snapshots of the XAML (the video itself is not captured). Used to review layout without a screen.
/// </summary>
public sealed partial class MainWindow
{
    internal async Task RunTourAsync(string dir, string media)
    {
        Directory.CreateDirectory(dir);
        try
        {
            await Task.Delay(1500);
            await SnapAsync(Root, dir, "01-welcome");

            await OpenItemsAsync([media], append: false);
            await Task.Delay(3500);
            _player.Pause();
            await Task.Delay(500);
            await SnapAsync(Root, dir, "02-player");

            Player.ToggleQuickSettings();
            await Task.Delay(600);
            await SnapAsync(Root, dir, "03-quick-video");
            Player.SelectQuickTab(1);
            await Task.Delay(400);
            await SnapAsync(Root, dir, "04-quick-audio");
            Player.SelectQuickTab(2);
            await Task.Delay(400);
            await SnapAsync(Root, dir, "05-quick-subtitles");

            Player.TogglePlaylist();
            await Task.Delay(500);
            await SnapAsync(Root, dir, "06-playlist");
            Player.CloseSidePanel();

            Player.ControlBar.SeekBar.TourHover(0.62);
            await Task.Delay(800);
            await SnapAsync(Player.ControlBar.SeekBar.TourHoverCard, dir, "07-seek-hover");

            Settings.Theme = AppTheme.Light;
            await Task.Delay(700);
            await SnapAsync(Root, dir, "08-player-light");
            Player.ToggleQuickSettings();
            await Task.Delay(600);
            await SnapAsync(Root, dir, "09-quick-light");
            Player.CloseSidePanel();

            CloseMedia();
            await Task.Delay(1200);
            await SnapAsync(Root, dir, "10-welcome-light");
            Settings.Theme = AppTheme.Dark;

            ShowSettings();
            await Task.Delay(1200);
            for (int i = 0; i < 7; i++)
            {
                _settingsWindow!.TourSelectPage(i);
                await Task.Delay(500);
                await SnapAsync(_settingsWindow.Content, dir, $"{11 + i:00}-settings-{i}");
            }
            Settings.Theme = AppTheme.System;
        }
        catch (Exception ex)
        {
            Log.Error("UI tour failed", ex);
        }
        Log.Info("UI tour finished");
        Close();
    }

    private static async Task SnapAsync(UIElement element, string dir, string name)
    {
        var rtb = new RenderTargetBitmap();
        await rtb.RenderAsync(element);
        var pixels = (await rtb.GetPixelsAsync()).ToArray();
        using var stream = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied,
            (uint)rtb.PixelWidth, (uint)rtb.PixelHeight, 96, 96, pixels);
        await encoder.FlushAsync();
        stream.Seek(0);
        var bytes = new byte[stream.Size];
        await stream.ReadAsync(bytes.AsBuffer(), (uint)bytes.Length, InputStreamOptions.None);
        await File.WriteAllBytesAsync(Path.Combine(dir, name + ".png"), bytes);
        Log.Info($"tour snapshot {name} ({rtb.PixelWidth}x{rtb.PixelHeight})");
    }
}
#endif
