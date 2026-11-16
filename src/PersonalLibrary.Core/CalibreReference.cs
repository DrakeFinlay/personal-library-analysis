namespace PersonalLibrary.Core;

public sealed record CalibreFile(string Format, string RelativePath);

public sealed record CalibreMetadata(string Title, string Author, string Isbn, string Language,
    int? PublicationYear, string Publisher, string Series, double? SeriesIndex,
    string Description, string[] Tags, string[] Identifiers, CalibreFile[] Files,
    string? CoverFile);

// Keep the last source values for comparison with independently edited personal fields.
public sealed record CalibreReference(Guid LibraryId, Guid BookId, string LibraryFolder,
    Guid EditionId, CalibreMetadata Metadata);
