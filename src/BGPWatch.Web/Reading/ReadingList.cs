using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using System.Text.Unicode;
using Microsoft.AspNetCore.Html;

namespace BGPWatch.Web.Reading;

/// <summary>A numbered source of the reading list, as the references page shows it.</summary>
public sealed record Source(int Number, string Title, string Year, string Summary, Uri Link)
{
    /// <summary>The link's host without "www.", shown next to the title.</summary>
    public string Host => Link.Host.StartsWith("www.", StringComparison.Ordinal) ? Link.Host[4..] : Link.Host;
}

/// <summary>One learning stage ("# Etapa N. Title") and its sources, in reading order.</summary>
public sealed record Stage(int Number, string Title, IReadOnlyList<string> Intro, IReadOnlyList<Source> Sources);

/// <summary>One RFC of the specification's lineage or of its updates; Version is empty for an update.</summary>
public sealed record Spec(int Rfc, string Title, string Year, string Version, string Relation, string Change, Uri Link);

/// <summary>A titled group of RFCs, the lineage section or the updates section, with its intro.</summary>
public sealed record SpecGroup(string Title, IReadOnlyList<string> Intro, IReadOnlyList<Spec> Specs);

/// <summary>A "start here" pick: the source it links to, the link text and the note after it.</summary>
public sealed record Pick(int Source, string Label, string Note);

