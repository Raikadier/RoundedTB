# HANDOFF — Continuar en agente Windows local

Fecha: 2026-08-06 (actualizado tarde)  
Repo: https://github.com/Raikadier/RoundedTB  
Rama activa: `master`  
PR #1 (tray / AH v6 / FillOnMaximise / watchdog): **MERGED** → `87e2e4f`

---

## 0. Arranque

```powershell
cd "D:\Github repos\RoundedTB"
git pull origin master
.\build-install-run.ps1
```

Tras lanzar: **2** procesos `RoundedTB` (main + `--watchdog`).

---

## 1. Estado validado (PR #1)

| # | Pedido | Estado |
|---|--------|--------|
| A | X → system tray | OK |
| B | Windows AH + RTB Always show (hide/reveal básico) | OK |
| C | Dynamic + FillOnMaximise off | OK |
| D | End task → watchdog restaura | OK |
| E | Program Files + Start menu | OK |

---

## 2. Trabajo en curso — flash al **mostrar** con Windows AH

**Objetivo del usuario:** Windows “Automatically hide the taskbar” ON + RTB AutoHide **Always show**, para maximizar apps a pantalla casi completa (RTB AutoHide no sirve: reserva work-area / no es AppBar real).

**Síntoma:** al aparecer la TB, a menudo un destello de barra stock/entera antes del pill. A veces sale limpia.

**Por qué:** Explorer también pone regiones al deslizar ([torchgm #36](https://github.com/torchgm/RoundedTB/issues/36)). Upstream abandonó Windows AH. No hay port limpio desde Gniang/torchgm.

### Evolución en este fork (ver FIXING.md §11)

| Ver | Idea | Resultado |
|-----|------|-----------|
| v7 | Clear RGN solo en hide; en show reaplicar rounded | Mejor, flash sigue |
| v8 | Opacity-gate en reveal; alpha en peek rompe hover | Gate ok; peek no tocar alpha |
| v9 | Pre-armar last-good pill\|strip con cursor cerca del borde | Flash deja de ser mayoría, sigue ~mitad |
| **v10** (código actual) | Pre-arm + **alpha=1 durante todo el slide**; alpha 255 solo con rect estable | Instalado en Program Files; **pendiente feedback del usuario** |

### Archivos tocados (v7–v10)

- `RoundedTB/Background.cs` — máquina de estados native AH
- `RoundedTB/Taskbar.cs` — `SetTaskbarAlpha`, `ApplyRounding`, `RememberGoodLayout`, `ApplyNativeAutohidePeekArmed`, `IsCursorNearAutohideEdge`, show/hide helpers
- `RoundedTB/Types.cs` — `LastGood*`, `NativeAhRevealPending`
- `RoundedTB/LocalPInvoke.cs` — `GetWindowRgn` correcto, `RGN_OR`
- `FIXING.md`, `AGENTS.md`

### Cómo probar al volver

1. Windows AH ON, RTB AutoHide = Always show, Dynamic ON.  
2. Hover borde varias veces (~10–20).  
3. ¿Destello stock? ¿La barra “aparece de golpe” ya redondeada (pop limpio)?  
4. Log: `%LocalAppData%\rtb.log` — línea `show stays alpha=1 until stable`.

### Si el destello sigue

Siguiente idea (no implementada): `SetWinEventHook(EVENT_OBJECT_LOCATIONCHANGE)` en `Shell_TrayWnd` para gate en el mismo instante del move (el poll 1 ms aún pierde frames). No usar RTB AutoHide como “solución” (rompe maximize full-bleed).

---

## 3. Paths

| Qué | Path |
|-----|------|
| Install | `C:\Program Files\RoundedTB\` |
| Settings | `%LocalAppData%\rtb.json` |
| Log | `%LocalAppData%\rtb.log` |
| Watchdog | `%LocalAppData%\rtb.watchdog.json` |
| One-shot | `build-install-run.ps1` |

Leer: [`AGENTS.md`](AGENTS.md) · [`FIXING.md`](FIXING.md) §11 · [`ARCHITECTURE.md`](ARCHITECTURE.md)
