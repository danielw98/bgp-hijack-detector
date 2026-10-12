namespace BGPWatch.Web.Localization;

/// <summary>A language the pages are written in.</summary>
public enum Language
{
    En,
    Ro,
}

/// <summary>The languages, their codes and the default one.</summary>
public static class Languages
{
    public const Language Default = Language.En;

    public static IReadOnlyList<Language> All { get; } = [Language.En, Language.Ro];

    public static string Code(this Language language)
    {
        return language switch
        {
            Language.En => "en",
            Language.Ro => "ro",
            _ => throw new ArgumentOutOfRangeException(nameof(language), language, "unknown language"),
        };
    }

    /// <summary>The language a code names ("en", "RO"), or null for any other text.</summary>
    public static Language? FromCode(string? code)
    {
        return All.Cast<Language?>().FirstOrDefault(l => string.Equals(l!.Value.Code(), code?.Trim(), StringComparison.OrdinalIgnoreCase));
    }
}
