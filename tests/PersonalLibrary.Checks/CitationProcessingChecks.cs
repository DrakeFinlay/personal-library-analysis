using PersonalLibrary.Core;
using PersonalLibrary.Infrastructure;

internal static class CitationProcessingChecks
{
    public static void Run(string workspace, Action<bool, string> check, Action<Action, string> reject)
    {
        const string full = "1 See A. Writer, A Fictional Coast: An Introduction (Town: First Press, 2020). Translation in B. Reader, Collected Fictional Texts, vol. 1, trans. C. Translator (Town: Second Press, 2021), 444.";
        var parsed = CitationProcessing.ReadText(full);
        var works = parsed.Where(c => c.Interpretation?.CanConnect == true).ToArray();
        check(works.Length == 2 && works[0].Title == "A Fictional Coast: An Introduction" && works[1].Title == "Collected Fictional Texts" && works[1].Author == "B. Reader",
            "A mixed note splits full references separated by translation prose into independent works without a library");
        var inverted = CitationProcessing.ReadText("Writer, Alice. The Fictional Coast (Town: Press, 2020).").First(c => c.Interpretation?.CanConnect == true);
        check(inverted.Author == "Writer, Alice" && inverted.Title == "The Fictional Coast" && inverted.Interpretation!.WorkKey == CitationProcessing.WorkKey(inverted.Title, "Alice Writer"),
            "Inverted bibliography names are separated from titles and share identities with normal author-name order");
        check(works[1].Interpretation is { NoteNumber: 1, Locator: "444", Volume: "1", Contributor: "C. Translator", EditionYear: 2021 } && works.All(c => c.Text == full),
            "Interpretation retains note numbers, edition years, volumes, translators, locators and complete original evidence separately");
        var mixed = CitationProcessing.ReadText(full + "\n\n4 Trans. Reader, Collected Fictional Texts, 444. D. Scholar, Another Fictional Book Trans. (Town: Press, 2022), 65.\n\n6 Ibid., 65.\n\n7 For discussion, see E. Author, A Last Fictional Book (Town: Press, 2023).\n\n8 Author, A Last Fictional Book, 395.\n\n9 Ibid., 396—97.");
        check(mixed.Any(c => c.Title == "Another Fictional Book" && c.Author == "D. Scholar") && mixed.Any(c => c.Title == "A Last Fictional Book" && c.Author == "E. Author") &&
            !mixed.Any(c => c.Author.Contains("discussion") || c.Title.Contains("444. D.")),
            "Locators, Trans and discussion cues do not become composite titles or invented authors");
        check(mixed.Single(c => c.Interpretation is { NoteNumber: 6, Resolution: CitationResolution.NeedsReview }).Title == "" &&
            mixed.Any(c => c.Interpretation is { NoteNumber: 8, Resolution: CitationResolution.ShortReference, Locator: "395" }) &&
            mixed.Any(c => c.Interpretation is { NoteNumber: 9, Resolution: CitationResolution.Ibid, Locator: "396—97" } && c.Title == "A Last Fictional Book"),
            "Ibid refuses a missing preceding note while consecutive short-form and Ibid references resolve to an earlier work");
        var ambiguous = CitationProcessing.ReadText("1 A. Writer, The Fictional Coast (Town: Press, 2020); A. Writer, The Fictional City (Town: Press, 2021).\n\n2 Writer, The Fictional, 22.\n\n3 Ibid., 23.");
        check(ambiguous.Count(c => c.Interpretation?.Resolution == CitationResolution.NeedsReview) == 2,
            "Shared short-title prefixes and ambiguous antecedents remain unresolved");
        var editions = CitationProcessing.ReadText("1 A. Writer, The Fictional Coast (Town: Press, 2020); A. Writer, The Fictional Coast (Town: Press, 2021).\n\n2 Ibid., 22.\n\n3 Writer, The Fictional Coast, 23.");
        check(editions.Any(c => c.Interpretation is { NoteNumber: 2, Resolution: CitationResolution.NeedsReview }) &&
            editions.Any(c => c.Interpretation is { NoteNumber: 3, Resolution: CitationResolution.ShortReference, EditionYear: null }),
            "Repeated editions do not silently assign an arbitrary Ibid antecedent or edition year to a short reference");
        var reset = CitationProcessing.ReadText("1 A. Writer, The Fictional Coast (Town: Press, 2020).\n\nChapter 2: A new context\n\n1 Ibid., 4.");
        check(reset.Any(c => c.Interpretation is { Resolution: CitationResolution.NeedsReview, NoteNumber: 1 }),
            "Chapter changes and numbering resets prevent accidental Ibid links across contexts");
        var source = new Book { Title = "Fictional source" }; var other = new Book { Title = "Other fictional source" };
        var unknown = CitationProcessing.ReadText("1 Ibid., 22.\n\n2 Someone, Unknown Work, 44.");
        check(BibliographyGraph.Build([source], _ => unknown).Connections.Length == 0,
            "Unresolved references and preserved full notes never create graph works or connections");
        check(CitationProcessing.WorkKey("A New Look atBetrayer", "Alice Writer") == CitationProcessing.WorkKey("A New Look at Betrayer", "Alice Writer") &&
            CitationProcessing.WorkKey("A Shared Title", "Alice Writer") != CitationProcessing.WorkKey("A Shared Title", "Bob Writer"),
            "Spacing variants share an inferred work identity without rewriting OCR evidence or depending on My Books");
        using var store = new LibraryStore(Path.Combine(workspace, "citation-processing")); store.SaveBatch([source, other]);
        store.SaveBibliography(source.Id, full, null); store.SaveBibliography(other.Id, full.Replace("1 See", "2 See"), null);
        var entry = store.ReadCitationEvidence(source.Id).First(c => c.Title.StartsWith("A Fictional Coast"));
        var snapshot = store.CitationIdentities(); var before = store.Changes(source.Id).Count;
        store.SaveCitationIdentity(source.Id, entry, "A Corrected Fictional Coast", "Alice Writer", null, snapshot.Hash);
        var corrected = store.ReadCitationEvidence(source.Id).First(c => c.Title == "A Corrected Fictional Coast");
        check(corrected.Interpretation?.IdentityReviewed == true && corrected.Text == full && CitationProvenance.Fingerprint(corrected) == CitationProvenance.Fingerprint(entry) &&
            store.ReadCitationEvidence(other.Id).Any(c => c.Title == corrected.Title) && store.Changes(source.Id).Count == before + 1,
            "A reviewed work correction propagates across bibliography aliases, preserves raw evidence/fingerprints, and records history");
        check(CitedRecommendations.LegacyPreferenceKey(corrected) == CitedRecommendations.LegacyPreferenceKey(entry),
            "Legacy citation recommendation preferences remain addressable after canonical work display corrections");
        reject(() => store.SaveCitationIdentity(source.Id, entry, "Stale correction", "A. Writer", null, snapshot.Hash), "Concurrent work-identity reviews cannot overwrite newer changes");
        var backup = store.Backup("citation-identities");
        var second = store.ReadCitationEvidence(source.Id).First(c => c.Title == "Collected Fictional Texts");
        store.SaveCitationIdentity(source.Id, second, corrected.Title, corrected.Author, corrected.Interpretation!.CanonicalWorkKey, store.CitationIdentities().Hash);
        check(store.ReadBibliographyGraph().Statistics().Count(row => row.CitingBooks == 2) == 1,
            "Explicitly merging reviewed work identities reconciles all source aliases without multiplying citing books");
        store.Restore(backup);
        check(store.ReadBibliographyGraph().Statistics().Count(row => row.CitingBooks == 2) == 2,
            "Profile backup and restore retain canonical citation identities independently of library books");
        store.SaveBibliography(source.Id, "1 A changed note.", store.Bibliography(source.Id).Hash);
        reject(() => store.SaveCitationIdentity(source.Id, entry, "Stale source", "A. Writer", null, store.CitationIdentities().Hash), "Work review rejects evidence from a replaced source revision");
        var demo = new Book { Title = "Citation processing demo", Author = "Synthetic evidence" }; store.Save(demo);
        store.SaveBibliography(demo.Id, full + "\n\n2 Writer, A Fictional Coast, 23.\n\n3 Ibid., 24.\n\n5 Ibid., 25.\n\n6 Unknown, A Hidden Work, 44.\n\n7 Ibid., 45.", null);
        var unresolved = store.ReadCitationEvidence(demo.Id).Single(c => c.Interpretation is { NoteNumber: 6, Resolution: CitationResolution.NeedsReview });
        store.SaveCitationIdentity(demo.Id, unresolved, "A Hidden Work", "Z. Unknown", null, store.CitationIdentities().Hash);
        var reviewedShort = store.ReadCitationEvidence(demo.Id).Single(c => c.Interpretation is { NoteNumber: 6, Resolution: CitationResolution.FullReference });
        check(store.ReadCitationEvidence(demo.Id).Any(c => c.Interpretation is { NoteNumber: 7, Resolution: CitationResolution.Ibid } info && info.CanonicalWorkKey == reviewedShort.Interpretation!.CanonicalWorkKey),
            "Reviewing an unresolved reference lets a consecutive Ibid inherit the same stable canonical identity");
        store.SaveCitationIdentity(demo.Id, reviewedShort, reviewedShort.Title, reviewedShort.Author, null, store.CitationIdentities().Hash, exclude: true);
        check(!store.ReadBibliographyGraph().Nodes.Values.Any(node => node.Title == "A Hidden Work") && store.ReadCitationEvidence(demo.Id).Any(c => c.Interpretation?.Excluded == true),
            "Excluding a false work interpretation removes graph links while retaining the original note and review metadata");
        var unsupported = CitationProcessing.ReadText("1 An unsupported manuscript reference without publication details.");
        check(unsupported.Any(c => c.Interpretation?.Resolution == CitationResolution.NeedsReview) && unsupported.All(c => c.Interpretation?.CanConnect == false),
            "Unsupported reference styles enter the review queue without inventing graph works");
        var legacySource = store.ReadCitationEvidence(demo.Id).First(c => c.Interpretation?.Resolution == CitationResolution.FullReference && c.Interpretation.NoteNumber == 1);
        var legacy = legacySource with { Title = legacySource.Interpretation!.ExtractedTitle, Author = legacySource.Interpretation.ExtractedAuthor, Interpretation = null };
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(store.BibliographyPath(demo.Id))!, "citation-reviews.json"),
            System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, CitationAttribution> { [CitationProvenance.Fingerprint(legacy)] = new(CitationRole.OriginalText, Reviewed: true) }, LibraryStore.Json));
        check(store.ReadCitationEvidence(demo.Id).Any(c => CitationProvenance.Fingerprint(c) == CitationProvenance.Fingerprint(legacySource) && c.Attribution?.Reviewed == true),
            "Exact legacy attribution reviews survive the new interpretation layer and canonical display corrections");
    }
}
