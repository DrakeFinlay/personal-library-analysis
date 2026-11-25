using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using PersonalLibrary.Core;

namespace PersonalLibrary.Infrastructure;

public static class EpubBibliographyReader
{
    private static readonly HashSet<string> BibliographyHeadings = new(
        ["bibliography", "selected bibliography", "select bibliography", "general bibliography", "annotated bibliography",
            "references", "selected references", "works cited", "bibliography of works cited", "works consulted", "further reading", "recommended reading",
            "suggested reading", "bibliography and guide to further reading", "bibliography and further reading", "sources and further reading",
            "literaturverzeichnis", "bibliographie", "参考文献", "引用文献"], StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> BackMatterHeadings = new(
        ["acknowledgments", "acknowledgements", "index", "notes", "endnotes", "footnotes", "glossary",
            "sources of illustrations", "about the author", "copyright", "permissions", "table of contents", "some final thoughts"],
        StringComparer.OrdinalIgnoreCase);
    private static readonly Regex HtmlEntity = new(@"&[A-Za-z][A-Za-z0-9]+;", RegexOptions.CultureInvariant);

    public static EpubBibliography Read(string path)
    {
        using var source = File.OpenRead(path);
        return Read(source, Path.GetFileName(path));
    }

    internal static EpubBibliography Read(Stream source, string filename, long? contentLimit = null)
    {
        var hash = Convert.ToHexString(SHA256.HashData(source));
        source.Position = 0;
        using var archive = new ZipArchive(source, ZipArchiveMode.Read, leaveOpen: true);
        var documents = archive.Entries.Where(IsContentDocument).ToArray();
        if (contentLimit is { } limit && (documents.Length > 2_000 || documents.Sum(member => (decimal)member.Length) > limit))
            throw new InvalidDataException("This edition contains too much text to preview.");
        var entries = new List<EpubCitation>();
        var introductoryNotes = new List<string>();
        var unreadable = 0;
        foreach (var member in documents)
        {
            if (member.Length > 12_000_000)
            {
                unreadable++;
                continue;
            }
            XDocument document;
            try
            {
                document = ReadDocument(member);
            }
            catch (XmlException)
            {
                unreadable++;
                continue;
            }
            var headings = document.Descendants().Where(IsBibliographyHeading).ToArray();
            foreach (var heading in headings)
            {
                var section = heading.Ancestors().FirstOrDefault(parent => parent.Name.LocalName is "section" or "body");
                if (section == null) continue;
                var blocks = section.Descendants().SkipWhile(element => element != heading).Skip(1)
                    .Where(element => !IsNavigationOrTable(element) &&
                        (element.Name.LocalName is "p" or "li" || IsHeading(element) || IsMarkedCitation(element)))
                    .TakeWhile(element => !IsBibliographyHeading(element) && !IsEndOfSection(heading, element));
                var chapter = IsReferenceHeading(heading) ? Clean(section.Descendants().TakeWhile(element => element != heading)
                    .LastOrDefault(element => element.Name.LocalName == "h1")?.Value ?? "") : "";
                ReadSection(blocks.ToArray(), member.FullName, chapter, entries, introductoryNotes);
            }
        }
        if (entries.Count == 0)
            throw new InvalidDataException("No dedicated bibliography was found in this EPUB. Its notes can still be added as Markdown.");
        return new EpubBibliography(filename, hash, introductoryNotes.ToArray(), entries.ToArray(), unreadable);
    }

    private static XDocument ReadDocument(ZipArchiveEntry member)
    {
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Ignore, XmlResolver = null, MaxCharactersInDocument = 12_000_000
        };
        try
        {
            using var content = member.Open();
            using var reader = XmlReader.Create(content, settings);
            return XDocument.Load(reader, LoadOptions.PreserveWhitespace);
        }
        catch (XmlException)
        {
            using var content = new StreamReader(member.Open());
            var normalized = HtmlEntity.Replace(content.ReadToEnd(), match =>
            {
                var decoded = WebUtility.HtmlDecode(match.Value);
                return decoded == match.Value ? match.Value : string.Concat(decoded.EnumerateRunes().Select(rune => $"&#x{rune.Value:X};"));
            });
            using var text = new StringReader(normalized);
            using var reader = XmlReader.Create(text, settings);
            return XDocument.Load(reader, LoadOptions.PreserveWhitespace);
        }
    }

