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

**ROM init levels:**
- **No** — command operates on raw files, no ROM object needed.
- **Partial** — loads ROM for metadata (song table pointer) but not full init.
- **Full** — calls `RomLoader.InitEnvironment()` + `RomLoader.LoadRom()` + `RomLoader.InitFull()` (Huffman, text, event scripts, caches).
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
```
