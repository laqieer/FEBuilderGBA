# Cross-Platform Architecture

This document describes the cross-platform strategy for FEBuilderGBA.

## Design Decisions

### Why Avalonia UI?

FEBuilderGBA's WinForms GUI has 330 forms and 130k+ lines of GUI code. A full rewrite is not practical. Instead, we chose **Avalonia UI** for the cross-platform GUI because:

- **XAML-based** — familiar to .NET developers, supports data binding
- **.NET 10.0 native** — no bridge layers or compatibility shims
- **Linux/macOS/Windows** — single codebase for all desktop platforms
- **Active community** — well-maintained, regular releases
- **Mature controls** — DataGrid, TreeView, TabControl cover FEBuilderGBA's needs

The Avalonia project (`FEBuilderGBA.Avalonia/`) now ships **367 `.axaml` files (355 `*View.axaml` editor views)** backed by **361 ViewModels**, of which **176 are write-enabled editors** (they implement `IDataVerifiable` and route ROM writes through the shared `Services/UndoService`). Migration from WinForms is far past the scaffold stage; remaining gaps are tracked incrementally.

### Why SkiaSharp?

FEBuilderGBA uses `System.Drawing` / GDI+ extensively (~15,600 lines across 12 `ImageUtil*.cs` files). GDI+ is Windows-only. We chose **SkiaSharp** because:

- **Cross-platform** — native rendering on Linux, macOS, Windows
- **2D graphics focus** — well-suited for pixel-level GBA tile/sprite manipulation
- **Performance** — hardware-accelerated where available
- **PNG/BMP support** — built-in codec for image I/O

The `IImageService` abstraction in Core lets platform backends swap implementations:
- `FEBuilderGBA.SkiaSharp/` — cross-platform SkiaSharp backend
- WinForms code continues using `System.Drawing` directly (no migration needed)

> **Version pin:** `FEBuilderGBA.SkiaSharp` references **SkiaSharp 2.88.9**, not the
> newer 3.x line. A process loads exactly one native `libSkiaSharp`, and Avalonia
> 11.2.3 bundles the **2.88** native. The managed SkiaSharp 3.116 rejects that native
> ("88.1") and crashes on Linux/macOS, so the version is deliberately held at 2.88.9
> to match Avalonia's bundled native.

## Project Dependencies

```
FEBuilderGBA.Core (net10.0)
├── No platform dependencies
├── Defines: IAppServices, IImageService, IImage
└── Contains: ROM, Undo, LZ77, text encoding, config, etc.

FEBuilderGBA.CLI (net10.0)
├── References: FEBuilderGBA.Core
└── Cross-platform CLI: 71 commands (--version, --help, --playtest, --makeups,
    --lint, --rebuild, --disasm, --export-data, --export-asset, …;
    full list via `FEBuilderGBA.CLI --help`)

FEBuilderGBA.SkiaSharp (net10.0)
├── References: FEBuilderGBA.Core
├── NuGet: SkiaSharp 2.88.9 (pinned to match Avalonia 11.2.3's bundled
│         native libSkiaSharp; managed 3.x crashes on Linux/macOS)
└── Implements: IImageService, IImage

FEBuilderGBA.Avalonia (net10.0)
├── References: FEBuilderGBA.Core
├── NuGet: Avalonia 11.2.3
└── Cross-platform GUI (367 .axaml, 361 ViewModels, 176 write-enabled editors)

FEBuilderGBA (net10.0-windows)
├── References: FEBuilderGBA.Core
└── WinForms GUI (existing, Windows-only)

FEBuilderGBA.Core.Tests (net10.0)
├── References: FEBuilderGBA.Core
└── Cross-platform unit tests (5,549 test methods)

FEBuilderGBA.Avalonia.Tests (net10.0)
├── References: FEBuilderGBA.Avalonia, .Core
└── Cross-platform Avalonia GUI/ViewModel tests (4,572 test methods)

FEBuilderGBA.Tests (net10.0-windows)
├── References: FEBuilderGBA.Core, .CLI, .SkiaSharp
└── Unit/integration tests (1,314 test methods)

FEBuilderGBA.E2ETests (net10.0-windows)
├── Launches FEBuilderGBA.exe
└── End-to-end GUI/CLI tests (161 test methods)
```

