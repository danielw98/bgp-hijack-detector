using BGPWatch.Web.Reading;

namespace BGPWatch.Tests;

/// <summary>
/// The reading lists behind the references page: the embedded Reading/readings.ro.md and
/// Reading/readings.en.md, their parity, and the parser's grammar in both languages.
/// </summary>
public class ReadingListTests
{
    [Theory]
    [InlineData("ro")]
    [InlineData("en")]
    public void TheEmbeddedListNumbersItsSourcesInReadingOrder(string language)
    {
        var list = Embedded(language);
        Assert.Equal(Enumerable.Range(1, list.Stages.Count), list.Stages.Select(stage => stage.Number));
        Assert.All(list.Sources, source => Assert.Equal("https", source.Link.Scheme));
        Assert.All(list.Picks, pick => Assert.Contains(list.Sources, source => source.Number == pick.Source));
    }

    [Theory]
    [InlineData("ro")]
    [InlineData("en")]
    public void TheEmbeddedLineageRunsFromRfc1105ToRfc4271InYearOrder(string language)
    {
        var lineage = Embedded(language).Lineage.Specs;
        Assert.Equal(1105, lineage[0].Rfc);
        Assert.Equal(4271, lineage[^1].Rfc);
        Assert.Equal(lineage.Select(spec => spec.Year).Order(), lineage.Select(spec => spec.Year));
        Assert.All(lineage, spec => Assert.NotEmpty(spec.Version));
    }

    [Theory]
    [InlineData("ro")]
    [InlineData("en")]
    public void EveryEmbeddedUpdateNamesRfc4271InItsRelation(string language)
    {
        var updates = Embedded(language).Updates.Specs;
        Assert.NotEmpty(updates);
        Assert.All(updates, spec => Assert.Contains("4271", spec.Relation));
    }

    [Fact]
    public void TheEnglishListHasTheRomanianStructureAndLinks()
    {
        Assert.Equal(Outline(ReadingList.EmbeddedRomanian()), Outline(ReadingList.EmbeddedEnglish()));
    }

    [Fact]
    public void EveryRomanianFieldKeyHasOneEnglishKeyInEveryEntry()
    {
        var romanian = Keys(Resource("readings.ro.md"));
        var english = Keys(Resource("readings.en.md"));
        Assert.Equal(romanian.Count, english.Count);
        var mapping = romanian.Zip(english).Distinct().ToList();
        Assert.Equal(mapping.Count, mapping.Select(pair => pair.First).Distinct().Count());
        Assert.Equal(mapping.Count, mapping.Select(pair => pair.Second).Distinct().Count());
    }

    [Fact]
    public void AListReadWithTheWrongGrammarIsRejectedWithItsResourceName()
    {
        var error = Assert.Throws<FormatException>(() => ReadingList.Embedded("readings.ro.md", ReadingGrammar.English));
        Assert.StartsWith("readings.ro.md: ", error.Message);
    }

    [Fact]
    public void AMissingResourceIsRejected()
    {
        Assert.Throws<InvalidOperationException>(() => ReadingList.Embedded("readings.xx.md", ReadingGrammar.English));
    }

    [Theory]
    [InlineData("ro")]
    [InlineData("en")]
    public void SectionsBecomePicksStagesAndSpecGroups(string language)
    {
        var list = ReadingList.Parse(Minimal(language), Grammar(language));
        Assert.Equal(["Two picks."], list.StartHereIntro);
        Assert.Equal(new Pick(2, "Second pick", "the detail."), list.Picks[1]);
        Assert.Equal(["Basics", "Detail"], list.Stages.Select(stage => stage.Title));
        Assert.Equal(["Stage intro."], list.Stages[1].Intro);
        Assert.Equal(1, list.LineageAfterStage);
        Assert.Equal((1105, "BGP-1", "1989"), (list.Lineage.Specs[0].Rfc, list.Lineage.Specs[0].Version, list.Lineage.Specs[0].Year));
        Assert.Equal("", list.Updates.Specs[0].Version);
    }

