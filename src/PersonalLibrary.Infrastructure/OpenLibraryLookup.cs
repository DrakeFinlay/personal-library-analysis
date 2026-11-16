using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PersonalLibrary.Infrastructure;

public sealed record MetadataMatch(string Title, string Author, int? OriginalYear, string? WorkId,
    string? EditionId, Uri? Cover, EditionMetadata? Edition = null, bool UsedMainTitle = false)
{
    public string Label => Author.Length == 0 ? Title : $"{Title} — {Author}";
}

public sealed record EditionMetadata(string Title, string Author, string Isbn, int? Year, int? Pages,
    string Publisher, Uri? Cover);

public sealed class OpenLibraryLookup
{
    private static readonly HttpClient DefaultClient = new() { Timeout = TimeSpan.FromSeconds(15), MaxResponseContentBufferSize = 4 * 1024 * 1024 };
    private static readonly SemaphoreSlim RequestGate = new(1);
    private static DateTimeOffset nextRequest;
    private readonly HttpClient client;

    public OpenLibraryLookup(HttpClient? client = null) => this.client = client ?? DefaultClient;

    public static string? NormalizeIsbn(string value)
    {
        var isbn = string.Concat(value.Where(c => c != '-' && !char.IsWhiteSpace(c))).ToUpperInvariant();
        if (isbn.Length == 10 && isbn[..9].All(char.IsAsciiDigit) && (char.IsAsciiDigit(isbn[9]) || isbn[9] == 'X'))
        {
            var check = isbn[9] == 'X' ? 10 : isbn[9] - '0';
            return (isbn[..9].Select((digit, index) => (digit - '0') * (10 - index)).Sum() + check) % 11 == 0 ? isbn : null;
        }
        if (isbn.Length != 13 || !isbn.All(char.IsAsciiDigit) || !(isbn.StartsWith("978") || isbn.StartsWith("979"))) return null;
        return isbn.Select((digit, index) => (digit - '0') * (index % 2 == 0 ? 1 : 3)).Sum() % 10 == 0 ? isbn : null;
    }

    internal static string IsbnKey(string value)
    {
        var isbn = NormalizeIsbn(value);
        if (isbn == null) return "";
        if (isbn.Length == 13) return isbn;
        var digits = "978" + isbn[..9];
        var sum = digits.Select((digit, index) => (digit - '0') * (index % 2 == 0 ? 1 : 3)).Sum();
        return digits + ((10 - sum % 10) % 10).ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    public async Task<IReadOnlyList<MetadataMatch>> Search(string title, string author, string isbn, CancellationToken token)
    {
        if (!string.IsNullOrWhiteSpace(isbn))
        {
            var normalized = NormalizeIsbn(isbn) ?? throw new ArgumentException("Enter a valid ISBN-10 or ISBN-13, or clear ISBN to search by title and author.");
            var exact = await ReadBook("ISBN:" + normalized, normalized, token);
            if (exact is { } match)
                return [new(match.Edition.Title, match.Edition.Author, null, null, match.Id, match.Edition.Cover, match.Edition)];
        }
        if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(author)) return [];
        var matches = await SearchWorks(title, author, token);
        var mainTitle = title.Split(':', 2)[0].Trim();
        if (matches.Length > 0 || mainTitle.Length == 0 || mainTitle == title.Trim()) return matches;
        return (await SearchWorks(mainTitle, author, token)).Select(match => match with { UsedMainTitle = true }).ToArray();
    }

    private async Task<MetadataMatch[]> SearchWorks(string title, string author, CancellationToken token)
    {
        var terms = new List<string>();
        if (!string.IsNullOrWhiteSpace(title)) terms.Add("title=" + Uri.EscapeDataString(title.Trim()));
        if (!string.IsNullOrWhiteSpace(author)) terms.Add("author=" + Uri.EscapeDataString(author.Trim()));
        const string fields = "key,title,author_name,first_publish_year,cover_i,editions,editions.key";
        using var response = await ReadJson("search.json?" + string.Join('&', terms) + "&fields=" + fields + "&limit=12", token);
        return Items(response.RootElement, "docs").Select(work =>
        {
            var edition = Items(Property(work, "editions"), "docs").FirstOrDefault();
            return new MetadataMatch(Str(work, "title"), Names(work, "author_name"), Number(work, "first_publish_year"),
                Id(Str(work, "key"), 'W'), Id(Str(edition, "key"), 'M'), CoverById(Number(work, "cover_i")));
        }).Where(match => match.Title.Length > 0).ToArray();
    }

    public async Task<EditionMetadata?> ReadEdition(MetadataMatch match, CancellationToken token)
    {
        if (match.Edition != null || match.EditionId == null) return match.Edition;
        return (await ReadBook("OLID:" + match.EditionId, "", token))?.Edition;
    }

