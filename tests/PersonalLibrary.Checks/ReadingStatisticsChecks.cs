using PersonalLibrary.Core;

internal static class ReadingStatisticsChecks
{
    public static void Run(Action<bool, string> check)
    {
        var epic = new Book { Title = "Synthetic Epic", OriginalYear = -365 };
        epic.SelectedEdition.PublicationYear = 2001;
        epic.SelectedEdition.PageCount = 120;
        ReadingRules.Start(epic, new DateOnly(2024, 12, 28));
        ReadingRules.Finish(epic, new DateOnly(2025, 1, 1), ReadingOutcome.Completed);
        var second = new Edition { Label = "Translation", PageCount = 230 };
        epic.Editions.Add(second);
        epic.SelectedEditionId = second.Id;
        ReadingRules.Start(epic, null);
        ReadingRules.Finish(epic, null, ReadingOutcome.Completed);

        var missingPages = Finished("No page count", 2025, null, -365);
        var spare = new Edition { Label = "Unread alternate", PageCount = 800 };
        missingPages.Editions.Add(spare);
        missingPages.SelectedEditionId = spare.Id;
        var missingYear = Finished("Unknown original year", 2026, 150);
        missingYear.SelectedEdition.PublicationYear = 1985;
        var excluded = Finished("Excluded completion", 2025, 900, 2010);
        excluded.Excluded = true;
        var unfinished = new Book { Title = "Unfinished", OriginalYear = 2020 };
        unfinished.SelectedEdition.PageCount = 400;
        ReadingRules.Start(unfinished, null);
        ReadingRules.Finish(unfinished, new DateOnly(2025, 3, 1), ReadingOutcome.DidNotFinish);
        var reference = new Book { Title = "Reference only", Status = ReadingStatus.ReferenceOnly };
        Book[] books = [epic, missingPages, missingYear, excluded, unfinished, reference];
        var statistics = new ReadingStatistics(books);

        check(statistics.Years.Select(year => year.Year).SequenceEqual([2026, 2025]) &&
              statistics.Years[1].Readings.Count == ReadingRules.CompletedInYear(books, 2025) &&
              statistics.Years[1].Readings.Count == 2 && statistics.Years[1].Pages == new PageCoverage(120, 1, 1),
            "Annual statistics group by completion year and show pages and missing counts from the read editions");
        check(statistics.LifetimePages == new PageCoverage(500, 3, 1) && statistics.UndatedCompletions == 1,
            "Lifetime pages include undated rereads without selecting another edition or including unfinished books");
        var point = statistics.PublicationPoints.Single();
        check(statistics.DatedWithoutPublicationYear == 1 && point.OriginalYear == -365 && point.Finished == new DateOnly(2025, 1, 1) &&
              point.Readings.Select(item => item.Reading.Id).ToHashSet().SetEquals([epic.Readings[0].Id, missingPages.Readings[0].Id]),
            "The scatterplot keeps BCE publication years, omits unavailable dates, and preserves all readings at shared coordinates");

        epic.Editions[0].PageCount = 175;
        var corrected = new ReadingStatistics(books);
        check(corrected.LifetimePages == new PageCoverage(555, 3, 1) && corrected.Years[1].Pages.KnownPages == 175 && epic.Readings.Count == 2,
            "Edition page corrections recalculate totals without creating reading records");
        var empty = new ReadingStatistics([]);
        check(empty.LifetimePages == new PageCoverage(0, 0, 0) && empty.Years.Count == 0 && empty.PublicationPoints.Count == 0,
            "An empty library produces no annual rows or publication points");

        epic.Author = "  Jordan  Vale ";
        epic.Rating = 5;
        missingPages.Author = "jordan vale";
        missingPages.Rating = 2;
        excluded.Author = epic.Author;
        excluded.Rating = 5;
        unfinished.Author = epic.Author;
        unfinished.Rating = 5;
        missingYear.Author = "Riley North";
        missingYear.Rating = 4;
        var secondFavorite = Finished("A Narrow Path", 2026, 60, 1972);
        secondFavorite.Author = missingYear.Author;
        secondFavorite.Rating = 5;
        var oldest = Finished("An Older Scroll", 2024, null, -550);
        oldest.Readings.Single().Finished = null;
        oldest.Author = "One Rated Work";
        oldest.Rating = 5;
        var extraAuthors = new[] { "Avery West", "Morgan East", "Taylor South" }.Select(name =>
        {
            var book = Finished($"Another book by {name}", 2026, null);
            book.Author = name;
            return book;
        });
        var highlights = new ReadingStatistics([.. books, secondFavorite, oldest, .. extraAuthors]);
        check(highlights.MostReadAuthors.Select(author => author.Author).SequenceEqual(
                ["Jordan Vale", "Riley North", "Avery West", "Morgan East", "One Rated Work"], StringComparer.OrdinalIgnoreCase) &&
              highlights.MostReadAuthors.Select(author => author.Readings.Count).SequenceEqual([3, 2, 1, 1, 1]),
            "Most-read authors shows the top five by completed readings including rereads, with alphabetical tie-breaking");
        check(highlights.FavoriteAuthors is [{ Author: "Riley North", RatedBooks: 2, AverageRating: 4.5m }],
            "Favorite authors includes the exact 4.5-star cutoff and excludes lower averages and authors with one rated work");
        check(highlights.OldestBook?.Book == oldest && highlights.LongestBook is { Pages: 230 } &&
              highlights.LongestBook.Edition.Id == second.Id && highlights.ShortestBook?.Book == secondFavorite,
            "Reading highlights include undated BCE books and compare only editions recorded on completed readings with known lengths");
        check(empty.MostReadAuthors.Count == 0 && empty.FavoriteAuthors.Count == 0 &&
              empty.OldestBook == null && empty.LongestBook == null && empty.ShortestBook == null,
            "Empty reading history has no author rankings or book highlights");
        var special = new[] { ReadingStatus.MusicBook, ReadingStatus.PhotographyArtBook, ReadingStatus.ReferenceOnly }.Select(status =>
        {
            var book = Finished("A collection", 2025, 600, 1820);
            book.Author = "Sample Author";
            book.Rating = 5;
            ReadingRules.Classify(book, status);
            return book;
        }).ToArray();
        var collectionStats = new ReadingStatistics(special);
        check(special.All(book => book.Readings.Count == 1 && book.Status != ReadingStatus.Read) &&
              collectionStats.LifetimePages == empty.LifetimePages && collectionStats.Years.Count == 0 &&
              collectionStats.PublicationPoints.Count == 0 && collectionStats.MostReadAuthors.Count == 0 &&
              ReadingRules.CompletedInYear(special, 2025) == 0 && Recommendations.Rank(special).RatedBooks == 0 &&
              special.All(book => !CitedRecommendations.IsSource(book)),
            "Music, photography/art, and reference books keep their history and are excluded from reading stats and recommendations");

        var full = PublicationYearRange.ForData(-500, 2000);
        const double cursor = .9;
        var zoomed = full.Zoom(full, cursor, 4);
        check(full.First < -500 && full.Last > 2000 && zoomed.Span < full.Span &&
              Math.Abs((zoomed.First + cursor * zoomed.Span) - (full.First + cursor * full.Span)) < .001 &&
              PublicationYearRange.TickStep(full.Span / 12) == 250,
            "The publication-year grid uses 250-year ticks over this BCE-to-modern range and zoom stays anchored at the cursor");
        var narrow = zoomed.Zoom(full, 1, 100);
        check(narrow.Last <= full.Last && narrow.Span == 10 && narrow.Zoom(full, 0, -100) == full &&
              PublicationYearRange.ForData(2000, 2000).Span > 0,
            "Publication zoom has bounded context, restores the full range, and supports a single publication year");
    }

    private static Book Finished(string title, int year, int? pages, int? originalYear = null)
    {
        var book = new Book { Title = title, OriginalYear = originalYear };
        book.SelectedEdition.PageCount = pages;
        ReadingRules.Start(book, null);
        ReadingRules.Finish(book, new DateOnly(year, 1, 1), ReadingOutcome.Completed);
        return book;
    }
}
