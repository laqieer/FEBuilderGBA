# FEBuilderGBA.CLI Reference

Comprehensive reference for the cross-platform `FEBuilderGBA.CLI` command-line tool.

**Source:** `FEBuilderGBA.CLI/Program.cs`, `FEBuilderGBA.CLI/RomLoader.cs`

## Usage

```
FEBuilderGBA.CLI [command] [options]
```

Arguments use `--key=value` syntax. Boolean flags use `--flag` (no value).
The short alias `-h` is equivalent to `--help`.

When invoked with no arguments, the CLI prints help and exits with code 0.

> **Android note:** The Android port is a **GUI build** (Avalonia single-view app), not a CLI command — there are no Android-specific CLI flags. See [docs/ANDROID.md](ANDROID.md) and [docs/CROSS_PLATFORM.md](CROSS_PLATFORM.md) for how the cross-platform builds are produced and run.

---

## Global Options

These options can be combined with any ROM-based command.

| Option | Description |
|---|---|
| `--rom=<path>` | Path to the GBA ROM file to operate on. Required by most commands. |
| `--force-version=<VER>` | Override automatic ROM version detection. Values: `FE6`, `FE7J`, `FE7U`, `FE8J`, `FE8U`. |

---

## Commands

### `--help`, `-h`

Print usage information and exit.

```
FEBuilderGBA.CLI --help
FEBuilderGBA.CLI -h
```

**Exit code:** always 0.

---

### `--version`

Print version, copyright, and license information.

```
FEBuilderGBA.CLI --version
```

Output format:
```
FEBuilderGBA.Core Version:YYYYMMDD.HH
Copyright: 2017-
License: GPLv3
```

**Exit code:** always 0.

---

### `--makeups=<path>`

Create a UPS patch by diffing a modified ROM against the original.

| Option | Required | Description |
|---|---|---|
| `--rom=<path>` | Yes | Path to the **modified** ROM. |
| `--fromrom=<path>` | Yes | Path to the **original** (vanilla) ROM. |

```
FEBuilderGBA.CLI --makeups=patch.ups --rom=modified.gba --fromrom=original.gba
```

**Exit code:** 0 on success, 1 if files are missing or arguments are incomplete.

---

### `--applyups=<path>`

Apply a UPS patch to a ROM and write the patched result.

| Option | Required | Description |
|---|---|---|
| `--rom=<path>` | Yes | Path to the **original** ROM to patch. |
| `--patch=<path>` | Yes | Path to the `.ups` patch file. |

The `=<path>` value of `--applyups` is the **output** file path.

```
FEBuilderGBA.CLI --applyups=output.gba --rom=original.gba --patch=patch.ups
```

**Exit code:** 0 on success, 1 on error. CRC mismatch prints a warning but still writes output.

---

### `--lint`

Run FELint checks on a ROM and report errors/warnings.

| Option | Required | Description |
|---|---|---|
| `--rom=<path>` | Yes | ROM to lint. |
| `--force-version=<VER>` | No | Override version detection. |

```
FEBuilderGBA.CLI --lint --rom=rom.gba
FEBuilderGBA.CLI --lint --rom=rom.gba --force-version=FE8U
```

**Exit code:** 0 if no errors found (warnings are OK), 1 if any ERROR-severity issues exist.

---

### `--disasm=<path>`

Disassemble the ROM's code sections to a text file.

| Option | Required | Description |
|---|---|---|
| `--rom=<path>` | Yes | ROM to disassemble. |
| `--force-version=<VER>` | No | Override version detection. |

The `=<path>` value is the output `.asm` file.

```
FEBuilderGBA.CLI --disasm=output.asm --rom=rom.gba
```

**Exit code:** 0 on success, 1 on error.

---

### `--decreasecolor`

Quantize an image's palette to a limited number of colors (for GBA-compatible graphics).

