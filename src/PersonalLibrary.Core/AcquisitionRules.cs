namespace PersonalLibrary.Core;

public static class AcquisitionRules
{
    public static void Mark(Book book, bool asGift)
    {
        var edition = book.SelectedEdition;
        if (edition.PhysicalCopies.Count == 0)
        {
            if (asGift)
            {
                edition.IsGift = true;
                edition.IsLibraryLoan = false;
                return;
            }
            var original = edition.LegacyPurchase();
            original.IsGift = false;
            original.IsLibraryLoan = true;
            edition.PhysicalCopies.Add(original);
            edition.SetLegacyPurchase(new PhysicalCopy());
            edition.UpdatePhysicalHolding();
            return;
        }

        var copy = edition.PhysicalCopies.LastOrDefault(copy => !copy.Sold);
        if (copy == null && !asGift)
        {
            copy = new PhysicalCopy();
            edition.PhysicalCopies.Add(copy);
        }
        copy ??= edition.PhysicalCopies[^1];
        copy.IsGift = asGift;
        copy.IsLibraryLoan = !asGift;
        edition.UpdatePhysicalHolding();
    }
}
