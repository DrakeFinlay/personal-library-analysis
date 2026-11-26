using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace PersonalLibrary.Core;

public enum CitationResolution { FullReference, ShortReference, Ibid, NeedsReview, LibrarySuggestion, FullNote }
public sealed record CitationInterpretation(string WorkKey, CitationResolution Resolution, string Reference,
    int? NoteNumber = null, string Locator = "", string Volume = "", int? EditionYear = null,
    string Contributor = "", string Reason = "", string ExtractedTitle = "", string ExtractedAuthor = "", string CanonicalWorkKey = "", bool IdentityReviewed = false, string EvidenceFingerprint = "", bool Excluded = false)
{
    public bool CanConnect => Resolution is CitationResolution.FullReference or CitationResolution.ShortReference or CitationResolution.Ibid;
}
public sealed record CitationWorkIdentity(string Id, string Title, string Author, bool Excluded = false);

public static class CitationProcessing
{
    public static string Label(CitationInterpretation info) => (info.Excluded ? "Excluded work link" : info.Resolution switch
    {
        CitationResolution.FullReference => "Identified reference", CitationResolution.ShortReference => "Resolved short reference",
        CitationResolution.Ibid => "Resolved Ibid.", CitationResolution.LibrarySuggestion => "Possible library match · needs review",
        CitationResolution.FullNote => "Complete note", _ => "Unresolved reference · needs review"
    }) + (info.IdentityReviewed ? " · identity reviewed" : info.CanConnect ? " · identity inferred" : "");
    private static readonly Regex Publication = new(@"\((?<publication>(?:[^()]|\([^()]*\)){3,350}?\b(?<year>1[4-9]\d{2}|20\d{2})\b[^()]{0,60})\)", RegexOptions.CultureInvariant);
    private static readonly Regex Number = new(@"^\s*(?<number>\d{1,3})[.)]?\s+(?=\p{L})");
    private static readonly Regex Heading = new(@"^(?:Chapter\s+[\dIVXL]|Notes$|Bibliography$|References$)", RegexOptions.IgnoreCase);

    public static string WorkKey(string title, string author) => "work:" + Hash(Compact(title) + "|" + string.Join("|",
        Regex.Split(author, @"\s+(?:and|&)\s+|;").Select(name => Compact(name.Contains(',') ? string.Join(" ", name.Split(',').Reverse()) : name)).Order(StringComparer.Ordinal)));
    private static string Compact(string value) => CitedRecommendations.TextKey(value).Replace(" ", "");
    private static string Creator(string value) => string.Join("|", Regex.Split(value, @"\s+(?:and|&)\s+|;").Select(CitedRecommendations.Surname).Where(s => s.Length > 0).Order(StringComparer.Ordinal));
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    public static string IdentityKey(EpubCitation citation) => citation.Interpretation is { WorkKey.Length: > 0 } info ? info.WorkKey : "mention:" + CitationProvenance.Fingerprint(citation);

    public static EpubCitation[] ReadText(string text, IReadOnlyDictionary<string, CitationWorkIdentity>? identities = null, string evidenceSource = "")
    {
        var inputs = new List<EpubCitation>(); var section = "Pasted or scanned notes"; var source = "bibliography.md";
        var separated = Regex.Replace(text.Replace("\r", ""), @"\n(?=[\p{L}’'\- ]{2,60},\s+[\p{L}])", "\n\n");
        foreach (var part in Regex.Split(separated, @"\n\s*\n|\n(?=\d{1,3}[.)]?\s+\p{L})"))
        {
            var block = part.Trim();
            if (block.StartsWith("## Photo:", StringComparison.Ordinal)) { source = block.TrimStart('#', ' '); continue; }
            if (Heading.IsMatch(block)) { section = block; continue; }
            if (block.Length == 0 || block.StartsWith('#') || block.StartsWith('>')) continue;
            inputs.Add(new(inputs.Count + 1, section, block, "", "", source, null, EvidenceSource: evidenceSource));
        }
        return Process(inputs, identities);
    }

