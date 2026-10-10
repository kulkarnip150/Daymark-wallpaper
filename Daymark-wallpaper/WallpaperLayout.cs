using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using SkiaSharp;

namespace DaymarkWallpaper;

public readonly record struct DayCell(int DayOfYear, SKRect Bounds, bool IsMonthStart);

public sealed record WallpaperGeometry(
    SKRect DateCard,
    SKRect GridBounds,
    SKRect ProgressBounds,
    IReadOnlyList<DayCell> Days,
    int Columns);

public readonly record struct WallpaperPalette(
    SKColor Background,
    SKColor BackgroundHighlight,
    SKColor Accent,
    SKColor Text,
    SKColor MutedText);

public static class WallpaperLogic
{
    public static int DaysInYear(int year) => DateTime.IsLeapYear(year) ? 366 : 365;

    public static int DayOfYear(DateOnly date) => date.DayOfYear;

    public static (int Width, int Height) RenderingResolutionForScreen(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        var aspectRatio = width / (double)height;
        var isWidescreen = Math.Abs(aspectRatio - 16d / 9d) <= Math.Abs(aspectRatio - 16d / 10d);
        return (isWidescreen ? 3840 : 3456, 2160);
    }

    public static WallpaperPalette PaletteFor(DateOnly date)
    {
        var seed = SHA256.HashData(Encoding.UTF8.GetBytes(date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));
        var hue = seed[0] * (360f / 256f);
        var background = FromHsv(hue, 0.22f + seed[1] / 255f * 0.12f, 0.12f + seed[2] / 255f * 0.055f);
        var highlight = FromHsv(hue, 0.20f + seed[1] / 255f * 0.10f, 0.15f + seed[2] / 255f * 0.055f);
        var accentHue = (hue + 158f + seed[3] / 255f * 34f) % 360f;
        var accent = FromHsv(accentHue, 0.28f + seed[4] / 255f * 0.14f, 0.80f + seed[5] / 255f * 0.10f);

        return new WallpaperPalette(
            background,
            highlight,
            accent,
            new SKColor(239, 239, 232),
            new SKColor(191, 195, 190));
    }

