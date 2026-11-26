using System.Text.Json;
using PersonalLibrary.Core;

namespace PersonalLibrary.Infrastructure;

public sealed record CitationIdentitySnapshot(Dictionary<string, CitationWorkIdentity> Entries, string? Hash);
public sealed partial class LibraryStore
{
    private string CitationIdentitiesPath => Path.Combine(Profile, "citation-work-identities.json");
    public CitationIdentitySnapshot CitationIdentities()
    {
        lock (gate)
        {
            if (!File.Exists(CitationIdentitiesPath)) return new(new(), null);
            var bytes = File.ReadAllBytes(CitationIdentitiesPath);
            return new(JsonSerializer.Deserialize<Dictionary<string, CitationWorkIdentity>>(bytes, Json) ?? new(), Hash(bytes));
        }
    }

    public EpubCitation[] ProcessCitations(Guid id, IEnumerable<EpubCitation> entries) =>
        ReviewedCitations(id, CitationProcessing.Process(entries, CitationIdentities().Entries));

    public void SaveCitationIdentity(Guid sourceId, EpubCitation citation, string title, string author, string? mergeIntoId, string? expectedHash, bool exclude = false)
    {
        title = title.Trim(); author = author.Trim();
        if (!exclude && (title.Length is < 2 or > 400 || author.Length is < 2 or > 200))
            throw new InvalidOperationException("Enter a work title and author/contributor. Keep uncertain references unresolved until you can identify them.");
        if (citation.Interpretation is null or { Resolution: CitationResolution.FullNote }) throw new InvalidOperationException("Select an individual reference, not the complete note.");
        lock (gate)
        {
            var fingerprint = CitationProvenance.Fingerprint(citation);
            var current = ReadCitationEvidence(sourceId).FirstOrDefault(entry => CitationProvenance.Fingerprint(entry) == fingerprint)
                ?? throw new InvalidOperationException("The source evidence changed. Reopen the processed citations before reviewing it.");
            var snapshot = CitationIdentities();
            if (snapshot.Hash != expectedHash) throw new InvalidOperationException("Work identities changed. Reopen this review before saving.");
            var key = CitationProcessing.IdentityKey(current);
            snapshot.Entries.TryGetValue(key, out var existing);
            var target = mergeIntoId == null ? existing : snapshot.Entries.Values.FirstOrDefault(value => value.Id == mergeIntoId)
                ?? throw new InvalidOperationException("The selected work identity is no longer available.");
            if (!exclude && mergeIntoId == null && snapshot.Entries.TryGetValue(CitationProcessing.WorkKey(title, author), out var collision) && collision.Id != target?.Id)
                throw new InvalidOperationException("That reviewed identity already exists. Choose it in the merge menu to combine the identities explicitly.");
            var identity = new CitationWorkIdentity(target?.Id ?? "reviewed:" + Guid.NewGuid().ToString("N"), title, author, exclude);
            // All aliases of the corrected identity follow it. An explicit merge
            // redirects the previous identity too; source texts remain untouched.
            foreach (var alias in snapshot.Entries.Where(pair => pair.Value.Id == identity.Id || existing != null && pair.Value.Id == existing.Id).Select(pair => pair.Key).ToArray())
                snapshot.Entries[alias] = identity;
            snapshot.Entries[key] = identity;
            if (!exclude) snapshot.Entries[CitationProcessing.WorkKey(title, author)] = identity;
            var before = File.Exists(CitationIdentitiesPath) ? File.ReadAllText(CitationIdentitiesPath) : null;
            var after = JsonSerializer.Serialize(snapshot.Entries, Json); if (before == after) return;
            var temp = CitationIdentitiesPath + ".tmp"; var replaced = false;
            try
            {
                File.WriteAllText(temp, after); File.Move(temp, CitationIdentitiesPath, true); replaced = true;
                RecordAttachmentChange(sourceId, "profile/citation-work-identities.json", before, after);
            }
            catch { if (replaced) { if (before == null) File.Delete(CitationIdentitiesPath); else File.WriteAllText(CitationIdentitiesPath, before); } throw; }
            finally { if (File.Exists(temp)) File.Delete(temp); }
            AutomaticBackup();
        }
    }
}