    public static EpubCitation[] Process(IEnumerable<EpubCitation> inputs, IReadOnlyDictionary<string, CitationWorkIdentity>? identities = null)
    {
        EpubCitation Create(EpubCitation input, string title, string author, CitationInterpretation info) => ApplyIdentity(Make(input, title, author, info), identities);
        var output = new List<EpubCitation>(); var known = new List<EpubCitation>(); EpubCitation[] previous = [];
        int? previousNumber = null; string? context = null; string? revision = null;
        foreach (var input in inputs)
        {
            if (input.Interpretation != null) { output.Add(input); continue; }
            if (context != input.Section || revision != input.EvidenceSource) { known.Clear(); previous = []; previousNumber = null; context = input.Section; revision = input.EvidenceSource; }
            var match = Number.Match(input.Text); int? note = match.Success ? int.Parse(match.Groups["number"].Value) : null;
            if (note != null && previousNumber != null && note <= previousNumber) { known.Clear(); previous = []; previousNumber = null; }
            if (input.Title.Length > 0)
            {
                var explicitEntry = Create(input, input.Title, input.Author, new(WorkKey(input.Title, input.Author), CitationResolution.FullReference,
                    input.Text, note, Reason: "Structured source title; identity remains inferred.", ExtractedTitle: input.Title, ExtractedAuthor: input.Author));
                output.Add(explicitEntry); known.Add(explicitEntry); previous = [explicitEntry]; previousNumber = note; continue;
            }
            var body = Strip(Regex.Replace(input.Text, @"\s+", " ")); var found = new List<EpubCitation>(); var consumed = new List<(int Start, int End)>(); var end = 0;
            foreach (Match publication in Publication.Matches(body))
            {
                var prefix = body[end..publication.Index]; var start = end; end = publication.Index + publication.Length;
                // A locator followed by another sentence is a new reference, not
                // part of the preceding title (e.g. "444. Marvin Meyer, ...").
                var pieces = Regex.Split(prefix, @"(?<=\d)\.\s+(?=\p{Lu})|;\s*");
                var candidate = Strip(pieces[^1]); start += prefix.LastIndexOf(pieces[^1], StringComparison.Ordinal);
                var comma = candidate.IndexOf(',');
                if (comma < 2) continue;
                var author = candidate[..comma].Trim(); var title = candidate[(comma + 1)..].Trim();
                var inverted = Regex.Match(candidate, @"^(?<author>[\p{L}’'\- ]+,\s+[\p{L}’'\- ]+)\.\s+(?<title>.+)$");
                if (inverted.Success) { author = inverted.Groups["author"].Value.Trim(); title = inverted.Groups["title"].Value.Trim(); }
                var contributor = Regex.Match(title, @",\s*trans\.\s*(?<value>.+)$", RegexOptions.IgnoreCase).Groups["value"].Value.Trim();
                var volume = Regex.Match(title, @",\s*vol\.?\s*(?<value>.+?)(?:,\s*trans\.|$)", RegexOptions.IgnoreCase).Groups["value"].Value.Trim();
                title = Regex.Split(title, @",\s*(?:vol\.?\s|trans\.\s)", RegexOptions.IgnoreCase)[0];
                title = Regex.Replace(title, @"\s+Trans\.?$", "", RegexOptions.IgnoreCase).Trim(' ', '.', '*', '_');
                if (!Plausible(author, title)) continue;
                var locator = Regex.Match(body[end..], @"^\s*,\s*(?<value>\d[\d.,–—\- ]*)").Groups["value"].Value.Trim(' ', '.', ',');
                var reference = body[start..end];
                var citation = Create(input, title, author, new(WorkKey(title, author), CitationResolution.FullReference, reference, note, locator, volume,
                    int.Parse(publication.Groups["year"].Value), contributor, "Publication details identify a work; identity remains inferred.", title, author));
                found.Add(citation); known.Add(citation); consumed.Add((start, end + (locator.Length > 0 ? body[end..].IndexOf(locator, StringComparison.Ordinal) + locator.Length : 0)));
            }
            if (found.Count == 0)
            {
                var marked = CitedRecommendations.ReadText(body).ToArray();
                if (marked.Length == 0)
                {
                    var printed = Regex.Match(body, @"^(?<author>[\p{L}’'\- ]+,\s+[\p{L}’'\- ]+)\.\s+(?<title>[^.?!“""\r\n]+)[.!?]\s+.{3,200}\b(?:1[4-9]\d{2}|20\d{2})\b");
                    if (printed.Success) marked = [input with { Title = printed.Groups["title"].Value.Trim(), Author = printed.Groups["author"].Value.Trim() }];
                }
                foreach (var entry in marked.Where(entry => Plausible(entry.Author, entry.Title)))
                {
                    var citation = Create(input, entry.Title, entry.Author, new(WorkKey(entry.Title, entry.Author), CitationResolution.FullReference,
                        body, note, Reason: "Formatted bibliography entry; identity remains inferred.", ExtractedTitle: entry.Title, ExtractedAuthor: entry.Author));
                    found.Add(citation); known.Add(citation); consumed.Add((0, body.Length));
                }
            }
            // Interpret the remaining short-reference segments with ordered context.
            var remaining = body.ToCharArray(); foreach (var span in consumed) for (var i = span.Start; i < Math.Min(span.End, remaining.Length); i++) remaining[i] = ' ';
            foreach (var part in Regex.Split(new string(remaining), @";\s*|(?<=\d)\.\s+(?=\p{Lu})"))
            {
                var value = Strip(part); if (value.Length == 0) continue;
                if (Regex.IsMatch(value, @"^Ibid\b", RegexOptions.IgnoreCase))
                {
                    var same = previous.Where(c => c.Interpretation?.CanConnect == true).DistinctBy(c => c.Interpretation!.WorkKey).ToArray();
                    var consecutive = note != null && previousNumber != null && note == previousNumber + 1;
                    var locator = Regex.Match(value, @"^Ibid\.?\s*,?\s*(?<value>\d[\d.,–—\- ]*)", RegexOptions.IgnoreCase).Groups["value"].Value.Trim(' ', '.');
                    var resolved = consecutive && previous.Length == 1 && same.Length == 1 && previous.All(c => c.Interpretation?.CanConnect == true);
                    var ibidPrior = resolved ? same[0] : null;
                    found.Add(Create(input, ibidPrior?.Title ?? "", ibidPrior?.Author ?? "", new(ibidPrior?.Interpretation?.WorkKey ?? "", resolved ? CitationResolution.Ibid : CitationResolution.NeedsReview,
                        value, note, locator.Length > 0 ? locator : ibidPrior?.Interpretation?.Locator ?? "", ibidPrior?.Interpretation?.Volume ?? "", ibidPrior?.Interpretation?.EditionYear,
                        Reason: resolved ? "Ibid. resolves to the single work in the immediately preceding numbered note." : "Ibid. is unresolved: missing note, ambiguous previous note, or no identified preceding work.",
                        CanonicalWorkKey: ibidPrior?.Interpretation?.CanonicalWorkKey ?? "", IdentityReviewed: ibidPrior?.Interpretation?.IdentityReviewed == true)));
                    continue;
                }
                var shortMatch = Regex.Match(value, @"^(?<author>[^,]{2,100}),\s*(?<title>.+?)(?:,\s*|\s+)(?<locator>\d[\d.,:–—\- ]*)[.]?$", RegexOptions.CultureInvariant);
                if (!shortMatch.Success) continue;
                var author = shortMatch.Groups["author"].Value.Trim(); var title = shortMatch.Groups["title"].Value.Trim(' ', '.', '*', '_');
                if (!Plausible(author, title)) continue;
                var matching = known.Where(c => Creator(c.Author) == Creator(author) && (Compact(c.Title).StartsWith(Compact(title), StringComparison.Ordinal) ||
                    Compact(c.Interpretation!.ExtractedTitle).StartsWith(Compact(title), StringComparison.Ordinal)))
                    .ToArray();
                var matches = matching.DistinctBy(c => c.Interpretation!.CanonicalWorkKey.Length > 0 ? c.Interpretation.CanonicalWorkKey : c.Interpretation.WorkKey).ToArray();
                var resolvedShort = matches.Length == 1 && Compact(title).Length >= 4;
                var prior = resolvedShort ? matches[0] : null;
                found.Add(Create(input, prior?.Title ?? title, prior?.Author ?? author, new(prior?.Interpretation?.WorkKey ?? "", resolvedShort ? CitationResolution.ShortReference : CitationResolution.NeedsReview,
                    value, note, shortMatch.Groups["locator"].Value.Trim(' ', '.'), EditionYear: matching.Select(c => c.Interpretation!.EditionYear).Distinct().Count() == 1 ? prior?.Interpretation?.EditionYear : null,
                    Reason: resolvedShort ? "Short title and author resolve to one earlier full reference in this section." : "Short reference has no unique earlier full reference in this section.", ExtractedTitle: title, ExtractedAuthor: author)));
            }
            if (found.Count == 0)
                found.Add(Create(input, "", "", new("", CitationResolution.NeedsReview, body, note,
                    Reason: "No supported work reference was identified in this block. Identify it only if the source supports a work, or exclude the interpretation.")));
            output.AddRange(found);
            // Keep the full original block even when interpretation is partial.
            if (note != null || input.ArchiveEntry != "bibliography.md" && input.Title.Length == 0 && input.Text.Count(char.IsLetter) > 25 || found.Count == 0)
                output.Add(Make(input, "", "", new("", CitationResolution.FullNote, input.Text, note, Reason: "Complete source text; not a work node.")) with { Section = input.Section + " · Full note (review)" });
            previous = found.ToArray(); previousNumber = note;
        }
        return output.Select((citation, index) => citation with { Number = index + 1 }).ToArray();
    }

