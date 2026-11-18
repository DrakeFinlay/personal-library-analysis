using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using PersonalLibrary.Core;

namespace PersonalLibrary.Infrastructure;

public sealed record SourcedMsrp(decimal Amount, PurchaseCurrency Currency, string Source, string Isbn, bool Estimated);
public sealed record SourcedCover(byte[] Bytes, string Source, string Isbn, bool AlternateEdition, bool ReplaceExisting = false);
public sealed record EnrichmentChange(Guid BookId, Guid EditionId, DateTimeOffset Version, string Title,
    SourcedCover? Cover, SourcedMsrp? Msrp);

public sealed class LibraryEnrichment
{
    private static readonly HttpClient DefaultClient = new()
    {
        Timeout = TimeSpan.FromSeconds(12), MaxResponseContentBufferSize = 3 * 1024 * 1024
    };
    private readonly HttpClient client;
    private readonly OpenLibraryLookup library;
    private readonly Dictionary<string, SourcedMsrp?> prices = [];
    private readonly Dictionary<string, string> publisherCovers = [];

    public LibraryEnrichment(HttpClient? client = null)
    {
        this.client = client ?? DefaultClient;
        library = new OpenLibraryLookup(client);
    }

    public async Task<IReadOnlyList<EnrichmentChange>> Find(Book book, bool needsCover, bool estimateMsrp,
        CancellationToken token)
    {
        var found = new Dictionary<Guid, SourcedMsrp>();
        SourcedCover? cover = null;
        foreach (var edition in book.Editions)
        {
            var isbn = OpenLibraryLookup.IsbnKey(edition.Isbn);
            if (isbn.Length == 0) continue;
            if ((edition.Msrp == null || needsCover && cover == null) && await Price(isbn, token) is { } price && edition.Msrp == null)
                found[edition.Id] = price;
            if (needsCover && cover == null && (estimateMsrp || edition.Id == book.SelectedEdition.Id))
                cover = await IsbnCover(isbn, edition.Id != book.SelectedEdition.Id, token);
        }

        if (estimateMsrp && ((needsCover && cover == null) || book.Editions.Any(e => e.Msrp == null && !found.ContainsKey(e.Id))))
        {
            IReadOnlyList<MetadataMatch> matches = [];
            try { matches = await library.Search(book.Title, book.Author.Split(',')[0], "", token); }
            catch (Exception ex) when (Unavailable(ex, token)) { }
            foreach (var match in matches.Where(match => SameWork(book.Title, book.Author, match.Title, match.Author)).Take(2))
            {
                if (needsCover && cover == null && match.Cover != null)
                {
                    var bytes = await ReadLibraryCover(match.Cover, token);
                    if (bytes is { Length: > 2000 }) cover = new(bytes, match.Cover.AbsoluteUri, "", true);
                }
                if (estimateMsrp)
                {
                    EditionMetadata? edition = null;
                    try { edition = await library.ReadEdition(match, token); }
                    catch (Exception ex) when (Unavailable(ex, token)) { }
                    var isbn = OpenLibraryLookup.IsbnKey(edition?.Isbn ?? "");
                    if (isbn.Length > 0 && await Price(isbn, token) is { } comparable)
                    {
                        foreach (var missing in book.Editions.Where(e => e.Msrp == null && !found.ContainsKey(e.Id)))
                            found[missing.Id] = comparable with { Estimated = OpenLibraryLookup.IsbnKey(missing.Isbn) != isbn };
                    }
                }
                if ((!needsCover || cover != null) && book.Editions.All(e => e.Msrp != null || found.ContainsKey(e.Id))) break;
            }
        }

        if (estimateMsrp && found.Values.FirstOrDefault() is { } known)
            foreach (var edition in book.Editions.Where(e => e.Msrp == null && !found.ContainsKey(e.Id)))
                found[edition.Id] = known with { Estimated = true };
        if (estimateMsrp && book.Editions.Any(e => e.Msrp == null && !found.ContainsKey(e.Id)) &&
            await ComparableDistributorPrice(book, token) is { } distributorPrice)
        {
            foreach (var edition in book.Editions.Where(e => e.Msrp == null && !found.ContainsKey(e.Id)))
                found[edition.Id] = distributorPrice with { Estimated = OpenLibraryLookup.IsbnKey(edition.Isbn) != distributorPrice.Isbn };
            if (needsCover && cover == null) cover = await IsbnCover(distributorPrice.Isbn, true, token);
        }
        return book.Editions.Where(e => found.ContainsKey(e.Id) || (e.Id == book.SelectedEdition.Id && cover != null))
            .Select(e => new EnrichmentChange(book.Id, e.Id, book.UpdatedAt, book.Title,
                e.Id == book.SelectedEdition.Id ? cover : null, found.GetValueOrDefault(e.Id))).ToArray();
    }

