using PersonalLibrary.Core;
using PersonalLibrary.Infrastructure;

internal static class PhotoOcrChecks
{
    public static void Run(string workspace, Action<bool, string> check, Action<Action, string> reject)
    {
        var pixels = Enumerable.Range(0, 10000).Select(i => (byte)(i % 100)).ToArray();
        var source = new GrayPhoto(100, 100, pixels);
        var crop = new PhotoArea(new(.2, .2), new(.8, .2), new(.8, .8), new(.2, .8));
        var flat = PhotoPage.Rectify(source, crop, false);
        check(flat.Width == 60 && flat.Height == 60 && flat.Pixels[0] is 19 or 20 && flat.Pixels[^1] is 79 or 80,
            "Page rectification samples the selected area rather than adjacent text");
        var perspective = PhotoPage.Rectify(source, new(new(.1, .2), new(.9, .1), new(.8, .9), new(.2, .8)), false);
        check(perspective.Pixels[0] is 9 or 10 && perspective.Pixels[perspective.Width - 1] is 89 or 90 && perspective.Pixels[^1] is 79 or 80,
            "Perspective correction maps all selected page corners to the output rectangle");
        reject(() => PhotoPage.Rectify(source, new(new(0, 0), new(1, 1), new(1, 0), new(0, 1))), "Crossed page corners are rejected");
        reject(() => PhotoPage.Rectify(source, new(new(-.1, 0), new(1, 0), new(1, 1), new(0, 1))), "Out-of-image page corners are rejected");
        var shadow = new GrayPhoto(100, 100, Enumerable.Repeat((byte)130, 10000).ToArray()); shadow.Pixels[5050] = 20;
        var normalized = PhotoPage.Rectify(shadow, PhotoArea.Whole);
        check(normalized.Pixels[0] > 230 && normalized.Pixels.Min() < 80 && shadow.Pixels[0] == 130,
            "Shadow reduction brightens paper, retains dark type, and preserves original pixels");
        OcrLine Line(string value, int y, int x = 10) => new(value, [new(value, x, y, 500, 20)]);
        var reconstructed = OcrParagraphs.Reconstruct(new(1000, 1000, 0, "en-US", [Line("242", 0, 900), Line("Chapter 1: A fictional chapter", 20),
            Line("1 See A. Writer, A Controver-", 60), Line("sial Coast (Town: Press, 2020).", 85), Line("2 A. Reader, Another Book.", 125)]));
        check(!reconstructed.Contains("242") && reconstructed.Contains("A Controversial Coast") && reconstructed.Contains("\n\n2 A. Reader") && reconstructed.Contains("Chapter 1"),
            "OCR reconstruction preserves headings and numbered notes, repairs wrapped words, and excludes outer page numbers");
        check(OcrParagraphs.ReviewWarnings(new(1000, 1000, 0, "en-US", [Line("11 Writer", 20), Line("12 Reader", 50), Line("14 Scholar", 90)]))
            .Single().Contains("13") && OcrParagraphs.ReviewWarnings(new(1000, 1000, 0, "en-US", [Line("9 Writer", 20), Line("1 Reader", 50)])).Length == 0,
            "Possible missing note numbers are flagged for review without fabricating text or treating chapter resets as lost notes");
        var notes = "## Photo: sample.jpg · area 1\n\nChapter 1: Synthetic notes\n\n" +
            "1 See A. Writer, The Fictional Coast (Town: First Press, 2020); B. Reader, Another Book (and its Notes) (City: Second Press, 2021).\n\n" +
            "2 Writer, The Fictional Coast, 42. This is commentary and an abbreviated reference.\n\n" +
            "A note whose number was lost by recognition must still remain reviewable.";
        var candidates = NoteReferences.Read(notes).ToArray();
        check(candidates.Count(c => c.Title.Length > 0) == 2 && candidates.Any(c => c.Title == "Another Book (and its Notes)"),
            "Mixed notes can contain several full publication citations and parenthesized titles");
        check(candidates.Count(c => c.Title.Length == 0) == 3 && candidates.All(c => c.ArchiveEntry.Contains("sample.jpg")) &&
            candidates.Where(c => c.Title.Length > 0).All(c => c.Text.StartsWith("1 See")),
            "Complete notes and source-photo labels survive extraction, including abbreviated and unnumbered unresolved notes");
        var known = new Book { Title = "The Fictional Coast", Author = "A. Writer" };
        var wrong = new Book { Title = known.Title, Author = "Other Person" };
        var matches = BibliographyMatches.ReadText("1 Writer, TheFictionalCoast, 42.", [known, wrong]);
        check(matches.Any(c => BibliographyMatches.Find(c, [known]).Length > 0) && !matches.Any(c => BibliographyMatches.Find(c, [wrong]).Length > 0),
            "Lost OCR spaces can produce a possible title match only with corresponding author evidence");
        check(!NoteReferences.Read("## Photo: sample.jpg · area 1\n\nChapter 1: A heading\n\n> Review this area carefully").Any(),
            "Photo markers, chapter headings and OCR warnings are not invented citations");
        check(NoteReferences.Read("I See A. Writer, A Fictional Work (Town: Press, 2020).").First().Author == "A. Writer" &&
            !NoteReferences.Read("## Photo: sample.jpg · area 1\n\nChapter I: Synthetic notes").Any(),
            "A leading OCR I before See is treated as a note marker and Roman chapter headings remain headings");
        check(NoteReferences.Read("1 A. Writer, ‘An Imaginary Article’, in A Collection (Town: Press, 2020).").All(c => c.Title.Length == 0),
            "Quoted article evidence remains a full note rather than an invented book title");
        using var store = new LibraryStore(Path.Combine(workspace, "photo-evidence")); store.Save(known);
        var image = Path.Combine(workspace, "original-photo.jpg"); var processed = Path.Combine(workspace, "selected-area.png"); var evidence = Path.Combine(workspace, "selected-area.ocr.json");
        File.WriteAllBytes(image, [1, 2, 3]); File.WriteAllBytes(processed, [4, 5, 6]);
        var raw = new PhotoOcrEvidence("original-photo.jpg", crop, true, new(1000, 1000, 0, "en-US", [Line("A synthetic citation", 20)]));
        var serialized = System.Text.Json.JsonSerializer.Serialize(raw, LibraryStore.Json);
        var recovered = System.Text.Json.JsonSerializer.Deserialize<PhotoOcrEvidence>(serialized, LibraryStore.Json)!;
        check(recovered.Source == raw.Source && recovered.Area == crop && recovered.Recognition.Lines.Single().Text == "A synthetic citation",
            "Production JSON settings retain crop coordinates, source filename and raw OCR words");
        File.WriteAllText(evidence, serialized);
        var saved = store.SaveBibliography(known.Id, notes, null, [image, processed, evidence]);
        store.SaveBibliography(known.Id, saved.Text, saved.Hash, [image, processed, evidence]);
        var sources = Directory.GetFiles(Path.Combine(Path.GetDirectoryName(store.BibliographyPath(known.Id))!, "sources"), "*", SearchOption.AllDirectories);
        check(sources.Length == 3 && sources.Any(p => p.EndsWith(".ocr.json")) && store.Bibliography(known.Id).Text == notes && store.Changes(known.Id).Any(c => c.Details?.Contains("selected-area.ocr.json") == true),
            "Saving scanned notes retains original, prepared area and raw recognition evidence once with audit history");
    }
}
