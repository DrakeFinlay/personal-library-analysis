namespace PersonalLibrary.Core;

public sealed record PhotoTextLayout(PhotoArea[] Areas, string[] Warnings);

// Detect text columns from recognition geometry, rather than assuming that the
// entire photograph is one page. Narrow facing-page scraps are not full pages.
public static class PhotoTextDetection
{
    public static PhotoTextLayout Detect(OcrPage page)
    {
        if (page.Width < 2 || page.Height < 2) return Fallback();
        var words = page.Lines.SelectMany(line => line.Words).Where(word =>
            word.Text.Count(char.IsLetterOrDigit) >= 2 && word.Width > 0 && word.Height > 0 &&
            word.X >= 0 && word.Y >= 0 && word.X + word.Width <= page.Width + 1 && word.Y + word.Height <= page.Height + 1).ToArray();
        if (words.Length < 12) return Fallback();
        var wordSet = words.ToHashSet();
        // Keep separate OCR line envelopes separate even at a narrow gutter.
        // A density histogram alone can join a full page to a facing-page scrap.
        var groups = new List<List<OcrWord[]>>();
        var candidates = page.Lines.Select(line => line.Words.Where(wordSet.Contains).ToArray())
            .Where(row => row.Length >= 3).OrderByDescending(row => row.Max(w => w.X + w.Width) - row.Min(w => w.X)).ToArray();
        foreach (var row in candidates)
        {
            var left = row.Min(w => w.X); var right = row.Max(w => w.X + w.Width);
            var group = groups.FirstOrDefault(group =>
            {
                var seed = group[0]; var l = seed.Min(w => w.X); var r = seed.Max(w => w.X + w.Width);
                return Math.Min(right, r) - Math.Max(left, l) > Math.Min(right - left, r - l) * .6;
            });
            if (group == null) groups.Add([row]); else group.Add(row);
        }
        var areas = new List<PhotoArea>(); var ignored = false;
        var widest = groups.Select(group => group[0].Max(w => w.X + w.Width) - group[0].Min(w => w.X)).DefaultIfEmpty(0).Max();
        foreach (var group in groups)
        {
            var left = group[0].Min(w => w.X); var right = group[0].Max(w => w.X + w.Width);
            if (right - left < Math.Max(page.Width * .18, widest * .55) || group.Count < 3) { ignored = true; continue; }
            var inColumn = words.Where(word => word.X + word.Width / 2 >= left - page.Width * .01 && word.X + word.Width / 2 <= right + page.Width * .01).ToArray();
            if (inColumn.Length < 12) { ignored = true; continue; }
            // Use broad lines to estimate the sides; short headings, note numbers,
            // and paragraph indents must not pinch the crop around the middle.
            var rows = group.Where(row => row.Max(w => w.X + w.Width) - row.Min(w => w.X) > (right - left) * .6).ToArray();
            if (rows.Length < 3) { ignored = true; continue; }
            var top = inColumn.Min(w => w.Y); var bottom = inColumn.Max(w => w.Y + w.Height);
            if (bottom - top < page.Height * .15) { ignored = true; continue; }
            var leftFit = Fit(rows.Select(row => (Y: row.Average(w => w.Y + w.Height / 2), X: row.Min(w => w.X))).ToArray());
            var rightFit = Fit(rows.Select(row => (Y: row.Average(w => w.Y + w.Height / 2), X: row.Max(w => w.X + w.Width))).ToArray());
            // Expand both fitted boundaries until every recognized column word is
            // inside, then add margin for thin/unrecognized letters at the edges.
            var leftOffset = inColumn.Min(w => w.X - leftFit(w.Y));
            var rightOffset = inColumn.Max(w => w.X + w.Width - rightFit(w.Y + w.Height));
            var pad = Math.Max(inColumn.Select(w => w.Height).Order().ElementAt(inColumn.Length / 2), page.Width * .012);
            top = Math.Max(0, top - pad); bottom = Math.Min(page.Height, bottom + pad);
            PhotoPoint Point(double x, double y) => new(Math.Clamp(x / page.Width, 0, 1), y / page.Height);
            var area = new PhotoArea(Point(leftFit(top) + leftOffset - pad, top), Point(rightFit(top) + rightOffset + pad, top),
                Point(rightFit(bottom) + rightOffset + pad, bottom), Point(leftFit(bottom) + leftOffset - pad, bottom));
            try { area.Validate(); areas.Add(area); } catch (InvalidOperationException) { return Fallback(); }
        }
        if (areas.Count == 0 || areas.Count > 2) return Fallback();
        return new(areas.OrderBy(area => area.TopLeft.X).ToArray(), ignored
            ? ["Narrow edge fragments were excluded from the detected text areas. The original photo is retained; use Adjust photo areas if a wanted page was missed."] : []);
    }

    private static PhotoTextLayout Fallback() => new([PhotoArea.Whole],
        ["Page boundaries were uncertain; the whole photo was scanned automatically. Adjust photo areas only if the result needs correction."]);

    private static Func<double, double> Fit((double Y, double X)[] points)
    {
        var x = points.Average(p => p.X); var y = points.Average(p => p.Y);
        var variance = points.Sum(p => (p.Y - y) * (p.Y - y));
        var slope = variance < 1 ? 0 : Math.Clamp(points.Sum(p => (p.Y - y) * (p.X - x)) / variance, -.25, .25);
        return value => x + slope * (value - y);
    }
}
