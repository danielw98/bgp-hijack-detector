using System.Net;
using System.Text.Json;
using BGPWatch.Core.Application;
using BGPWatch.Core.Domain;
using BGPWatch.Web.Localization;
using Tomlyn;

namespace BGPWatch.Web.Config;

/// <summary>A monitored prefix as a config.toml writes it.</summary>
public sealed record MonitoredEntry(string Prefix, long[] ExpectedOrigins, string Description = "")
{
    public MonitoredPrefix ToDomain()
    {
        return new MonitoredPrefix(IPNetwork.Parse(Prefix), ExpectedOrigins.ToHashSet(), Description);
    }
}

/// <summary>A validated ROA payload as a config.toml writes it.</summary>
public sealed record VrpEntry(string Prefix, int MaxLength, long OriginAs)
{
    public Roa ToDomain()
    {
        return new Roa(IPNetwork.Parse(Prefix), MaxLength, OriginAs);
    }
}

/// <summary>A moment of an incident's documented timeline, labelled in English and in Romanian.</summary>
public sealed record IncidentEvent(DateTime At, string Label, string LabelRo)
{
    public string LabelIn(Language language)
    {
        return language == Language.Ro ? LabelRo : Label;
    }
}

/// <summary>An incident's config.toml: its ground truth and its documented timeline.</summary>
public sealed record IncidentFile(IReadOnlyList<MonitoredEntry> Monitored, IReadOnlyList<IncidentEvent> Events);

/// <summary>
/// The TOML files that hold the ground truth: detector/config.toml for the lab and one
/// config.toml per recorded incident, each with monitored prefixes and RPKI payloads;
/// an incident's file also holds its documented timeline.
/// </summary>
public static class TomlFiles
{
    /// <summary>snake_case keys, as every TOML file of the project writes them.</summary>
    public static readonly TomlSerializerOptions Options = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    public static DetectorConfig LoadDetector(string path)
    {
        var file = Read(path);
        return new DetectorConfig([.. file.Monitored.Select(m => m.ToDomain())], [.. file.Vrp.Select(v => v.ToDomain())]);
    }

    public static IncidentFile LoadIncident(string path)
    {
        var file = Read(path);
        return new IncidentFile(file.Monitored, [.. file.Events.Select(e => new IncidentEvent(e.At.UtcDateTime, e.Label, e.LabelRo))]);
    }

    private static GroundTruth Read(string path)
    {
        return TomlSerializer.Deserialize<GroundTruth>(File.ReadAllText(path), Options)
            ?? throw new InvalidDataException($"empty TOML file: {path}");
    }

    private sealed class GroundTruth
    {
        public List<MonitoredEntry> Monitored { get; init; } = [];

        public List<VrpEntry> Vrp { get; init; } = [];

        public List<EventEntry> Events { get; init; } = [];
    }


    private sealed record EventEntry(DateTimeOffset At, string Label, string LabelRo);
}
