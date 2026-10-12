using System.Runtime.InteropServices;

namespace BGPWatch.Web.Native;

/// <summary>The C library in detector/mrt (mrt.h), built by its build.sh.</summary>
internal static unsafe partial class NativeMrt
{
    private const string Library = "mrt";

    public const int End = 0;
    public const int Malformed = -1;
    public const int Invalid = -2;
    public const int MaxAsPath = 2048;

    [LibraryImport(Library, EntryPoint = "mrt_open")]
    public static partial nint MrtOpen(byte* data, nuint size);

    [LibraryImport(Library, EntryPoint = "bgp_update_open")]
    public static partial nint BgpUpdateOpen(byte* message, nuint size);

    [LibraryImport(Library, EntryPoint = "mrt_next")]
    public static partial int Next(nint reader, Route* route);

    [LibraryImport(Library, EntryPoint = "mrt_close")]
    public static partial void Close(nint reader);

    /// <summary>mrt_address: family 4, 6, or 0 when absent, and the address bytes.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct Address
    {
        public byte Family;
        public fixed byte Bytes[16];
    }

    /// <summary>mrt_route: one prefix of an UPDATE with the attributes it shares.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct Route
    {
        public uint Timestamp;
        public Address Peer;
        public Address Prefix;
        public byte PrefixLength;
        public byte Withdrawn;
        public Address NextHop;
        public ushort AsPathLength;
        public fixed uint AsPath[MaxAsPath];
    }
}
