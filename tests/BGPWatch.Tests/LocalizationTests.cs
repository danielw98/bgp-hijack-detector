using System.Text.RegularExpressions;
using BGPWatch.Web.Localization;
using Microsoft.AspNetCore.Http;

namespace BGPWatch.Tests;

/// <summary>The texts of the pages: both languages complete and aligned, every key the pages and modules ask for present, the language choice, and the helpers.</summary>
public partial class LocalizationTests
{
    private static readonly Translations Embedded = Translations.Embedded();

    private static string Web(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "BGPWatch.slnx")))
        {
            dir = dir.Parent;
        }
        return Path.Combine([dir?.FullName ?? throw new DirectoryNotFoundException("BGPWatch.slnx"), "src", "BGPWatch.Web", .. parts]);
    }

    [Fact]
    public void BothLanguagesHoldTheSameKeys()
    {
        Assert.Equal(Embedded.Keys(Language.En).Order(), Embedded.Keys(Language.Ro).Order());
    }

    [Fact]
    public void BothLanguagesUseTheSamePlaceholdersPerKey()
    {
        var differ = Embedded.Keys(Language.En)
            .Where(key => !Placeholders(Embedded.Text(Language.En, key)).SetEquals(Placeholders(Embedded.Text(Language.Ro, key))))
            .ToList();
        Assert.Empty(differ);
    }

    [Fact]
    public void NoTextIsEmpty()
    {
        var empty = Languages.All.SelectMany(l => Embedded.Keys(l).Where(k => Embedded.Text(l, k).Trim().Length == 0).Select(k => $"{l.Code()} {k}"));
        Assert.Empty(empty);
    }

    [Fact]
    public void EveryKeyThePagesAskForExists()
    {
        var used = Directory.EnumerateFiles(Web("Pages"), "*.cshtml", SearchOption.AllDirectories)
            .SelectMany(file => PageKey().Matches(File.ReadAllText(file)).Select(m => m.Groups[1].Value)
                .Concat(PageCount().Matches(File.ReadAllText(file)).SelectMany(m => new[] { $"{m.Groups[1].Value}.one", $"{m.Groups[1].Value}.many" })))
            .ToHashSet();
        Assert.NotEmpty(used);
        Assert.Equal("", string.Join(", ", used.Where(key => !Embedded.Keys(Language.En).Contains(key)).Order()));
    }

    [Fact]
    public void EveryKeyTheModulesAskForExists()
    {
        var sources = Directory.EnumerateFiles(Web("wwwroot", "static"), "*.js").Select(File.ReadAllText).ToList();
        var terms = TermKeys().Match(sources.Single(s => s.Contains("TERM_KEYS = [", StringComparison.Ordinal))).Groups[1].Value
            .Split(',').Select(k => k.Trim().Trim('"'));
        var used = sources.SelectMany(s => ModuleKey().Matches(s).Select(m => m.Groups[1].Value)
                .Concat(ModuleCount().Matches(s).SelectMany(m => new[] { $"{m.Groups[1].Value}.one", $"{m.Groups[1].Value}.many" })))
            .Concat(terms.SelectMany(k => new[] { $"term.{k}.title", $"term.{k}.body" }))
            .Concat(["hop.announce", "hop.withdraw"])
            .Concat(new[] { "valid", "invalid", "not-found", "unknown" }.SelectMany(s => new[] { $"rpki.sentence.{s}", $"rpki.meaning.{s}" }))
            .Concat(from verdict in new[] { "hijacked", "legit" } from noun in new[] { "peer", "router" } from n in new[] { "one", "many" } select $"prefix.{verdict}.{noun}.{n}")
            .ToHashSet();
        Assert.Equal("", string.Join(", ", used.Where(key => !Embedded.Client(Language.En).ContainsKey(key)).Order()));
    }

    [Fact]
    public void TheBrowserGetsOnlyTheClientKeysWithoutTheirPrefix()
    {
        var client = Embedded.Client(Language.Ro);
        Assert.Equal("Victimă", client["role.victim"]);
        Assert.DoesNotContain("brand", client.Keys);
    }

    [Fact]
    public void AMissingKeyIsAnError()
    {
        Assert.Throws<KeyNotFoundException>(() => Embedded.Text(Language.En, "no.such.key"));
    }

    [Fact]
    public void ALanguageWithoutTextsIsRefused()
    {
        Assert.Throws<ArgumentException>(() => new Translations(new Dictionary<Language, IReadOnlyDictionary<string, string>> { [Language.En] = new Dictionary<string, string>() }));
    }

    [Theory]
    [InlineData("en", Language.En)]
    [InlineData("RO", Language.Ro)]
    [InlineData(" ro ", Language.Ro)]
    public void ACodeNamesItsLanguage(string code, Language language)
    {
        Assert.Equal(language, Languages.FromCode(code));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("fr")]
    public void AnyOtherCodeNamesNoLanguage(string? code)
    {
        Assert.Null(Languages.FromCode(code));
    }

    [Theory]
    [InlineData(1, "o sursă")]
    [InlineData(19, "19 surse")]
    [InlineData(20, "20 de surse")]
    [InlineData(101, "101 surse")]
    [InlineData(119, "119 surse")]
    [InlineData(120, "120 de surse")]
    [InlineData(200, "200 de surse")]
    public void RomanianCountsPutDeFromTwenty(int count, string text)
    {
        Assert.Equal(text, Translator(Language.Ro).Count(count, "refs.sources"));
    }

    [Theory]
    [InlineData(1, "one source")]
    [InlineData(44, "44 sources")]
    public void EnglishCountsHaveNoDe(int count, string text)
    {
        Assert.Equal(text, Translator(Language.En).Count(count, "refs.sources"));
    }

    [Fact]
    public void FormatFillsEveryPlaceholder()
    {
        Assert.Equal("Stage 3", Translator(Language.En).Format("refs.stage", ("n", 3)));
    }

    [Fact]
    public void HtmlPointsAppLinksAtTheBasePath()
    {
        var html = Translator(Language.En, "/bgp").Html("guide.intro.map").ToString();
        Assert.Contains("href=\"/bgp/references\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("~/", html, StringComparison.Ordinal);
    }

    [Fact]
    public void TheClientJsonCannotCloseItsScriptElement()
    {
        var texts = new Dictionary<Language, IReadOnlyDictionary<string, string>>
        {
            [Language.En] = new Dictionary<string, string> { ["js.x"] = "</script>" },
            [Language.Ro] = new Dictionary<string, string> { ["js.x"] = "" },
        };
        Assert.DoesNotContain("</", new Translator(new Translations(texts), Language.En, "").ClientJson(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task WithoutAChoiceTheLanguageIsEnglish()
    {
        Assert.Equal(Language.En, await ChosenAsync(new DefaultHttpContext()));
    }

    [Fact]
    public async Task TheQueryChoosesTheLanguageAndStoresIt()
    {
        var http = new DefaultHttpContext();
        http.Request.QueryString = new QueryString("?lang=ro");
        Assert.Equal(Language.Ro, await ChosenAsync(http));
        Assert.Contains("lang=ro", http.Response.Headers.SetCookie.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheCookieKeepsTheChoice()
    {
        var http = new DefaultHttpContext();
        http.Request.Headers.Cookie = "lang=ro";
        Assert.Equal(Language.Ro, await ChosenAsync(http));
        Assert.Empty(http.Response.Headers.SetCookie.ToString());
    }

    [Fact]
    public async Task AnUnknownChoiceFallsBackToTheCookieOrEnglish()
    {
        var http = new DefaultHttpContext();
        http.Request.QueryString = new QueryString("?lang=xx");
        Assert.Equal(Language.En, await ChosenAsync(http));
    }

    private static Translator Translator(Language language, string pathBase = "")
    {
        return new Translator(Embedded, language, pathBase);
    }

    private static async Task<Language> ChosenAsync(HttpContext http)
    {
        await LanguageChoice.Middleware(http, _ => Task.CompletedTask);
        return LanguageChoice.Of(http);
    }

    private static HashSet<string> Placeholders(string text)
    {
        return Placeholder().Matches(text).Select(m => m.Value).ToHashSet();
    }

    [GeneratedRegex(@"\{\w+\}")]
    private static partial Regex Placeholder();

    [GeneratedRegex(@"T(?:\[|\.Html\(|\.Format\()""([^""]+)""")]
    private static partial Regex PageKey();

    [GeneratedRegex(@"T\.Count\([^,]+, ""([^""]+)""\)")]
    private static partial Regex PageCount();

    [GeneratedRegex(@"\bt\(""([^""$]+)""")]
    private static partial Regex ModuleKey();

    [GeneratedRegex(@"\bcount\([^,]+, ""([^""]+)""\)")]
    private static partial Regex ModuleCount();

    [GeneratedRegex(@"TERM_KEYS = \[([^\]]+)\]")]
    private static partial Regex TermKeys();
}
