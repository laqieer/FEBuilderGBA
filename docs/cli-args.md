# FEBuilderGBA.CLI — Command-Line Arguments

This document tracks the **E2E-covered** primary commands of the cross-platform `FEBuilderGBA.CLI`
tool and their alignment with the original WinForms CLI. The table below is *not* the complete
command set — it lists commands exercised by `FEBuilderGBA.E2ETests/Tests/CliArgsE2ETests.cs`
and command-specific E2E suites such as `CliExportBuildfileE2ETests.cs`.

For the **full command set** (the `Main` dispatcher in `FEBuilderGBA.CLI/Program.cs` routes 68
distinct user-facing commands), see [`docs/cli-reference.md`](cli-reference.md) or run:

```bash
FEBuilderGBA.CLI --help
```

E2E tests are in `FEBuilderGBA.E2ETests/Tests/CliArgsE2ETests.cs` and sibling command-specific
test files in that directory.

## Primary Commands (E2E-covered)

The **WinForms Alignment** column tracks whether the cross-platform CLI output is aligned with the
original WinForms CLI (`FEBuilderGBA.exe`). "ALIGNED" means the CLI produces equivalent or better
output for that command compared to the WinForms exe. "CLI-ONLY (N/A)" marks commands that have no
WinForms CLI counterpart to align against.

| Argument | Description | Required Args | E2E Status | WinForms Alignment |
|---|---|---|---|---|
| `--help`, `-h` | Show help message | — | E2E COVERED | ALIGNED |
| `--version` | Show version information | — | E2E COVERED | ALIGNED |
| `--makeups=<path>` | Create UPS patch | `--rom` (opt: `--fromrom`) | E2E COVERED | ALIGNED |
| `--applyups=<path>` | Apply UPS patch | `--rom`, `--patch` | E2E COVERED | ALIGNED |
| `--lint` | Run lint checks on ROM | `--rom` | E2E COVERED | ALIGNED |
| `--disasm=<path>` | Disassemble ROM to file | `--rom` | E2E COVERED | ALIGNED |
| `--decreasecolor` | Quantize image palette | `--in`, `--out` (opt: `--paletteno`, `--json`) | E2E COVERED | ALIGNED |
| `--pointercalc` | Search pointer references | `--rom`, `--target`, `--address` | E2E COVERED | ALIGNED |
| `--rebuild` | Rebuild/defragment ROM | `--rom` (opt: `--fromrom`) | E2E COVERED | ALIGNED |
| `--export-buildfile` | Export a deterministic buildfile recipe of the clean→modded delta | `--rom`, `--clean`, `--out` (opt: `--force-version`, `--with-source`) | E2E COVERED | CLI-ONLY (N/A) |
| `--songexchange` | Copy song between ROMs | `--rom`, `--fromrom`, `--fromsong`, `--tosong` | E2E COVERED | ALIGNED |
| `--convertmap1picture` | Convert image to map tiles | `--in`, one or more of `--outImg`/`--outTSA`/`--outPal` (opt: `--json`) | E2E COVERED | ALIGNED |
| `--translate` | Dump or import ROM text | `--rom` | E2E COVERED | ALIGNED |
| `--translate-roundtrip` | Validate text export/import round-trip | `--rom` (opt: `--out`) | E2E COVERED | ALIGNED |
| `--lastrom` | Load last-used ROM from config | — | E2E COVERED | ALIGNED |
| `--force-detail` | Force detailed editor mode (Avalonia GUI flag) | — | E2E COVERED | ALIGNED |
| `--translate_batch` | Batch translation: export + import all text | `--rom` | E2E COVERED | ALIGNED |
| `--test`, `--testonly` | Run self-test diagnostics (both route to the same handler) | — (optionally `--rom`) | E2E COVERED | ALIGNED |
| `--import-battle-anime` | Import battle animation from `.txt`/`.bin` | `--rom`, `--animation-id`, `--in` | E2E COVERED | ALIGNED |
| `--export-battle-anime` | Export battle animation to `.txt`+PNG (or `--gif`) | `--rom`, `--animation-id`, `--out` | E2E COVERED | ALIGNED |
| `--diff` | Compare two ROMs byte-by-byte | `--rom`, `--rom2` | E2E COVERED | ALIGNED |
| `--import-portrait-all` | Batch import portraits from a directory | `--rom`, `--dir` | E2E COVERED | ALIGNED |
| `--export-map-settings` | Export all map/chapter settings to TSV | `--rom`, `--out` | E2E COVERED | ALIGNED |
| `--lz77` | LZ77 compress/decompress a file | `--in`, `--out`, `--compress`/`--decompress` | E2E COVERED | ALIGNED |
| `--checksum` | Validate GBA ROM header checksum | `--rom` | E2E COVERED | ALIGNED |
| `--repair-header` | Fix corrupted GBA ROM header checksum | `--rom` | E2E COVERED | ALIGNED |
| `--rom-info` | Print ROM metadata (version, title, CRC32, …) | `--rom` | E2E COVERED | ALIGNED |
| `--list-tables` | List all exportable struct table names | — | E2E COVERED | ALIGNED |
| `--export-palette` | Export GBA palette to a file | `--rom`, `--addr`, `--out` | E2E COVERED | ALIGNED |
| `--import-palette` | Import a palette file into the ROM | `--rom`, `--addr`, `--in` | E2E COVERED | ALIGNED |