    private async Task<SourcedCover?> IsbnCover(string isbn, bool alternate, CancellationToken token)
    {
        if (publisherCovers.TryGetValue(isbn, out var publishedSource))
        {
            var publishedImage = await Read(publishedSource, token);
            if (Jpeg(publishedImage)) return new(publishedImage, publishedSource, isbn, alternate);
        }
        var response = await Read($"https://books.google.com/books?jscmd=viewapi&bibkeys=ISBN:{isbn}&callback=bookinfo", token);
        var json = Encoding.UTF8.GetString(response);
        var start = json.IndexOf('(');
        var end = json.LastIndexOf(')');
        if (json.StartsWith("bookinfo(", StringComparison.Ordinal) && start >= 0 && end > start)
        {
            using var document = JsonDocument.Parse(json[(start + 1)..end]);
            if (document.RootElement.TryGetProperty("ISBN:" + isbn, out var book) &&
                book.TryGetProperty("thumbnail_url", out var thumbnail) && thumbnail.GetString() is { } url)
            {
                url = Regex.Replace(url.Replace("http:", "https:"), @"zoom=\d+", "zoom=1");
                if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == "https" && uri.Host == "books.google.com")
                {
                    var image = await Read(url, token);
                    if (Jpeg(image)) return new(image, url, isbn, alternate);
                }
            }
        }
        var source = $"https://covers.openlibrary.org/b/isbn/{isbn}-M.jpg?default=false";
        var bytes = await ReadLibraryCover(new Uri(source), token);
        return bytes != null && Jpeg(bytes) ? new(bytes, source, isbn, alternate) : null;
    }

    private async Task<SourcedMsrp?> Price(string isbn, CancellationToken token)
    {
        if (prices.TryGetValue(isbn, out var cached)) return cached;
        var source = "https://www.penguinrandomhouseretail.com/book/?isbn=" + isbn;
        var page = Encoding.UTF8.GetString(await Read(source, token));
        var price = ParsePublisherPrice(page, isbn, source);
        if (price != null) publisherCovers[isbn] = "https://images.penguinrandomhouse.com/cover/" + isbn;
        if (price == null)
        {
            var search = Encoding.UTF8.GetString(await Read("https://bookpal.com/search.php?search_query=" + isbn, token));
            var card = Regex.Match(search, "data-product-sku=\"" + isbn + "\".*?</article>", RegexOptions.Singleline).Value;
            var link = Regex.Match(card, "href=\"(https://bookpal.com/[^\"<]*" + isbn + "[^\"<]*)\"").Groups[1].Value;
            if (Uri.TryCreate(WebUtility.HtmlDecode(link).Split('?')[0], UriKind.Absolute, out var uri) &&
                uri.Host == "bookpal.com" && uri.Scheme == "https")
            {
                page = Encoding.UTF8.GetString(await Read(uri.AbsoluteUri, token));
                price = ParseDistributorPrice(page, isbn, uri.AbsoluteUri);
                if (price != null) RememberDistributorCover(page, isbn);
            }
        }
        prices[isbn] = price;
        // Publisher pages are paced independently of the Open Library API.
        if (client == DefaultClient) await Task.Delay(550, token);
        return price;
    }

    private async Task<SourcedMsrp?> ComparableDistributorPrice(Book book, CancellationToken token)
    {
        var query = Uri.EscapeDataString(book.Title.Split(':')[0].Split('(')[0]);
        var search = Encoding.UTF8.GetString(await Read("https://bookpal.com/search.php?search_query=" + query, token));
        foreach (Match card in Regex.Matches(search, "data-product-sku=\"([0-9]{13})\".*?</article>", RegexOptions.Singleline))
        {
            var title = Regex.Match(card.Value, "data-name=\"([^\"]+)\"").Groups[1].Value;
            title = Regex.Replace(WebUtility.HtmlDecode(title), @"\s*\[[^]]*\]", "");
            var author = WebUtility.HtmlDecode(Regex.Match(card.Value, "alt=\"[^\"]*book cover by ([^\"]+)\"").Groups[1].Value);
            if (!SameWork(book.Title, book.Author, title, author)) continue;
            var isbn = card.Groups[1].Value;
            var link = Regex.Match(card.Value, "href=\"(https://bookpal.com/[^\"<]*" + isbn + "[^\"<]*)\"").Groups[1].Value;
            if (!Uri.TryCreate(WebUtility.HtmlDecode(link).Split('?')[0], UriKind.Absolute, out var uri) ||
                uri.Host != "bookpal.com" || uri.Scheme != "https") continue;
            var page = Encoding.UTF8.GetString(await Read(uri.AbsoluteUri, token));
            if (ParseDistributorPrice(page, isbn, uri.AbsoluteUri) is { } price)
            {
                RememberDistributorCover(page, isbn);
                return price;
            }
        }
        return null;
    }

    private void RememberDistributorCover(string page, string isbn)
    {
        var source = Regex.Match(page, "src=\"(https://cdn[0-9]+\\.bigcommerce\\.com/[^\"<]*" +
            isbn + "[^\"<]*\\.(?:jpg|png)[^\"<]*)\"").Groups[1].Value;
        if (source.Length > 0) publisherCovers[isbn] = WebUtility.HtmlDecode(source);
    }

    public static SourcedMsrp? ParsePublisherPrice(string page, string isbn, string source)
    {
        if (!Regex.IsMatch(page, "data-component=\"book-detail-meta-isbn\"[^>]*>\\s*" + Regex.Escape(isbn) + @"\s*<"))
            return null;
        var format = Regex.Match(page, "data-component=\"book-detail-meta-format\"[^>]*>(.*?)</div>", RegexOptions.Singleline).Groups[1].Value.Trim();
        if (format is not ("Paperback" or "Hardcover" or "Mass Market Paperback" or "Board Book" or "Leather Bound")) return null;
        var block = Regex.Match(page, "<div class=\"book-detail-price\".*?<!-- /book-detail-price -->", RegexOptions.Singleline).Value;
        var price = Regex.Match(block, "price-numbers[^>]*>\\s*([0-9]+\\.[0-9]{2})</span>\\s*(US|CAD|CAN)");
        if (!price.Success || !decimal.TryParse(price.Groups[1].Value, NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture, out var amount) || amount <= 0) return null;
        return new(amount, price.Groups[2].Value == "US" ? PurchaseCurrency.USD : PurchaseCurrency.CAD, source, isbn, false);
    }

    public static bool SameWork(string title, string author, string candidateTitle, string candidateAuthor)
    {
        string Normalize(string value) => Regex.Replace(
            string.Concat(value.Normalize(NormalizationForm.FormD).Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark))
                .ToLowerInvariant(), @"[^\p{L}\p{N}]+", " ").Trim();
        var first = Normalize(title.Split(':')[0].Split('(')[0]);
        var second = Normalize(candidateTitle.Split(':')[0].Split('(')[0]);
        if (first.Length == 0 || first != second || BookIdentity.DifferentVolumes(title, candidateTitle)) return false;
        var authors = Normalize(author).Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(word => word.Length > 3).ToArray();
        return authors.Length == 0 || authors.Contains("anonymous") || authors.Intersect(Normalize(candidateAuthor).Split(' ')).Any();
    }

    public static SourcedMsrp? ParseDistributorPrice(string page, string isbn, string source)
    {
        if (!Regex.IsMatch(page, "(?:data-product-sku|data-product-name|data-name)=\"[^\"]*" + Regex.Escape(isbn)) ||
            !page.Contains("content=\"USD\"", StringComparison.Ordinal)) return null;
        var format = Regex.Match(page, "<span class=\"bk-kicker\">\\s*(.*?)\\s*</span>", RegexOptions.Singleline).Groups[1].Value.Trim();
        if (format is not ("Paperback" or "Hardcover" or "Mass Market Paperback" or "Board Book" or "Leather Bound")) return null;
        var price = Regex.Match(page, "<div class=\"list\">List\\s*<s>\\$([0-9,]+\\.[0-9]{2})");
        return price.Success && decimal.TryParse(price.Groups[1].Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount) && amount > 0
            ? new(amount, PurchaseCurrency.USD, source, isbn, false) : null;
    }

    private async Task<byte[]> Read(string url, CancellationToken token)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.UserAgent.ParseAdd("PersonalLibrary/1.0");
            using var response = await client.SendAsync(request, token);
            if (!response.IsSuccessStatusCode) return [];
            return await response.Content.ReadAsByteArrayAsync(token);
        }
        catch (Exception ex) when (Unavailable(ex, token)) { return []; }
    }

    private async Task<byte[]?> ReadLibraryCover(Uri uri, CancellationToken token)
    {
        try { return await library.ReadCover(uri, token); }
        catch (Exception ex) when (Unavailable(ex, token)) { return null; }
    }

    private static bool Unavailable(Exception ex, CancellationToken token) =>
        !token.IsCancellationRequested && ex is HttpRequestException or JsonException or TaskCanceledException;

    private static bool Jpeg(byte[] bytes) => bytes.Length > 2000 && bytes[0] == 0xff && bytes[1] == 0xd8;

    public static int Apply(LibraryStore store, IReadOnlyList<EnrichmentChange> changes)
    {
        if (changes.Count == 0) return 0;
        var backup = store.Backup("pre-library-enrichment");
        store.PreviewBackup(backup);
        var current = store.Books().ToDictionary(book => book.Id);
        var pending = new List<Book>();
        foreach (var group in changes.GroupBy(change => change.BookId))
        {
            if (!current.TryGetValue(group.Key, out var book) || book.Excluded || group.Any(change => change.Version != book.UpdatedAt)) continue;
            var updated = false;
            foreach (var change in group)
            {
                var edition = book.Editions.Find(edition => edition.Id == change.EditionId);
                if (edition == null) continue;
                if (change.Cover is { } cover && (store.CoverPath(edition) == null || cover.ReplaceExisting))
                {
                    edition.CoverFile = store.ImportJpegCover(cover.Bytes);
                    edition.CoverSource = cover.Source;
                    edition.CoverSourceIsbn = cover.Isbn;
                    edition.CoverIsAlternateEdition = cover.AlternateEdition;
                    updated = true;
                }
                if (change.Msrp is { } price && edition.Msrp == null)
                {
                    edition.Msrp = price.Amount;
                    edition.MsrpCurrency = price.Currency;
                    edition.MsrpSource = price.Source;
                    edition.MsrpSourceIsbn = price.Isbn;
                    edition.MsrpIsEstimated = price.Estimated;
                    updated = true;
                }
            }
            if (updated) pending.Add(book);
        }
        if (pending.Count == 0) return 0;
        store.SaveBatch(pending);
        return pending.Count;
    }
}
