using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Html;

namespace BGPWatch.Web.Localization;

/// <summary>
/// The texts of one request, in the language it asked for. Pages write T["key"] for plain
/// text, T.Html("key") for a text with markup, where "~/" in an attribute becomes the
/// app's base path, and T.Format("key", ("name", value)) to fill {name} placeholders.
/// </summary>
public sealed partial class Translator(Translations translations, Language language, string pathBase)
{
    public Language Language => language;

    public string Code => language.Code();

    /// <summary>The language a toggle offers: the other one.</summary>
    public Language Other => language == Language.En ? Language.Ro : Language.En;

    public string this[string key] => translations.Text(language, key);

    public IHtmlContent Html(string key)
    {
        return new HtmlString(AppRelative().Replace(this[key], $"$1{pathBase}/"));
    }

    public string Format(string key, params (string Name, object Value)[] values)
    {
        return values.Aggregate(this[key], (text, v) => text.Replace($"{{{v.Name}}}", Convert.ToString(v.Value, System.Globalization.CultureInfo.InvariantCulture)));
    }

    /// <summary>
    /// A count with its noun from "{key}.one" and "{key}.many". From 20 on, Romanian puts
    /// "de" between them (51 de surse), except after 01-19 in the last two digits (101 surse).
    /// </summary>
    public string Count(int count, string key)
    {
        if (count == 1)
        {
            return this[$"{key}.one"];
        }
        var de = language == Language.Ro && count >= 20 && count % 100 is 0 or >= 20 ? "de " : "";
        return $"{count} {de}{this[$"{key}.many"]}";
    }

    /// <summary>The browser's texts as a JSON object, safe inside a script element.</summary>
    public string ClientJson()
    {
        return JsonSerializer.Serialize(translations.Client(language)).Replace("</", "<\\/");
    }

    [GeneratedRegex("((?:href|src)=\")~/")]
    private static partial Regex AppRelative();
}
