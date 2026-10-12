using System.Text.RegularExpressions;

namespace BGPWatch.Web.Reading;

/// <summary>
/// The words of one language's reading list: the start-here title, the word that opens a stage
/// title, the title prefixes of the lineage and updates sections, the word of a pick's source
/// reference and the field keys. The Markdown structure around them is the same in every language.
/// </summary>
public sealed record ReadingGrammar(
    string StartHereTitle,
    string StageWord,
    string LineagePrefix,
    string UpdatesPrefix,
    string SourceWord,
    string YearField,
    string SummaryField,
    string LinkField,
    string VersionField,
    string RelationField,
    string ChangeField)
{
    /// <summary>The grammar of Reading/readings.ro.md.</summary>
    public static ReadingGrammar Romanian { get; } = new(
        StartHereTitle: "Pentru început",
        StageWord: "Etapa",
        LineagePrefix: "Genealogia",
        UpdatesPrefix: "Actualizările",
        SourceWord: "sursa",
        YearField: "An",
        SummaryField: "Pe scurt",
        LinkField: "Link",
        VersionField: "Versiune",
        RelationField: "Relația",
        ChangeField: "Ce s-a schimbat");

    /// <summary>The grammar of Reading/readings.en.md.</summary>
    public static ReadingGrammar English { get; } = new(
        StartHereTitle: "Start here",
        StageWord: "Stage",
        LineagePrefix: "Lineage",
        UpdatesPrefix: "Updates",
        SourceWord: "source",
        YearField: "Year",
        SummaryField: "Summary",
        LinkField: "Link",
        VersionField: "Version",
        RelationField: "Relation",
        ChangeField: "What changed");

    /// <summary>The pattern of a stage title "Stage N. Title": group 1 the number, group 2 the title.</summary>
    internal string StagePattern => $@"^{Regex.Escape(StageWord)} (\d+)\. (.+)$";

    /// <summary>The pattern of a pick line "N. Label (source K): note": groups label, source number, note.</summary>
    internal string PickPattern => $@"^\d+\. (.+?) \({Regex.Escape(SourceWord)} (\d+)\): (.+)$";
}
