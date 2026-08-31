# AGENTS.md — RoundedTB

Guía corta para agentes (Cursor u otros) que abran este repo.

## Handoff activo (lee primero)

Windows AH + Always show: modelo **Gniang** (sin máquina ABS_AUTOHIDE). Con Windows AH, **márgenes = 0** (sobre todo Top) o el hover no revela la TB.  
Detalle: [`HANDOFF.md`](HANDOFF.md) · trampas: [`FIXING.md`](FIXING.md) §11.

## Antes de tocar código

1. Leer [`HANDOFF.md`](HANDOFF.md) si continúas AH / tray / maximize.
2. Leer [`FIXING.md`](FIXING.md) — historial completo del hardening y trampas.
3. Leer [`ARCHITECTURE.md`](ARCHITECTURE.md) — mapa runtime y módulos.
4. No inventar segunda base de docs fuera del repo para este proyecto.

## Stack rápido

- WPF · `net10.0-windows10.0.19041.0`
- Loop: `Background.DoWork` ~100 ms → `Taskbar.UpdateSimple/Dynamic` → `SetWindowRgn`
- Config/log: `%LocalAppData%\rtb.json` / `rtb.log`

## Invariantes

- Tras `SetWindowRgn` **exitoso**, no `DeleteObject` de esa HRGN.
- Worker muta solo `settings.Clone()`, nunca el objeto UI sin lock.
- Cerrar la ventana oculta; no es exit (`shouldReallyDieNoReally`). Exit real → `RestoreAllTaskbars`.
- TitleBar: `ApplicationNavigation=False` (si True, WPF-UI hace `Application.Shutdown` al cerrar la X).
- `ShutdownMode=OnExplicitShutdown` — ocultar UI no mata el proceso.
- Logging debe permanecer activo (crash forensics).
- Kill forzoso (Administrador de tareas) → proceso `--watchdog` restaura RGN; estado en `%LocalAppData%\rtb.watchdog.json`.
- **Windows autohide (ABS_AUTOHIDE):** mismo modelo que Gniang — solo rounding normal; no peek-strip/overlay/freeze. **Márgenes (esp. Top) = 0** o se recorta la franja de hover y la TB no aparece. No apilar RTB AutoHide (rompe maximize full-bleed).
- **No** reinventar hit-strips full-width *siempre* visibles ni pelear `SetWindowRgn` mid-slide (torchgm #36).
- **No** poner alpha 0/bajo en peek (rompe hover de Windows AH).

## Comandos

```powershell
dotnet build RoundedTB.sln -c Release
# exe: RoundedTB\bin\Release\net10.0-windows10.0.19041.0\RoundedTB.exe
.\build-install-run.ps1
```

## Al diagnosticar

1. ¿Proceso vivo? `Get-Process RoundedTB` (main + `--watchdog`)
2. Cola de `rtb.log` (heartbeat ~60 s, native autohide, excepciones, `App.OnExit`)
3. Settings en `rtb.json` — `FillOnMaximise`, `IsDynamic`, `ShowSegmentsOnHover`, `AutoHide`
4. Dynamic: UIA `TaskbarFrame` desde `InputSite.WindowClass`
5. Parpadeo / AH no aparece: ¿márgenes > 0? Ver FIXING §11 (v15). No reintroducir máquina AH.

## Alcance preferido

Cambios mínimos, evidencia primero (log/rects/alpha). No reintroducir dead code (`TaskbarEffect`, `AppBars`, `IAppVisibility`).
