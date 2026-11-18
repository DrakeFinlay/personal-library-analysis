using PersonalLibrary.Core;
using PersonalLibrary.Infrastructure;

internal static class LibraryEditingChecks
{
    public static void Run(string workspace, Action<bool, string> check)
    {
        var ids = Enumerable.Range(0, 6).Select(_ => Guid.NewGuid()).ToArray();
        var selected = new HashSet<Guid>();
        LibrarySelection.Change(selected, ids, null, ids[1], true, false);
        LibrarySelection.Change(selected, ids, ids[1], ids[4], true, true);
        check(selected.SetEquals(ids[1..5]), "Shift selection includes both endpoints in displayed order");
        selected.Clear();
        LibrarySelection.Change(selected, ids, ids[4], ids[1], true, true);
        check(selected.SetEquals(ids[1..5]), "Shift selection works right to left across rows");
        LibrarySelection.Change(selected, ids, ids[4], ids[2], false, true);
        check(selected.SetEquals(new[] { ids[1] }), "Shift deselection changes only the requested range");
        LibrarySelection.Change(selected, ids, Guid.NewGuid(), ids[5], true, true);
        check(selected.SetEquals(new[] { ids[1], ids[5] }), "A filtered-away selection anchor falls back to the clicked book");
        using var store = new LibraryStore(Path.Combine(workspace, "bulk-editor"));
        var book = new Book { Title = "Synthetic multilingual work", Author = "A. Writer", Rating = 5,
            Status = ReadingStatus.Read, Review = "Keep review", Shelves = ["original"] };
        var originalEdition = book.SelectedEdition;
        book.Readings.Add(new Reading { EditionId = originalEdition.Id, Outcome = ReadingOutcome.Completed });
        var translation = new Edition { Language = "French", Publisher = "Keep publisher", PageCount = 200 };
        book.Editions.Add(translation); book.SelectedEditionId = translation.Id;
        var other = new Book { Title = "Unselected book" };
        store.SaveBatch([book, other]);
        var changed = LibraryStore.Clone(book);
        new BulkBookChanges(Language: " Japanese ", AddShelves: "new-shelf, original").Apply(changed);
        store.SaveBatch([changed]);
        var saved = store.Books().Single(b => b.Id == book.Id);
        check(saved.SelectedEdition.Language == "Japanese" && saved.Editions[0].Language == "English" &&
            saved.SelectedEdition.Publisher == "Keep publisher" && saved.SelectedEdition.PageCount == 200 &&
            saved.Readings.Single().EditionId == originalEdition.Id && saved.Review == book.Review && saved.Rating == 5 &&
            saved.Shelves.Order().SequenceEqual(new[] { "new-shelf", "original" }) &&
            store.Books().Single(b => b.Id == other.Id).SelectedEdition.Language == "English",
            "Bulk metadata saves selected edition and additive shelves while preserving unchecked fields, readings, and unselected books");
        new BulkBookChanges(Publisher: "", SetPages: true, SetRating: true).Apply(changed);
        check(changed.SelectedEdition.Publisher == "" && changed.SelectedEdition.PageCount == null && changed.Rating == null,
            "Explicit blank bulk fields clear metadata without treating unchecked fields as blank");
        var display = new LibraryDisplay(99, 2, 100).Validated();
        store.SetSetting("library-display", System.Text.Json.JsonSerializer.Serialize(new LibraryDisplay(7, 22, 16)));
        check(display == new LibraryDisplay(20, 12, 24) &&
            System.Text.Json.JsonSerializer.Deserialize<LibraryDisplay>(store.Setting("library-display", "{}")) == new LibraryDisplay(7, 22, 16),
            "My Books display preferences persist and invalid external values are bounded");
    }
}
