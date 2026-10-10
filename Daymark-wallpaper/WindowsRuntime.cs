using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;
using SkiaSharp;

namespace DaymarkWallpaper;

internal static class WindowsRuntime
{
    private const int SmCxScreen = 0;
    private const int SmCyScreen = 1;
    private const uint SpiSetDesktopWallpaper = 0x0014;
    private const uint SpiGetDesktopWallpaper = 0x0073;
    private const uint SpifUpdateIniFile = 0x0001;
    private const uint SpifSendChange = 0x0002;
    private const int DpiAwarenessContextPerMonitorAwareV2 = -4;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetProcessDpiAwarenessContext(IntPtr dpiAwarenessContext);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "SystemParametersInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(uint action, uint parameter, string value, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "SystemParametersInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemParametersInfo(uint action, uint parameter, [Out] StringBuilder value, uint flags);

    public static (int Width, int Height) GetPrimaryDisplayResolution()
    {
        _ = SetProcessDpiAwarenessContext(new IntPtr(DpiAwarenessContextPerMonitorAwareV2));
        var width = GetSystemMetrics(SmCxScreen);
        var height = GetSystemMetrics(SmCyScreen);
        if (width <= 0 || height <= 0)
        {
            throw new InvalidOperationException("Windows returned an invalid primary display resolution.");
        }

        return (width, height);
    }

    public static void SetWallpaper(string imagePath)
    {
        imagePath = Path.GetFullPath(imagePath);
        if (!File.Exists(imagePath))
        {
            throw new FileNotFoundException("The generated wallpaper image does not exist.", imagePath);
        }

        EnsureWallpaperChangesAllowed();
        using var desktop = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop", writable: true)
            ?? throw new InvalidOperationException("Windows desktop wallpaper settings could not be opened.");
        desktop.SetValue("WallpaperStyle", "6", RegistryValueKind.String);
        desktop.SetValue("TileWallpaper", "0", RegistryValueKind.String);

        if (!SystemParametersInfo(SpiSetDesktopWallpaper, 0, imagePath, SpifUpdateIniFile | SpifSendChange))
        {
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Windows could not set the desktop wallpaper.");
        }
    }

    public static void VerifyWallpaper(string expectedPath)
    {
        var path = new StringBuilder(32768);
        if (!GetSystemParametersInfo(SpiGetDesktopWallpaper, (uint)path.Capacity, path, 0))
        {
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(),
                "Windows accepted the wallpaper request, but its current wallpaper could not be verified.");
        }

        VerifyWallpaperPath(expectedPath, path.ToString());
    }

    internal static void VerifyWallpaperPath(string expectedPath, string actualPath)
    {
        if (string.IsNullOrWhiteSpace(actualPath) ||
            !string.Equals(Path.GetFullPath(expectedPath), Path.GetFullPath(actualPath), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Wallpaper verification failed. Expected '{expectedPath}', but Windows reports '{actualPath}'. " +
                "An organization policy or another wallpaper application may be overriding the change.");
        }
    }

    public static void EnsureWallpaperChangesAllowed()
    {
        foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            using var restrictions = hive.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Policies\ActiveDesktop");
            using var enforcedWallpaper = hive.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Policies\System");
            VerifyWallpaperPolicy(restrictions?.GetValue("NoChangingWallPaper"),
                enforcedWallpaper?.GetValue("Wallpaper"), hive.Name);
        }
    }

    internal static void VerifyWallpaperPolicy(object? restriction, object? enforcedWallpaper, string source)
    {
        if (restriction is not null and not int || enforcedWallpaper is not null and not string)
        {
            throw new InvalidOperationException($"Wallpaper policy values in {source} have unexpected types; permission could not be verified.");
        }

        if (restriction is int enabled && enabled != 0 ||
            enforcedWallpaper is string wallpaper && !string.IsNullOrWhiteSpace(wallpaper))
        {
            throw new InvalidOperationException(
                $"Wallpaper changes are restricted by an organization policy in {source}. " +
                "No wallpaper or positioning settings were changed. Contact your IT administrator, or use --preview.");
        }
    }

    public static void OpenImage(string imagePath)
    {
        using var viewer = Process.Start(new ProcessStartInfo(imagePath) { UseShellExecute = true });
    }
}

internal sealed record WallpaperMetadata(string Date, int Year, int Width, int Height, string Layout, int Version);

