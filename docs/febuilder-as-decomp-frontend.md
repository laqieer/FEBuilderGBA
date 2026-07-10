# FEBuilderGBA as a Decomp Authoring Front-End

**Purpose.** Completed FE decompilations ([fireemblem8u](https://github.com/laqieer/fireemblem8u) —
~99.8%, builds byte-identical without the ROM; [fireemblem8j](https://github.com/laqieer/fireemblem8j))
are the FE analog of `pret/pokeemerald`. Their own *"Missing essentials and roadmap"* moddability
matrix rates **brand-new maps, battle animations, and skills** as **Hard**, and **portraits/graphics
import** as **Moderate** (doable via the `graphics/` PNG pipeline but "format/palette-fiddly and lacks
a preview loop") — because these are **perception / asset** problems, not logic problems. That is
exactly where FEBuilderGBA's **visual
editors + live preview** shine. This doc describes how FEBuilderGBA is **repositioned (not obsoleted)**
for the decomp era: as an **authoring / preview front-end** where a human (or agent) edits assets
visually, then lands the result **as source in the decomp tree** — honoring the decomp rule that
*final changes land as C, asm, text, JSON, or asset files*.

Tracks [#1940](https://github.com/laqieer/FEBuilderGBA/issues/1940); spun out of discussion
[#1930](https://github.com/laqieer/FEBuilderGBA/discussions/1930).

> **Scope of this doc — the GUI-first *authoring workflow*.** It covers the *asset / visual* front-end
> (edit visually → export a decomp-consumable artifact → commit as source). Two siblings own adjacent
> concerns, and this doc **cross-links, not duplicates** them:
> - [#1939](https://github.com/laqieer/FEBuilderGBA/issues/1939) — emitting **decomp-C struct/table
>   data** (the data half).
> - [#1941](https://github.com/laqieer/FEBuilderGBA/issues/1941) — hardening the CLI convert verbs into
>   a **stable, scriptable converter interface** (stable flags / `--json`). This doc does **not** make
>   interface-stability guarantees — those live in #1941.

## The opening: decomp's honest weak spots are FEBuilder's strengths

fireemblem8u's own [*"Missing essentials and roadmap"*](https://github.com/laqieer/fireemblem8u/wiki/Missing-essentials-and-roadmap)
moddability matrix rates these subsystems **Hard** (maps, animations) or **Moderate but a real
ergonomics gap** (graphics) — all perception/asset work with no in-tree visual editor:

| Decomp weak spot | Decomp rating | Why it's fiddly in a decomp | FEBuilder strength |
|---|---|---|---|
| Map tile layouts (brand-new maps) | **Hard** | binary `.mar`/`.bin`/`.lz`; no in-tree visual map editor | visual map editor + live preview |
| Battle animations | **Hard** | least-decompiled; opaque keyframes | frame-by-frame animation editor + preview |
| Portraits / graphics import | **Moderate** | `graphics/` PNG pipeline works, but must match dims / bit-depth / palette; no in-loop preview | visual import with live palette/dimension feedback |

> (The matrix also rates **skills** *Hard* — "none shipped" — but FEBuilder has no decomp-consumable
> skill export today; see the **Honest gaps** section below.)

## The workflow (author visually → export → commit as source)

1. **Open the ROM in FEBuilderGBA** (the decomp's *built* ROM works as a preview target, or any
   working ROM). Edit the asset visually — map tiles, a battle animation, a portrait, a palette.
2. **Export the edited asset headlessly** in the form the decomp build expects, using the CLI verb
   for that asset (see the mapping table below). Prefer the **`--project=<dir>`** mode where available:
   it reads the decomp project's built ROM and writes **project-relative** paths.
3. **Commit the emitted file(s) as source** in the decomp tree (`.s` / `.json` / asset blob / PNG +
   `.pal`) and rebuild with the decomp toolchain. The emitted artifacts are reviewable source, not an
   opaque ROM patch.
4. **(Optional) verify before committing:** round-trip map **layouts** with `--roundtrip-asset`
   (ROM-free structural proof), and byte-verify the **ROM-backed** kinds
   (`mapchange`/`mapanime2pal`/`objtiles`/`mapchipconfig`/`mapanime1gfx`) against the ROM with
   `--verify-asset`.

> This is deliberately **iterative and visual**: edit → preview → export → build. The scriptable,
> stable-flag "converter interface" for unattended pipelines is #1941's concern.

## Mapping: decomp subsystem → FEBuilder export path → artifact

Every verb below exists today in [`docs/cli-reference.md`](cli-reference.md) (and
`FEBuilderGBA.CLI/Program.cs`). **Honest gaps are called out explicitly.**

| Decomp subsystem | FEBuilder export verb | Emitted artifact |
|---|---|---|
| **Map tile layout** | `--export-asset --kind=map` (always LZ77-decompressed) | raw `.mar` tile-layout blob + `.mar.json` sidecar; round-trips via `--import-asset` / `--roundtrip-asset` (ROM-free — `--verify-asset` does **not** cover `map`) |
| **Map changes / tile-anim / chipset** | `--export-asset --kind=` one of `mapchange`, `mapanime2pal`, `objtiles`, `mapchipconfig`, `mapanime1gfx` | raw uncompressed overlay/config/gfx blobs + `.json` sidecars; ROM-backed byte proof via `--verify-asset` for these kinds |
| **Newly-authored map (from an image)** | `--convertmap1picture` | `tiles.bin` + `tsa.bin` + matching `palette.bin` (image → GBA map artifacts; no ROM required) |
| **Battle animation** (primary) | **`--export-battle-anim-decomp`** | `banim_<TAG>_motion.s` (macro assembly) + per-team `.pal` + `.json` manifest, using fireemblem8u's banim macros — **READ-ONLY** |
| Battle animation (preview only) | `--export-battle-anime` | classic `.txt`+PNG or GIF — a **preview / reimport aid**, *not* a decomp-consumable artifact |
| **Portraits** | `--render-portrait` / `--export-portrait-all` | single-portrait PNG(s) |
| **Palettes** | `--export-palette` | `.pal`/`.act`/`.gpl`/`.txt`/`.gbapal` |
| **Generic graphics / tiles** | `--export-asset --kind=graphics` | decoded PNG (with `--palette-addr`, `--width`, `--height`, `--bpp`) |

### Honest gaps (not yet first-class — tracked elsewhere)

- **Composite portrait "package" (ROM → package)** — the `portrait-package` format (128×112 sheet +
  JASC `.pal`) has **`--import-asset`/`--roundtrip-asset`/`--validate-asset`** support, but there is
  **no ROM→package `--export`** direction yet. Single-portrait PNG export (`--render-portrait` /
  `--export-portrait-all`) is the current path.
- **Skills** — the decomp ships none; FEBuilder's skill editors have no decomp-consumable export path
  today (Core seams exist but no CLI verb — see the cheap-win list in
  [`agent-parity.md`](agent-parity.md)).
- **Data-table C emission** — emitting `const struct` C tables a `devkitARM` project can `#include` is
  **#1939**, not this doc.

## Relationship

- Discussion [#1930](https://github.com/laqieer/FEBuilderGBA/discussions/1930).
- [#1939](https://github.com/laqieer/FEBuilderGBA/issues/1939) — decomp-**C struct/table** emission
  (the *data* half; this doc is the *asset/visual* half).
- [#1941](https://github.com/laqieer/FEBuilderGBA/issues/1941) — hardening these convert verbs into a
  **stable, scriptable converter interface** (FEHRR already drives `FEBuilderGBA.exe` as its map/TSA
  converter). This doc is the interactive authoring story; #1941 owns interface-stability guarantees.
- [#1937](https://github.com/laqieer/FEBuilderGBA/issues/1937) — CLI as a format-knowledge backend;
  [#1935](https://github.com/laqieer/FEBuilderGBA/issues/1935) — emit-recipe;
  [#1938](https://github.com/laqieer/FEBuilderGBA/issues/1938) — base-ROM / template policy.

## Out of scope

- Implementing the decompilation, or a visual editor *inside* the decomp tree (FEBuilderGBA already
  is that editor).
- Interface-stability / `--json` converter guarantees (→ #1941); decomp-C data-table emission (→ #1939).
