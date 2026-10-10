namespace BGPWatch.Web;

/// <summary>
/// Where the app finds its files, whether it may drive the lab, and the URL prefix it is
/// served under (PATH_BASE, empty at the root). Read from the environment, with the paths
/// of the container image as defaults.
/// </summary>
public sealed record Settings(
    string ConnectionString,
    bool LabControl,
    string AttackerContainer,
    string IncidentsDir,
    string DetectorConfig,
    string Schema,
    string PathBase)
{
    public static Settings From(IConfiguration config)
    {
        return new Settings(
            config.GetConnectionString("BGPWatch") ?? "Host=db;Username=bgp;Password=bgp;Database=bgp",
            config["LAB_CONTROL"] == "1",
            config["LAB_ATTACKER_CONTAINER"] ?? "lab-attacker",
            config["INCIDENTS_DIR"] ?? "/data/incidents",
            config["DETECTOR_CONFIG"] ?? "/data/config.toml",
            config["SCHEMA"] ?? "/data/schema.sql",
            (config["PATH_BASE"] ?? "").TrimEnd('/'));
    }

    /// <summary>The folder of a recorded incident, or null when the name names none.</summary>
    public string? IncidentDir(string name)
    {
        var folder = Path.Combine(IncidentsDir, name);
        var valid = name.Length > 0 && name.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '-');
        return valid && File.Exists(Path.Combine(folder, "config.toml")) ? folder : null;
    }

    /// <summary>Every recorded incident: the folders under IncidentsDir that hold a config.toml.</summary>
    public IEnumerable<string> IncidentNames()
    {
        return Directory.Exists(IncidentsDir)
            ? Directory.GetDirectories(IncidentsDir).Select(Path.GetFileName).OfType<string>().Where(n => IncidentDir(n) is not null).Order(StringComparer.Ordinal)
            : [];
    }
}
