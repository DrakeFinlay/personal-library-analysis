using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using PersonalLibrary.Core;

namespace PersonalLibrary.App;

internal sealed record AutomaticPhotoPlan(string Source, string SourceSha256, int QuarterTurns, PhotoTextLayout Layout, OcrPage Detection);

internal static class AutomaticPhotoOcr
{
    public static async Task<AutomaticPhotoPlan> AnalyzeAsync(string source, string folder, CancellationToken cancellationToken)
    {
        // EXIF is respected before trying reading orientations. Preview recognition
        // locates columns; final OCR runs on separately straightened full-size areas.
        OcrPage? best = null; var turns = 0; var bestScore = double.NegativeInfinity;
        var preview = Path.Combine(folder, "layout-preview.png");
        for (var rotation = 0; rotation < 4; rotation++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = rotation;
            await Task.Run(() => PhotoPageImage.Prepare(source, PhotoArea.Whole, true, preview, current, 2200), cancellationToken);
            var page = await BibliographyOcr.ReadAsync(preview, cancellationToken, wholePageOnly: true);
            var score = Score(page);
            if (score > bestScore) { best = page; bestScore = score; turns = rotation; }
        }
        await using var input = File.OpenRead(source);
        var hash = Convert.ToHexString(await SHA256.HashDataAsync(input, cancellationToken));
        var result = new AutomaticPhotoPlan(Path.GetFileName(source), hash, turns, PhotoTextDetection.Detect(best!), best!);
        await File.WriteAllTextAsync(Path.Combine(folder, "photo-layout.json"), JsonSerializer.Serialize(result), cancellationToken);
        return result;
    }

    private static double Score(OcrPage page) => page.Lines.SelectMany(line => line.Words).Sum(word =>
    {
        var letters = word.Text.Count(char.IsLetter); var other = word.Text.Count(c => !char.IsLetterOrDigit(c) && !char.IsPunctuation(c));
        return letters >= 3 ? Math.Min(letters, 12) - other * 2 : 0d;
    });
}