    public async Task<byte[]?> ReadCover(Uri uri, CancellationToken token)
    {
        if (!IsCoverUri(uri)) return null;
        await Pace(token);
        using var response = await client.GetAsync(uri, token);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        var bytes = await response.Content.ReadAsByteArrayAsync(token);
        return bytes.Length >= 3 && bytes[0] == 0xff && bytes[1] == 0xd8 && bytes[2] == 0xff ? bytes : null;
    }

    private async Task<(string? Id, EditionMetadata Edition)?> ReadBook(string key, string isbn, CancellationToken token)
    {
        using var response = await ReadJson("api/books?bibkeys=" + Uri.EscapeDataString(key) + "&jscmd=data&format=json", token);
        var book = Property(response.RootElement, key);
        var title = Str(book, "title");
        if (title.Length == 0) return null;
        var subtitle = Str(book, "subtitle");
        if (subtitle.Length > 0 && !title.Contains(subtitle, StringComparison.OrdinalIgnoreCase)) title += ": " + subtitle;
        var ids = Property(book, "identifiers");
        if (isbn.Length == 0)
            isbn = Items(ids, "isbn_13").Concat(Items(ids, "isbn_10")).Select(value => NormalizeIsbn(Str(value))).FirstOrDefault(value => value != null) ?? "";
        var yearText = Regex.Match(Str(book, "publish_date"), @"\b[0-9]{4}\b", RegexOptions.RightToLeft).Value;
        var cover = Uri.TryCreate(Str(Property(book, "cover"), "medium"), UriKind.Absolute, out var uri) && IsCoverUri(uri)
            ? new UriBuilder(uri) { Scheme = "https", Port = -1, Query = "default=false" }.Uri : CoverByIsbn(isbn);
        return (Id(Str(book, "url"), 'M'), new(title, Names(book, "authors"), isbn, int.TryParse(yearText, out var year) && year > 0 ? year : null,
            Number(book, "number_of_pages") is > 0 and var pages ? pages : null, Names(book, "publishers"), cover));
    }

    private async Task<JsonDocument> ReadJson(string path, CancellationToken token)
    {
        await Pace(token);
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://openlibrary.org/" + path);
        request.Headers.UserAgent.ParseAdd("PersonalLibrary/1.0");
        using var response = await client.SendAsync(request, token);
        response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync(token));
    }

    private static async Task Pace(CancellationToken token)
    {
        await RequestGate.WaitAsync(token);
        try
        {
            var wait = nextRequest - DateTimeOffset.UtcNow;
            if (wait > TimeSpan.Zero) await Task.Delay(wait, token);
            nextRequest = DateTimeOffset.UtcNow.AddSeconds(1);
        }
        finally { RequestGate.Release(); }
    }

    private static bool IsCoverUri(Uri uri) => (uri.Scheme is "http" or "https") && uri.IsDefaultPort
        && uri.Host == "covers.openlibrary.org" && Regex.IsMatch(uri.AbsolutePath, @"^/b/(id/[0-9]+|isbn/([0-9]{13}|[0-9]{9}[0-9X]))-M\.jpg$");

    internal static Uri? CoverById(int? id) => id > 0 ? new($"https://covers.openlibrary.org/b/id/{id}-M.jpg?default=false") : null;
    private static Uri? CoverByIsbn(string isbn) => NormalizeIsbn(isbn) is { } valid
        ? new($"https://covers.openlibrary.org/b/isbn/{valid}-M.jpg?default=false") : null;
    internal static string? Id(string value, char kind)
    {
        var id = Regex.Match(value, $@"(?:^|/)(OL[0-9]+{kind})(?:/|$)");
        return id.Success ? id.Groups[1].Value : null;
    }
    private static JsonElement Property(JsonElement value, string key) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(key, out var result) ? result : default;
    internal static IEnumerable<JsonElement> Items(JsonElement value, string key) =>
        Property(value, key) is { ValueKind: JsonValueKind.Array } array ? array.EnumerateArray() : [];
    internal static string Str(JsonElement value) => value.ValueKind == JsonValueKind.String ? value.GetString()!.Trim() : "";
    internal static string Str(JsonElement value, string key) => Str(Property(value, key));
    private static string Names(JsonElement value, string key) =>
        string.Join(", ", Items(value, key).Select(item => item.ValueKind == JsonValueKind.String ? Str(item) : Str(item, "name"))
            .Where(name => name.Length > 0));
    internal static int? Number(JsonElement value, string key) =>
        Property(value, key) is { ValueKind: JsonValueKind.Number } number && number.TryGetInt32(out var result) ? result : null;
}
