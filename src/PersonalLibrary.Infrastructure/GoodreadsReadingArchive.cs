using System.Globalization;
using System.IO.Compression;
using System.Text.Json;

namespace PersonalLibrary.Infrastructure;

internal static class GoodreadsReadingArchive
{
    private const int ArchiveLimit = 32 * 1024 * 1024;
    private const int SessionLimit = 16 * 1024 * 1024;
    internal sealed record Session(DateOnly? Started, DateOnly? Finished, bool Completed);

    public static Dictionary<string, Session[]> Read(string path)
    {
        using var archive = ZipFile.OpenRead(path);
        using var buffer = new MemoryStream();
        using (var entry = OpenEntry(archive, "reading_sessions.zip", ArchiveLimit))
            entry.CopyTo(buffer);
        buffer.Position = 0;
        using var nested = new ZipArchive(buffer);
        using var data = OpenEntry(nested, "reading_sessions.json", SessionLimit);
        using var json = JsonDocument.Parse(data);
        if (json.RootElement.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("The Goodreads reading sessions must be a JSON array.");

        var sessions = new List<(string Title, Session Reading)>();
        foreach (var row in json.RootElement.EnumerateArray())
        {
            var title = Value(row, "book").Trim();
            var state = Value(row, "state");
            if (title.Length == 0 || state is not ("READING" or "COMPLETED")) continue;
            var started = Date(Value(row, "started_at"));
            var finished = state == "COMPLETED" ? Date(Value(row, "ended_at")) : null;
            if (started > finished) started = null;
            sessions.Add((title, new Session(started, finished, state == "COMPLETED")));
        }
        return sessions.GroupBy(item => item.Title, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Select(item => item.Reading).Distinct().ToArray(),
                StringComparer.OrdinalIgnoreCase);
    }

    private static Stream OpenEntry(ZipArchive archive, string name, int maxLength)
    {
        var matches = archive.Entries.Where(entry => entry.FullName == name).ToArray();
        if (matches.Length != 1 || matches[0].Length > maxLength)
            throw new InvalidDataException($"The Goodreads archive must contain one supported {name} entry.");
        return matches[0].Open();
    }

    private static string Value(JsonElement row, string key) =>
        row.ValueKind == JsonValueKind.Object && row.TryGetProperty(key, out var value) &&
        value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";

    private static DateOnly? Date(string text) =>
        text.Length >= 10 && DateOnly.TryParseExact(text[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var date) ? date : null;
}
