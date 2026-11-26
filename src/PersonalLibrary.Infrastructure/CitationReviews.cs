using System.Text.Json;
using PersonalLibrary.Core;

namespace PersonalLibrary.Infrastructure;

public sealed record CitationReviewSnapshot(Dictionary<string, CitationAttribution> Entries, string? Hash);
public sealed partial class LibraryStore
{
    private string CitationReviewsPath(Guid id) => Path.Combine(Path.GetDirectoryName(BibliographyPath(id))!, "citation-reviews.json");
    public CitationReviewSnapshot CitationReviews(Guid id)
    {
        lock (gate)
        {
            var path = CitationReviewsPath(id); if (!File.Exists(path)) return new(new(), null);
            var bytes = File.ReadAllBytes(path);
            return new(JsonSerializer.Deserialize<Dictionary<string, CitationAttribution>>(bytes, Json) ?? new(), Hash(bytes));
        }
    }

    public EpubCitation[] ReviewedCitations(Guid id, IEnumerable<EpubCitation> citations)
    {
        var reviews = CitationReviews(id).Entries;
        return citations.Select(citation =>
        {
            if (reviews.TryGetValue(CitationProvenance.Fingerprint(citation), out var attribution)) return citation with { Attribution = attribution };
            // Preserve legacy reviews only for the exact same source revision,
            // note wording and extracted title/author. A newly split or changed
            // interpretation must not inherit an unrelated review.
            if (citation.Interpretation is { } info)
            {
                var legacy = citation with { Title = info.ExtractedTitle, Author = info.ExtractedAuthor, Interpretation = null };
                if (reviews.TryGetValue(CitationProvenance.Fingerprint(legacy), out attribution)) return citation with { Attribution = attribution };
            }
            return citation;
        }).ToArray();
    }

    public void SaveCitationReview(Guid id, EpubCitation citation, CitationAttribution attribution, string? expectedHash)
    {
        CitationProvenance.Validate(attribution);
        lock (gate)
        {
            if (FindBook(id) == null) throw new InvalidOperationException("The source book is no longer saved.");
            if (!ReadCitationEvidence(id).Any(current => CitationProvenance.Fingerprint(current) == CitationProvenance.Fingerprint(citation)))
                throw new InvalidOperationException("This citation or its source revision changed. Reopen the bibliography before reviewing it.");
            var snapshot = CitationReviews(id);
            if (snapshot.Hash != expectedHash) throw new InvalidOperationException("Citation reviews changed. Reopen this review before saving.");
            snapshot.Entries[CitationProvenance.Fingerprint(citation)] = attribution;
            var path = CitationReviewsPath(id); var before = File.Exists(path) ? File.ReadAllText(path) : null;
            var after = JsonSerializer.Serialize(snapshot.Entries, Json); if (before == after) return;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temp = path + ".tmp"; var replaced = false;
            try
            {
                File.WriteAllText(temp, after); File.Move(temp, path, true); replaced = true;
                RecordAttachmentChange(id, "citation-reviews.json", before, after);
            }
            catch { if (replaced) { if (before == null) File.Delete(path); else File.WriteAllText(path, before); } throw; }
            finally { if (File.Exists(temp)) File.Delete(temp); }
            AutomaticBackup();
        }
    }
}
