using DaymarkWallpaper;
using System.Text;
using Xunit;

namespace DaymarkWallpaper.Tests;

public sealed class RuntimeDiagnosticsTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), $"daymark-diagnostics-{Guid.NewGuid():N}");

    [Fact]
    public void WallpaperVerificationAcceptsMatchingPathsIgnoringCase()
    {
        var expected = Path.Combine(directory, "wallpaper.png");
        WindowsRuntime.VerifyWallpaperPath(expected, expected.ToUpperInvariant());
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("corporate-wallpaper.png")]
    public void WallpaperVerificationRejectsMissingOrDifferentWallpaper(string reportedPath)
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            WindowsRuntime.VerifyWallpaperPath(Path.Combine(directory, "wallpaper.png"), reportedPath));
        Assert.Contains("Wallpaper verification failed", exception.Message);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData(0, null)]
    [InlineData(0, "")]
    public void UnrestrictedWallpaperPolicyAllowsChanges(object? restriction, object? wallpaper) =>
        WindowsRuntime.VerifyWallpaperPolicy(restriction, wallpaper, "Test policy");

    [Theory]
    [InlineData(1, null)]
    [InlineData(null, "corporate-wallpaper.png")]
    [InlineData(0, "corporate-wallpaper.png")]
    public void EnforcedWallpaperPolicyStopsChanges(object? restriction, object? wallpaper)
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            WindowsRuntime.VerifyWallpaperPolicy(restriction, wallpaper, "Test policy"));
        Assert.Contains("restricted by an organization policy", exception.Message);
    }

    [Theory]
    [InlineData("1", null)]
    [InlineData(null, 1)]
    public void InvalidPolicyTypesCannotProduceSuccess(object? restriction, object? wallpaper) =>
        Assert.Throws<InvalidOperationException>(() =>
            WindowsRuntime.VerifyWallpaperPolicy(restriction, wallpaper, "Test policy"));

    [Fact]
    public void LoggerCreatesDirectoryAndLogOnStartup()
    {
        var path = Path.Combine(directory, "nested", "app.log");

        AppLogger.LogInfo(path, "Started.");

        Assert.Contains("INFO Started.", File.ReadAllText(path));
    }

    [Fact]
    public void PrimaryAndFallbackLogsTogetherStayWithinOneMegabyte()
    {
        var primaryPath = Path.Combine(directory, "primary", "app.log");
        var fallbackPath = Path.Combine(directory, "fallback", "app.log");
        var message = new string('x', 1_000_001);

        AppLogger.LogInfo(primaryPath, message);
        AppLogger.LogInfo(fallbackPath, message);

        Assert.InRange(new FileInfo(primaryPath).Length + new FileInfo(fallbackPath).Length, 1, 1_000_000);
    }

    [Fact]
    public void RepeatedLogWritesNeverExceedOneMegabyte()
    {
        var path = Path.Combine(directory, "app.log");
        var message = new string('x', 4096);
        for (var index = 0; index < 300; index++)
        {
            AppLogger.LogInfo(path, $"{index}: {message}");
            Assert.InRange(new FileInfo(path).Length, 1, AppLogger.MaximumLogBytes);
        }

        Assert.Contains("299:", File.ReadAllText(path));
        Assert.Single(Directory.EnumerateFiles(directory));
    }

    [Fact]
    public void ExistingOversizedLogIsResetOnNextWrite()
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "app.log");
        File.WriteAllBytes(path, new byte[AppLogger.MaximumLogBytes + 1]);

        AppLogger.LogInfo(path, "Latest entry.");

        Assert.InRange(new FileInfo(path).Length, 1, AppLogger.MaximumLogBytes);
        Assert.Contains("Latest entry.", File.ReadAllText(path));
        Assert.DoesNotContain('\0', File.ReadAllText(path));
    }

    [Fact]
    public void OversizedUnicodeEntryIsTruncatedWithinByteLimitWithoutBreakingUtf8()
    {
        var path = Path.Combine(directory, "app.log");
        var message = string.Concat(Enumerable.Repeat("\U0001F600", AppLogger.MaximumLogBytes / 2));

        AppLogger.LogInfo(path, message);

        var bytes = File.ReadAllBytes(path);
        Assert.InRange(bytes.Length, 1, AppLogger.MaximumLogBytes);
        var log = new UTF8Encoding(false, true).GetString(bytes);
        Assert.Contains("[Log entry truncated.]", log);
    }

    [Fact]
    public void LogKeepsEarlierEntriesWhileTheyFit()
    {
        var path = Path.Combine(directory, "app.log");
        AppLogger.LogInfo(path, "First entry.");
        AppLogger.LogInfo(path, "Second entry.");

        var log = File.ReadAllText(path);
        Assert.Contains("First entry.", log);
        Assert.Contains("Second entry.", log);
    }

    [Fact]
    public void LoggerUsesFallbackWhenPrimaryDirectoryCannotBeCreated()
    {
        Directory.CreateDirectory(directory);
        var blocker = Path.Combine(directory, "blocked");
        File.WriteAllText(blocker, "A file cannot be used as a directory.");
        var primaryPath = Path.Combine(blocker, "app.log");
        var fallbackPath = Path.Combine(directory, "fallback", "app.log");
        Directory.CreateDirectory(Path.GetDirectoryName(fallbackPath)!);
        File.WriteAllBytes(fallbackPath, new byte[AppLogger.MaximumLogBytes + 1]);

        AppLogger.Write(primaryPath, "INFO Started.", fallbackPath);

        var log = File.ReadAllText(fallbackPath);
        Assert.Contains("Could not write log", log);
        Assert.Contains(primaryPath, log);
        Assert.Contains("INFO Started.", log);
        Assert.InRange(new FileInfo(fallbackPath).Length, 1, AppLogger.MaximumLogBytes);
    }

    [Fact]
    public void ErrorLogIncludesStageAndInnerException()
    {
        var path = Path.Combine(directory, "app.log");
        var exception = new InvalidOperationException("Rendering failed.", new IOException("Native asset missing."));

        AppLogger.LogError(path, exception, "Failed while rendering.");

        var log = File.ReadAllText(path);
        Assert.Contains("Failed while rendering.", log);
        Assert.Contains("Rendering failed.", log);
        Assert.Contains("Native asset missing.", log);
    }

    [Fact]
    public void CorruptMetadataIsLoggedAndInvalidatesCache()
    {
        var cache = new WallpaperCache(directory);
        cache.EnsureDirectory();
        File.WriteAllText(cache.WallpaperPath, "not a PNG");
        File.WriteAllText(Path.Combine(directory, "metadata.json"), "{invalid");

        Assert.False(cache.IsCurrent(new DateOnly(2026, 10, 10), 1920, 1080));
        Assert.Contains("Could not read the wallpaper cache.", File.ReadAllText(cache.LogPath));
    }

    [Fact]
    public void GeneratedCacheIsReusedOnlyForMatchingDateAndDimensions()
    {
        var cache = new WallpaperCache(directory);
        cache.EnsureDirectory();
        var date = new DateOnly(2026, 10, 10);
        cache.GenerateCurrent(date, 1920, 1080);

        Assert.True(cache.IsCurrent(date, 1920, 1080));
        Assert.False(cache.IsCurrent(date.AddDays(1), 1920, 1080));
        Assert.False(cache.IsCurrent(date, 3456, 2160));
        File.WriteAllText(cache.WallpaperPath, "not a PNG");
        Assert.False(cache.IsCurrent(date, 1920, 1080));
        Assert.Contains("not a valid PNG", File.ReadAllText(cache.LogPath));
    }

    [Theory]
    [InlineData("--preview")]
    [InlineData("--date", "2026-12-31")]
    [InlineData("--year", "2028")]
    public void DevelopmentModesDoNotSelectWallpaperSetting(params string[] args) =>
        Assert.True(AppOptions.Parse(args).IsDevelopmentMode);

    [Fact]
    public void RegenerationUsesNormalWallpaperMode()
    {
        var options = AppOptions.Parse(["--regenerate"]);
        Assert.True(options.Regenerate);
        Assert.False(options.IsDevelopmentMode);
    }

    [Fact]
    public void DateAndYearCannotBeCombined() =>
        Assert.Throws<ArgumentException>(() => AppOptions.Parse(["--date", "2026-12-31", "--year", "2028"]));

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, true);
        }
    }
}