    public static EpubCitation ApplyIdentity(EpubCitation citation, IReadOnlyDictionary<string, CitationWorkIdentity>? identities)
    {
        if (citation.Interpretation is not { } info || info.Resolution == CitationResolution.FullNote || identities == null || !identities.TryGetValue(IdentityKey(citation), out var identity)) return citation;
        if (info.IdentityReviewed && info.CanonicalWorkKey == identity.Id && info.Excluded == identity.Excluded && citation.Title == identity.Title && citation.Author == identity.Author) return citation;
        return citation with { Title = identity.Title, Author = identity.Author, Interpretation = info with
        { CanonicalWorkKey = identity.Id, IdentityReviewed = true, Excluded = identity.Excluded,
            Resolution = identity.Excluded ? CitationResolution.NeedsReview : info.CanConnect ? info.Resolution : CitationResolution.FullReference,
            Reason = (identity.Excluded ? "Excluded from work links after review. " : "Work identity reviewed. ") + "Original source wording is preserved." } };
    }

    private static EpubCitation Make(EpubCitation source, string title, string author, CitationInterpretation info) => source with
    { Title = title, Author = author, Interpretation = info with { ExtractedTitle = info.ExtractedTitle.Length > 0 ? info.ExtractedTitle : title,
        ExtractedAuthor = info.ExtractedAuthor.Length > 0 ? info.ExtractedAuthor : author,
        EvidenceFingerprint = CitationProvenance.Fingerprint(source.Title.Length > 0 ? source : source with { Anchor = (source.Anchor ?? "") + "|reference:" + info.Reference }) } };

