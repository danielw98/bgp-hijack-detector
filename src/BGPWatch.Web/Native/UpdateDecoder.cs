using System.Net;
using BGPWatch.Core.Domain;

namespace BGPWatch.Web.Native;

/// <summary>The data holds a truncated or malformed record.</summary>
public sealed class MalformedUpdateException() : Exception("truncated or malformed BGP data");

/// <summary>
/// BGP UPDATEs decoded by the C library into announcements, one per prefix: from MRT
/// records (RFC 6396), or from one bare UPDATE as BMP carries it (RFC 7854).
/// </summary>
public static unsafe class UpdateDecoder
{
    /// <summary>Every announced or withdrawn prefix of MRT-encoded bytes.</summary>
    public static List<Announcement> ReadMrt(ReadOnlySpan<byte> data)
    {
        fixed (byte* bytes = data)
        {
            return Drain(NativeMrt.MrtOpen(bytes, (nuint)data.Length), null, default);
        }
    }

    /// <summary>
    /// Every prefix of one BGP message from its marker on, with four-octet AS numbers,
    /// as seen by <paramref name="peer"/> at <paramref name="seenAt"/>.
    /// </summary>
    public static List<Announcement> ReadUpdate(ReadOnlySpan<byte> message, string peer, DateTime seenAt)
    {
        fixed (byte* bytes = message)
        {
            return Drain(NativeMrt.BgpUpdateOpen(bytes, (nuint)message.Length), peer, seenAt);
        }
    }

    private static List<Announcement> Drain(nint reader, string? peer, DateTime seenAt)
    {
        if (reader == 0)
        {
            throw new OutOfMemoryException("mrt reader");
        }
        try
        {
            var routes = new List<Announcement>();
            var route = new NativeMrt.Route();
            int status;
            while ((status = NativeMrt.Next(reader, &route)) != NativeMrt.End)
            {
                if (status == NativeMrt.Malformed)
                {
                    throw new MalformedUpdateException();
                }
                routes.Add(ToAnnouncement(&route, peer, seenAt));
            }
            return routes;
        }
        finally
        {
            NativeMrt.Close(reader);
        }
    }

    private static Announcement ToAnnouncement(NativeMrt.Route* route, string? peer, DateTime seenAt)
    {
        var prefix = new IPNetwork(ToAddress(&route->Prefix), route->PrefixLength);
        var at = peer is null ? DateTime.UnixEpoch.AddSeconds(route->Timestamp) : seenAt;
        var from = peer ?? ToAddress(&route->Peer).ToString();
        if (route->Withdrawn != 0)
        {
            return Announcement.Withdrawal(prefix, from, at);
        }
        var asPath = new int[route->AsPathLength];
        for (var i = 0; i < asPath.Length; i++)
        {
            asPath[i] = checked((int)route->AsPath[i]);
        }
        var nextHop = route->NextHop.Family == 0 ? null : ToAddress(&route->NextHop).ToString();
        return Announcement.Of(prefix, asPath, from, nextHop, at);
    }

    private static IPAddress ToAddress(NativeMrt.Address* address)
    {
        var size = address->Family == 6 ? 16 : 4;
        return new IPAddress(new ReadOnlySpan<byte>(address->Bytes, size));
    }
}
