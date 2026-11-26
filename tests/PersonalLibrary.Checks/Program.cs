using System.IO.Compression;
using System.Text;
using System.Text.Json;
using PersonalLibrary.Core;
using PersonalLibrary.Infrastructure;

var workspace = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../.local/checks/" + Guid.NewGuid().ToString("N")));
Directory.CreateDirectory(workspace);
var passed = 0;
void Check(bool condition, string label)
{
    if (!condition) throw new Exception("FAILED: " + label);
    passed++; Console.WriteLine("PASS " + label);
}
void Reject(Action action, string label)
{
    try { action(); } catch (Exception ex) when (ex is InvalidOperationException or InvalidDataException or IOException) { Check(true, label); return; }
    throw new Exception("FAILED (did not reject): " + label);
}
Book NewBook(string title) { var book = new Book { Title = title }; book.SelectedEditionId = book.Editions[0].Id; book.SelectedEdition.PageCount = 400; return book; }

try
{
    GoalChecks.Run(workspace, Check, Reject);
    BookChangeChecks.Run(workspace, Check, Reject);
    GoodreadsOrderChecks.Run(workspace, Check, Reject);
    EpubImportChecks.Run(workspace, Check, Reject);
    PhotoOcrChecks.Run(workspace, Check, Reject);
    CitationProcessingChecks.Run(workspace, Check, Reject);
    CitationDiscoveryChecks.Run(workspace, Check);
    BibliographyGraphChecks.Run(workspace, Check);
    CitationProvenanceChecks.Run(workspace, Check, Reject);
    BrowsingChecks.Run(workspace, Check);
    LocalCitationChecks.Run(workspace, Check);
    var book = NewBook("Synthetic coastal history");
    ReadingRules.Start(book, new DateOnly(2025, 12, 20));
    Check(book.Status == ReadingStatus.CurrentlyReading && book.ActiveReading?.Progress.Count == 0,
        "Starting a reading requires no progress and creates no progress entry");
    ReadingRules.Progress(book, 100, ProgressUnit.Page);
    Check(book.ActiveReading!.Progress.Count == 1 && ReadingRules.ProgressText(book).Contains("25%"), "Page progress converts using reading edition");
    Reject(() => ReadingRules.Progress(book, 401, ProgressUnit.Page), "Out-of-range page rejected");
    Reject(() => ReadingRules.Progress(book, 1.5m, ProgressUnit.Page), "Fractional page rejected");
    Reject(() => ReadingRules.Progress(book, 101, ProgressUnit.Percent), "Out-of-range percentage rejected");
    Check(book.ActiveReading!.Progress.Count == 1, "Rejected progress leaves history unchanged");
    ReadingRules.Progress(book, 100, ProgressUnit.Percent);
    Check(book.Status == ReadingStatus.CurrentlyReading, "100 percent does not silently finish a reading");
    var progressCount = book.ActiveReading!.Progress.Count;
    book.SelectedEdition.PageCount = 450;
    Check(book.ActiveReading.Progress.Count == progressCount, "Page correction does not invent a progress event");
    Reject(() => ReadingRules.Finish(book, new DateOnly(2025, 12, 19), ReadingOutcome.Completed), "Finish before start rejected");
    ReadingRules.Finish(book, new DateOnly(2026, 1, 4), ReadingOutcome.Completed);
    Reject(() => ReadingRules.Finish(book, new DateOnly(2026, 1, 4), ReadingOutcome.Completed), "Repeated finish cannot double-count");
    ReadingRules.Start(book, null); ReadingRules.Finish(book, null, ReadingOutcome.Completed);
    Check(ReadingRules.CompletedInYear([book], 2026) == 1 && book.Readings.Count == 2, "Undated completed reread stays outside annual totals");
    var second = new Edition { Label = "Another translation", PageCount = 200 };
    book.Editions.Add(second); book.SelectedEditionId = second.Id; ReadingRules.Start(book, new DateOnly(2026, 2, 1));
    ReadingRules.Progress(book, 50, ProgressUnit.Percent);
    Check(book.Readings[0].EditionId != book.ActiveReading!.EditionId, "Edition switch preserves past reading identity");
    ReadingRules.Finish(book, new DateOnly(2026, 2, 8), ReadingOutcome.Completed);
    Check(ReadingRules.CompletedInYear([book], 2026) == 2, "Dated reread counts separately");
    book.Excluded = true; Check(ReadingRules.CompletedInYear([book], 2026) == 0, "Excluded content removed from challenge"); book.Excluded = false;
    book.Rating = 0; Reject(() => ReadingRules.Validate(book), "Zero is not an unrated rating"); book.Rating = null;
    var root = Path.Combine(workspace, "profile-root");
    string backup;
    using (var store = new LibraryStore(root))
    {
        book.Notes = "Private synthetic note"; store.Save(book);
        var original = store.Bibliography(book.Id);
        var saved = store.SaveBibliography(book.Id, "# References\n\nA fictional reference.", original.Hash);
        Check(saved.Hash != null && saved.Text.Contains("fictional"), "Bibliography written and hash returned");
        Reject(() => store.SaveBibliography(book.Id, "Stale overwrite", null), "Stale bibliography save rejected");
        File.AppendAllText(store.BibliographyPath(book.Id), "\nExternal edit");
        Reject(() => store.SaveBibliography(book.Id, "Discard external", saved.Hash), "External Markdown edit protected");
        Check(store.Bibliography(book.Id).Text.Contains("External edit"), "External edit survives conflict");
        var latest = store.Bibliography(book.Id);
        store.SaveBibliography(book.Id, latest.Text + "\nApp revision", latest.Hash);
        store.SetSetting("goal-2026", "40");
        backup = store.Backup(); var preview = store.PreviewBackup(backup);
        Check(preview.Books == 1 && preview.Files >= 4, "Backup preview validates database and document revisions");
        book.Notes = "Changed after backup"; store.Save(book);
        store.SaveBibliography(book.Id, "Replaced after backup", store.Bibliography(book.Id).Hash);
        store.Restore(backup);
        Check(store.Books().Single().Notes == "Private synthetic note" && store.Bibliography(book.Id).Text.Contains("App revision"), "Restore recovers matching book and bibliography");
        Check(store.Setting("goal-2026", "0") == "40", "Settings restored with profile");
        Check(Directory.GetFiles(store.BackupDirectory, "pre-restore-*.zip").Length == 1, "Pre-restore snapshot retained");
        var tampered = Path.Combine(workspace, "tampered.zip"); File.Copy(backup, tampered);
        using (var zip = ZipFile.Open(tampered, ZipArchiveMode.Update))
        {
            var entry = zip.GetEntry("personal.sqlite")!; entry.Delete();
            using var output = new StreamWriter(zip.CreateEntry("personal.sqlite").Open()); output.Write("corrupt");
        }
        Reject(() => store.Restore(tampered), "Corrupt backup rejected before replacement");
        Check(store.Books().Count == 1, "Failed restore preserves live profile");
        var unsafeArchive = Path.Combine(workspace, "unsafe.zip");
        using (var zip = ZipFile.Open(unsafeArchive, ZipArchiveMode.Create))
        {
            using (var writer = new StreamWriter(zip.CreateEntry("manifest.json").Open()))
                writer.Write(JsonSerializer.Serialize(new BackupManifest(1, DateTimeOffset.Now, new() { ["../escape"] = "x", ["personal.sqlite"] = "x" }), LibraryStore.Json));
            using (var writer = new StreamWriter(zip.CreateEntry("personal.sqlite").Open())) writer.Write("invalid");
            using (var writer = new StreamWriter(zip.CreateEntry("../escape").Open())) writer.Write("invalid");
        }
        Reject(() => store.PreviewBackup(unsafeArchive), "Unsafe/incomplete archive rejected");
        Check(!File.Exists(Path.Combine(root, "escape")), "Archive cannot write outside staging");
        Reject(() => { using var other = new LibraryStore(root); }, "Second writer cannot open same profile");
        for (var i = 1; i <= 20; i++) File.WriteAllText(Path.Combine(store.BackupDirectory, $"daily-200001{i:00}-old.zip"), "old synthetic fixture");
        for (var i = 1; i <= 10; i++) File.WriteAllText(Path.Combine(store.BackupDirectory, $"weekly-2000-{i:00}-old.zip"), "old synthetic fixture");
        store.SetSetting("retention-check", "true");
        Check(Directory.GetFiles(store.BackupDirectory, "daily-*.zip").Length == 14 && Directory.GetFiles(store.BackupDirectory, "weekly-*.zip").Length == 4, "Daily and weekly retention limits enforced");
        Check(File.Exists(backup), "Automatic pruning preserves manual backups");
    }
    using (var reopened = new LibraryStore(root))
        Check(reopened.Books().Single().Readings.Count == 3, "Reading records survive profile close and reopen");
    using (var demo = new LibraryStore(Path.Combine(workspace, "demo")))
    {
        SyntheticLibrary.Seed(demo);
        Check(demo.Books().Count == 18 && demo.Books().Count(b => b.ActiveReading != null) == 3, "Synthetic seed produces usable reading home");
        Reject(() => SyntheticLibrary.Seed(demo), "Seed cannot overwrite a populated profile");
        Check(demo.Setting("synthetic", "false") == "true", "Synthetic profile stays explicitly marked");
    }
    ReadingSectionChecks.Run(workspace, Check, Reject);
    CalibreChecks.Run(workspace, Check);
    BibliographyChecks.Run(workspace, Check);
    await PublicBibliographyChecks.Run(workspace, Check);
    GoodreadsChecks.Run(workspace, Check);
    GoodreadsRatingsChecks.Run(workspace, Check, Reject);
    RecommendationChecks.Run(Check);
    LibraryEditingChecks.Run(workspace, Check);
    ReadingStatisticsChecks.Run(Check);
    await PurchaseChecks.Run(workspace, Check);
    EnrichmentChecks.Run(workspace, Check);
    await EnrichmentChecks.Network(Check);
    LibraryCleanupChecks.Run(workspace, Check);
    await MetadataChecks.Run(Check);
    Console.WriteLine($"{passed} checks passed. Synthetic artifacts: {workspace}");
}
catch (Exception ex) { Console.Error.WriteLine(ex); Environment.ExitCode = 1; }
