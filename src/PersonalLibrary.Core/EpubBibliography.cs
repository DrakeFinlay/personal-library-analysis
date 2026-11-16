namespace PersonalLibrary.Core;

public sealed record EpubCitation(int Number, string Section, string Text, string Title,
    string Author, string ArchiveEntry, string? Anchor);

public sealed record EpubBibliography(string SourceFile, string SourceSha256,
    string[] IntroductoryNotes, EpubCitation[] Entries, int UnreadableDocuments)
{
    public string ToMarkdown()
    {
        var markdown = new System.Text.StringBuilder("# Bibliography\n\n");
        foreach (var note in IntroductoryNotes) markdown.AppendLine(note).AppendLine();
        string? section = null;
        foreach (var citation in Entries)
        {
            if (section != citation.Section)
            {
                section = citation.Section;
                markdown.Append("## ").AppendLine(section).AppendLine();
            }
            markdown.Append(citation.Number).Append(". ").AppendLine(citation.Text).AppendLine();
        }
        return markdown.ToString().TrimEnd();
    }
}
