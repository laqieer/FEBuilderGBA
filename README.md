README
===

[![MSBuild](https://github.com/laqieer/FEBuilderGBA/actions/workflows/msbuild.yml/badge.svg)](https://github.com/laqieer/FEBuilderGBA/actions/workflows/msbuild.yml)
[![E2E: No ROM](https://github.com/laqieer/FEBuilderGBA/actions/workflows/e2e-norom.yml/badge.svg)](https://github.com/laqieer/FEBuilderGBA/actions/workflows/e2e-norom.yml)
[![E2E: FE6](https://github.com/laqieer/FEBuilderGBA/actions/workflows/e2e-fe6.yml/badge.svg)](https://github.com/laqieer/FEBuilderGBA/actions/workflows/e2e-fe6.yml)
[![E2E: FE7J](https://github.com/laqieer/FEBuilderGBA/actions/workflows/e2e-fe7j.yml/badge.svg)](https://github.com/laqieer/FEBuilderGBA/actions/workflows/e2e-fe7j.yml)
[![E2E: FE7U](https://github.com/laqieer/FEBuilderGBA/actions/workflows/e2e-fe7u.yml/badge.svg)](https://github.com/laqieer/FEBuilderGBA/actions/workflows/e2e-fe7u.yml)
[![E2E: FE8J](https://github.com/laqieer/FEBuilderGBA/actions/workflows/e2e-fe8j.yml/badge.svg)](https://github.com/laqieer/FEBuilderGBA/actions/workflows/e2e-fe8j.yml)
[![E2E: FE8U](https://github.com/laqieer/FEBuilderGBA/actions/workflows/e2e-fe8u.yml/badge.svg)](https://github.com/laqieer/FEBuilderGBA/actions/workflows/e2e-fe8u.yml)
[![GitHub Release](https://img.shields.io/github/v/release/laqieer/FEBuilderGBA)](https://github.com/laqieer/FEBuilderGBA/releases/latest)
[<img src="https://raw.githubusercontent.com/oprypin/nightly.link/master/logo.svg" height="16" style="height: 16px; vertical-align: sub">Nightly Build](https://nightly.link/laqieer/FEBuilderGBA/workflows/msbuild/master)
[![codecov](https://codecov.io/gh/laqieer/FEBuilderGBA/branch/master/graph/badge.svg)](https://codecov.io/gh/laqieer/FEBuilderGBA)
[![Cross-Platform](https://github.com/laqieer/FEBuilderGBA/actions/workflows/crossplatform.yml/badge.svg)](https://github.com/laqieer/FEBuilderGBA/actions/workflows/crossplatform.yml)

Mirrors for Chinese mainland users (面向中国大陆用户的镜像发布地址): [![Gitee Release](https://gitee-badge.vercel.app/svg/release/laqieer/FEBuilderGBA?style=flat)](https://gitee.com/laqieer/FEBuilderGBA/releases/latest) [<img src="[https://raw.githubusercontent.com/oprypin/nightly.link/master/logo.svg](https://gitee.com/laqieer/FEBuilderGBA/widgets/widget_5.svg)" height="16" style="height: 16px; vertical-align: sub">Gitee Go Build](https://gitee.com/laqieer/FEBuilderGBA/gitee_go/pipelines?tab=release)

## 🚀 Getting Started

### Project Structure

| Project | Target | Description |
|---------|--------|-------------|
| `FEBuilderGBA.Core` | net9.0 | Cross-platform core library (ROM, Undo, LZ77, text encoding, Huffman codec, patch detection, translation, cache, git, archive, event ASM, disassembler, export, mod, address, event script, EtcCache, symbol util, magic split, grow simulator, system text encoder, config persistence, GDB socket, event script util, EA lyn dump parser, lint core types/validation, UPS patch, image service abstraction, path utilities, logging facade, utilities, HeadlessEtcCache, HeadlessSystemTextEncoder, MapSettingCore, StructMetadata, StructExportCore, FELintScanner, DisassemblerCore, ImageUtilCore, ImageImportCore, DecreaseColorCore, PointerCalcCore, RebuildCore, SongExchangeCore, MapConvertCore) |
| `FEBuilderGBA` | net9.0-windows | WinForms GUI application |
| `FEBuilderGBA.CLI` | net9.0 | Cross-platform CLI tool (22 commands: `--version`, `--help`, `--makeups`, `--applyups`, `--lint`, `--disasm`, `--decreasecolor`, `--pointercalc`, `--rebuild`, `--songexchange`, `--convertmap1picture`, `--translate`, `--translate-roundtrip`, `--export-data`, `--import-data`, `--data-roundtrip`, `--lastrom`, `--force-detail`, `--translate_batch`, `--test`, `--testonly`; flags: `--force-version`, `--noScale`, `--noReserve1stColor`, `--ignoreTSA`, `--table`) |
| `FEBuilderGBA.SkiaSharp` | net9.0 | SkiaSharp implementation of IImageService (GBA 4bpp/8bpp tiles, palette conversion) |
| `FEBuilderGBA.Avalonia` | net9.0 | Cross-platform Avalonia UI (~47% feature completeness). ROM loading, 356 editors: unit/item/class editors with full read/write + undo; map/event/AI/text/audio/graphics/portrait/world map/support/arena/monster/summon/menu/credits viewers; image editors with PNG import; hex editor with hex dump view, jump, search; pointer search and free space scanning tools; editor search/filter in main window; dirty-check on close; named dropdowns (`ComboResourceHelper`); bit flag panels (`BitFlagPanel`); all 148 write-enabled editors wrapped with `UndoService`; all editors use `IsLoading` guards; cross-editor navigation (Jump to Class/Portrait) with pick-and-return support (`PickFromEditor<T>()`); ROM info display with free space analysis and data section pointers; proper list loading in class list, promo list, weapon lock, and unit short text editors with AddressListControl and name resolution. All 357 window views use `SizeToContent="WidthAndHeight"` for auto-sizing, and all views include `ScrollViewer` wrappers to prevent content clipping. See `docs/avalonia-gap-analysis.md` for details. |
| `FEBuilderGBA.Tests` | net9.0-windows | Unit and integration tests |
| `FEBuilderGBA.Core.Tests` | net9.0 | Cross-platform Core unit tests (runs on Linux/macOS/Windows) |
| `FEBuilderGBA.E2ETests` | net9.0-windows | End-to-end GUI/CLI tests |

### Cloning the Repository

This repository uses **git submodules** for patch management. Clone with:

```bash
git clone --recursive https://github.com/laqieer/FEBuilderGBA.git
```

Or if you already cloned without `--recursive`:

```bash
git submodule update --init --recursive
```

**Note:** The patch repository ([FEBuilderGBA-patch2](https://github.com/laqieer/FEBuilderGBA-patch2)) is maintained separately for independent versioning and faster updates.

### Cross-Platform Build (Linux / macOS / Windows)

The Core library, CLI, SkiaSharp backend, and Avalonia GUI scaffold all target `net9.0` and build on any platform:

```bash
# Build Core library
dotnet build FEBuilderGBA.Core/FEBuilderGBA.Core.csproj

# Build cross-platform CLI
dotnet build FEBuilderGBA.CLI/FEBuilderGBA.CLI.csproj

# Run CLI
dotnet run --project FEBuilderGBA.CLI -- --version
dotnet run --project FEBuilderGBA.CLI -- --makeups=out.ups --rom=modified.gba --fromrom=original.gba
dotnet run --project FEBuilderGBA.CLI -- --applyups=output.gba --rom=original.gba --patch=patch.ups
dotnet run --project FEBuilderGBA.CLI -- --lint --rom=rom.gba
dotnet run --project FEBuilderGBA.CLI -- --disasm=output.asm --rom=rom.gba
dotnet run --project FEBuilderGBA.CLI -- --decreasecolor --rom=rom.gba --in=input.png --out=output.png --paletteno=0
dotnet run --project FEBuilderGBA.CLI -- --pointercalc --rom=source.gba --target=target.gba --address=0x1234
dotnet run --project FEBuilderGBA.CLI -- --rebuild --rom=modified.gba --fromrom=vanilla.gba
dotnet run --project FEBuilderGBA.CLI -- --songexchange --rom=dest.gba --fromrom=source.gba --fromsong=1 --tosong=2
dotnet run --project FEBuilderGBA.CLI -- --convertmap1picture --rom=rom.gba --in=map.png
dotnet run --project FEBuilderGBA.CLI -- --translate --rom=rom.gba --out=texts.tsv
dotnet run --project FEBuilderGBA.CLI -- --translate --rom=rom.gba --in=texts.tsv
dotnet run --project FEBuilderGBA.CLI -- --translate-roundtrip --rom=rom.gba
dotnet run --project FEBuilderGBA.CLI -- --translate-roundtrip --rom=rom.gba --out=diff
dotnet run --project FEBuilderGBA.CLI -- --export-data --rom=rom.gba --table=units --out=units.tsv  # 40 tables: units, classes, items, portraits, sound_room, sound_boss_bgm, support_units, support_talks, support_attributes, event_haiku, event_battle_talk, event_force_sortie, worldmap_points, worldmap_paths, worldmap_bgm, map_settings, link_arena_deny, cc_branch, menu_definitions, item_weapon_triangle, map_exit_points, ai_map_settings, ai_perform_items, ai_perform_staff, ai_steal_items, ai_targets, generic_enemy_portraits, status_options, ed_retreat, ed_epithet, ed_epilogue_a, ed_epilogue_b, ed_epilogue_c, op_class_demo, op_class_font, op_prologue, class_alpha_names, summon_units, summons_demon_king, monster_probability
dotnet run --project FEBuilderGBA.CLI -- --export-data --rom=rom.gba --table=all --out=data
dotnet run --project FEBuilderGBA.CLI -- --import-data --rom=rom.gba --table=units --in=units.tsv
dotnet run --project FEBuilderGBA.CLI -- --data-roundtrip --rom=rom.gba --table=all
dotnet run --project FEBuilderGBA.CLI -- --lastrom
dotnet run --project FEBuilderGBA.CLI -- --force-detail
dotnet run --project FEBuilderGBA.CLI -- --translate_batch --rom=rom.gba --out=texts.tsv
dotnet run --project FEBuilderGBA.CLI -- --test --rom=rom.gba
dotnet run --project FEBuilderGBA.CLI -- --testonly --rom=rom.gba

# Build SkiaSharp image backend
dotnet build FEBuilderGBA.SkiaSharp/FEBuilderGBA.SkiaSharp.csproj

# Build Avalonia GUI
dotnet build FEBuilderGBA.Avalonia/FEBuilderGBA.Avalonia.csproj

# Run Avalonia GUI with a ROM
dotnet run --project FEBuilderGBA.Avalonia -- --rom path/to/rom.gba

# Run Avalonia smoke test (loads ROM, opens editors, selects items, verifies no crash)
dotnet run --project FEBuilderGBA.Avalonia -- --rom path/to/rom.gba --smoke-test

# Run Avalonia data verification (loads ROM, opens editors, cross-checks ViewModel data vs raw ROM + NumericUpDown UI display + text encoding)
dotnet run --project FEBuilderGBA.Avalonia -- --rom path/to/rom.gba --data-verify

# Capture Avalonia screenshots of all editors (saves PNGs to --screenshot-dir)
dotnet run --project FEBuilderGBA.Avalonia -- --rom path/to/rom.gba --screenshot-all --screenshot-dir=./screenshots

# Capture WinForms screenshots of all editors (saves PNGs for side-by-side comparison with Avalonia)
FEBuilderGBA.exe --rom path/to/rom.gba --screenshot-all --screenshot-dir=./screenshots

# Export decoded graphics editor images (for cross-platform pixel comparison)
# Exports 16 editors: PortraitViewer, BattleBGViewer, BattleTerrainViewer, BigCGViewer,
# ChapterTitleViewer, ChapterTitleFE7Viewer, ItemIconViewer, SystemIconViewer,
# OPClassFontViewer, OPPrologueViewer, ImagePortraitFE6, ImageBG, ImageCG,
# ImageCGFE7U, ImageTSAAnime, ImageBattleBG
dotnet run --project FEBuilderGBA.Avalonia -- --rom path/to/rom.gba --export-editor-images --screenshot-dir=./editor_images
FEBuilderGBA.exe --rom path/to/rom.gba --export-editor-images --screenshot-dir=./editor_images

# Validate image import roundtrip (export→import→export→compare for all graphics editors)
# Validated on all 5 ROM variants: FE6, FE7J, FE7U, FE8J, FE8U
# Note: Image import auto-expands ROM (up to 32MB max) when no free space is found,
# appending data to the end of the ROM rather than overwriting existing data.
# Shared palette detection: If a palette pointer is referenced by multiple entries,
# the import remaps pixel indices to the existing palette instead of overwriting it,
# preserving visual consistency for all entries sharing that palette.
dotnet run --project FEBuilderGBA.Avalonia -- --rom path/to/rom.gba --validate-import

# Validate palette roundtrip (export palette→import palette→re-export→binary compare)
# Tests all pointer-based palette editors (BattleBG, ImageCG, ImageBG, TSAAnime,
# OPPrologue, BigCG, BattleTerrain, Portrait).
# Also validates roundtrip through each supported palette format:
#   - JASC-PAL (.pal) — Aseprite, GIMP, Paint Shop Pro (text: "JASC-PAL\n0100\nN\nR G B\n...")
#   - Adobe ACT (.act) — Photoshop (binary: 256×3B RGB, optional 4B footer)
#   - GIMP GPL (.gpl) — GIMP (text: "GIMP Palette\nName:...\nR G B\tname\n")
#   - Hex Text (.txt) — Universal (one RRGGBB per line)
#   - GBA Raw (.gbapal) — Raw BGR555 LE, 2 bytes/color (backward compat)
# Export: format auto-selected from file extension (.pal → JASC-PAL by default)
# Import: format auto-detected from file content/header, then extension, then GBA raw fallback
dotnet run --project FEBuilderGBA.Avalonia -- --rom path/to/rom.gba --validate-palette

# Cross-platform publish (self-contained)
./scripts/publish-all.sh linux-x64 osx-arm64 win-x64

# Run cross-platform tests
dotnet test FEBuilderGBA.Core.Tests/FEBuilderGBA.Core.Tests.csproj
```

### Architecture Diagram

```
FEBuilderGBA.sln
├── FEBuilderGBA.Core/           net9.0    (cross-platform core)
│   ├── IAppServices.cs                     Platform abstraction
│   ├── IImageService.cs                    Image service abstraction
│   ├── Rom.cs / ROMFE*.cs                  ROM manipulation
│   ├── UPSUtil.cs                          UPS patch creation
│   ├── FELintCore.cs                       Lint validation
│   ├── PathUtil.cs                         Cross-platform paths
│   ├── PointerCalcCore.cs                 Pointer search engine
│   ├── RebuildCore.cs                     ROM defragmentation
│   ├── SongExchangeCore.cs                Song exchange between ROMs
│   ├── MapConvertCore.cs                  Map tile conversion
│   ├── NameResolver.cs                    Entity name resolution with caching
│   └── WriteValidator.cs                  ROM write validation utilities
├── FEBuilderGBA.CLI/            net9.0    (cross-platform CLI — 19 commands)
├── FEBuilderGBA.SkiaSharp/      net9.0    (image backend)
├── FEBuilderGBA.Avalonia/       net9.0    (cross-platform GUI — 323 editors, with ambient undo, dirty tracking, data export/import)
├── FEBuilderGBA/                net9.0-windows (WinForms GUI)
├── FEBuilderGBA.Tests/          net9.0-windows (unit tests)
├── FEBuilderGBA.Core.Tests/     net9.0    (cross-platform tests)
└── FEBuilderGBA.E2ETests/       net9.0-windows (E2E tests)
```

## Testing & Coverage

- ✅ **888 unit/integration tests** passing (775 WinForms + 113 Core cross-platform)
- ✅ **30 E2E tests** passing without ROMs (CLI + GUI automation + output log capture); **140 E2E tests** passing with all 5 ROMs (including 323-editor Avalonia smoke test, screenshot capture for both GUIs, + CLI output log capture for both CLI and WinForms executables)
- 📊 [View Full Coverage Report on Codecov](https://codecov.io/gh/laqieer/FEBuilderGBA)
- 🔍 Latest test results and coverage reports available as [GitHub Actions artifacts](https://github.com/laqieer/FEBuilderGBA/actions)
- 🧪 **Test Coverage:**
  - Unit tests for core utilities (RegexCache, LZ77, U, TextEscape, CoreState, Elf, SystemTextEncoderTBLEncode, MultiByteJPUtil, MyTranslateResource, EtcCacheResource, GitUtil, GitInstaller, AddrResult, ArchSevenZip, NewEventASM, ExportFunction, UpdateInfo, TranslateManager, DisassemblerTrumb, AsmMapSt, GbaBiosCall, R, Log, Mod, PatchDetection, FETextEncode, FETextDecode, TranslateCore, DecreaseColorCore sub-flags)
  - UpdateInfo version tracking and comparison
  - Core package download logic
  - Integration tests for update system
  - E2E CLI tests (`--version` flag, exit codes, output content, `--help` coverage)
  - CLI arg parsing tests (all 19 commands with complete argument sets)
  - E2E GUI tests (startup window detection, child controls, graceful shutdown)
  - ROM-based E2E CLI tests (`--lint`, `--makeups` × 5 ROMs, `--rebuild` × 2 representative ROMs — skipped without ROMs)
  - ROM-based E2E GUI tests (main form loads, title, child controls × 5 ROMs — skipped without ROMs)
  - Form smoke tests (all toolbar buttons × 5 ROMs — skipped without ROMs)
  - Avalonia editor smoke tests: Unit/Item editor selection (× 5 ROMs — skipped without ROMs)
  - Avalonia all-editors smoke test: all 323 GUI editors open/close (× 5 ROMs — skipped without ROMs)
  - Avalonia data verification: `--data-verify` mode cross-checks ViewModel fields against raw ROM bytes, verifies NumericUpDown UI controls display values, and validates text encoding (Shift-JIS for JP ROMs, ISO-8859-1 for US ROMs) for 50+ editors (× 5 ROMs — skipped without ROMs)
  - **Field completeness tests**: `AvaloniaFieldCompletenessTests` compares WinForms Designer.cs ROM data field controls against Avalonia ViewModel ROM access patterns across all 170 mapped forms (1562 WinForms fields, 0 gaps). Tests are **strict** — they fail on any gap, type/offset mismatch, or unmapped ROM-field form. Includes cross-checks: `AllFormFields_TypeAndOffsetMatch` verifies ROM read types match WinForms field types, `AllViewModels_ReportMethodsAreConsistent` verifies GetDataReport/GetRawRomReport key consistency, `MappedVMs_RawRomReport_CoversRomReads` enforces ≥60% raw ROM report coverage for all mapped VMs, `NoOrphanVMs_ImplementIDataVerifiable` prevents non-data-editor VMs from implementing IDataVerifiable, and `AllDesignerFilesWithRomFields_HaveAvaloniaMapping` auto-discovers ALL Designer.cs files with ROM fields to prevent new forms from being invisible to tests. Orphan cleanup removed IDataVerifiable from 49 non-editor VMs (dialogs, tools, infrastructure). Reports in `docs/field-completeness-report.txt`

## E2E Automation Tests

The project includes a dedicated end-to-end test suite (`FEBuilderGBA.E2ETests`) that covers both CLI and GUI behavior by launching the real application executable.

### Test Categories

| Test File | ROMs required | What it tests |
|-----------|--------------|--------------|
| `Tests/CliTests.cs` | No | CLI flag `--version`: exit code 0, output contains "FEBuilderGBA" and version info |
| `Tests/CliArgsE2ETests.cs` | No | All 18 CLI primary commands via `FEBuilderGBA.CLI`: `--help/-h`, `--version`, `--makeups`, `--applyups`, `--lint`, `--disasm`, `--decreasecolor`, `--pointercalc`, `--rebuild`, `--songexchange`, `--convertmap1picture`, `--translate`, `--translate-roundtrip`, `--lastrom`, `--force-detail`, `--translate_batch`, `--test/--testonly` — 38 tests ([docs/cli-args.md](docs/cli-args.md)) |
| `Tests/GuiStartupTests.cs` | No | GUI startup: window appears within 30 s, has non-empty title, has child controls, responds to WM_CLOSE |
| `Tests/DiagnosticTests.cs` | No | Diagnostic: logs all window handles, titles (hex-encoded), and class names — always passes |
| `Tests/RomCliTests.cs` | Yes (×5/×2) | `--lint`, `--makeups` × 5 ROMs; `--rebuild` × 2 representative ROMs (FE8U, FE6) — 12 tests, skipped without ROMs |
| `Tests/RomGuiTests.cs` | Yes (×5) | Main form loads per ROM: window appears, non-empty title, ≥10 child controls — 15 tests, skipped without ROMs |
| `Tests/FormSmokeTests.cs` | Yes (×5) | All toolbar buttons clicked per ROM; verifies ≥1 opens a form — 5 tests, skipped without ROMs |
| `Tests/AvaloniaEditorSmokeTests.cs` | Yes (×5) | Avalonia: ROM load + Unit/Item editor selection per ROM — 10 tests, skipped without ROMs |
| `Tests/AvaloniaAllEditorsSmokeTests.cs` | Yes (×5) | Avalonia: all 185 GUI editors opened/closed per ROM via `--smoke-test-all` — 10 tests, skipped without ROMs ([docs/avalonia-gui-forms.md](docs/avalonia-gui-forms.md), [docs/avalonia-forms.md](docs/avalonia-forms.md)) |
| `Tests/CliOutputLogNoRomTests.cs` | No | New CLI output log capture: `--help`, `-h`, `--version`, `--force-detail`, `--test`, `--testonly`, no args, `--bogus-command` — 8 tests |
| `Tests/CliOutputLogRomPart1Tests.cs` | Yes (×5/×2) | New CLI ROM output logs: `--lint` ×5, `--disasm` ×5, `--translate` ×5, `--rebuild` ×2 — 17 tests, skipped without ROMs |
| `Tests/CliOutputLogRomPart2Tests.cs` | Yes (×5/×2) | New CLI ROM output logs: `--makeups` ×5, `--applyups` ×2, `--pointercalc` ×2, `--songexchange` ×2 — 11 tests, skipped without ROMs |
| `Tests/CliOutputLogImageTests.cs` | No | New CLI image output logs: `--decreasecolor` (5 flag variants), `--convertmap1picture` — 6 tests |
| `Tests/WinFormsCliOutputLogNoRomTests.cs` | No | WinForms CLI output log capture: `--version`, no args, `--bogus-command` — 3 tests |
| `Tests/WinFormsCliOutputLogRomTests.cs` | Yes (×5/×2) | WinForms CLI ROM output logs: `--lint` ×5, `--rebuild` ×2, `--makeups` ×5, `--disasm` ×2, `--translate` ×2, `--pointercalc` ×2, `--songexchange` ×2 — 20 tests, skipped without ROMs |
| `Tests/AvaloniaScreenshotTests.cs` | Yes (×2) | Avalonia: captures PNG screenshots of all 323 editors via `--screenshot-all` — 4 tests, skipped without ROMs |
| `Tests/WinFormsScreenshotAllTests.cs` | Yes (×2) | WinForms: screenshots of main form + all toolbar-openable editor forms — 4 tests, skipped without ROMs |
| `Tests/WinFormsScreenshotAllCliTests.cs` | Yes (×2) | WinForms: captures screenshots of all editors via `--screenshot-all` CLI flag — 4 tests, skipped without ROMs |
| `Tests/EditorImageComparisonTests.cs` | Yes (×1) | Cross-platform image export + pixel-perfect comparison for 16 editors: `--export-editor-images` on both WinForms and Avalonia — 3 tests, strict assertions, skipped without ROMs |

**Without ROMs:** 30 passed, 112 skipped. **With all 5 ROMs:** 142 passed, 0 skipped.

### Running E2E Tests Locally

**Prerequisites:**  Build the main app first.

```bash
# Build the main application (Release, x86)
msbuild FEBuilderGBA.sln /p:Configuration=Release /p:Platform=x86 /t:build /restore

# Run without ROMs — 13 passed, 32 skipped (fast, ~20 s)
ROMS_DIR="" dotnet test FEBuilderGBA.E2ETests/FEBuilderGBA.E2ETests.csproj -c Release --no-build

# Run with ROMs — all 45 tests execute
ROMS_DIR=/path/to/roms dotnet test FEBuilderGBA.E2ETests/FEBuilderGBA.E2ETests.csproj -c Release --no-build
```

ROM files expected in `ROMS_DIR`: `FE6.gba`, `FE7J.gba`, `FE7U.gba`, `FE8J.gba`, `FE8U.gba`.

If `ROMS_DIR` is **not set at all**, `RomLocator` falls back to a `roms/` directory beside `FEBuilderGBA.sln` (useful during local development).  Set `ROMS_DIR=""` to explicitly suppress that fallback and force all ROM tests to skip.

Or point to an already-built binary:

```bash
export FEBUILDERGBA_EXE=/path/to/FEBuilderGBA.exe
ROMS_DIR="" dotnet test FEBuilderGBA.E2ETests/FEBuilderGBA.E2ETests.csproj -c Release --no-build
```

### CI/CD Integration

E2E tests are split into 6 parallel GitHub Actions workflows (`.github/workflows/e2e-*.yml`) — one no-ROM workflow and one per ROM variant (FE6, FE7J, FE7U, FE8J, FE8U). All share a reusable workflow (`e2e-run.yml`) and run in parallel, reducing wall-clock time from ~30 min to ~12 min. Each per-ROM workflow downloads `roms.zip` but keeps only its target ROM, so tests for other ROMs auto-skip.

ROM-based tests are gated on the `ROMS_URL` repository secret.  When the secret is present the workflow attempts to download `roms.zip`, validate it, extract it, and set `ROMS_DIR` for the test run.  When the secret is absent (forks, external PRs) the Download ROMs step is skipped entirely and all 35 ROM tests skip cleanly.

**ROM download — tiered failure policy:**
| Situation | Behaviour |
|-----------|-----------|
| `ROMS_URL` secret absent | Step skipped; ROM tests skip via `Assert.Skip()` |
| Network/HTTP error (unreachable URL) | Hard fail → pipeline blocked |
| Downloaded file not a valid zip (magic bytes ≠ `PK`) | Warning + exit 0; ROM tests skip |
| Zip structurally corrupt (`ZipFile::OpenRead` fails) | Warning + exit 0; ROM tests skip |
| Zip valid, all 5 ROMs extracted | All 45 tests run |

The step lists every zip entry with its uncompressed size before extraction, so the log shows exactly what is inside `roms.zip`.

**Artifacts produced:**
- `e2e-test-report` — TRX test report (viewable via the **E2E Test Results** check-run posted by `dorny/test-reporter`)
- `e2e-screenshots` — PNG screenshots of all GUI forms captured during E2E tests (Avalonia `Avalonia_*.png` + WinForms `WinForms_*.png`)
- `cli-output-logs` — `.log` files capturing stdout/stderr/exit code for every CLI command (both New CLI and WinForms CLI), useful for regression tracking

**Implementation notes:**
- Tests run sequentially (`[assembly: CollectionBehavior(DisableTestParallelization = true)]`) — each GUI test launches an exclusive app process; concurrent launches cause window-detection races
- Window detection polls **all process windows** via `EnumWindows` rather than relying on `Process.MainWindowHandle`, which can point to a transient splash/startup dialog before the main editor form appears
- Win32 `GetWindowText` P/Invoke uses `CharSet.Unicode` to correctly handle CJK characters; title-based detection is avoided for startup state (the app shows a Chinese "初始设置向导" Init Wizard on first run)
- CLI argument values must use `--key=value` (equals) syntax — `Program.ArgsDic` is built by `U.OptionMap` which only recognises the `=` separator (space-separated values are only picked up via a `File.Exists` fallback, which does not apply to output paths that don't yet exist)
- `AppRunner.Run()` calls `WaitForExit()` (no-param) after `WaitForExit(timeout)` to flush async `OutputDataReceived` events before reading captured stdout
- `RomLocator` treats any explicit `ROMS_DIR` value (even empty string) as an override — only when the variable is **absent** from the environment does the walk-up fallback activate

## 🔄 Update System

FEBuilderGBA uses a two-track update model that keeps the application and patch data independent:

### How It Works

| Component | What it contains | How it updates |
|-----------|-----------------|----------------|
| **Core** | FEBuilderGBA.exe, DLLs, config data | Download `FEBuilderGBA_YYYYMMDD.HH.zip` from GitHub Releases or nightly.link |
| **Patch2** | ~44,000 patch files in `config/patch2/` | `git fetch` + `git reset --hard` via the built-in Git updater |

When you check for updates the app compares the remote version against the local assembly build date and shows only the relevant update button(s).

### Updating Patch2 via Git

Patch2 is a [git submodule](https://github.com/laqieer/FEBuilderGBA-patch2) updated independently of core releases.

- **In-app:** Tools → Check for Updates → "Gitでパッチデータを更新します"
- **Manual:** `cd config/patch2 && git pull`
- **First run:** The app detects missing patch2 directories and offers to clone them automatically. If Git is not installed, empty directories are created so the app still starts.

The app automatically selects the patch2 git source based on your **Options → Release Source** setting — the same setting that controls where the core update is downloaded from:

| Release Source setting | Patch2 git remote used |
|------------------------|------------------------|
| Auto (Chinese language detected) | `gitee.com/laqieer/FEBuilderGBA-patch2` |
| Gitee | `gitee.com/laqieer/FEBuilderGBA-patch2` |
| GitHub / Nightly | `github.com/laqieer/FEBuilderGBA-patch2` |

### Benefits

- ✅ **Incremental patch updates** — only changed patch files are transferred via git
- ✅ **Faster patch updates** — no ZIP download or extraction required
- ✅ **Offline-friendly** — patch2 can be updated separately from the core app
- ✅ **Git history** — full audit trail of every patch data change

### Version Information

- **Core version:** Help → About
- **Patch2 version:** `git -C config/patch2 log -1 --format="%h %s"`

[This fork](https://github.com/laqieer/FEBuilderGBA/) is an integration of several forks of FEBuilderGBA and continues development based on it.

README for Korean character table
===

It is from an [unofficial build](https://github.com/delvier/FEBuilderGBA) of FEBuilderGBA that supports Korean character table.

The character table used is **Johab**, only for the Hangul Syllables part. If you want to use another character table like Wansung or Windows-949, you may replace __FE\[678\].tbl__ in __./config/translate/ko_tbl__.

Since this fork is incomplete, there might be some issues that raw code points appear can be occurred, e.g. '@61A0' rather than '마' (0xA061) appears. This is likely because the upper bytes from 0xA0 to 0xDF are used for single-byte representation in Shift JIS and Windows-932.

You should change "Text Encoding in ROM" in Options manually every time the ROM is loaded.

Original README
===

FE_Builder_GBA
===
This is a ROM hacking suite for the Trilogy of Fire Emblem games for the Game Boy Advance.
The editor supports
 * FE6 (The Binding Blade)
 * FE7J/FE7U (The Blazing Blade)
 * FE8J/FE8U (The Sacred Stones)
Essentially, both Japanese and North American releases of all games (with the exception of FE6 being Japan-only) are supported.

Starting from the main screen, FEBuilder supports a wide range of functions from image displaying, importing and export of most data, map remodeling, table editing, community patch management, music insertion, and much more. 

This suite was made at first to help make my Kaitou patch easier to create!

The origin of the name is from 某LAND.  
However, the development language is C#. (We're in this together...)  

Of course, it's open source.
The license of the source code is GPL3.  
Please use it freely with no limitations.

Much of this project's functions are thanks to the data collected by various communities and people.
We would like to thank our hacking predecessors who have publicly shared any analyzed data. 

Details (There is a commentary at the bottom of the page, and the wiki provides other instructions)  
https://dw.ngmansion.xyz/doku.php?id=en:guide:febuildergba:index

Some poorly designed anti-virus software may misidentify FEBuilderGBA as a virus.
This is because FEBuilderGBA uses the WindowsDebugAPI to communicate with the emulator.
Please configure your anti-virus to exclude the FEBuilderGBA directory.
FEBuilderGBA is NOT virus. 
The source code is all available on github, so you can build it yourself if you are worried.


This software has no association with the official products.  
We do not need any donations as we are making this software non-commercial. 

If you really want to donate to someone, donate to the charitable organization supporting the freedom of speech on the Internet, **Freedom of Expression**, including the **EFF Electronic Frontier Foundation**. 

Of course, you are free to write articles about FEBuilderGBA.  
In some cases, you may earn some pocket money through affiliates. :)  
However, please do it at your own risk. :(  

If you have something you do not understand through hacking or the editor, please read "Manual" in "Help".  
If you find a bug that you can not solve by any means, please create report.7z from 'File' -> 'Menu' -> 'Create Report Issue' and consult with the community.
https://discordapp.com/invite/Yzztqqa
Do NOT send your ROM (.gba) directly.

SourceCode:
https://github.com/FEBuilderGBA/FEBuilderGBA

Installer:
https://github.com/FEBuilderGBA/FEBuilderGBA_Installer/releases/download/ver_20200130.17.1/FEBuilderGBA_Downloader.exe


FE_Builder_GBA
===
FE GBA 3部作のROMエディターです。  
FE8J FE7J FE6 FE8U FE7U に対応しています。  

Project_FE_GBA の画面を参考に、  
新規に判明した部分を追加しました。  
画像表示やインポートエクスポート、マップ改造まで幅広い機能をサポートします。  

怪盗パッチを作っているときに思った、こんな機能が欲しい!!という機能をすべて入れ込みました。  

名前の由来は、 某LANDのアレからです。  
ただし、開発言語はC# です。 (中の人達は一緒だしね・・・)  
C#でありますが、特にパフォーマンスに注意しているので、サクサク動くかと思います。  

当然、オープンソース。ソースコードのライセンスは GPL3 です。  
ご自由にご利用ください。  

これを作るのに、いろいろいなデータ、コミニティを参考にしました。  
解析したデータを公開してくれた先人にお礼を申し上げます。  


詳細 (ページ下部に解説集があるよ)  
https://dw.ngmansion.xyz/doku.php?id=guide:febuildergba:index

一部の出来の悪いアンチウイルスソフトが、FEBuilderGBAをウイルスと誤認することがあるようです。
これは、FEBuilderGBAがエミュレータと通信するためにWindowsDebugAPIを利用しているからだと思います。
もしそうなったら、アンチウイルスの設定で、FEBuilderGBAディレクトリを除外してください。
FEBuilderGBAはウイルスではありません。
ソースコードはすべてgithubで公開しているので、心配な場合は自分でビルドしてください。


このソフトウェアは、公式とは一切関係ありません。  
私達は非営利でこのソフトウェアを作っているので、寄付を必要としません。  
どうしても寄付したい方は、EFF 電子フロンティア財団を始めとする、インターネットでの言論の自由、表現の自由を支援している慈善団体にでも寄付してください。  

もちろん、あなたがFEBuilderGBAに関する記事を書くのは自由です。  
場合によっては、アフェリエイトでお小遣いを稼ぐこともできるでしょう。 :)  
ただし、あなたの責任において実施してください。 :(  

もし、hackromでわからないことがあれば、「ヘルプ」の「マニュアル」を読んでください。  
どうしても解決しないバグが発生した場合は、「メニュー」の「ファイル」->「問題報告ツール」から、report.7zを作成して、コミニティに相談してください。
https://discordapp.com/invite/Yzztqqa
(ROMは送信しないでください。)  

SourceCode:
https://github.com/FEBuilderGBA/FEBuilderGBA

Installer:
https://github.com/FEBuilderGBA/FEBuilderGBA_Installer/releases/download/ver_20200130.17.1/FEBuilderGBA_Downloader.exe


FE_Builder_GBA
===
它是FE GBA三部曲的ROM编辑器。  
它对应于 FE8J FE7J FE6 FE8U FE7U.  

参考Project_FE_GBA的屏幕，  
我添加了一个新发现的部分。  
我们支持图像显示，导入导出，地图重构等功能。  

当我制作一个kaitou补丁时，我想要这样的功能  

这个名字的起源是来自 某LAND。  
但是，开发语言是C＃。 （里面的人在一起...）  
它是C＃，但我担心性能，所以我认为它会工作很好。  

当然，开源。源代码的许可证是GPL3。  
请自由使用。  

我参考了各种数据和社区来做到这一点。  
我要感谢发布分析数据的前辈。  


详细信息（页面底部有评论）  
https://dw.ngmansion.xyz/doku.php?id=zh:guide:febuildergba:index

Some poorly designed anti-virus software may misidentify FEBuilderGBA as a virus.
This is because FEBuilderGBA uses the WindowsDebugAPI to communicate with the emulator.
Please configure your anti-virus to exclude the FEBuilderGBA directory.
FEBuilderGBA is NOT virus. 
The source code is all available on github, so you can build it yourself if you are worried.


这个软件与官方无关。  
我们不需要捐赠，因为我们正在制作该软件的非营利。  
如果你真的想捐赠，  
捐赠给支持言论自由的慈善组织，包括EFF电子前沿基金会在内的言论自由  

当然，您可以自由撰写关于FEBuilderGBA的文章。  
在某些情况下，您可以通过会员赚取零用钱。 :)  
但是，请自行承担风险。 :(  

如果你有一些你从hackrom不能理解的东西，请阅读“帮助”中的“手册”。  
如果您发现无法解决的错误，请在'菜单'的'文件' -> '问题报告工具'中创建report.7z，并咨询社区。
https://discordapp.com/invite/Yzztqqa
（请不要发送ROM。）  

SourceCode:
https://github.com/FEBuilderGBA/FEBuilderGBA

Installer:
https://github.com/FEBuilderGBA/FEBuilderGBA_Installer/releases/download/ver_20200130.17.1/FEBuilderGBA_Downloader.exe
