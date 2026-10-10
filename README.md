# Daymark-wallpaper

A small, silent Windows utility that creates a minimalist year-progress wallpaper,
sets it through the native Windows API, and exits.

## Features

- Calendar-page date card with a two-digit day, full English month name, and four-digit year.
- One block per day of the year, with automatic 365/366-day and leap-year handling.
- Muted filled blocks for past days, a brighter highlighted block for today, and empty outlines for future days.
- Subtle gaps at actual month boundaries and progress displayed as `274 / 365`.
- Refined calendar spacing, visually centered typography, a quieter progress indicator,
  and a smoothly blurred glow around today's block.
- Deterministic daily dark colors: the same date, render size, and application version produce the same design on the same system.
- Local PNG and JSON caching, with no database, telemetry, or runtime network access.
- No console, tray icon, settings window, or persistent background process.

The wallpaper updates **when the executable runs**, not automatically at midnight.
Run it manually or add it to Windows Startup. A new year is handled automatically
on the next launch.

## Requirements

- A Windows x64 computer. The application uses Windows display and wallpaper APIs.
- A primary display with a 16:9 or 16:10 aspect ratio, up to the chosen 4K limit.
- The .NET 10 SDK to build or run tests. A self-contained published executable does
  not require a separately installed .NET runtime.
- An image viewer associated with PNG files when using `--preview`.

Package restore requires network access unless dependencies are already cached;
normal application execution is offline.

## Build, test, and publish

Run these commands in PowerShell from the project root:

```powershell
dotnet build .\Daymark-wallpaper\Daymark-wallpaper.csproj -c Release -p:RestoreSources=https://api.nuget.org/v3/index.json
dotnet test .\Daymark-wallpaper.Tests\Daymark-wallpaper.Tests.csproj -p:RestoreSources=https://api.nuget.org/v3/index.json
dotnet publish .\Daymark-wallpaper\Daymark-wallpaper.csproj -c Release -r win-x64 --self-contained true -p:RestoreSources=https://api.nuget.org/v3/index.json
```

The published executable is:

```text
Daymark-wallpaper\bin\Release\net10.0-windows\win-x64\publish\Daymark-wallpaper.exe
```

Copy it to a permanent location, for example
`%LOCALAPPDATA%\Programs\Daymark-wallpaper\`. The executable bundles .NET and the
Windows SkiaSharp native runtime; native libraries are extracted when needed.

## GitHub Actions Windows build

The [Windows build workflow](./.github/workflows/windows-build.yml) runs only when
manually dispatched using a GitHub-hosted `windows-latest` runner. Pushes and pull
requests do not trigger builds.
It installs .NET 10, runs the Release test suite, and publishes a self-contained,
single-file `win-x64` executable. Publishing only proceeds if the tests pass.
The workflow does not execute the wallpaper utility or change the runner's desktop.

To use it:

1. Create an empty GitHub repository and push this project's source to it.
2. Open the repository's **Actions** tab and select **Windows build**.
3. Select **Run workflow**, choose a branch, and start the build.
4. Open a successful run and download **Daymark-wallpaper-win-x64** from **Artifacts**.
5. Extract the downloaded ZIP and run `Daymark-wallpaper.exe` on your Windows computer.

The workflow file must be present on the default branch for manual dispatch to be available.
TRX test results are uploaded separately as **Windows-test-results**, including
when tests fail. Artifacts are retained for 14 days; save an executable locally if
you need it longer. No automatic GitHub Release or installer is created.

For a newly initialized local repository, the initial upload can be:

```powershell
git add .
git commit -m "Initial project setup"
git remote add origin https://github.com/YOUR-USERNAME/Daymark-wallpaper.git
git push -u origin main
```

Replace the remote URL with your repository's URL. The [.gitignore](./.gitignore)
excludes build output, test results, local IDE settings, and logs.

CI downloads actions and NuGet dependencies and uploads build artifacts/test
reports to GitHub. This is build-time activity only; it adds no runtime network
calls or telemetry to the wallpaper utility. .NET CLI telemetry is disabled in CI.

## How to use

Double-click the published executable to set today's wallpaper silently. It checks
the cache, generates an image if needed, applies it, and exits.

From PowerShell in the directory containing the executable:

```powershell
# Set today's wallpaper, reusing a valid cache.
.\Daymark-wallpaper.exe

# Force regeneration and set today's wallpaper.
.\Daymark-wallpaper.exe --regenerate

# Render today and open the image without changing the desktop.
.\Daymark-wallpaper.exe --preview

# Render a specified date without changing the desktop or system date.
.\Daymark-wallpaper.exe --date 2026-12-31

# Render January 1 of a specified year without changing the desktop.
.\Daymark-wallpaper.exe --year 2028

