using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PersonalLibrary.Core;

public enum CitationRole { Unreviewed, OriginalText, Editorial, FurtherReading }
public sealed record CitationAttribution(CitationRole Role, string Contributor = "", int? SourceEditionYear = null,
    int? CitedOriginalYear = null, int? CitedEditionYear = null, bool Reviewed = false);
public sealed record CitationAssessment(CitationAttribution Attribution, int? SourceOriginalYear, int? CitedOriginalYear,
    int? ReferenceYear, string Warning)
{
    public bool EligibleOriginalText => Attribution is { Role: CitationRole.OriginalText, Reviewed: true } && Warning.Length == 0;
}

public static class CitationProvenance
{
    public static string Fingerprint(EpubCitation citation) => citation.Interpretation?.EvidenceFingerprint is { Length: > 0 } fingerprint ? fingerprint : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        JsonSerializer.Serialize(new[] { citation.EvidenceSource, citation.Section, citation.Text, citation.Interpretation?.ExtractedTitle ?? citation.Title,
            citation.Interpretation?.ExtractedAuthor ?? citation.Author, citation.ArchiveEntry, citation.Anchor ?? "" }))));

    public static string Label(CitationAttribution attribution) => (attribution.Role switch
    {
        CitationRole.OriginalText => "Original-text citation", CitationRole.Editorial => "Editorial / introduction / translator material",
        CitationRole.FurtherReading => "Suggested / further reading", _ => "Attribution unreviewed"
    }) + (attribution.Reviewed ? " · reviewed" : attribution.Role != CitationRole.Unreviewed ? " · inferred from section" : "");

    public static CitationAssessment Assess(Book source, EpubCitation citation, Book? target = null)
    {
        var attribution = citation.Attribution ?? Infer(citation.Section);
        var original = attribution.CitedOriginalYear ?? target?.OriginalYear;
        var years = Regex.Matches(citation.Text, @"\b(?:1[4-9]\d{2}|20\d{2})\b").Select(match => int.Parse(match.Value, System.Globalization.CultureInfo.InvariantCulture)).Distinct().ToArray();
        var reference = attribution.CitedEditionYear ?? citation.Interpretation?.EditionYear ?? (years.Length == 1 ? years[0] : (int?)null);
        var contextYear = attribution.Role is CitationRole.Editorial or CitationRole.FurtherReading ? attribution.SourceEditionYear : source.OriginalYear;
        var warning = "";
        if (original is { } cited && contextYear is { } sourceYear && cited > sourceYear)
            warning = $"Chronology needs review: cited work's original year ({cited}) is later than the source {(attribution.Role is CitationRole.Editorial or CitationRole.FurtherReading ? "edition" : "original work")} ({sourceYear}). Check attribution and dates.";
        else if (original == null && reference is { } printed && contextYear is { } sourceDate && printed > sourceDate)
            warning = $"Chronology needs review: reference/edition year {printed} is later than source year {sourceDate}. This may be a later edition of an older cited work or later editorial material; supply the cited original year and review attribution.";
        return new(attribution, source.OriginalYear, original, reference, warning);
    }

    public static CitationAttribution Infer(string section)
    {
        if (Regex.IsMatch(section, @"further reading|suggested reading|recommended reading|guide to.*reading", RegexOptions.IgnoreCase))
            return new(CitationRole.FurtherReading);
        if (Regex.IsMatch(section, @"introduction|editorial|translator|editor's|editor’s", RegexOptions.IgnoreCase))
            return new(CitationRole.Editorial);
        return new(CitationRole.Unreviewed);
    }

    public static void Validate(CitationAttribution attribution)
    {
        if (!Enum.IsDefined(attribution.Role)) throw new InvalidOperationException("Choose an attribution type.");
        if (new[] { attribution.SourceEditionYear, attribution.CitedOriginalYear, attribution.CitedEditionYear }.Any(year => year == 0 || year is < -9999 or > 9999))
            throw new InvalidOperationException("Use a year from -9999 to 9999, excluding zero, or leave it blank.");
    }
}
