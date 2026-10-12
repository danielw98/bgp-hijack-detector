using BGPWatch.Web.Reading;

namespace BGPWatch.Tests;

/// <summary>The reading list behind the references page: the embedded Reading/lecturi-bgp.md and the parser's grammar.</summary>
public class ReadingListTests
{
    private static readonly string Minimal = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "lecturi-minimal.md"));

    [Fact]
    public void TheEmbeddedListNumbersItsSourcesInReadingOrder()
    {
        var list = ReadingList.Embedded();
        Assert.Equal(Enumerable.Range(1, list.Stages.Count), list.Stages.Select(stage => stage.Number));
        Assert.All(list.Sources, source => Assert.Equal("https", source.Link.Scheme));
        Assert.All(list.Picks, pick => Assert.Contains(list.Sources, source => source.Number == pick.Source));
    }

    [Fact]
    public void TheEmbeddedLineageRunsFromRfc1105ToRfc4271InYearOrder()
    {
        var lineage = ReadingList.Embedded().Lineage.Specs;
        Assert.Equal(1105, lineage[0].Rfc);
        Assert.Equal(4271, lineage[^1].Rfc);
        Assert.Equal(lineage.Select(spec => spec.Year).Order(), lineage.Select(spec => spec.Year));
        Assert.All(lineage, spec => Assert.NotEmpty(spec.Version));
    }

    [Fact]
    public void EveryEmbeddedUpdateNamesRfc4271InItsRelation()
    {
        var updates = ReadingList.Embedded().Updates.Specs;
        Assert.NotEmpty(updates);
        Assert.All(updates, spec => Assert.Contains("4271", spec.Relation));
    }

    [Fact]
    public void SectionsBecomePicksStagesAndSpecGroups()
    {
        var list = ReadingList.Parse(Minimal);
        Assert.Equal(["Two picks."], list.StartHereIntro);
        Assert.Equal(new Pick(2, "Second pick", "the detail."), list.Picks[1]);
        Assert.Equal(["Basics", "Detail"], list.Stages.Select(stage => stage.Title));
        Assert.Equal(["Stage intro."], list.Stages[1].Intro);
        Assert.Equal(1, list.LineageAfterStage);
        Assert.Equal((1105, "BGP-1", "1989"), (list.Lineage.Specs[0].Rfc, list.Lineage.Specs[0].Version, list.Lineage.Specs[0].Year));
        Assert.Equal("", list.Updates.Specs[0].Version);
    }

    [Fact]
    public void ASourceKeepsItsYearSummaryAndFirstLink()
    {
        var sources = ReadingList.Parse(Minimal).Sources.ToList();
        Assert.Equal(new Source(1, "An overview", "2024", "Read it first, with `code`.", new Uri("https://www.example.org/a")), sources[0]);
        Assert.Equal("example.org", sources[0].Host);
        Assert.Equal(new Uri("https://a.example/x"), sources[1].Link);
    }

    [Fact]
    public void ASourceWithoutASummaryIsRejected()
    {
        var broken = Minimal.Replace("- Pe scurt: Then this.\n", "");
        var error = Assert.Throws<FormatException>(() => ReadingList.Parse(broken));
        Assert.Contains("\"2. The detail\" has no \"Pe scurt\"", error.Message);
    }

    [Fact]
    public void SourcesOutOfOrderAreRejected()
    {
        var broken = Minimal.Replace("## 2. The detail", "## 3. The detail");
        Assert.Throws<FormatException>(() => ReadingList.Parse(broken));
    }

    [Fact]
    public void APickToAMissingSourceIsRejected()
    {
        var broken = Minimal.Replace("(sursa 2)", "(sursa 9)");
        var error = Assert.Throws<FormatException>(() => ReadingList.Parse(broken));
        Assert.Contains("Second pick", error.Message);
    }

    [Fact]
    public void MarkupEncodesTextAndKeepsCodeSpans()
    {
        Assert.Equal("a &lt; b <code>x&lt;y&gt;</code> ș", ReadingList.Markup("a < b `x<y>` ș").Value);
    }
}
