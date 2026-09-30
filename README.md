# Clicker — WPF auto clicker

An auto clicker for Windows 10/11, built with .NET 8 and WPF.

## Features
- **Custom start/stop hotkey** — any key with optional Ctrl/Alt/Shift/Win, or the side (Mouse 4/5) or middle mouse button. Default: `F6`.
- **Click interval in milliseconds** — from 1 to 3,600,000, with a clicks-per-second preview. The interval can be changed while clicking.
- **Mouse button** — left, right or middle.
- **Saved screen points** — hover over a spot and press `F7` (configurable) to save the cursor position as a named point. Add as many as you need; each has an editable name and an on/off switch, and only one can be on at a time. When a point is on, the cursor jumps there on start and the clicks land on it (optionally the cursor is put back on the point before every click). With all points off, clicks go to the current cursor position.
- **Pattern recording** — press `F8` (configurable), do any mouse moves, clicks, wheel scrolls and (optionally) key presses, then press `F8` again. The recording is saved as a named pattern. Switch a pattern on and `F6` replays it in a loop with the original timing, instead of auto-clicking — set the number of repeats (0 = endless) and the pause between them. Keys and buttons still held when playback stops are always released.
- **Tabs**: Auto-click (interval, button, points), Patterns, Hotkeys.
- **Close button minimizes to the tray.** Left-click the tray icon to open the window, right-click for the Open / Start-Stop / Exit menu. A green dot on the icon shows that the clicker is running.
- **Ukrainian and English UI**, switchable on the fly. On first launch the language follows the Windows language.
- Settings are saved to `%APPDATA%\Clicker\settings.json`, patterns to `%APPDATA%\Clicker\patterns.json`.
- Single instance: launching the exe again just brings the running window to the front.

## Games
- Clicks are sent with `SendInput`, a driver-level input path that games using DirectInput / Raw Input also receive.
- The button is held down for up to 30 ms (never longer than half the interval), because many games read the button state once per frame and miss "instant" clicks.
- The hotkey uses low-level hooks (`WH_KEYBOARD_LL` / `WH_MOUSE_LL`) on a dedicated thread, so it works even while a fullscreen game has focus.
- Without an active point, clicks happen at the current cursor position. With a point, the cursor is moved there via `SendInput` with absolute virtual-desktop coordinates (works with multiple monitors and display scaling), then the clicker waits a few ms so the game registers the hover.
- The clicker never clicks on its own window, so pressing Start with the mouse doesn't immediately stop it.
- Patterns are recorded from the same low-level hooks (the hotkeys themselves and clicks on the clicker's own window are left out) and replayed with `SendInput`; keys are sent by hardware scan code, which is what most games read. Moves and clicks use absolute screen coordinates, so keep the game window in the same place and resolution as when recording. In games where the mouse turns the camera (shooters), replayed mouse movement won't match.
- Timing is accurate to 1 ms (`timeBeginPeriod(1)` + `Stopwatch`).

**If the game runs as administrator**, Windows (UIPI) blocks input from regular programs. Use the "Restart as administrator" button.

**Online games with anti-cheat** (Vanguard, EAC, BattlEye, etc.) may detect synthetic input and ban the account. Use it in single-player games.

## Build and run
Requires the .NET SDK 8 or newer (`dotnet --version`).

```
cd C:\work\clicker
dotnet run --project Clicker
```

Or open `Clicker.sln` in Visual Studio 2022 and press F5.

To get a standalone exe, run `publish.cmd`. It produces `publish\Clicker.exe`; the target PC needs the .NET Desktop Runtime 8+.

## Project structure
```
Clicker/
  App.xaml(.cs)                     — startup, single instance, styles
  MainWindow.xaml(.cs)              — window layout, start/stop, status, language, tray
  MainWindow.Hotkeys.cs             — hotkey dispatch and recording new hotkeys
  MainWindow.Points.cs              — saved screen points
  MainWindow.Patterns.cs            — pattern recording, list, playback settings
  Localization/Loc.cs               — UA/EN strings, live language switching
  Localization/TrExtension.cs       — {loc:Tr Key} markup extension for XAML
  Models/                           — settings, hotkeys, points, patterns
  Services/ClickEngine.cs           — auto-click thread
  Services/PatternRecorder.cs       — collects input events while recording
  Services/PatternPlayer.cs         — replays a pattern in a loop
  Services/InputSender.cs           — SendInput wrapper (mouse, wheel, keys)
  Services/GlobalHotkeyListener.cs  — global keyboard/mouse hooks
  Services/TrayIcon.cs              — tray icon and menu
  Services/SettingsStore.cs         — settings.json
  Services/PatternStore.cs          — patterns.json
  Services/NativeMethods.cs         — Win32 P/Invoke
```

## Roadmap
See [ROADMAP.md](ROADMAP.md).
