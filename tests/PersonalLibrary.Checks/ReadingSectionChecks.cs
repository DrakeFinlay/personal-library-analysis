using PersonalLibrary.Core;
using PersonalLibrary.Infrastructure;

internal static class ReadingSectionChecks
{
    public static void Run(string workspace, Action<bool, string> check, Action<Action, string> reject)
    {
        var root = Path.Combine(workspace, "reading-sections");
        var daily = new Book { Title = "A Fictional Morning Reader", SortOrder = 10 };
        var second = new Book { Title = "An Atlas of Imaginary Birds", SortOrder = 1 };
        var project = new Book { Title = "A History of the Silver Railway", SortOrder = 5 };
        foreach (var book in new[] { daily, second, project }) ReadingRules.Start(book, null);
        ReadingRules.Progress(daily, 12, ProgressUnit.Page);
        var readingId = daily.ActiveReading!.Id;
        ReadingSection defaultSection;
        var study = new ReadingSection(Guid.NewGuid(), " Train research ");

        using (var store = new LibraryStore(root))
        {
            store.SaveBatch([daily, second, project]);
            defaultSection = store.ReadingSections().Single();
            check(defaultSection.Name == "Daily Reads" && store.Books().All(book => book.ReadingSectionId == null),
                "Profiles without section settings have Daily Reads and keep their books unassigned");
            store.MoveToReadingSection(daily.Id, defaultSection.Id);
            store.MoveToReadingSection(second.Id, defaultSection.Id);
            check(store.Books().Where(book => book.ReadingSectionId == defaultSection.Id).Select(book => book.Id)
                .SequenceEqual([daily.Id, second.Id]) && store.Books().Single(book => book.Id == project.Id).SortOrder == 5,
                "Moving books appends within the destination and preserves the other section's order");
            reject(() => store.SaveReadingSections([defaultSection, study with { Name = " daily reads " }]),
                "Duplicate section names are rejected after trimming and ignoring case");
            reject(() => store.MoveToReadingSection(daily.Id, study.Id), "Moving to an unknown section is rejected");
            store.SaveReadingSections([study, defaultSection with { Name = "Morning Reads" }]);
            store.MoveToReadingSection(project.Id, study.Id);
        }

        using (var store = new LibraryStore(root))
        {
            var books = store.Books();
            var morning = books.Single(book => book.Id == daily.Id);
            check(store.ReadingSections().Select(section => section.Name).SequenceEqual(["Train research", "Morning Reads"])
                && morning.ReadingSectionId == defaultSection.Id && morning.ActiveReading?.Id == readingId
                && morning.ActiveReading.Progress.Single().Value == 12,
                "Section order, renamed membership and progress survive reopening the profile");

            var duplicate = new Book { Title = daily.Title };
            store.Save(duplicate);
            store.MergeBooks(duplicate, morning);
            var merged = store.Books().Single(book => book.Id == duplicate.Id);
            check(merged.ReadingSectionId == defaultSection.Id && merged.ActiveReading?.Id == readingId,
                "Merging a currently reading duplicate carries its section with the active reading");
            store.MoveToReadingSection(merged.Id, null);
            check(store.Books().Single(book => book.Id == merged.Id).ReadingSectionId == null,
                "A reader can move a book back to Other books");

            var inactive = store.Books().Single(book => book.Id == second.Id);
            ReadingRules.Finish(inactive, null, ReadingOutcome.Completed);
            store.Save(inactive);
            store.SaveReadingSections([]);
            check(store.ReadingSections().Count == 0 && store.Books().Count == 3
                && store.Books().All(book => book.ReadingSectionId == null)
                && store.Books().Single(book => book.Id == inactive.Id).Status == ReadingStatus.Read,
                "Removing sections clears active and inactive memberships while preserving books and status");
        }

        using var reopened = new LibraryStore(root);
        check(reopened.ReadingSections().Count == 0, "Removing the default section persists across reopening");
    }
}