| Option | Required | Description |
|---|---|---|
| `--in=<path>` | Yes | Input image file (PNG, etc.). |
| `--out=<path>` | Yes | Output image file. |
| `--paletteno=<n>` | No | Maximum palette colors. Default: **16**. |
| `--noScale` | No | Do not scale RGB values to GBA 5-bit range (0-31). |
| `--noReserve1stColor` | No | Do not reserve palette slot 0 for transparency. |
| `--ignoreTSA` | No | Ignore TSA 8x8 tile deduplication constraints. |

```
FEBuilderGBA.CLI --decreasecolor --in=input.png --out=output.png --paletteno=16
FEBuilderGBA.CLI --decreasecolor --in=input.png --out=output.png --paletteno=16 --noScale --noReserve1stColor
```

This command does **not** require a ROM; it operates purely on image files.

**Exit code:** 0 on success, 1 on error.

---

### `--pointercalc`

Search for pointer references across two ROMs. Finds where addresses in the source ROM map to in the target ROM.

| Option | Required | Description |
|---|---|---|
| `--rom=<path>` | Yes | Source ROM. |
| `--target=<path>` | Yes | Target ROM to search in. |
| `--address=<hex_list>` | Yes | Comma-separated hex addresses (e.g., `0x100,0x200`) or path to a file containing addresses. |
| `--tracelevel=<n>` | No | Search depth level. Higher values search more bytes per address. Default search length is 16 bytes; tracelevel N sets it to `max(4, N*4)`. |

```
FEBuilderGBA.CLI --pointercalc --rom=source.gba --target=target.gba --address=0x100,0x200
```

This command does **not** perform full ROM initialization — it reads the raw binary data directly.

**Exit code:** always 0 (even if no matches found).

---

### `--rebuild`

Rebuild (defragment) a modified ROM using the vanilla ROM as a reference.

| Option | Required | Description |
|---|---|---|
| `--rom=<path>` | Yes | Path to the **modified** ROM. |
| `--fromrom=<path>` | Yes | Path to the **vanilla** (original) ROM. |

```
FEBuilderGBA.CLI --rebuild --rom=modified.gba --fromrom=original.gba
```

**Exit code:** 0 on success, 1 on error.

---

### `--songexchange`

Copy a song from one ROM to another.

| Option | Required | Description |
|---|---|---|
| `--rom=<path>` | Yes | **Destination** ROM (will be modified in-place). |
| `--fromrom=<path>` | Yes | **Source** ROM to copy the song from. |
| `--fromsong=<hex>` | Yes | Song ID in the source ROM (hex, e.g., `0x1A`). |
| `--tosong=<hex>` | Yes | Song slot in the destination ROM (hex, e.g., `0x1A`). |
| `--force-version=<VER>` | No | Override version detection for the destination ROM. |

```
FEBuilderGBA.CLI --songexchange --rom=dest.gba --fromrom=source.gba --fromsong=0x1A --tosong=0x1A
```

**Warning:** This command modifies the destination ROM file in-place.

**Exit code:** 0 on success, 1 on error.

---

### `--convertmap1picture`

Convert an image to GBA map tile data and TSA (Tile Set Arrangement).

| Option | Required | Description |
|---|---|---|
| `--in=<path>` | Yes | Input image file. Dimensions must be multiples of 8 pixels. |
| `--outImg=<path>` | No* | Output tile data binary file. |
| `--outTSA=<path>` | No* | Output TSA data file (LZ77-compressed). |

*At least one of `--outImg` or `--outTSA` is required.

```
FEBuilderGBA.CLI --convertmap1picture --in=map.png --outImg=tiles.bin --outTSA=tsa.bin
```

This command does **not** require a ROM.

**Exit code:** 0 on success, 1 on error.

---

### `--translate`

Dump ROM text to a TSV file, or import translated text from a TSV file back into the ROM.

| Option | Required | Description |
|---|---|---|
| `--rom=<path>` | Yes | ROM file. |
| `--out=<path>` | No | Export: write all text entries to this TSV file. |
| `--in=<path>` | No | Import: read text entries from this TSV file and write them to the ROM. |
| `--force-version=<VER>` | No | Override version detection. |

