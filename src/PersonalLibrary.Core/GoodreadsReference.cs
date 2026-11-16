namespace PersonalLibrary.Core;

public sealed record GoodreadsReference(string BookId, Dictionary<string, string> Fields,
    string[] Shelves, Guid EditionId, Guid? CompletionId);
