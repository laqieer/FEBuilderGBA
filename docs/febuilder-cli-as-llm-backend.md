# FEBuilderGBA.CLI as an LLM ROM-generator format-knowledge backend

Tracks issue [#1937](https://github.com/laqieer/FEBuilderGBA/issues/1937).

## Motivation

**Prior art:** [i-am-neon/fe-infinity](https://github.com/i-am-neon/fe-infinity) is a Deno/TypeScript
app that uses an LLM (OpenAI + `zod`-structured output) to generate a whole FE8 game from a one-line
prompt, then builds it via the **buildfile method** (Event Assembler under Wine onto a clean ROM,
adapted from [Legends of Avenir](https://github.com/Snakey11/Legends-of-Avenir)). It was investigated
in discussion [#1930](https://github.com/laqieer/FEBuilderGBA/discussions/1930), where it turned out to
be prior art already adjacent to production use: the maintainer's own FEHRR project already shells out
to `FEBuilderGBA.exe` headlessly as a map/TSA converter.

**The gap.** fe-infinity is **pure buildfile, with no FEBuilder in the loop** — so it hand-reimplements
ROM-format knowledge in TypeScript that `FEBuilderGBA.Core` already encodes for all five ROM variants
(FE6, FE7J/U, FE8J/U), with `--lint` and round-trip validation on top. Concretely, it re-derives:

- `class-name-to-rom-class-name.ts` — mapping a class name to its ROM class ID.
- `generate-character-stats.ts`, `generate-affinity.ts`, `get-weapon-rank.ts`,
  `getWeaponTypesForClass.ts` — unit/class/weapon-rank data.
- `character-table-csv-headers.ts` — a hand-maintained CSV column layout.
- Unit-placement coordinate logic and `map-processing/`.

This document maps each of those items to what `FEBuilderGBA.CLI` already provides, is honest about
what it does **not** provide yet, and gives a literal, reproducible worked example an LLM-driven
generator can follow instead of re-deriving format knowledge.

**Out of scope** (per the issue): writing the LLM generator itself, shipping a base ROM, or
implementing the gaps identified below — those are candidate follow-up issues, not delivered here.

## Mapping table: what fe-infinity re-derives → what `FEBuilderGBA.CLI` already provides

| fe-infinity re-derivation | FEBuilderGBA.CLI equivalent | Status |
|---|---|---|
| `character-table-csv-headers.ts` (hand-maintained CSV columns) | `--list-tables` (enumerate the 40 registered tables) + `--export-data --table=<name> --format=<tsv\|csv\|ea\|json>` (schema *is* the column/key set — no hand-maintenance needed) | **Existing verb** |
| `generate-character-stats.ts` (base stats, growths) | `--export-data --table=units` — real fields `BaseHP`/`BasePow`/`BaseSkl`/`BaseSpd`/`BaseDef`/`BaseRes`/`BaseLck`/`BaseCon` (`0x0C`–`0x13`) and `GrowthHP`/`GrowthPow`/… (`0x1C`–`0x22`), verified against both unit ViewModels and `config/data/struct_unit_*.txt` | **Existing verb** |
| `get-weapon-rank.ts` | `--export-data --table=units` — real fields `SwordRank`/`LanceRank`/`AxeRank`/`BowRank`/`StaffRank`/`AnimaRank`/`LightRank`/`DarkRank` (`struct_unit_fe78.txt`) | **Existing verb** |
| Item/weapon stats (`Might`, `Hit`, `Weight`, `Crit`, ranges, `WeaponRank`, …) | `--export-data --table=items` (`struct_item_fe78.txt`) | **Existing verb** |
| Multi-version support (FE6/7/8, J/U) | Automatic ROM version detection, or `--force-version=<FE6\|FE7J\|FE7U\|FE8J\|FE8U>` | **Existing, free** |
| The correctness gate | `--lint` (structural/pointer/text-id validity), `--data-roundtrip` (struct read/write stability), and `--translate-roundtrip` (text export/import losslessness) — see the [precision note](#correctness-gate-precision-lint-vs-round-trip) below; **none checks gameplay/semantic validity** | **Existing verb, precision caveat** |
| `class-name-to-rom-class-name.ts` (name → id) | **Gap.** `--resolve-names` is **id → name only** (`--kind=<unit\|class\|item\|song> --ids=<comma-list>`); the reverse (name → id) has no CLI verb today. | **Needed verb** |
| `generate-affinity.ts` (decoded affinity) | `--export-data --table=units` exposes the direct `Affinity` byte at `0x09` for FE6/7/8. It exports the raw hex value; converting that value to a human-readable affinity name still requires a decoder. | **Existing raw field; decoded-name gap** |
| `map-processing/` (terrain-aware coordinate logic) | **Gap.** No terrain-query CLI verb exists; `docs/agent-parity.md` rates map *tile layout* as **Partial** (chapter/map *settings* are `--export-data --table=map_settings`-covered, but terrain-aware coordinate processing is not). | **Needed verb** |
| Unit-placement coordinates | **Different surface, not struct data.** Chapter unit spawns live in event scripts, not a struct table — use `--disasm-event` / `--compile-event` (Event Assembler), not `--export-data`. | **Different surface** |

Every verb cited above is documented in [`cli-reference.md`](cli-reference.md); re-verify against that
file if this doc and the CLI ever drift.

## Stable schema commitment

`--export-data --format=json` (added by #1937) is the generator-facing, machine-readable format:

- **Shape:** a JSON array of objects — one object per row.
- **Keys:** the public key for the row identifier is `Index` (never the internal `_Index` used inside
  `FEBuilderGBA.Core`), followed by one key per struct field, in the struct's declared field order.
  These are exactly the same column headers `--format=tsv`/`--format=csv` already use.
- **Values:** every value is a JSON **string**, holding the identical hex/text representation TSV/CSV
  use (e.g. `"0x0A"`, `"0x08123456"`) — never a JSON number/boolean. Unlike TSV import's forgiving
  `Index` parser (which silently aliases unparsable text to row 0 via `U.atoi0x`'s truncating
  fallback), JSON's `Index` is **strictly** validated: it must be a `0x`-hex, `$`-hex, or plain
  decimal token (optionally followed by a space and a label), in range for a 32-bit value, and
  non-negative — a garbage, overflowing, or negative `Index` is rejected outright rather than
  quietly mutating the wrong row.
- **Additive, backward-compatible:** `json` is a new, opt-in `--format=` value, and the existing
  TSV/CSV/EA syntax is unchanged. All formats now preserve the complete row index rather than
  truncating it to one byte (`Index` for row 256 is `0x0100`, never `0x00`). `--import-data` accepts
  JSON either explicitly (`--format=json`) or automatically when `--in` has a `.json` extension;
  TSV import behavior is unchanged. An explicit `--format` value outside the supported set
  (`tsv`/`csv`/`ea`/`json` for export, `tsv`/`json` for import) is rejected with an actionable error
  instead of silently falling back to TSV.
- **Backstop:** this shape is covered by regression tests — `FEBuilderGBA.Core.Tests/StructExportFormatTests.cs`
  (`FormatJSON`/`ParseJSON`/`ValidateJSONEntries` unit tests, including the strict `Index` parsing,
  duplicate-property rejection, unknown-field rejection, per-field numeric strictness/width
  range-checking, duplicate-row-index rejection, and out-of-range-`Index` rejection below) and
  `FEBuilderGBA.E2ETests/Tests/CliDataJsonE2ETests.cs` (ROM-gated export/import/round-trip E2E
  tests, plus non-ROM `--format` validation tests).
- **Validation before mutation:** `--import-data` with JSON input runs two validation passes, both
  **before** writing anything to the ROM. First, a shape check: root must be an array, every row an
  object, every property value a JSON string, and no row may repeat the same property name (including
  `Index`) twice. Second, a struct/count-aware semantic preflight that catches what the shape check
  can't: every non-`Index` property name must be a known field of the target table's struct (a typo'd
  field name, e.g. `Wieght` instead of `Weight`, is rejected instead of being silently ignored); every
  field value must strictly parse as a complete `0x`-hex/`$`-hex/plain-decimal token — no trailing
  tokens, no bare prefixes, no negatives, no overflow (e.g. `"banana"`, `"-1"`, `"0x"`, `"0x0A extra"`)
  — and must fit the field's byte/word/dword/pointer width, or it is rejected instead of the
  permissive `U.atoi0x` silently coercing it to `0` and mutating the ROM; accepted values are
  normalized to a canonical lowercase-`0x` hexadecimal form (including accepted decimal input) so
  the full unsigned field range parses correctly downstream; no two rows may target the same `Index`; and every `Index` must be
  within `[0, entryCount)` for the resolved table instead of relying on the writer's silent per-row
  skip. Any violation (shape or semantic) fails with a specific, actionable error — naming the row
  number and, where applicable, the offending property — and leaves the ROM byte-for-byte unchanged.

## Correctness-gate precision: `--lint` vs. `--data-roundtrip`

An LLM generator needs to know exactly what these gates do and do **not** guarantee:

- **`--lint`** runs `FELintScanner` — it checks **structural, pointer, and text-ID** validity (e.g.
  dangling pointers, malformed tables, missing text references). It does **not** evaluate gameplay
  balance, and a ROM with absurd stats (e.g. a level-1 unit with 255 HP) can still pass `--lint`
  cleanly.
- **`--data-roundtrip`** verifies **struct read/write stability** on a temporary ROM copy: it reads
  each table into in-memory field dictionaries, writes those same dictionaries back, re-reads the
  table, and compares the values. It does **not** pass through TSV, CSV, EA, or JSON, so it does not
  prove that a serialization format is lossless; the JSON parser/writer contract is exercised by the
  JSON Core and CLI E2E tests instead. `--translate-roundtrip` separately exercises the text
  export/import path. Neither gate verifies that the *values* the generator chose are
  gameplay-sensible.
  (`--import-data`'s JSON-only semantic preflight, described above, does reject a value that
  overflows its field's byte/word/dword/pointer width — but that is a numeric-width check, not a
  gameplay/balance judgment; a byte-width stat like `0xFF` HP is numerically valid and will still
  pass.)

Neither gate is a semantic/gameplay validator. An LLM generator should treat them as "did my data
survive the round-trip and does it still look like a valid ROM," not "is this a balanced game."

## Worked example: LLM emits JSON → `--import-data` → correctness gates

This is the literal, reproducible sequence an LLM-driven generator (or a human) can follow to edit a
struct table via JSON, end to end:

```bash
# 1. See the schema by exporting the table you want to edit.
FEBuilderGBA.CLI --export-data --rom=rom.gba --table=units --format=json --out=units.json

# 2. Edit units.json — e.g. the LLM emits a modified array of the same shape:
#    [{"Index": "0x01 Eirika", "Affinity": "0x02", "BasePow": "0x05", ...}, ...]
#    Every value must stay a JSON string in the same hex/text form as the export.
#    Unit metadata maps Affinity to byte 0x09 and BasePow to byte 0x0D.

# 3. Import the edited JSON back into the ROM.
#    Format is auto-detected from the .json extension (or pass --format=json explicitly).
FEBuilderGBA.CLI --import-data --rom=rom.gba --table=units --in=units.json

# 4. Run the correctness gates before trusting the result.
FEBuilderGBA.CLI --lint --rom=rom.gba
FEBuilderGBA.CLI --data-roundtrip --rom=rom.gba --table=units
```

If step 2 produces a malformed document (a number instead of a string, a missing `Index`, or a root
that isn't an array) or a *semantically* invalid one (an unknown/typo'd field name, a garbage/negative/
overflowing field value, a duplicate row `Index`, or an `Index` outside the table's entry count), step
3 fails with a specific error and the ROM is left untouched — the generator can fix the JSON and retry
without having corrupted anything.

## Relationship to other agent-native issues

- Discussion [#1930](https://github.com/laqieer/FEBuilderGBA/discussions/1930) — the fe-infinity
  investigation this issue grew out of.
- [#1935](https://github.com/laqieer/FEBuilderGBA/issues/1935) — emit-recipe / export-to-buildfile
  (expressing a mod as source deltas from a clean ROM).
- [#1933](https://github.com/laqieer/FEBuilderGBA/issues/1933) — agent-harness coverage of the
  remaining CLI verbs.
- [#1931](https://github.com/laqieer/FEBuilderGBA/issues/1931) — the GUI↔CLI parity audit
  ([`agent-parity.md`](agent-parity.md)) this doc's mapping table cross-checks against.
- [#1938](https://github.com/laqieer/FEBuilderGBA/issues/1938) — base-ROM/template policy for
  agent-generated content (fe-infinity's Legends-of-Avenir base is exactly the case this covers).
- [#1939](https://github.com/laqieer/FEBuilderGBA/issues/1939) — emit-recipe targeting decomp-C data
  headers, not just an EA buildfile.
- [#1941](https://github.com/laqieer/FEBuilderGBA/issues/1941) — the in-tree converter `--json`
  contracts (`--convertmap1picture`, `--decreasecolor`) that established the `System.Text.Json`
  pattern this issue's `--export-data --format=json` reuses.
