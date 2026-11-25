using System.Text;
using System.Text.RegularExpressions;

namespace PersonalLibrary.Core;

public sealed record OcrWord(string Text, double X, double Y, double Width, double Height);
public sealed record OcrLine(string Text, OcrWord[] Words);
public sealed record OcrPage(int Width, int Height, double? Angle, string Language, OcrLine[] Lines, OcrLine[]? WholePageLines = null);
public sealed record PhotoOcrEvidence(string Source, PhotoArea Area, bool ImproveContrast, OcrPage Recognition);

public static class OcrParagraphs
{
    public static string[] ReviewWarnings(OcrPage page)
    {
        var missing = new SortedSet<int>(); int? previous = null;
        foreach (var line in page.Lines)
        {
            var match = Regex.Match(line.Text.Trim(), @"^(?<number>\d{1,3})[.)]?\s+\p{L}");
            if (!match.Success) continue;
            var number = int.Parse(match.Groups["number"].Value, System.Globalization.CultureInfo.InvariantCulture);
            if (previous is { } before && number > before + 1 && number <= before + 6)
                for (var i = before + 1; i < number; i++) missing.Add(i);
            previous = number;
        }
        var warnings = new List<string>();
        if (missing.Count > 0) warnings.Add("Possible missed note numbers: " + string.Join(", ", missing) + ". Compare this area with the photo; numbering can also skip in the source.");
        if (page.Angle is { } angle && Math.Abs(angle) > 7) warnings.Add("Text is still slanted. Consider selecting smaller text blocks.");
        return warnings.ToArray();
    }

    public static string Reconstruct(OcrPage page)
    {
        var lines = page.Lines.Where(line => line.Words.Length > 0 && !string.IsNullOrWhiteSpace(line.Text) &&
            !(Regex.IsMatch(line.Text.Trim(), @"^\d{1,4}$") && line.Words.Min(w => w.X) > page.Width * .65 &&
                (line.Words.Max(w => w.Y + w.Height) < page.Height * .15 || line.Words.Min(w => w.Y) > page.Height * .9))).ToArray();
        if (lines.Length == 0) return "";
        var heights = lines.Select(line => line.Words.Max(w => w.Height)).Order().ToArray();
        var typicalHeight = heights[heights.Length / 2];
        var result = new StringBuilder(); OcrLine? previous = null;
        foreach (var line in lines)
        {
            var value = line.Text.Trim();
            var top = line.Words.Min(w => w.Y);
            var previousBottom = previous?.Words.Max(w => w.Y + w.Height) ?? 0;
            var numbered = Regex.IsMatch(value, @"^\d{1,3}[.)]?\s+\p{L}");
            var heading = Regex.IsMatch(value, @"^(?:Chapter\s+[\dIVXL]|Notes$|Bibliography$|References$)", RegexOptions.IgnoreCase);
            var gap = previous != null && top - previousBottom > typicalHeight * .8;
            if (result.Length > 0)
            {
                if (numbered || heading || gap || Regex.IsMatch(previous!.Text.Trim(), @"^(?:Chapter\s+[\dIVXL]|Notes$|Bibliography$|References$)", RegexOptions.IgnoreCase))
                    result.Append("\n\n");
                else if (result[^1] == '-' && value.Length > 0 && char.IsLower(value[0]) && result.Length > 1 && char.IsLetter(result[^2]))
                    result.Length--;
                else result.Append(' ');
            }
            result.Append(value); previous = line;
        }
        return result.ToString();
    }
}
