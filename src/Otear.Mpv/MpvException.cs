using Otear.Mpv.Native;

namespace Otear.Mpv;

public sealed class MpvException : Exception
{
    public int ErrorCode { get; }

    public MpvException(int errorCode, string context)
        : base($"{context}: {Describe(errorCode)} ({errorCode})")
    {
        ErrorCode = errorCode;
    }

    public static unsafe string Describe(int errorCode)
        => Utf8.ToManaged(LibMpv.mpv_error_string(errorCode)) ?? "unknown error";

    /// <summary>mpv error codes (subset of enum mpv_error).</summary>
    public static class Codes
    {
        public const int PropertyNotFound = -8;
        public const int PropertyFormat = -9;
        public const int PropertyUnavailable = -10;
        public const int Command = -12;
        public const int LoadingFailed = -13;
        public const int AoInitFailed = -14;
        public const int VoInitFailed = -15;
        public const int NothingToPlay = -16;
        public const int UnknownFormat = -17;
    }
}
