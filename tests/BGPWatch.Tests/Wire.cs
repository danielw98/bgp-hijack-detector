using System.Buffers.Binary;
using System.Net;

namespace BGPWatch.Tests;

/// <summary>Hand-built BGP messages and MRT records for the parser tests.</summary>
public static class Wire
{
    public const uint Timestamp = 1203878877;
    public const string Peer = "192.0.2.1";
    public const ushort Bgp4mp = 16;
    public const ushort Bgp4mpEt = 17;
    public const ushort StateChange = 0;
    public const ushort Message = 1;
    public const ushort MessageAs4 = 4;
    public const byte Update = 2;
    public const byte Keepalive = 4;
    public const byte AsSet = 1;
    public const byte AsSequence = 2;
    public const int AsTrans = 23456;

    public static byte[] Prefix(string text)
    {
        var network = IPNetwork.Parse(text);
        var used = (network.PrefixLength + 7) / 8;
        return [(byte)network.PrefixLength, .. network.BaseAddress.GetAddressBytes()[..used]];
    }

    public static byte[] Attribute(byte code, byte[] value)
    {
        return [0x40, code, (byte)value.Length, .. value];
    }

    public static byte[] AsPath(long[] asns, int width = 2, byte segment = AsSequence)
    {
        return [segment, (byte)asns.Length, .. asns.SelectMany(a => BigEndian((uint)a, width))];
    }

    public static byte[] Bgp(byte[] body, byte type = Update)
    {
        return [.. Enumerable.Repeat((byte)0xFF, 16), .. BigEndian((uint)(19 + body.Length), 2), type, .. body];
    }

    public static byte[] UpdateMessage(byte[]? withdrawn = null, byte[]? attributes = null, byte[]? nlri = null)
    {
        withdrawn ??= [];
        attributes ??= [];
        return Bgp([.. BigEndian((uint)withdrawn.Length, 2), .. withdrawn, .. BigEndian((uint)attributes.Length, 2), .. attributes, .. nlri ?? []]);
    }

    public static byte[] Record(byte[] message, ushort subtype = Message, ushort type = Bgp4mp)
    {
        var width = subtype == MessageAs4 ? 4 : 2;
        var peer = IPAddress.Parse(Peer).GetAddressBytes();
        byte[] body =
        [
            .. type == Bgp4mpEt ? new byte[] { 0, 0, 0x12, 0x34 } : [],
            .. BigEndian(64500, width),
            .. BigEndian(64496, width),
            0, 0, 0, 1,
            .. peer,
            .. new byte[peer.Length],
            .. message,
        ];
        return [.. BigEndian(Timestamp, 4), .. BigEndian(type, 2), .. BigEndian(subtype, 2), .. BigEndian((uint)body.Length, 4), .. body];
    }

    public static byte[] BigEndian(uint value, int width)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
        return bytes[(4 - width)..];
    }

    public static readonly byte[] Announce = UpdateMessage(
        attributes: [.. Attribute(2, AsPath([64500, 64501])), .. Attribute(3, IPAddress.Parse(Peer).GetAddressBytes())],
        nlri: [.. Prefix("203.0.113.0/24"), .. Prefix("198.51.100.0/25")]);
}
