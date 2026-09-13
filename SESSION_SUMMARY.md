# TypeFlow — Session Summary

## Objective
1. **Bilingual UI** — English/Arabic with live switching, RTL, auto-detect, persisted setting (done).
2. **Prune bundled defaults** — remove web templates, report paragraphs, signatures, and clinical phrases from bundled shortcuts and the installed store (done).
3. **Create distributable(s)** — self-contained single-file EXE **and** an installer (done: Inno Setup EXE; MSI planned but halted on request).

## Deliverables (in `TypeFlow\dist\`)
| File | Size | Notes |
|---|---|---|
| `TypeFlow-Setup-1.0.0.exe` | 44.5 MB | Inno Setup installer. Per-user, no UAC, Start Menu + optional desktop icon, uninstaller. |
| `TypeFlow\TypeFlow.exe` | 146 MB | Self-contained single-file publish (`.NET 5`, win-x64). Portable; run standalone. |

Verified: silent install → app launches with title `TypeFlow — Text Expander`, no `crash.log` → silent uninstall removes everything. Tests green **89/89**.

## Key implementation facts
- **net5.0-windows TFM** must stay (VS2019 support); only .NET 8 SDK installed → `<RollForward>LatestMajor</RollForward>` in App/Tests; 4× `NETSDK1138` warnings expected.
- Localization class renamed `Localization` → `L10n` (namespace `TypeFlow.App.Localization` collided with the class name, causing CS0234); `Application` ambiguity fixed by fully qualifying `System.Windows.Application.Current`.
- Settings: `DataDirectory = %APPDATA%\TypeFlow`; `settings.json` = `{"IsInputEnabled":…,"Language":"en|ar|"}` (`""` = auto-detect via `CurrentUICulture`). Written only on toggle/language change, not on vanilla startup.
- CSV loaders handle UTF-8 BOM, strict UTF-8, cp1252 fallback; `TypeBuffer` supports Arabic/Unicode.
- `PublishSingleFile` self-contained win-x64 works; `EnableCompressionInSingleFile` fails on net5.0 (NETSDK1167) → uncompressed output (~146 MB).

## Bugs fixed this session
- **Fresh-install defaults bug** (`MainWindow.xaml.cs`): the ctor set `ActiveToggle.IsChecked` before `InputToggle`, firing the shared `Toggle_Changed` before `InputToggle` existed, which silently set `engine.IsInputEnabled = false`. Fixed with a `_restoringToggles` guard. Fresh installs now start with both toggles **On**.

## Defaults pruning (user-confirmed tiers)
Remove: html templates, 8+ word report paragraphs, Dr./Prof. signatures, 3–7 word clinical phrases.
Keep: terse clinical abbreviations (mri, ct, us, abd, ln…) and 36 generic rows (incl. grammar fixes like `could of been`).
- Bundled CSV: 350 removed (clinical37=195, paragraph=112, signature=38, html=5); **1202 data rows** remain.
- Store (`%APPDATA%\TypeFlow\shortcuts.json`): 1556 → 1206.
- Backups: `assets\typeflow_shortcuts.full.csv`, `shortcuts.json.bak`, `shortcuts.removed.txt`.
- Tests updated (1553 → 1203 constants).

## Test status
`dotnet run --project tests\TypeFlow.Tests` → **Passed: 89, Failed: 0**.
Prune regression checks: macros removed (adeno/dexaoo/cont/aca/apc/ak/avts), generics kept (could of been/abbout/vis-a-vis).

## Installer build steps (for reproducibility)
1. `dotnet publish src\TypeFlow.App -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true -o dist\TypeFlow`
2. Inno Setup 6.7.3 installed to `%LOCALAPPDATA%\TypeFlowTools\InnoSetup` (portable, no admin).
3. Script: `installer\TypeFlow.iss` — **only used at build time** (feed to `ISCC.exe`), never at runtime.
4. `ISCC.exe installer\TypeFlow.iss` → `dist\TypeFlow-Setup-1.0.0.exe` (LZMA2 compressed).
5. Sanity: silent install → launch → uninstall.

## Pending / decisions
- **MSI (Windows Installer)**: user asked to build one in addition → paused on request. Would require WiX v5 (`dotnet tool install -g wix`) and admin elevation for per-machine install. Inno EXE remains the primary installer.
- Installer and EXE are **unsigned** → SmartScreen/AV may warn on first run.

## Notes
- Kill running `TypeFlow.exe` before rebuilds; stale `obj\...\MainWindow.g.cs` can hide missing `x:Name` errors.
- Inno Setup download note: `jrsoftware.org/download.php/is.exe` returns an HTML index, not a binary — fetch `https://github.com/jrsoftware/issrc/releases/download/is-6_7_3/innosetup-6.7.3.exe` directly.