# FEBuilderGBA.CLI as a decomp-C data backend

Tracks issue [#1939](https://github.com/laqieer/FEBuilderGBA/issues/1939).

## Motivation

FE decomp projects (e.g. the FE8/FE7/FE6 disassembly trees the community builds against) need C
source that declares ROM table data as real, typed, byte-exact C objects — not a TSV/CSV/JSON row
dump a human or script has to hand-translate into `struct` initializers. `--export-data
--format=json` (issue [#1937](https://github.com/laqieer/FEBuilderGBA/issues/1937), see
[`febuilder-cli-as-llm-backend.md`](febuilder-cli-as-llm-backend.md)) solved the LLM/agent-editable
side of that problem; this issue solves the **decomp-C** side: a fifth `--export-data` format that
emits a self-contained, GNU11, `arm-none-eabi-gcc`-compatible translation unit per table, straight
from `FEBuilderGBA.Core`'s existing struct metadata and the same shared ROM-reading seam every other
export format already uses.

**Out of scope** (deferred to other issues, not delivered here): a shipped decomp tree, orchestrating
a whole mod's worth of tables into one project (issue
[#1935](https://github.com/laqieer/FEBuilderGBA/issues/1935)), and a guaranteed C→ROM round-trip
(issue [#1936](https://github.com/laqieer/FEBuilderGBA/issues/1936)). See
[Honest exclusions](#honest-exclusions) below.

## The contract

```text
FEBuilderGBA.CLI --export-data --rom=<rom> --table=<name> --format=c [--out=<path>] [--c-symbol=<identifier>]
```

- **`--format=c`** is a fifth, **export-only** value alongside `tsv`/`csv`/`ea`/`json` (an unsupported
  value — including trying to use `c` with `--import-data` — is rejected with an error, same as any
  other bad `--format`; see [Export-only](#export-only-import-data-and---data-roundtrip-are-unaffected)).
- **Default extension is `.c`.** With no `--out`, a single-table export writes
  `<rom>.<table>.c` (same pattern as every other format). With `--out=<path>` given, a single-table
  export writes exactly `<path>`.
- **`--table=all --format=c --out=<base>`** emits `<base>.<table>.c` — one valid translation unit
  per registered table (currently 40; see `--list-tables`), **including** zero-row / version-absent
  tables (e.g. an FE8-only table exported from an FE6 ROM — see the
  [zero-row contract](#zero-row-contract) below). A C layout/validation failure for any one table is
  **fatal** (nonzero exit, an error naming the failing table) — the CLI does not silently skip a
  table and keep going.
- **`--c-symbol=<identifier>`** overrides the emitted data-array symbol for a **single-table** export
  only:
  - `<identifier>` must be a strictly valid external C/GNU identifier
    (`[A-Za-z][A-Za-z0-9_]*`, not a C11/GNU keyword). Leading underscores are rejected because
    they are implementation-reserved for file-scope symbols, where the data array is emitted.
    Names reserved by the generated `<stdint.h>` prologue are also rejected, including its
    typedef/macro families (`uint8_t`, `uint16_t`, `uint32_t`, `INT32_MAX`, `SIZE_MAX`, and
    implementation-defined widths such as `int24_t`). An invalid value is **rejected outright**,
    never silently sanitized/rewritten.
  - Combining `--c-symbol` with `--table=all` is rejected (every table would collide on the same
    symbol name).
  - Combining `--c-symbol` with any format other than `c` is rejected explicitly (`--c-symbol
    requires --format=c`), not silently ignored.
  - All three of these checks run **before `RomLoader` touches `--rom`** and **before any output
    file is created** — a bad `--c-symbol` never partially writes output.
  - The count symbol always deterministically derives from whichever data symbol is actually
    emitted: `<symbol>Count`. The row **type** name (`struct FEBuilder_<StructName>`) is derived from
    the struct metadata name and is **not** affected by `--c-symbol` either way.

## GNU11 / devkitARM requirement

The emitted translation unit is plain GNU11 C, meant to compile as-is under **devkitARM's**
`arm-none-eabi-gcc` (or host `gcc`, for a portable smoke build) — **not** strict ISO `-std=c11`:

- `#include <stdint.h>` and fixed-width storage only: `uint8_t`/`uint16_t`/`uint32_t`. Pointers stay
  **raw `uint32_t`** GBA addresses in this slice — no relocation or named-symbol resolution (see
  [Honest exclusions](#honest-exclusions)).
- The row struct is `struct __attribute__((packed)) FEBuilder_<StructName> { ... };` — packed so
  member offsets exactly match the ROM's byte layout regardless of natural C alignment.
- The exported array object is separately `__attribute__((aligned(4)))` — this controls the table's
  base address alignment; it does not change the (already packed) row stride.
- `_Static_assert(sizeof(struct FEBuilder_<StructName>) == <resolved stride>, "...");` — a
  compile-time proof that every declared/gap/trailing/overlap member really does add up to the exact
  runtime entry size.
- Required compiler flags: **`-std=gnu11 -Wall -Werror`** (non-pedantic — `-Wpedantic`/`-pedantic` is
  intentionally not required or assumed; GNU extensions like `__attribute__` and zero-length arrays
  are used deliberately and would themselves warn under strict `-Wpedantic`).

## Byte preservation

The formatter is raw-byte-backed: a shared row-extraction seam (`ExportTableRows`) reads, in one
pass, both the typed field values (used by TSV/CSV/EA/JSON, unchanged) **and** the exact raw bytes
for the table's resolved runtime entry stride (`table.GetDataSize(rom)` — which can be **larger**
than what the shared struct-metadata file declares; e.g. FE7U's `map_settings` runtime stride is 152
bytes against the shared 148-byte metadata file). The two views can never drift because they come
from the same address in the same loop iteration. From that raw+typed pair, every byte of the
resolved stride is covered by **exactly one** of:

- **A plain typed member** — a metadata field that doesn't overlap any other field.
- **A named gap member** (`uint8_t _gap0[N];`, `_gap1[N]`, …) — an interior byte range the struct
  metadata doesn't cover at all, filled from the raw ROM bytes verbatim (unknown/dynamic data is
  preserved, not zeroed or dropped).
- **A named trailing member** (`uint8_t _trailing[N];`) — the runtime-stride bytes beyond the
  furthest declared field end (exactly the FE7U `map_settings` case above), also raw ROM bytes.
- **One arm of an anonymous union inside the packed row** — for a **connected group of overlapping fields**
  (contained, like FE6 `map_settings`' `BGM1` word aliasing its second byte as `Field15`; or
  crossing, two fields whose byte ranges partially intersect without one containing the other).
  Each field in the group gets a uniquely named, packed, correctly-offset promoted view
  (`struct __attribute__((packed)) { uint8_t _pad[N]; uint16_t BGM1; } as_BGM1;`, with a synthetic
  `_pad` member only when the field doesn't start at the group's base offset) **plus exactly one raw
  byte-array arm** (`uint8_t _overlap0_raw[N];`) covering the whole group. Row initializers set
  **only** that raw arm — never a second member of the same union — so every alias is exposed for
  reading while the bytes are written exactly once, unambiguously.

No row is ever fabricated: if a row's raw byte length doesn't match the resolved stride, if the
resolved stride is smaller than the furthest declared field end, if two members would collide on the
same sanitized C identifier, or if a field's value can't be strictly parsed and width-checked, the
whole export throws — a clear, fatal error naming the row/field/table, never a silently-wrong C file.

## Deterministic naming

| Symbol | Default | With `--c-symbol=<name>` |
|---|---|---|
| Row type | `struct FEBuilder_<StructName>` | unchanged |
| Data array | `gFEBuilder_<table>` | `<name>` |
| Count | `gFEBuilder_<table>Count` | `<name>Count` |

Row comments use a **full-width**, ordinal-based designator — `[0x000]`, `[0x001]`, … — derived from
each row's position in the export, **never** the byte-truncatable `_Index` column's own (variable-
width) hex prefix. The width grows as needed (a 300-row table's last row renders `[0x12B]`, not a
byte-truncated alias of `[0x2B]`), so no two rows ever share a designator regardless of table size.
The (separately comment-escaped) decoded `_Index` label is appended after the designator for human
readability, e.g. `/* [0x001] 0x01 Iron Sword */`.

## Zero-row contract

A table with zero ROM-resolved rows — most commonly a version-specific table exported from a ROM
version that doesn't have it (e.g. an FE8-only table against an FE6 ROM) — still emits a **real,
valid GNU C data symbol**, not an omitted file or a fabricated row:

```c
const struct FEBuilder_Example gFEBuilder_example[0] __attribute__((aligned(4)));
const uint32_t gFEBuilder_exampleCount = 0;
```

This is the GNU zero-length-array extension: a real, stable, correctly-typed symbol with zero
storage and **no initializer** (a nonzero-row export emits the same array-symbol shape, plus an
exact `= { ... }` initializer and count). **Consumers must gate every access on the count symbol**
before indexing the array — like any zero-length array, indexing `gFEBuilder_example[0]` when
`gFEBuilder_exampleCount == 0` is undefined behavior, not a safe no-op.

## Safety / robustness

- **`_Index` never becomes a field.** It is only ever used to build the row's ordinal comment text,
  and that text is passed through a strict allow-list escape: every character outside printable
  ASCII, plus `*`, `/`, and `\` specifically, becomes `_`. This removes CR/LF/control bytes (can't
  split a single-line comment), removes every `*`/`/` (can't ever form a `*/` early terminator), and
  removes every backslash (can't trigger C's phase-2 backslash-newline line-splicing, which happens
  *before* comments are stripped and would otherwise let a trailing `\` merge the comment into the
  next source line).
- **Identifiers are deterministic and collision-checked.** ROM-metadata-derived names (struct/table/
  field names) are sanitized deterministically — invalid characters become `_`, a leading digit gets
  a `_` prefix, implementation-reserved `__*`/`_[A-Z]*` prefixes get a `field` prefix, and a C/GNU
  reserved keyword gets a trailing `_` — and every name actually emitted in a row struct's flat
  namespace (fields, union views, raw arms, gap/trailing members — GNU anonymous
  unions/structs promote all of them into one scope) is tracked; a post-sanitization collision is
  **rejected outright**, never silently renamed further. A **user-supplied** `--c-symbol` is held to
  a stricter, different standard: it is never sanitized at all — file-scope-safe and non-conflicting
  with the generated `<stdint.h>` prologue as given, or rejected.
  (`FEBuilderGBA.Core.Tests/StructExportCDataFormatTests.cs` covers keyword/leading-digit/invalid-
  character sanitization and forced collisions for both cases.)
- **Numeric values are strictly re-parsed and width-checked**, not copied as raw tokens: every field
  value goes through the same strict `0x`/`$`/decimal parsing and byte/word/dword/pointer width
  check JSON import uses, then is re-emitted as a fresh hex literal. A malformed or overflowing value
  fails the export instead of reaching the generated C source.
- **UTF-8 without a BOM.** A leading byte-order mark is not valid at the start of a C translation
  unit for every compiler, so the writer matches the existing `.h`/`.nmm` no-BOM convention.

## Export-only: `--import-data` and `--data-roundtrip` are unaffected

- `--import-data` continues to accept only `tsv`/`json` (auto-detected from a `.json` `--in`, or
  `--format=json` explicit); `c` was never added to that allow-list, so it is rejected the same way
  any other unsupported `--import-data --format` value already was. There is no C→ROM import path.
- `--data-roundtrip` verifies **direct struct read/write stability** on a temp ROM copy (read table
  values into memory, write the same values back, re-read, compare) — it does **not** serialize
  through TSV/CSV/EA/JSON, and it **does not exercise `--format=c` at all**. A clean
  `--data-roundtrip` result says nothing about whether the generated C compiles, still less that it
  round-trips back into ROM bytes. Treat it as exactly what it is: a struct-field read/write
  stability check, independent of every export format.

## Worked example

```bash
# 1. Export the "units" table as a GNU11 C translation unit with a custom data symbol.
FEBuilderGBA.CLI --export-data --rom=rom.gba --table=units --format=c --c-symbol=gUnitData --out=units.c

# 2. Compile it (devkitARM's arm-none-eabi-gcc, or host gcc for a portable smoke build).
arm-none-eabi-gcc -std=gnu11 -Wall -Werror -c units.c -o units.o
```

`units.c` declares `struct FEBuilder_Unit_FE8` (or the FE6/FE7 equivalent), the data array
`gUnitData[N]` (or the zero-row form if `N == 0`), and `gUnitDataCount`. What happens to `units.o`
next — linking it into an external decomp project's build (a Makefile rule, a linker script, symbol
resolution against the rest of that project's sources) — is entirely **consumer-owned**.
FEBuilderGBA emits one self-contained, valid translation unit per table; it does not manage or ship
a decomp build.

## Honest exclusions

- **No shipped decomp.** This issue does not add or bundle a disassembly tree.
- **No whole-mod orchestration.** Turning an entire mod's worth of tables (plus assets, code patches,
  event scripts, …) into one coherent source tree is issue
  [#1935](https://github.com/laqieer/FEBuilderGBA/issues/1935)'s job, not this one's. `--format=c`
  is deliberately **per-table**, composable with whatever orchestration #1935 eventually builds.
- **No C→ROM round-trip guarantee.** Issue [#1936](https://github.com/laqieer/FEBuilderGBA/issues/1936)
  covers whether/how a decomp's C sources can be proven to reproduce the original ROM bytes; this
  issue only proves ROM→C is byte-exact and compiles, not the reverse direction.
  `--data-roundtrip` (see above) is not that proof either.
- **No named-pointer relocation or symbol-map consumption.** Pointer-typed fields are emitted as raw
  `uint32_t` GBA addresses, exactly as read from ROM — there is no mechanism yet to rewrite a pointer
  field as `&SomeOtherTable[i]` or to consume an external symbol map. That is a deliberately deferred,
  separate contract for a future issue.

## Validation evidence (read this before trusting a "passes" claim)

- **Core layout/formatter tests** (`FEBuilderGBA.Core.Tests/StructExportCDataFormatTests.cs`) need no
  compiler and run on any `dotnet test` host, including local Windows dev machines: mixed-width
  fields + stride assertion, gap/trailing raw initialization, contained/crossing/transitive overlap
  grouping (incl. the real FE6 `map_settings` shape), a byte-coverage/reconstruction oracle, invalid
  stride/raw-length rejection, a 300-row designator-uniqueness fixture, packed/aligned output,
  zero/nonzero-row contracts, comment neutralization, identifier sanitization/collision/keyword
  handling, malformed/overflow numeric rejection, and the `--c-symbol` override/validator.
- **The Core compiler smoke** (`FormatCData_CompilerSmoke_FiveRepresentativeShapesCompileCleanly`)
  actually compiles the formatter's real output (never a hand-written lookalike) for five
  representative shapes (FE8-style mixed widths, FE6-style contained overlap, an arbitrary crossing
  overlap, a >255-row table, and a zero-row table) with `-std=gnu11 -Wall -Werror -c`. It **prefers an
  already-installed `arm-none-eabi-gcc`, else host `gcc`**, and downloads/installs neither. **A local
  Windows dev machine without either compiler on `PATH` will report this test as a clean SKIP, not a
  pass** — that is expected, not a failure, and it means the generated GNU11 source has **not**
  actually been compiled on that machine. **Ubuntu CI, where a compiler is present, is the
  authoritative compile gate.** Do not describe local Windows results as "GNU compilation passed";
  only cite an actual Ubuntu CI run for that claim.
- **CLI E2E** (`FEBuilderGBA.E2ETests/Tests/CliDataCExportE2ETests.cs`, ROM-gated, skips cleanly
  without local ROMs) exercises the real `FEBuilderGBA.CLI` binary end-to-end: FE8U `items` shape +
  no-BOM, FE6 `map_settings`' real overlap, a deterministically-extended 257-row `portraits` fixture
  (proving `[0x100]` designators are never byte-truncated, without depending on any stock ROM
  happening to have that many real rows), `--table=all --format=c` across all five ROM
  variants (every expected table present, at least one zero-row/version-absent table observed),
  a valid `--c-symbol` override, and every pre-ROM-load / pre-output-file `--c-symbol`/`--format`
  rejection path.

## Cross-links

- [#1939](https://github.com/laqieer/FEBuilderGBA/issues/1939) — this issue.
- [#1937](https://github.com/laqieer/FEBuilderGBA/issues/1937) /
  [`febuilder-cli-as-llm-backend.md`](febuilder-cli-as-llm-backend.md) — the JSON agent/LLM-backend
  sibling format this issue's `--format=c` sits alongside.
- [#1935](https://github.com/laqieer/FEBuilderGBA/issues/1935) — whole-mod emit-recipe orchestration
  (this issue's per-table `--format=c` is one ingredient, not the whole recipe).
- [#1936](https://github.com/laqieer/FEBuilderGBA/issues/1936) — C→ROM round-trip guarantee (not
  delivered here).
- [`agent-parity.md`](agent-parity.md) — the GUI↔CLI↔Core parity matrix; struct export formats
  (§8, "Tools") lists `c` alongside `tsv`/`csv`/`ea`/`json`.
- [`cli-reference.md`](cli-reference.md) — the full `--export-data`/`--c-symbol` flag reference.
