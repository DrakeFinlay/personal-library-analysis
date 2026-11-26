using PersonalLibrary.Core;
using PersonalLibrary.Infrastructure;

internal static class CitationProvenanceChecks
{
    public static void Run(string workspace, Action<bool, string> check, Action<Action, string> reject)
    {
        var older = new Book { Title = "A Fictional Old Work", Author = "A. Writer", OriginalYear = 1790 };
        var later = new Book { Title = "A Fictional Modern Work", Author = "B. Reader", OriginalYear = 1999 };
        var citation = new EpubCitation(1, "Bibliography", "Writer, A. A Fictional Old Work. Town: Press, 1993.", older.Title, older.Author, "bibliography.xhtml", null);
        check(CitationProvenance.Assess(later, citation, older) is { CitedOriginalYear: 1790, ReferenceYear: 1993, Warning.Length: 0 } &&
            !CitationProvenance.Assess(later, citation, older).EligibleOriginalText,
            "Modern references to a later edition of an old work retain separate dates and do not silently become reviewed original-text citations");
        var impossible = citation with { Title = later.Title, Author = later.Author, Attribution = new(CitationRole.OriginalText, Reviewed: true) };
        check(CitationProvenance.Assess(older, impossible, later).Warning.Contains("1999") && !CitationProvenance.Assess(older, impossible, later).EligibleOriginalText,
            "A reviewed original-text claim with a known later cited original year is flagged and excluded from original-only graph scope");
        var editorial = impossible with { Attribution = new(CitationRole.Editorial, "Fictional editor", SourceEditionYear: 2005, Reviewed: true) };
        check(CitationProvenance.Assess(older, editorial, later).Warning.Length == 0 && !CitationProvenance.Assess(older, editorial, later).EligibleOriginalText,
            "Later editorial references use the source edition date and never imply original-author citation");
        check(CitationProvenance.Assess(older, citation).Warning.Contains("later edition") && CitationProvenance.Infer("Suggested reading").Role == CitationRole.FurtherReading &&
            CitationProvenance.Infer("Introduction references").Role == CitationRole.Editorial,
            "Unknown cited original dates produce a qualified review warning and section-role inference stays distinct from manual review");
        reject(() => CitationProvenance.Validate(new(CitationRole.OriginalText, CitedOriginalYear: 0)), "Attribution review rejects year zero");
        using var store = new LibraryStore(Path.Combine(workspace, "provenance")); store.SaveBatch([older, later]);
        store.SaveEpubBibliography(later.Id, new("synthetic.epub", "first-revision", [], [citation], 0));
        var saved = store.ReadEpubBibliography(later.Id)!.Entries.Single(); var snapshot = store.CitationReviews(later.Id);
        var before = store.Changes(later.Id).Count;
        store.SaveCitationReview(later.Id, saved, new(CitationRole.OriginalText, CitedOriginalYear: 1790, CitedEditionYear: 1993, Reviewed: true), snapshot.Hash);
        check(store.ReadCitationEvidence(later.Id).Single().Attribution?.Reviewed == true && store.ReadBibliographyGraph(true).Connections.Single().Target == "book:" + older.Id &&
            store.Changes(later.Id).Count == before + 1 && store.Changes(later.Id)[0].Details!.Contains("citation-reviews.json"),
            "Reviewed attribution persists separately from source evidence, updates book history, and controls original-only graph scope");
        reject(() => store.SaveCitationReview(later.Id, saved, new(CitationRole.Editorial), snapshot.Hash), "Stale attribution review snapshots cannot overwrite newer reviews");
        var backup = store.Backup("provenance-test"); var count = store.Changes(later.Id).Count;
        store.SaveCitationReview(later.Id, saved, new(CitationRole.Editorial, "Editor", SourceEditionYear: 2005, Reviewed: true), store.CitationReviews(later.Id).Hash);
        check(store.ReadBibliographyGraph(true).Connections.Length == 0 && store.ReadBibliographyGraph().Connections.Length == 1,
            "Editorial references remain visible in all-reference scope while original-only scope excludes them");
        store.Restore(backup);
        check(store.ReadCitationEvidence(later.Id).Single().Attribution?.Role == CitationRole.OriginalText && store.Changes(later.Id).Count == count,
            "Profile backup and restore retain citation attribution reviews with their audit timeline");
        store.SaveEpubBibliography(later.Id, new("replacement.epub", "different-revision", [], [citation], 0));
        check(store.ReadCitationEvidence(later.Id).Single().Attribution == null && store.ReadBibliographyGraph(true).Connections.Length == 0,
            "A changed source revision does not inherit a review from an older edition even when citation wording is unchanged");
        reject(() => store.SaveCitationReview(later.Id, saved, new(CitationRole.OriginalText), store.CitationReviews(later.Id).Hash), "Reviewing citation evidence from a replaced source is rejected");
        var mixed = BibliographyGraph.Build([older, later], id => id == later.Id ? [citation with { Attribution = new(CitationRole.OriginalText, Reviewed: true) },
            citation with { Attribution = new(CitationRole.Editorial, "Editor", Reviewed: true) }] : []);
        check(mixed.Connections.Single().Evidence.Length == 2 && mixed.Statistics().Single().CitingBooks == 1,
            "Identical reference text with different attribution contexts retains both evidence records without multiplying distinct citing books");
    }
}