**Three modes:**
1. **Info mode** (no `--out`, no `--in`): prints ROM version and text entry count.
2. **Export mode** (`--out`): dumps all text to a TSV file.
3. **Import mode** (`--in`): reads TSV and writes text back to the ROM. The ROM file is saved in-place.

```
# Info mode
FEBuilderGBA.CLI --translate --rom=rom.gba

# Export
FEBuilderGBA.CLI --translate --rom=rom.gba --out=texts.tsv

# Import
FEBuilderGBA.CLI --translate --rom=rom.gba --in=texts.tsv
```

**Exit code:** 0 on success, 1 on error (including if zero entries were written during import).

---

### `--translate_batch`

Batch translation workflow: export all text, then optionally import translated text.

| Option | Required | Description |
|---|---|---|
| `--rom=<path>` | Yes | ROM file. |
| `--out=<path>` | No | Export TSV path. Defaults to `<rom_name>.tsv` if omitted. |
| `--in=<path>` | No | If provided, import translated text from this TSV after exporting. |
| `--force-version=<VER>` | No | Override version detection. |

```
FEBuilderGBA.CLI --translate_batch --rom=rom.gba --out=texts.tsv --in=translated.tsv
```

The export always runs. The import only runs if `--in` is provided. The ROM is saved in-place after import.

**Exit code:** 0 on success, 1 on error.

---

### `--lastrom`

Load the last-used ROM from the application config file.

| Option | Required | Description |
|---|---|---|
| `--force-version=<VER>` | No | Override version detection. |

```
FEBuilderGBA.CLI --lastrom
```

Reads `Last_Rom_Filename` from `config/config.xml`, loads the ROM, and prints version info.

**Exit code:** 0 on success, 1 if no last ROM is configured or the file is missing.

---

### `--force-detail`

Acknowledge the detailed editor mode flag. This is primarily used by the Avalonia GUI to skip easy-mode. In the CLI, it simply prints a message and exits.

```
FEBuilderGBA.CLI --force-detail
```

**Exit code:** always 0.

---

### `--test`, `--testonly`

Run built-in self-test diagnostics. Checks config loading, ROM loading, text system, event scripts, and image service.

| Option | Required | Description |
|---|---|---|
| `--rom=<path>` | No | If provided, runs additional ROM-specific tests (load, init, text, event scripts). |
| `--force-version=<VER>` | No | Override version detection. |

`--testonly` behaves identically to `--test` but prints an additional "exiting" message.

```
FEBuilderGBA.CLI --test --rom=rom.gba
FEBuilderGBA.CLI --testonly --rom=rom.gba
FEBuilderGBA.CLI --test
```

**Exit code:** 0 if all tests pass, 1 if any test fails.

---

## Decomp project mode

These commands operate on a **decomp project directory** (one containing a `febuilder.project.json`
manifest and a buildable C/JSON source tree) instead of a single `.gba` file. The classic ROM mode
(`--rom=<path>`) is unchanged; the decomp family below adds a `--project=<dir>` mode that loads the
project's *built* ROM as a preview and can rewrite the owning source elements (a churn-free diff)
rather than mutating the ROM directly. A typical workflow is: open the project (`--project`) → resolve
addresses to symbols (`--resolve-addr`) → classify FEBuilder edits (`--migrate-diff`) → rewrite the
owning source (`--write-source`) / export assets (`--export-asset`) → rebuild (`--build-project`).

### `--project=<dir>`

Open a decomp project directory and load its built ROM for preview. This is a **modifier**, not a
standalone command — combine it with `--rom-info` (which then prints a `Mode: Decomp (preview ROM …)`
line, flagging that the ROM is a source-backed build preview) or with any of the other decomp commands
below. Classic ROM mode (`--rom=<path>`) is unchanged.

| Option | Required | Description |
|---|---|---|
| `--project=<dir>` | Yes | Decomp project directory (containing `febuilder.project.json`). |

