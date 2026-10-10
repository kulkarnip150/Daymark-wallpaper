using System.Globalization;
using System.Diagnostics;
using DaymarkWallpaper;
using Xunit;

namespace DaymarkWallpaper.Tests;

public sealed class RunDiagnosticsTests
{
    [Fact]
    public void ConcurrentRunIsRejectedAndLockIsReleasedAfterCompletion()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"daymark-lock-{Guid.NewGuid():N}");
        using (var first = new RunLock(directory))
        {
            Assert.True(first.Acquired);
            var acquired = true;
            Exception? failure = null;
            var contender = new Thread(() =>
            {
                try
                {
                    using var second = new RunLock(directory);
                    acquired = second.Acquired;
                }
                catch (Exception exception)
                {
                    failure = exception;
                }
            }) { IsBackground = true };
            contender.Start();
            Assert.True(contender.Join(TimeSpan.FromSeconds(5)));
            Assert.Null(failure);
            Assert.False(acquired);
        }

        using var next = new RunLock(directory);
        Assert.True(next.Acquired);
    }

    [Fact]
    public void DifferentDataDirectoriesCanRunIndependently()
    {
        using var first = new RunLock(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));
        using var second = new RunLock(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));
        Assert.True(first.Acquired);
        Assert.True(second.Acquired);
    }

    [Fact]
    public void LockIsReleasedWhenOperationThrows()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"daymark-lock-{Guid.NewGuid():N}");
        Assert.Throws<IOException>((Action)(() =>
        {
            using var runLock = new RunLock(directory);
            throw new IOException("Simulated operation failure.");
        }));

        using var next = new RunLock(directory);
        Assert.True(next.Acquired);
    }

    [Fact]
    public void PerformanceLogReadsActualProcessPeakMemory()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"daymark-performance-{Guid.NewGuid():N}");
        var logPath = Path.Combine(directory, "app.log");
        try
        {
            var timer = Stopwatch.StartNew();
            RunPerformance.Log(logPath, timer, "preview", 0);
            var log = File.ReadAllText(logPath);
            Assert.Contains("Performance: operation=preview;", log);
            Assert.Contains("exitCode=0.", log);
            var peakText = log.Split("peakWorkingSetMiB=")[1].Split(';')[0];
            Assert.True(double.Parse(peakText, CultureInfo.InvariantCulture) > 0);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }

    [Fact]
    public void PerformanceSummaryHasMeasuredValuesAndInvariantUnits()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            var summary = RunPerformance.Summary(TimeSpan.FromMilliseconds(125.5), 32 * 1048576L, "cached", 0);
            Assert.Equal("Performance: operation=cached; elapsedMs=125.5; peakWorkingSetMiB=32.0; exitCode=0.", summary);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }
}