internal sealed class WallpaperCache(string directory)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
    private const int MetadataVersion = 2;
    private const string LayoutName = "Grid";

    public string WallpaperPath => Path.Combine(directory, "current-wallpaper.png");
    public string PreviewPath => Path.Combine(directory, "preview-wallpaper.png");
    public string LogPath => Path.Combine(directory, "app.log");

    public void EnsureDirectory() => Directory.CreateDirectory(directory);

    public bool IsCurrent(DateOnly date, int width, int height)
    {
        try
        {
            var metadataPath = Path.Combine(directory, "metadata.json");
            if (!File.Exists(WallpaperPath) || !File.Exists(metadataPath))
            {
                return false;
            }

            var metadata = JsonSerializer.Deserialize<WallpaperMetadata>(File.ReadAllText(metadataPath), JsonOptions);
            if (metadata != ExpectedMetadata(date, width, height))
            {
                return false;
            }

            using var bitmap = SKBitmap.Decode(WallpaperPath);
            if (bitmap is null)
            {
                AppLogger.LogInfo(LogPath, "Cached wallpaper is not a valid PNG. Regenerating.");
                return false;
            }

            return bitmap.Width == width && bitmap.Height == height;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            AppLogger.LogError(LogPath, exception, "Could not read the wallpaper cache. Regenerating.");
            return false;
        }
    }

    public void GenerateCurrent(DateOnly date, int width, int height)
    {
        var temporaryImage = WallpaperPath + ".tmp";
        var temporaryMetadata = Path.Combine(directory, "metadata.json.tmp");
        try
        {
            WallpaperRenderer.Render(date, width, height, temporaryImage);
            File.Move(temporaryImage, WallpaperPath, true);

            File.WriteAllText(temporaryMetadata, JsonSerializer.Serialize(ExpectedMetadata(date, width, height), JsonOptions));
            File.Move(temporaryMetadata, Path.Combine(directory, "metadata.json"), true);
        }
        finally
        {
            if (File.Exists(temporaryImage))
            {
                File.Delete(temporaryImage);
            }

            if (File.Exists(temporaryMetadata))
            {
                File.Delete(temporaryMetadata);
            }
        }
    }

    public void GeneratePreview(DateOnly date, int width, int height) => WallpaperRenderer.Render(date, width, height, PreviewPath);

    private static WallpaperMetadata ExpectedMetadata(DateOnly date, int width, int height) =>
        new(date.ToString("yyyy-MM-dd"), date.Year, width, height, LayoutName, MetadataVersion);
}

internal static class AppLogger
{
    internal const int MaximumLogBytes = 500_000;

    public static string FallbackLogPath => Path.Combine(Path.GetTempPath(), "Daymark-wallpaper", "app.log");

    public static void LogInfo(string path, string message) => Write(path, $"INFO {message}", FallbackLogPath);

    public static void LogError(string path, Exception exception, string? context = null) =>
        Write(path, $"ERROR {context} {exception}", FallbackLogPath);

    internal static void Write(string path, string message, string fallbackPath)
    {
        try
        {
            Append(path, message);
        }
        catch (Exception primaryException) when (primaryException is IOException or UnauthorizedAccessException)
        {
            try
            {
                Append(fallbackPath, $"Could not write log to {path}: {primaryException.Message}{Environment.NewLine}{message}");
            }
            catch (Exception fallbackException) when (fallbackException is IOException or UnauthorizedAccessException)
            {
                Trace.TraceError($"Daymark-wallpaper could not write either log: {primaryException.Message}; {fallbackException.Message}. {message}");
            }
        }
    }

    private static void Append(string path, string message)
    {
        var line = $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}";
        var byteCount = Encoding.UTF8.GetByteCount(line);
        var buffer = new byte[Math.Min(byteCount, MaximumLogBytes)];
        if (byteCount > MaximumLogBytes)
        {
            ReadOnlySpan<byte> marker = "\n[Log entry truncated.]\n"u8;
            Encoding.UTF8.GetEncoder().Convert(line.AsSpan(), buffer.AsSpan(0, MaximumLogBytes - marker.Length),
                flush: true, out _, out var bytesWritten, out _);
            marker.CopyTo(buffer.AsSpan(bytesWritten));
            byteCount = bytesWritten + marker.Length;
        }
        else
        {
            Encoding.UTF8.GetBytes(line.AsSpan(), buffer);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var output = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read);
        if (output.Length > MaximumLogBytes - byteCount)
        {
            output.SetLength(0);
        }

        output.Position = output.Length;
        output.Write(buffer.AsSpan(0, byteCount));
    }
}