```
FEBuilderGBA.CLI --rom-info --project=decomp/
```

**Exit code:** no standalone exit code — determined by the command it combines with.

---

### `--resolve-addr=<hex>`

Resolve an address to a decomp project symbol (requires `--project`). Layers the project's
`.map` / ELF / `.sym` / JSON symbols over the shipped symbol set; this is also what powers the
Pointer Tool's "What is this address?" lookup.

| Option | Required | Description |
|---|---|---|
| `--project=<dir>` | Yes | Decomp project directory whose symbols are layered over the shipped set. |
| `--resolve-addr=<hex>` | Yes | The address to resolve (e.g., `0x08012345`). |

Output: it first prints `addr=0x........`. **When a symbol resolves**, it additionally prints
`symbol=...`, `source=<map|elf|sym|json|shipped>`, and `offset=+0x..`. On the no-symbol path (or an
internal error) it instead prints `symbol=(none)` (preceded by `addr=` if it got that far). The
`source=` / `offset=` lines do **not** always print — only when a symbol is actually found. This
command never throws.

```
FEBuilderGBA.CLI --resolve-addr=0x08012345 --project=decomp/
```

**Exit code:** always 0.

---

### `--migrate-diff`

Decomp **diff-to-source migration assistant**: classifies the changes between the project's
built/baseline ROM and a FEBuilder-edited ROM by symbol / category / source / confidence. This is
**advisory and read-only** — it never writes the ROM or any source file.

| Option | Required | Description |
|---|---|---|
| `--project=<dir>` | Yes | Decomp project directory (its built ROM is the baseline). |
| `--rom2=<editedRom>` | Yes | The FEBuilder-edited ROM, compared against the project's built/baseline ROM. |
| `--out=<report.tsv>` | No | Write the classified report (range / symbol / category / source / confidence) as TSV. |
| `--max-gap=<int>` | No | Small-gap merge distance for range coalescing. Default: **16**. |

```
FEBuilderGBA.CLI --migrate-diff --project=decomp/ --rom2=edited.gba --out=migrate.tsv
```

**Exit code:** 1 on a usage fault (missing `--project` / `--rom2` / file not found), otherwise 0.

---

### `--write-source`

Rewrite the owning **C/JSON source element** for a structured table entry instead of mutating the
ROM. This produces a churn-free, minimal diff and marks the project as "needs rebuild".