    [Theory]
    [InlineData("ro")]
    [InlineData("en")]
    public void ASourceKeepsItsYearSummaryAndFirstLink(string language)
    {
        var sources = ReadingList.Parse(Minimal(language), Grammar(language)).Sources.ToList();
        Assert.Equal(new Source(1, "An overview", "2024", "Read it first, with `code`.", new Uri("https://www.example.org/a")), sources[0]);
        Assert.Equal("example.org", sources[0].Host);
        Assert.Equal(new Uri("https://a.example/x"), sources[1].Link);
    }

    [Theory]
    [InlineData("ro")]
    [InlineData("en")]
    public void ASourceWithoutASummaryIsRejected(string language)
    {
        var grammar = Grammar(language);
        var broken = Minimal(language).Replace($"- {grammar.SummaryField}: Then this.\n", "");
        var error = Assert.Throws<FormatException>(() => ReadingList.Parse(broken, grammar));
        Assert.Contains($"\"2. The detail\" has no \"{grammar.SummaryField}\"", error.Message);
    }

    [Theory]
    [InlineData("ro")]
    [InlineData("en")]
    public void SourcesOutOfOrderAreRejected(string language)
    {
        var broken = Minimal(language).Replace("## 2. The detail", "## 3. The detail");
        Assert.Throws<FormatException>(() => ReadingList.Parse(broken, Grammar(language)));
    }

    [Theory]
    [InlineData("ro")]
    [InlineData("en")]
    public void APickToAMissingSourceIsRejected(string language)
    {
        var grammar = Grammar(language);
        var broken = Minimal(language).Replace($"({grammar.SourceWord} 2)", $"({grammar.SourceWord} 9)");
        var error = Assert.Throws<FormatException>(() => ReadingList.Parse(broken, grammar));
        Assert.Contains("Second pick", error.Message);
    }

    [Fact]
    public void MarkupEncodesTextAndKeepsCodeSpans()
    {
        Assert.Equal("a &lt; b <code>x&lt;y&gt;</code> ș", ReadingList.Markup("a < b `x<y>` ș").Value);
    }

    private static ReadingGrammar Grammar(string language)
    {
        return language switch
        {
            "ro" => ReadingGrammar.Romanian,
            "en" => ReadingGrammar.English,
            _ => throw new ArgumentOutOfRangeException(nameof(language), language, "no reading list in this language"),
        };
    }

    private static ReadingList Embedded(string language)
    {
        return ReadingList.Embedded($"readings.{language}.md", Grammar(language));
    }

    private static string Minimal(string language)
    {
        return File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", $"readings-minimal.{language}.md"));
    }

    private static string Resource(string name)
    {
        using var stream = typeof(ReadingList).Assembly.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>The list's language-free shape: every section, entry, pick and link, in order.</summary>
    private static List<string> Outline(ReadingList list)
    {
        var outline = new List<string> { $"start {list.StartHereIntro.Count} lineage-after {list.LineageAfterStage}" };
        outline.AddRange(list.Picks.Select(pick => $"pick {pick.Source}"));
        foreach (var stage in list.Stages)
        {
            outline.Add($"stage {stage.Number} intro {stage.Intro.Count}");
            outline.AddRange(stage.Sources.Select(source => $"source {source.Number} {source.Year} {source.Link}"));
        }
        foreach (var group in new[] { list.Lineage, list.Updates })
        {
            outline.Add($"group intro {group.Intro.Count}");
            outline.AddRange(group.Specs.Select(spec => $"rfc {spec.Rfc} {spec.Year} {spec.Version.Length > 0} {spec.Link}"));
        }
        return outline;
    }

    /// <summary>The key of every "- Key: value" line, with "##" for each entry heading, in order.</summary>
    private static List<string> Keys(string markdown)
    {
        return markdown.Split('\n')
            .Where(line => line.StartsWith("## ", StringComparison.Ordinal) || line.StartsWith("- ", StringComparison.Ordinal))
            .Select(line => line.StartsWith("## ", StringComparison.Ordinal) ? "##" : line[2..line.IndexOf(':')])
            .ToList();
    }
}
