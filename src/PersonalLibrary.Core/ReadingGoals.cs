namespace PersonalLibrary.Core;

public static class ReadingGoals
{
    public static bool Completed(Book book, int year) =>
        ReadingRules.CompletedReadings(book).Any(reading => reading.CompletionYear == year);

    public static IEnumerable<Book> RecentlyChanged(IEnumerable<Book> books) =>
        books.Where(book => !book.Excluded).OrderByDescending(book => book.UpdatedAt)
            .ThenBy(book => book.Title, StringComparer.CurrentCultureIgnoreCase).ThenBy(book => book.Id).Take(10);
}
