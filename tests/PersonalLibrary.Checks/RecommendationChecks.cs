using PersonalLibrary.Core;

internal static class RecommendationChecks
{
    public static void Run(Action<bool, string> check)
    {
        var favorite = new Book { Title = "Favorite Coast", Author = "Sample Writer", Rating = 5,
            Status = ReadingStatus.Read, Shelves = ["coastal-history", "Coastal History"] };
        var disliked = new Book { Title = "Disliked Coast", Author = "Another Writer", Rating = 1,
            Status = ReadingStatus.Read, Shelves = ["Coastal History", "mountains"] };
        var likedTopic = new Book { Title = "An Island", Author = "Third Writer", Rating = 4,
            Status = ReadingStatus.Read, Shelves = ["islands"] };
        var sameAuthor = new Book { Title = "New Coast", Author = "sample writer", Shelves = ["coastal history"] };
        var sameTopic = new Book { Title = "The Archipelago", Shelves = ["ISLANDS"] };
        var offset = new Book { Title = "Mountain Island", Shelves = ["islands", "mountains"] };
        var unrelated = new Book { Title = "Unrelated" };
        sameTopic.Editions.Add(new Edition { Holdings = ["Physical"] });
        var library = new List<Book> { disliked, favorite, likedTopic, sameTopic, offset, sameAuthor, unrelated };

        var ranked = Recommendations.Rank(library);
        check(ranked.RatedBooks == 3 && ranked.Candidates == 4 && ranked.Matches.Select(result => result.Book.Id)
            .SequenceEqual(new[] { sameAuthor.Id, sameTopic.Id }),
            "Recommendations rank author and topic matches and use low ratings to lower weak matches");
        check(ranked.Matches[0].Reasons.Single() is { MatchedBooks: 1, AverageRating: 5 } &&
              ranked.Matches[1].Reasons.Single().Label == "Topic: ISLANDS",
            "Recommendations explain their evidence and count duplicated topic labels only once per book");
        check(Recommendations.Rank(library, ownedOnly: true).Matches.Single().Book.Id == sameTopic.Id,
            "Owned recommendations recognize holdings across editions");

        var completed = new Book { Title = "Already Read", Author = favorite.Author };
        ReadingRules.Start(completed, null);
        ReadingRules.Finish(completed, null, ReadingOutcome.Completed);
        ReadingRules.Classify(completed, ReadingStatus.ToRead);
        library.AddRange([completed,
            new Book { Title = "Excluded Candidate", Author = favorite.Author, Excluded = true },
            new Book { Title = "In progress", Author = favorite.Author, Status = ReadingStatus.CurrentlyReading },
            new Book { Title = "Abandoned", Author = favorite.Author, Status = ReadingStatus.DidNotFinish },
            new Book { Title = "Excluded Evidence", Author = sameAuthor.Author, Excluded = true, Status = ReadingStatus.Read, Rating = 1 }]);
        var filtered = Recommendations.Rank(library);
        check(filtered.Candidates == ranked.Candidates && filtered.RatedBooks == ranked.RatedBooks &&
              filtered.Matches.Select(match => match.Score).SequenceEqual(ranked.Matches.Select(match => match.Score)),
            "Current, completed, abandoned, and excluded books cannot be recommended and excluded ratings are ignored");
        check(Recommendations.Rank([sameAuthor, unrelated]).Matches.Count == 0,
            "Unrated libraries do not produce personalized recommendations");

        var authors = Enumerable.Range(1, 12).Select(index => $"Example Writer {index}").ToArray();
        var rated = authors.Select(author => new Book { Title = $"Finished by {author}", Author = author,
            Status = ReadingStatus.Read, Rating = 5 }).ToArray();
        var queue = authors.Select((author, index) => new Book { Title = $"Queued {index + 1:00}", Author = author }).ToArray();
        queue[0].SelectedEdition.Holdings.Add("Ebook");
        var ownedUnread = Enumerable.Range(1, 11).Select(index =>
        {
            var book = new Book { Title = $"Z Owned {index:00}", Author = authors[index], Status = ReadingStatus.ToRead };
            book.SelectedEdition.Holdings.Add("Physical");
            return book;
        }).ToArray();
        var sameWriter = new Book { Title = "Queued 01 sequel", Author = $" {queue[0].Author.ToUpperInvariant()} " };
        var overview = Recommendations.Overview([.. rated, .. queue, sameWriter, .. ownedUnread]);
        check(overview.ToRead.Select(pick => pick.Book.Id).SequenceEqual(queue.Take(10).Select(book => book.Id)) &&
              overview.OwnedUnread.Select(pick => pick.Book.Id).SequenceEqual(ownedUnread.Take(10).Select(book => book.Id)),
            "Recommendation overview shows ten books per section, limits repeats by author, and prioritizes distinct physical To read picks");
        var sparse = Recommendations.Overview([.. rated, queue[0], ownedUnread[0]]);
        check(sparse.ToRead.Count == 2 && sparse.OwnedUnread.Select(pick => pick.Book.Id)
            .SequenceEqual(new[] { ownedUnread[0].Id }),
            "Sparse owned sections include physical To read overlaps and omit ebook-only holdings");
        var physical = new Book { Author = favorite.Author };
        physical.SelectedEdition.Holdings.Add("Physical");
        var ebookOnly = new Book { Author = favorite.Author };
        ebookOnly.SelectedEdition.Holdings.Add("Ebook");
        var unclassifiedPhysical = new Book { Author = favorite.Author, Status = ReadingStatus.Unclassified };
        unclassifiedPhysical.SelectedEdition.Holdings.Add("Physical");
        var soldPhysical = new Book { Author = favorite.Author };
        soldPhysical.SelectedEdition.PhysicalCopies.Add(new PhysicalCopy { Sold = true });
        soldPhysical.SelectedEdition.Holdings.Add("Physical");
        var loan = new Book { Author = favorite.Author };
        loan.SelectedEdition.IsLibraryLoan = true;
        loan.SelectedEdition.Holdings.Add("Physical");
        check(Recommendations.Rank([favorite, physical, ebookOnly, unclassifiedPhysical, soldPhysical, loan], ownedOnly: true)
            .Matches.Select(pick => pick.Book.Id).SequenceEqual(new[] { physical.Id }) &&
            Recommendations.Rank([favorite, ebookOnly]).Matches.Count == 1,
            "Owned unread requires a currently held physical copy and To read; digital, sold, loaned, and unclassified books are excluded");

        var now = new DateTimeOffset(2026, 11, 16, 8, 0, 0, TimeSpan.Zero);
        Recommendations.Snooze(queue[0], now);
        check(queue[0].Status == ReadingStatus.ToRead && queue[0].RecommendationSnoozedUntil == now.AddYears(1) &&
              Recommendations.Rank([rated[0], queue[0]], asOf: now.AddYears(1).AddSeconds(-1)).Matches.Count == 0 &&
              Recommendations.Rank([rated[0], queue[0]], asOf: now.AddYears(1)).Matches.Count == 1,
            "Not now keeps To read and hides the title for exactly one year");
        Recommendations.Snooze(ownedUnread[0], now);
        ReadingRules.Classify(ownedUnread[0], ReadingStatus.ToRead);
        check(Recommendations.Rank([rated[1], ownedUnread[0]], asOf: now).Matches.Count == 0,
            "Changing a snoozed book's reading status keeps its one-year recommendation pause");
        queue[0].ReadNext = true;
        Recommendations.Dismiss(queue[0]);
        check(queue[0].Status == ReadingStatus.Unclassified && !queue[0].ReadNext && Recommendations.IsOwned(queue[0]) &&
              Recommendations.Rank([rated[0], queue[0]], ownedOnly: true, asOf: now.AddYears(10)).Matches.Count == 0,
            "Not interested removes To read and Read next while preserving ownership and suppressing future recommendations");
        ReadingRules.Classify(queue[0], ReadingStatus.ToRead);
        check(Recommendations.Rank([rated[0], queue[0]], asOf: now).Matches.Count == 1,
            "Deliberately returning a book to To read clears its recommendation dismissal");

        var cited = new Book { Title = "The Paper City", Author = "Alex García" };
        var alreadyRead = new Book { Title = "The Stone City", Author = "Sam Carter", Status = ReadingStatus.Read };
        var paused = new Book { Title = "The Snow City", Author = "Morgan Stern" };
        Recommendations.Snooze(paused, now);
        var firstList = CitedRecommendations.ReadText("""
            1. García, Alex. *The Paper City*. First Press, 2019.
            2. García, Alex. *The Paper City*. First Press, 2019.
            3. Carter, Sam. The Stone City. Town: First Press, 2001.
            4. Stern, Morgan. *The Snow City*. First Press, 2020.
            5. Chen, May. “An Article.” *The History Journal* 1 (2): 10–20.
            Some miscellaneous OCR text
            """).ToArray();
        EpubCitation Citation(string title, string author) => new(1, "Bibliography", $"{author}. {title}.",
            title, author, "references.xhtml", null);
        var referenceLists = new Dictionary<Guid, EpubCitation[]>
        {
            [favorite.Id] = firstList,
            [likedTopic.Id] = [Citation(cited.Title, "Alex Garcia"), Citation("A New Book", "Priya Rao"),
                Citation("The History Journal", "May Chen") with { Text = "Chen, May. “An Article.” The History Journal 1 (2): 10–20." }],
            [disliked.Id] = [Citation("A Low-Rated Source's Pick", "Jamie Stone")]
        };
        var citedPicks = CitedRecommendations.Rank([favorite, likedTopic, disliked, cited, alreadyRead, paused],
            id => referenceLists[id], now).ToArray();
        check(firstList.Length == 4 && firstList[2].Title == "The Stone City" &&
              citedPicks.Select(pick => pick.Title).SequenceEqual(new[] { cited.Title, "A New Book" }),
            "Cited recommendations recognize marked and printed book references and omit journals, read books, snoozes, and poorly rated sources");
        check(citedPicks[0].LocalBook?.Id == cited.Id && citedPicks[0].Sources.Count == 2 &&
              citedPicks[0].Sources[0].Source.Id == favorite.Id && citedPicks[1].LocalBook == null,
            "Cited recommendations count distinct liked source books, match local works, and suggest works outside the library");
    }
}
