using PersonalLibrary.Core;
using PersonalLibrary.Infrastructure;
using System.Net;

internal static class EnrichmentChecks
{
    public static async Task Network(Action<bool, string> check)
    {
        using var client = new HttpClient(new Catalog());
        var book = new Book { Title = "Example", Author = "First Writer" };
        book.SelectedEdition.Isbn = "9780140449419";
        var result = (await new LibraryEnrichment(client).Find(book, true, false, default)).Single();
        check(result.Msrp?.Amount == 13 && result.Cover?.Isbn == book.SelectedEdition.Isbn &&
              result.Cover.AlternateEdition == false,
            "Batch lookup combines exact print pricing and a Google Books ISBN cover through its normal entry point");
    }

    public static void Run(string workspace, Action<bool, string> check)
    {
        const string isbn = "9780140449419";
        const string source = "https://www.penguinrandomhouseretail.com/book/?isbn=9780140449419";
        const string page = """
            <div data-component="book-detail-meta-format">Paperback</div>
            <div class="book-detail-price"><span class="price-numbers">13.00</span> US</div><!-- /book-detail-price -->
            <span data-component="book-detail-meta-isbn">9780140449419</span>
            <div class="carousel-meta-price"><span class="price-numbers">90.00</span> US</div>
            """;
        check(LibraryEnrichment.ParsePublisherPrice(page, isbn, source) == new SourcedMsrp(13, PurchaseCurrency.USD, source, isbn, false) &&
              LibraryEnrichment.ParsePublisherPrice(page, "9780140442847", source) == null &&
              LibraryEnrichment.ParsePublisherPrice(page.Replace("Paperback", "Ebook"), isbn, source) == null,
            "Publisher MSRP requires the exact ISBN and a print format and ignores prices for suggested books");
        const string distributor = """
            <meta property="product:price:currency" content="USD" />
            <main data-name="Example [9780140449419]">
            <span class="bk-kicker">Paperback</span>
            <div class="price">$7.67</div><div class="list">List <s>$13.00</s></div>
            </main>
            """;
        check(LibraryEnrichment.ParseDistributorPrice(distributor, isbn, source)?.Amount == 13 &&
              LibraryEnrichment.ParseDistributorPrice(distributor, "9780140442847", source) == null,
            "Distributor lookup uses the explicit ISBN list price and ignores the lower bulk purchase price");
        check(LibraryEnrichment.SameWork("Example: A subtitle", "René Author", "Example", "Rene Author") &&
              !LibraryEnrichment.SameWork("Example", "First Writer", "Example", "Different Person") &&
              !LibraryEnrichment.SameWork("Example, Part 1", "First Writer", "Example, Part 2", "First Writer"),
            "Comparable-edition matching requires title and author evidence and keeps volumes distinct");

        using var store = new LibraryStore(Path.Combine(workspace, "enrichment"));
        var book = new Book { Title = "Example", Author = "First Writer", Notes = "Keep this note", Rating = 5 };
        book.SelectedEdition.Holdings.Add("Physical");
        store.Save(book);
        var price = new SourcedMsrp(13, PurchaseCurrency.USD, source, isbn, true);
        var change = new EnrichmentChange(book.Id, book.SelectedEdition.Id, book.UpdatedAt, book.Title, null, price);
        check(LibraryEnrichment.Apply(store, [change]) == 1 && Directory.GetFiles(store.BackupDirectory, "pre-library-enrichment-*.zip").Length == 1,
            "Bulk enrichment creates a verified profile backup before saving");
        var saved = store.Books().Single();
        check(saved.SelectedEdition.Msrp == 13 && saved.SelectedEdition.MsrpCurrency == PurchaseCurrency.USD &&
              saved.SelectedEdition.MsrpIsEstimated && saved.SelectedEdition.MsrpSource == source &&
              saved.SelectedEdition.MsrpSourceIsbn == isbn && saved.Notes == book.Notes && saved.Rating == book.Rating &&
              new PurchaseStatistics([saved], new CanadianExchangeRates(new(2026, 11, 17), 1.4m, .009m, 1.5m)).EstimatedRetailItems == 1,
            "Estimated MSRP retains source evidence, personal data, and the estimated count in purchase statistics");
        check(LibraryEnrichment.Apply(store, [change]) == 0,
            "A stale enrichment preview cannot overwrite a book changed since the search");
    }

    private sealed class Catalog : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            if (uri.AbsolutePath == "/books/content")
            {
                var bytes = new byte[2200];
                bytes[0] = 0xff;
                bytes[1] = 0xd8;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) });
            }
            var body = uri.Host == "books.google.com"
                ? """bookinfo({"ISBN:9780140449419":{"thumbnail_url":"https://books.google.com/books/content?id=example&zoom=5"}});"""
                : """
                  <div data-component="book-detail-meta-format">Paperback</div>
                  <div class="book-detail-price"><span class="price-numbers">13.00</span> US</div><!-- /book-detail-price -->
                  <span data-component="book-detail-meta-isbn">9780140449419</span>
                  """;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
        }
    }
}
