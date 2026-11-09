# TaskbarClear

[中文](README.md) | English

A tiny tool that makes the Windows 10 taskbar fully transparent. **No hooks, no injection into Explorer**, and measured zero overhead on system window events.

## Why

While chasing occasional stutter in Apex Legends and whole-system freezes when switching Task Manager tabs, I used a probe to time each call to `NotifyWinEvent` (the function Windows uses to broadcast window events):

- On my machine each call took **73–232 µs**; a clean system usually takes single-digit microseconds.
- The cause: every program that subscribes to window events with `SetWinEventHook` adds time to each event **while the global USER lock is held**. Keyboard and mouse input need the same lock.
- Switching one Task Manager tab fires tens of thousands of events. Multiply by hundreds of microseconds each and you get tens of seconds of freeze.

Closing resident programs one by one showed TranslucentTB was one of the biggest subscribers:

| State | CREATE event | NAMECHANGE event |
|---|---|---|
| TranslucentTB not running | 20.7 µs | 0.8 µs |
| TranslucentTB running (dynamic states on, default) | 69–84 µs | 81–89 µs |
| TranslucentTB running (all dynamic states off) | 47 µs | 46 µs |
| **TaskbarClear running** | **same as not running** | **same as not running** |

(Standard user, median of 7 runs. For TaskbarClear I compared three cases: running, not running, and applied once then exited. All three gave the same numbers.)

After closing TranslucentTB and a few other heavy subscribers, switching Task Manager tabs went from a **30–60 s full freeze** to **done in 5–10 s**. I still wanted a transparent taskbar, so I wrote TaskbarClear: it keeps only the "transparent" feature and implements it without any hook.

## Compared with TranslucentTB

| | TaskbarClear | TranslucentTB |
|---|---|---|
| WinEvent hooks | None | Yes |
| Injects into Explorer | No | Yes (`ExplorerHooks.dll`) |
| Extra cost per window event | Measured zero | Tens of µs per event |
| Size | One exe, about 9 KB | Needs installing |
| Dependencies | .NET Framework 4.x built into Win10 | Ships its own runtime |
| Source | One file, about 140 lines | Full application project |
| Resource use | About 15 MB RAM, near-zero CPU | — |
| Dynamic looks (tint when maximized, Start menu style, etc.) | Not supported | Supported |

**Good fit** if you just want a fully transparent taskbar and care about stable frame times or system responsiveness.
**Not a fit** if you want the taskbar style to change with window state, or you are on Windows 11.

## Install

There is no prebuilt release yet, so build it yourself. Win10 ships with the compiler; nothing to install:

```bat
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe -nologo -optimize -target:winexe -out:TaskbarClear.exe TaskbarClear.cs
```

Double-click `TaskbarClear.exe` and it runs in the background with no window. Only one instance is allowed.

Start with Windows (replace the path with yours):

```bat
reg add HKCU\Software\Microsoft\Windows\CurrentVersion\Run /v TaskbarClear /t REG_SZ /d "\"D:\Tools\TaskbarClear.exe\"" /f
```

Remove from startup:

```bat
reg delete HKCU\Software\Microsoft\Windows\CurrentVersion\Run /v TaskbarClear /f
```

Quit (it has no window, so `/f` is required):

```bat
taskkill /f /im TaskbarClear.exe
```

## Usage

| Argument | Effect |
|---|---|
| (none) | Run in the background, re-apply transparency every 30 s as a fallback |
| `/interval 60000` | Set the fallback interval to 60 s (minimum 1 s) |
| `/once` | Apply transparency once, then exit |
| `/restore` | Restore the system default look, then exit |
| `/keeponshell` | Don't switch back to the system look while Start or Search is open |
| `/log D:\path\log.txt` | Log foreground changes and each apply to a file, for troubleshooting |
| `/debug` | With a console build (`-target:exe`), print foreground changes to stderr |

## How it works

- Uses `SetWindowCompositionAttribute` to set a transparent accent (`ACCENT_ENABLE_TRANSPARENTGRADIENT`, color 0) on the primary taskbar `Shell_TrayWnd` and every secondary taskbar `Shell_SecondaryTrayWnd`.
- Explorer resets the style from time to time, and the current style can't be read across processes (`GetWindowCompositionAttribute` fails). So it **reads the foreground window's class every 100 ms** instead. That is a plain read; it subscribes to nothing.
- While Start, Search, Action Center, Task View or the taskbar itself is in front, Explorer is styling the taskbar itself, so TaskbarClear **leaves the taskbar alone** to avoid a tug-of-war that causes flicker.
- When focus returns to a normal window it re-applies at once, then again after 100, 300 and 700 ms, plus a fallback every 30 s.

## FAQ

**With ExplorerPatcher installed, the primary taskbar's search box flickers when I open Start on a secondary screen?**
That comes from ExplorerPatcher's secondary-taskbar icon centering, not from TaskbarClear: it still flickers with TaskbarClear closed, and stops once only ExplorerPatcher's secondary centering is turned off. To stop it, turn off secondary centering in ExplorerPatcher's settings, or run this and restart Explorer:

```bat
reg add HKCU\Software\ExplorerPatcher /v MMOldTaskbarAl /t REG_DWORD /d 0 /f
```

**The taskbar sometimes turns opaque again?**
It comes back within 30 s, or as soon as you switch windows. After Explorer restarts it also applies to the new taskbar automatically.

## Tech stack

C# (single file) + Win32 API via P/Invoke, built with the .NET Framework 4.x `csc.exe` that ships with Windows. No third-party dependencies.

## License

[GPL-3.0](LICENSE)