# Open the specified-date or specified-year image too.
.\Daymark-wallpaper.exe --preview --date 2028-02-29
.\Daymark-wallpaper.exe --preview --year 2028
```

`--date` requires `yyyy-MM-dd`; `--year` accepts years 1 through 9999.
Do not combine `--date` and `--year`. Preview, date, and year modes never change
the desktop wallpaper or its positioning settings. Date and year modes save the
preview image but only open it when `--preview` is also supplied.

### Run at sign-in

The application does not register itself with Windows Startup.

1. Keep the executable in a permanent location.
2. Press `Win + R`, enter `shell:startup`, and press Enter.
3. Create a shortcut to the executable in that folder, with no command-line arguments.

Windows runs the shortcut when you sign in. The utility sets the wallpaper and
exits; it does not continue running. To stop automatic updates, remove the shortcut.
To remove the application, also delete its executable and, optionally, its local
data folder after choosing a different wallpaper.

## Display support

The application targets primary displays with 16:9 or 16:10 aspect ratios, up to 4K.
It detects the display's aspect ratio, then always renders a fixed-size image:

- 16:9: 3840 x 2160.
- 16:10: 3456 x 2160, keeping both dimensions within the 3840 x 2160 limit.

Normal execution explicitly sets Windows wallpaper positioning to **Fit**, with tiling
disabled. Windows scales the image while preserving its proportions and all content.
This changes the current user's wallpaper positioning preference. Display DPI scaling
does not change the chosen image dimensions. Preview and test-date modes use the same
image sizes but do not change Windows wallpaper settings.

Changing resolution within the same aspect ratio reuses the cached image; switching
between 16:9 and 16:10 regenerates it. Other aspect ratios are outside the target scope:
the nearest supported ratio is selected, and Fit may leave unused bands.

On exact matching ratios, Fit downscales the image to smaller displays without
cropping or distortion. Approximately matching ratios such as 1366 x 768 can have
tiny unused bands. A 3840 x 2400 display is 16:10 but exceeds the selected image's
2160-pixel height; it still fits, with slight upscaling.

The layout is designed for the primary monitor. Independent per-monitor wallpapers
are not implemented, and Windows wallpaper positioning is a user-wide preference.

## Local files and caching

Application data is stored in:

```text
%LOCALAPPDATA%\Daymark-wallpaper\
```

| File | Purpose |
|---|---|
| `current-wallpaper.png` | The image used for the desktop wallpaper. |
| `metadata.json` | Cached date, year, render dimensions, layout, and version. |
| `preview-wallpaper.png` | Most recent preview, date, or year render. |
| `app.log` | Startup, selected mode/render size, wallpaper application, and failure diagnostics. |

The current wallpaper is replaced rather than creating a daily image archive.
Cache reuse checks the metadata and decodes the PNG to verify its dimensions.
Missing, outdated, or invalid cache data triggers regeneration.

### Resource usage and safety

- A named mutex protects all modes that share the same local data directory.
  A second simultaneous launch exits immediately with code `0` as a no-op,
  without touching cache files, wallpaper settings, or shared logs. The lock is
  held through final logging and released on exit; Windows also releases ownership
  after a crash. Different user data directories have independent locks.

### Performance measurements

Each accepted run writes a local summary before releasing its lock:

```text
Performance: operation=generated; elapsedMs=420.0; peakWorkingSetMiB=85.0; exitCode=0.
```

The values above are illustrative, not benchmark results. `operation` distinguishes
`generated`, `cached`, and `preview` paths, or the earlier stage reached on failure.
Elapsed time starts at managed entry and includes rendering/cache checks, Windows
calls, preview launching, and earlier logging. It excludes native host startup,
single-file extraction before managed entry, and the final summary write.
Peak working set is the operating system's process-lifetime high-water resident
memory value, not only managed heap usage or a leak measurement. The external
preview viewer is not included. There is one process query per run, no sampling
timer, no forced garbage collection, and no network reporting.

On an unmanaged Windows computer, compare a `--regenerate` run with a subsequent
no-argument run on the same date. Use the existing `Start-Process -Wait -PassThru`
instructions below, then read the two `Performance` lines in `app.log`.
For preview-only measurements on a restricted computer, use `--date 2026-10-10`
to render without launching an image viewer or changing the wallpaper.

### Cleanup and memory

- Organization policy keys are opened read-only and never modified. Only normal
  current-user wallpaper positioning preferences are written when permitted.
- Normal mode checks known restrictions before decoding or rendering an image,
  avoiding that work on a restricted computer. Preview modes remain available.
- The utility performs one run and exits with code `0` or `1`. There are no timers,
  file watchers, worker loops, services, or resident Daymark processes.
- SkiaSharp surfaces, images, fonts, paints, blur filters, decoded bitmaps, file
  streams, and opened registry keys are disposed on normal and managed-error paths.
  The image viewer's process handle is disposed too; `--preview` intentionally
  leaves the external viewer open for you to inspect the image.
- A 3840 x 2160 RGBA pixel buffer alone is about 31.6 MiB. Peak process memory also
  includes .NET, native libraries, PNG encoding, and other buffers; it is not
  limited to the size of the PNG. Cache validation currently decodes the full PNG
  to preserve corruption detection.
- Windows releases process-owned memory and handles when the process terminates,
  including after a fatal crash. Fatal native crashes may bypass managed cleanup
  and leave temporary files, but do not leave a terminated Daymark process holding
  memory. Windows crash-reporting processes and an external viewer are separate.
- No forced garbage collection is used. Process exit is the final reclamation
  boundary; absolute leak-free behavior of .NET, SkiaSharp, or Windows is not
  guaranteed without platform-specific profiling.
- Logs have a **combined budget of 1 MB (1,000,000 bytes)**: each primary/fallback
  `app.log` is capped at 500,000 bytes. When the
  next entry would exceed the limit, that file is cleared before writing the new
  entry. There are no rotated archives. Oversized entries are UTF-8-safe truncated;
  existing oversized logs are reduced on their next write. Each file has its own
  cap, so using both log locations cannot exceed the combined 1 MB budget after
  each has been maintained by the updated logger.

## Troubleshooting and validation status

- Normal execution is intentionally silent. If the wallpaper does not change,
  inspect `%LOCALAPPDATA%\Daymark-wallpaper\app.log`.
- Use `--regenerate` to replace the cached image.
- Use `--preview` to inspect the design without changing the desktop.
- Invalid arguments and rendering or wallpaper-setting failures return exit code
  `1`; successful execution returns `0`.
- In normal mode, success requires both the wallpaper-setting API to succeed and
  `SPI_GETDESKWALLPAPER` to report the generated image as the primary-monitor
  wallpaper. A different or empty path, or a failed read-back, returns exit code `1`
  and logs an error rather than reporting success.
- Common current-user and machine wallpaper policies (`NoChangingWallPaper` and
  an enforced `Wallpaper`) are checked before any wallpaper or positioning
  settings are changed. If a restriction is present, the application logs it and
  exits with code `1`. It does not modify or bypass those policies; preview remains
  available. Other MDM/management-agent restrictions may not appear in these keys.
- A startup log entry is written on every launch, including successful runs.
  Cache read failures are logged before regeneration. Failure entries include
  the operation being attempted and the full exception, including inner errors.
- If the usual log cannot be written, diagnostics are attempted at
  `%TEMP%\Daymark-wallpaper\app.log`. If neither location is writable, a local
  .NET trace error is emitted and the application remains silent.

### Wallpaper does not change, or no log appears

Use the freshly published Windows executable, not a standalone executable copied
from the normal build folder. Copy the new publish output after rebuilding.
Run the following in PowerShell from the folder containing that executable:

```powershell
$process = Start-Process -FilePath ".\Daymark-wallpaper.exe" -ArgumentList "--regenerate" -Wait -PassThru
$process.ExitCode
Get-Content "$env:LOCALAPPDATA\Daymark-wallpaper\app.log" -Tail 40
```

Exit code `1` indicates a failure; the log names the failed stage. If the normal
log is missing, check the fallback:

```powershell
Get-Content "$env:TEMP\Daymark-wallpaper\app.log" -Tail 40
```

`--preview`, `--date`, and `--year` intentionally do not change the desktop.
Use no arguments or `--regenerate` to apply the wallpaper. If neither log exists
after launching the updated executable, it may be failing before managed startup;
check Windows Event Viewer under **Windows Logs > Application** for the matching
application/.NET error and verify that Windows security software did not block it.
A `Verified` entry means Windows reported the expected primary-monitor wallpaper
path at read-back, not that screen pixels were inspected. Windows may suppress
background images, and policies or another utility may override the image later.
The utility does not remain running to monitor or fight subsequent changes.

The test project includes Linux native SkiaSharp assets only when testing on Linux,
so rendering tests can run there too (Linux requires `libfontconfig.so.1`).
The production executable still bundles only
Windows native assets. Tests cover deterministic PNG rendering, output dimensions,
day-state center pixels, and the outer glow at both fixed image sizes and at 1080p.
Month-gap distances and consistent, non-overlapping cells are tested for normal
and leap years in both supported aspect ratios. All 77 cases passed on Linux;
self-contained Windows Release publishing also succeeded.

Windows end-to-end checks for silent sign-in execution, wallpaper Fit behavior,
preview opening, process exit, visual quality, and resource usage are still pending.
Tests also cover startup/error logging, fallback logging, corrupt cache recovery,
cache date/size invalidation, development-mode argument selection, wallpaper-path
verification, policy restriction decisions, repeated log writes, oversized log
recovery, and UTF-8-safe oversized-entry truncation.
The updated design invalidates older cached
images automatically through metadata version 2.

## Project structure

- [Program.cs](./Daymark-wallpaper/Program.cs): command-line parsing and application lifecycle.
- [RunDiagnostics.cs](./Daymark-wallpaper/RunDiagnostics.cs): overlapping-run protection and local performance measurements.
- [WallpaperLayout.cs](./Daymark-wallpaper/WallpaperLayout.cs): date logic, render-size selection, deterministic palette, and grid geometry.
- [WallpaperRenderer.cs](./Daymark-wallpaper/WallpaperRenderer.cs): SkiaSharp drawing and PNG encoding.
- [WindowsRuntime.cs](./Daymark-wallpaper/WindowsRuntime.cs): Windows integration, file caching, and logging.
- [WallpaperLogicTests.cs](./Daymark-wallpaper.Tests/WallpaperLogicTests.cs): xUnit tests.