| Option | Required | Description |
|---|---|---|
| `--project=<dir>` | Yes | Decomp project directory (the table must declare a source owner in `tables[]`). |
| `--table=<name>` | Yes | Structured table: `items`, `units` (alias `characters`), `classes`, `map_settings`/`chapter`, `support_units`/`support_attributes`/`support_talks`. |
| `--id=<n>` | Yes | Entry index, in array order. |
| `--field=<name>` | Yes | C/JSON field to change. **REPEATABLE, ordered** — pair each `--field` with a following `--value`. |
| `--value=<int>` | Yes | New value for the preceding `--field` (`0x` hex or decimal; signed fields take the two's-complement magnitude). **REPEATABLE.** |
| `--out-diff=<path>` | No | Write a before/after of the changed source element. |

Unsupported / pointer-like fields fall back to ROM-only / manual handling, and shops are ROM-only.

```
FEBuilderGBA.CLI --write-source --project=decomp/ --table=items --id=1 --field=might --value=0x0A
FEBuilderGBA.CLI --write-source --project=decomp/ --table=units --id=1 --field=hp --value=18 --field=pow --value=7
```

**Exit code:** 0 on success, non-zero on usage / write fault.

---

### `--export-asset`

Export a ROM asset to a decomp source-tree path. Use the existing dedicated commands
(`--export-midi`, `--render-portrait` / `--export-portrait-all`, `--export-battle-anime`) for
music, portraits, and battle animations.

| Option | Required | Description |
|---|---|---|
| `--kind=<kind>` | Yes | Asset kind: `graphics`, `palette`, `map` (always LZ77-decompressed), `text`. |
| `--out=<path>` | Yes | Output path (project-relative when `--project`; absolute or relative when `--rom`). |
| `--rom=<path>` **or** `--project=<dir>` | Yes | Source ROM, or a decomp project whose built ROM is read (one is required). |
| `--addr=<hex>` | Cond. | ROM address of the asset (required for `graphics`, `palette`, `map`). |
| `--palette-addr=<hex>` | Cond. | ROM address of the palette data (required for `graphics`). |
| `--width=<int>` | Cond. | Image width in pixels (required for `graphics`). |
| `--height=<int>` | Cond. | Image height in pixels (required for `graphics`). |
| `--colors=<int>` | No | Palette colors (for `palette` and `graphics`). Default: **16**. |
| `--bpp=<int>` | No | Bits per pixel for `graphics`: `4` or `8`. Default: **4**. |
| `--compressed` | No | (graphics only) the source tiles at `--addr` are LZ77-compressed (flag). |

```
FEBuilderGBA.CLI --export-asset --kind=palette --rom=rom.gba --addr=0x5524 --out=gfx/palette.pal
FEBuilderGBA.CLI --export-asset --kind=graphics --project=decomp/ --addr=0x123000 --width=64 --height=64 --palette-addr=0x124000 --out=gfx/tiles.png
FEBuilderGBA.CLI --export-asset --kind=map --rom=rom.gba --addr=0x200000 --out=map/chapter1.mar
FEBuilderGBA.CLI --export-asset --kind=text --rom=rom.gba --out=text/
```

**Exit code:** 0 on success, non-zero on usage / export fault.

---

### `--build-project`

Run the decomp project's declared build command (requires `--project`; the manifest
`febuilder.project.json` must declare a `build` section). **Gated behind `--yes`** — it never runs
implicitly. Captures the build's stdout/stderr.

| Option | Required | Description |
|---|---|---|
| `--project=<dir>` | Yes | Decomp project directory containing `febuilder.project.json` with a `build` section. |
| `--yes` | Yes | Required to actually execute the build command (explicit opt-in gate). |
| `--reload` | No | After a successful build, reload the built ROM into CoreState and print version info. |
| `--timeout=<ms>` | No | Build timeout in milliseconds. Default: **600000** (10 minutes). |

```
FEBuilderGBA.CLI --build-project --project=decomp/ --reload --yes
```

**Exit code:** 0 on success, non-zero on usage / build fault.

---

### `--decomp-audit`

