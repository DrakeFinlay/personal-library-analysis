$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)
try {
    Add-Type -AssemblyName System.Runtime.WindowsRuntime
    $null = [Windows.Storage.StorageFile, Windows.Storage, ContentType=WindowsRuntime]
    $null = [Windows.Graphics.Imaging.BitmapDecoder, Windows.Graphics.Imaging, ContentType=WindowsRuntime]
    $null = [Windows.Media.Ocr.OcrEngine, Windows.Foundation, ContentType=WindowsRuntime]
    $asTask = [System.WindowsRuntimeSystemExtensions].GetMethods() | Where-Object {
        $_.Name -eq 'AsTask' -and $_.IsGenericMethod -and $_.GetGenericArguments().Length -eq 1 -and $_.GetParameters().Length -eq 1
    } | Select-Object -First 1
    function Get-Result($operation, [type]$resultType) {
        $task = $asTask.MakeGenericMethod($resultType).Invoke($null, @($operation))
        return $task.GetAwaiter().GetResult()
    }
    $engine = [Windows.Media.Ocr.OcrEngine]::TryCreateFromUserProfileLanguages()
    if ($null -eq $engine) { throw 'Install an OCR language in Windows language settings, then try again.' }

    $file = Get-Result ([Windows.Storage.StorageFile]::GetFileFromPathAsync($env:PERSONAL_LIBRARY_OCR_IMAGE)) ([Windows.Storage.StorageFile])
    $stream = Get-Result ($file.OpenReadAsync()) ([Windows.Storage.Streams.IRandomAccessStreamWithContentType, Windows.Storage.Streams, ContentType=WindowsRuntime])
    try {
        $decoder = Get-Result ([Windows.Graphics.Imaging.BitmapDecoder]::CreateAsync($stream)) ([Windows.Graphics.Imaging.BitmapDecoder])
        $transform = [Windows.Graphics.Imaging.BitmapTransform, Windows.Graphics.Imaging, ContentType=WindowsRuntime]::new()
        $limit = [Windows.Media.Ocr.OcrEngine]::MaxImageDimension
        $ratio = [Math]::Min(1, $limit / [Math]::Max($decoder.PixelWidth, $decoder.PixelHeight))
        if ($ratio -lt 1) {
            $transform.ScaledWidth = [uint32][Math]::Max(1, [Math]::Floor($decoder.PixelWidth * $ratio))
            $transform.ScaledHeight = [uint32][Math]::Max(1, [Math]::Floor($decoder.PixelHeight * $ratio))
        }
        $bitmap = Get-Result ($decoder.GetSoftwareBitmapAsync(
            [Windows.Graphics.Imaging.BitmapPixelFormat, Windows.Graphics.Imaging, ContentType=WindowsRuntime]::Bgra8,
            [Windows.Graphics.Imaging.BitmapAlphaMode, Windows.Graphics.Imaging, ContentType=WindowsRuntime]::Ignore,
            $transform,
            [Windows.Graphics.Imaging.ExifOrientationMode, Windows.Graphics.Imaging, ContentType=WindowsRuntime]::RespectExifOrientation,
            [Windows.Graphics.Imaging.ColorManagementMode, Windows.Graphics.Imaging, ContentType=WindowsRuntime]::DoNotColorManage
        )) ([Windows.Graphics.Imaging.SoftwareBitmap, Windows.Graphics.Imaging, ContentType=WindowsRuntime])
        try {
            $result = Get-Result ($engine.RecognizeAsync($bitmap)) ([Windows.Media.Ocr.OcrResult, Windows.Foundation, ContentType=WindowsRuntime])
            $lines = @($result.Lines | ForEach-Object {
                @{ Text = $_.Text; Words = @($_.Words | ForEach-Object {
                    @{ Text = $_.Text; X = $_.BoundingRect.X; Y = $_.BoundingRect.Y; Width = $_.BoundingRect.Width; Height = $_.BoundingRect.Height }
                }) }
            })
            # Short overlapping bands reduce the effect of different baselines
            # across a curved page. Only each band's central rows are retained;
            # overlap protects letters at a cut without duplicating citations.
            $wholePageLines = $lines
            if ($bitmap.PixelHeight -gt 1400 -and $env:PERSONAL_LIBRARY_OCR_WHOLE_ONLY -ne '1') {
                $bandLines = [System.Collections.Generic.List[object]]::new()
                $bandHeight = 900
                for ($start = 0; $start -lt $bitmap.PixelHeight; $start += $bandHeight) {
                    $top = [Math]::Max(0, $start - 180)
                    $bottom = [Math]::Min($bitmap.PixelHeight, $start + $bandHeight + 180)
                    $bandTransform = [Windows.Graphics.Imaging.BitmapTransform]::new()
                    $bounds = [Windows.Graphics.Imaging.BitmapBounds, Windows.Graphics.Imaging, ContentType=WindowsRuntime]::new()
                    $bounds.X = 0; $bounds.Y = [uint32]$top; $bounds.Width = [uint32]$bitmap.PixelWidth; $bounds.Height = [uint32]($bottom - $top)
                    $bandTransform.Bounds = $bounds
                    $bandBitmap = Get-Result ($decoder.GetSoftwareBitmapAsync(
                        [Windows.Graphics.Imaging.BitmapPixelFormat]::Bgra8, [Windows.Graphics.Imaging.BitmapAlphaMode]::Ignore,
                        $bandTransform, [Windows.Graphics.Imaging.ExifOrientationMode]::RespectExifOrientation,
                        [Windows.Graphics.Imaging.ColorManagementMode]::DoNotColorManage
                    )) ([Windows.Graphics.Imaging.SoftwareBitmap])
                    try {
                        $band = Get-Result ($engine.RecognizeAsync($bandBitmap)) ([Windows.Media.Ocr.OcrResult])
                        foreach ($line in $band.Lines) {
                            $centers = @($line.Words | ForEach-Object { $_.BoundingRect.Y + $_.BoundingRect.Height / 2 } | Sort-Object)
                            if ($centers.Count -eq 0) { continue }
                            $center = $centers[[int][Math]::Floor($centers.Count / 2)] + $top
                            if ($center -lt $start -or $center -ge [Math]::Min($bitmap.PixelHeight, $start + $bandHeight)) { continue }
                            $words = @($line.Words | ForEach-Object { @{ Text = $_.Text; X = $_.BoundingRect.X;
                                Y = $_.BoundingRect.Y + $top; Width = $_.BoundingRect.Width; Height = $_.BoundingRect.Height } })
                            $bandLines.Add(@{ Text = $line.Text; Words = $words })
                        }
                    } finally { $bandBitmap.Dispose() }
                }
                if ($bandLines.Count -gt 0) { $lines = @($bandLines.ToArray()) }
            }
            @{ Width = $bitmap.PixelWidth; Height = $bitmap.PixelHeight; Angle = $result.TextAngle;
               Language = $engine.RecognizerLanguage.LanguageTag; Lines = $lines; WholePageLines = $wholePageLines } | ConvertTo-Json -Depth 8 -Compress
        } finally { $bitmap.Dispose() }
    } finally { $stream.Dispose() }
} catch {
    [Console]::Error.WriteLine($_.Exception.Message)
    exit 1
}
