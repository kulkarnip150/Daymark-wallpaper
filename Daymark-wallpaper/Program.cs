using System.Globalization;
using System.Diagnostics;

namespace DaymarkWallpaper;

internal sealed record AppOptions(bool Preview, bool Regenerate, DateOnly? Date, int? Year)
{
    public bool IsDevelopmentMode => Preview || Date.HasValue || Year.HasValue;

    public static AppOptions Parse(string[] args)
    {
        var preview = false;
        var regenerate = false;
        DateOnly? date = null;
        int? year = null;

        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--preview":
                    preview = true;
                    break;
                case "--regenerate":
                    regenerate = true;
                    break;
                case "--date" when index + 1 < args.Length && DateOnly.TryParseExact(
                    args[index + 1], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate):
                    date = parsedDate;
                    index++;
                    break;
                case "--year" when index + 1 < args.Length && int.TryParse(args[index + 1], out var parsedYear) && parsedYear is >= 1 and <= 9999:
                    year = parsedYear;
                    index++;
                    break;
                default:
                    throw new ArgumentException($"Invalid or incomplete argument: {args[index]}");
            }
        }

        if (date.HasValue && year.HasValue)
        {
            throw new ArgumentException("Use either --date or --year, not both.");
        }

        return new AppOptions(preview, regenerate, date, year);
    }
}

internal static class Program
{
    private static int Main(string[] args)
    {
        var timer = Stopwatch.StartNew();
        try
        {
            var localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(localData))
            {
                throw new InvalidOperationException("The local application data directory could not be resolved.");
            }

            using var runLock = new RunLock(Path.Combine(localData, "Daymark-wallpaper"));
            if (!runLock.Acquired)
            {
                return 0;
            }

            return Run(args, timer);
        }
        catch (Exception exception)
        {
            AppLogger.LogError(AppLogger.FallbackLogPath, exception, "Could not acquire the application run lock.");
            return 1;
        }
    }

    private static int Run(string[] args, Stopwatch timer)
    {
        string? logPath = null;
        var stage = "initializing application storage";
        var operation = "startup";
        var exitCode = 1;
        try
        {
            var localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(localData))
            {
                throw new InvalidOperationException("The local application data directory could not be resolved.");
            }

            var cache = new WallpaperCache(Path.Combine(localData, "Daymark-wallpaper"));
            logPath = cache.LogPath;
            AppLogger.LogInfo(logPath, $"Started. Runtime={Environment.Version}; 64-bit={Environment.Is64BitProcess}.");
            cache.EnsureDirectory();
            stage = "parsing command-line arguments";
            var options = AppOptions.Parse(args);
            stage = "checking operating system support";
            if (!OperatingSystem.IsWindows())
            {
                throw new PlatformNotSupportedException("Daymark-wallpaper requires Windows.");
            }

            var date = options.Date ?? (options.Year.HasValue
                ? new DateOnly(options.Year.Value, 1, 1)
                : DateOnly.FromDateTime(DateTime.Today));
            stage = "detecting the primary display";
            var (screenWidth, screenHeight) = WindowsRuntime.GetPrimaryDisplayResolution();
            var (width, height) = WallpaperLogic.RenderingResolutionForScreen(screenWidth, screenHeight);
            AppLogger.LogInfo(logPath, $"Date={date:yyyy-MM-dd}; display={screenWidth}x{screenHeight}; image={width}x{height}.");

            if (options.IsDevelopmentMode)
            {
                operation = "preview";
                stage = "generating the preview image";
                cache.GeneratePreview(date, width, height);
                if (options.Preview)
                {
                    stage = "opening the preview image";
                    WindowsRuntime.OpenImage(cache.PreviewPath);
                }

                AppLogger.LogInfo(logPath, $"Preview saved to {cache.PreviewPath}. Desktop wallpaper was not changed.");
                exitCode = 0;
                return 0;
            }

            operation = "policy-check";
            stage = "checking organization wallpaper restrictions";
            WindowsRuntime.EnsureWallpaperChangesAllowed();

            stage = "checking the wallpaper cache";
            operation = "cache-check";
            if (options.Regenerate || !cache.IsCurrent(date, width, height))
            {
                stage = "generating the wallpaper image";
                operation = "generated";
                AppLogger.LogInfo(logPath, "Generating wallpaper.");
                cache.GenerateCurrent(date, width, height);
            }
            else
            {
                operation = "cached";
                AppLogger.LogInfo(logPath, "Reusing cached wallpaper.");
            }

            stage = "applying the Windows desktop wallpaper";
            AppLogger.LogInfo(logPath, $"Applying {cache.WallpaperPath} with Fit positioning.");
            WindowsRuntime.SetWallpaper(cache.WallpaperPath);
            stage = "verifying the Windows desktop wallpaper";
            WindowsRuntime.VerifyWallpaper(cache.WallpaperPath);
            AppLogger.LogInfo(logPath, "Verified: Windows reports the generated image as its primary-monitor wallpaper. Exiting.");
            exitCode = 0;
            return 0;
        }
        catch (Exception exception)
        {
            AppLogger.LogError(logPath ?? AppLogger.FallbackLogPath, exception, $"Failed while {stage}.");
            return 1;
        }
        finally
        {
            RunPerformance.Log(logPath ?? AppLogger.FallbackLogPath, timer, operation, exitCode);
        }
    }
}