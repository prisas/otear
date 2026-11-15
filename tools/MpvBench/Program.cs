using System.Diagnostics;
using Otear.Mpv;

// Usage: MpvBench <clip> <seconds> [name=value ...]
// Plays <clip> (looped) through libmpv with Otear's composition-mode options plus overrides, then prints
// process CPU and the per-thread breakdown for the measured window.
if (args.Length == 2 && args[0] == "--profile")
{
    using var m = MpvClient.Create([new("vo", "null"), new("ao", "null")], null);
    foreach (var p in ((List<object?>)m.GetNode("profile-list")!).OfType<IReadOnlyDictionary<string, object?>>().Where(p => (p["name"] as string) == args[1]))
        foreach (var o in ((List<object?>)p["options"]!).OfType<IReadOnlyDictionary<string, object?>>())
            Console.WriteLine($"{o["key"]}={o["value"]}");
    return 0;
}
if (args.Length < 2) { Console.Error.WriteLine("usage: MpvBench <clip> <seconds> [name=value ...]"); return 2; }
string clip = args[0];
double seconds = double.Parse(args[1], System.Globalization.CultureInfo.InvariantCulture);

var opts = new PlayerOptions
{
    CompositionWidth = 2560,
    CompositionHeight = 1440,
    WatchLaterDirectory = Path.GetTempPath(),
    ShaderCacheDirectory = Path.Combine(Path.GetTempPath(), "otear-bench-shaders"),
    ScreenshotDirectory = Path.GetTempPath(),
    ResumePlayback = false,
}.Build().ToList();
opts.Add(new("loop-file", "inf"));
opts.Add(new("ao", "null"));
foreach (var kv in PlayerOptions.ParseExtra(string.Join('\n', args.Skip(2)))) opts.Add(kv);

using var mpv = MpvClient.Create(opts, "error");
mpv.LogMessage += m => Console.Error.WriteLine($"[{m.Prefix}] {m.Text}");
mpv.Command("loadfile", clip);
Thread.Sleep(4000);

int pid = Environment.ProcessId;
var proc = Process.GetCurrentProcess();
proc.Refresh();
var cpu0 = proc.TotalProcessorTime;
var t0 = OtearThreadTimes.Take(pid);
long drop0 = mpv.GetInt64("frame-drop-count") ?? 0, late0 = mpv.GetInt64("vo-delayed-frame-count") ?? 0;
var sw = Stopwatch.StartNew();
Thread.Sleep(TimeSpan.FromSeconds(seconds));
var t1 = OtearThreadTimes.Take(pid);
proc.Refresh();
double wall = sw.Elapsed.TotalSeconds;
double cpu = (proc.TotalProcessorTime - cpu0).TotalMilliseconds / wall / 10;
long drops = (mpv.GetInt64("frame-drop-count") ?? 0) - drop0, late = (mpv.GetInt64("vo-delayed-frame-count") ?? 0) - late0;

Console.WriteLine($"scale={mpv.GetString("scale")} cscale={mpv.GetString("cscale")} dscale={mpv.GetString("dscale")}");
Console.WriteLine($"cpu={cpu:0.0}% drops={drops} delayed={late} vf-fps={mpv.GetString("estimated-vf-fps")} hwdec={mpv.GetString("hwdec-current")} opts=[{string.Join(' ', args.Skip(2))}]");
foreach (var (name, ms) in OtearThreadTimes.Diff(t0, t1).OrderByDescending(kv => kv.Value).Take(6))
    Console.WriteLine($"   {name,-24} {ms / wall / 10,5:0.0}%");
return 0;
