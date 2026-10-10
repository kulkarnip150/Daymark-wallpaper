using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace DaymarkWallpaper;

internal sealed class RunLock : IDisposable
{
    private readonly Mutex mutex;
    public bool Acquired { get; }

    public RunLock(string dataDirectory)
    {
        var identity = Path.GetFullPath(dataDirectory);
        if (OperatingSystem.IsWindows())
        {
            identity = identity.ToUpperInvariant();
        }

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
        var name = (OperatingSystem.IsWindows() ? @"Global\" : "") + "Daymark-wallpaper-" + hash;
        mutex = new Mutex(false, name);
        try
        {
            Acquired = mutex.WaitOne(0);
        }
        catch (AbandonedMutexException)
        {
            Acquired = true;
        }
        catch
        {
            mutex.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        if (Acquired)
        {
            mutex.ReleaseMutex();
        }

        mutex.Dispose();
    }
}

internal static class RunPerformance
{
    public static string Summary(TimeSpan elapsed, long peakWorkingSetBytes, string operation, int exitCode) =>
        string.Create(CultureInfo.InvariantCulture,
            $"Performance: operation={operation}; elapsedMs={elapsed.TotalMilliseconds:F1}; peakWorkingSetMiB={peakWorkingSetBytes / 1048576d:F1}; exitCode={exitCode}.");

    public static void Log(string logPath, Stopwatch timer, string operation, int exitCode)
    {
        try
        {
            using var process = Process.GetCurrentProcess();
            process.Refresh();
            AppLogger.LogInfo(logPath, Summary(timer.Elapsed, process.PeakWorkingSet64, operation, exitCode));
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException or NotSupportedException)
        {
            AppLogger.LogError(logPath, exception, "Could not measure process performance.");
        }
    }
}
