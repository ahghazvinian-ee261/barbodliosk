# Barbod Kiosk

A native Windows fullscreen kiosk browser, replacing the Python/Selenium/Firefox
script. Built with .NET 8 + WebView2 (the Chromium engine already built into
Windows 10/11) — no external browser, no geckodriver, no Selenium.

## Why this is faster than the Selenium version

- No separate Firefox process + WebDriver protocol round-trips. WebView2 is an
  in-process control — navigation and JS calls are direct.
- No Python interpreter startup.
- Compiled, self-contained native exe — starts in a fraction of a second.
- Polling logic runs on a lightweight in-process timer instead of a Python
  `while True: sleep()` loop with `requests` calls.

## What it does (mirrors your script exactly)

1. Fetches `url.txt` to get the target URL, opens it fullscreen on the
   **largest monitor** (prefers an extended/non-primary monitor when it's the
   biggest — matches "extended monitor... larger size... aimed to be used by
   full screen browser").
2. Every 3 seconds, fetches `number.txt`, extracts the 4-digit code.
3. If the code changed: re-checks `url.txt` (in case the URL changed too),
   clears localStorage/sessionStorage, and hard-reloads with a `?nocache=`
   cache-busting timestamp — exactly like `hard_reload()` in the Python script.
4. Re-asserts fullscreen bounds after every reload (handles monitor
   hot-plug/resolution changes).

## Prerequisites (on the machine you BUILD on)

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (only needed
  to build — NOT needed on the kiosk PC that runs the final .exe, since it's
  self-contained).

## Prerequisites (on the KIOSK machine that RUNS the exe)

- **WebView2 Runtime** — pre-installed on all Windows 11 machines and most
  up-to-date Windows 10 machines. If missing, Windows will prompt, or you can
  install the "Evergreen Bootstrapper" once from:
  https://developer.microsoft.com/microsoft-edge/webview2/
- Nothing else. No Python, no Firefox, no geckodriver.

## Build

Double-click `build.bat`, or run manually:

```
dotnet publish -c Release -r win-x64 --self-contained true
```

The finished exe appears at:
```
bin\Release\net8.0-windows\win-x64\publish\BarbodKiosk.exe
```

Copy just that one `.exe` to the kiosk PC — that's the entire deliverable.

## Config

Edit the top of `Program.cs` before building if URLs/timing need to change:

```csharp
private const string UrlSource = "https://barbodinstitute.ir/clock/calendar/url.txt";
private const string TxtUrl = "https://barbodinstitute.ir/clock/calendar/number.txt";
private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(3);
```

## Exiting the kiosk

Press **Ctrl+Shift+Esc** while the window is focused. (Remove the `KeyDown`
handler in `Program.cs` if you want it fully unkillable via keyboard.)

## Auto-start on boot (optional)

Place a shortcut to `BarbodKiosk.exe` in:
```
shell:startup
```
(paste that into Windows Explorer's address bar), or register it as a
Scheduled Task set to run at logon for more reliability (survives Explorer
crashes, can auto-restart on failure).

## Running on the correct monitor

No manual monitor index is required — on launch (and after every reload) it
enumerates `Screen.AllScreens`, sorts by pixel area (width × height)
descending, and picks the largest, preferring a non-primary/extended display
if it ties or wins on size. If you'd rather **pin to a fixed monitor index**
instead of "largest", that's a one-line change in
`PositionOnLargestExtendedMonitor()` — just ask and I'll adjust it.
