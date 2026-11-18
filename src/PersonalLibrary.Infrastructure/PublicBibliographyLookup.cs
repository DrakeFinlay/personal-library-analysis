using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using PersonalLibrary.Core;

namespace PersonalLibrary.Infrastructure;

public sealed record PublicBookEdition(string Id, string Title, string Author, string Date)
{
    public Uri Page => new("https://archive.org/details/" + Uri.EscapeDataString(Id));
    public string Label => string.Join(" · ", new[] { Title, Author, Date }.Where(value => value.Length > 0));
}

public sealed partial class PublicBibliographyLookup
{
    private const int FileLimit = 40 * 1024 * 1024;
    private static readonly HttpClient DefaultClient = new(new HttpClientHandler
    {
        AllowAutoRedirect = false,
        AutomaticDecompression = DecompressionMethods.All,
        UseCookies = false
    });
    private readonly HttpClient client;

    public PublicBibliographyLookup(HttpClient? client = null) => this.client = client ?? DefaultClient;

    public async Task<IReadOnlyList<PublicBookEdition>> SearchAsync(string title, string author, string isbn,
        CancellationToken cancellationToken)
    {
        var terms = new List<string> { "mediatype:texts", "-access-restricted-item:true" };
        if (!string.IsNullOrWhiteSpace(isbn))
        {
            var normalized = OpenLibraryLookup.NormalizeIsbn(isbn) ??
                throw new ArgumentException("Enter a valid ISBN, or clear it to search by title and author.");
            terms.Add($"isbn:{normalized}");
        }
        else
        {
            if (string.IsNullOrWhiteSpace(title)) throw new ArgumentException("Enter a title or an ISBN.");
            terms.Add("title:" + Phrase(title));
            var authors = Words().Matches(author).Select(match => Phrase(match.Value)).ToArray();
            if (authors.Length > 0) terms.Add("creator:(" + string.Join(" AND ", authors) + ")");
        }
        var query = Uri.EscapeDataString(string.Join(" AND ", terms));
        using var json = await ReadJson(new($"https://archive.org/advancedsearch.php?q={query}&fl[]=identifier&fl[]=title&fl[]=creator&fl[]=date&rows=20&output=json"), cancellationToken);
        var docList = Property(Property(json.RootElement, "response"), "docs");
        if (docList.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("Internet Archive did not return a search result.");
        return docList.EnumerateArray().Select(doc => new PublicBookEdition(Text(doc, "identifier"), Text(doc, "title"),
            Text(doc, "creator"), Text(doc, "date"))).Where(item => ItemId().IsMatch(item.Id) && item.Title.Length > 0)
            .DistinctBy(item => item.Id).ToArray();
    }

    public async Task<EpubBibliography> PreviewAsync(PublicBookEdition edition, CancellationToken cancellationToken)
    {
        if (!ItemId().IsMatch(edition.Id)) throw new ArgumentException("The archive item identifier is invalid.");
        using var json = await ReadJson(new($"https://archive.org/metadata/{edition.Id}"), cancellationToken);
        var metadata = Property(json.RootElement, "metadata");
        if (metadata.ValueKind != JsonValueKind.Object || Restricted(metadata, "access-restricted-item") ||
            Restricted(metadata, "is_dark") || Restricted(json.RootElement, "is_dark"))
            throw new InvalidDataException("This edition is unavailable as a public download.");
        var files = Property(json.RootElement, "files");
        if (files.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("This edition has no public files.");
        var names = files.EnumerateArray().Where(file => !Restricted(file, "private") &&
                (!long.TryParse(Text(file, "size"), out var size) || size <= FileLimit))
            .Select(file => Text(file, "name"))
            .Where(name => name.Length > 0 && !name.Contains('/') && !name.Contains('\\')).ToArray();
        var epub = names.FirstOrDefault(name => name.EndsWith(".epub", StringComparison.OrdinalIgnoreCase));
        var scan = names.FirstOrDefault(name => name.EndsWith("_djvu.txt", StringComparison.OrdinalIgnoreCase));
        if (epub == null && scan == null)
            throw new InvalidDataException("This edition has no public EPUB or scan text that the app can read.");

        var lastProblem = new InvalidDataException("No dedicated bibliography was found in this edition.");
        foreach (var name in new[] { epub, scan }.OfType<string>())
        {
            var url = new Uri($"https://archive.org/download/{edition.Id}/{Uri.EscapeDataString(name)}");
            try
            {
                using var content = await Download(url, FileLimit, cancellationToken);
                var result = await Task.Run(() => name == epub
                    ? EpubBibliographyReader.Read(content, name, contentLimit: 56 * 1024 * 1024)
                    : ReadScanText(content, name), cancellationToken);
                return result with
                {
                    OnlineSource = new("Internet Archive", edition.Title, edition.Author, edition.Date,
                        edition.Page.AbsoluteUri, url.AbsoluteUri, DateTimeOffset.UtcNow)
                };
            }
            catch (InvalidDataException ex) { lastProblem = ex; }
            catch (HttpRequestException ex) when (ex.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized)
            {
                lastProblem = new InvalidDataException("The archive did not make this file available for public download.", ex);
            }
        }
        throw lastProblem;
    }

    private async Task<JsonDocument> ReadJson(Uri url, CancellationToken cancellationToken)
    {
        using var bytes = await Download(url, 4 * 1024 * 1024, cancellationToken);
        return await JsonDocument.ParseAsync(bytes, cancellationToken: cancellationToken);
    }

    private async Task<MemoryStream> Download(Uri url, int limit, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(35));
        var token = deadline.Token;
        for (var redirects = 0; redirects <= 4; redirects++)
        {
            if (url.Scheme != Uri.UriSchemeHttps || !url.IsDefaultPort || url.UserInfo.Length > 0 ||
                !(url.Host == "archive.org" || url.Host.EndsWith(".archive.org", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("The download redirected outside Internet Archive.");
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.UserAgent.ParseAdd("PersonalLibrary/1.0 (interactive bibliography lookup)");
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
            if ((int)response.StatusCode is >= 300 and < 400 && response.Headers.Location is { } next)
            {
                url = new Uri(url, next);
                continue;
            }
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength > limit)
                throw new InvalidDataException("The public source file is too large to preview.");
            await using var input = await response.Content.ReadAsStreamAsync(token);
            var output = new MemoryStream();
            try
            {
                var buffer = new byte[32 * 1024];
                int read;
                while ((read = await input.ReadAsync(buffer, token)) > 0)
                {
                    if (output.Length + read > limit) throw new InvalidDataException("The public source file is too large to preview.");
                    output.Write(buffer, 0, read);
                }
                output.Position = 0;
                return output;
            }
            catch { output.Dispose(); throw; }
        }
        throw new InvalidDataException("The archive download redirected too many times.");
    }

    internal static EpubBibliography ReadScanText(Stream source, string name)
    {
        var hash = Convert.ToHexString(SHA256.HashData(source));
        source.Position = 0;
        using var reader = new StreamReader(source, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        var lines = reader.ReadToEnd().Replace("\r", "").Split('\n');
        for (var heading = 0; heading < lines.Length; heading++)
        {
            var label = Spaces().Replace(lines[heading].Trim(), " ").TrimEnd(':', '.');
            if (label.Length > 60 || !ScanHeading().IsMatch(label)) continue;
            var entries = new List<EpubCitation>();
            var pending = new StringBuilder();
            var strongEntries = 0;
            var count = 0;
            for (var index = heading + 1; index < lines.Length; index++)
            {
                var line = Spaces().Replace(lines[index].Trim(), " ");
                if (line.Length < 80 && ScanEnd().IsMatch(line)) break;
                if (index - heading > 3_500 || count > 200_000 || (index - heading > 55 && strongEntries < 2))
                {
                    entries.Clear();
                    break;
                }
                if (line.Equals(label, StringComparison.OrdinalIgnoreCase) ||
                    (label.Contains("bibliograph", StringComparison.OrdinalIgnoreCase) &&
                        PlainBibliography().IsMatch(line))) continue;
                if (line.Length == 0 || (line.Length < 12 && PageNumber().IsMatch(line)))
                {
                    FinishCitation();
                    continue;
                }
                if (CitationStart().IsMatch(line)) FinishCitation();
                if (pending.Length > 0) pending.Append(' ');
                pending.Append(line);
                count += line.Length;
            }
            FinishCitation();
            if (entries.Count >= 2 && strongEntries >= 2)
                return new(name, hash, ["Text recovered from the archive’s scan. Check the original for OCR errors."], entries.ToArray(), 0);

            void FinishCitation()
            {
                if (pending.Length == 0) return;
                var text = pending.ToString();
                pending.Clear();
                if (text.Count(char.IsLetter) < Math.Max(6, text.Length / 2)) return;
                var parsed = CitedRecommendations.ReadText(text).FirstOrDefault();
                if (CitationStart().IsMatch(text) && CitationDate().IsMatch(text)) strongEntries++;
                entries.Add(new(entries.Count + 1, label, text, parsed?.Title ?? "", parsed?.Author ?? "", name, null));
            }
        }
        throw new InvalidDataException("No recognizable bibliography section was found in the public scan text.");
    }

    private static JsonElement Property(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) ? value : default;

    private static string Text(JsonElement element, string property)
    {
        var value = Property(element, property);
        return value.ValueKind switch
        {
            JsonValueKind.Array => string.Join("; ", value.EnumerateArray().Select(item => item.ToString())),
            JsonValueKind.Undefined or JsonValueKind.Null => "",
            _ => value.ToString()
        };
    }

    private static bool Restricted(JsonElement element, string property) =>
        Text(element, property).Trim().ToLowerInvariant() is not ("" or "0" or "false" or "no");

    private static string Phrase(string value) => "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    [GeneratedRegex(@"^[a-zA-Z0-9][a-zA-Z0-9_.-]{0,119}$")]
    private static partial Regex ItemId();
    [GeneratedRegex(@"[\p{L}\p{N}]+")]
    private static partial Regex Words();
    [GeneratedRegex(@"^(?:(?:Selected|Select|General|Annotated) )?Bibliograph(?:y|ie)(?: of works cited)?$|^(?:Works cited|References|Further reading)$", RegexOptions.IgnoreCase)]
    private static partial Regex ScanHeading();
    [GeneratedRegex(@"^(?:Index(?: of .+)?|Appendix(?:\s.*)?|Glossary|Endnotes|Notes|Preface|Introduction|About the author|(?:Chapter|Part|Book)\s+[IVXLC\d]+[.:]?(?:\s.*|[—–-].*)?|\*\*\*\s*END.*)$", RegexOptions.IgnoreCase)]
    private static partial Regex ScanEnd();
    [GeneratedRegex(@"^Bibliograph(?:y|ie)[.:]?$", RegexOptions.IgnoreCase)]
    private static partial Regex PlainBibliography();
    [GeneratedRegex(@"^(?:\d{1,3}|[IVXLCDM]{1,8})$", RegexOptions.IgnoreCase)]
    private static partial Regex PageNumber();
    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();
    [GeneratedRegex(@"^(?:\[?\d{1,3}[.)\]]\s+)?[\p{Lu}][\p{L}'’ .-]{1,40},\s+(?:[\p{Lu}]|\()")]
    private static partial Regex CitationStart();
    [GeneratedRegex(@"\b(?:1[4-9]\d{2}|20\d{2})\b")]
    private static partial Regex CitationDate();
}
