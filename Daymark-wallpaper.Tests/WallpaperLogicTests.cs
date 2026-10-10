using DaymarkWallpaper;
using SkiaSharp;
using Xunit;

namespace DaymarkWallpaper.Tests;

public sealed class WallpaperLogicTests
{
    [Theory]
    [InlineData(1280, 720, 3840, 2160)]
    [InlineData(1366, 768, 3840, 2160)]
    [InlineData(1920, 1080, 3840, 2160)]
    [InlineData(2560, 1440, 3840, 2160)]
    [InlineData(3840, 2160, 3840, 2160)]
    [InlineData(1280, 800, 3456, 2160)]
    [InlineData(1920, 1200, 3456, 2160)]
    [InlineData(2560, 1600, 3456, 2160)]
    [InlineData(3456, 2160, 3456, 2160)]
    public void RenderingResolutionUsesFixedAspectMatchedImagesWithin4K(
        int screenWidth, int screenHeight, int expectedWidth, int expectedHeight)
    {
        var resolution = WallpaperLogic.RenderingResolutionForScreen(screenWidth, screenHeight);

        Assert.Equal((expectedWidth, expectedHeight), resolution);
        Assert.True(resolution.Width <= 3840);
        Assert.True(resolution.Height <= 2160);
        Assert.InRange(Math.Abs(resolution.Width / (double)resolution.Height - screenWidth / (double)screenHeight),
            0, 0.005);
    }

