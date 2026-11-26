namespace PersonalLibrary.Infrastructure;

public sealed partial class LibraryStore
{
    public Dictionary<string, string> CitationPreferences()
    {
        lock (gate)
        {
            using var connection = Open(); using var command = connection.CreateCommand();
            command.CommandText = "SELECT key,value FROM settings WHERE key LIKE 'citation-recommendation:%'";
            using var reader = command.ExecuteReader(); var result = new Dictionary<string, string>();
            while (reader.Read()) result[reader.GetString(0)] = reader.GetString(1);
            return result;
        }
    }
}
