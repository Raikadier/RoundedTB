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
| **v13** | Ganar carrera RGN (timer+estimate) | Flash ↓; **rompe iconos** / Simple margins |
| **v14** | Overlay máscara | Flash del **borde superior** residual |
| **v15** (actual) | **Volver al modelo Gniang**: sin máquina ABS_AUTOHIDE | OK si **márgenes = 0** |

**Lección:** el upstream que funciona no pelea Explorer en AH. Nuestros “fixes” (peek-strip, overlay, TaskbarAnimations=0) **creaban** el destello. Además: con Windows AH, **MarginTop > 0** recorta la franja de hover → la TB no aparece; márgenes 0 = hover OK.

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