> `[Theory]` test methods expand to multiple cases at runtime, so the actual
> executed-test totals are higher than these attribute counts.

## Platform Support Matrix

| Feature | Windows | Linux | macOS | Android | iOS | Web (wasm) |
|---------|---------|-------|-------|---------|-----|-----|
| Core library | Yes | Yes | Yes | Yes | Yes | Yes |
| CLI (71 commands) | Yes | Yes | Yes | — | — | — |
| SkiaSharp image backend | Yes | Yes | Yes | Yes | Yes | Yes |
| Avalonia GUI (full editor) | Yes | Yes | Yes | Preview | Preview | Preview |
| WinForms GUI (full editor) | Yes | No | No | No | No | No |
| Unit tests (Core.Tests) | Yes | Yes | Yes | — | — | — |
| Unit tests (Avalonia.Tests) | Yes | Yes | Yes | — | — | — |
| Unit tests (FEBuilderGBA.Tests) | Yes | No | No | — | — | — |
| E2E tests | Yes | No | No | — | — | — |
| Emulator integration (RAM.cs) | Yes | No | No | No | No | No |

> **Android / iOS / Web = Preview:** native heads (`FEBuilderGBA.Android` / `FEBuilderGBA.iOS` / `FEBuilderGBA.Browser`) build the shared Avalonia GUI into an APK / `.ipa` / wasm bundle; the runtime is maturing. See [docs/ANDROID.md](ANDROID.md) / [docs/IOS.md](IOS.md) / [docs/WEBASSEMBLY.md](WEBASSEMBLY.md). Try the web build: <https://laqieer.github.io/FEBuilderGBA/>.

## Migration Roadmap

### Phase 0: Foundation (Current)

- [x] Extract platform-independent Core library (57 files)
- [x] Define IAppServices abstraction
- [x] Define IImageService abstraction
- [x] Create cross-platform CLI with `--version`, `--help`, `--makeups`
- [x] Implement SkiaSharp image backend
- [x] Scaffold Avalonia GUI project
- [x] Create cross-platform test project
- [x] Add cross-platform CI/CD (Linux/macOS/Windows)
- [x] Extract FELintCore types to Core
- [x] Add PathUtil for cross-platform paths

### Phase 1: CLI Expansion (Complete)

- [x] Add `--lint` command to CLI (using FELintCore)
- [x] Add `--rebuild` command to CLI
- [x] Add `--disasm` command to CLI
- [x] ROM loading without WinForms dependencies

The CLI now exposes **71 commands** (full list via `FEBuilderGBA.CLI --help`).

### Phase 2: Image Migration (Complete)

- [x] Port ImageUtil.cs core operations to IImageService
- [x] Port LZ77 + tile decode/encode pipeline
- [x] Port palette conversion utilities
- [x] Enable image export/import in CLI

### Phase 3: Avalonia Forms (Complete)

- [x] Port MainForm layout and navigation
- [x] Port data editing forms (Unit, Class, Item)
- [x] Port map editor
- [x] Port image viewers
- [x] Port event script editor

The Avalonia GUI now ships **367 `.axaml` views** and **176 write-enabled editors**.

### Phase 4: Full Cross-Platform

- [ ] Complete Avalonia form migration
- [ ] Platform-specific tool integration (devkitARM, Event Assembler)
- [ ] Cross-platform emulator integration
- [ ] Release Linux/macOS builds

## Known Limitations

### SkiaSharp

