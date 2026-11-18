namespace PersonalLibrary.Core;

public sealed record PurchaseCount(string Label, int Count);
public sealed record PurchaseTotal(decimal Amount, int Included, int Missing, int AwaitingConversion);
public sealed record CanadianExchangeRates(DateOnly Date, decimal Usd, decimal Jpy, decimal Eur)
{
    public bool IsValid => Date != default && Usd > 0 && Jpy > 0 && Eur > 0;

    public decimal ToCad(PurchaseCurrency currency) => currency switch
    {
        PurchaseCurrency.USD => Usd,
        PurchaseCurrency.JPY => Jpy,
        PurchaseCurrency.EUR => Eur,
        _ => 1m
    };
}

public sealed class PurchaseStatistics
{
    public PurchaseTotal RetailValue { get; }
    public PurchaseTotal Spending { get; }
    public PurchaseTotal SaleProceeds { get; }
    public int EstimatedRetailItems { get; }
    public IReadOnlyList<PurchaseCount> Conditions { get; }
    public IReadOnlyList<PurchaseCount> Vendors { get; }
    public int UnknownCondition { get; }
    public int UnknownVendor { get; }
    public bool HasForeignAmounts { get; }

    public PurchaseStatistics(IEnumerable<Book> books, CanadianExchangeRates? rates = null, bool currentOnly = false)
    {
        var entries = books.Where(book => !book.Excluded).SelectMany(book => book.Editions)
            .SelectMany(Entries).Where(entry => !currentOnly || entry.Owned).ToArray();
        var retail = entries.Select(entry => (entry.Msrp, entry.Currency));
        var spending = entries.Where(entry => !entry.Copy.IsGift).Select(entry => (entry.Copy.PurchasePrice, entry.Copy.PaidCurrency));
        var sales = entries.Where(entry => entry.Copy.Sold).Select(entry => (entry.Copy.SoldFor, entry.Copy.SaleCurrency));
        RetailValue = Total(retail, rates);
        EstimatedRetailItems = entries.Count(entry => entry.Estimated && entry.Msrp.HasValue &&
            (entry.Currency == PurchaseCurrency.CAD || entry.Msrp == 0 || rates is { IsValid: true }));
        Spending = Total(spending, rates);
        SaleProceeds = Total(sales, rates);
        HasForeignAmounts = retail.Concat(spending).Concat(sales)
            .Any(amount => amount.Item1 > 0 && amount.Item2 != PurchaseCurrency.CAD);
        var purchases = entries.Select(entry => entry.Copy).Where(copy => !copy.IsGift).ToArray();
        Conditions = purchases.Where(copy => copy.PurchaseCondition.HasValue)
            .GroupBy(copy => copy.PurchaseCondition!.Value).OrderBy(group => group.Key)
            .Select(group => new PurchaseCount(group.Key.ToString(), group.Count())).ToArray();
        Vendors = purchases.Where(copy => !string.IsNullOrWhiteSpace(copy.PurchasedFrom))
            .GroupBy(copy => string.Join(' ', copy.PurchasedFrom!.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)),
                StringComparer.OrdinalIgnoreCase)
            .Select(group => new PurchaseCount(group.Key, group.Count()))
            .OrderByDescending(item => item.Count).ThenBy(item => item.Label, StringComparer.CurrentCultureIgnoreCase).ToArray();
        UnknownCondition = purchases.Length - Conditions.Sum(item => item.Count);
        UnknownVendor = purchases.Length - Vendors.Sum(item => item.Count);
    }

    private static IEnumerable<(PhysicalCopy Copy, decimal? Msrp, PurchaseCurrency Currency, bool Owned, bool Estimated)> Entries(Edition edition)
    {
        foreach (var copy in edition.PhysicalCopies.Where(copy => !copy.IsLibraryLoan))
            yield return (copy, edition.Msrp, edition.MsrpCurrency, !copy.Sold, edition.MsrpIsEstimated);

        var hasCopies = edition.PhysicalCopies.Count > 0;
        var ownsLegacy = hasCopies ? edition.Holdings.Any(holding => holding != "Physical") : edition.Holdings.Count > 0;
        if (!edition.IsLibraryLoan && (ownsLegacy || edition.HasLegacyPurchase))
            yield return (edition.LegacyPurchase(), hasCopies ? null : edition.Msrp, edition.MsrpCurrency, ownsLegacy, edition.MsrpIsEstimated);
    }

    private static PurchaseTotal Total(IEnumerable<(decimal? Value, PurchaseCurrency Currency)> amounts, CanadianExchangeRates? rates)
    {
        decimal total = 0;
        var included = 0;
        var missing = 0;
        var awaiting = 0;
        foreach (var (amount, currency) in amounts)
        {
            if (amount == null) missing++;
            else if (currency == PurchaseCurrency.CAD || amount == 0) { total += amount.Value; included++; }
            else if (rates is { IsValid: true })
            {
                total += amount.Value * rates.ToCad(currency);
                included++;
            }
            else awaiting++;
        }
        return new(decimal.Round(total, 2, MidpointRounding.AwayFromZero), included, missing, awaiting);
    }

    public static void ValidateAmount(decimal? amount, string label)
    {
        if (amount is { } value && (value < 0 || decimal.Round(value, 2) != value))
            throw new InvalidOperationException($"{label} must be a nonnegative amount with up to two decimal places, or blank.");
    }
}
