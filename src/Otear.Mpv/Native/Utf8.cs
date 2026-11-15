using System.Runtime.InteropServices;
using System.Text;

namespace Otear.Mpv.Native;

/// <summary>Helpers for passing UTF-8 strings and string arrays to libmpv without allocations on the GC heap.</summary>
internal static unsafe class Utf8
{
    public static string? ToManaged(byte* p) => p == null ? null : Marshal.PtrToStringUTF8((nint)p);

    /// <summary>A null-terminated UTF-8 copy of a string in native memory. Dispose to free.</summary>
    public readonly struct NativeString : IDisposable
    {
        public readonly byte* Pointer;

        public NativeString(string? value)
        {
            if (value is null) { Pointer = null; return; }
            int max = Encoding.UTF8.GetMaxByteCount(value.Length) + 1;
            Pointer = (byte*)NativeMemory.Alloc((nuint)max);
            int written = Encoding.UTF8.GetBytes(value, new Span<byte>(Pointer, max - 1));
            Pointer[written] = 0;
        }

        public void Dispose()
        {
            if (Pointer != null) NativeMemory.Free(Pointer);
        }
    }

    /// <summary>A NULL-terminated array of UTF-8 strings (const char**), as used by mpv_command.</summary>
    public readonly struct NativeStringArray : IDisposable
    {
        public readonly byte** Pointer;
        private readonly int _count;

        public NativeStringArray(ReadOnlySpan<string> values)
        {
            _count = values.Length;
            Pointer = (byte**)NativeMemory.AllocZeroed((nuint)(values.Length + 1), (nuint)sizeof(byte*));
            for (int i = 0; i < values.Length; i++)
                Pointer[i] = new NativeString(values[i] ?? string.Empty).Pointer;
        }

        public void Dispose()
        {
            if (Pointer == null) return;
            for (int i = 0; i < _count; i++)
                if (Pointer[i] != null) NativeMemory.Free(Pointer[i]);
            NativeMemory.Free(Pointer);
        }
    }
}