/// <summary>
/// The reading list Reading/lecturi-bgp.md, embedded in the assembly at build time and parsed
/// once. Grammar: "# Title" opens a section, its plain lines are intro paragraphs and its
/// "N. ... (sursa K): note" lines are picks; "## Heading" opens an entry, whose "- Key: value"
/// lines are its fields. Stage sections ("Etapa N. Title") hold sources ("## N. Title"); the
/// lineage and updates sections hold specs ("## RFC NNNN, Title"); the start-here section holds
/// the picks. Section titles and field keys are the constants of this type; the version field
/// of a spec is optional. Any other section is ignored. A malformed entry throws a FormatException.
/// </summary>
public sealed partial record ReadingList(
    IReadOnlyList<string> StartHereIntro,
    IReadOnlyList<Pick> Picks,
    IReadOnlyList<Stage> Stages,
    SpecGroup Lineage,
    SpecGroup Updates,
    int LineageAfterStage)
{
    private const string ResourceName = "lecturi-bgp.md";
    private const string StagePrefix = "Etapa ";
    private const string StartHereTitle = "Începe de aici";
    private const string LineagePrefix = "Genealogia";
    private const string UpdatesPrefix = "Actualizările";
    private const string YearField = "An";
    private const string SummaryField = "Pe scurt";
    private const string LinkField = "Link";
    private const string VersionField = "Versiune";
    private const string RelationField = "Relația";
    private const string ChangeField = "Ce s-a schimbat";
    private static readonly HtmlEncoder Encoder = HtmlEncoder.Create(UnicodeRanges.All);

    /// <summary>Every source of every stage, in reading order.</summary>
    public IEnumerable<Source> Sources => Stages.SelectMany(stage => stage.Sources);

    /// <summary>The reading list embedded in this assembly.</summary>
    public static ReadingList Embedded()
    {
        using var stream = typeof(ReadingList).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"resource {ResourceName} is not embedded");
        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd());
    }

    /// <summary>Parses the reading list's Markdown.</summary>
    public static ReadingList Parse(string markdown)
    {
        var sections = Sections(markdown);
        var stages = sections.Where(IsStage).Select(ToStage).ToList();
        var startHere = Single(sections, title => title == StartHereTitle);
        var lineage = Single(sections, title => title.StartsWith(LineagePrefix, StringComparison.Ordinal));
        var updates = Single(sections, title => title.StartsWith(UpdatesPrefix, StringComparison.Ordinal));
        var list = new ReadingList(
            startHere.Paragraphs,
            startHere.ListLines.Select(ToPick).ToList(),
            stages,
            ToGroup(lineage),
            ToGroup(updates),
            sections.TakeWhile(section => section != lineage).Count(IsStage));
        list.Validate();
        return list;
    }

    /// <summary>The text as HTML: encoded, with each `code` span as a code element.</summary>
    public static HtmlString Markup(string text)
    {
        var parts = text.Split('`');
        var html = parts.Select((part, i) => i % 2 == 1 ? $"<code>{Encoder.Encode(part)}</code>" : Encoder.Encode(part));
        return new HtmlString(string.Concat(html));
    }

    private void Validate()
    {
        var numbers = Sources.Select(source => source.Number).ToList();
        if (!numbers.SequenceEqual(Enumerable.Range(1, numbers.Count)))
        {
            throw new FormatException($"{ResourceName}: sources are not numbered 1 to {numbers.Count} in order");
        }
        var dangling = Picks.Where(pick => pick.Source < 1 || pick.Source > numbers.Count).Select(pick => pick.Label).ToList();
        if (dangling.Count > 0)
        {
            throw new FormatException($"{ResourceName}: picks point to missing sources: {string.Join(", ", dangling)}");
        }
    }

    private sealed record Entry(string Heading, Dictionary<string, string> Fields)
    {
        public string Field(string key)
        {
            return Fields.TryGetValue(key, out var value) && value.Length > 0
                ? value
                : throw new FormatException($"{ResourceName}: \"{Heading}\" has no \"{key}\" field");
        }

        public string Optional(string key)
        {
            return Fields.GetValueOrDefault(key, "");
        }

        public Uri Link()
        {
            var match = MarkdownLink().Match(Field(LinkField));
            return match.Success
                ? new Uri(match.Groups[1].Value)
                : throw new FormatException($"{ResourceName}: \"{Heading}\" has no Markdown link in its Link field");
        }
    }

    private sealed record Section(string Title, List<string> Paragraphs, List<string> ListLines, List<Entry> Entries);

    private static List<Section> Sections(string markdown)
    {
        var sections = new List<Section>();
        var paragraph = new List<string>();
        foreach (var text in Body(markdown).Select(line => line.Trim()))
        {
            if (text.Length == 0 || text.StartsWith('#'))
            {
                EndParagraph(sections, paragraph);
            }
            if (text.StartsWith("# ", StringComparison.Ordinal))
            {
                sections.Add(new Section(text[2..], [], [], []));
            }
            else if (sections.Count == 0 || text.Length == 0)
            {
                continue;
            }
            else if (text.StartsWith("## ", StringComparison.Ordinal))
            {
                sections[^1].Entries.Add(new Entry(text[3..], []));
            }
            else if (sections[^1].Entries.Count > 0)
            {
                AddField(sections[^1].Entries[^1], text);
            }
            else if (PickLine().IsMatch(text))
            {
                sections[^1].ListLines.Add(text);
            }
            else
            {
                paragraph.Add(text);
            }
        }
        EndParagraph(sections, paragraph);
        return sections;
    }

    private static void EndParagraph(List<Section> sections, List<string> paragraph)
    {
        if (paragraph.Count > 0 && sections.Count > 0)
        {
            sections[^1].Paragraphs.Add(string.Join(' ', paragraph));
        }
        paragraph.Clear();
    }

    private static void AddField(Entry entry, string text)
    {
        var field = FieldLine().Match(text);
        if (field.Success)
        {
            entry.Fields[field.Groups[1].Value] = field.Groups[2].Value.Trim();
        }
    }

    private static IEnumerable<string> Body(string markdown)
    {
        var lines = markdown.ReplaceLineEndings("\n").Split('\n');
        var start = lines.Length > 0 && lines[0].Trim() == "---" ? Array.FindIndex(lines, 1, l => l.Trim() == "---") + 1 : 0;
        return lines.Skip(start);
    }

    private static bool IsStage(Section section)
    {
        return section.Title.StartsWith(StagePrefix, StringComparison.Ordinal);
    }

    private static Section Single(List<Section> sections, Func<string, bool> titled)
    {
        var found = sections.Where(section => titled(section.Title)).ToList();
        return found.Count == 1
            ? found[0]
            : throw new FormatException($"{ResourceName}: expected one matching section, found {found.Count}");
    }

    private static Stage ToStage(Section section)
    {
        var heading = StageHeading().Match(section.Title);
        if (!heading.Success)
        {
            throw new FormatException($"{ResourceName}: stage \"{section.Title}\" is not \"Etapa N. Title\"");
        }
        return new Stage(int.Parse(heading.Groups[1].Value), heading.Groups[2].Value, section.Paragraphs,
            section.Entries.Select(ToSource).ToList());
    }

    private static Source ToSource(Entry entry)
    {
        var heading = SourceHeading().Match(entry.Heading);
        if (!heading.Success)
        {
            throw new FormatException($"{ResourceName}: source \"{entry.Heading}\" is not \"N. Title\"");
        }
        return new Source(int.Parse(heading.Groups[1].Value), heading.Groups[2].Value, entry.Field(YearField),
            entry.Field(SummaryField), entry.Link());
    }

    private static SpecGroup ToGroup(Section section)
    {
        return new SpecGroup(section.Title, section.Paragraphs, section.Entries.Select(ToSpec).ToList());
    }

    private static Spec ToSpec(Entry entry)
    {
        var heading = SpecHeading().Match(entry.Heading);
        if (!heading.Success)
        {
            throw new FormatException($"{ResourceName}: spec \"{entry.Heading}\" is not \"RFC NNNN, Title\"");
        }
        return new Spec(int.Parse(heading.Groups[1].Value), heading.Groups[2].Value, entry.Field(YearField),
            entry.Optional(VersionField), entry.Field(RelationField), entry.Field(ChangeField), entry.Link());
    }

    private static Pick ToPick(string line)
    {
        var pick = PickLine().Match(line);
        return new Pick(int.Parse(pick.Groups[2].Value), pick.Groups[1].Value, pick.Groups[3].Value);
    }

    [GeneratedRegex(@"^- ([^:]+): (.*)$")]
    private static partial Regex FieldLine();

    [GeneratedRegex(@"^\d+\. (.+?) \(sursa (\d+)\): (.+)$")]
    private static partial Regex PickLine();

    [GeneratedRegex(@"^Etapa (\d+)\. (.+)$")]
    private static partial Regex StageHeading();

    [GeneratedRegex(@"^(\d+)\. (.+)$")]
    private static partial Regex SourceHeading();

    [GeneratedRegex(@"^RFC (\d+), (.+)$")]
    private static partial Regex SpecHeading();

    [GeneratedRegex(@"\]\((https://[^)\s]+)\)")]
    private static partial Regex MarkdownLink();
}
