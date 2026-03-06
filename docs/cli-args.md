# FEBuilderGBA.CLI — Command-Line Arguments

This document lists all command-line arguments supported by the cross-platform `FEBuilderGBA.CLI` tool.

E2E tests are in `FEBuilderGBA.E2ETests/Tests/CliArgsE2ETests.cs`.

## Primary Commands

The **WinForms Alignment** column tracks whether the cross-platform CLI output is aligned with the
original WinForms CLI (`FEBuilderGBA.exe`). "ALIGNED" means the CLI produces equivalent or better
output for that command compared to the WinForms exe.

| Argument | Description | Required Args | E2E Status | WinForms Alignment |
|---|---|---|---|---|
| `--help`, `-h` | Show help message | — | E2E COVERED | ALIGNED |
| `--version` | Show version information | — | E2E COVERED | ALIGNED |
| `--makeups=<path>` | Create UPS patch | `--rom`, `--fromrom` | E2E COVERED | |
| `--applyups=<path>` | Apply UPS patch | `--rom`, `--patch` | E2E COVERED | |
| `--lint` | Run lint checks on ROM | `--rom` | E2E COVERED | |
| `--disasm=<path>` | Disassemble ROM to file | `--rom` | E2E COVERED | |
| `--decreasecolor` | Quantize image palette | `--in`, `--out`, `--paletteno` | E2E COVERED | |
| `--pointercalc` | Search pointer references | `--rom`, `--target`, `--address` | E2E COVERED | |
| `--rebuild` | Rebuild/defragment ROM | `--rom`, `--fromrom` | E2E COVERED | |
| `--songexchange` | Copy song between ROMs | `--rom`, `--fromrom`, `--fromsong`, `--tosong` | E2E COVERED | |
| `--convertmap1picture` | Convert image to map tiles | `--in`, `--outImg` and/or `--outTSA` | E2E COVERED | |
| `--translate` | Dump or import ROM text | `--rom` | E2E COVERED | |
| `--lastrom` | Load last-used ROM from config | — | E2E COVERED | |
| `--force-detail` | Force detailed editor mode (Avalonia GUI flag) | — | E2E COVERED | |
| `--translate_batch` | Batch translation: export + import all text | `--rom` | E2E COVERED | |
| `--test` | Run self-test diagnostics | — (optionally `--rom`) | E2E COVERED | |
| `--testonly` | Run self-test diagnostics then exit | — (optionally `--rom`) | E2E COVERED | |

## Auxiliary Arguments (used with primary commands)

| Argument | Description | Used by |
|---|---|---|
| `--rom=<path>` | Specify ROM file to load | Most commands |
| `--fromrom=<path>` | Specify original/source ROM | `--makeups`, `--rebuild`, `--songexchange` |
| `--force-version=<VER>` | Force ROM version (FE6, FE7J, FE7U, FE8J, FE8U) | Any ROM command |
| `--patch=<path>` | Specify UPS patch file | `--applyups` |
| `--in=<path>` | Input file | `--decreasecolor`, `--convertmap1picture`, `--translate` |
| `--out=<path>` | Output file | `--decreasecolor`, `--translate`, `--translate_batch` |
| `--paletteno=<n>` | Number of palette colors (default: 16) | `--decreasecolor` |
| `--noScale` | Do not scale colors to GBA 5-bit range | `--decreasecolor` |
| `--noReserve1stColor` | Do not reserve palette slot 0 for transparency | `--decreasecolor` |
| `--ignoreTSA` | Ignore TSA tile deduplication constraints | `--decreasecolor` |
| `--target=<path>` | Target ROM file | `--pointercalc` |
| `--address=<hex_list>` | Hex address list (comma-separated or file) | `--pointercalc` |
| `--tracelevel=<n>` | Search depth level | `--pointercalc` |
| `--fromsong=<hex>` | Source song ID | `--songexchange` |
| `--tosong=<hex>` | Destination song ID | `--songexchange` |
| `--outImg=<path>` | Output tile data file | `--convertmap1picture` |
| `--outTSA=<path>` | Output TSA data file | `--convertmap1picture` |

## No-Args Behavior

When invoked with no arguments, the CLI prints the help message and exits with code 0. (E2E COVERED)