    public static WallpaperGeometry CreateGeometry(DateOnly date, int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        var screenWidth = (float)width;
        var screenHeight = (float)height;
        var cardHeight = screenHeight * 0.22f;
        var cardWidth = Math.Min(screenWidth * 0.24f, Math.Min(screenHeight, screenWidth) * 0.30f);
        var gridWidth = screenWidth * 0.64f;
        var gridHeight = screenHeight * 0.31f;
        var progressHeight = screenHeight * 0.045f;
        var upperGap = screenHeight * 0.065f;
        var lowerGap = screenHeight * 0.045f;
        var compositionHeight = cardHeight + upperGap + gridHeight + lowerGap + progressHeight;
        var compositionTop = (screenHeight - compositionHeight) / 2f;
        var centerX = screenWidth / 2f;

        var cardTop = compositionTop;
        var dateCard = new SKRect(
            centerX - cardWidth / 2f,
            cardTop,
            centerX + cardWidth / 2f,
            cardTop + cardHeight);
        var gridTop = dateCard.Bottom + upperGap;
        var gridBounds = new SKRect(centerX - gridWidth / 2f, gridTop, centerX + gridWidth / 2f, gridTop + gridHeight);
        var progressTop = gridBounds.Bottom + lowerGap;
        var progressBounds = new SKRect(
            centerX - gridWidth / 2f,
            progressTop,
            centerX + gridWidth / 2f,
            progressTop + progressHeight);

        var totalDays = DaysInYear(date.Year);
        var desiredColumns = Math.Clamp(
            (int)Math.Round(Math.Sqrt(totalDays * (gridWidth / gridHeight))),
            12,
            46);
        var columns = ChooseColumns(totalDays, desiredColumns, gridWidth / gridHeight);
        var rows = (int)Math.Ceiling(totalDays / (double)columns);
        const float gapRatio = 0.42f;
        const float monthGapRatio = 0.30f;

        var maximumRowMonthGaps = 0;
        var monthGapsBetweenRows = 0;
        for (var row = 0; row < rows; row++)
        {
            var rowStart = row * columns;
            var rowEnd = Math.Min(rowStart + columns, totalDays);
            var rowMonthGaps = 0;
            for (var index = rowStart; index < rowEnd; index++)
            {
                var currentDate = date.AddDays(index - date.DayOfYear + 1);
                if (currentDate.Day != 1 || index == 0)
                {
                    continue;
                }

                if (index == rowStart)
                {
                    monthGapsBetweenRows++;
                }
                else
                {
                    rowMonthGaps++;
                }
            }

            maximumRowMonthGaps = Math.Max(maximumRowMonthGaps, rowMonthGaps);
        }

        var cellSize = Math.Max(1f, Math.Min(
            gridWidth / (columns + (columns - 1) * gapRatio + maximumRowMonthGaps * monthGapRatio),
            gridHeight / (rows + (rows - 1) * gapRatio + monthGapsBetweenRows * monthGapRatio)));
        var gap = cellSize * gapRatio;
        var monthGap = cellSize * monthGapRatio;
        var totalGridHeight = rows * cellSize + (rows - 1) * gap + monthGapsBetweenRows * monthGap;
        var currentY = gridBounds.MidY - totalGridHeight / 2f;
        var days = new List<DayCell>(totalDays);

        for (var row = 0; row < rows; row++)
        {
            var rowStart = row * columns;
            var rowEnd = Math.Min(rowStart + columns, totalDays);
            var rowDate = date.AddDays(rowStart - date.DayOfYear + 1);
            var rowHasMonthGap = row > 0 && rowDate.Day == 1;
            if (rowHasMonthGap)
            {
                currentY += monthGap;
            }

            var rowCellCount = rowEnd - rowStart;
            var rowInnerGaps = 0;
            for (var index = rowStart + 1; index < rowEnd; index++)
            {
                if (date.AddDays(index - date.DayOfYear + 1).Day == 1)
                {
                    rowInnerGaps++;
                }
            }

            var rowWidth = rowCellCount * cellSize + (rowCellCount - 1) * gap + rowInnerGaps * monthGap;
            var currentX = centerX - rowWidth / 2f;
            for (var index = rowStart; index < rowEnd; index++)
            {
                var currentDate = date.AddDays(index - date.DayOfYear + 1);
                var isMonthStart = currentDate.Day == 1 && index > 0;
                if (index > rowStart && isMonthStart)
                {
                    currentX += monthGap;
                }

                days.Add(new DayCell(
                    index + 1,
                    new SKRect(currentX, currentY, currentX + cellSize, currentY + cellSize),
                    isMonthStart));
                currentX += cellSize + gap;
            }

            currentY += cellSize + gap;
        }

        return new WallpaperGeometry(dateCard, gridBounds, progressBounds, days, columns);
    }

    private static int ChooseColumns(int totalDays, int desiredColumns, float targetAspect)
    {
        var firstCandidate = Math.Max(12, desiredColumns - 5);
        var lastCandidate = Math.Min(Math.Min(46, totalDays), desiredColumns + 5);
        var bestColumns = Math.Min(desiredColumns, totalDays);
        var bestScore = double.MaxValue;

        for (var candidate = firstCandidate; candidate <= lastCandidate; candidate++)
        {
            var rows = (int)Math.Ceiling(totalDays / (double)candidate);
            var finalRowCount = totalDays - (rows - 1) * candidate;
            var aspect = candidate / (double)rows;
            var aspectError = Math.Abs(Math.Log(aspect / targetAspect));
            var sparseRowPenalty = Math.Max(0d, 0.65d - finalRowCount / (double)candidate) * 1.25d;
            var score = aspectError + sparseRowPenalty;

            if (score < bestScore)
            {
                bestScore = score;
                bestColumns = candidate;
            }
        }

        return bestColumns;
    }

    private static SKColor FromHsv(float hue, float saturation, float value)
    {
        var chroma = value * saturation;
        var secondary = chroma * (1f - Math.Abs(hue / 60f % 2f - 1f));
        var offset = value - chroma;
        var (red, green, blue) = hue switch
        {
            < 60f => (chroma, secondary, 0f),
            < 120f => (secondary, chroma, 0f),
            < 180f => (0f, chroma, secondary),
            < 240f => (0f, secondary, chroma),
            < 300f => (secondary, 0f, chroma),
            _ => (chroma, 0f, secondary)
        };

        return new SKColor(
            (byte)Math.Round((red + offset) * 255f),
            (byte)Math.Round((green + offset) * 255f),
            (byte)Math.Round((blue + offset) * 255f));
    }
}