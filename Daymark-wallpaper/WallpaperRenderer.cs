using System.Globalization;
using SkiaSharp;

namespace DaymarkWallpaper;

public static class WallpaperRenderer
{
    public static void Render(DateOnly date, int width, int height, string destinationPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        var geometry = WallpaperLogic.CreateGeometry(date, width, height);
        var palette = WallpaperLogic.PaletteFor(date);
        var imageInfo = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(imageInfo) ?? throw new InvalidOperationException("Unable to create the wallpaper surface.");
        var canvas = surface.Canvas;
        DrawBackground(canvas, width, height, palette);
        DrawDateCard(canvas, geometry.DateCard, date, palette, height);
        DrawDayGrid(canvas, geometry.Days, date.DayOfYear, palette);
        DrawProgress(canvas, geometry.ProgressBounds, date, palette, height);

        using var image = surface.Snapshot();
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100)
            ?? throw new InvalidOperationException("Unable to encode the wallpaper image.");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destinationPath))!);
        using var output = File.Create(destinationPath);
        encoded.SaveTo(output);
    }

    private static void DrawBackground(SKCanvas canvas, int width, int height, WallpaperPalette palette)
    {
        using var shader = SKShader.CreateLinearGradient(
            new SKPoint(0, 0),
            new SKPoint(width, height),
            [palette.Background, palette.BackgroundHighlight],
            [0f, 1f],
            SKShaderTileMode.Clamp);
        using var paint = new SKPaint { Shader = shader, IsAntialias = true };
        canvas.DrawRect(new SKRect(0, 0, width, height), paint);
    }

    private static void DrawDateCard(SKCanvas canvas, SKRect bounds, DateOnly date, WallpaperPalette palette, int height)
    {
        var radius = height * 0.008f;
        using var border = new SKPaint
        {
            Color = palette.Text.WithAlpha(42),
            Style = SKPaintStyle.Stroke,
            StrokeWidth = Math.Max(1f, height * 0.0012f),
            IsAntialias = true
        };
        using var topAccent = new SKPaint
        {
            Color = palette.Accent.WithAlpha(105),
            StrokeWidth = Math.Max(1f, height * 0.0016f),
            IsAntialias = true
        };
        using var cardFill = new SKPaint { Color = palette.Text.WithAlpha(4), IsAntialias = true };
        canvas.DrawRoundRect(bounds, radius, radius, cardFill);
        canvas.DrawRoundRect(bounds, radius, radius, border);
        canvas.DrawLine(bounds.Left + bounds.Width * 0.24f, bounds.Top, bounds.Right - bounds.Width * 0.24f, bounds.Top, topAccent);

        var centerX = bounds.MidX;
        DrawCenteredText(canvas, date.ToString("dd", CultureInfo.InvariantCulture), centerX, bounds.Top + bounds.Height * 0.36f,
            palette.Text, height * 0.076f, new SKFontStyle(300, 5, SKFontStyleSlant.Upright), bounds.Width * 0.80f);
        DrawCenteredText(canvas, date.ToString("MMMM", CultureInfo.InvariantCulture).ToUpperInvariant(), centerX, bounds.Top + bounds.Height * 0.68f,
            palette.MutedText, height * 0.018f, new SKFontStyle(500, 5, SKFontStyleSlant.Upright), bounds.Width * 0.80f);
        DrawCenteredText(canvas, date.ToString("yyyy", CultureInfo.InvariantCulture), centerX, bounds.Top + bounds.Height * 0.84f,
            palette.MutedText.WithAlpha(175), height * 0.016f, SKFontStyle.Normal, bounds.Width * 0.80f);
    }

    private static void DrawDayGrid(SKCanvas canvas, IReadOnlyList<DayCell> days, int currentDay, WallpaperPalette palette)
    {
        using var pastPaint = new SKPaint
        {
            Color = palette.Accent.WithAlpha(145),
            IsAntialias = true
        };
        using var todayPaint = new SKPaint { Color = palette.Accent, IsAntialias = true };
        using var futurePaint = new SKPaint
        {
            Color = palette.MutedText.WithAlpha(58),
            Style = SKPaintStyle.Stroke,
            StrokeWidth = Math.Max(1f, days[0].Bounds.Width * 0.045f),
            IsAntialias = true
        };

        var today = days[currentDay - 1];
        using var blur = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, today.Bounds.Width * 0.22f);
        using var glow = new SKPaint
        {
            Color = palette.Accent.WithAlpha(110),
            MaskFilter = blur,
            IsAntialias = true
        };
        canvas.DrawRoundRect(today.Bounds, today.Bounds.Width * 0.14f, today.Bounds.Width * 0.14f, glow);

        foreach (var day in days)
        {
            var radius = day.Bounds.Width * 0.14f;
            if (day.DayOfYear == currentDay)
            {
                canvas.DrawRoundRect(day.Bounds, radius, radius, todayPaint);
            }
            else if (day.DayOfYear < currentDay)
            {
                canvas.DrawRoundRect(day.Bounds, radius, radius, pastPaint);
            }
            else
            {
                canvas.DrawRoundRect(day.Bounds, radius, radius, futurePaint);
            }
        }
    }

    private static void DrawProgress(SKCanvas canvas, SKRect bounds, DateOnly date, WallpaperPalette palette, int height)
    {
        var progress = $"{date.DayOfYear} / {WallpaperLogic.DaysInYear(date.Year)}";
        var fontSize = height * 0.022f;
        DrawCenteredText(canvas, progress, bounds.MidX, bounds.MidY,
            palette.MutedText, fontSize, new SKFontStyle(300, 5, SKFontStyleSlant.Upright), bounds.Width);
    }

    private static void DrawCenteredText(SKCanvas canvas, string text, float x, float y, SKColor color, float size, SKFontStyle style, float maximumWidth)
    {
        using var typeface = SKTypeface.FromFamilyName("Segoe UI", style);
        using var font = new SKFont(typeface, size) { Subpixel = true };
        using var paint = new SKPaint { Color = color, IsAntialias = true };
        font.MeasureText(text, out var bounds);
        if (bounds.Width > maximumWidth)
        {
            font.Size *= maximumWidth / bounds.Width;
            font.MeasureText(text, out bounds);
        }

        canvas.DrawText(text, x - bounds.MidX, y - bounds.MidY, SKTextAlign.Left, font, paint);
    }
}