    [Theory]
    [InlineData(0, 1080)]
    [InlineData(1920, 0)]
    [InlineData(-1, 1080)]
    [InlineData(1920, -1)]
    public void RenderingResolutionRejectsInvalidScreenDimensions(int width, int height) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => WallpaperLogic.RenderingResolutionForScreen(width, height));

    [Theory]
    [InlineData(2026, 365)]
    [InlineData(2028, 366)]
    public void DaysInYearUsesCalendarYear(int year, int expected) =>
        Assert.Equal(expected, WallpaperLogic.DaysInYear(year));

    [Theory]
    [InlineData("2026-01-01", 1, 365)]
    [InlineData("2026-06-30", 181, 365)]
    [InlineData("2026-12-31", 365, 365)]
    [InlineData("2028-02-29", 60, 366)]
    [InlineData("2028-12-31", 366, 366)]
    public void DayNumberAndYearLengthAreCorrect(string input, int expectedDay, int expectedDays)
    {
        var date = DateOnly.Parse(input);
        Assert.Equal(expectedDay, WallpaperLogic.DayOfYear(date));
        Assert.Equal(expectedDays, WallpaperLogic.DaysInYear(date.Year));
    }

    [Theory]
    [InlineData("2026-01-31", 31)]
    [InlineData("2026-02-01", 32)]
    [InlineData("2026-02-28", 59)]
    [InlineData("2028-02-29", 60)]
    [InlineData("2026-03-01", 60)]
    [InlineData("2026-04-01", 91)]
    [InlineData("2026-06-01", 152)]
    [InlineData("2026-12-01", 335)]
    [InlineData("2026-12-31", 365)]
    public void MonthBoundaryDatesHaveCorrectDayNumbers(string input, int expectedDay) =>
        Assert.Equal(expectedDay, WallpaperLogic.DayOfYear(DateOnly.Parse(input)));

    [Theory]
    [InlineData(2026, new[] { 32, 60, 91, 121, 152, 182, 213, 244, 274, 305, 335 })]
    [InlineData(2028, new[] { 32, 61, 92, 122, 153, 183, 214, 245, 275, 306, 336 })]
    public void MonthStartsAreMarkedAtActualCalendarBoundaries(int year, int[] expectedDayNumbers)
    {
        var layout = WallpaperLogic.CreateGeometry(new DateOnly(year, 1, 1), 1920, 1080);
        var actual = layout.Days.Where(day => day.IsMonthStart).Select(day => day.DayOfYear).ToArray();
        Assert.Equal(expectedDayNumbers, actual);
    }

    [Fact]
    public void PaletteIsRepeatableForADateAndChangesForAnotherDate()
    {
        var first = WallpaperLogic.PaletteFor(new DateOnly(2026, 10, 1));
        var repeated = WallpaperLogic.PaletteFor(new DateOnly(2026, 10, 1));
        var nextDay = WallpaperLogic.PaletteFor(new DateOnly(2026, 10, 2));

        Assert.Equal(first, repeated);
        Assert.NotEqual(first.Background, nextDay.Background);
        Assert.NotEqual(first.Accent, nextDay.Accent);
        Assert.True(first.Background.Red + first.Background.Green + first.Background.Blue < 210);
    }

    [Theory]
    [InlineData(2026, 3840, 2160)]
    [InlineData(2028, 3840, 2160)]
    [InlineData(2026, 3456, 2160)]
    [InlineData(2028, 3456, 2160)]
    public void GridSpacingSeparatesMonthsWithoutOverlappingCells(int year, int width, int height)
    {
        var layout = WallpaperLogic.CreateGeometry(new DateOnly(year, 1, 1), width, height);
        var cellSize = layout.Days[0].Bounds.Width;
        for (var index = 1; index < layout.Days.Count; index++)
        {
            var previous = layout.Days[index - 1];
            var current = layout.Days[index];
            Assert.InRange(Math.Abs(current.Bounds.Width - cellSize), 0, 0.001f);
            var gap = current.Bounds.Top == previous.Bounds.Top
                ? current.Bounds.Left - previous.Bounds.Right
                : current.Bounds.Top - previous.Bounds.Bottom;
            var expectedGap = current.IsMonthStart ? 0.72f : 0.42f;
            Assert.InRange(gap / cellSize, expectedGap - 0.001f, expectedGap + 0.001f);
        }
    }

    [Theory]
    [InlineData(1920, 1080)]
    [InlineData(2560, 1440)]
    [InlineData(3840, 2160)]
    [InlineData(3456, 2160)]
    [InlineData(3440, 1440)]
    [InlineData(5120, 1440)]
    public void LayoutFitsRequestedResolution(int width, int height)
    {
        var date = new DateOnly(2028, 12, 31);
        var layout = WallpaperLogic.CreateGeometry(date, width, height);
        var screen = new SKRect(0, 0, width, height);

        Assert.Equal(366, layout.Days.Count);
        Assert.All(layout.Days, day => Assert.True(screen.Contains(day.Bounds)));
        Assert.True(screen.Contains(layout.DateCard));
        Assert.True(screen.Contains(layout.GridBounds));
        Assert.True(screen.Contains(layout.ProgressBounds));
        var finalRowCount = layout.Days.Count % layout.Columns;
        if (finalRowCount == 0)
        {
            finalRowCount = layout.Columns;
        }

        Assert.True(finalRowCount >= layout.Columns * 0.65f);
    }

    [Theory]
    [InlineData(1920, 1080)]
    [InlineData(3840, 2160)]
    [InlineData(3456, 2160)]
    public void RenderingIsByteDeterministicForTheSameDateAndResolution(int width, int height)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"daymark-wallpaper-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var firstPath = Path.Combine(directory, "first.png");
        var secondPath = Path.Combine(directory, "second.png");
        var otherDatePath = Path.Combine(directory, "other.png");

        try
        {
            WallpaperRenderer.Render(new DateOnly(2026, 10, 1), width, height, firstPath);
            WallpaperRenderer.Render(new DateOnly(2026, 10, 1), width, height, secondPath);
            WallpaperRenderer.Render(new DateOnly(2026, 10, 2), width, height, otherDatePath);

            Assert.Equal(File.ReadAllBytes(firstPath), File.ReadAllBytes(secondPath));
            Assert.NotEqual(File.ReadAllBytes(firstPath), File.ReadAllBytes(otherDatePath));
            using var bitmap = SKBitmap.Decode(firstPath);
            Assert.NotNull(bitmap);
            Assert.Equal(width, bitmap.Width);
            Assert.Equal(height, bitmap.Height);
            var layout = WallpaperLogic.CreateGeometry(new DateOnly(2026, 10, 1), width, height);
            var today = layout.Days[273].Bounds;
            var past = layout.Days[0].Bounds;
            var future = layout.Days[^1].Bounds;
            var todayColor = bitmap.GetPixel((int)today.MidX, (int)today.MidY);
            var pastColor = bitmap.GetPixel((int)past.MidX, (int)past.MidY);
            var futureColor = bitmap.GetPixel((int)future.MidX, (int)future.MidY);
            Assert.Equal(WallpaperLogic.PaletteFor(new DateOnly(2026, 10, 1)).Accent, todayColor);
            Assert.True(todayColor.Red + todayColor.Green + todayColor.Blue >
                pastColor.Red + pastColor.Green + pastColor.Blue);
            Assert.True(pastColor.Red + pastColor.Green + pastColor.Blue >
                futureColor.Red + futureColor.Green + futureColor.Blue);
            var glowX = (int)(today.Left - today.Width * 0.18f);
            var glowY = (int)today.MidY;
            var glowColor = bitmap.GetPixel(glowX, glowY);
            var backgroundLeft = bitmap.GetPixel(0, glowY);
            var backgroundRight = bitmap.GetPixel(width - 1, glowY);
            var position = glowX / (double)(width - 1);
            var backgroundBrightness =
                (backgroundLeft.Red + backgroundLeft.Green + backgroundLeft.Blue) * (1 - position) +
                (backgroundRight.Red + backgroundRight.Green + backgroundRight.Blue) * position;
            Assert.True(glowColor.Red + glowColor.Green + glowColor.Blue > backgroundBrightness + 6,
                "Today's soft glow must extend beyond the filled block.");
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }
}