# HANDOFF — Continuar en agente Windows local

> **Para el siguiente agente:** este trabajo quedó en Cloud (Linux).  
> **Debes correr en Cursor Desktop → Run on: This Computer (Windows)**  
> para build, instalar en Program Files y probar la taskbar.

Fecha: 2026-08-06  
Repo: https://github.com/Raikadier/RoundedTB  
PR: https://github.com/Raikadier/RoundedTB/pull/1  
Rama: `cursor/tray-autohide-fillmax-ac69` (HEAD tipificado abajo)  
Base: `master` @ `34d97e3`

---

## 0. Arranque obligatorio (Windows)

El usuario en `D:\Github repos\RoundedTB` tenía **cambios locales sin commit** que bloqueaban el checkout. Usar esto primero:

```powershell
cd "D:\Github repos\RoundedTB"
git stash push -u -m "wip local antes de tray-autohide"
git fetch origin
git checkout cursor/tray-autohide-fillmax-ac69
git pull origin cursor/tray-autohide-fillmax-ac69
.\build-install-run.ps1
```

Alternativa destructiva (si el stash no interesa; el remote ya tiene el trabajo):

```powershell
git reset --hard
git clean -fd
git fetch origin
git checkout cursor/tray-autohide-fillmax-ac69
.\build-install-run.ps1
```

