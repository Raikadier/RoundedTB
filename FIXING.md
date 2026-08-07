# RoundedTB — Historial de fixing (contexto para agentes)

> **Lee esto primero** si vas a continuar trabajo en este fork.  
> Complementa: [`ARCHITECTURE.md`](ARCHITECTURE.md) · [`AGENTS.md`](AGENTS.md) · [`README.md`](README.md)

**Base upstream:** [Gniang/RoundedTB](https://github.com/Gniang/RoundedTB) @ `b78e5d6`  
**Objetivo del fork local:** que RoundedTB sea usable en Win11 actual (dynamic mode, estabilidad, net8) sin reescribir el modelo de clipping.

**Estado (2026-08-06):** Código de tray-close + AH nativo v6 + FillOnMaximise default off + watchdog está en rama `cursor/tray-autohide-fillmax-ac69` / [PR #1](https://github.com/Raikadier/RoundedTB/pull/1). **Falta validación en Windows local** — ver [`HANDOFF.md`](HANDOFF.md). Cloud Linux no puede ejecutar el exe WPF.

---

## 1. Qué es el producto (modelo mental)

RoundedTB **no** redibuja la taskbar. Cada ~100 ms:

1. Localiza `Shell_TrayWnd` / secundarias.
2. Mide rects (AppList, Tray, taskbar).
3. Crea regiones GDI (`CreateRoundRectRgn` / `CombineRgn`).
4. Aplica `SetWindowRgn` → Windows recorta paint + hit-test.

**ADR implícito:** seguir con `SetWindowRgn` + hardening. DWM rounded corners **no** sustituyen márgenes ni segmentos dinámicos.

Config: `%LocalAppData%\rtb.json`  
Log: `%LocalAppData%\rtb.log` (y rotación a `rtb.log.old`)

---

## 2. Síntomas que motivaron el trabajo

1. Dynamic mode no ajustaba el “pill” a la cantidad de iconos.
2. “Show segments only when hovered” parecía no mostrar tray al cerrar la UI.
3. Sospecha (confirmada) de **cierre inesperado del proceso** → sin efecto visual porque el worker deja de correr.
4. Build bloqueado: runtime `dotnet` sin SDK; luego SDK 8 instalado.

---

## 3. Diagnóstico empírico (Win11, taskbar centrada)

Herramienta temporal UIA (eliminada tras medir) mostró:

| Fuente | Resultado |
|--------|-----------|
| `MSTaskSwWClass` (HWND legacy) | Rect fijo-ish; **no refleja bien** iconos XAML (p.ej. 397–881 mientras iconos UIA iban a ~1014). |
| `TaskbarFrame` vía `ElementFromHandle(Shell_TrayWnd)` + Descendants | **No encontrado** — el árbol UIA de `Shell_TrayWnd` casi no expone el island XAML. |
| `TaskbarFrame` vía `InputSite.WindowClass` (fast path HWND) | **Sí** — hijos = Start + botones `Appid:*`; tray = hermanos `SystemTrayIcon`. |

Implicación: dynamic **depende** de `AppListXaml` (UIA). Si falla, cae al HWND y “no se ajusta”.

Settings del usuario al diagnosticar (`rtb.json`):

- `IsDynamic: true`, `IsCentred: true`
- `ShowSegmentsOnHover: true`, `ShowTray/ShowWidgets: false`
- `FillOnMaximise: true`, `AutoHide: 0`
- `CompositionCompat: true`

**Fill on maximise:** si hay ventana maximizada en el monitor, `Background` hace `ResetTaskbar` + `continue` **antes** del hover → dynamic y segments-on-hover parecen “rotos”. No confundir con bug de “cerrar UI” (cerrar solo hace `Visibility.Hidden`).

---

## 4. Cambios realizados (por capa)

### 4.1 P0 — Ownership GDI (`Taskbar.cs`)

**Bug:** tras `SetWindowRgn` exitoso se hacía `DeleteObject` de la HRGN. En Win32, si `SetWindowRgn` tiene éxito, **el sistema posee** la región; liberarla es UB (crashes intermitentes / corrupción).

**Fix:** flag `regionOwnedBySystem` / `workingRegionOwnedBySystem`; solo `DeleteObject` si el set falló. Regiones intermedias (tray/widgets/clock) siguen siendo del caller.

### 4.2 P0 — `AppListXaml` más resiliente (`Types.cs`)

- Guard `IsWindow` antes de UIA.
- Fast path: bridge → `InputSite` → hijo `TaskbarFrame`.
- Fallback: Descendants desde `Shell_TrayWnd` (por si el árbol cambia).
- `ReloadTaskbarFrameElement` libera COM stale y re-adquiere.
- `GetWindowRect` ante excepción suelta el frame para `ReloadRequired`.
- `Dispose` null-safe.

> Nota: el fallback Descendants desde `Shell_TrayWnd` **puede seguir fallando** en builds donde UIA no cuelga el island del tray HWND; el fast path es el que importa. Si dynamic vuelve a fallar, priorizar STA/COM o medir desde `InputSite` siempre.

### 4.3 P1 — Concurrencia UI ↔ worker (`MainWindow` + `Background`)

- `DataLock` protege `taskbarDetails` / `activeSettings`.
- `Types.Settings.Clone()` + `CloneSegment`: el worker muta **snapshot** en hover (`ShowTray`/`ShowWidgets`), no el objeto bound a UI.
- Apply / OnClosing reales usan snapshot bajo lock.

### 4.4 P1 — Auto-hide fade sin `Thread.Sleep` largo (`Background.cs` + `Types.Taskbar`)

Antes: Sleeps en el worker durante fade → deja de poll-ear rects.  
Ahora: `FadeAnimDir` / `FadeAnimStep` + `FadeSteps`; un step de opacidad por tick (~100 ms).

### 4.5 P2 — Tray secundario (`Taskbar.GenerateTaskbarInfo`)

`GetWindowRect` del tray secundario usa `hwndSecTray`, no el tray del primario.

### 4.6 P2 — net8 + limpieza

- TFM: `net8.0-windows10.0.19041.0`
- Compatibility package 8.x
- Dead code eliminado: `AppBars.cs`, `IAppVisibility.cs`, `TaskbarEffect.xaml(.cs)`
- Deps ruidosas recortadas donde aplicó el csproj

### 4.7 Observabilidad y supervivencia del proceso

**Problema:** `AddLog` estaba comentado (no-op). Proceso podía morir sin rastro. Worker solo catch-eaba `TypeInitializationException` y **rethrow** → mataba el BW. Sin `RunWorkerCompleted`.

**Fix:**

| Pieza | Comportamiento |
|-------|----------------|
| `Interaction.AddLog` | Append thread-safe a `rtb.log`, rotación ~5 MB → `.old` |
| `Interaction.WriteCrashLog` | Static para handlers tempranos |
| `App.OnStartup/OnExit` | Log + `DispatcherUnhandledException` (Handled), `UnhandledException`, `UnobservedTaskException` |
| `Background.DoWork` | `catch (Exception)` log + sleep 500 ms, **sigue el loop** |
| `RunWorkerCompleted` | Log + auto-restart si no es exit real |
| Heartbeat | ~60 s (uso diario; antes ~5 s en fase debug) |
| `FileSystem` | **No** trunca el log al arrancar (preserva evidencia entre sesiones) |
| OnClosing (hide) | Log `"UI hidden..."` |

---

## 5. Mapa de archivos tocados

| Archivo | Rol del cambio |
|---------|----------------|
| `RoundedTB/Taskbar.cs` | GDI ownership, tray secundario |
| `RoundedTB/Types.cs` | AppListXaml hardening, FadeAnim*, Settings.Clone |
| `RoundedTB/Background.cs` | snapshot, fade steps, catch-all, heartbeat |
| `RoundedTB/MainWindow.xaml.cs` | DataLock, Apply/Close locks, RunWorkerCompleted |
| `RoundedTB/Interaction.cs` | logging real |
| `RoundedTB/App.xaml.cs` | crash handlers |
| `RoundedTB/RoundedTB.csproj` | net8 + deps |
| Eliminados | `AppBars.cs`, `IAppVisibility.cs`, `TaskbarEffect.*` |

---

## 6. Cómo construir y ejecutar (cotidiano)

Requisitos: [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) + Windows Desktop workload.

```powershell
dotnet build "RoundedTB.sln" -c Release
Start-Process "RoundedTB\bin\Release\net8.0-windows10.0.19041.0\RoundedTB.exe"
```

Debug (desarrollo):

```powershell
dotnet build "RoundedTB.sln" -c Debug
Start-Process "RoundedTB\bin\Debug\net8.0-windows10.0.19041.0\RoundedTB.exe"
```

Tray icon = UI. Cerrar la ventana **no** mata el proceso (solo oculta). Salir de verdad = menú Exit / `shouldReallyDieNoReally`.

### Si “deja de redondear”

1. `Get-Process RoundedTB` — ¿vivo?
2. Leer cola de `%LocalAppData%\rtb.log`:
   - último `bw heartbeat` antiguo → proceso muerto o worker parado
   - `bw exception` / `DispatcherUnhandled` / `App.OnExit` → causa
   - heartbeat presente pero sin efecto → mirar FillOnMaximise / medida AppList

---

## 7. Comportamientos que parecen bugs pero son settings

| Observación | Causa frecuente |
|-------------|-----------------|
| Dynamic “lleno” con apps maximizadas | `FillOnMaximise: true` |
| Hover de tray no hace nada con max | Mismo `continue` antes de hover |
| Cerrar UI “rompe” todo | Proceso muerto (ver log), no el hide |
| Dynamic no sigue iconos | UIA `TaskbarFrame` falló → HWND legacy |
| Esquinas dentadas | Limitación Win32 RGN (conocido upstream) |

---

## 8. Deuda / siguientes candidatos (no bloquean uso diario)

1. **UIA desde MTA:** `BackgroundWorker` es MTA; UIA a veces es frágil. Evaluar STA dedicada o marshaling al UI thread solo para `AppListXaml.GetWindowRect`.
2. **Fill vs hover:** opcionalmente aplicar hover aunque fill esté activo, o documentar en UI el conflicto.
3. **ShowSegmentsOnHover:** cada tick con `Ignored=true` cuando no hay hover → refrescos constantes (CPU). Ideal: solo marcar Ignored en **transición** hover on/off.
4. Limpiar warnings CS0162 (código muerto en `TrayIconCheck`) si se toca esa zona.
5. Tests automatizados: difíciles (Shell vivo); al menos harness UIA de diagnóstico versionado bajo `tools/` si vuelve a romper measure.
6. TranslucentTB / auto-hide nativo: issues upstream sin cerrar.

---

## 9. Lo que un agente NO debe hacer sin evidencia

- No “arreglar” dynamic reescribiendo todo el clipper sin medir rects reales en la máquina del usuario.
- No volver a `DeleteObject` tras `SetWindowRgn` exitoso.
- No mutar `activeSettings` desde el worker (usar `Clone()`).
- No truncar `rtb.log` al startup.
- No reintroducir `Thread.Sleep` largos dentro del loop de fade.
- No asumir que cerrar la ventana = exit del proceso.

---

## 10. Checklist de handoff

- [x] net8 build
- [x] GDI ownership correcto
- [x] AppListXaml fast+fallback
- [x] DataLock + settings snapshot
- [x] Fade no bloqueante
- [x] Logging + crash handlers + worker restart
- [x] Validación usuario: “funcionando bien”
- [ ] Commit/PR (solo si el humano lo pide)
- [ ] STA/UIA hardening (opcional)
- [ ] Reducir refrescos de ShowSegmentsOnHover (opcional)

---

## 11. Follow-up fixes (2026-08-06, post daily-use)

### Exit left taskbar clipped
- **Cause:** `ResetTaskbar` only ran on the “real exit” OnClosing branch; `CloseMenuItem` set the flag *after* closing windows; `App.OnExit` did not restore. Log showed `UI hidden` + `App.OnExit` without `Exiting RoundedTB`.
- **Fix:** `RestoreAllTaskbars(reason)` (idempotent); flag set **before** Close; restore from OnClosing, `App.OnExit`, `SessionEnding`, and terminating unhandled exceptions.
- **Follow-up (Task Manager):** End task often cancels WPF close (tray hide) then `TerminateProcess` — no managed cleanup. **`TaskbarWatchdog`** (`RoundedTB.exe --watchdog <pid>`) waits on the main process and clears `SetWindowRgn`/layered flags from `%LocalAppData%\rtb.watchdog.json` HWNDs.

### Close UI exited the whole process (no tray)
- **Cause:** WPF-UI `TitleBar ApplicationNavigation="True"` maps to app-level shutdown (`Application.Shutdown`) on the close button — bypasses “cancel close → hide”. `ShowMenuItem` also called `Close()` on MainWindow. Default `ShutdownMode` can exit when the last window goes away.
- **Fix:** `ApplicationNavigation="False"`, `MinimizeToTray="True"`, `ShutdownMode=OnExplicitShutdown`, OnClosing hide path uses `Hide()`, Show menu only closes accessory windows, real Exit calls `Shutdown()` after restore.

### Dynamic “stops ~10 apps”
- **Cause:** Not a hard limit. When AppList approached/overlapped tray, `CheckDynamicUpdateIsValid` rejected updates → pill froze. Tiny positive gap + any tray also forced simple.
- **Fix:** `Taskbar.ClampAppListAwayFromTray`; force-simple only when `ShowTray` and gap is tiny; fallback still applies dynamic when validation is strict.

### Maximised window makes taskbar “full” / dynamic looks off
- **Cause:** `FillOnMaximise: true` (former default) calls `ResetTaskbar` whenever any maximised window shares the monitor — intentional “restore stock bar”, not a dynamic bug.
- **Fix:** Default `FillOnMaximise`/`FillOnTaskSwitch` to **false**; checkbox label clarifies it fills/disables dynamic rounding. Uncheck in UI if an old `rtb.json` still has it true.

### Windows native autohide hover broken / bar won’t hide while RTB running
- **Cause:** With Windows ABS_AUTOHIDE, only a thin edge stays on-screen. `SetWindowRgn` (centred pill + margin top) removes hit-tests from that strip → hover does nothing. Freezing without updating `TaskbarRect` left `rectMoved` stuck and kept a rounded RGN during slide so Explorer could not cleanly hide. Explorer also rewrites regions while sliding (torchgm #36).
- **Fix v1–v5:** See prior notes (hit-strip variants → freeze-only).
- **Fix v6:** Peek → `ApplyNativeAutohidePeekHitRegion`. Slide → `ResetTaskbar` once + freeze + **always update** stored rects. Stable shown → rounded. Skip RTB opacity AutoHide and FillOnMaximise while native AH is on. Some flicker can remain (Explorer limitation).
- **Fix v7 (show flash):** v6 cleared RGN on *any* slide, so on **show** the bar looked stock/full until stable. Asymmetric: **hide** → `ResetTaskbar` + freeze; **show** → no clear, force `UpdateDynamic`/`UpdateSimple` each fast tick (`IsNativeAutohideShowing`/`Hiding` via visible area vs monitor). Peek unchanged. Residual Explorer flicker possible (#36).
- **Fix v8:** Stock flash is Explorer painting before our RGN wins. Opacity on the **peek** strip breaks AH hover (alpha 0 = click-through; even alpha 1 interfered). Gate only on **reveal**: alpha 1 → `ApplyRounding` → 255; peek stays hit-strip with untouched alpha; faster poll when cursor is on the peek edge.
- **Fix v9:** Intermittent flash = race before reveal tick. When peeked **and** cursor on edge, pre-arm `last-good pill OR hit-strip` so the window is already rounded when Explorer slides it up; still opacity-gate on reveal; reject full-width AppList on first show frames.
- **Fix v10:** Still ~50% flash because reveal set alpha 255 while Explorer was mid-slide. Keep **alpha=1 for the whole show animation**; only alpha 255 when rect is stable. Wider “near edge” band to pre-arm earlier.
- **Fix v11:** Residual flicker after “stable”: debounce **~80 ms** at alpha=1 then re-`ApplyRounding` before 255; UI-thread `SetWinEventHook(LOCATIONCHANGE)` forces alpha=1 the instant Explorer moves the tray HWND (beats the worker poll).
- **Fix v11.1:** Calendar/Action Center hover caused endless flicker: LOCATIONCHANGE kept forcing alpha=1 while the bar was fully shown, fighting the Always-show fade-in. WinEvent gate now **skips** when `IsTaskbarFullyShownOnMonitor` (only mid-slide).
- **Fix v12:** Initial show flash still visible with alpha=1 alone (Explorer can composite a stock frame). During show slide: **`DWMWA_CLOAK` + alpha=1** (`BeginAhShowGate`); uncloak only after fully-on-monitor + **~100 ms** debounce + re-`ApplyRounding`. Peek/hide/exit always `EndAhShowGate`. Never cloak on peek (breaks hover).
- **Fix v13 (lateral):** Cloak/alpha on `Shell_TrayWnd` do not stick — Explorer wins attribute races. Stop hiding; **win the `SetWindowRgn` race**: UI `WinEvent LOCATIONCHANGE` + **4 ms timer** call `ApplyAhSlideRegion` (last-good dynamic pill, never live full-width UIA). While native AH is active, force **`TaskbarAnimations=0`** (restore on exit) so the bar teleports and the race is one frame.
- **Fix v13.1:** v13 fallback used `UpdateSimpleTaskbar` when no last-good → full-width bar + **Simple** margins (bottom gap with Dynamic bottom=0). Timer could also stomp Dynamic after settle. Dynamic slide path never uses Simple; clear `RevealPending`/Publish **before** `ApplyRounding`.
- **Fix v14 (definitive):** Stop fighting Explorer `SetWindowRgn` mid-slide entirely (root cause of clipped icons + unwinnable flash). During show: **corner-mask overlay** (RTB-owned click-through window = full rect DIFF roundrect) follows the TB via WinEvent; on settle hide overlay and `ApplyRounding` with live UIA. Peek = hit-strip only. `TaskbarAnimations=0` while running.
- **Fix v14.1:** Residual flash = **stock top edge** (1 frame after overlay hide before RGN, and/or top border visible through the hole). ApplyRounding **before** Hide; overlay hole flush-top + full-width **top-cap** strip; Sync via `Invoke` on reveal start.
- **Fix v15 (align Gniang):** User confirmed **[Gniang/RoundedTB](https://github.com/Gniang/RoundedTB)** works with Windows AH + Always show and no flash. Upstream has **zero** ABS_AUTOHIDE special-case — only normal `UpdateSimple/Dynamic` on rect change. This fork’s peek-strip / overlay / freeze / TaskbarAnimations gate **was** the top-edge flash. Removed that machine; Background matches upstream model again (keep Clone/lock/fade/watchdog hardening).
- **Key user finding (v15+):** With Windows AH, **MarginTop (and likely other edge margins) must be 0**. Peek hit-testing uses the on-screen edge of `Shell_TrayWnd`; any top inset from `SetWindowRgn` removes that strip → hover does nothing / bar won’t show. Gniang “works” when margins are 0; `MarginTop=3` looked like an AH bug but was clipping.
