namespace PersonalLibrary.Core;

public sealed record EpubCitation(int Number, string Section, string Text, string Title,
    string Author, string ArchiveEntry, string? Anchor, CitationAttribution? Attribution = null, string EvidenceSource = "",
    CitationInterpretation? Interpretation = null);

public sealed record OnlineBibliographySource(string Provider, string Title, string Author, string Date,
    string ItemUrl, string FileUrl, DateTimeOffset RetrievedAt);

public sealed record EpubBibliography(string SourceFile, string SourceSha256,
    string[] IntroductoryNotes, EpubCitation[] Entries, int UnreadableDocuments, OnlineBibliographySource? OnlineSource = null)
{
    public string ToMarkdown()
    {
        var markdown = new System.Text.StringBuilder("# Bibliography\n\n");
        if (OnlineSource is { } source)
            markdown.AppendLine($"Source: {source.Title} · {source.Provider} · {source.ItemUrl}").AppendLine();
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
