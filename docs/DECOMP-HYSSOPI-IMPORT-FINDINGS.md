# Decomp maps: Hyssopi map import to source `.mar` — investigation findings

> **Outcome: documented safe failure (no importer this pass).**
> A safe *automatic* converter from a Hyssopi map export into a FEBuilder/decomp
> `.mar` map layout **cannot be built today** because the critical tile-hash → GBA
> Fire Emblem `u16` metatile/config-index crosswalk is undocumented and
> game-version-dependent. Guessing it would silently produce wrong maps, which
> [#1361](https://github.com/laqieer/FEBuilderGBA/issues/1361) explicitly forbids.
> Its acceptance criteria permit exactly this resolution: *"If a safe
> hash-to-metatile crosswalk cannot be established, the result is a documented safe
> failure rather than a claimed importer."*

This page records what was verified so a future narrow bridge has a precise starting
point. Each fact below is labeled **[confirmed]** (read from public Hyssopi source /
FEBuilder source) or **[inferred]** (deduced from implementation, not a documented
contract).

## 1. What Hyssopi is

The **Hyssopi Fire Emblem Tile Map Editor** is a browser-based map editor for the
GBA Fire Emblem games:

- Repository: <https://github.com/Hyssopi/Fire-Emblem-Tile-Map-Editor>
- Live app: <https://hyssopi.github.io/Fire-Emblem-Tile-Map-Editor/>

The `fireemblem8u` wiki recommends it as an FE-native map editor while noting that
FEBuilder's own `.mar` already drops into the decomp pipeline (`Porymap-for-FE8.md`,
`Missing-essentials-and-roadmap.md`).

## 2. Hyssopi's export shape (the candidate input)

- **[confirmed]** Hyssopi exports/imports map files as **JSON** containing a 2D grid
  of **opaque tile *hashes*** (export action `exportAsTileHashesButtonId` /
  `exportMapAsTileHashes`; load via `loadMapJson`), organized by game and chapter.
- **[confirmed]** Each tile hash resolves against Hyssopi's own
  `tiles/tileReferences.json`, whose entries expose **only** `tileHash`, a `group`
  label, neighbor lists (north/east/south/west), and `originFilePaths` (image asset
  paths). There is **no** field that carries a GBA FE metatile / map-config `u16`
  index, nor a ROM address.
- **[inferred]** The precise top-level JSON object schema of an exported map
  (key names, nesting, how width/height/chapter are represented) is **not published
  as a stable contract** — it is only discoverable by reading the editor's
  implementation, which can change without notice.

So the Hyssopi export answers *"what does this tile look like / what are its valid
neighbors"* but **not** *"what GBA map-layout `u16` does this tile correspond to in
FE6 / FE7 / FE8."*

## 3. The `.mar` target contract (what FEBuilder/decomp needs)

FEBuilder's `.mar` map-layout path is fully documented and confirmed in
`FEBuilderGBA.Core/DecompAssetExportCore.cs` (`ExportMap` / `ImportMap` /
`ValidateAsset(MapLayout)`, added in [#1148](https://github.com/laqieer/FEBuilderGBA/issues/1148)
/ PR [#1346](https://github.com/laqieer/FEBuilderGBA/pull/1346)):

- **[confirmed]** Body: a stream of little-endian `u16` entries, one per map cell.
  Each `.mar` entry is the raw tilemap `u16` shifted left by 3: `marTile = rawTile << 3`.
- **[confirmed]** Sidecar `.mar.json`:
  `{ "width": W, "height": H, "srcAddr": "0x…", "format": "febuilder-mar-u16-shl3" }`.
- **[confirmed]** Lossless boundary: each **raw** tile index must be `< 0x2000`
  (bits 13–15 must be clear, else the `<<3` truncates palette/flag bits and the
  entry is **rejected**). Import reverses the encoding with `rawTile = marU16 >> 3`.

Producing a valid `.mar` therefore requires knowing each cell's **raw GBA `u16`
tile index** (`< 0x2000`) — exactly the value Hyssopi does **not** carry.

## 4. The blocker

| Need (for a safe converter) | Available from Hyssopi today |
| --- | --- |
| Stable, documented map-JSON schema | **No** — implementation-only, may change |
| Per-cell GBA FE `u16` tile/config index | **No** — only an opaque hash + group/neighbor metadata |
| Game/tileset (version) context for that index | **No** — FE6/7/8 tilesets differ; not encoded as a crosswalk |

A converter built now would have to **invent or infer** a hash → GBA-`u16`-index
mapping. Because the same Hyssopi hash means different raw indices across games and
tilesets, any inferred mapping would be a guess. A wrong guess does not fail loudly —
it yields a **structurally valid but semantically wrong** `.mar`, i.e. a corrupted
map that still imports. That is precisely the "silently guess source ownership / tile
IDs" failure mode the issue's non-goals and acceptance criteria prohibit.

## 5. Prerequisites for a future narrow bridge

A safe, conservative converter becomes possible only once **both** of these exist:

1. **A documented/stable Hyssopi map-JSON schema** (or a frozen, versioned export
   format) so the grid geometry and tile-hash field can be parsed without guessing.
2. **A hash → GBA-`u16`-index resolution source**, supplied explicitly rather than
   inferred. The realistic form is a **user-supplied mapping file** derived from the
   project's own `tileReferences` / tileset, **plus an explicit target game/version
   (tileset) selection**. Every resolved raw index must land `< 0x2000` to satisfy
   the `.mar` lossless boundary.

Any such bridge must **fail closed**: reject (with actionable diagnostics, never a
silent default) any tile hash it cannot resolve, any tileset/game-context mismatch,
or any index `≥ 0x2000`. It must be **read-only / never mutate the preview ROM**, and
write outputs only to user-selected paths under the opened decomp source tree — the
same guarantees the existing `.mar` import path provides.

## 6. Interim recommendation

Until the prerequisites above are met, the supported authoring path into the decomp
pipeline is **FEBuilder's own `.mar` flow**, which is already validated and
round-trip-verified:

- Export a chapter map layout to `.mar` + sidecar via `--export-asset --kind=map`.
- Re-import / verify via the `.mar` import path (`--import-asset` /
  `--roundtrip-asset`), backed by `DecompAssetExportCore` and
  `--validate-asset --kind=map`.

This is the route the wiki (`Porymap-for-FE8.md`) already points decomp users to:
FEBuilder `.mar` "drops into the decomp pipeline" today, whereas a Hyssopi bridge
remains blocked on the undocumented crosswalk.

## See also

- [#1361](https://github.com/laqieer/FEBuilderGBA/issues/1361) — this investigation
- [#1148](https://github.com/laqieer/FEBuilderGBA/issues/1148) /
  [PR #1346](https://github.com/laqieer/FEBuilderGBA/pull/1346) — source-backed
  `.mar` map-layout import + round-trip-verify
- [#1360](https://github.com/laqieer/FEBuilderGBA/issues/1360) /
  [PR #1365](https://github.com/laqieer/FEBuilderGBA/pull/1365) — remaining LZ77 /
  pointer-heavy map asset classes (tracked separately; not this layout bridge)
- [docs/DECOMP-FEATURE-INVENTORY.md](DECOMP-FEATURE-INVENTORY.md) — honest residuals
