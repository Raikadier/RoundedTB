# Calidad — ISO/IEC 25010 (fork Raikadier)

Evaluación práctica del producto frente a las características de calidad de [ISO/IEC 25010](https://iso25000.com/index.php/en/iso-25000-standards/iso-25010).  
Fecha de release: **v1.1.0** · Target: **Windows 11** (`net8.0-windows10.0.19041.0+`).

| Característica | Qué cubre RoundedTB | Evidencia / control |
|----------------|---------------------|---------------------|
| **Functional suitability** | Márgenes, radio, Dynamic/Simple, tray, Windows AH + Always show (márgenes 0), FillOnMaximise | Checklist manual + `scripts/smoke-test.ps1` |
| **Performance efficiency** | Loop ~100 ms; fade RTB sin `Sleep` bloqueante; sin pelear `SetWindowRgn` mid-slide | Worker + fast-poll solo en fade |
| **Compatibility** | Win11 actual; TranslucentTB opcional (`CompositionCompat`); Gniang-compatible AH | Target TFM 19041+; docs FIXING §11 |
| **Usability** | Tray host; X oculta; menú Exit restaura TB; logs en `%LocalAppData%` | Tray + `RestoreAllTaskbars` |
| **Reliability** | Handlers UI/AppDomain/Task; watchdog en kill forzoso; worker no muere por TypeInit | `App.xaml.cs`, `TaskbarWatchdog`, `rtb.log` |
| **Security** | Sin red; sin elevación requerida para uso normal; install Program Files opcional | Superficie local-only |
| **Maintainability** | `AGENTS.md` / `FIXING.md` / `ARCHITECTURE.md`; sin dead AH machine | Diff vs Gniang documentado |
| **Portability** | Solo Windows desktop (WPF); no UWP Store de terceros | README + TFM |

## Fiabilidad (anti-crash)

1. Excepciones UI → log + `Handled=true` + intento de restore.  
2. `UnhandledException` / `UnobservedTaskException` → crash log.  
3. `SessionEnding` / `OnExit` → `RestoreAllTaskbars`.  
4. Kill desde Task Manager → proceso `--watchdog` limpia RGN.  
5. Worker: catch amplio, sleep y continúa (no tumba el proceso).

## Pruebas

```powershell
.\scripts\smoke-test.ps1
```

Incluye: build Release, arranque, presencia de main+watchdog, heartbeat en log, cierre limpio, restore de `TaskbarAnimations` si quedó en 0.

Checklist manual (release): ver sección *Test plan* en las notas de `v1.1.0`.
