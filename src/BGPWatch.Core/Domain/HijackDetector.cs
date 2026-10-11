using System.Net;

namespace BGPWatch.Core.Domain;

/// <summary>
/// The detection core: classifies each observed announcement against the known-good
/// origins of the most specific monitored prefix covering it and against the RPKI
/// payloads, and raises an alert when it does not match.
/// </summary>
public sealed class HijackDetector(IEnumerable<MonitoredPrefix> monitored, IEnumerable<Roa> vrps)
{
    private readonly Dictionary<IPNetwork, MonitoredPrefix> monitored = monitored.ToDictionary(m => m.Prefix);
    private readonly IReadOnlyList<Roa> vrps = vrps.ToList();

    /// <summary>Every alert the announcement warrants: none, one, or several.</summary>
    public IReadOnlyList<Alert> Inspect(Announcement announcement)
    {
        if (announcement.Withdrawn)
        {
            return [];
        }
        return [.. OriginOrSubprefix(announcement), .. Rpki(announcement)];
    }

    /// <summary>RFC 6811 origin validation of one announcement against a set of VRPs.</summary>
    public static RpkiState RpkiValidity(Announcement announcement, IEnumerable<Roa> vrps)
    {
        var covering = vrps.Where(v => v.Prefix.Covers(announcement.Prefix)).ToList();
        if (covering.Count == 0)
        {
            return RpkiState.NotFound;
        }
        var authorized = covering.Any(v =>
            announcement.Prefix.PrefixLength <= v.MaxLength && announcement.OriginAs == v.OriginAs);
        return authorized ? RpkiState.Valid : RpkiState.Invalid;
    }

    /// <summary>The alert of the most specific monitored prefix covering the announcement, when that prefix does not expect its origin.</summary>
    private IEnumerable<Alert> OriginOrSubprefix(Announcement ann)
    {
        if (monitored.Keys.MostSpecific(ann.Prefix) is not { } governing)
        {
            yield break;
        }
        var watched = monitored[governing];
        if (ann.OriginAs is { } origin && watched.ExpectedOrigins.Contains(origin))
        {
            yield break;
        }
        var expected = FormatOrigins(watched.ExpectedOrigins);
        yield return ann.Prefix == watched.Prefix
            ? Raise(ann, AlertType.OriginHijack, watched, null,
                $"{ann.Prefix} announced by AS{FormatAsn(ann.OriginAs)}, expected {expected}")
            : Raise(ann, AlertType.SubprefixHijack, watched, watched.Prefix,
                $"more-specific {ann.Prefix} of monitored {watched.Prefix} announced by AS{FormatAsn(ann.OriginAs)}, expected {expected}");
    }

    private IEnumerable<Alert> Rpki(Announcement ann)
    {
        if (vrps.Count == 0 || RpkiValidity(ann, vrps) != RpkiState.Invalid)
        {
            yield break;
        }
        yield return new Alert(AlertType.RpkiInvalid, ann.Prefix, ann.OriginAs, ann.Peer, ann.AsPath,
            $"{ann.Prefix} from AS{FormatAsn(ann.OriginAs)} is RPKI-invalid (no VRP authorizes this origin or length)",
            new HashSet<long>(), null, ann.SeenAt);
    }

    private static Alert Raise(Announcement ann, AlertType type, MonitoredPrefix watched, IPNetwork? covering, string detail)
    {
        return new Alert(type, ann.Prefix, ann.OriginAs, ann.Peer, ann.AsPath, detail,
            watched.ExpectedOrigins, covering, ann.SeenAt);
    }

    private static string FormatOrigins(IReadOnlySet<long> origins)
    {
        return origins.Count == 0 ? "none" : string.Join(", ", origins.Order().Select(a => $"AS{a}"));
    }

    private static string FormatAsn(long? asn)
    {
        return asn?.ToString() ?? "None";
    }
}
