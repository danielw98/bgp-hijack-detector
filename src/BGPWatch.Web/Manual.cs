using System.Reflection;

namespace BGPWatch.Web;

/// <summary>One command of the manual: how it is called, what it does, and the examples that show it.</summary>
public sealed record Command(string Name, string[] Synopsis, string[] Description, string[] Examples);

/// <summary>The manual of the command line, in the shape of a man page: <see cref="Page"/> whole, <see cref="Section"/> for one command, <see cref="Usage"/> for a usage error.</summary>
public static class Manual
{
    public const string Name = "bgp-watch";

    public static readonly Command[] Commands =
    [
        new("watch",
            ["watch [--json]"],
            ["Watch the file's prefixes on RIS Live until Ctrl+C. Alerts and incidents go to standard",
             "output, one line each, times in UTC; the webhooks of the file are posted. The current",
             "routes are read from RIPEstat at start, so a hijack already under way is reported at once."],
            ["bgp-watch watch", "bgp-watch watch --json | tee alerts.jsonl"]),
        new("check",
            ["check PREFIX"],
            ["What RIPEstat sees for PREFIX today: the covering prefix, each origin with its holder and",
             "RPKI state, and whether the file expects those origins. Exit status 2 on an origin the",
             "file does not expect."],
            ["bgp-watch check 193.0.0.0/21", "bgp-watch check 2001:67c:2e8::/48 --config ripe.toml"]),
        new("replay",
            ["replay FILE... [--json]"],
            ["Run the detector over MRT update files (.gz, .bz2 or plain) under the file's prefixes,",
             "then close the incidents. Exit status 2 when it raised anything. Inside the container the",
             "2008 recording is /data/incidents/youtube-2008/updates.20080224.1845.gz."],
            ["bgp-watch replay updates.20080224.1845.gz --config youtube.toml",
             "bgp-watch replay /data/incidents/youtube-2008/*.gz --json"]),
        new("prefixes",
            ["prefixes [list]", "prefixes add PREFIX ASN... [--description TEXT]", "prefixes remove PREFIX"],
            ["Show or edit the watched prefixes. add writes PREFIX with its expected origins (AS3333",
             "or 3333) and replaces an entry of the same prefix; remove deletes the entry. A running",
             "detector applies the file within five seconds."],
            ["bgp-watch prefixes", "bgp-watch prefixes add 193.0.0.0/21 AS3333 --description \"RIPE NCC\"",
             "bgp-watch prefixes remove 193.0.0.0/21"]),
        new("notify",
            ["notify test"],
            ["Post one test notification to every webhook of the file. Exit status 1 when one fails."],
            ["bgp-watch notify test"]),
        new("serve",
            ["serve"],
            ["Run the application: the BMP station, the web page and the API. Needs PostgreSQL",
             "(ConnectionStrings__BGPWatch) and is what the container runs."],
            []),
        new("help",
            ["help [COMMAND]"],
            ["This manual, or the section of one command."],
            ["bgp-watch help", "bgp-watch help replay"]),
        new("dump",
            ["dump FILE"],
            ["Every route of an MRT file, one JSON line each; on standard error how many malformed",
             "records were dropped. An author's verb, for measurements."],
            []),
        new("measure",
            ["measure CONFIG FILE"],
            ["What the detector raises on an MRT file under a detector configuration (detector/config.toml",
             "shape), which peers carried the alerted routes, and how long parsing and detection took.",
             "An author's verb, for the report."],
            []),
    ];

    private const string Indent = "        ";

    public static string Version => Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";

    /// <summary>The whole manual.</summary>
    public static string Page()
    {
        var lines = new List<string>
        {
            "NAME",
            $"    {Name} - watches BGP routes for the prefixes you own and raises an alert when another",
            "    network announces them",
            "",
            "SYNOPSIS",
            $"    {Name} <command> [arguments] [--config FILE] [--json]",
            $"    {Name} help [command]",
            "",
            "DESCRIPTION",
            $"    {Name} compares every BGP announcement it sees with a watch file that lists your prefixes",
            "    and the AS numbers allowed to originate them. Each announcement gets three checks: a",
            "    watched prefix with another origin (false origin), a more specific prefix inside a watched",
            "    one with another origin (sub-prefix hijack), and an announcement the RPKI authorisations do",
            "    not allow (RPKI invalid). The alerts of one prefix and one foreign origin form an incident;",
            "    a webhook is posted when an incident opens and when it closes.",
            "",
            "    The announcements come from RIPE RIS Live (watch), from RIPEstat's view of today (check)",
            "    or from recorded MRT files (replay). None of these needs the database or the web page.",
            "",
            "COMMANDS",
        };
        foreach (var command in Commands)
        {
            lines.AddRange(command.Synopsis.Select(s => $"    {s}"));
            lines.AddRange(command.Description.Select(d => Indent + d));
            lines.Add("");
        }
        lines.AddRange(
        [
            "OPTIONS",
            "    --config FILE   The watch file. Default: $WATCH_CONFIG, then watch/bgpwatch.toml.",
            "    --json          One JSON object per line instead of text, in the words of the webhook",
            "                    payload (watch, replay).",
            "    --version       Print the version and exit.",
            "",
            "FILES",
            "    watch/bgpwatch.toml   The watch file: [[monitored]] prefixes with expected_origins and a",
            "                          description, [[webhook]] entries, optional static [[vrp]] ROAs, the",
            "                          RIS collectors and the RPKI refresh. watch/example.toml is commented.",
            "",
            "ENVIRONMENT",
            "    WATCH_CONFIG    The watch file when --config is absent.",
            "",
            "EXIT STATUS",
            "    0   done, nothing found",
            "    1   usage error or failure",
            "    2   check saw an origin the file does not expect, or replay raised an alert",
            "",
            "EXAMPLES",
        ]);
        lines.AddRange(Commands.SelectMany(c => c.Examples).Select(e => $"    {e}"));
        return string.Join('\n', lines) + "\n";
    }

    /// <summary>The section of one command, or null when there is no such command.</summary>
    public static string? Section(string name)
    {
        var command = Commands.FirstOrDefault(c => c.Name == name);
        if (command is null)
        {
            return null;
        }
        var lines = new List<string> { "SYNOPSIS" };
        lines.AddRange(command.Synopsis.Select(s => $"    {Name} {s}"));
        lines.Add("");
        lines.Add("DESCRIPTION");
        lines.AddRange(command.Description.Select(d => $"    {d}"));
        if (command.Examples.Length > 0)
        {
            lines.Add("");
            lines.Add("EXAMPLES");
            lines.AddRange(command.Examples.Select(e => $"    {e}"));
        }
        return string.Join('\n', lines) + "\n";
    }

    /// <summary>The one-line usage of a command, for a usage error, with the way to the manual.</summary>
    public static string Usage(string name)
    {
        var command = Commands.FirstOrDefault(c => c.Name == name);
        IEnumerable<string> synopsis = command is null
            ? [$"{Name} <command> [arguments]"]
            : command.Synopsis.Select(s => $"{Name} {s}");
        return string.Join('\n', synopsis.Select(s => $"usage: {s}")) + $"\nTry '{Name} help{(command is null ? "" : " " + name)}'.\n";
    }
}
