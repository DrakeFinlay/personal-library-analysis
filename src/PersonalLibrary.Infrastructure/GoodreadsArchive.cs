using System.Globalization;
using System.IO.Compression;
using System.Text.Json;

namespace PersonalLibrary.Infrastructure;

internal static class GoodreadsArchive
{
    private const int ArchiveLimit = 32 * 1024 * 1024;
    private const int JsonLimit = 16 * 1024 * 1024;
    internal sealed record Session(DateOnly? Started, DateOnly? Finished, bool Completed, int? FinishYear);
    internal sealed record Rating(string Title, int? Stars);

    private static JsonDocument ReadJson(string path, string name)
    {
        using var archive = ZipFile.OpenRead(path);
        using var buffer = new MemoryStream();
        using (var entry = OpenEntry(archive, name + ".zip", ArchiveLimit))
            entry.CopyTo(buffer);
        buffer.Position = 0;
        using var nested = new ZipArchive(buffer);
        using var data = OpenEntry(nested, name + ".json", JsonLimit);
        return JsonDocument.Parse(data);
    }

    public static Dictionary<string, Session[]> ReadSessions(string path)
    {
        using var json = ReadJson(path, "reading_sessions");
        if (json.RootElement.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("The Goodreads reading sessions must be a JSON array.");

        var sessions = new List<(string Title, Session Reading)>();
        foreach (var row in json.RootElement.EnumerateArray())
        {
            var title = Value(row, "book").Trim();
            var state = Value(row, "state");
            if (title.Length == 0 || state is not ("READING" or "COMPLETED")) continue;
            var started = Date(Value(row, "started_at"));
            var end = state == "COMPLETED" ? Value(row, "ended_at").Trim() : "";
            var finished = Date(end);
            int? finishYear = end.Length == 4 && int.TryParse(end, NumberStyles.None, CultureInfo.InvariantCulture, out var year)
                && year is >= 1 and <= 9999 ? year : null;
            if (started > finished) started = null;
            if (started?.Year > finishYear) started = null;
            sessions.Add((title, new Session(started, finished, state == "COMPLETED", finishYear)));
        }
        return sessions.GroupBy(item => item.Title, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Select(item => item.Reading).Distinct().ToArray(),
                StringComparer.OrdinalIgnoreCase);
    }

    public static Rating[] ReadRatings(string path)
    {
        using var json = ReadJson(path, "reviews");
        if (json.RootElement.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("The Goodreads review archive must be a JSON array.");
        var sections = json.RootElement.EnumerateArray()
            .Where(row => row.ValueKind == JsonValueKind.Object && row.TryGetProperty("reviews", out _)).ToArray();
        if (sections.Length != 1 || sections[0].GetProperty("reviews").ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("The Goodreads archive must contain one reviews array.");
        var ratings = new List<Rating>();
        foreach (var review in sections[0].GetProperty("reviews").EnumerateArray())
        {
            var title = Value(review, "book").Trim();
            if (title.Length == 0) continue;
            var stars = review.TryGetProperty("rating", out var value) && value.ValueKind == JsonValueKind.Number &&
                value.TryGetDecimal(out var number) && number is >= 1 and <= 5 && number == decimal.Truncate(number)
                ? (int)number : (int?)null;
            ratings.Add(new(title, stars));
        }
        return ratings.ToArray();
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