## Auxiliary Arguments (used with primary commands)

| Argument | Description | Used by |
|---|---|---|
| `--rom=<path>` | Specify ROM file to load | Most commands |
| `--fromrom=<path>` | Specify original/source ROM (auto-detected if omitted for `--makeups`/`--rebuild`) | `--makeups`, `--rebuild`, `--songexchange` |
| `--force-version=<VER>` | Force ROM version (FE6, FE7J, FE7U, FE8J, FE8U); the value is mandatory, and valueless forms exit 1 before command I/O | Any ROM command |
| `--patch=<path>` | Specify UPS patch file | `--applyups` |
| `--rom2=<path>` | Second ROM file to compare against | `--diff` |
| `--in=<path>` | Input file | `--decreasecolor`, `--convertmap1picture`, `--translate`, `--lz77`, `--import-palette`, `--import-battle-anime` |
| `--out=<path>` | Output file | `--decreasecolor`, `--translate`, `--translate-roundtrip`, `--translate_batch`, `--export-map-settings`, `--lz77`, `--export-palette`, `--export-battle-anime`, `--diff`, `--export-buildfile` (new project directory, must not exist) |
| `--clean=<path>` | Clean/baseline ROM of the same version; its SHA-256 is the reproducibility identity | `--export-buildfile` |
| `--with-source` | Also emit an advisory, non-authoritative `source/` projection (opt-in) | `--export-buildfile` |
| `--dir=<path>` | Input directory of portraits | `--import-portrait-all` |
| `--addr=<hex>` | ROM address (raw offset or `0x08…` pointer) | `--export-palette`, `--import-palette` |
| `--colors=<n>` | Number of palette colors (default: 16, range 1-256) | `--export-palette` |
| `--animation-id=<id>` | Battle animation id (0-based) | `--import-battle-anime`, `--export-battle-anime` |
| `--gif` | Export the animation as an animated GIF | `--export-battle-anime` |
| `--section=<N>` | Section index 0-11 for GIF export (default: 0) | `--export-battle-anime` |
| `--compress` / `--decompress` | LZ77 mode (exactly one required) | `--lz77` |
| `--paletteno=<n>` | Number of palette colors (default: 16; range 2-256, or 1-256 with `--noReserve1stColor`) | `--decreasecolor` |
| `--noScale` | Do not scale colors to GBA 5-bit range | `--decreasecolor` |
| `--noReserve1stColor` | Do not reserve palette slot 0 for transparency | `--decreasecolor` |
| `--ignoreTSA` | Ignore TSA tile deduplication constraints | `--decreasecolor` |
| `--outPal=<path>` | Output the matching GBA palette data | `--convertmap1picture` |
| `--json` | Emit one machine-readable result object on stdout; errors remain non-zero | `--decreasecolor`, `--convertmap1picture` |
| `--target=<path>` | Target ROM file | `--pointercalc` |
| `--address=<hex_list>` | Hex address list (comma-separated or file) | `--pointercalc` |
| `--tracelevel=<n>` | Search depth level | `--pointercalc` |
| `--fromsong=<hex>` | Source song ID | `--songexchange` |
| `--tosong=<hex>` | Destination song ID | `--songexchange` |
| `--outImg=<path>` | Output tile data file | `--convertmap1picture` |
| `--outTSA=<path>` | Output TSA data file | `--convertmap1picture` |

## No-Args Behavior

When invoked with no arguments, the CLI prints the help message and exits with code 0. (E2E COVERED)
