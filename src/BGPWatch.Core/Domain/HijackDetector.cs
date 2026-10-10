using System.Net;

namespace BGPWatch.Core.Domain;

/// <summary>
/// The detection core: classifies each observed announcement against the known-good
/// origins and the RPKI payloads, and raises an alert when it does not match.
/// </summary>
public sealed class HijackDetector(IEnumerable<MonitoredPrefix> monitored, IEnumerable<Roa> vrps)
{
    private readonly IReadOnlyList<MonitoredPrefix> monitored = monitored.ToList();
    private readonly IReadOnlyList<Roa> vrps = vrps.ToList();

    /// <summary>Every alert the announcement warrants: none, one, or several.</summary>
    public IReadOnlyList<Alert> Inspect(Announcement announcement)
    {
        if (announcement.Withdrawn)
        {
            return [];
        }
        return [.. OriginAndSubprefix(announcement), .. Rpki(announcement)];
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

    private IEnumerable<Alert> OriginAndSubprefix(Announcement ann)
    {
        foreach (var watched in monitored.Where(m => Prefixes.SameFamily(m.Prefix, ann.Prefix)))
        {
            var unexpected = ann.OriginAs is not { } origin || !watched.ExpectedOrigins.Contains(origin);
            if (!unexpected)
            {
                continue;
            }
            var expected = FormatOrigins(watched.ExpectedOrigins);
            if (ann.Prefix == watched.Prefix)
            {
                yield return Raise(ann, AlertType.OriginHijack, watched, null,
                    $"{ann.Prefix} announced by AS{FormatAsn(ann.OriginAs)}, expected {expected}");
            }
            else if (watched.Prefix.Covers(ann.Prefix))
            {
                yield return Raise(ann, AlertType.SubprefixHijack, watched, watched.Prefix,
                    $"more-specific {ann.Prefix} of monitored {watched.Prefix} announced by AS{FormatAsn(ann.OriginAs)}, expected {expected}");
            }
        }
    }

    private IEnumerable<Alert> Rpki(Announcement ann)
    {
        if (vrps.Count == 0 || RpkiValidity(ann, vrps) != RpkiState.Invalid)
        {
            yield break;
        }
        yield return new Alert(AlertType.RpkiInvalid, ann.Prefix, ann.OriginAs, ann.Peer, ann.AsPath,
            $"{ann.Prefix} from AS{FormatAsn(ann.OriginAs)} is RPKI-invalid (no VRP authorizes this origin or length)",
            new HashSet<int>(), null, ann.SeenAt);
    }

    private static Alert Raise(Announcement ann, AlertType type, MonitoredPrefix watched, IPNetwork? covering, string detail)
    {
        return new Alert(type, ann.Prefix, ann.OriginAs, ann.Peer, ann.AsPath, detail,
            watched.ExpectedOrigins, covering, ann.SeenAt);
    }

    private static string FormatOrigins(IReadOnlySet<int> origins)
    {
        return origins.Count == 0 ? "none" : string.Join(", ", origins.Order().Select(a => $"AS{a}"));
    }

    private static string FormatAsn(int? asn)
    {
        return asn?.ToString() ?? "None";
    }
}
