using System.Buffers.Binary;
using System.Net;
using System.Text;
using BGPWatch.Core.Domain;
using BGPWatch.Web.Native;

namespace BGPWatch.Web.Bmp;

/// <summary>The per-peer header of a BMP message (RFC 7854 section 4.2).</summary>
public sealed record PeerHeader(byte PeerType, byte Flags, IPAddress Address, uint PeerAs, IPAddress BgpId, DateTime Timestamp)
{
    public const int Size = 42;

    /// <summary>The peer type of a router's own Loc-RIB (RFC 9069).</summary>
    public const byte LocRib = 3;

    private const byte V6Flag = 0x80;

    public static PeerHeader Read(ReadOnlySpan<byte> data)
    {
        var flags = data[1];
        var address = (flags & V6Flag) != 0 ? new IPAddress(data.Slice(10, 16)) : new IPAddress(data.Slice(22, 4));
        var seconds = BinaryPrimitives.ReadUInt32BigEndian(data[34..]);
        var microseconds = BinaryPrimitives.ReadUInt32BigEndian(data[38..]);
        var timestamp = DateTime.UnixEpoch.AddSeconds(seconds).AddTicks(microseconds * 10L);
        return new PeerHeader(data[0], flags, address, BinaryPrimitives.ReadUInt32BigEndian(data[26..]),
            new IPAddress(data.Slice(30, 4)), timestamp);
    }
}

/// <summary>
/// One BMP version 3 message (RFC 7854): its type, the per-peer header when the type
/// carries one, and the body after it.
/// </summary>
public sealed record BmpMessage(BmpType Type, PeerHeader? Peer, byte[] Body)
{
    public const int Version = 3;
    public const int HeaderSize = 6;
    public const int MaxSize = 1 << 20;

    private const int InformationSysName = 2;

    /// <summary>The next message of a BMP stream, or null at its clean end.</summary>
    public static async Task<BmpMessage?> ReadAsync(Stream stream, CancellationToken cancel)
    {
        var header = new byte[HeaderSize];
        var read = await stream.ReadAtLeastAsync(header, HeaderSize, throwOnEndOfStream: false, cancel);
        if (read == 0)
        {
            return null;
        }
        if (read < HeaderSize || header[0] != Version)
        {
            throw new InvalidDataException($"not a BMP v3 header: {Convert.ToHexString(header, 0, read)}");
        }
        var length = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(1));
        if (length < HeaderSize || length > MaxSize)
        {
            throw new InvalidDataException($"BMP message length {length}");
        }
        var payload = new byte[length - HeaderSize];
        await stream.ReadExactlyAsync(payload, cancel);
        return Parse((BmpType)header[5], payload);
    }

    private static BmpMessage Parse(BmpType type, byte[] payload)
    {
        if (!HasPeerHeader(type))
        {
            return new BmpMessage(type, null, payload);
        }
        if (payload.Length < PeerHeader.Size)
        {
            throw new InvalidDataException($"BMP {type} shorter than its per-peer header");
        }
        return new BmpMessage(type, PeerHeader.Read(payload), payload[PeerHeader.Size..]);
    }

    private static bool HasPeerHeader(BmpType type)
    {
        return type is not (BmpType.Initiation or BmpType.Termination);
    }

    /// <summary>
    /// The routes of a Loc-RIB route monitoring message, seen at <paramref name="seenAt"/>
    /// from the router named by its BGP identifier; none for any other message.
    /// FRR sends a path the router learned with the router's AS in front, and a path the
    /// router originates with an empty AS_PATH and next hop 0.0.0.0; that one gets the
    /// router's AS and no next hop here, so every path reads as the router would announce
    /// it to a neighbor.
    /// </summary>
    public List<Announcement> LocRibRoutes(DateTime seenAt)
    {
        if (Type != BmpType.RouteMonitoring || Peer is not { PeerType: PeerHeader.LocRib } peer)
        {
            return [];
        }
        long routerAs = peer.PeerAs;
        return UpdateDecoder.ReadUpdate(Body, peer.BgpId.ToString(), seenAt)
            .Select(r => r.Withdrawn || r.AsPath.Count > 0 ? r : r with { AsPath = [routerAs], OriginAs = routerAs, NextHop = null })
            .ToList();
    }

    /// <summary>The sysName of an Initiation message, or empty.</summary>
    public string SysName()
    {
        if (Type != BmpType.Initiation)
        {
            return "";
        }
        var body = Body.AsSpan();
        while (body.Length >= 4)
        {
            var kind = BinaryPrimitives.ReadUInt16BigEndian(body);
            var size = Math.Min(BinaryPrimitives.ReadUInt16BigEndian(body[2..]), body.Length - 4);
            if (kind == InformationSysName)
            {
                return Encoding.UTF8.GetString(body.Slice(4, size));
            }
            body = body[(4 + size)..];
        }
        return "";
    }
}

public enum BmpType : byte
{
    RouteMonitoring = 0,
    StatisticsReport = 1,
    PeerDown = 2,
    PeerUp = 3,
    Initiation = 4,
    Termination = 5,
    RouteMirroring = 6,
}
