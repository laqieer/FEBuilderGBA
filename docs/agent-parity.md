# Agent Parity Matrix — GUI ↔ CLI ↔ Core

**Purpose.** FEBuilderGBA's GUI (WinForms + the Avalonia preview) is the **human** editing
surface; [`FEBuilderGBA.CLI`](cli-reference.md) (~67 commands) is the **agent** surface. For the
app to be genuinely *agent-native*, an agent needs a headless path (a CLI verb **or** a
`FEBuilderGBA.Core` seam) for roughly whatever a human can edit in the GUI. This document audits
that coverage per **editable surface** and classifies each **Full / Partial / None / N/A**, then
lists the highest-value gaps and the concrete "cheap wins."

Tracks issue [#1931](https://github.com/laqieer/FEBuilderGBA/issues/1931).

> **This is the GUI→CLI axis (does a headless verb exist at all).** Two *different* matrices exist:
> - [#1933](https://github.com/laqieer/FEBuilderGBA/issues/1933) — the **CLI→harness** axis (does the
>   Python `agent-harness` wrap an existing CLI verb).
> - [`avalonia-gap-analysis.md`](avalonia-gap-analysis.md) — the **Avalonia↔WinForms** GUI-parity axis.
>
> Don't conflate them.

## How to read the classification

| Coverage | Meaning |
|---|---|
| **Full** | A headless path covers the domain end-to-end (read **and** write, where the GUI writes). |
| **Partial** | A headless path exists but is read-only, format-limited, or covers only part of the surface. |
| **None** | No CLI verb *and* no `FEBuilderGBA.Core` seam — an agent cannot do this headlessly today. |
| **N/A** | Not a ROM-data domain (pure presentation/UI shell, or a generic shared widget). |
| **Core-only** | A `FEBuilderGBA.Core` seam exists but is **not** exposed as a CLI verb — a **cheap win** (wrap it). |

## Methodology (why this matrix is accurate)

1. **Row = editable *surface*, not a broad GUI category.** Where a GUI category straddles coverage
   levels (e.g. "Maps" = fully-covered chapter *settings* + not-round-trippable *tile layout*), it is
   split into separate rows.
2. **Registry cross-check first.** Every one of the **40 registered struct tables**
   (`FEBuilderGBA.Core/StructExportCore.cs`) is covered by the generic struct-data surface:
   `--export-data` supports TSV/CSV/EA/JSON, `--import-data` accepts TSV/JSON with real ROM mutation
   (`RunImportData` → `StructExportCore.WriteTable` → `ROM.Save`), and `--data-roundtrip` checks
   direct struct read/write stability without serializing through any file format. These are mapped
   to their GUI editors **before** per-editor verb classification, so a table-backed editor is never
   mislabeled as worse-covered than it is.
3. **"CLI *or* Core-level headless equivalent"** (the issue's rule): a domain with a Core seam but no
   CLI verb is **Core-only** (a cheap win), not None.

Counts cited: **67** user-facing CLI commands (`cli-args.md` — "the `Main` dispatcher … routes 67
distinct user-facing commands"); **40** struct tables (`--list-tables`); **~60** Core seams
(`CORE-SEAMS.md`).

---

## 1. Struct-data domains — Full via `--export-data` / `--import-data`

All 40 registered tables support direct struct read/write stability checks
(`--data-roundtrip --table=<name>`), export to `tsv|csv|ea|json`, and import from TSV or JSON with a
real ROM save. CSV and EA are export-only. Tables are grouped by the GUI editor family they back
(`StructExportCore.cs` line in parentheses).

| Domain / surface | GUI editor(s) | Table(s) (`--table=`) | Coverage |
|---|---|---|---|
| Units | Unit & Class editors | `units` (53) | **Full** |
| Classes | Unit & Class editors | `classes` (65), `class_alpha_names` (902), `cc_branch` (458) | **Full** |
| Items | Item editors | `items` (87), `item_weapon_triangle` (511) | **Full** |
| Portrait table | Portrait editor (metadata) | `portraits` (111), `generic_enemy_portraits` (655) | **Full** |
| Supports | Support System editors | `support_units` (189), `support_talks` (210), `support_attributes` (230) | **Full** |
| Event data tables | Event editors (data, not script) | `event_haiku` (253), `event_battle_talk` (274), `event_force_sortie` (295) | **Full** |
| World-map data | World Map editors (points/paths/bgm) | `worldmap_points` (324), `worldmap_paths` (352), `worldmap_bgm` (378) | **Full** |
| Chapter / map settings | `MapSettingView` family | `map_settings` (411), `map_exit_points` (532) | **Full** |
| AI **settings** tables | AI setting editors (`AIMapSettingView`, `AIPerformItemView`, `AIPerformStaffView`, `AIStealItemView`, `AITargetView`) | `ai_map_settings` (553), `ai_perform_items` (574), `ai_perform_staff` (595), `ai_steal_items` (616), `ai_targets` (637) | **Full** |
| Menu definitions | Menu editors (definitions) | `menu_definitions` (488) | **Full** |
| Status options | Status Screen editors (options) | `status_options` (667) | **Full** |
| Endings | Ending (ED) editors | `ed_retreat` (691), `ed_epithet` (717), `ed_epilogue_a/b/c` (743/769/795) | **Full** |
| Opening | Opening (OP) editors | `op_class_demo` (824), `op_class_font` (850), `op_prologue` (876) | **Full** |
| Sound room | `SoundRoomViewerView` | `sound_room` (139), `sound_boss_bgm` (160) | **Full** |
| Arena / Summon / Monster | Arena/Monster/Summon editors | `link_arena_deny` (432), `summon_units` (935), `summons_demon_king` (961), `monster_probability` (984) | **Full** |

> A "raw struct words" read-only alternative (`--export-map-settings`) also exists for chapter data,
> superseded by the generic `--export-data --table=map_settings` (which also imports).

## 2. Text & dialogue — Full

| Surface | GUI editor(s) | Headless path | Coverage |
|---|---|---|---|
| Text / dialogue | Text & Translation editors | `--translate` (export/import), `--translate_batch`, `--search-text`, `--text-refs`, `--translate-roundtrip` | **Full** |

## 3. Graphics & assets

| Surface | GUI editor(s) | Headless path | Coverage | Notes / gap |
|---|---|---|---|---|
| Portrait images | Portrait editor | `--render-portrait`, `--export-portrait-all`, `--import-portrait`, `--import-portrait-all` | **Full** | |
| Battle animations | Battle-anime editor | `--import-battle-anime`, `--export-battle-anime` (+ `--gif`), decomp `--export-battle-anim-decomp` | **Full** | |
| Palettes | Palette tools | `--export-palette`, `--import-palette` (JASC/ACT/GIMP/hex/raw) | **Full** | |
| Map/screen graphics & TSA | TSA / map-graphic editors | `--convertmap1picture`, decomp `--import-asset`/`--export-asset` (map/mapchange/objtiles/…) | **Partial** | No general in-ROM TSA round-trip verb; decomp-asset family is project-mode. |
| **Wait icons** | `ImageUnitWaitIconView` | Core `WaitIconRenderCore` / `WaitIconImportCore` — **no CLI verb** | **Core-only** | Cheap win → see gaps. |
| **Move icons** | `ImageUnitMoveIconView` | Core `UnitMoveIconRenderCore` / `UnitMoveIconImportCore` — **no CLI verb** | **Core-only** | Cheap win → see gaps. |

## 4. Maps (tile layout, distinct from settings in §1)

| Surface | GUI editor(s) | Headless path | Coverage | Notes / gap |
|---|---|---|---|---|
| Map tile layout | Map editor (tiles) | decomp `--import-asset --kind=map` / `--export-asset`; `--convertmap1picture` | **Partial** | Chapter *settings* are Full (§1); tile-layout has no plain-ROM round-trip verb (only decomp project mode). |

## 5. Event / Procs / AI **scripts** (distinct from AI/event data tables in §1)

| Surface | GUI editor(s) | Headless path | Coverage | Notes / gap |
|---|---|---|---|---|
| Event / Procs / AI script opcodes | `EventScriptView`, `ProcsScriptView`, `AIScriptView` | Read: `--disasm-event --type=` (`event`, `procs`, or `ai`). Write: `--compile-event` (EA `.event` only) | **Partial** | No structured per-command *import* back into the ROM; write path is EA-source compilation, not opcode editing. |

## 6. Audio

| Surface | GUI editor(s) | Headless path | Coverage | Notes / gap |
|---|---|---|---|---|
| Songs (MIDI) | Song editors | `--export-midi`, `--import-midi`, `--songexchange`, `--export-voicegroup` | **Full** | |
| **Instruments / track changes / samples** | Song instrument / track editors | Core `SongTrackChangeCore` / `SongWaveConvertCore` — **no CLI verb** | **Core-only** | Cheap win → see gaps. |
| Sound room | `SoundRoomViewerView` | `--export-data --table=` (`sound_room`, `sound_boss_bgm`) (§1) | **Full** | |

## 7. Patches & mods — Full

| Surface | GUI editor(s) | Headless path | Coverage |
|---|---|---|---|
| Patches | Patch Manager | `--list-patches`, `--apply-patch`, `--uninstall-patch`, `--makeups`/`--applyups` (UPS) | **Full** |

## 8. Tools (per-surface, not blanket-Full)

| Surface | GUI editor(s) | Headless path | Coverage | Notes / gap |
|---|---|---|---|---|
| Hex dump / disassembly | Hex editor, DisASM | `--hex-dump`, `--disasm`, `--lint-oam` | **Partial** | Read/dump only; interactive ASM *edit/insert* surfaces have no CLI verb. |
| Pointer search / free space | PointerTool, MoveToFreeSpace | `--pointercalc`, `--freespace` | **Partial** | Pointer *search* + free-space *scan* covered; `MoveToFreeSpace`, RAM/emulator-memory tools have no headless equivalent. |
| Free-space report | — | `--freespace` | **Full** | |
| Struct export **formats** | `DumpStructSelectDialogView` | `--export-data --format=` (`tsv`, `csv`, `ea`, `json`) | **Partial** | GUI also exports **STRUCT** / **NMM** (`ExportToSTRUCT`/`ExportToNMM`) — not reachable from the CLI `--format=` switch. Cheap win → see gaps. `json` (#1937) is the LLM-backend-facing format: like TSV, `--import-data` accepts it back (auto-detected from a `.json` `--in`, or `--format=json`); CSV and EA remain export-only. |

## 9. Skills

| Surface | GUI editor(s) | Headless path | Coverage | Notes / gap |
|---|---|---|---|---|
| Skill config / assignment | Skill System editors (`SkillConfig*`) | Core `SkillConfigSkillSystemBulkExportCore` / `SkillConfigSkillSystemBulkImportCore` (bulk TSV, one atomic transaction), `SkillSystemsAnimeExportCore` / `SkillSystemsAnimeImportCore` — **no CLI verb** | **Core-only** | Strongest cheap win in the repo → see gaps. |

## 10. N/A (UI-only — not a ROM-data domain)

Application Shell, Error & Notification dialogs, Tool Utilities (e.g. `ToolFlagNameForm` — local
labels, no ROM data), Simple Menu Mode, and the generic **bit-flag** editing widget (a shared
control invoked contextually by other editors; the underlying values are covered by whichever domain
owns them). These are **N/A**, not gaps.

---

## Highest-value gaps (prioritized follow-up-issue candidates)

**Cheap wins first** — each is a `FEBuilderGBA.Core` seam that already works headlessly and only needs
a thin CLI verb wrapper (same pattern as every other verb in `Program.cs`). None are filed as issues
yet; this list is the candidate set.

| # | Proposed CLI verb | Existing Core seam (no CLI ref today) | Scope |
|---|---|---|---|
| 1 | `--export-skills` / `--import-skills` | `SkillConfigSkillSystemBulkExportCore`, `SkillConfigSkillSystemBulkImportCore` | Bulk skill-config TSV round-trip (atomic). |
| 2 | `--export-skill-anime` / `--import-skill-anime` | `SkillSystemsAnimeExportCore`, `SkillSystemsAnimeImportCore` | Skill-animation `.txt`+PNG/GIF export & import. |
| 3 | `--export-data --format=struct` + `--format=nmm` | `StructExportCore.ExportToSTRUCT`, `ExportToNMM` | Extend the existing `--format=` switch (GUI already offers these). |
| 4 | `--render-waiticon` / `--import-waiticon` (+ move-icon variants) | `WaitIconRenderCore`/`WaitIconImportCore` (wait); `UnitMoveIconRenderCore`/`UnitMoveIconImportCore` (move) | Unit wait-icon **and** move-icon PNG render & import (distinct seams). |
| 5 | `--export-instrument` / track-change verbs | `SongTrackChangeCore`, `SongWaveConvertCore` | Instrument/sample/track-change headless path. |

**Larger gaps (need new work, not just a wrapper):**

| # | Gap | Why it's not a cheap win |
|---|---|---|
| 6 | Structured event/procs/AI **script import** | Only EA `--compile-event` exists; per-command opcode import is a new capability. |
| 7 | Plain-ROM **map tile-layout** round-trip verb | Only decomp project-mode `--import-asset` exists; no `--rom`-mode tile round-trip. |
| 8 | `MoveToFreeSpace` / RAM / emulator-memory headless tools | GUI-interactive; needs design (relates to the #1932 playtest harness). |

---

*Generated for #1931. Every CLI verb here is in [`cli-reference.md`](cli-reference.md); every Core seam
is in [`CORE-SEAMS.md`](CORE-SEAMS.md) or the cited `FEBuilderGBA.Core/*.cs` file; the 40 tables and
their line numbers are from `FEBuilderGBA.Core/StructExportCore.cs`.*
