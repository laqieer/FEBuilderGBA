# FEBuilderGBA as an In-Tree Asset Converter (decomp / homebrew builds)

**Purpose.** A decomp or homebrew GBA project builds from *editable source* (JSON, images, C), but the
engine consumes *in-game binary forms* (map tile data, TSA, quantized 4bpp graphics). Something has to
convert one to the other **as a build rule**. FEBuilderGBA already owns that format knowledge — and the
maintainer's **FEHRR** series already drives it headlessly. This doc documents FEBuilderGBA's convert
verbs as a **supported, stable, scriptable converter interface** (stable flags **+ machine-readable
`--json`**) so decomp/homebrew builds can depend on a real CLI contract.

Tracks [#1941](https://github.com/laqieer/FEBuilderGBA/issues/1941); spun out of discussion
[#1930](https://github.com/laqieer/FEBuilderGBA/discussions/1930).

> **This is the converter / build-rule view.** The sibling
> [febuilder-as-decomp-frontend.md](febuilder-as-decomp-frontend.md) ([#1940](https://github.com/laqieer/FEBuilderGBA/issues/1940))
> covers the **GUI-first interactive authoring** view of the same verbs (edit visually → export). Here we
> treat them as **unattended build-rule converters** with a stable machine contract.

## The insight: converters dissolve the "Hard" ratings

The FE decomp wikis rate maps/graphics as fiddly — but that reflects the decomp's **byte-match-vanilla**
constraint (assets tracked in their in-game binary form, no editable-source converter). A **hack built on
a decomp is under no such obligation**: pair it with **(1) in-tree converters** (editable form → in-game
blob) and **(2) friendlier engine structures**, and the gap dissolves. FEBuilderGBA is the ready-made
converter for (1).

## The converter interface

Two **ROM-free** convert verbs turn editable images into in-game blobs. Both now support a **`--json`**
flag that emits a single machine-readable result object to **stdout** (success *and* error), while the
default human-readable output is unchanged when `--json` is absent.

| Verb | Input | In-game output | `--json` result keys |
|---|---|---|---|
| `--convertmap1picture` | `--in=<map.png>` | Any of `--outImg=<tiles.bin>` (4bpp tiles), `--outTSA=<tsa.bin>` (LZ77 TSA), `--outPal=<palette.bin>` (matching GBA palette) | `command`, `ok`, `in`, `outImg`, `outTSA`, `outPal`, `outImgBytes`, `outTSABytes`, `outPalBytes`, `tiles`, `gridWidth`, `gridHeight` |
| `--decreasecolor` | `--in=<image.png>` (+ optional `--paletteno`, default 16; 2–256 with reserved transparency, or 1–256 with `--noReserve1stColor`) | `--out=<image.png>` (median-cut RGB palette reduction to ≤ `--paletteno` colors) | `command`, `ok`, `in`, `out`, `outBytes`, `paletteNo`, `colors`, `width`, `height` |

**Contract:**
- `--json` present → one JSON object on **stdout**; **`--json` absent → unchanged human output** (fully
  backward-compatible; existing scripts are unaffected).
- Success → `"ok": true` + the keys above; **failure → `{"command":…, "ok":false, "error":…}` on stdout**
  with the existing **non-zero exit code** (so a consumer can branch on exit code *or* on `ok`).
- Deterministic partial outputs: for `--convertmap1picture`, untaken `--outImg`/`--outTSA`/`--outPal`
  fields and byte counts are reported as `null`, never omitted.
- `--convertmap1picture` reserves palette index 0 for transparency and rejects images with more than
  15 opaque GBA colors or 1024 unique tiles, rather than masking palette/tile indices into corrupt output.
- Requested map outputs are staged and committed transactionally. Filesystem aliases cannot make one
  artifact overwrite another while returning `ok:true`; on failure, the prior output set is restored.

```bash
# Map image → tiles + TSA, machine-readable:
FEBuilderGBA.CLI --convertmap1picture --in=map.png --outImg=tiles.bin --outTSA=tsa.bin --outPal=palette.bin --json
# {"command":"convertmap1picture","ok":true,"in":"map.png","outImg":"tiles.bin","outTSA":"tsa.bin",
#  "outPal":"palette.bin","outImgBytes":4096,"outTSABytes":512,"outPalBytes":32,
#  "tiles":128,"gridWidth":30,"gridHeight":20}

# Reduce an image to a 16-color RGB palette:
FEBuilderGBA.CLI --decreasecolor --in=portrait.png --out=portrait_reduced.png --paletteno=16 --json
# {"command":"decreasecolor","ok":true,"in":"portrait.png","out":"portrait_reduced.png","outBytes":146,
#  "paletteNo":16,"colors":16,"width":16,"height":16}
```

## Worked example — FEHRR

The maintainer's **FEHRR** (a *Fire Emblem Heroes* remake rebuilt as a GBA game) already uses FEBuilderGBA
as its map/TSA converter. Its `asset/script/make_maps.py` converts datamined **JSON + map images** into
the in-game forms (map TSA, terrain, `gfx/map`) by shelling out to FEBuilderGBA. The upstream variable
name is reproduced verbatim (including its missing `l`):

```python
FEBuiderGBA = '..\\FEBuilderGBA\\FEBuilderGBA\\bin\\Debug\\FEBuilderGBA.exe --rom=baserom.gba'  # sic
```

This is production prior art for calling FEBuilderGBA from a build script, but it is **not** a drop-in
executable swap. The legacy WinForms command reconstructs `--outImg=gfx/map/<id>.png` as an encoded PNG.
The cross-platform CLI has a pre-existing, different contract: `--outImg` is always raw 4bpp tile bytes
(exactly `tiles * 32` bytes), regardless of the file extension.

A safe FEHRR migration therefore requires both:

1. Change `--outImg` to a binary-oriented path such as `gfx/map/<id>.4bpp`, pass
   `--outPal=gfx/map/<id>.pal`, and update the downstream build step to consume the paired raw tile and
   palette files. If that consumer still requires PNG, retain the legacy WinForms conversion until it
   is adapted.
2. Then invoke **`FEBuilderGBA.CLI`** (built/published, or `dotnet run`) with **`--json`**, and parse the
   structured result (`ok`, output paths, byte sizes, grid dimensions) instead of regex-scraping logs.

For example, after the downstream consumer accepts raw 4bpp:

```powershell
FEBuilderGBA.CLI --convertmap1picture --in=map.png --outImg=gfx/map/0001.4bpp --outTSA=maps/0001.tsa.lz --outPal=gfx/map/0001.pal --json
```

`--json` stabilizes process integration; it does not change any output file format.

## Relationship & forward work

- Discussion [#1930](https://github.com/laqieer/FEBuilderGBA/discussions/1930);
  [#1937](https://github.com/laqieer/FEBuilderGBA/issues/1937) (CLI as format-knowledge backend);
  [#1940](https://github.com/laqieer/FEBuilderGBA/issues/1940) (GUI-first authoring view — same verbs);
  [`agent-parity.md`](agent-parity.md) / [#1931](https://github.com/laqieer/FEBuilderGBA/issues/1931)
  (these verbs on the GUI↔CLI parity matrix).
- **Friendlier / expanded engine structures** (a decomp hack need not byte-match vanilla — repointed/larger
  tables, indirection) is **forward work** tied to the emit paths in
  [#1935](https://github.com/laqieer/FEBuilderGBA/issues/1935) (emit-recipe) and
  [#1939](https://github.com/laqieer/FEBuilderGBA/issues/1939) (decomp-C emit), which don't exist yet — so
  it is **cross-linked here, not implemented**.

## Out of scope

- A global `--json` mode across all 71 CLI verbs — only the two ROM-free converter verbs FEHRR relies on
  gained `--json` here.
- Implementing friendlier/expanded structures (→ #1935 / #1939); the decompilation itself.