    private static void ReadSection(XElement[] blocks, string archiveEntry, string chapter, List<EpubCitation> entries, List<string> notes)
    {
        var marked = blocks.Where(IsMarkedCitation).ToHashSet();
        var hasCitation = false;
        var group = chapter.Length == 0 ? "Bibliography" : chapter;
        foreach (var element in blocks)
        {
            if (element.Ancestors().Any(parent => marked.Contains(parent) || parent.Name.LocalName is "nav" or "li")) continue;
            var text = Clean(element.Value);
            if (text.Length == 0) continue;
            if (IsHeading(element) || HasClass(element, "sec") || HasClass(element, "h1-n") ||
                !marked.Contains(element) && text.Length <= 60 && text.Any(char.IsUpper) && !text.Any(char.IsLower) && !text.Any(char.IsDigit))
            {
                group = text;
                continue;
            }
            if (marked.Count > 0 && !marked.Contains(element))
            {
                if (!hasCitation && element.Name.LocalName == "p") notes.Add(text);
                continue;
            }
            if (!hasCitation && marked.Count == 0 && IsIntroductoryNote(text)) { notes.Add(text); continue; }
            var emphasis = element.Descendants().FirstOrDefault(child => child.Name.LocalName is "em" or "i");
            var title = emphasis == null ? "" : Clean(emphasis.Value);
            var author = emphasis == null ? "" : Clean(string.Concat(element.DescendantNodes()
                .TakeWhile(node => node != emphasis).OfType<XText>().Select(node => node.Value))).TrimEnd(':', ',', '.', ' ');
            entries.Add(new EpubCitation(entries.Count + 1, group, text, title, author, archiveEntry, (string?)element.Attribute("id")));
            hasCitation = true;
        }
    }

    private static bool IsBibliographyHeading(XElement element) =>
        (IsHeading(element) || element.Name.LocalName is "p" or "div" && !element.Descendants().Any(child => child.Name.LocalName is "p" or "div" or "li" || IsHeading(child))) &&
        BibliographyHeadings.Contains(Clean(element.Value).TrimEnd(':', '.')) &&
        !IsNavigationOrTable(element) && !element.Ancestors().Any(parent => parent.Name.LocalName == "li") &&
        !element.Descendants().Any(child => child.Name.LocalName == "a" && child.Attribute("href") is { } href &&
            Clean(child.Value).Length > 0 && !IsContentsLink(href.Value));

    private static bool IsReferenceHeading(XElement element) => Clean(element.Value).Equals("References", StringComparison.OrdinalIgnoreCase);

    private static bool IsEndOfSection(XElement heading, XElement element)
    {
        if (IsHeading(element) && IsHeading(heading) && element.Name.LocalName[1] <= heading.Name.LocalName[1]) return true;
        var text = Clean(element.Value).TrimEnd(':', '.');
        return BackMatterHeadings.Contains(text) || text.StartsWith("Index of ", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsNavigationOrTable(XElement element) =>
        element.AncestorsAndSelf().Any(parent => parent.Name.LocalName is "nav" or "table");

    private static bool IsContentsLink(string href)
    {
        var file = Path.GetFileNameWithoutExtension(href.Split('#', 2)[0]);
        return file.Contains("contents", StringComparison.OrdinalIgnoreCase) || file.Equals("toc", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsMarkedCitation(XElement element) =>
        ((string?)element.Attribute("class") ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Any(name => name.Equals("bib", StringComparison.OrdinalIgnoreCase) || name.Equals("bib1", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("bib-bot", StringComparison.OrdinalIgnoreCase) || name.Equals("para-bib", StringComparison.OrdinalIgnoreCase) ||
                element.Name.LocalName == "dt" && name.Equals("biblio", StringComparison.OrdinalIgnoreCase) ||
                element.Name.LocalName == "div" && name.Equals("references", StringComparison.OrdinalIgnoreCase)) ||
        element.Attributes().Any(attribute => attribute.Name.LocalName == "type" && attribute.Value.Split(' ').Contains("biblioentry"));

    private static bool IsIntroductoryNote(string text) => text.StartsWith("Note:", StringComparison.OrdinalIgnoreCase) ||
        text.StartsWith("This bibliography", StringComparison.OrdinalIgnoreCase) || text.StartsWith("This section", StringComparison.OrdinalIgnoreCase) ||
        text.StartsWith("The bibliography of", StringComparison.OrdinalIgnoreCase);

    private static bool IsContentDocument(ZipArchiveEntry member) =>
        Path.GetExtension(member.Name).ToLowerInvariant() is ".html" or ".xhtml" or ".htm";
    private static bool IsHeading(XElement element) => element.Name.LocalName is "h1" or "h2" or "h3" or "h4" or "h5" or "h6";
    private static bool HasClass(XElement element, string name) => ((string?)element.Attribute("class") ?? "").Split(' ').Contains(name, StringComparer.OrdinalIgnoreCase);
    private static string Clean(string value) => Regex.Replace(value, @"\s+", " ").Trim();
}
