# AGENTS.md — RoundedTB

Guía corta para agentes (Cursor u otros) que abran este repo.

## Antes de tocar código

1. Leer [`FIXING.md`](FIXING.md) — historial completo del hardening y trampas.
2. Leer [`ARCHITECTURE.md`](ARCHITECTURE.md) — mapa runtime y módulos.
3. No inventar segunda base de docs fuera del repo para este proyecto.

## Stack rápido

- WPF · `net8.0-windows10.0.19041.0`
- Loop: `Background.DoWork` ~100 ms → `Taskbar.UpdateSimple/Dynamic` → `SetWindowRgn`
- Config/log: `%LocalAppData%\rtb.json` / `rtb.log`

## Invariantes

- Tras `SetWindowRgn` **exitoso**, no `DeleteObject` de esa HRGN.
- Worker muta solo `settings.Clone()`, nunca el objeto UI sin lock.
- Cerrar la ventana oculta; no es exit (`shouldReallyDieNoReally`). Exit real → `RestoreAllTaskbars`.
- Logging debe permanecer activo (crash forensics).
- **No** reinventar hit-strips full-width para Windows autohide (pintan bordes fantasma). Con ABS_AUTOHIDE: freeze RGN en peek/slide; preferir Always hide de RTB (torchgm #36).

## Comandos

```powershell
dotnet build RoundedTB.sln -c Release
# exe: RoundedTB\bin\Release\net8.0-windows10.0.19041.0\RoundedTB.exe
```

## Al diagnosticar

1. ¿Proceso vivo? `Get-Process RoundedTB`
2. Cola de `rtb.log` (heartbeat ~60 s, excepciones, `App.OnExit`, `RestoreAllTaskbars`)
3. Settings en `rtb.json` — `FillOnMaximise`, `IsDynamic`, `ShowSegmentsOnHover`, `AutoHide`
4. Dynamic: UIA `TaskbarFrame` desde `InputSite.WindowClass`
5. Parpadeo + Windows AH: esperado si se pelea el slide; ver FIXING §11

## Alcance preferido

Cambios mínimos, evidencia primero (log/rects). No reintroducir dead code (`TaskbarEffect`, `AppBars`, `IAppVisibility`).
