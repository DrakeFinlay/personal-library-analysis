using System.Globalization;
using System.Text.Json;
using PersonalLibrary.Core;

namespace PersonalLibrary.Infrastructure;

public sealed class BankOfCanadaLookup(HttpClient? client = null)
{
    private const string CacheKey = "purchase-cad-exchange-rates";
    private const string Source = "https://www.bankofcanada.ca/valet/observations/FXUSDCAD,FXJPYCAD,FXEURCAD/json?recent=7";
    private static readonly HttpClient DefaultClient = new()
    {
        Timeout = TimeSpan.FromSeconds(12),
        MaxResponseContentBufferSize = 128 * 1024
    };

    public static CanadianExchangeRates? Cached(LibraryStore store)
    {
        var saved = store.Setting(CacheKey, "");
        if (saved.Length == 0) return null;
        try
        {
            var rates = JsonSerializer.Deserialize<CanadianExchangeRates>(saved, LibraryStore.Json);
            return rates is { IsValid: true } ? rates : null;
        }
        catch (JsonException) { return null; }
    }

    public static void Save(LibraryStore store, CanadianExchangeRates rates) =>
        store.SetSetting(CacheKey, JsonSerializer.Serialize(rates, LibraryStore.Json));

    public async Task<CanadianExchangeRates> FetchAsync(CancellationToken cancellationToken = default)
    {
        using var response = await (client ?? DefaultClient).GetAsync(Source, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false));
        if (json.RootElement.TryGetProperty("observations", out var observations) && observations.ValueKind == JsonValueKind.Array)
        {
            var latest = observations.EnumerateArray().Select(row =>
                DateOnly.TryParseExact(Text(row, "d"), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                    ? new CanadianExchangeRates(date, Rate(row, "FXUSDCAD"), Rate(row, "FXJPYCAD"), Rate(row, "FXEURCAD"))
                    : null)
                .Where(rates => rates is { IsValid: true }).MaxBy(rates => rates!.Date);
            if (latest != null) return latest;
        }
        throw new InvalidDataException("The Bank of Canada did not return a complete set of exchange rates.");
    }

    private static decimal Rate(JsonElement row, string key) =>
        row.ValueKind == JsonValueKind.Object && row.TryGetProperty(key, out var value) &&
        decimal.TryParse(Text(value, "v"), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var rate) ? rate : 0;

    private static string? Text(JsonElement row, string key) =>
        row.ValueKind == JsonValueKind.Object && row.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() : null;
}
