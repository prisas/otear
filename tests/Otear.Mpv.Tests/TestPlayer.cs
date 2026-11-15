using System.Collections.Concurrent;
using Otear.Mpv;
using Otear.Mpv.Native;

namespace Otear.Mpv.Tests;

/// <summary>A headless mpv core (vo=null, ao=null) with helpers to await events in tests.</summary>
internal sealed class TestPlayer : IDisposable
{
    public MpvClient Client { get; }
    private readonly BlockingCollection<object> _events = new();
    public ConcurrentQueue<string> Log { get; } = new();

    public TestPlayer(params (string, string)[] extra)
    {
        var options = new List<KeyValuePair<string, string>>
        {
            new("vo", "null"),
            new("ao", "null"),
            new("idle", "yes"),
            new("keep-open", "yes"),
            new("pause", "yes"),
            new("config", "no"),
            new("osc", "no"),
            new("load-scripts", "no"),
            new("ytdl", "no"),
            new("terminal", "no"),
            new("hwdec", "no"),
        };
        options.AddRange(extra.Select(e => new KeyValuePair<string, string>(e.Item1, e.Item2)));
        Client = MpvClient.Create(options, "warn");
        Client.EventRaised += e => _events.Add(e);
        Client.EndFile += e => _events.Add(e);
        Client.PropertyChanged += e => _events.Add(e);
        Client.LogMessage += m => Log.Enqueue($"[{m.Prefix}] {m.Level}: {m.Text}");
    }

    public static string Media(string name)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "testmedia")))
            dir = dir.Parent;
        string path = dir is null ? string.Empty : Path.Combine(dir.FullName, "testmedia", name);
        if (!File.Exists(path))
            Assert.Inconclusive($"Test clip '{name}' missing. Run tools/New-TestMedia.ps1.");
        return path;
    }

    public T WaitFor<T>(Func<T, bool>? predicate = null, double seconds = 15)
    {
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        while (true)
        {
            var remaining = deadline - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero || !_events.TryTake(out var ev, remaining))
                throw new TimeoutException($"Timed out waiting for {typeof(T).Name}. mpv log:\n{string.Join('\n', Log)}");
            if (ev is T t && (predicate is null || predicate(t))) return t;
        }
    }

    public void WaitEvent(MpvEventId id, double seconds = 15) => WaitFor<MpvSimpleEvent>(e => e.Id == id, seconds);

    public void Load(string path)
    {
        Client.Command("loadfile", path);
        WaitEvent(MpvEventId.FileLoaded);
    }

    public void Dispose() => Client.Dispose();
}
