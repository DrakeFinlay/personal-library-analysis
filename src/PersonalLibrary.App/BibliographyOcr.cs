using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using PersonalLibrary.Core;

namespace PersonalLibrary.App;

internal static class BibliographyOcr
{
    public static async Task<OcrPage> ReadAsync(string path, CancellationToken cancellationToken = default)
    {
        using var script = new StreamReader(typeof(BibliographyOcr).Assembly
            .GetManifestResourceStream("PersonalLibrary.BibliographyOcr.ps1")!);
        var command = Convert.ToBase64String(Encoding.Unicode.GetBytes(script.ReadToEnd()));
        var start = new ProcessStartInfo
        {
            FileName = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe"),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-EncodedCommand", command })
            start.ArgumentList.Add(argument);
        start.Environment["PERSONAL_LIBRARY_OCR_IMAGE"] = path;
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Windows OCR could not start.");
        var output = process.StandardOutput.ReadToEndAsync();
        var errors = process.StandardError.ReadToEndAsync();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(1));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill();
            await process.WaitForExitAsync();
            cancellationToken.ThrowIfCancellationRequested();
            throw new InvalidOperationException("Windows OCR timed out on this image.");
        }
        if (process.ExitCode != 0) throw new InvalidOperationException((await errors).Trim());
        return JsonSerializer.Deserialize<OcrPage>((await output).Trim())
            ?? throw new InvalidOperationException("Windows OCR returned no page information.");
    }
}
