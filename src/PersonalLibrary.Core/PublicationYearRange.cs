namespace PersonalLibrary.Core;

public readonly record struct PublicationYearRange(double First, double Last)
{
    public double Span => Last - First;

    public static PublicationYearRange ForData(int earliest, int latest)
    {
        var padding = Math.Max(1, ((double)latest - earliest) * .035);
        return new(earliest - padding, latest + padding);
    }

    public PublicationYearRange Zoom(PublicationYearRange fullRange, double positionFromBottom, double wheelSteps)
    {
        var span = Math.Clamp(Span / Math.Pow(1.3, wheelSteps), Math.Min(10, fullRange.Span), fullRange.Span);
        if (span == fullRange.Span) return fullRange;
        var first = Math.Clamp(First + (Span - span) * positionFromBottom, fullRange.First, fullRange.Last - span);
        return new(first, first + span);
    }

    public static long TickStep(double minimum)
    {
        var scale = Math.Pow(10, Math.Floor(Math.Log10(Math.Max(1, minimum))));
        return (long)Math.Ceiling(new[] { scale, 2 * scale, 2.5 * scale, 5 * scale, 10 * scale }
            .First(step => step >= minimum));
    }
}
