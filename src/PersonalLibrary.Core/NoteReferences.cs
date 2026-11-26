namespace PersonalLibrary.Core;

public static class NoteReferences
{
    public static IEnumerable<EpubCitation> Read(string text) => CitationProcessing.ReadText(text);
}