- **No native indexed bitmap support** — SkiaSharp uses RGBA internally. Indexed (paletted) images store palette indices as metadata alongside the RGBA bitmap. This works for GBA tile operations but means palette manipulation requires explicit index tracking.
- **Platform-specific native libraries** — SkiaSharp tests require the correct native library for the build platform. On Windows with x86 builds, use `-p:Platform=x86` when running tests.

### Avalonia

- **No WinForms interop** — Avalonia forms cannot embed WinForms controls. Migration is form-by-form, but most editors are already ported (367 `.axaml` views, 176 write-enabled editors).
- **Remaining gaps** — A handful of features still trail the WinForms editor (e.g. some animation playback and OAM sprite-assembly paths); these are tracked incrementally.

### CLI

- **Coverage** — The CLI exposes **71 commands** (`--version`, `--playtest`, `--makeups`, `--lint`, `--rebuild`, `--build-buildfile`, `--buildfile-roundtrip`, `--disasm`, `--export-data`, `--export-asset`, and more — full list via `FEBuilderGBA.CLI --help`). Commands no longer require WinForms Form classes; the needed logic lives in `FEBuilderGBA.Core`.
- **No config/ auto-discovery** — The CLI expects `config/` in the executable directory. Cross-platform config path resolution is handled by `PathUtil.ConfigPath()`.

### Core Library

- **Internal U.cs** — The utility class `U.cs` is `internal` to Core. CLI and other projects access it through `InternalsVisibleTo` attributes.
- **WinForms shadow classes** — Some Core classes (R.cs, Log.cs) have WinForms counterparts that add UI functionality via CS0436 intentional shadowing.

## Building Cross-Platform

```bash
# Core library (any platform)
dotnet build FEBuilderGBA.Core/FEBuilderGBA.Core.csproj

# CLI (any platform)
dotnet build FEBuilderGBA.CLI/FEBuilderGBA.CLI.csproj
dotnet run --project FEBuilderGBA.CLI -- --version

# SkiaSharp backend (any platform)
dotnet build FEBuilderGBA.SkiaSharp/FEBuilderGBA.SkiaSharp.csproj

# Avalonia GUI (any platform)
dotnet build FEBuilderGBA.Avalonia/FEBuilderGBA.Avalonia.csproj

# Cross-platform tests (any platform)
dotnet test FEBuilderGBA.Core.Tests/FEBuilderGBA.Core.Tests.csproj
dotnet test FEBuilderGBA.Avalonia.Tests/FEBuilderGBA.Avalonia.Tests.csproj

# Full solution (Windows only — includes WinForms)
dotnet msbuild /m /p:Configuration=Release /p:Platform=x86 /t:build /restore FEBuilderGBA.sln
```

## Local Test ROMs (GUI / E2E reproduction)

Running the GUI/CLI against real game data needs Fire Emblem GBA ROMs, which are
copyrighted and therefore **never committed or shipped**. For local testing, place your
own legally-obtained ROMs in a **git-ignored `roms/`** folder at the repo root:
`FE6.gba`, `FE7J.gba`, `FE7U.gba`, `FE8J.gba`, `FE8U.gba`.

- They drive local GUI reproduction and the headless Avalonia screenshot recipe, e.g.
  `FEBuilderGBA.Avalonia --rom roms/FE8U.gba --screenshot-all --screenshot-dir shots`
  (optionally add `--screenshot-tab=<AutomationId>` to activate a specific tab before capture).
- `roms/` lives only in the **main working copy** — it is **not** copied into `git worktree`
  checkouts, so build/screenshot from the main repo (or pass an absolute ROM path) when
  reproducing GUI issues.
- CI / E2E does **not** use these local files; it fetches ROMs from the `ROMS_URL` repository
  secret instead.

## External Tools (Per-Platform)

| Tool | Windows | Linux | macOS |
|------|---------|-------|-------|
| devkitARM (ASM compilation) | `devkitPro/devkitARM/bin/` | `/opt/devkitpro/devkitARM/bin/` | `/opt/devkitpro/devkitARM/bin/` |
| Event Assembler | `Tools/Event Assembler/` | Wine or native port | Wine or native port |
| 7-Zip | `7-zip32.dll` (bundled) | SharpCompress fallback | SharpCompress fallback |
| Git | `PortableGit/` or system | System package | System or Homebrew |

