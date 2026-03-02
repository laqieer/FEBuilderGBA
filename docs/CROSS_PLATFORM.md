# Cross-Platform Architecture

This document describes the cross-platform strategy for FEBuilderGBA.

## Design Decisions

### Why Avalonia UI?

FEBuilderGBA's WinForms GUI has 311 forms and 130k+ lines of GUI code. A full rewrite is not practical. Instead, we chose **Avalonia UI** for the cross-platform GUI shell because:

- **XAML-based** — familiar to .NET developers, supports data binding
- **.NET 9.0 native** — no bridge layers or compatibility shims
- **Linux/macOS/Windows** — single codebase for all desktop platforms
- **Active community** — well-maintained, regular releases
- **Mature controls** — DataGrid, TreeView, TabControl cover FEBuilderGBA's needs

The Avalonia project (`FEBuilderGBA.Avalonia/`) is currently a scaffold. Full form migration will happen incrementally.

### Why SkiaSharp?

FEBuilderGBA uses `System.Drawing` / GDI+ extensively (~15,600 lines across 12 `ImageUtil*.cs` files). GDI+ is Windows-only. We chose **SkiaSharp** because:

- **Cross-platform** — native rendering on Linux, macOS, Windows
- **2D graphics focus** — well-suited for pixel-level GBA tile/sprite manipulation
- **Performance** — hardware-accelerated where available
- **PNG/BMP support** — built-in codec for image I/O

The `IImageService` abstraction in Core lets platform backends swap implementations:
- `FEBuilderGBA.SkiaSharp/` — cross-platform SkiaSharp backend
- WinForms code continues using `System.Drawing` directly (no migration needed)

## Project Dependencies

```
FEBuilderGBA.Core (net9.0)
├── No platform dependencies
├── Defines: IAppServices, IImageService, IImage
└── Contains: ROM, Undo, LZ77, text encoding, config, etc.

FEBuilderGBA.CLI (net9.0)
├── References: FEBuilderGBA.Core
└── Cross-platform CLI: --version, --help, --makeups

FEBuilderGBA.SkiaSharp (net9.0)
├── References: FEBuilderGBA.Core
├── NuGet: SkiaSharp 3.116.1
└── Implements: IImageService, IImage

FEBuilderGBA.Avalonia (net9.0)
├── References: FEBuilderGBA.Core
├── NuGet: Avalonia 11.2.3
└── Cross-platform GUI scaffold

FEBuilderGBA (net9.0-windows)
├── References: FEBuilderGBA.Core
└── WinForms GUI (existing, Windows-only)

FEBuilderGBA.Core.Tests (net9.0)
├── References: FEBuilderGBA.Core
└── Cross-platform unit tests (21 tests)

FEBuilderGBA.Tests (net9.0-windows)
├── References: FEBuilderGBA.Core, .CLI, .SkiaSharp
└── Unit/integration tests (715 tests)

FEBuilderGBA.E2ETests (net9.0-windows)
├── Launches FEBuilderGBA.exe
└── End-to-end GUI/CLI tests (13-45 tests)
```

## Platform Support Matrix

| Feature | Windows | Linux | macOS |
|---------|---------|-------|-------|
| Core library | Yes | Yes | Yes |
| CLI (`--version`, `--makeups`) | Yes | Yes | Yes |
| SkiaSharp image backend | Yes | Yes | Yes |
| Avalonia GUI (scaffold) | Yes | Yes | Yes |
| WinForms GUI (full editor) | Yes | No | No |
| Unit tests (Core.Tests) | Yes | Yes | Yes |
| Unit tests (FEBuilderGBA.Tests) | Yes | No | No |
| E2E tests | Yes | No | No |
| Emulator integration (RAM.cs) | Yes | No | No |

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

### Phase 1: CLI Expansion (Next)

- [ ] Add `--lint` command to CLI (using FELintCore)
- [ ] Add `--rebuild` command to CLI
- [ ] Add `--disasm` command to CLI
- [ ] ROM loading without WinForms dependencies

### Phase 2: Image Migration

- [ ] Port ImageUtil.cs core operations to IImageService
- [ ] Port LZ77 + tile decode/encode pipeline
- [ ] Port palette conversion utilities
- [ ] Enable image export/import in CLI

### Phase 3: Avalonia Forms

- [ ] Port MainForm layout and navigation
- [ ] Port data editing forms (Unit, Class, Item)
- [ ] Port map editor
- [ ] Port image viewers
- [ ] Port event script editor

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

- **Scaffold only** — The Avalonia project is a minimal shell (menu bar, status bar). No ROM editing functionality is implemented yet.
- **No WinForms interop** — Avalonia forms cannot embed WinForms controls. Migration must be form-by-form.

### CLI

- **Limited commands** — Only `--version`, `--help`, and `--makeups` are implemented. Commands that depend on WinForms Form classes (lint, rebuild) need further Core extraction.
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

# Full solution (Windows only — includes WinForms)
msbuild /m /p:Configuration=Release /p:Platform=x86 /t:build /restore FEBuilderGBA.sln
```

## External Tools (Per-Platform)

| Tool | Windows | Linux | macOS |
|------|---------|-------|-------|
| devkitARM (ASM compilation) | `devkitPro/devkitARM/bin/` | `/opt/devkitpro/devkitARM/bin/` | `/opt/devkitpro/devkitARM/bin/` |
| Event Assembler | `Tools/Event Assembler/` | Wine or native port | Wine or native port |
| 7-Zip | `7-zip32.dll` (bundled) | SharpCompress fallback | SharpCompress fallback |
| Git | `PortableGit/` or system | System package | System or Homebrew |
