using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

// Loaded by Measure-Playback.ps1 (Add-Type): per-thread CPU time with thread descriptions (mpv names its threads).
public static class OtearThreadTimes
{
    [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr OpenThread(uint access, bool inherit, uint id);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
    [DllImport("kernel32.dll")] static extern bool GetThreadTimes(IntPtr h, out long creation, out long exit, out long kernel, out long user);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern int GetThreadDescription(IntPtr h, out IntPtr desc);
    [DllImport("kernel32.dll")] static extern IntPtr LocalFree(IntPtr p);
    [DllImport("ntdll.dll")] static extern int NtQueryInformationThread(IntPtr h, int cls, out IntPtr info, int len, IntPtr retLen);

    static string ModuleFor(Process p, IntPtr addr)
    {
        long a = addr.ToInt64();
        try
        {
            foreach (ProcessModule m in p.Modules)
            {
                long b = m.BaseAddress.ToInt64();
                if (a >= b && a < b + m.ModuleMemorySize) return m.ModuleName;
            }
        }
        catch { }
        return "?";
    }

    const uint THREAD_QUERY_LIMITED_INFORMATION = 0x0800;
    const uint THREAD_QUERY_INFORMATION = 0x0040;

    public sealed class Sample { public string Name = ""; public double CpuMs; }

    public static Dictionary<int, Sample> Take(int pid)
    {
        var result = new Dictionary<int, Sample>();
        Process p;
        try { p = Process.GetProcessById(pid); } catch { return result; }
        int mainTid = -1;
        foreach (ProcessThread t in p.Threads)
        {
            IntPtr h = OpenThread(THREAD_QUERY_LIMITED_INFORMATION | THREAD_QUERY_INFORMATION, false, (uint)t.Id);
            if (h == IntPtr.Zero) continue;
            try
            {
                long c, e, k, u;
                if (!GetThreadTimes(h, out c, out e, out k, out u)) continue;
                string name = "";
                IntPtr d;
                if (GetThreadDescription(h, out d) >= 0 && d != IntPtr.Zero) { name = Marshal.PtrToStringUni(d) ?? ""; LocalFree(d); }
                if (mainTid < 0) mainTid = t.Id;
                if (t.Id == mainTid && name.Length == 0) name = "(main/UI)";
                if (name.Length == 0)
                {
                    IntPtr start;
                    if (NtQueryInformationThread(h, 9, out start, IntPtr.Size, IntPtr.Zero) == 0) name = "[" + ModuleFor(p, start) + "]";
                }
                result[t.Id] = new Sample { Name = name.Length == 0 ? "(unnamed)" : name, CpuMs = (k + u) / 10000.0 };
            }
            finally { CloseHandle(h); }
        }
        return result;
    }

    /// <summary>CPU ms per thread name between two samples (threads grouped by description).</summary>
    public static Dictionary<string, double> Diff(Dictionary<int, Sample> a, Dictionary<int, Sample> b)
    {
        var byName = new Dictionary<string, double>();
        foreach (var kv in b)
        {
            Sample before;
            double delta = a.TryGetValue(kv.Key, out before) ? kv.Value.CpuMs - before.CpuMs : kv.Value.CpuMs;
            double cur;
            byName.TryGetValue(kv.Value.Name, out cur);
            byName[kv.Value.Name] = cur + delta;
        }
        return byName;
    }
}
