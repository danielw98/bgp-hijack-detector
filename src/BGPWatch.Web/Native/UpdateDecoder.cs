using System.Net;
using BGPWatch.Core.Application;
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
    /// <summary>
    /// Every announced or withdrawn prefix of MRT-encoded bytes. A malformed record is
    /// dropped and counted, and reading goes on with the next record.
    /// </summary>
    public static ArchiveFile ReadMrt(ReadOnlySpan<byte> data)
    {
        fixed (byte* bytes = data)
        {
            var (routes, malformed) = Drain(NativeMrt.MrtOpen(bytes, (nuint)data.Length), null, default);
            return new ArchiveFile(routes, malformed);
        }
    }

    /// <summary>
    /// Every prefix of one BGP message from its marker on, with four-octet AS numbers,
    /// as seen by <paramref name="peer"/> at <paramref name="seenAt"/>. A malformed message
    /// yields no route: it throws.
    /// </summary>
    public static List<Announcement> ReadUpdate(ReadOnlySpan<byte> message, string peer, DateTime seenAt)
    {
        fixed (byte* bytes = message)
        {
            var (routes, malformed) = Drain(NativeMrt.BgpUpdateOpen(bytes, (nuint)message.Length), peer, seenAt);
            return malformed == 0 ? routes : throw new MalformedUpdateException();
        }
    }

    private static (List<Announcement> Routes, int Malformed) Drain(nint reader, string? peer, DateTime seenAt)
    {
        if (reader == 0)
        {
            throw new OutOfMemoryException("mrt reader");
        }
        try
        {
            var routes = new List<Announcement>();
            var malformed = 0;
            var route = new NativeMrt.Route();
            int status;
            while ((status = NativeMrt.Next(reader, &route)) != NativeMrt.End)
            {
                if (status == NativeMrt.Invalid)
                {
                    throw new InvalidOperationException("mrt_next refused its arguments");
                }
                if (status == NativeMrt.Malformed)
                {
                    malformed++;
                    continue;
                }
                routes.Add(ToAnnouncement(&route, peer, seenAt));
            }
            return (routes, malformed);
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
        var asPath = new long[route->AsPathLength];
        for (var i = 0; i < asPath.Length; i++)
        {
            asPath[i] = route->AsPath[i];
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
