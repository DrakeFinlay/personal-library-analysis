using PersonalLibrary.Core;
using PersonalLibrary.Infrastructure;

internal static class GoalChecks
{
    public static void Run(string workspace, Action<bool, string> check, Action<Action, string> reject)
    {
        var root = Path.Combine(workspace, "annual-goals");
        var book = new Book { Title = "An Imaginary Journey", Notes = "Keep this note" };
        ReadingRules.Start(book, null); ReadingRules.Finish(book, new DateOnly(2026, 5, 2), ReadingOutcome.Completed);
        ReadingRules.Start(book, null); ReadingRules.Finish(book, null, ReadingOutcome.Completed);
        using (var store = new LibraryStore(root))
        {
            store.Save(book);
            store.PlanReading(book.Id, 2026, true); store.PlanReading(book.Id, 2026, true);
            store.PlanReading(book.Id, 2027, true);
            var saved = store.Books().Single();
            check(saved.PlannedReadingYears.SequenceEqual([2026, 2027]) && saved.Notes == book.Notes && saved.Readings.Count == 2,
                "Annual title plans deduplicate, support multiple years, and preserve reading history and notes");
            check(ReadingGoals.Completed(saved, 2026) && !ReadingGoals.Completed(saved, 2027),
                "Title completion uses the planned year and ignores undated readings");
            saved.Readings.Add(new Reading { EditionId = saved.SelectedEdition.Id, Outcome = ReadingOutcome.Completed, ApproximateFinishYear = 2027 });
            check(ReadingGoals.Completed(saved, 2027), "Year-only completed readings satisfy the title plan");
            saved.Status = ReadingStatus.ReferenceOnly;
            check(!ReadingGoals.Completed(saved, 2026), "Special collections do not satisfy reading goals");
            store.PlanReading(book.Id, 2026, false);
            reject(() => store.PlanReading(book.Id, 0, true), "Invalid annual plan year rejected");
            reject(() => store.PlanReading(Guid.NewGuid(), 2026, true), "Missing plan book rejected");
        }
        using (var store = new LibraryStore(root))
        {
            var saved = store.Books().Single();
            check(saved.PlannedReadingYears.SequenceEqual([2027]) && saved.Readings.Count == 2,
                "Title plans survive reopening and removing from one year preserves other years and history");
            saved.ToReadGroups = ["Research for a book"]; store.Save(saved);
            var other = new Book { Title = "Another Imaginary Journey", PlannedReadingYears = [2028], ToReadGroups = ["Biblical research"] };
            store.Save(other);
            store.MergeBooks(saved, other);
            check(store.Books().Single().PlannedReadingYears.SequenceEqual([2027, 2028]) && store.Books().Single().ToReadGroups.Count == 2,
                "Merging books preserves title plans and groups from both records");
        }
        var now = DateTimeOffset.Now;
        var recent = Enumerable.Range(0, 15).Select(i => new Book
        {
            Title = $"Book {i}", UpdatedAt = now.AddDays(-i), ToReadAddedAt = now.AddDays(i),
            Status = i % 2 == 0 ? ReadingStatus.Read : ReadingStatus.CurrentlyReading, Excluded = i == 0
        }).ToList();
        check(ReadingGoals.RecentlyChanged(recent).Select(book => book.Title).SequenceEqual(Enumerable.Range(1, 10).Select(i => $"Book {i}")),
            "Recently changed shows ten saved books across statuses, newest first, excluding out-of-scope records");
    }
}