Print the maintained decomp **round-trip coverage matrix** (#1150) — which FEBuilder editor/action is source-backed, exporter-migrated, preview-only, manual, or ROM-only. READ-ONLY; never loads a ROM.

| Option | Required | Description |
|---|---|---|
| `--format=<tsv\|md>` | No | Output format: `tsv` (default) or `md` (GitHub markdown table). |
| `--out=<path>` | No | Write the matrix to a file (otherwise printed to stdout). |

```
FEBuilderGBA.CLI --decomp-audit
FEBuilderGBA.CLI --decomp-audit --format=md --out=docs/decomp-coverage.md
```

**Exit code:** 0 (1 on a write fault).

---

### `--nmm-to-manifest`

Parse a No$gba memory map (`.nmm`, the `--export-data … STRUCT/NMM` sibling) into a decomp manifest `tables[]` entry JSON (#1150). A **schema aid, not a writability path**: pointer / var-length / odd-size fields survive flagged `"unsupported": true` (never dropped). No ROM.

| Option | Required | Description |
|---|---|---|
| `--in=<x.nmm>` | Required | Input `.nmm` file. |
| `--table=<name>` | No | Table name for the emitted entry (default `table`). |
| `--out=<path>` | No | Write the JSON to a file (otherwise stdout); warnings go to stderr. |

```
FEBuilderGBA.CLI --nmm-to-manifest --in=items.nmm --table=items --out=items.tables.json
```

**Exit code:** 0 on parse-ok, 1 on usage/file-not-found, 2 when the NMM header is unusable.

---

### `--manifest-to-nmm`

Emit `.nmm` text for a manifest table owner (#1150), reusing the FormatNMM grammar. Pointer/var fields are flagged unsafe via stderr warnings. No ROM mutation.

| Option | Required | Description |
|---|---|---|
| `--project=<dir>` | Required | Decomp project directory whose manifest declares the table owner. |
| `--table=<name>` | Required | Table name to export to `.nmm`. |
| `--out=<path>` | No | Write the `.nmm` to a file (otherwise stdout). |

```
FEBuilderGBA.CLI --manifest-to-nmm --project=decomp/ --table=items --out=items.nmm
```

**Exit code:** 0 on success, 1 on usage/load fault, 2 when the table has no owner in the manifest.

---

### `--validate-asset`

Structurally validate a decomp IMPORT asset on disk (#1150) **before** wiring it into a build. READ-ONLY; **NEVER loads a ROM**. Indexed PNG → color type 3 / tile alignment / palette size / in-range indices; JASC `.pal` → header/count/color triples; `.mar` → length == w*h*2 and the `<<3` low-3-bits-zero invariant (validated against the `.mar.json` sidecar).

| Option | Required | Description |
|---|---|---|
| `--kind=<kind>` | Required | Asset kind: `graphics`, `palette`, `portrait`, `icon`, `map`. |
| `--in=<srcAsset>` | Required | Input asset file (PNG / `.pal` / `.mar`). |

```
FEBuilderGBA.CLI --validate-asset --kind=graphics --in=gfx/tiles.png
FEBuilderGBA.CLI --validate-asset --kind=palette --in=gfx/palette.pal
```

Each finding prints as `ERROR [CODE] msg` (stderr) or `WARN [CODE] msg` (stdout) plus a summary line.

**Exit code:** 0 on no errors (warnings allowed), 2 on errors, 1 on usage / bad-kind.

---

## Summary Table

| Command | `--rom` | `--fromrom` | `--in` | `--out` | Other required | ROM init |
|---|---|---|---|---|---|---|
| `--help` / `-h` | — | — | — | — | — | No |
| `--version` | — | — | — | — | — | No |
| `--makeups=<path>` | Required | Required | — | — | — | No |
| `--applyups=<path>` | Required | — | — | — | `--patch` | No |
| `--lint` | Required | — | — | — | — | Full |
| `--disasm=<path>` | Required | — | — | — | — | Full |
| `--decreasecolor` | — | — | Required | Required | — | No |
| `--pointercalc` | Required | — | — | — | `--target`, `--address` | No |
| `--rebuild` | Required | Required | — | — | — | No |
| `--songexchange` | Required | Required | — | — | `--fromsong`, `--tosong` | Partial |
| `--convertmap1picture` | — | — | Required | — | `--outImg`/`--outTSA` | No |
| `--translate` | Required | — | Optional | Optional | — | Full |
| `--translate_batch` | Required | — | Optional | Optional | — | Full |
| `--lastrom` | — | — | — | — | — | Full |
| `--force-detail` | — | — | — | — | — | No |
| `--test` / `--testonly` | Optional | — | — | — | — | Conditional |
| `--rom-info` | Optional | — | — | — | `--rom` or `--project` | Full |
| `--project=<dir>` | — | — | — | — | (modifier; combine with another command) | Project |
| `--resolve-addr=<hex>` | — | — | — | — | `--project` | Project |
| `--migrate-diff` | — | — | — | Optional | `--project`, `--rom2` | Project |
| `--write-source` | — | — | — | — | `--project`, `--table`, `--id`, `--field`, `--value` | Project |
| `--export-asset` | Optional | — | — | Required | `--kind` (+ `--rom` or `--project`) | Project |
| `--validate-asset` | — | — | Required | — | `--kind` | No |
| `--build-project` | — | — | — | — | `--project`, `--yes` | Project |
| `--decomp-audit` | — | — | — | Optional | — | No |
| `--nmm-to-manifest` | — | — | Required | Optional | — | No |
| `--manifest-to-nmm` | — | — | — | Optional | `--project`, `--table` | Project |

**ROM init levels:**
- **No** — command operates on raw files, no ROM object needed.
- **Partial** — loads ROM for metadata (song table pointer) but not full init.
- **Full** — calls `RomLoader.InitEnvironment()` + `RomLoader.LoadRom()` + `RomLoader.InitFull()` (Huffman, text, event scripts, caches).
- **Project** — opens a decomp project via `--project` and loads its *built* ROM (full init); commands that also accept `--rom` fall back to a plain ROM load with that flag.
- **Conditional** — depends on whether `--rom` is provided.

---

## Exit Codes

| Code | Meaning |
|---|---|
| `0` | Success (or no errors for `--lint`). |
| `1` | Error: missing arguments, file not found, operation failed, or lint found ERROR-severity issues. |

---

## Argument Parsing

Arguments are parsed into a `Dictionary<string, string>`:

- `--key=value` → key = `"--key"`, value = `"value"`
- `--flag` (no `=`) → key = `"--flag"`, value = `""` (empty string)
- `-h` → mapped to `--help`
- Positional arguments (no `--` prefix) are ignored.
- Duplicate keys: last value wins.

---

## Examples

```bash
# Show help
FEBuilderGBA.CLI --help

# Show version
FEBuilderGBA.CLI --version

# Create a UPS patch
FEBuilderGBA.CLI --makeups=patch.ups --rom=modified.gba --fromrom=original.gba

# Apply a UPS patch
FEBuilderGBA.CLI --applyups=output.gba --rom=original.gba --patch=patch.ups

# Lint a ROM
FEBuilderGBA.CLI --lint --rom=rom.gba
FEBuilderGBA.CLI --lint --rom=rom.gba --force-version=FE8U

# Disassemble
FEBuilderGBA.CLI --disasm=output.asm --rom=rom.gba

# Reduce image colors
FEBuilderGBA.CLI --decreasecolor --in=input.png --out=output.png --paletteno=16
FEBuilderGBA.CLI --decreasecolor --in=input.png --out=output.png --paletteno=16 --noScale --noReserve1stColor

# Pointer search
FEBuilderGBA.CLI --pointercalc --rom=source.gba --target=target.gba --address=0x100,0x200

# Rebuild ROM
FEBuilderGBA.CLI --rebuild --rom=modified.gba --fromrom=original.gba

# Song exchange
FEBuilderGBA.CLI --songexchange --rom=dest.gba --fromrom=source.gba --fromsong=0x1A --tosong=0x1A

# Convert image to map tiles
FEBuilderGBA.CLI --convertmap1picture --in=map.png --outImg=tiles.bin --outTSA=tsa.bin

# Export text
FEBuilderGBA.CLI --translate --rom=rom.gba --out=texts.tsv

# Import text
FEBuilderGBA.CLI --translate --rom=rom.gba --in=texts.tsv

# Batch translate
FEBuilderGBA.CLI --translate_batch --rom=rom.gba --out=texts.tsv --in=translated.tsv

# Load last ROM
FEBuilderGBA.CLI --lastrom

# Self-test
FEBuilderGBA.CLI --test --rom=rom.gba
FEBuilderGBA.CLI --testonly --rom=rom.gba

# Decomp project mode
FEBuilderGBA.CLI --rom-info --project=decomp/
FEBuilderGBA.CLI --resolve-addr=0x08012345 --project=decomp/
FEBuilderGBA.CLI --migrate-diff --project=decomp/ --rom2=edited.gba --out=migrate.tsv
FEBuilderGBA.CLI --write-source --project=decomp/ --table=items --id=1 --field=might --value=0x0A
FEBuilderGBA.CLI --export-asset --kind=graphics --project=decomp/ --addr=0x123000 --width=64 --height=64 --palette-addr=0x124000 --out=gfx/tiles.png
FEBuilderGBA.CLI --build-project --project=decomp/ --reload --yes
```
