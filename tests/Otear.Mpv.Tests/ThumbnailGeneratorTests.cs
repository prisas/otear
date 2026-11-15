using Otear.Mpv;

namespace Otear.Mpv.Tests;

[TestClass]
public sealed class ThumbnailGeneratorTests
{
    [TestMethod]
    [DataRow("h264_720p_120s.mp4", 120.0, 24, 135)]
    [DataRow("hevc_2160p60_main10_hdr10.mkv", 10.0, 10, 135)]
    public void GeneratesDownscaledOpaqueFrames(string clip, double duration, int expectedCount, int expectedHeight)
    {
        using var gen = new ThumbnailGenerator(TestPlayer.Media(clip), duration, width: 240) { StartDelay = TimeSpan.Zero, IdleGap = TimeSpan.Zero };
        Assert.AreEqual(expectedCount, gen.Count);
        var done = new ManualResetEventSlim();
        int ready = 0;
        gen.FrameReady += _ => { if (Interlocked.Increment(ref ready) >= gen.Count) done.Set(); };
        gen.Start();
        Assert.IsTrue(done.Wait(TimeSpan.FromSeconds(60)), $"only {ready}/{gen.Count} thumbnails");

        var frame = gen.Get(duration / 2);
        Assert.IsNotNull(frame);
        // mpv may round the display size by a pixel (aspect correction of the even-height scale).
        Assert.IsInRange(236, 244, frame.Width);
        Assert.IsInRange(expectedHeight - 3, expectedHeight + 3, frame.Height);
        Assert.AreEqual(frame.Width * frame.Height * 4, frame.Bgra.Length);
        for (int i = 3; i < frame.Bgra.Length; i += 4 * 97) Assert.AreEqual((byte)255, frame.Bgra[i]);
        // A test pattern is colourful: the frame must not be blank.
        Assert.IsGreaterThan(10, frame.Bgra.Where((_, i) => i % 4 != 3).Distinct().Count());
    }

    [TestMethod]
    public void WaitsBeforeStartingAndPacesInTheBackground()
    {
        using var gen = new ThumbnailGenerator(TestPlayer.Media("h264_720p_120s.mp4"), 120, width: 160)
        {
            StartDelay = TimeSpan.FromSeconds(1.5),
            IdleGap = TimeSpan.FromMilliseconds(100),
        };
        var times = new System.Collections.Concurrent.ConcurrentQueue<long>();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        gen.FrameReady += _ => times.Enqueue(sw.ElapsedMilliseconds);
        gen.Start();
        Thread.Sleep(1200);
        Assert.IsEmpty(times, "no work during the start delay");
        Thread.Sleep(3000);
        var t = times.ToArray();
        Assert.IsGreaterThanOrEqualTo(3, t.Length);
        // Frames are spaced by at least the idle gap when nobody is hovering.
        for (int i = 1; i < t.Length; i++) Assert.IsGreaterThanOrEqualTo(90, t[i] - t[i - 1]);
    }

    [TestMethod]
    public void FocusPrioritisesNearbyFrames()
    {
        using var gen = new ThumbnailGenerator(TestPlayer.Media("h264_720p_120s.mp4"), 120, width: 160);
        var first = new TaskCompletionSource<int>();
        gen.FrameReady += i => first.TrySetResult(i);
        gen.Focus(100);
        gen.Start();
        Assert.IsTrue(first.Task.Wait(TimeSpan.FromSeconds(30)));
        Assert.AreEqual(gen.IndexFor(100), first.Task.Result);
    }
}
