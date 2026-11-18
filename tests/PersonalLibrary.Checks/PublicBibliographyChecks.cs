using System.IO.Compression;
using System.Net;
using System.Text;
using PersonalLibrary.Core;
using PersonalLibrary.Infrastructure;

internal static class PublicBibliographyChecks
{
    public static async Task Run(string workspace, Action<bool, string> check)
    {
        using var fixture = new ArchiveFixture();
        using var client = new HttpClient(fixture);
        var lookup = new PublicBibliographyLookup(client);
        var results = await lookup.SearchAsync("Test Title", "Jane Sample", "", CancellationToken.None);
        check(results.Count == 1 && results[0].Author == "Sample, Jane" && fixture.Requests[0].AbsolutePath == "/advancedsearch.php"
            && Uri.UnescapeDataString(fixture.Requests[0].Query).Contains("creator:(\"Jane\" AND \"Sample\")"),
            "Public bibliography search looks for editions by title and author and ignores invalid archive IDs");

        var preview = await lookup.PreviewAsync(results[0], CancellationToken.None);
        check(preview.Entries is [{ Title: "The Distant Shore" }] && preview.OnlineSource is { Provider: "Internet Archive" }
            && preview.OnlineSource.ItemUrl.EndsWith("/public-item") && preview.ToMarkdown().Contains(preview.OnlineSource.ItemUrl),
            "Public EPUB import extracts actual bibliography text and records its archive edition");

        var scan = await lookup.PreviewAsync(results[0] with { Id = "scan-item" }, CancellationToken.None);
        check(scan.Entries.Length == 2 && scan.Entries[0].Text.Contains("Example Press") &&
            scan.Entries[1].Title == "The Near Shore" && scan.Entries.All(entry => !entry.Text.Contains("Unrelated")),
            "Public scan import joins wrapped citations across repeated page headers and stops at the index");

        var count = fixture.Requests.Count;
        await ExpectRejected(() => lookup.PreviewAsync(results[0] with { Id = "restricted-item" }, CancellationToken.None));
        check(fixture.Requests.Count == count + 1 && fixture.Requests[^1].AbsolutePath.StartsWith("/metadata/"),
            "Public bibliography preview rejects restricted items before downloading any text");
        await ExpectRejected(() => lookup.PreviewAsync(results[0] with { Id = "no-bibliography" }, CancellationToken.None));
        check(fixture.Requests.All(uri => !uri.AbsolutePath.EndsWith("private.epub")),
            "Public lookup excludes private files and rejects a contents-only bibliography heading");

        var sourceBook = new Book { Title = "Test Title" };
        using var store = new LibraryStore(Path.Combine(workspace, "public-bibliography-target"));
        store.Save(sourceBook);
        store.SaveBibliography(sourceBook.Id, "Local notes", null);
        store.SaveEpubBibliography(sourceBook.Id, preview with { OnlineSource = null });
        store.SaveOnlineBibliography(sourceBook.Id, scan);
        check(store.HasOnlineBibliography(sourceBook.Id) && store.ReadOnlineBibliography(sourceBook.Id)?.Entries.Length == 2
            && store.ReadEpubBibliography(sourceBook.Id)?.OnlineSource == null && store.Bibliography(sourceBook.Id).Text == "Local notes",
            "Saving an online bibliography preserves the local EPUB bibliography and editable notes");
    }

    private static async Task ExpectRejected(Func<Task<EpubBibliography>> action)
    {
        try { await action(); }
        catch (InvalidDataException) { return; }
        throw new InvalidOperationException("The invalid public source was unexpectedly accepted.");
    }

    private sealed class ArchiveFixture : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            Requests.Add(uri);
            HttpContent content = uri.AbsolutePath switch
            {
                "/advancedsearch.php" => new StringContent("""
                    {"response":{"docs":[{"identifier":"public-item","title":"Test Title","creator":["Sample, Jane"],"date":"1923"},
                    {"identifier":"../invalid","title":"Not a valid archive ID"}]}}
                    """),
                "/metadata/public-item" => new StringContent("""
                    {"metadata":{},"files":[{"name":"private.epub","private":"true"},{"name":"public.epub"}]}
                    """),
                "/metadata/restricted-item" => new StringContent("""
                    {"metadata":{"access-restricted-item":true},"files":[{"name":"public.epub"}]}
                    """),
                "/metadata/scan-item" => new StringContent("""{"metadata":{},"files":[{"name":"scan_djvu.txt"}]}"""),
                "/metadata/no-bibliography" => new StringContent("""{"metadata":{},"files":[{"name":"empty_djvu.txt"}]}"""),
                "/download/public-item/public.epub" => new ByteArrayContent(CreateEpub()),
                "/download/scan-item/scan_djvu.txt" => new StringContent("""
                    Contents
                    Bibliography
                    Index

                    Chapter IV
                    Book text.

                    SELECTED BIBLIOGRAPHY
                    Smith, Anna. The Distant Shore. Example:
                    Example Press, 1912.

                    Bibliography
                    xxv

                    Jones, Georgia. The Near Shore. Boston: Cedar Press, 1919.
                    INDEX
                    Green, Walter. Unrelated 1916.
                    """),
                "/download/no-bibliography/empty_djvu.txt" => new StringContent("""
                    CONTENTS
                    Bibliography
                    Chapter 1
                    Smith, Anna. Example: Example Press, 1912.
                    Jones, Georgia. Boston: Cedar Press, 1919.
                    """),
                _ => throw new InvalidOperationException("Unexpected request: " + uri)
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        }

        private static byte[] CreateEpub()
        {
            using var bytes = new MemoryStream();
            using (var zip = new ZipArchive(bytes, ZipArchiveMode.Create, leaveOpen: true))
            using (var writer = new StreamWriter(zip.CreateEntry("refs.xhtml").Open(), Encoding.UTF8))
                writer.Write("<html><body><h1>Bibliography</h1><p>Smith, Anna. <em>The Distant Shore</em>. 1912.</p></body></html>");
            return bytes.ToArray();
        }
    }
}
