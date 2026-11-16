using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using PersonalLibrary.Core;

namespace PersonalLibrary.Infrastructure;

public static class EpubBibliographyReader
{
    private static readonly HashSet<string> BibliographyHeadings = new(
        ["bibliography", "selected bibliography", "references", "works cited"], StringComparer.OrdinalIgnoreCase);

    public static EpubBibliography Read(string path)
    {
        using var source = File.OpenRead(path);
        var hash = Convert.ToHexString(SHA256.HashData(source));
        source.Position = 0;
        using var archive = new ZipArchive(source, ZipArchiveMode.Read);
        var entries = new List<EpubCitation>();
        var introductoryNotes = new List<string>();
        var unreadable = 0;
        foreach (var member in archive.Entries.Where(IsContentDocument))
        {
            if (member.Length > 12_000_000)
            {
                unreadable++;
                continue;
            }
            XDocument document;
            try
            {
                using var content = member.Open();
                using var xml = XmlReader.Create(content, new XmlReaderSettings
                {
                    DtdProcessing = DtdProcessing.Ignore, XmlResolver = null, MaxCharactersInDocument = 12_000_000
                });
                document = XDocument.Load(xml, LoadOptions.PreserveWhitespace);
            }
            catch (XmlException)
            {
                unreadable++;
                continue;
            }
            var sections = document.Descendants().Where(element => IsHeading(element)
                    && BibliographyHeadings.Contains(Clean(element.Value)))
                .Select(heading => heading.Ancestors().FirstOrDefault(parent => parent.Name.LocalName is "section" or "body"))
                .OfType<XElement>().Distinct().ToList();
            foreach (var section in sections)
                ReadSection(section, member.FullName, entries, introductoryNotes);
        }
        if (entries.Count == 0)
            throw new InvalidDataException("No dedicated bibliography was found in this EPUB. Its notes can still be added as Markdown.");
        return new EpubBibliography(Path.GetFileName(path), hash, introductoryNotes.ToArray(), entries.ToArray(), unreadable);
    }

    private static void ReadSection(XElement section, string archiveEntry, List<EpubCitation> entries, List<string> notes)
    {
        var marked = section.Descendants().Where(element => HasClass(element, "bib")
            || element.Attributes().Any(attribute => attribute.Name.LocalName == "type"
                && attribute.Value.Split(' ').Contains("biblioentry"))).ToHashSet();
        var group = "Bibliography";
        foreach (var element in section.Descendants())
        {
            if (element.Ancestors().Any(marked.Contains)) continue;
            if (IsHeading(element) || HasClass(element, "sec"))
            {
                var heading = Clean(element.Value);
                if (heading.Length > 0 && !BibliographyHeadings.Contains(heading)) group = heading;
                continue;
            }
            if (marked.Count > 0 && !marked.Contains(element))
            {
                if (element.Name.LocalName == "p" && Clean(element.Value) is { Length: > 0 } note) notes.Add(note);
                continue;
            }
            if (marked.Count == 0 && !(element.Name.LocalName == "li"
                || element.Name.LocalName == "p" && !element.Ancestors().Any(parent => parent.Name.LocalName == "li"))) continue;
            var text = Clean(element.Value);
            if (text.Length == 0) continue;
            var emphasis = element.Descendants().FirstOrDefault(child => child.Name.LocalName is "em" or "i");
            var title = emphasis == null ? "" : Clean(emphasis.Value);
            var author = emphasis == null ? "" : Clean(string.Concat(element.DescendantNodes()
                .TakeWhile(node => node != emphasis).OfType<XText>().Select(node => node.Value))).TrimEnd(':', ',', '.', ' ');
            entries.Add(new EpubCitation(entries.Count + 1, group, text, title, author, archiveEntry, (string?)element.Attribute("id")));
        }
    }

    private static bool IsContentDocument(ZipArchiveEntry member) =>
        Path.GetExtension(member.Name).ToLowerInvariant() is ".html" or ".xhtml" or ".htm";
    private static bool IsHeading(XElement element) => element.Name.LocalName is "h1" or "h2" or "h3" or "h4" or "h5" or "h6";
    private static bool HasClass(XElement element, string name) => ((string?)element.Attribute("class") ?? "").Split(' ').Contains(name);
    private static string Clean(string value) => Regex.Replace(value, @"\s+", " ").Trim();
}
