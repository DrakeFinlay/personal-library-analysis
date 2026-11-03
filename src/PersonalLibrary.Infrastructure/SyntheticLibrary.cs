using PersonalLibrary.Core;

namespace PersonalLibrary.Infrastructure;

public static class SyntheticLibrary
{
    public static void Seed(LibraryStore store, int seed = 731)
    {
        if (store.Books().Count != 0) throw new InvalidOperationException("Demo data can only be added to an empty profile.");
        store.SetSetting("synthetic", "true");
        var random = new Random(seed);
        string[] titles = ["The Cartographer's Winter", "A History of Quiet Cities", "Letters from the Northern Coast",
            "The Shape of an Argument", "Gardens of the Early Republic", "On Seeing Slowly", "The Lantern Archives",
            "A Field Guide to Forgotten Roads", "Music Beyond the Courtyard", "The Language of River Stones",
            "An Atlas of Everyday Things", "The Glass Observatory", "Margins of the Ancient World", "The Orchard and the Sea",
            "Notes on Common Ground", "The Last Paper Bridge", "Ways of Remembering", "The Distant Harbour"];
        string[] authors = ["Mara Ellison", "Theo Ashford", "Ada Vale", "Julian Fenwick", "Elin Rowe", "Noah Mercer"];
        string[] shelves = ["history", "philosophy", "nature", "fiction", "art", "language"];
        for (var i = 0; i < titles.Length; i++)
        {
            var book = new Book { Title = titles[i], Author = authors[i % authors.Length], SortOrder = i,
                OriginalYear = random.Next(1840, 2026), Shelves = [shelves[i % shelves.Length]],
                ToReadAddedAt = DateTimeOffset.Now.AddDays(-i), ReadNext = i is 5 or 6 or 7 };
            book.SelectedEditionId = book.Editions[0].Id;
            book.SelectedEdition.PageCount = random.Next(160, 640);
            book.SelectedEdition.Label = "Synthetic paperback";
            book.SelectedEdition.Holdings = i % 3 == 0 ? [] : [i % 2 == 0 ? "Physical" : "Ebook"];
            if (i < 3)
            {
                ReadingRules.Start(book, DateOnly.FromDateTime(DateTime.Today.AddDays(-12 - i)));
                ReadingRules.Progress(book, random.Next(10, 100), ProgressUnit.Page);
            }
            if (i >= 10)
            {
                ReadingRules.Start(book, i == 10 ? null : DateOnly.FromDateTime(DateTime.Today.AddDays(-70 - i)));
                ReadingRules.Finish(book, i == 10 ? null : DateOnly.FromDateTime(DateTime.Today.AddDays(-10 - i)), ReadingOutcome.Completed);
                book.Rating = random.Next(2, 6);
            }
            store.Save(book);
        }
    }
}
