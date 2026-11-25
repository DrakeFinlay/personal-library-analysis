using System.Text.RegularExpressions;

namespace PersonalLibrary.Core;

public static class NoteReferences
{
    // These are candidates, not accepted identities. Keep the complete note as
    // evidence for every candidate; commentary and unresolved short forms survive.
    public static IEnumerable<EpubCitation> Read(string text)
    {
        var section = "Pasted or scanned notes"; var source = "bibliography.md"; var number = 0;
        foreach (var part in Regex.Split(text.Replace("\r", ""), @"\n\s*\n"))
        {
            var block = part.Trim();
            if (block.StartsWith("## Photo:", StringComparison.Ordinal)) { section = block.TrimStart('#', ' '); source = section; continue; }
            if (Regex.IsMatch(block, @"^(?:Chapter\s+[\dIVXL]|Notes$|Bibliography$|References$)", RegexOptions.IgnoreCase)) { section = block; continue; }
            if (block.Length == 0 || block.StartsWith('#') || block.StartsWith('>')) continue;
            var full = Regex.Matches(block,
                @"(?:^|;\s*)(?:(?:\d{1,3}[.)]?|[Il](?=\s+See\b))\s+)?(?:See(?:,\s*e\.g\.,)?\s+)?(?<author>[\p{Lu}][\p{L}\p{M}’'.,&\- ]{1,100}?),\s+(?<title>[^;\r\n]{3,300}?)\s*\([^()\r\n]{3,180}\b(?:1[4-9]\d{2}|20\d{2})\b[^()\r\n]{0,30}\)",
                RegexOptions.CultureInvariant);
            foreach (Match match in full)
            {
                var title = match.Groups["title"].Value.Trim(); var author = match.Groups["author"].Value.Trim();
                if (title.StartsWith('“') || title.StartsWith('‘') || title.StartsWith('"') || author.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length > 12) continue;
                yield return new(++number, section, block, title, author, source, null);
            }
            // A numbered note remains reviewable even if all its references are
            // abbreviated, corrupted, articles, or mixed into a prose argument.
            if (Regex.IsMatch(block, @"^\d{1,3}[.)]?\s+\p{L}") || source != "bibliography.md" && block.Count(char.IsLetter) > 25)
                yield return new(++number, section + " · Full note (review)", block, "", "", source, null);
        }
    }
}
