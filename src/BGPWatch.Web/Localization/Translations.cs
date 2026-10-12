using System.Reflection;
using System.Text.Json;

namespace BGPWatch.Web.Localization;

/// <summary>
/// The texts of the pages in every language: one flat JSON object per language, key to
/// text, embedded in the assembly as strings.{code}.json. Keys under "js." are the
/// browser's: the layout hands them to the page modules without the prefix. A text may
/// hold {name} placeholders and, for the keys a page renders as HTML, trusted markup.
/// </summary>
public sealed class Translations
{
    public const string ClientPrefix = "js.";

    private readonly IReadOnlyDictionary<Language, IReadOnlyDictionary<string, string>> texts;
    private readonly IReadOnlyDictionary<Language, IReadOnlyDictionary<string, string>> client;

    public Translations(IReadOnlyDictionary<Language, IReadOnlyDictionary<string, string>> texts)
    {
        var missing = Languages.All.Where(l => !texts.ContainsKey(l)).ToList();
        if (missing.Count > 0)
        {
            throw new ArgumentException($"no texts for {string.Join(", ", missing)}", nameof(texts));
        }
        this.texts = texts;
        client = texts.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyDictionary<string, string>)pair.Value
                .Where(text => text.Key.StartsWith(ClientPrefix, StringComparison.Ordinal))
                .ToDictionary(text => text.Key[ClientPrefix.Length..], text => text.Value));
    }

    public static Translations Embedded()
    {
        var assembly = Assembly.GetExecutingAssembly();
        return new Translations(Languages.All.ToDictionary(l => l, l => Parse(Read(assembly, $"strings.{l.Code()}.json"))));
    }

    /// <summary>One language's file: a flat JSON object of string values.</summary>
    public static IReadOnlyDictionary<string, string> Parse(string json)
    {
        return JsonSerializer.Deserialize<Dictionary<string, string>>(json)
            ?? throw new FormatException("a strings file must be a JSON object");
    }

    public IReadOnlyCollection<string> Keys(Language language)
    {
        return texts[language].Keys.ToList();
    }

    public string Text(Language language, string key)
    {
        return texts[language].TryGetValue(key, out var text)
            ? text
            : throw new KeyNotFoundException($"no {language.Code()} text for \"{key}\"");
    }

    public IReadOnlyDictionary<string, string> Client(Language language)
    {
        return client[language];
    }

    private static string Read(Assembly assembly, string name)
    {
        using var stream = assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"missing embedded resource {name}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