`build-install-run.ps1` hace: `dotnet build -c Release` → copia a `C:\Program Files\RoundedTB\` (UAC) → acceso directo Inicio → lanza el exe.

Después de lanzar: confirmar **2** procesos `RoundedTB` (main + `--watchdog`).

---

## 1. Qué pidió el usuario (pendiente de validar en máquina real)

| # | Pedido | Estado código | Validar en Windows |
|---|--------|---------------|--------------------|
| A | Cerrar UI → system tray (no matar proceso) | Hecho | Cerrar X de config; proceso + icono tray vivos; menú Show/Close |
| B | Windows auto-hide ON, RTB AutoHide OFF → la TB se esconde y reaparece con hover | Hecho (v6) | Settings Windows AH on; RTB AutoHide=0; mouse al borde |
| C | Maximizar app no debe “apagar” dynamic / TB entera | Hecho (default FillOnMaximise=false) | Si `rtb.json` aún tiene `FillOnMaximise: true`, **desmarcar** checkbox y Apply |
| D | Kill por Administrador de tareas no deja TB inutilizable | Hecho (watchdog) | End task del proceso **principal** (no el watchdog); TB debe volver usable |
| E | Instalar en Program Files + acceso Inicio | Script listo | Tras `build-install-run.ps1` |

---

## 2. Cambios ya en la rama (no reimplementar)

### Tray / ciclo de vida
- `MainWindow.xaml`: `ApplicationNavigation="False"`, `MinimizeToTray="True"`  
  (`ApplicationNavigation=True` en WPF-UI 1.2.1 ⇒ `Application.Shutdown` al pulsar X).
- `App.xaml.cs`: `ShutdownMode=OnExplicitShutdown`; modo `--watchdog`.
- `OnClosing`: cancel + `Hide()` si no es exit real; exit real → `RestoreAllTaskbars` + `Shutdown()`.
- `ShowMenuItem_Click`: no hace `Close()` del MainWindow; solo accesorios + Hide.

### Windows native autohide (Background + Taskbar)
- Peek → `ApplyNativeAutohidePeekHitRegion` (franja fina).
- Slide (`rectMoved`) → `ResetTaskbar` una vez + freeze + **actualizar** `TaskbarRect` (bug anterior: rect stale ⇒ `rectMoved` eterno / no hide).
- Estable → reaplicar rounded (`Ignored=true`).
- Con AH nativo: no correr fade de RTB AutoHide; no FillOnMaximise.

### FillOnMaximise
- Defaults `false` en MainWindow / Interaction.
- Checkbox: “fill taskbar (disables dynamic rounding)”.
- Log al arrancar si dynamic + FillOnMaximise aún ON en `rtb.json`.

### Watchdog
- `RoundedTB/TaskbarWatchdog.cs`
- Estado: `%LocalAppData%\rtb.watchdog.json`
- Main publica HWNDs; al kill forzoso el helper limpia `SetWindowRgn`/layered.
- `MarkGracefulExit` en `RestoreAllTaskbars` para no pelear al salir limpio.
- Single-instance: por ventana título `RoundedTB`, no por conteo de procesos (el watchdog comparte nombre).

### Otros
- `res/Headbanner.png` → `HeadBanner.png` (case para Linux/CI).
- `.github/workflows/ci.yml` → `dotnet build` en `windows-2022` (Actions del fork puede estar sin runs / 403 al disparar).

Docs tocadas: `AGENTS.md`, `FIXING.md` §11, `README.md`.

---

## 3. Commits en la rama

```
7f6cc40 Add build-install-run.ps1 for one-shot Windows deploy/test.
e1b1c55 Update CI to dotnet build on windows-2022 with artifacts.
8462d18 Fix HeadBanner casing for Linux CI and modernize Windows build workflow.
b9d80f0 Fix tray close, Windows autohide hide path, and FillOnMaximise defaults.
```

(Base hardening previo en `master`: `34d97e3`.)

---

## 4. Checklist de prueba (agente Windows)

1. [ ] Checkout limpio de `cursor/tray-autohide-fillmax-ac69`
2. [ ] `.\build-install-run.ps1` OK; exe en Program Files
3. [ ] X en config → proceso sigue; tray icon; log: `UI hidden (close cancelled...)`
4. [ ] Menú tray **Close RoundedTB** → restore + exit; log `RestoreAllTaskbars` / `Exiting`
5. [ ] Windows AH on, RTB AutoHide 0 → hide al quitar mouse; hover en borde revela
6. [ ] Dynamic on, FillOnMaximise **off** → maximizar app mantiene pill (no barra full)
7. [ ] Task Manager End task al PID main → watchdog restaura TB; cola `rtb.log` / HWND limpios
8. [ ] Si algo falla: pegar cola de `%LocalAppData%\rtb.log` + settings relevantes de `rtb.json`

---

## 5. Paths útiles

| Qué | Path |
|-----|------|
| Exe build | `RoundedTB\bin\Release\net8.0-windows10.0.19041.0\RoundedTB.exe` |
| Install | `C:\Program Files\RoundedTB\` |
| Start menu | `%ProgramData%\Microsoft\Windows\Start Menu\Programs\RoundedTB.lnk` |
| Settings | `%LocalAppData%\rtb.json` |
| Log | `%LocalAppData%\rtb.log` |
| Watchdog state | `%LocalAppData%\rtb.watchdog.json` |
| Install script (legacy) | `install-to-programfiles.ps1` (si existe en working tree local) |
| One-shot | `build-install-run.ps1` |

---

## 6. Límites conocidos (no “arreglar” reinventando)

- AH nativo de Windows **siempre** pelea `SetWindowRgn` (torchgm #36). v6 es compromiso; algo de flicker puede quedar.
- No reintroducir hit-strip full-width **siempre** visible (bordes fantasma).
- Cloud agent Linux **no puede** ejecutar WPF ni tocar la taskbar real.
- GitHub Actions del repo: disparo/listado puede dar 403; no depender de CI para validar UX.

---

## 7. Tras validar

- Si OK: merge PR #1 a `master` (el usuario o agente local con permiso).
- Si hay bugs: evidencia en `rtb.log` + commits mínimos en la misma rama; actualizar este HANDOFF y el PR.

Leer también: [`AGENTS.md`](AGENTS.md) · [`FIXING.md`](FIXING.md) · [`ARCHITECTURE.md`](ARCHITECTURE.md)