## Running on Android

> **Status: experimental / unsupported / community-tested.** Nothing in this section is something this project builds, ships, or can support. It is documented for users who ask about it (see source [discussion #1062](https://github.com/laqieer/FEBuilderGBA/discussions/1062)).

### Option A — Emulation via Gamenative / Winlator (experimental, unsupported)

**Gamenative** (a Winlator fork) runs Windows x86/x64 binaries on Android via **Wine + Box86/Box64** (x86/x64 → ARM dynamic translation). This runs the **Windows *desktop* build** of FEBuilderGBA under emulation — a **user-side compatibility layer**, **not** a native Android app.

- **Which build to try:** as a best-effort suggestion, the **Avalonia desktop `win-x64`** self-contained build (`./scripts/publish-all.sh win-x64`) is more likely to behave under Wine than the classic WinForms build — modern cross-platform .NET generally fares better under Wine than WinForms. This is *not* a guarantee of compatibility or a support commitment.
- **Known caveats — set expectations accordingly:**
  - Desktop mouse/keyboard UX on a touchscreen (the UI is not touch-designed).
  - Wine + .NET-under-Wine reliability is **unverified** for this app.
  - No native Android file picker / scoped-storage (SAF) integration.
  - External-tool integrations are not expected to work (and are unsupported in this path): GBA emulator test-play, devkitARM assembler, and EA/ColorzCore event compilation.

### Option B — Native Android app

Running the Windows binary under emulation gives you the *desktop* program on an Android device; it does **not** give you a touch-native app. An actual native Android build of the Avalonia GUI is a substantial, separate port tracked as the exploration epic [#1070](https://github.com/laqieer/FEBuilderGBA/issues/1070) — there is no commitment to ship it yet.

The structural prerequisite **landed in [#1121](https://github.com/laqieer/FEBuilderGBA/issues/1121)**: `FEBuilderGBA.Avalonia` now **conditionally multi-targets** `net10.0;net10.0-android` — opt-in via the `EnableAndroidTarget` MSBuild property (default **OFF**, so the desktop `.sln` and all CI still see `net10.0` only). The Android head at [`FEBuilderGBA.Android/`](../FEBuilderGBA.Android/README.md) **builds a real APK** against the shared UI:

```bash
dotnet workload install android
dotnet build FEBuilderGBA.Android/FEBuilderGBA.Android.csproj -c Release -p:EnableAndroidTarget=true
```

The `-p:EnableAndroidTarget=true` flag is required: the `ProjectReference`'s `AdditionalProperties` activates the android TFM for the build phase, but NuGet restore ignores per-reference `AdditionalProperties`, so the global property is needed to make restore cross-target the shared project too. The APK is **not yet device-validated**: booting it shows no editor until the single-view navigation rework lands. Desktop-only pieces (`Program.cs`, `Avalonia.Desktop`, `app.manifest`, the `GapSweep` Roslyn dev-tooling) are excluded on the android TFM.

The full, evidence-backed feasibility assessment lives in **[docs/ANDROID.md](ANDROID.md)**: which layers are Android-capable (`Core` + `Avalonia`; WinForms excluded), the **SkiaSharp native-version pin** (`SkiaSharp.NativeAssets.Android` must match Avalonia's bundled `2.88.x`), **scoped-storage / SAF** ROM access (the desktop file dialogs collapse picks to a local path that Android `content://` URIs lack), **`config/` bundling** (ship as `AndroidAsset`, extract to `Context.FilesDir` on first run), the **multi-window → single-activity** navigation gap (the largest item), and the build prerequisites. The head is intentionally **not** in `FEBuilderGBA.sln` so it never affects the desktop CI build.

## Running on iOS

A native iOS/iPadOS build of the Avalonia GUI, added in [#1859](https://github.com/laqieer/FEBuilderGBA/issues/1859) — the iOS counterpart of the Android epic. It reuses the **same** platform-agnostic seams the Android port introduced (single-view lifetime, first-run `config/` extraction via `FEBuilderGBA.Core/AndroidConfigExtractorCore`, stream-based ROM I/O), so it is a close mirror. `FEBuilderGBA.Avalonia` conditionally multi-targets `net10.0;net10.0-ios` via the opt-in `EnableIosTarget` property (default **OFF**), and the head at [`FEBuilderGBA.iOS/`](../FEBuilderGBA.iOS/README.md) builds an iOS `.app` / **unsigned `.ipa`** on macOS:

```bash
dotnet workload install ios
dotnet build FEBuilderGBA.iOS/FEBuilderGBA.iOS.csproj -c Release -p:EnableIosTarget=true
```

`-p:EnableIosTarget=true` is required as a **global** property (the same NuGet-restore static-graph reason as Android's `EnableAndroidTarget`). `config/**` (excluding `patch2`) ships as `<BundleResource>` (structure preserved via `LogicalName`) and is extracted on first run into an app-private writable dir. iOS is a **PREVIEW**: it builds, but the on-device runtime (touch UX, file pickers, AOT/trim — mitigated with the Mono interpreter + `MtouchLink=SdkOnly`) is unvalidated. The CI-produced `.ipa` is **unsigned** (no Apple secret on this fork) — install it via re-signing with AltStore / Sideloadly / Apple Configurator. The head is intentionally **not** in `FEBuilderGBA.sln` (iOS needs macOS + Xcode and the `ios` workload). Full assessment: **[docs/IOS.md](IOS.md)**.

## Running in the browser (WebAssembly)

FEBuilderGBA runs in a **browser** via WebAssembly, added in [#1864](https://github.com/laqieer/FEBuilderGBA/issues/1864) — the 4th Avalonia head. It reuses the *same* platform-agnostic seams the mobile ports introduced (single-view lifetime, `App.BaseDirectoryOverride`, `AndroidConfigExtractorCore`). `FEBuilderGBA.Avalonia` conditionally multi-targets `net10.0;net10.0-browser` via the opt-in `EnableBrowserTarget` property (default **OFF**), and the head at [`FEBuilderGBA.Browser/`](../FEBuilderGBA.Browser/README.md) builds a `net10.0-browser` AppBundle:

```bash
dotnet workload install wasm-tools
dotnet publish FEBuilderGBA.Browser/FEBuilderGBA.Browser.csproj -c Release \
  -p:EnableBrowserTarget=true -p:WasmEnableThreads=false -p:PublishTrimmed=false -p:CompressionEnabled=false
```

`-p:EnableBrowserTarget=true` is required as a **global** property (the same NuGet-restore static-graph reason as android/iOS). The head links `SkiaSharp.NativeAssets.WebAssembly` 2.88.9 + `HarfBuzzSharp.NativeAssets.WebAssembly` 7.3.0.3 (both wasm natives emcc-relinked into `dotnet.wasm`) + `Avalonia.Fonts.Inter` (wasm has no system fonts). `config/**` (excl. `patch2`) is zipped into `wwwroot/config.zip`, fetched over HTTP and extracted into the browser's in-memory filesystem on first run via the pure `FEBuilderGBA.Core/ZipAssetSource`. Runs **single-threaded** (GitHub Pages sends no COOP/COEP → no `SharedArrayBuffer`) and **untrimmed** (reflection-heavy Core). It is a **PREVIEW**: the milestone is builds + deploys + loads/renders the shell. `.github/workflows/pages.yml` deploys it to **<https://laqieer.github.io/FEBuilderGBA/>**. The head is intentionally **not** in `FEBuilderGBA.sln` (the required desktop `build` check has no `wasm-tools`). Full assessment: **[docs/WEBASSEMBLY.md](WEBASSEMBLY.md)**.
