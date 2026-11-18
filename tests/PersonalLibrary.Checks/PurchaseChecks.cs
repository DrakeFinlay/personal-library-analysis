using System.Net;
using PersonalLibrary.Core;
using PersonalLibrary.Infrastructure;

internal static class PurchaseChecks
{
    public static async Task Run(string workspace, Action<bool, string> check)
    {
        var books = new[]
        {
            new Book { Title = "Purchased editions", Status = ReadingStatus.MusicBook, Editions =
            [
                new Edition
                {
                    Msrp = 100, MsrpCurrency = PurchaseCurrency.USD, PurchasePrice = 80, PurchaseCondition = PurchaseCondition.New,
                    PurchasedFrom = "Corner Books", Holdings = ["Physical", "Ebook"]
                },
                new Edition
                {
                    Msrp = 2000, MsrpCurrency = PurchaseCurrency.JPY, PurchasePrice = 12, PaidCurrency = PurchaseCurrency.EUR,
                    PurchaseCondition = PurchaseCondition.Used, PurchasedFrom = " corner   books "
                }
            ] },
            new Book { Title = "Owned with unknown amounts", Editions = [new Edition { Holdings = ["Physical"] }] },
            new Book { Title = "Free purchase", Editions = [new Edition { PurchasePrice = 0, PaidCurrency = PurchaseCurrency.JPY }] },
            new Book { Title = "Unowned title", Editions = [new Edition { Msrp = 999 }] },
            new Book { Title = "Excluded title", Excluded = true, Editions = [new Edition { PurchasePrice = 500 }] }
        };
        var withoutRates = new PurchaseStatistics(books);
        check(withoutRates.RetailValue == new PurchaseTotal(0, 0, 2, 2) &&
              withoutRates.Spending == new PurchaseTotal(80, 2, 1, 1),
            "Purchase totals distinguish missing prices, unavailable exchange rates, and a recorded zero price");

        using var client = new HttpClient(new ExchangeFixture());
        var rates = await new BankOfCanadaLookup(client).FetchAsync();
        var withRates = new PurchaseStatistics(books, rates);
        check(rates.Date == new DateOnly(2026, 11, 16) &&
              withRates.RetailValue == new PurchaseTotal(143, 2, 2, 0) &&
              withRates.Spending == new PurchaseTotal(98, 3, 1, 0),
            "The latest complete Bank of Canada observation converts mixed USD, JPY and EUR prices into CAD");
        check(withRates.Conditions.SequenceEqual([new PurchaseCount("New", 1), new PurchaseCount("Used", 1)]) &&
              withRates.Vendors.SequenceEqual([new PurchaseCount("Corner Books", 2)]) &&
              withRates.UnknownCondition == 2 && withRates.UnknownVendor == 2,
            "Purchase charts count each edition once, preserve blank coverage, and group normalized vendor names");

        using var store = new LibraryStore(Path.Combine(workspace, "purchase-target"));
        store.SaveBatch(books);
        BankOfCanadaLookup.Save(store, rates);
        var saved = store.Books().Single(book => book.Title == "Purchased editions").Editions[1];
        check(saved.PaidCurrency == PurchaseCurrency.EUR && saved.PurchaseCondition == PurchaseCondition.Used &&
              saved.Msrp == 2000m && saved.PurchasePrice == 12m && saved.PurchasedFrom?.Trim() == "corner   books" &&
              BankOfCanadaLookup.Cached(store) == rates,
            "Optional purchase fields and the offline exchange-rate cache survive profile reads");

        var duplicatePurchase = new Book { Title = "Anthology, seventh edition" };
        var physical = duplicatePurchase.SelectedEdition;
        physical.Msrp = 100;
        physical.Holdings = ["Ebook"];
        physical.PhysicalCopies =
        [
            new() { PurchaseYear = 2014, PurchasePrice = 40, Sold = true, SoldFor = 10, SaleCurrency = PurchaseCurrency.USD },
            new() { PurchaseYear = 2026, PurchasePrice = 60, PurchasedFrom = "Campus Books" }
        ];
        physical.UpdatePhysicalHolding();
        store.Save(duplicatePurchase);
        var savedBook = store.Books().Single(book => book.Id == duplicatePurchase.Id);
        var savedEdition = savedBook.SelectedEdition;
        var lifetime = new PurchaseStatistics([savedBook], rates);
        var current = new PurchaseStatistics([savedBook], rates, currentOnly: true);
        check(savedEdition.PhysicalCopies.Select(copy => copy.PurchaseYear).SequenceEqual([2014, 2026]) &&
              lifetime.RetailValue == new PurchaseTotal(200, 2, 1, 0) &&
              lifetime.Spending.Amount == 100 && lifetime.SaleProceeds == new PurchaseTotal(12.50m, 1, 0, 0) &&
              current.RetailValue == new PurchaseTotal(100, 1, 1, 0) && current.Spending.Amount == 60,
            "Same-edition repurchases survive a save and sold copies contribute only to lifetime totals");
        savedEdition.PhysicalCopies[1].Sold = true;
        savedEdition.UpdatePhysicalHolding();
        check(savedEdition.Holdings.SequenceEqual(["Ebook"]) &&
              new PurchaseStatistics([savedBook], rates, currentOnly: true).Spending.Included == 0 &&
              new PurchaseStatistics([savedBook], rates).SaleProceeds.Missing == 1,
            "Selling the last physical copy preserves ebook ownership and leaves unknown proceeds blank");
        var legacyCurrent = new PurchaseStatistics(books, rates, currentOnly: true);
        check(legacyCurrent.RetailValue == new PurchaseTotal(125, 1, 1, 0) && legacyCurrent.Spending.Amount == 80,
            "Current purchase totals require ownership for existing editions without copy history");

        var gift = new Book { Title = "Gift copy", Editions = [new Edition { Msrp = 30, Holdings = ["Physical"], IsGift = true }] };
        var loan = new Book { Title = "Library reading", Editions = [new Edition { Msrp = 100, Holdings = ["Physical"], IsLibraryLoan = true }] };
        var mixed = new Book { Title = "Different copies", Editions = [new Edition
        {
            Msrp = 20, PhysicalCopies =
            [
                new() { IsGift = true, PurchasedFrom = "Gift store" },
                new() { IsLibraryLoan = true },
                new() { PurchasePrice = 10, PurchasedFrom = "Amazon" }
            ]
        }] };
        mixed.SelectedEdition.UpdatePhysicalHolding();
        var acquisitions = new PurchaseStatistics([gift, loan, mixed]);
        store.SaveBatch([gift, loan, mixed]);
        var reloaded = store.Books().Single(book => book.Id == mixed.Id);
        check(acquisitions.RetailValue == new PurchaseTotal(70, 3, 0, 0) &&
              acquisitions.Spending == new PurchaseTotal(10, 1, 0, 0) &&
              acquisitions.Vendors.SequenceEqual([new PurchaseCount("Amazon", 1)]) &&
              reloaded.SelectedEdition.PhysicalCopies[0].IsGift &&
              reloaded.SelectedEdition.PhysicalCopies[1].IsLibraryLoan &&
              store.Books().Single(book => book.Id == gift.Id).SelectedEdition.IsGift,
            "Gift and library flags survive saving and distinguish owned gift value from actual purchasing");
        var onlyLoan = new Book { Title = "Borrowed copy", Editions = [new Edition
        {
            Msrp = 40, Holdings = ["Physical"], PhysicalCopies = [new() { IsLibraryLoan = true }]
        }] };
        onlyLoan.SelectedEdition.UpdatePhysicalHolding();
        check(!Recommendations.IsOwned(onlyLoan) && !Recommendations.IsOwned(loan) &&
              Recommendations.IsOwned(gift) &&
              new PurchaseStatistics([onlyLoan], currentOnly: true).RetailValue.Included == 0,
            "Library loans stay out of current ownership and financial statistics while gifts remain owned");

        var bulk = new Book { Title = "Bulk acquisition", Review = "Saved review", Rating = 5 };
        var selectedEdition = bulk.SelectedEdition;
        selectedEdition.Holdings = ["Physical", "Ebook"];
        selectedEdition.PurchasePrice = 25;
        selectedEdition.PurchasedFrom = "Amazon";
        var otherEdition = new Edition();
        bulk.Editions.Add(otherEdition);
        ReadingRules.Classify(bulk, ReadingStatus.Read);
        var readingId = bulk.Readings.Single().Id;
        AcquisitionRules.Mark(bulk, asGift: true);
        check(selectedEdition.IsGift && !otherEdition.IsGift &&
              new PurchaseStatistics([bulk]).Spending.Included == 0,
            "Bulk Gift marks the selected edition and uses gift spending rules");
        AcquisitionRules.Mark(bulk, asGift: false);
        store.Save(bulk);
        var bulkSaved = store.Books().Single(book => book.Id == bulk.Id);
        var borrowed = bulkSaved.SelectedEdition.PhysicalCopies.Single();
        check(borrowed.IsLibraryLoan && !borrowed.IsGift && borrowed.PurchasePrice == 25 &&
              borrowed.PurchasedFrom == "Amazon" && bulkSaved.SelectedEdition.Holdings.SequenceEqual(["Ebook"]) &&
              bulkSaved.Readings.Single().Id == readingId && bulkSaved.Status == ReadingStatus.Read &&
              bulkSaved.Review == "Saved review" && bulkSaved.Rating == 5,
            "Bulk Library preserves reading and purchase history and retains independent digital ownership");
        var soldBook = new Book { Title = "Sold purchases", Editions = [new Edition
        {
            PhysicalCopies = [new() { Sold = true, PurchasePrice = 40, SoldFor = 15 }]
        }] };
        AcquisitionRules.Mark(soldBook, asGift: false);
        AcquisitionRules.Mark(soldBook, asGift: true);
        check(soldBook.SelectedEdition.PhysicalCopies is [ { Sold: true, PurchasePrice: 40, SoldFor: 15 }, { IsGift: true, IsLibraryLoan: false, Sold: false } ] &&
              Recommendations.IsOwned(soldBook),
            "Bulk acquisition changes preserve sold copies and target the latest unsold copy");
    }

    private sealed class ExchangeFixture : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""
                    {"observations": [
                        {"d":"2026-11-16","FXUSDCAD":{"v":"1.25"},"FXJPYCAD":{"v":"0.009"},"FXEURCAD":{"v":"1.5"}},
                        {"d":"2026-11-17","FXUSDCAD":{"v":"1.29"}}
                    ]}
                    """)
            });
    }
}
