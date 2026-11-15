using System.ComponentModel;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Otear.Mpv;
using Otear.Player;

namespace Otear.Services;

/// <summary>
/// Starts a <see cref="ThumbnailGenerator"/> for each local video and serves its frames to the seek bar
/// as a (reused) WriteableBitmap. UI thread only.
/// </summary>
internal sealed class ThumbnailService : IDisposable
{
    private readonly PlayerController _player;
    private readonly AppSettings _settings;
    private ThumbnailGenerator? _generator;
    private string? _path;
    private WriteableBitmap? _bitmap;
    private ThumbnailGenerator.Frame? _shown;

    public ThumbnailService(PlayerController player, AppSettings settings)
    {
        _player = player;
        _settings = settings;
        player.PropertyChanged += OnChanged;
        settings.PropertyChanged += OnChanged;
    }

    private void OnChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(PlayerController.HasMedia):
            case nameof(PlayerController.IsLoaded):
            case nameof(PlayerController.HasVideo):
            case nameof(PlayerController.Duration):
            case nameof(PlayerController.Path):
            case nameof(AppSettings.SeekThumbnails):
                Evaluate();
                break;
        }
    }

    private void Evaluate()
    {
        var p = _player;
        string? path = p.Path;
        bool local = path is not null && System.IO.Path.IsPathRooted(path) && File.Exists(path);
        bool wanted = _settings.SeekThumbnails && p.HasMedia && p.IsLoaded && p.HasVideo && p.Duration > 2 && local;
        if (!wanted || !string.Equals(path, _path, StringComparison.OrdinalIgnoreCase)) Stop();
        if (!wanted || _generator is not null) return;

        _path = path;
        _generator = new ThumbnailGenerator(path!, p.Duration, width: 240, hardwareDecoding: _settings.HardwareDecoding);
        _generator.Start();
    }

    /// <summary>Thumbnail for a time (and nudges the generator towards it), or null.</summary>
    public ImageSource? Get(double time)
    {
        var gen = _generator;
        if (gen is null) return null;
        gen.Focus(time);
        var frame = gen.Get(time);
        if (frame is null) return null;
        if (ReferenceEquals(frame, _shown) && _bitmap is not null) return _bitmap;

        if (_bitmap is null || _bitmap.PixelWidth != frame.Width || _bitmap.PixelHeight != frame.Height)
            _bitmap = new WriteableBitmap(frame.Width, frame.Height);
        frame.Bgra.CopyTo(_bitmap.PixelBuffer);
        _bitmap.Invalidate();
        _shown = frame;
        return _bitmap;
    }

    private void Stop()
    {
        var gen = _generator;
        _generator = null;
        _path = null;
        _shown = null;
        if (gen is not null) Task.Run(gen.Dispose);
    }

    public void Dispose()
    {
        _player.PropertyChanged -= OnChanged;
        _settings.PropertyChanged -= OnChanged;
        // Don't block shutdown on the preview core: it is headless and the process is ending.
        Stop();
    }
}
