![RoundedTB](https://cdn.discordapp.com/attachments/272509873479221249/891555515799318568/unknown.png)

# RoundedTB (Raikadier fork)

Add margins, rounded corners and segments to your Windows taskbar — maintained for **current Windows 11** on **.NET 8**.

![image](https://user-images.githubusercontent.com/31840547/134795141-76349eaf-12da-40f8-b2a0-d7b7c268d152.png)

## How do I get it?

Download the latest build from this fork’s [**Releases**](https://github.com/Raikadier/RoundedTB/releases).

- Upstream community fork: [Gniang/RoundedTB](https://github.com/Gniang/RoundedTB)  
- Original project: [torchgm/RoundedTB](https://github.com/torchgm/RoundedTB)  
- The Microsoft Store app is **not** this fork.

**Requirements:** Windows 10 2004+ / Windows 11 (built against `net8.0-windows10.0.19041.0`), [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) if you run the framework-dependent build.

## What’s new in this fork (v1.1.0)

Validated for recent Windows 11:

- Stable worker loop (thread-safe settings, crash logging, no silent worker death)
- Reliable tray host (close config → stay in tray; Exit restores the stock taskbar)
- **TaskbarWatchdog** — if RoundedTB is killed from Task Manager, regions are cleared
- Dynamic mode hardening (AppList/tray clamping, FillOnMaximise default off)
- Windows **“Automatically hide the taskbar”** + RoundedTB **Always show** works like Gniang when **margins are 0** (especially Top). Non-zero top margin clips the AH hover strip so the bar never appears
- No experimental AH “flash machines” (peek-strip / overlay / animation registry hacks) — those caused regressions

Quality notes (ISO/IEC 25010): [`QUALITY.md`](QUALITY.md)

## Windows autohide

| Setting | Recommendation |
|--------|----------------|
| Windows Settings → Taskbar → Automatically hide | Optional (full-bleed maximize) |
| RoundedTB Auto-hide | **Always show** when using Windows hide |
| Margins (Top/Left/Right/Bottom) | **0** if Windows hide is on |
| Fill taskbar when maximised | Uncheck to keep Dynamic rounding over maximised apps |

## Known limitations

- Rounded corners are not antialiased (Win32 `SetWindowRgn` limit — [torchgm #4](https://github.com/torchgm/RoundedTB/issues/4))
- TranslucentTB: enable “Improve compatibility…” if you use both (may flicker)
- Dynamic mode quirks on first start / alignment — toggle Left↔Center if the pill looks wrong ([#98](https://github.com/torchgm/RoundedTB/issues/98))
- Fighting Explorer mid-slide regions is inherently racy ([torchgm #36](https://github.com/torchgm/RoundedTB/issues/36)); this fork follows Gniang’s simple refresh model

## Build

```powershell
dotnet build RoundedTB.sln -c Release
# or one-shot install + run (UAC):
.\build-install-run.ps1
# smoke / reliability checks:
.\scripts\smoke-test.ps1
```

| Path | Location |
|------|----------|
| Exe | `RoundedTB\bin\Release\net8.0-windows10.0.19041.0\RoundedTB.exe` |
| Config | `%LocalAppData%\rtb.json` |
| Log | `%LocalAppData%\rtb.log` |

## Docs for contributors / agents

- [`AGENTS.md`](AGENTS.md) — invariants  
- [`FIXING.md`](FIXING.md) — hardening history & traps  
- [`ARCHITECTURE.md`](ARCHITECTURE.md) — runtime map  
- [`HANDOFF.md`](HANDOFF.md) — current AH notes  

## Credits

Created by [torchgm](https://github.com/torchgm/RoundedTB). Continued by [Gniang](https://github.com/Gniang/RoundedTB). This repository hardens the Gniang baseline for modern Windows 11.

If something breaks badly: <kbd>Ctrl</kbd>+<kbd>Shift</kbd>+<kbd>Esc</kbd> → end RoundedTB → restart Explorer if needed. RoundedTB makes no permanent system changes (aside from optional “start with Windows” from the tray).
