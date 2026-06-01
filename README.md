README
===

[![MSBuild](https://github.com/laqieer/FEBuilderGBA/actions/workflows/msbuild.yml/badge.svg)](https://github.com/laqieer/FEBuilderGBA/actions/workflows/msbuild.yml)
[![E2E: No ROM](https://github.com/laqieer/FEBuilderGBA/actions/workflows/e2e-norom.yml/badge.svg)](https://github.com/laqieer/FEBuilderGBA/actions/workflows/e2e-norom.yml)
[![E2E: FE6](https://github.com/laqieer/FEBuilderGBA/actions/workflows/e2e-fe6.yml/badge.svg)](https://github.com/laqieer/FEBuilderGBA/actions/workflows/e2e-fe6.yml)
[![E2E: FE7J](https://github.com/laqieer/FEBuilderGBA/actions/workflows/e2e-fe7j.yml/badge.svg)](https://github.com/laqieer/FEBuilderGBA/actions/workflows/e2e-fe7j.yml)
[![E2E: FE7U](https://github.com/laqieer/FEBuilderGBA/actions/workflows/e2e-fe7u.yml/badge.svg)](https://github.com/laqieer/FEBuilderGBA/actions/workflows/e2e-fe7u.yml)
[![E2E: FE8J](https://github.com/laqieer/FEBuilderGBA/actions/workflows/e2e-fe8j.yml/badge.svg)](https://github.com/laqieer/FEBuilderGBA/actions/workflows/e2e-fe8j.yml)
[![E2E: FE8U](https://github.com/laqieer/FEBuilderGBA/actions/workflows/e2e-fe8u.yml/badge.svg)](https://github.com/laqieer/FEBuilderGBA/actions/workflows/e2e-fe8u.yml)
[![GitHub Release](https://img.shields.io/github/v/release/laqieer/FEBuilderGBA)](https://github.com/laqieer/FEBuilderGBA/releases/latest)
[<img src="https://raw.githubusercontent.com/oprypin/nightly.link/master/logo.svg" height="16" style="height: 16px; vertical-align: sub">Nightly Build](https://nightly.link/laqieer/FEBuilderGBA/workflows/msbuild/master)
[![codecov](https://codecov.io/gh/laqieer/FEBuilderGBA/branch/master/graph/badge.svg)](https://codecov.io/gh/laqieer/FEBuilderGBA)
[![Cross-Platform](https://github.com/laqieer/FEBuilderGBA/actions/workflows/crossplatform.yml/badge.svg)](https://github.com/laqieer/FEBuilderGBA/actions/workflows/crossplatform.yml)

Mirrors for Chinese mainland users (面向中国大陆用户的镜像发布地址): [![Gitee Release](https://gitee-badge.vercel.app/svg/release/laqieer/FEBuilderGBA?style=flat)](https://gitee.com/laqieer/FEBuilderGBA/releases/latest) [![Gitee Go Build](https://gitee.com/laqieer/FEBuilderGBA/widgets/widget_5.svg)](https://gitee.com/laqieer/FEBuilderGBA/gitee_go/pipelines?tab=release)

## 🚀 Getting Started

### Project Structure

| Project | Target | Description |
|---------|--------|-------------|
| `FEBuilderGBA.Core` | net9.0 | Cross-platform core library (ROM, Undo, LZ77, text encoding, Huffman codec, patch detection, translation, cache, git, archive, event ASM, disassembler, export, mod, address, event script, EtcCache, symbol util, magic split, grow simulator, system text encoder, config persistence, GDB socket, event script util, EA lyn dump parser, lint core types/validation, UPS patch, image service abstraction, path utilities, logging facade, utilities, HeadlessEtcCache, HeadlessSystemTextEncoder, MapSettingCore, StructMetadata, StructExportCore, FELintScanner, DisassemblerCore, ImageUtilCore, ImageImportCore, DecreaseColorCore, PointerCalcCore, RebuildCore, SongExchangeCore, MapConvertCore) |
| `FEBuilderGBA` | net9.0-windows | WinForms GUI application |
| `FEBuilderGBA.CLI` | net9.0 | Cross-platform CLI tool (42 commands: `--version`, `--help`, `--makeups`, `--applyups`, `--lint`, `--lint-oam`, `--disasm`, `--disasm-event`, `--decreasecolor`, `--pointercalc`, `--rebuild`, `--songexchange`, `--convertmap1picture`, `--translate`, `--translate-roundtrip`, `--translate_batch`, `--export-data`, `--import-data`, `--data-roundtrip`, `--render-portrait`, `--export-portrait-all`, `--import-portrait`, `--generate-font`, `--export-midi`, `--import-midi`, `--apply-patch`, `--list-patches`, `--list-resources`, `--uninstall-patch`, `--expand-table`, `--merge3`, `--compile-event`, `--import-battle-anime`, `--export-battle-anime`, `--freespace`, `--hex-dump`, `--search-text`, `--resolve-names`, `--lastrom`, `--force-detail`, `--test`, `--testonly`; flags: `--force-version`, `--noScale`, `--noReserve1stColor`, `--ignoreTSA`, `--table`, `--patch-name`) |
| `FEBuilderGBA.SkiaSharp` | net9.0 | SkiaSharp implementation of IImageService (GBA 4bpp/8bpp tiles, palette conversion) and `SkiaFontRasterizer` — the cross-platform `IFontRasterizer` that reproduces the WinForms GDI glyph algorithm byte-for-byte so translation-font auto-generation works on Windows/Linux/macOS (#796) |
| `FEBuilderGBA.Avalonia` | net9.0 | Cross-platform Avalonia UI (~47% feature completeness). ROM loading, 356 editors: unit/item/class editors with full read/write + undo (Class Editor includes a class-card preview panel — class face portrait + name + wait icon, matching WinForms `L_8_PORTRAIT_CLASS`/`L_5_CLASS`/`L_6_CLASSICONSRC` block, see issue #357); map/event/AI/text/audio/graphics/portrait/world map/support/arena/monster/summon/menu/credits viewers; image editors with PNG import; hex editor with hex dump view, jump, search; pointer search and free space scanning tools; editor search/filter in main window; dirty-check on close; named dropdowns (`ComboResourceHelper`); bit flag panels (`BitFlagPanel`); all 148 write-enabled editors wrapped with `UndoService`; all editors use `IsLoading` guards; cross-editor navigation (Jump to Class/Portrait) with pick-and-return support (`PickFromEditor<T>()`) and reusable `IdFieldControl` (hyperlink label + value box + name preview + Jump + Pick, with optional `ShowPick=False` for fields whose target editor isn't IPickableEditor) used by CC Branch Editor, Unit FE6/FE7 Class ID fields (#366), and 32 id fields across 19 editors covering Item / Unit / Class / Song / Text IDs in ItemRandomChest, ItemWeaponEffect, MonsterItem, LinkArenaDenyUnit, SummonUnit, EDView, SummonsDemonKing (UnitId+ClassId), ArenaClass, OPClassDemoFE7/FE7U/FE8U, SoundRoomViewer, EDSensekiComment, ItemShop, ItemEffectivenessViewer, ItemPromotionViewer, and SupportTalk/FE6/FE7 (the canonical original example with 5 IdFieldControls per editor — 2 partner UnitId + 3 TextId C/B/A with ShowPick=False) (closes #360); Class Editor's Pointers/Movement/Terrain panel exposes Jump buttons next to all six pointer fields — Battle Anime, Move Cost Rain/Snow, and Terrain Avoid/Def/Res — version-aware for all five ROM variants (FE6 reuses the Rain/Snow controls for Terrain Avoid/Def at the shifted P56/P60 offsets), matching the existing Move Cost (P56) Jump button (#359); ROM info display with free space analysis and data section pointers; proper list loading in class list, promo list, weapon lock (usage guide: [docs/weapon-lock-vennou-editor.md](docs/weapon-lock-vennou-editor.md)), and unit short text editors with AddressListControl and name resolution. All 357 window views use `SizeToContent="WidthAndHeight"` for auto-sizing, and all views include `ScrollViewer` wrappers to prevent content clipping. The Text Editor now hosts a read-only **Simple Conversation Viewer** tab that mirrors the WinForms `TextForm` simple-mode preview: each dialogue line renders as a portrait + bubble card so translators and event scripters can review a chapter's flow at a glance (issue #367). The Map Exit Point editor's **Data Expansion** (Expand List) button now grows the selected map's per-map exit-point block by one blank row via `DataExpansionCore.ExpandTableTo` — it relocates the block to free space, repoints the per-map pointer slot, zero-fills the new row, preserves the terminator, and is undoable; blank/no-list and corrupt/unterminated blocks are refused (issue #773). Table-expansion in Core also exposes `DataExpansionCore.RepointAllReferences(rom, oldBase, newBase, undo)` — an opt-in, all-reference rescan that repoints EVERY pointer to a moved table base: raw 32-bit pointers (`U.GrepPointerAll`) AND ARM Thumb LDR literal-pool loads (`U.GrepPointerAllOnLDR`, ported to Core with EOF-safety guards in #781), de-duplicated and undoable, safe on a no-reference ROM. This closes the former `DataExpansionCore` LDR-rescan known-gap (the single-slot repoint in `ExpandTable`/`ExpandTableTo` is still correct for unshared tables). The Instrument (SongInstrument) editor's **Expand List** button now grows the loaded song's voicegroup (instrument set) to the full 128 12-byte records via `RepointAllReferences`: it relocates the voicegroup to free space, copies the defined-prefix instruments verbatim, fills **every** added record (through record 127) from instrument 0 and moves the original stop record to position 128 (matching WinForms `MoveToFreeSapceForm` — the voicegroup is a fixed-size instrument set with no terminator, so all 128 records are filled, which makes a re-expand idempotent), then repoints **every** song header that shares the voicegroup (so the other songs are not corrupted — the #782 shared-voicegroup win); it is undoable, the button only enables for a song-context voicegroup with fewer than 128 defined instruments, and empty/already-128 sets are refused (a freshly-expanded voicegroup reads as a full 128, so a second click is a no-op) (issue #780). The **World Map Image** editor's two **List Expand** buttons (Border tab and Icon Data tab) now grow their fixed-`RomInfo`-pointer tables — the 12-byte border table (`worldmap_county_border_pointer`) and the 16-byte icon-data table (`worldmap_icon_data_pointer`) — to a prompted row count via `DataExpansionCore.ExpandTableTo` **then** `RepointAllReferences`: `ExpandTableTo` relocates the table to free space, copies existing rows verbatim, zero-fills the new rows, writes the `0xFFFFFFFF` terminator and single-slot-repoints the canonical pointer, after which `RepointAllReferences` repoints every other raw-pointer / ARM-Thumb-LDR reference to the old base (matching WinForms `InputFormRef.ExpandsArea` → `MoveToFreeSapceForm.SearchPointer`); the expand is undoable, and on a clean ROM with no secondary references `RepointAllReferences` returns 0 which is treated as success (the canonical pointer is already repointed). The grown list is rendered directly from the post-expand row count rather than by re-scanning (the appended rows are zero-filled, so a re-scan would stop at the first new row). The list's comment/lint cache repoint is forward-only (not reversed on rollback — accepted WinForms parity); the SkillAssignment (issue #834) and Magic FEditor/CSA Creator (issue #837) list-expands that #825 left as follow-ups have since been wired (both described below). The Event Unit (FE8) editor's **New Allocation** button now allocates a real, editable unit-list block instead of opening a stub: a modal count-picker (NumericUpDown Min=1/Max=50/Value=1 — WinForms `EventUnitNewAllocForm` parity) chooses the row count, then `MapEventUnitCore.AllocNewUnitList` writes `count * eventunit_data_size + 1` bytes (each row's B0=1, trailing 0x00 terminator — byte-for-byte WinForms `EventUnitForm.CreateNewData`) via the shared free-space allocation seam (`MapEventUnitCore.AppendBinaryDataHeadless`, also now used by `EventCondViewModel`), under an undo scope; the new "NEW" group entry is tracked in a session list that survives map/group refresh (WinForms `NewAllocData`/`AppendNoWriteNewData` parity). WinForms writes **no** cond pointer for a NEW block — it is a reserved editing convenience the user references from the event script — so to match that, Core `MapEventUnitCore.GetUnitGroupsForMap` now also runs an event-script `POINTER_UNIT` scan (mirroring WinForms `EventCondForm.MakeUnitPointerEventScan`: it disassembles the map's START/END-event scripts via the Core `EventScript` disassembler and collects `ArgType.POINTER_UNIT` targets, de-duped with the direct cond-slot lists), so a unit list referenced from the event script shows up in the Avalonia group list on the next reload — the load-bearing reachability fix that makes the NEW block discoverable end-to-end (issue #776). The standalone **Instrument Selection** (SongTrack instrument-set) browser is now populated from the new cross-platform `InstrumentSetCore` — it scans the ROM for known native-instrument-map / voicegroup signatures (NIMAP / NIMAP2 / AllInstrument) above the compressed-image borderline and lists each discovered instrument set (seeded with the song's "Current" pointer), auto-selecting the first discovered set, mirroring WinForms `SongTrackImportSelectInstrumentForm.PickupInstrument()` (issue #787). The **Skill Configuration (FE8N)** editor's Unit Skill List tab now makes the FE8N v1 skill row's 16 ext-bytes **B16..B31** editable (16 `NumericUpDown` inputs synced via the view's code-behind — `UpdateUI()` pushes the new ViewModel `Ext0..Ext15` values into the controls and `WriteButton_Click()` pulls them back, the same mechanism as the editor's other fields): selecting a skill row loads them and **Write** persists them as a PURE in-place byte write at `addr+16..+31` (no table relocation), tracked in the same undo scope as the rest of the row — FE8N v1's N00..N03 sub-tabs all union onto these same 16 bytes, so the one editable tab is faithful and complete (issue #790). Building on that, the same Unit Skill List tab now shows a **read-only inline unit-name preview** next to each of the 16 B16..B31 ext-byte inputs (WinForms **N00** decoration parity): each ext-byte is treated as a one-based unit ID and the resolved unit name is rendered beside it via the existing Core `NameResolver.GetUnitNameByOneBasedId` (no new Core code, no writes). The preview refreshes live from each input's **current** value (not the not-yet-written ViewModel property), is cleared when no row is loaded, and renders empty for a value of 0; this is decoration only — the Class/Item/Other-tab decorations and skill icons remain deferred (issue #793). The **ROM Translation Tool**'s "Import Font" action now auto-generates missing glyphs cross-platform: the previously WinForms-only bitmap auto-generation (`ImageUtil.AutoGenerateFont` + `Image4ToByte`) is replaced by the platform-neutral `IFontRasterizer` seam, whose `SkiaFontRasterizer` (in `FEBuilderGBA.SkiaSharp`) reproduces the WF GDI algorithm byte-for-byte (16×16 base render, text-font 16×22 scale + (-2, offset) composite, item-font 16×16 scale + (-1, 2+offset) composite with the 4-neighbour outline ring, `R<0xA0` threshold, and the exact 64-byte 2bpp pack); the Core `ToolTranslateROMCore.ImportFonts` threads the rasterizer through the per-glyph port loop and the Avalonia editor exposes the "Auto-Generate Missing Fonts" checkbox + "Use Font Name" + "Font Size" inputs (the legacy `ImportFontFromROMs` ROM-port-only overload is kept binary-compatible) (issue #796). The **Battle Animation Palette** editor now renders the WinForms `DrawSample` battle-animation **sample-preview grid** cross-platform (closing the last viable Avalonia↔WinForms preview parity gap): the new Core `BattleAnimeRendererCore.RenderSampleBattleAnime` LZ77-decompresses the animation's palette block, slices the selected palette-type (Player/Enemy/Other/4th, the cross-platform equivalent of WinForms `ImageUtil.SwapPalette`), renders each frame via `RenderSingleFrame`, crops it to a 90×90 window at source (100,30) (matching WinForms `SCALE_90`), blank-checks the crop (`IsBlankImage`, threshold 10), and walks 12 cells with a single persistent section/frame cursor into the 360×290 grid — re-rendering on entry-load and on palette-type change, hosted in a `GbaImageControl`; Phase 1 previews the saved ROM palette (WinForms parity) and live-spinner re-render stays a follow-up (issue #822). The **Unit Palette** editor now reuses that same renderer to preview a **class's battle animation recolored with the UNIT palette being edited** (closing the `:100`/`:113` deferred-preview gap): the Edit tab gains a Class selector + sub-palette (Palette Type) combo + read-only resolved Battle-Animation-ID field + a `GbaImageControl` sample preview; the new Core `ClassFormCore.GetAnimeIDByClassID` resolves the class → battle-anime ID (the WinForms `ImageBattleAnimeForm.GetAnimeIDByClassID` chain: `p32(classAddr + 48)` for FE6 / `+52` for FE7-8, then `u16(animeSetting + 2)`), and the new Core `BattleAnimeRendererCore.GetUnitPaletteAddr` resolves the unit-palette slot address (WinForms `ImageUnitPaletteForm.GetPaletteAddr` = `p32(IDToAddr(paletteno-1) + 12)`); a palette-override overload of `RenderSampleBattleAnime` then renders the 12-cell grid against THAT unit-palette block instead of the animation record's own `rec+0x1C` palette — mirroring WinForms `DrawBattleAnime`'s `custompalette>0 → palettes = GetPaletteAddr(custompalette)` override — while still applying the `paletteIndex` sub-palette slice (the two are independent: `paletteno` = unit-palette slot / custompalette; `paletteIndex` = enemy/ally sub-palette / `SwapPalette`). The preview re-renders on entry-load, class change, palette-slot change, and palette-type change; Phase 1 previews the SAVED unit palette (the live `OnChangeColor` post-render bitmap-palette mutation stays a follow-up, like #822), but the rendered base is the unit palette, not the anime palette (issue #840). The **World Map Image** editor now renders five **reuse-based live previews** cross-platform (the `#500`/`#769` deferred world-map previews, NV5a): the **Event** map (`worldmap_event_image_pointer`/`_tsa_pointer`/`_palette_pointer`, 32×20 tiles = 256×160), the **Mini** map (`worldmap_mini_image_pointer`/`_mini_palette_pointer`, 8×8 = 64×64), **Point 1** (`worldmap_icon1_pointer`, 32×8 = 256×64), **Point 2** (`worldmap_icon2_pointer`, 12×4 = 96×32), and the **Road** strip (`worldmap_road_tile_pointer`, 1×15 = 8×120) — the last three sharing `worldmap_icon_palette_pointer`. The new Core `ImageWorldMapCore` dereferences each canonical pointer **pointer-to-pointer** (`p32` first) behind `U.isSafetyOffset` + a 4-byte LZ77-header guard (#818/#827) on every LZ77 stream, then composes via the EXISTING Core decode primitives: the event preview LZ77-decompresses **both** the image **and** the header-TSA stream and reads the event palette as **64 colors / 4 sub-palettes** (so `ImageUtilCore.DecodeHeaderTSA` can select the per-tile sub-palette via TSA bits 12–15), while mini/point/road each decode a single LZ77 image through `ImageUtilCore.LoadROMTiles4bpp` with a 16-color palette. Each preview is hosted in a `GbaImageControl` with a read-only **Export PNG** button gated by a per-preview render-success flag; every resolver is null-safe (a bad/truncated/missing pointer clears the preview, never throws). The main field map (a linear palette-map decode = a new primitive, NV5b) and the county border (an OAM-from-pointer blit, NV5c) stay deferred follow-ups (issue #843). The **main field map** preview (NV5b) is now rendered too via a NEW **FE8-only** Core primitive: `ImageUtilCore.ByteToImage16TilePaletteMap` decodes the big field map (a fixed **480×320**, 4bpp tiles) by selecting each tile's 16-color sub-palette from a linear **palette-map** nibble stream (one nibble per tile, two tiles per byte, ×0x10 sub-palette base) into a single 256-color palette, honouring the WinForms per-row `+4` off-screen-margin quirk and rendering **partially (never throwing) on a short image** — a faithful, pure (zero-decompression) port of WinForms `ImageUtil.ByteToImage16TilePaletteMap`. `ImageWorldMapCore.TryRenderMainFieldMap` gates on `RomInfo.version == 8` (FE6 routes to `DrawWorldMapFE6`, and FE7's `worldmap_big_palettemap_pointer` is a TSA-12-split not a palette-map — both return `null`), LZ77-decompresses **only** the palette-map while reading the **image (76,800 B) and palette (512 B) RAW**, and requires the full fixed regions (incomplete → `null`). The Main Field Map tab hosts it in a `GbaImageControl` with a read-only **Export PNG** button gated on a successful FE8 render; the county border (NV5c, an OAM-from-pointer blit) is the remaining deferred world-map follow-up (issue #846). The **World Map Image** editor's **Border (国境) tab** now renders a live **AP (Animated Parts) preview** (NV5c, issue #849): `ImageWorldMapCore.TryRenderBorder` LZ77-decompresses the parts image (P0), dereferences the 16-color palette pointer `worldmap_county_border_palette_pointer` via `p32` (pointer-to-pointer, matching WinForms `DrawBorderBitmap`) and reads the 32-byte palette RAW, renders the parts sheet via the new `ImageUtilCore.ByteToImage16Tile` (palette index 0 = transparent), parses the AP data at P4 via the new cross-platform `ImageUtilAPCore`, blits AP frame 0 + frame 1 (OAM-blit with the WF G4 bit-math: 2-D SharpTable, 9-bit/8-bit sign extension, transparent-index-0 blit, paletteShift) onto a transparent 256×160 canvas, composites the AP layer over the event world map, and returns a 256×160 RGBA result. FE8-only: `worldmap_county_border_palette_pointer == 0` on FE6/FE7 → null/disabled. The Border Export PNG button is gated on `CanExportBorder`; Import stays disabled (read-only cross-platform viewer). The **Item** editors (ItemEditor for FE7/FE8 and ItemFE6) now wire all four **new-alloc** buttons — Stat Bonuses (P12) and Effectiveness (P16) in both editors — which were previously deferred warning labels: when an item's sub-data pointer is 0 (and the item index > 0), the "New-alloc" button allocates a fresh block of the EXACT WinForms `InputFormRef.AllocEvent` default template (StatBooster = `byte[20]` with `[1]=5`; Effectiveness = `byte[12]` all `0x01` except `[11]=0`, or the SkillSystems-rework variant `[1]=6,[2]=1,[5]=6,[6]=2` selected via `PatchDetectionService.SkillSystemsClassTypeRework`) through the now-wired `CoreState.AppendBinaryData` seam (#796), converts the returned offset with `U.toPointer`, and writes it into the item field — block-write + pointer-write share one `ROM.BeginUndoScope`, so a single undo reverts both (the field returns to 0); a non-zero pointer is left untouched (no-clobber). The cross-platform alloc + exact templates live in Core `ItemAllocCore` (issue #831). The **Skill Assignment (Class)** editor's **Make Selected Class Independent** button is now wired (one of the SkillAssignment list-expand follow-ups deferred from #825): when the selected class's per-class level-up skill table is shared with one or more other classes, the button clones that table verbatim into a fresh free-space block and repoints **only** the selected class's pointer slot via a **single** `write_p32` — deliberately the inverse of `RepointAllReferences`, so every other sharing class stays on the intact original table (mirroring WinForms `SkillAssignmentClassSkillSystemForm.IndependenceButton_Click` → `PatchUtil.WriteIndependence`); the clone-block + pointer-write share one `ROM.BeginUndoScope` so a single undo reverts both, and the WinForms guards (in-bounds slot, safe resolved pointer, empty-list confirm) are preserved. The cross-platform clone + single-slot repoint live in Core `SkillAssignmentIndependenceCore`; the spurious master Class-Skill *List Expand* button was removed (the master table is class-count-bound and has no WinForms expand semantics), and the N1 level-up *List Expand* (single-slot `DataExpansionCore.ExpandTable`) is unchanged (issue #834). The **Magic FEditor** and **CSA Magic Creator** editors' **List Expansion** buttons are now wired (the last of the #825 list-expand follow-ups): each click grows BOTH the magic-effect pointer table (`magic_effect_pointer`, 4-byte entries, unconditional) and the CSA spell table (5×4=20-byte entries, conditional) to a fixed 254 rows via the all-reference path — `DataExpansionCore.ExpandTableTo` (relocate + copy verbatim + zero-fill + `0xFFFFFFFF` terminator + single-slot canonical repoint) **then** `RepointAllReferences` (repoint every other raw-pointer / ARM-Thumb-LDR reference to the old base, so the FEditor/CSA patch ASM's literal-pool loads don't dangle), exactly mirroring WinForms `ImageMagicFEditorForm`/`ImageMagicCSACreatorForm` `MagicListExpandsButton_Click` → `InputFormRef.ExpandsArea` → `MoveToFreeSapceForm.SearchPointer`; the CSA spell-table-pointer discovery + `NOT_FOUND` clean-abort runs FIRST (before the table-1 expand) so a ROM without the CSA table is rejected with zero mutation, the whole expand is one undo transaction, and the button hides once the table is already expanded. `RepointAllReferences` omits the event-aware `GrepPointerAllOnEvent` pass that WinForms `SearchPointer` includes (the documented #781 limitation) — acceptable here because these are ASM-referenced graphics/animation tables, not event-script data; the shared two-table expand logic lives in Core `MagicListExpandCore` (issue #837). The **Magic FEditor** editor now renders a **live magic-effect frame preview** (240×128) in the "Display Example" row: the new Core `MagicEffectRendererCore.RenderMagicFrame` decodes the FEditor magic-animation script starting from P0 (frame-data pointer), finds the Nth 0x86 frame record (28-byte stride, command byte at [+3], 0x85 skip, 0x80 terminator, 00 01 00 80 continuation), reads the BG palette (+24) and OBJ palette (+20) RAW, LZ77-decompresses the BG tiles (+16) and OBJ tiles (+4) with header+truncation guards, decodes both 4bpp sheets (index 0 = transparent), scales the 256×BG_height BG sheet left-240px vertically to 240×128 (WinForms `ImageUtil.Scale` parity), then blits the back OAM layer (P12 base + relative offset at record +12) followed by the front OAM layer (P4 base + relative offset at record +8) both with `isMagicOAM=true` via the existing `BattleAnimeRendererCore.DrawOAMSprites`; the result is hosted in a `GbaImageControl` that replaces the former `#500` deferred label. A read-only **Export PNG** button gated on render success completes the view. Frame index and P0/P4/P12 changes re-render the preview; Import/Open-source/Select-source remain disabled (out of scope). FE-gate: the preview clears automatically when no FEditor/CSA_Creator magic-system patch is detected (issue #852). The **Event Map Change** editor now renders a live **change-overlay preview** (NV6-PR2, issue #857): `MapRenderCore.RenderChangeMap` reads the RAW (uncompressed) `width × height` u16 tile-index array from P8 (B3/B4 = width/height from the 12-byte change record), resolves the OBJ tileset, 512-byte palette, and LZ77 config descriptors from the selected map's `map_setting` fields (`obj_plist & 0xFF` → OBJECT plist, `palette_plist` → new `PlistType.PALETTE` via `map_pal_pointer`, `config_plist` → CONFIG plist), composites via the shared `RenderTsaComposite` helper with `opaqueIndex0=true`, and displays the result in a `GbaImageControl`; a read-only **Export PNG** button gated on `CanExportChange` completes the view. `MapRenderCore` was refactored to extract the common TSA-composite loop into a private `RenderTsaComposite` helper shared by both `RenderMapImage` (PR1/#855) and the new `RenderChangeMap` (PR2/#857), keeping the 13 existing `RenderMapImage` tests green. Out-of-range config descriptors skip the offending tile (partial render); width/height of zero or oversized dimensions (`> MAX_CHANGE_TILES`) return null without allocating. See `docs/avalonia-gap-analysis.md` for details. |
| `FEBuilderGBA.Tests` | net9.0-windows | Unit and integration tests |
| `FEBuilderGBA.Core.Tests` | net9.0 | Cross-platform Core unit tests (runs on Linux/macOS/Windows) |
| `FEBuilderGBA.E2ETests` | net9.0-windows | End-to-end GUI/CLI tests |

### Cloning the Repository

This repository uses **git submodules** for patch management. Clone with:

```bash
git clone --recursive https://github.com/laqieer/FEBuilderGBA.git
```

Or if you already cloned without `--recursive`:

```bash
git submodule update --init --recursive
```

**Note:** The patch repository ([FEBuilderGBA-patch2](https://github.com/laqieer/FEBuilderGBA-patch2)) is maintained separately for independent versioning and faster updates.

**Bundled Tools:** [Event Assembler](https://github.com/laqieer/Event-Assembler) and [ColorzCore](https://github.com/FireEmblemUniverse/ColorzCore) are included as submodules in `tools/`. If no external EA path is configured, FEBuilderGBA automatically uses the bundled tools. To build them locally:
```bash
git submodule update --init tools/Event-Assembler tools/ColorzCore
# Windows:
dotnet build tools/ColorzCore/ColorzCore/ColorzCore.csproj -c Release
# Linux/macOS (produces a runnable executable in tools/bin/):
# Replace linux-x64 with your platform's RID (e.g. osx-arm64, osx-x64)
dotnet publish tools/ColorzCore/ColorzCore/ColorzCore.csproj -c Release -r linux-x64 --self-contained true -o tools/bin
```

**Runtime note:** All releases (WinForms, CLI, Avalonia) ship ColorzCore as a self-contained executable, requiring no additional .NET runtime.

**Public Resources:** [FE-Repo](https://github.com/Klokinator/FE-Repo) (graphics) and [FE-Repo-Music-No-Preview](https://github.com/laqieer/FE-Repo-Music-No-Preview) (music) are included as submodules in `resources/`. Browse and insert resources directly from the portrait editor (FE-Repo button), the Portrait Import Wizard (FE-Repo button + PNG/BMP drag-and-drop + advanced palette options: Auto-quantize / Share with target slot / Custom palette file + Fuchidori black-outline checkbox — #662 + Detail expander for eye/mouth block coords B20-B23 on FE7/FE8 — #663 Slice A), and song exchange form (FE-Repo Music button) in both WinForms and Avalonia.

### Cross-Platform Build (Linux / macOS / Windows)

The Core library, CLI, SkiaSharp backend, and Avalonia GUI scaffold all target `net9.0` and build on any platform:

```bash
# Build Core library
dotnet build FEBuilderGBA.Core/FEBuilderGBA.Core.csproj

# Build cross-platform CLI
dotnet build FEBuilderGBA.CLI/FEBuilderGBA.CLI.csproj

# Run CLI
dotnet run --project FEBuilderGBA.CLI -- --version
dotnet run --project FEBuilderGBA.CLI -- --makeups=out.ups --rom=modified.gba --fromrom=original.gba
dotnet run --project FEBuilderGBA.CLI -- --applyups=output.gba --rom=original.gba --patch=patch.ups
dotnet run --project FEBuilderGBA.CLI -- --lint --rom=rom.gba
dotnet run --project FEBuilderGBA.CLI -- --disasm=output.asm --rom=rom.gba
dotnet run --project FEBuilderGBA.CLI -- --decreasecolor --rom=rom.gba --in=input.png --out=output.png --paletteno=0
dotnet run --project FEBuilderGBA.CLI -- --pointercalc --rom=source.gba --target=target.gba --address=0x1234
dotnet run --project FEBuilderGBA.CLI -- --rebuild --rom=modified.gba --fromrom=vanilla.gba
dotnet run --project FEBuilderGBA.CLI -- --songexchange --rom=dest.gba --fromrom=source.gba --fromsong=1 --tosong=2
dotnet run --project FEBuilderGBA.CLI -- --convertmap1picture --rom=rom.gba --in=map.png
dotnet run --project FEBuilderGBA.CLI -- --translate --rom=rom.gba --out=texts.tsv
dotnet run --project FEBuilderGBA.CLI -- --translate --rom=rom.gba --in=texts.tsv
dotnet run --project FEBuilderGBA.CLI -- --translate-roundtrip --rom=rom.gba
dotnet run --project FEBuilderGBA.CLI -- --translate-roundtrip --rom=rom.gba --out=diff
dotnet run --project FEBuilderGBA.CLI -- --export-data --rom=rom.gba --table=units --out=units.tsv  # 40 tables: units, classes, items, portraits, sound_room, sound_boss_bgm, support_units, support_talks, support_attributes, event_haiku, event_battle_talk, event_force_sortie, worldmap_points, worldmap_paths, worldmap_bgm, map_settings, link_arena_deny, cc_branch, menu_definitions, item_weapon_triangle, map_exit_points, ai_map_settings, ai_perform_items, ai_perform_staff, ai_steal_items, ai_targets, generic_enemy_portraits, status_options, ed_retreat, ed_epithet, ed_epilogue_a, ed_epilogue_b, ed_epilogue_c, op_class_demo, op_class_font, op_prologue, class_alpha_names, summon_units, summons_demon_king, monster_probability
dotnet run --project FEBuilderGBA.CLI -- --export-data --rom=rom.gba --table=all --out=data
dotnet run --project FEBuilderGBA.CLI -- --import-data --rom=rom.gba --table=units --in=units.tsv
dotnet run --project FEBuilderGBA.CLI -- --data-roundtrip --rom=rom.gba --table=all
dotnet run --project FEBuilderGBA.CLI -- --lastrom
dotnet run --project FEBuilderGBA.CLI -- --force-detail
dotnet run --project FEBuilderGBA.CLI -- --translate_batch --rom=rom.gba --out=texts.tsv
dotnet run --project FEBuilderGBA.CLI -- --test --rom=rom.gba
dotnet run --project FEBuilderGBA.CLI -- --testonly --rom=rom.gba
dotnet run --project FEBuilderGBA.CLI -- --rom-info --rom=rom.gba
dotnet run --project FEBuilderGBA.CLI -- --list-tables
dotnet run --project FEBuilderGBA.CLI -- --export-palette --rom=rom.gba --addr=0x5524 --out=palette.pal --colors=16
dotnet run --project FEBuilderGBA.CLI -- --import-palette --rom=rom.gba --addr=0x5524 --in=palette.pal

# Build SkiaSharp image backend
dotnet build FEBuilderGBA.SkiaSharp/FEBuilderGBA.SkiaSharp.csproj

# Build Avalonia GUI
dotnet build FEBuilderGBA.Avalonia/FEBuilderGBA.Avalonia.csproj

# Run Avalonia GUI with a ROM
dotnet run --project FEBuilderGBA.Avalonia -- --rom path/to/rom.gba

# Run Avalonia smoke test (loads ROM, opens editors, selects items, verifies no crash)
dotnet run --project FEBuilderGBA.Avalonia -- --rom path/to/rom.gba --smoke-test

# Run Avalonia data verification (loads ROM, opens editors, cross-checks ViewModel data vs raw ROM + NumericUpDown UI display + text encoding)
dotnet run --project FEBuilderGBA.Avalonia -- --rom path/to/rom.gba --data-verify

# Run full data verification (iterates ALL list items per editor, per-field cross-check via GetFieldOffsetMap)
dotnet run --project FEBuilderGBA.Avalonia -- --rom path/to/rom.gba --data-verify-full

# Capture Avalonia screenshots of all editors (saves PNGs to --screenshot-dir)
dotnet run --project FEBuilderGBA.Avalonia -- --rom path/to/rom.gba --screenshot-all --screenshot-dir=./screenshots

# Capture WinForms screenshots of all editors (saves PNGs for side-by-side comparison with Avalonia)
FEBuilderGBA.exe --rom path/to/rom.gba --screenshot-all --screenshot-dir=./screenshots

# NOTE: Avalonia and WinForms share the same config.xml for settings.
# The Avalonia Options dialog exposes 20+ external tool paths (emulator,
# binary_editor, sappy, event_assembler, devkitpro_eabi, etc.) using the same
# config keys as WinForms, and still reads legacy Avalonia-only keys such as
# Emulator_Path/BinaryEditor_Path during upgrade so existing settings keep working.

# Export decoded graphics editor images (for cross-platform pixel comparison)
# Exports 16 editors: PortraitViewer, BattleBGViewer, BattleTerrainViewer, BigCGViewer,
# ChapterTitleViewer, ChapterTitleFE7Viewer, ItemIconViewer, SystemIconViewer,
# OPClassFontViewer, OPPrologueViewer, ImagePortraitFE6, ImageBG, ImageCG,
# ImageCGFE7U, ImageTSAAnime, ImageBattleBG
dotnet run --project FEBuilderGBA.Avalonia -- --rom path/to/rom.gba --export-editor-images --screenshot-dir=./editor_images
FEBuilderGBA.exe --rom path/to/rom.gba --export-editor-images --screenshot-dir=./editor_images

# Validate image import roundtrip (export→import→export→compare for all graphics editors)
# Validated on all 5 ROM variants: FE6, FE7J, FE7U, FE8J, FE8U
# Note: Image import auto-expands ROM (up to 32MB max) when no free space is found,
# appending data to the end of the ROM rather than overwriting existing data.
# Shared palette detection: If a palette pointer is referenced by multiple entries,
# the import remaps pixel indices to the existing palette instead of overwriting it,
# preserving visual consistency for all entries sharing that palette.
# BG255/BG224 (255-color cutscene backgrounds): on ROMs with the BG256Color patch,
# the Avalonia ImageBG editor imports and previews 255/224-color backgrounds (8bpp
# LZ77 tiles + 512-byte palette + P4 mode flag), mirroring WinForms ImportButton255
# via the cross-platform ImageBG256ColorCore (224-color mode rejects any pre-remap
# pixel index >= 224 — indices must be 0..223 — rather than silently blacking them
# out; the import is fixed to 256x160).
dotnet run --project FEBuilderGBA.Avalonia -- --rom path/to/rom.gba --validate-import

# Validate palette roundtrip (export palette→import palette→re-export→binary compare)
# Tests all pointer-based palette editors (BattleBG, ImageCG, ImageBG, TSAAnime,
# OPPrologue, BigCG, BattleTerrain, Portrait).
# The standalone Palette Editor (ImagePalletView) also supports palette-file
# Import/Export plus a "Clipboard" copy (RRGGBB,RRGGBB,... of the 16 displayed
# colors), mirroring ImageBG's palette path via PaletteCore + PaletteFormatConverter.
# Also validates roundtrip through each supported palette format:
#   - JASC-PAL (.pal) — Aseprite, GIMP, Paint Shop Pro (text: "JASC-PAL\n0100\nN\nR G B\n...")
#   - Adobe ACT (.act) — Photoshop (binary: 256×3B RGB, optional 4B footer)
#   - GIMP GPL (.gpl) — GIMP (text: "GIMP Palette\nName:...\nR G B\tname\n")
#   - Hex Text (.txt) — Universal (one RRGGBB per line)
#   - GBA Raw (.gbapal) — Raw BGR555 LE, 2 bytes/color (backward compat)
# Export: format auto-selected from file extension (.pal → JASC-PAL by default)
# Import: format auto-detected from file content/header, then extension, then GBA raw fallback
dotnet run --project FEBuilderGBA.Avalonia -- --rom path/to/rom.gba --validate-palette

# Avalonia ↔ WinForms gap-sweep — Phases 1/2/4/5/6 (static analysis, no ROM needed)
# Generates markdown reports under docs/avalonia-gaps/ ranking every paired editor
# by control-count delta, label-set diff, cross-editor navigation parity, undo
# coverage, and localisation. See docs/avalonia-gaps/README.md for the multi-axis
# methodology (Phase 1 = density, Phase 2 = label diff, Phase 3 = side-by-side
# screenshot gallery, Phase 4 = headless jump/navigation parity, Phase 5 = undo
# coverage, Phase 6 = localisation, Phase 7 = meta-CI).
dotnet run --project FEBuilderGBA.Avalonia -- --gap-sweep-density --out=docs/avalonia-gaps/$(date +%F)-density-sweep.md
dotnet run --project FEBuilderGBA.Avalonia -- --gap-sweep-labels  --out=docs/avalonia-gaps/$(date +%F)-labels-sweep.md
dotnet run --project FEBuilderGBA.Avalonia -- --gap-sweep-jumps   --out=docs/avalonia-gaps/$(date +%F)-jumps-sweep.md
dotnet run --project FEBuilderGBA.Avalonia -- --gap-sweep-undo    --out=docs/avalonia-gaps/$(date +%F)-undo-sweep.md
dotnet run --project FEBuilderGBA.Avalonia -- --gap-sweep-l10n --languages=ja,zh,ko --out=docs/avalonia-gaps/$(date +%F)-l10n-sweep.md
dotnet run --project FEBuilderGBA.Avalonia -- --gap-sweep-density --dry-run --out=tmp/density.md

# Avalonia ↔ WinForms gap-sweep — Phase 3 (requires ROM — drives both
# --screenshot-all runners against the chosen ROM then pairs the captured
# PNGs). PNGs are gitignored; only the index.md manifest is committed.
./scripts/make-screenshots.ps1 -Rom roms/FE8U.gba

# Cross-platform publish (self-contained)
./scripts/publish-all.sh linux-x64 osx-arm64 win-x64

# Run cross-platform tests
dotnet test FEBuilderGBA.Core.Tests/FEBuilderGBA.Core.Tests.csproj
```

### Architecture Diagram

```
FEBuilderGBA.sln
├── FEBuilderGBA.Core/           net9.0    (cross-platform core)
│   ├── IAppServices.cs                     Platform abstraction
│   ├── IImageService.cs                    Image service abstraction
│   ├── Rom.cs / ROMFE*.cs                  ROM manipulation
│   ├── UPSUtil.cs                          UPS patch creation
│   ├── FELintCore.cs                       Lint validation
│   ├── PathUtil.cs                         Cross-platform paths
│   ├── PointerCalcCore.cs                 Pointer search engine
│   ├── RebuildCore.cs                     ROM defragmentation
│   ├── SongExchangeCore.cs                Song exchange between ROMs
│   ├── MapConvertCore.cs                  Map tile conversion
│   ├── NameResolver.cs                    Entity name resolution with caching
│   └── WriteValidator.cs                  ROM write validation utilities
├── FEBuilderGBA.CLI/            net9.0    (cross-platform CLI — 51 commands)
├── FEBuilderGBA.SkiaSharp/      net9.0    (image backend)
├── FEBuilderGBA.Avalonia/       net9.0    (cross-platform GUI — 325 editors, with ambient undo, dirty tracking, data export/import, full Options dialog with 20+ external tool paths)
├── FEBuilderGBA/                net9.0-windows (WinForms GUI)
├── FEBuilderGBA.Tests/          net9.0-windows (unit tests)
├── FEBuilderGBA.Core.Tests/     net9.0    (cross-platform tests)
└── FEBuilderGBA.E2ETests/       net9.0-windows (E2E tests)
```

## Testing & Coverage

- ✅ **2670 unit/integration tests** passing (1666 WinForms/Avalonia + 1004 Core cross-platform)
- ✅ **30 E2E tests** passing without ROMs (CLI + GUI automation + output log capture); **140 E2E tests** passing with all 5 ROMs (including 325-editor Avalonia smoke test, screenshot capture for both GUIs, + CLI output log capture for both CLI and WinForms executables)
- 📊 [View Full Coverage Report on Codecov](https://codecov.io/gh/laqieer/FEBuilderGBA)
- 🔍 Latest test results and coverage reports available as [GitHub Actions artifacts](https://github.com/laqieer/FEBuilderGBA/actions)
- 🧪 **Test Coverage:**
  - Unit tests for core utilities (RegexCache, LZ77, U, TextEscape, CoreState, Elf, SystemTextEncoderTBLEncode, MultiByteJPUtil, MyTranslateResource, EtcCacheResource, GitUtil, GitInstaller, AddrResult, ArchSevenZip, NewEventASM, ExportFunction, UpdateInfo, TranslateManager, DisassemblerTrumb, AsmMapSt, GbaBiosCall, R, Log, Mod, PatchDetection, FETextEncode, FETextDecode, TranslateCore, DecreaseColorCore sub-flags)
  - UpdateInfo version tracking and comparison
  - Core package download logic
  - Integration tests for update system
  - E2E CLI tests (`--version` flag, exit codes, output content, `--help` coverage)
  - CLI arg parsing tests (all 19 commands with complete argument sets)
  - E2E GUI tests (startup window detection, child controls, graceful shutdown)
  - ROM-based E2E CLI tests (`--lint`, `--makeups` × 5 ROMs, `--rebuild` × 2 representative ROMs — skipped without ROMs)
  - ROM-based E2E GUI tests (main form loads, title, child controls × 5 ROMs — skipped without ROMs)
  - Form smoke tests (all toolbar buttons × 5 ROMs — skipped without ROMs)
  - Avalonia editor smoke tests: Unit/Item editor selection (× 5 ROMs — skipped without ROMs)
  - Avalonia all-editors smoke test: all 325 GUI editors open/close (× 5 ROMs — skipped without ROMs)
- Avalonia data verification: `--data-verify` mode cross-checks ViewModel fields against raw ROM bytes, verifies NumericUpDown UI controls display values, validates text encoding (Shift-JIS for JP ROMs, ISO-8859-1 for US ROMs), and skips helper/context-only editors when they have no comparable ROM-backed record instead of reporting false mismatches (× 5 ROMs — skipped without ROMs). `--data-verify-full` mode iterates ALL list items per editor (not just the first) and performs per-field cross-checking via `GetFieldOffsetMap()` to verify each ViewModel field maps to the correct raw ROM byte offset, reporting `FIELDMISMATCH` lines for any discrepancy.
  - **Field completeness tests**: `AvaloniaFieldCompletenessTests` compares WinForms Designer.cs ROM data field controls against Avalonia ViewModel ROM access patterns across all 170 mapped forms (1562 WinForms fields, 0 gaps). Tests are **strict** — they fail on any gap, type/offset mismatch, or unmapped ROM-field form. Includes cross-checks: `AllFormFields_TypeAndOffsetMatch` verifies ROM read types match WinForms field types, `AllViewModels_ReportMethodsAreConsistent` verifies GetDataReport/GetRawRomReport key consistency, `MappedVMs_RawRomReport_CoversRomReads` enforces ≥60% raw ROM report coverage for all mapped VMs, `NoOrphanVMs_ImplementIDataVerifiable` prevents non-data-editor VMs from implementing IDataVerifiable, and `AllDesignerFilesWithRomFields_HaveAvaloniaMapping` auto-discovers ALL Designer.cs files with ROM fields to prevent new forms from being invisible to tests. Orphan cleanup removed IDataVerifiable from 49 non-editor VMs (dialogs, tools, infrastructure). Reports in `docs/field-completeness-report.txt`

## E2E Automation Tests

The project includes a dedicated end-to-end test suite (`FEBuilderGBA.E2ETests`) that covers both CLI and GUI behavior by launching the real application executable.

### Test Categories

| Test File | ROMs required | What it tests |
|-----------|--------------|--------------|
| `Tests/CliTests.cs` | No | CLI flag `--version`: exit code 0, output contains "FEBuilderGBA" and version info |
| `Tests/CliArgsE2ETests.cs` | No | All 18 CLI primary commands via `FEBuilderGBA.CLI`: `--help/-h`, `--version`, `--makeups`, `--applyups`, `--lint`, `--disasm`, `--decreasecolor`, `--pointercalc`, `--rebuild`, `--songexchange`, `--convertmap1picture`, `--translate`, `--translate-roundtrip`, `--lastrom`, `--force-detail`, `--translate_batch`, `--test/--testonly` — 38 tests ([docs/cli-args.md](docs/cli-args.md)) |
| `Tests/GuiStartupTests.cs` | No | GUI startup: window appears within 30 s, has non-empty title, has child controls, responds to WM_CLOSE |
| `Tests/DiagnosticTests.cs` | No | Diagnostic: logs all window handles, titles (hex-encoded), and class names — always passes |
| `Tests/RomCliTests.cs` | Yes (×5/×2) | `--lint`, `--makeups` × 5 ROMs; `--rebuild` × 2 representative ROMs (FE8U, FE6) — 12 tests, skipped without ROMs |
| `Tests/RomGuiTests.cs` | Yes (×5) | Main form loads per ROM: window appears, non-empty title, ≥10 child controls — 15 tests, skipped without ROMs |
| `Tests/FormSmokeTests.cs` | Yes (×5) | All toolbar buttons clicked per ROM; verifies ≥1 opens a form — 5 tests, skipped without ROMs |
| `Tests/AvaloniaEditorSmokeTests.cs` | Yes (×5) | Avalonia: ROM load + Unit/Item editor selection per ROM — 10 tests, skipped without ROMs |
| `Tests/AvaloniaAllEditorsSmokeTests.cs` | Yes (×5) | Avalonia: all 325 GUI editors opened/closed per ROM via `--smoke-test-all` — 10 tests, skipped without ROMs ([docs/avalonia-gui-forms.md](docs/avalonia-gui-forms.md), [docs/avalonia-forms.md](docs/avalonia-forms.md)) |
| `Tests/CliOutputLogNoRomTests.cs` | No | New CLI output log capture: `--help`, `-h`, `--version`, `--force-detail`, `--test`, `--testonly`, no args, `--bogus-command` — 8 tests |
| `Tests/CliOutputLogRomPart1Tests.cs` | Yes (×5/×2) | New CLI ROM output logs: `--lint` ×5, `--disasm` ×5, `--translate` ×5, `--rebuild` ×2 — 17 tests, skipped without ROMs |
| `Tests/CliOutputLogRomPart2Tests.cs` | Yes (×5/×2) | New CLI ROM output logs: `--makeups` ×5, `--applyups` ×2, `--pointercalc` ×2, `--songexchange` ×2 — 11 tests, skipped without ROMs |
| `Tests/CliOutputLogImageTests.cs` | No | New CLI image output logs: `--decreasecolor` (5 flag variants), `--convertmap1picture` — 6 tests |
| `Tests/WinFormsCliOutputLogNoRomTests.cs` | No | WinForms CLI output log capture: `--version`, no args, `--bogus-command` — 3 tests |
| `Tests/WinFormsCliOutputLogRomTests.cs` | Yes (×5/×2) | WinForms CLI ROM output logs: `--lint` ×5, `--rebuild` ×2, `--makeups` ×5, `--disasm` ×2, `--translate` ×2, `--pointercalc` ×2, `--songexchange` ×2 — 20 tests, skipped without ROMs |
| `Tests/AvaloniaScreenshotTests.cs` | Yes (×2) | Avalonia: captures PNG screenshots of all 325 editors via `--screenshot-all` — 4 tests, skipped without ROMs |
| `Tests/WinFormsScreenshotAllTests.cs` | Yes (×2) | WinForms: screenshots of main form + all toolbar-openable editor forms — 4 tests, skipped without ROMs |
| `Tests/WinFormsScreenshotAllCliTests.cs` | Yes (×2) | WinForms: captures screenshots of all editors via `--screenshot-all` CLI flag — 4 tests, skipped without ROMs |
| `Tests/EditorImageComparisonTests.cs` | Yes (×1) | Cross-platform image export + pixel-perfect comparison for 16 editors: `--export-editor-images` on both WinForms and Avalonia — 3 tests, strict assertions, skipped without ROMs |

**Without ROMs:** 30 passed, 112 skipped. **With all 5 ROMs:** 142 passed, 0 skipped.

### Avalonia UI Automation Testing

All 361 Avalonia `.axaml` files (360 views + 1 dialog) have `AutomationProperties.AutomationId` attributes on every interactive control, enabling reliable UI automation testing with tools like Appium, FlaUI, or MCP Computer Use.

**3,132 unique AutomationIds** follow the naming convention `{EditorName}_{FieldName}_{ControlType}`:

| Suffix | Control Types |
|--------|--------------|
| `_Input` | TextBox, NumericUpDown, Slider |
| `_Combo` | ComboBox |
| `_Button` | Button, MenuItem |
| `_List` | ListBox, ListView, ItemsControl |
| `_Check` | CheckBox, ToggleButton, RadioButton, BitFlagPanel |
| `_Expander` | Expander |
| `_TabControl` / `_Tab` | TabControl, TabItem |
| `_Image` | Image, GbaImageControl, IconPreviewControl |
| `_Label` | TextBlock (dynamic/bound only) |

**Exempt files** (no AutomationIds — reusable controls instantiated multiple times):
- `Controls/BitFlagPanel.axaml`, `Controls/AddressListControl.axaml`, `Controls/GbaImageControl.axaml`, `Controls/IconPreviewControl.axaml`, `Controls/IdFieldControl.axaml`, `App.axaml`

**Scripts:**
- `scripts/add-automation-ids.ps1` — adds/refreshes AutomationIds across all .axaml files
- `scripts/validate-automation-ids.ps1` — validates coverage, naming, and uniqueness (exit 0 = pass, 1 = fail)

**Tests** (`FEBuilderGBA.Avalonia.Tests/AutomationIdTests.cs`):
- Per-editor assertions (UnitEditor, ClassEditor, ItemEditor, MessageBox)
- Naming convention compliance (>99% threshold)
- No duplicate IDs within any single view
- Minimum coverage threshold (>2000 IDs, >90% view coverage)
- Static .axaml source file checks (>95% files have IDs)
- Exempt file verification (reusable controls have no IDs)

### Running E2E Tests Locally

**Prerequisites:**  Build the main app first.

```bash
# Build the main application (Release, x86)
msbuild FEBuilderGBA.sln /p:Configuration=Release /p:Platform=x86 /t:build /restore

# Run without ROMs — 13 passed, 32 skipped (fast, ~20 s)
ROMS_DIR="" dotnet test FEBuilderGBA.E2ETests/FEBuilderGBA.E2ETests.csproj -c Release --no-build

# Run with ROMs — all 45 tests execute
ROMS_DIR=/path/to/roms dotnet test FEBuilderGBA.E2ETests/FEBuilderGBA.E2ETests.csproj -c Release --no-build
```

ROM files expected in `ROMS_DIR`: `FE6.gba`, `FE7J.gba`, `FE7U.gba`, `FE8J.gba`, `FE8U.gba`.

If `ROMS_DIR` is **not set at all**, `RomLocator` falls back to a `roms/` directory beside `FEBuilderGBA.sln` (useful during local development).  Set `ROMS_DIR=""` to explicitly suppress that fallback and force all ROM tests to skip.

Or point to an already-built binary:

```bash
export FEBUILDERGBA_EXE=/path/to/FEBuilderGBA.exe
ROMS_DIR="" dotnet test FEBuilderGBA.E2ETests/FEBuilderGBA.E2ETests.csproj -c Release --no-build
```

### CI/CD Integration

E2E tests are split into 6 parallel GitHub Actions workflows (`.github/workflows/e2e-*.yml`) — one no-ROM workflow and one per ROM variant (FE6, FE7J, FE7U, FE8J, FE8U). All share a reusable workflow (`e2e-run.yml`) and run in parallel, reducing wall-clock time from ~30 min to ~12 min. Each per-ROM workflow downloads `roms.zip` but keeps only its target ROM, so tests for other ROMs auto-skip.

ROM-based tests are gated on the `ROMS_URL` repository secret.  When the secret is present the workflow attempts to download `roms.zip`, validate it, extract it, and set `ROMS_DIR` for the test run.  When the secret is absent (forks, external PRs) the Download ROMs step is skipped entirely and all 35 ROM tests skip cleanly.

**ROM download — tiered failure policy:**
| Situation | Behaviour |
|-----------|-----------|
| `ROMS_URL` secret absent | Step skipped; ROM tests skip via `Assert.Skip()` |
| Network/HTTP error (unreachable URL) | Hard fail → pipeline blocked |
| Downloaded file not a valid zip (magic bytes ≠ `PK`) | Warning + exit 0; ROM tests skip |
| Zip structurally corrupt (`ZipFile::OpenRead` fails) | Warning + exit 0; ROM tests skip |
| Zip valid, all 5 ROMs extracted | All 45 tests run |

The step lists every zip entry with its uncompressed size before extraction, so the log shows exactly what is inside `roms.zip`.

**Artifacts produced:**
- `e2e-test-report` — TRX test report (viewable via the **E2E Test Results** check-run posted by `dorny/test-reporter`)
- `e2e-screenshots` — PNG screenshots of all GUI forms captured during E2E tests (Avalonia `Avalonia_*.png` + WinForms `WinForms_*.png`)
- `cli-output-logs` — `.log` files capturing stdout/stderr/exit code for every CLI command (both New CLI and WinForms CLI), useful for regression tracking

**Implementation notes:**
- Tests run sequentially (`[assembly: CollectionBehavior(DisableTestParallelization = true)]`) — each GUI test launches an exclusive app process; concurrent launches cause window-detection races
- Window detection polls **all process windows** via `EnumWindows` rather than relying on `Process.MainWindowHandle`, which can point to a transient splash/startup dialog before the main editor form appears
- Win32 `GetWindowText` P/Invoke uses `CharSet.Unicode` to correctly handle CJK characters; title-based detection is avoided for startup state (the app shows a Chinese "初始设置向导" Init Wizard on first run)
- CLI argument values must use `--key=value` (equals) syntax — `Program.ArgsDic` is built by `U.OptionMap` which only recognises the `=` separator (space-separated values are only picked up via a `File.Exists` fallback, which does not apply to output paths that don't yet exist)
- `AppRunner.Run()` calls `WaitForExit()` (no-param) after `WaitForExit(timeout)` to flush async `OutputDataReceived` events before reading captured stdout
- `RomLocator` treats any explicit `ROMS_DIR` value (even empty string) as an override — only when the variable is **absent** from the environment does the walk-up fallback activate

## 🔄 Update System

FEBuilderGBA uses a two-track update model that keeps the application and patch data independent:

### How It Works

| Component | What it contains | How it updates |
|-----------|-----------------|----------------|
| **Core** | FEBuilderGBA.exe, DLLs, config data | Download `FEBuilderGBA_YYYYMMDD.HH.zip` from GitHub Releases or nightly.link |
| **Patch2** | ~44,000 patch files in `config/patch2/` | `git fetch` + `git reset --hard` via the built-in Git updater |

When you check for updates the app compares the remote version against the local assembly build date and shows only the relevant update button(s).

### Updating Patch2 via Git

Patch2 is a [git submodule](https://github.com/laqieer/FEBuilderGBA-patch2) updated independently of core releases.

- **In-app:** Tools → Check for Updates → "Gitでパッチデータを更新します"
- **Manual:** `cd config/patch2 && git pull`
- **First run:** The app detects missing patch2 directories and offers to clone them automatically. If Git is not installed, empty directories are created so the app still starts.

The app automatically selects the patch2 git source based on your **Options → Release Source** setting — the same setting that controls where the core update is downloaded from:

| Release Source setting | Patch2 git remote used |
|------------------------|------------------------|
| Auto (Chinese language detected) | `gitee.com/laqieer/FEBuilderGBA-patch2` |
| Gitee | `gitee.com/laqieer/FEBuilderGBA-patch2` |
| GitHub / Nightly | `github.com/laqieer/FEBuilderGBA-patch2` |

### Benefits

- ✅ **Incremental patch updates** — only changed patch files are transferred via git
- ✅ **Faster patch updates** — no ZIP download or extraction required
- ✅ **Offline-friendly** — patch2 can be updated separately from the core app
- ✅ **Git history** — full audit trail of every patch data change

### Version Information

- **Core version:** Help → About
- **Patch2 version:** `git -C config/patch2 log -1 --format="%h %s"`

[This fork](https://github.com/laqieer/FEBuilderGBA/) is an integration of several forks of FEBuilderGBA and continues development based on it.

## MCP Computer Use (Windows)

An MCP (Model Context Protocol) server that gives Claude Code screenshot, mouse, and keyboard control for GUI testing. Windows-only, requires Python 3.10+.

### Setup

```bash
# Create venv and install dependencies
cd tools/mcp-computer-use
python -m venv .venv
.venv/Scripts/pip install -r requirements.txt

# Verify server starts (Ctrl+C to stop)
.venv/Scripts/python server.py
```

The `.mcp.json` at the repo root auto-configures Claude Code to use the server as `febuildergba-computer-use`. After setup, its tools (screenshot, click, type_text, key_press, mouse_move, scroll, drag, get_screen_size, wait, find_window, focus_window) appear in Claude Code sessions opened from this repo.

README for Korean character table
===

It is from an [unofficial build](https://github.com/delvier/FEBuilderGBA) of FEBuilderGBA that supports Korean character table.

The character table used is **Johab**, only for the Hangul Syllables part. If you want to use another character table like Wansung or Windows-949, you may replace __FE\[678\].tbl__ in __./config/translate/ko_tbl__.

Since this fork is incomplete, there might be some issues that raw code points appear can be occurred, e.g. '@61A0' rather than '마' (0xA061) appears. This is likely because the upper bytes from 0xA0 to 0xDF are used for single-byte representation in Shift JIS and Windows-932.

You should change "Text Encoding in ROM" in Options manually every time the ROM is loaded.

Original README
===

FE_Builder_GBA
===
This is a ROM hacking suite for the Trilogy of Fire Emblem games for the Game Boy Advance.
The editor supports
 * FE6 (The Binding Blade)
 * FE7J/FE7U (The Blazing Blade)
 * FE8J/FE8U (The Sacred Stones)
Essentially, both Japanese and North American releases of all games (with the exception of FE6 being Japan-only) are supported.

Starting from the main screen, FEBuilder supports a wide range of functions from image displaying, importing and export of most data, map remodeling, table editing, community patch management, music insertion, and much more.

This suite was made at first to help make my Kaitou patch easier to create!

The origin of the name is from 某LAND.
However, the development language is C#. (We're in this together...)

Of course, it's open source.
The license of the source code is GPL3.
Please use it freely with no limitations.

Much of this project's functions are thanks to the data collected by various communities and people.
We would like to thank our hacking predecessors who have publicly shared any analyzed data.

Details (There is a commentary at the bottom of the page, and the wiki provides other instructions)
https://dw.ngmansion.xyz/doku.php?id=en:guide:febuildergba:index

Some poorly designed anti-virus software may misidentify FEBuilderGBA as a virus.
This is because FEBuilderGBA uses the WindowsDebugAPI to communicate with the emulator.
Please configure your anti-virus to exclude the FEBuilderGBA directory.
FEBuilderGBA is NOT virus.
The source code is all available on github, so you can build it yourself if you are worried.


This software has no association with the official products.
We do not need any donations as we are making this software non-commercial.

If you really want to donate to someone, donate to the charitable organization supporting the freedom of speech on the Internet, **Freedom of Expression**, including the **EFF Electronic Frontier Foundation**.

Of course, you are free to write articles about FEBuilderGBA.
In some cases, you may earn some pocket money through affiliates. :)
However, please do it at your own risk. :(

If you have something you do not understand through hacking or the editor, please read "Manual" in "Help".
If you find a bug that you can not solve by any means, please create report.7z from 'File' -> 'Menu' -> 'Create Report Issue' and consult with the community.
https://discordapp.com/invite/Yzztqqa
Do NOT send your ROM (.gba) directly.

SourceCode:
https://github.com/FEBuilderGBA/FEBuilderGBA

Installer:
https://github.com/FEBuilderGBA/FEBuilderGBA_Installer/releases/download/ver_20200130.17.1/FEBuilderGBA_Downloader.exe


FE_Builder_GBA
===
FE GBA 3部作のROMエディターです。
FE8J FE7J FE6 FE8U FE7U に対応しています。

Project_FE_GBA の画面を参考に、
新規に判明した部分を追加しました。
画像表示やインポートエクスポート、マップ改造まで幅広い機能をサポートします。

怪盗パッチを作っているときに思った、こんな機能が欲しい!!という機能をすべて入れ込みました。

名前の由来は、 某LANDのアレからです。
ただし、開発言語はC# です。 (中の人達は一緒だしね・・・)
C#でありますが、特にパフォーマンスに注意しているので、サクサク動くかと思います。

当然、オープンソース。ソースコードのライセンスは GPL3 です。
ご自由にご利用ください。

これを作るのに、いろいろいなデータ、コミニティを参考にしました。
解析したデータを公開してくれた先人にお礼を申し上げます。


詳細 (ページ下部に解説集があるよ)
https://dw.ngmansion.xyz/doku.php?id=guide:febuildergba:index

一部の出来の悪いアンチウイルスソフトが、FEBuilderGBAをウイルスと誤認することがあるようです。
これは、FEBuilderGBAがエミュレータと通信するためにWindowsDebugAPIを利用しているからだと思います。
もしそうなったら、アンチウイルスの設定で、FEBuilderGBAディレクトリを除外してください。
FEBuilderGBAはウイルスではありません。
ソースコードはすべてgithubで公開しているので、心配な場合は自分でビルドしてください。


このソフトウェアは、公式とは一切関係ありません。
私達は非営利でこのソフトウェアを作っているので、寄付を必要としません。
どうしても寄付したい方は、EFF 電子フロンティア財団を始めとする、インターネットでの言論の自由、表現の自由を支援している慈善団体にでも寄付してください。

もちろん、あなたがFEBuilderGBAに関する記事を書くのは自由です。
場合によっては、アフェリエイトでお小遣いを稼ぐこともできるでしょう。 :)
ただし、あなたの責任において実施してください。 :(

もし、hackromでわからないことがあれば、「ヘルプ」の「マニュアル」を読んでください。
どうしても解決しないバグが発生した場合は、「メニュー」の「ファイル」->「問題報告ツール」から、report.7zを作成して、コミニティに相談してください。
https://discordapp.com/invite/Yzztqqa
(ROMは送信しないでください。)

SourceCode:
https://github.com/FEBuilderGBA/FEBuilderGBA

Installer:
https://github.com/FEBuilderGBA/FEBuilderGBA_Installer/releases/download/ver_20200130.17.1/FEBuilderGBA_Downloader.exe


FE_Builder_GBA
===
它是FE GBA三部曲的ROM编辑器。
它对应于 FE8J FE7J FE6 FE8U FE7U.

参考Project_FE_GBA的屏幕，
我添加了一个新发现的部分。
我们支持图像显示，导入导出，地图重构等功能。

当我制作一个kaitou补丁时，我想要这样的功能

这个名字的起源是来自 某LAND。
但是，开发语言是C＃。 （里面的人在一起...）
它是C＃，但我担心性能，所以我认为它会工作很好。

当然，开源。源代码的许可证是GPL3。
请自由使用。

我参考了各种数据和社区来做到这一点。
我要感谢发布分析数据的前辈。


详细信息（页面底部有评论）
https://dw.ngmansion.xyz/doku.php?id=zh:guide:febuildergba:index

Some poorly designed anti-virus software may misidentify FEBuilderGBA as a virus.
This is because FEBuilderGBA uses the WindowsDebugAPI to communicate with the emulator.
Please configure your anti-virus to exclude the FEBuilderGBA directory.
FEBuilderGBA is NOT virus.
The source code is all available on github, so you can build it yourself if you are worried.


这个软件与官方无关。
我们不需要捐赠，因为我们正在制作该软件的非营利。
如果你真的想捐赠，
捐赠给支持言论自由的慈善组织，包括EFF电子前沿基金会在内的言论自由

当然，您可以自由撰写关于FEBuilderGBA的文章。
在某些情况下，您可以通过会员赚取零用钱。 :)
但是，请自行承担风险。 :(

如果你有一些你从hackrom不能理解的东西，请阅读“帮助”中的“手册”。
如果您发现无法解决的错误，请在'菜单'的'文件' -> '问题报告工具'中创建report.7z，并咨询社区。
https://discordapp.com/invite/Yzztqqa
（请不要发送ROM。）

SourceCode:
https://github.com/FEBuilderGBA/FEBuilderGBA

Installer:
https://github.com/FEBuilderGBA/FEBuilderGBA_Installer/releases/download/ver_20200130.17.1/FEBuilderGBA_Downloader.exe