    private static string Strip(string value)
    {
        value = Number.Replace(value.Trim(' ', '.', ';', '\n'), "");
        value = Regex.Replace(value, @"^[Il]\s+(?=See\b)", "");
        value = Regex.Replace(value, @"^(?:Greek Manuscript [^,]+,\s*)?(?:For discussion,\s*)?(?:(?:See(?:,\s*e\.g\.,)?|See also|Cf\.|Translation in|Trans\.)\s+)+", "", RegexOptions.IgnoreCase);
        // "see also" must be consumed as one cue rather than leaving "also" as an author.
        return Regex.Replace(value, @"^also\s+", "", RegexOptions.IgnoreCase).Trim();
    }

    private static bool Plausible(string author, string title) => author.Length is >= 2 and <= 100 && title.Length is >= 3 and <= 300 &&
        !Regex.IsMatch(author, @"\d|\b(?:discussion|translation|trans|see|ibid|chapter|manuscript)\b", RegexOptions.IgnoreCase) &&
        !Regex.IsMatch(title, @"[“‘\""].+?[”’\""].*\bin\b|\d{2,}\.\s+\p{Lu}|https?://", RegexOptions.IgnoreCase) &&
        !title.StartsWith('“') && !title.StartsWith('‘') && !title.StartsWith('"');
}
