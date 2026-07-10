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
| `generate-character-stats.ts` (base stats, growths) | `--export-data --table=units` — real fields `BaseHP`/`BasePow`/`BaseSkl`/`BaseSpd`/`BaseDef`/`BaseRes`/`BaseLck`/`BaseCon` and `GrowthHP`/`GrowthPow`/… (verified against `config/data/struct_unit_fe78.txt`) | **Existing verb** |
| `get-weapon-rank.ts` | `--export-data --table=units` — real fields `SwordRank`/`LanceRank`/`AxeRank`/`BowRank`/`StaffRank`/`AnimaRank`/`LightRank`/`DarkRank` (`struct_unit_fe78.txt`) | **Existing verb** |
| Item/weapon stats (`Might`, `Hit`, `Weight`, `Crit`, ranges, `WeaponRank`, …) | `--export-data --table=items` (`struct_item_fe78.txt`) | **Existing verb** |
| Multi-version support (FE6/7/8, J/U) | Automatic ROM version detection, or `--force-version=<FE6\|FE7J\|FE7U\|FE8J\|FE8U>` | **Existing, free** |
| The correctness gate | `--lint` (structural/pointer/text-id validity) and `--data-roundtrip` / `--translate-roundtrip` (export/import losslessness) — see the [precision note](#correctness-gate-precision-lint-vs-round-trip) below; **neither checks gameplay/semantic validity** | **Existing verb, precision caveat** |
| `class-name-to-rom-class-name.ts` (name → id) | **Gap.** `--resolve-names` is **id → name only** (`--kind=<unit\|class\|item\|song> --ids=<comma-list>`); the reverse (name → id) has no CLI verb today. | **Needed verb** |
| `generate-affinity.ts` (decoded affinity) | **Gap.** The `units` struct has no named `Affinity` column — affinity is packed into the opaque `Ability1`–`Ability4` bitfields (`struct_unit_fe78.txt`), which `--export-data` exports as raw hex, not a decoded value. | **Needed: undecoded-field gap** |
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
- **Additive, backward-compatible:** `tsv`/`csv`/`ea` output is byte-for-byte unchanged; `json` is a
  new, opt-in `--format=` value. `--import-data` accepts JSON either explicitly (`--format=json`) or
  automatically when `--in` has a `.json` extension; TSV import behavior is unchanged. An explicit
  `--format` value outside the supported set (`tsv`/`csv`/`ea`/`json` for export, `tsv`/`json` for
  import) is rejected with an actionable error instead of silently falling back to TSV.
- **Backstop:** this shape is covered by regression tests — `FEBuilderGBA.Core.Tests/StructExportFormatTests.cs`
  (`FormatJSON`/`ParseJSON` unit tests, including the strict `Index` parsing and duplicate-property
  rejection below) and `FEBuilderGBA.E2ETests/Tests/CliDataJsonE2ETests.cs` (ROM-gated export/import/
  round-trip E2E tests, plus non-ROM `--format` validation tests).
- **Validation before mutation:** `--import-data` with JSON input validates the **entire** document —
  root must be an array, every row an object, every property value a JSON string, and no row may
  repeat the same property name (including `Index`) twice — before writing anything to the ROM. A
  malformed document (a stray number/boolean/null/array/object value, a non-array root, a duplicated
  property, or an unparsable `Index`) fails with a specific, actionable error and leaves the ROM
  byte-for-byte unchanged.

## Correctness-gate precision: `--lint` vs. `--data-roundtrip`

An LLM generator needs to know exactly what these gates do and do **not** guarantee:

- **`--lint`** runs `FELintScanner` — it checks **structural, pointer, and text-ID** validity (e.g.
  dangling pointers, malformed tables, missing text references). It does **not** evaluate gameplay
  balance, and a ROM with absurd stats (e.g. a level-1 unit with 255 HP) can still pass `--lint`
  cleanly.
- **`--data-roundtrip`** (and `--translate-roundtrip` for text) verifies **export/import
  losslessness**: export → import → re-export → diff, on a temporary copy of the ROM. It proves the
  serialization format doesn't lose or corrupt data; it does **not** verify that the *values* the
  generator chose are sensible or in-range for the field.

Neither gate is a semantic/gameplay validator. An LLM generator should treat them as "did my data
survive the round-trip and does it still look like a valid ROM," not "is this a balanced game."

## Worked example: LLM emits JSON → `--import-data` → correctness gates

This is the literal, reproducible sequence an LLM-driven generator (or a human) can follow to edit a
struct table via JSON, end to end:

```bash
# 1. See the schema by exporting the table you want to edit.
FEBuilderGBA.CLI --export-data --rom=rom.gba --table=units --format=json --out=units.json

# 2. Edit units.json — e.g. the LLM emits a modified array of the same shape:
#    [{"Index": "0x01 Eirika", "BasePow": "0x05", ...}, ...]
#    Every value must stay a JSON string in the same hex/text form as the export.

# 3. Import the edited JSON back into the ROM.
#    Format is auto-detected from the .json extension (or pass --format=json explicitly).
FEBuilderGBA.CLI --import-data --rom=rom.gba --table=units --in=units.json

# 4. Run the correctness gates before trusting the result.
FEBuilderGBA.CLI --lint --rom=rom.gba
FEBuilderGBA.CLI --data-roundtrip --rom=rom.gba --table=units
```

If step 2 produces a malformed document (a number instead of a string, a missing `Index`, or a root
that isn't an array), step 3 fails with a specific error and the ROM is left untouched — the generator
can fix the JSON and retry without having corrupted anything.

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
