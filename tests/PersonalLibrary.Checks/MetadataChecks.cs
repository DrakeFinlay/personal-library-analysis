using System.Net;
using PersonalLibrary.Infrastructure;

internal static class MetadataChecks
{
    public static async Task Run(Action<bool, string> check)
    {
        check(OpenLibraryLookup.NormalizeIsbn("0-9802004-4-x") == "098020044X" &&
            OpenLibraryLookup.NormalizeIsbn("978-0-9802004-4-7") == "9780980200447" &&
            OpenLibraryLookup.NormalizeIsbn("978-0-9802004-4-8") == null &&
            OpenLibraryLookup.NormalizeIsbn("1570719797-658") == null, "ISBN lookup validates checksums and rejects filename-like titles");
        using var handler = new Catalog();
        using var client = new HttpClient(handler);
        var lookup = new OpenLibraryLookup(client);
        var isbn = (await lookup.Search("Bad source title", "Unknown", "978-0-9802004-4-7", default)).Single();
        check(handler.Requests.Count == 1 && isbn.Title == "Catalog title: A subtitle" && isbn.Author == "Test Author"
            && isbn.EditionId == "OL123M" && isbn.Edition?.Pages == 80 && isbn.Edition.Publisher == "Online Press" && isbn.OriginalYear == null,
            "Exact ISBN lookup prefers the edition and leaves original publication year unknown");
        var work = (await lookup.Search("Title & subtitle", "A. Author", "9781626251700", default)).Single();
        check(handler.Requests.Count == 3 && handler.Requests[2].Contains("title=Title%20%26%20subtitle&author=A.%20Author")
            && work.WorkId == "OL456W" && work.OriginalYear == 1902 && work.Edition == null,
            "An ISBN miss falls back to an escaped title and author query without inferring edition metadata");
        var edition = await lookup.ReadEdition(work, default);
        check(edition?.Year == 2009 && edition.Isbn == "9780980200447" && edition.Pages == 80 && edition.Cover?.Scheme == "https",
            "An online work match hydrates only its shown edition and upgrades the cover URL to HTTPS");
        var subtitle = (await lookup.Search("Example Title: A Long Subtitle", "Example Writer", "", default)).Single();
        check(subtitle.UsedMainTitle && handler.Requests[^1].Contains("title=Example%20Title&author=Example%20Writer"),
            "Zero title results retry without the subtitle and retain the author filter");
        var missingCover = (await lookup.Search("", "", "9781982158033", default)).Single();
        check(missingCover.Cover?.AbsolutePath == "/b/isbn/9781982158033-M.jpg"
            && await lookup.ReadCover(missingCover.Cover, default) is { Length: 3 },
            "An ISBN edition without a cover URL can query the Covers API by validated ISBN");
    }

    private sealed class Catalog : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.AbsoluteUri;
            Requests.Add(url);
            if (request.RequestUri.Host == "covers.openlibrary.org")
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([0xff, 0xd8, 0xff]) });
            var data = url switch
            {
                var value when value.Contains("ISBN%3A9780980200447") => "{\"ISBN:9780980200447\":" + Edition + "}",
                var value when value.Contains("ISBN%3A9781626251700") => "{}",
                var value when value.Contains("ISBN%3A9781982158033") => """{"ISBN:9781982158033":{"title":"Example","url":"https://openlibrary.org/books/OL1M"}}""",
                var value when value.Contains("title=Example%20Title%3A") => """{"docs":[]}""",
                var value when value.Contains("OLID%3AOL123M") => "{\"OLID:OL123M\":" + Edition + "}",
                var value when value.Contains("/search.json?") => """
                    {"docs":[{"title":"Catalog title","key":"/works/OL456W","author_name":["Test Author"],
                    "first_publish_year":1902,"editions":{"docs":[{"key":"/books/OL123M"}]}}]}
                    """,
                _ => throw new InvalidOperationException("Unexpected catalog URL " + url)
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(data) });
        }

        private const string Edition = """
            {"title":"Catalog title","subtitle":"A subtitle","url":"https://openlibrary.org/books/OL123M/Catalog_title",
            "authors":[{"name":"Test Author"}],"number_of_pages":80,"publish_date":"May 2009",
            "publishers":[{"name":"Online Press"}],
            "identifiers":{"isbn_13":["9780980200447"]},"cover":{"medium":"http://covers.openlibrary.org/b/id/123-M.jpg"}}
            """;
    }
}
