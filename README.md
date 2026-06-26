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
[![Android](https://github.com/laqieer/FEBuilderGBA/actions/workflows/android.yml/badge.svg)](https://github.com/laqieer/FEBuilderGBA/actions/workflows/android.yml)
[![Android Emulator Parity](https://github.com/laqieer/FEBuilderGBA/actions/workflows/android-emulator-parity.yml/badge.svg)](https://github.com/laqieer/FEBuilderGBA/actions/workflows/android-emulator-parity.yml)

Mirrors for Chinese mainland users (面向中国大陆用户的镜像发布地址): [![Gitee Release](https://gitee-badge.vercel.app/svg/release/laqieer/FEBuilderGBA?style=flat)](https://gitee.com/laqieer/FEBuilderGBA/releases/latest) [![Gitee Go Build](https://gitee.com/laqieer/FEBuilderGBA/widgets/widget_5.svg)](https://gitee.com/laqieer/FEBuilderGBA/gitee_go/pipelines?tab=release)

## 🚀 Getting Started

### Project Structure

| Project | Target | Description |
|---------|--------|-------------|
| `FEBuilderGBA.Core` | net9.0 | Cross-platform core library (ROM, Undo, LZ77, text encoding, Huffman codec, patch detection, translation, cache, git, archive, event ASM, disassembler, export, mod, address, event script, EtcCache, symbol util, magic split, grow simulator, system text encoder, config persistence, GDB socket, event script util, EA lyn dump parser, lint core types/validation, UPS patch, image service abstraction, path utilities, logging facade, utilities, HeadlessEtcCache, HeadlessSystemTextEncoder, MapSettingCore, StructMetadata, StructExportCore, FELintScanner, DisassemblerCore, DisASMArgGrepCore (read-only register-flow "Disassembly Argument Grep" porting WinForms `DisASMDumpAllArgGrepForm.Grep`/`IsSearchRegister` — anchor on a `mov`/`ldr` register-set, find the target call within an allowed-rows window, emit the argument-setup block; case-sensitive verbatim matching, hide-function-call / hide-unknown-arg filters, target-function hex-or-symbol normalization; drives the Avalonia Disassembly Argument Grep view with all 5 options wired, #1463), HexEditCore (ROM-mutating writable Hex Editor edit-commit path porting WinForms `HexEditorForm.WriteButton_Click` — `ParseDisplay` re-parses the edited hex page deriving every byte's address POSITIONALLY from the page base so the typed address gutter is validated, never trusted as a write target, with strict positional 2-hex-digit slot validation that rejects shifted/missing/extra/non-hex tokens before any mutation; `BuildEdits` diffs vs the ROM so only edited cells are written; `ApplyWrite` resizes-if-larger then `rom.write_u8` per cell under the ambient undo scope; wires the Avalonia Hex Editor's now-editable grid + Write button/Ctrl+S, #1466), ImageUtilCore, ImageImportCore, DecreaseColorCore, DecreaseColorConvertCore, PointerCalcCore, RebuildCore, SongExchangeCore (cross-ROM song transplant — InstrumentMap/Rip/Burn port with sample recycling + pointer fixups, validate-all-before-mutate, ambient undo, no ROM growth), SongTrackChangeCore (single-track + bulk Track Change writer — voice remap + Vol/Pan/Tempo deltas, TEMPO clamps 0..255, isAbbreviation-aware, ambient undo), MapConvertCore, MapPListResolverCore, MapPlistSplitCore (ROM-mutating PLIST Split/Expand — ports WinForms `MapPointerForm.PListSplitsExpands`: breaks the shared PLIST table into independent 256-entry tables per purpose, lifting the usable-slot limit to 256; validate-all-before-mutate, self-contained snapshot + ambient undo, byte-identical rollback on fault; wires the Avalonia Map Pointer editor's "PLIST Split" panel shown only when not yet split, #1432), PatchFilterCore (Patch Manager filter token helper reusing PatchHardCodeScanner — handles the `HARDCODING_{UNIT|CLASS|ITEM}=NN` tokens seeded by the editors' `[HardCoding]` links and the `!` installed-only token in Avalonia `PatchManagerViewModel.ApplyFilter`, #1376), PatchMetadataCore clean-ROM-diff uninstall (`CollectPatchRegionsWithBytes`/`RomContainsPatch`/`IsCompatibleRom`/`UninstallPatchWithCleanRom` — ports WinForms `PatchForm.UninstallPatchInner` so a BIN patch installed in a PRIOR/WinForms session, with no per-patch backup file, can still be uninstalled by diffing against a user-supplied patch-free "clean" ROM: the patch-absence check is faithful to WF `SearchContainThisPatchBy` (memcmp each region's candidate bytes against the patch's OWN `.bin` bytes, not the current ROM, so a still-patched-but-edited-elsewhere ROM is rejected), gated BEFORE any mutation by a GBA-header compatibility gate + a preflight size gate (clean ROM must cover every region, else Fail not partial-Ok) + the patch-absence check; correction-only batched restore writes only the bytes that DIFFER so over-estimated region/JUMP lengths never clobber neighbours, partial/incomplete report when EA/`$FREEAREA`/`$GREP` entries can't be traced, ambient undo with byte-identical rollback; wires the previously-orphaned Avalonia `PatchFormUninstallDialogView` into the Uninstall flow, #1462), PatchHardCodeScanner + CoreAsmMapCache — patch-scan hardcode detection lighting up the Avalonia/CLI Unit/Class/Item `[HardCoding]` warning, plus `GetAsmMapFile()` returning an `AsmMapSymbolFile` (asmmap_*.txt symbol reader — `SearchNear`/`TryGetValue`) backing the Avalonia Pointer Tool "What is this address?" symbol-name resolution, PointerToolAutoSearchCore (read-only, never-throws — cross-ROM Pointer Tool AutoSearch porting WF `PointerToolForm.AutoSearch`: ASM-map symbol NAME search via `IAsmMapFile.SearchName`/`GetName`, source↔target LDR-literal-pool-map symmetry, and auto-tracking match-window/slide retry), MakeVarsIDArrayCore + TextFreeAreaCore + AsmMapTextSymbolReader + PatchTextRefScannerCore — the definitive Text Editor free-area + cross-reference union faithfully porting `U.MakeVarsIDArray`, WorkSupportUpdateDownloadCore (the work/ROM-hack Work-Support update download+apply-UPS pipeline porting WinForms `ToolWorkSupportForm.RunDownloadAndExtract`/`DownloadAndExtract` — resolve UPDATE_URL/UPDATE_REGEX, download+extract via `ArchSevenZip`, **validate-all-before-write** apply of every staged `.ups` against a CRC32-auto-found vanilla ROM, CRC-mismatch warnings surfaced not flattened; all network/extract/ROM touches injected for offline tests; Avalonia Work Support "Update" now updates the loaded hack, not the editor's GitHub release, #1454), MonsterWMapProbabilityCore (FE8-only World Map Monster editor — ports the 3 WinForms `MonsterWMapProbabilityForm` editing surfaces the Avalonia editor dropped: stage spread `monster_wmap_stage_1/2` stride-1 Eirika/Ephraim, per-base probabilities `monster_wmap_probability_1/2` stride-9 with a live 9-cell SUM%, and the `worldmap_skirmish_startevent/endevent` skirmish-event pointers with Jump-to-EventScript; rom-aware safety guards, never throws, ambient undo; Avalonia `MonsterWMapProbabilityViewerView` gains the 3 sections with Eirika/Ephraim filter+row sync and world-map-event-kind-flagged jumps, #1464)) |
| `FEBuilderGBA` | net9.0-windows | WinForms GUI application |
| `FEBuilderGBA.CLI` | net9.0 | Cross-platform CLI tool (44 commands: `--version`, `--help`, `--makeups`, `--applyups`, `--lint`, `--lint-oam`, `--disasm`, `--disasm-event`, `--decreasecolor`, `--pointercalc`, `--rebuild`, `--songexchange`, `--convertmap1picture`, `--translate`, `--translate-roundtrip`, `--translate_batch`, `--export-data`, `--import-data`, `--data-roundtrip`, `--render-portrait`, `--export-portrait-all`, `--import-portrait`, `--generate-font`, `--export-midi`, `--import-midi`, `--apply-patch`, `--list-patches`, `--list-resources`, `--uninstall-patch`, `--expand-table`, `--merge3`, `--compile-event`, `--import-battle-anime`, `--export-battle-anime`, `--freespace`, `--hex-dump`, `--search-text`, `--resolve-names`, `--export-voicegroup`, `--export-battle-anim-decomp`, `--lastrom`, `--force-detail`, `--test`, `--testonly`; flags: `--force-version`, `--noScale`, `--noReserve1stColor`, `--ignoreTSA`, `--table`, `--patch-name`) |
| `FEBuilderGBA.SkiaSharp` | net9.0 | SkiaSharp implementation of IImageService (GBA 4bpp/8bpp tiles, palette conversion) and `SkiaFontRasterizer` — the cross-platform `IFontRasterizer` that reproduces the WinForms GDI glyph algorithm byte-for-byte so translation-font auto-generation works on Windows/Linux/macOS (#796) |
| `FEBuilderGBA.Avalonia` | net9.0 | Cross-platform Avalonia UI (~47% feature completeness). ROM loading, 356 editors: unit/item/class editors with full read/write + undo (Class Editor includes a class-card preview panel — class face portrait + name + wait icon, matching WinForms `L_8_PORTRAIT_CLASS`/`L_5_CLASS`/`L_6_CLASSICONSRC` block, see issue #357); map/event/AI/text/audio/graphics/portrait/world map/support/arena/monster/summon/menu/credits viewers; image editors with PNG import; hex editor with hex dump view, jump, search; pointer search and free space scanning tools; editor search/filter in main window; dirty-check on close; named dropdowns (`ComboResourceHelper`); bit flag panels (`BitFlagPanel`); all 148 write-enabled editors wrapped with `UndoService`; all editors use `IsLoading` guards; cross-editor navigation (Jump to Class/Portrait) with pick-and-return support (`PickFromEditor<T>()`) and reusable `IdFieldControl` (hyperlink label + value box + name preview + Jump + Pick, with optional `ShowPick=False` for fields whose target editor isn't IPickableEditor) used by CC Branch Editor, Unit FE6/FE7 Class ID fields (#366), and 32 id fields across 19 editors covering Item / Unit / Class / Song / Text IDs in ItemRandomChest, ItemWeaponEffect, MonsterItem, LinkArenaDenyUnit, SummonUnit, EDView, SummonsDemonKing (UnitId+ClassId), ArenaClass, OPClassDemoFE7/FE7U/FE8U, SoundRoomViewer, EDSensekiComment, ItemShop, ItemEffectivenessViewer, ItemPromotionViewer, and SupportTalk/FE6/FE7 (the canonical original example with 5 IdFieldControls per editor — 2 partner UnitId + 3 TextId C/B/A with ShowPick=False) (closes #360); Class Editor's Pointers/Movement/Terrain panel exposes Jump buttons next to all six pointer fields — Battle Anime, Move Cost Rain/Snow, and Terrain Avoid/Def/Res — version-aware for all five ROM variants (FE6 reuses the Rain/Snow controls for Terrain Avoid/Def at the shifted P56/P60 offsets), matching the existing Move Cost (P56) Jump button (#359); the Battle Anime (P52 FE7/8 / P48 FE6) Jump now resolves the class's battle-anime SETTING pointer to the owning class via `ClassFormCore.GetIDWhereBattleAnimeAddr` and direct-loads that animation in the Battle Animation Editor instead of falling back to entry 0 — mirroring WinForms `ImageBattleAnimeForm.JumpToAnimeSettingPointer` (#1377); ROM info display with free space analysis and data section pointers; proper list loading in class list, promo list, weapon lock (usage guide: [docs/weapon-lock-vennou-editor.md](docs/weapon-lock-vennou-editor.md)), and unit short text editors with AddressListControl and name resolution. Most window views use `SizeToContent="WidthAndHeight"` for auto-sizing (a few, e.g. the Options and Color Reduction Tool views, use `SizeToContent="Height"`), and all views include `ScrollViewer` wrappers to prevent content clipping. The Text Editor now hosts a read-only **Simple Conversation Viewer** tab that mirrors the WinForms `TextForm` simple-mode preview: each dialogue line renders as a portrait + bubble card so translators and event scripters can review a chapter's flow at a glance (issue #367). The Text Editor's **References** tab now has a working **Add Reference** button (issue #1028 Slice A): it opens the Add-Text-Reference dialog pre-filled with the selected text's id + its existing reference comment, then persists the comment through the new cross-platform `ITextIDCache` Core seam (`TextIDCacheCore` — `Update` + `Save` to `config/etc/<rom>/textid_.txt`) and refreshes the cross-reference list, replicating WinForms `TextForm.ShowRefAddDialog` (including the "blank comment on a new entry → keep a single space" convention). The Text Editor's TSV export now offers a working **Include AI Hints** checkbox (issue #1028 Slice C): when checked, every text entry that loads a face appends the referenced unit's translate-info line (name + info text + role/flag descriptors, or a mob-character fallback for unreferenced faces) into that row's Text column without corrupting columns, ported faithfully from WinForms `ToolTranslateROM.AppendAIHintMessage` / `UnitForm.GetTranslateInfoByFaceID` via the cross-platform `NameResolver.GetFaceTranslateInfo` seam (e.g. FE8U text id 0x900 emits Vanessa's name + role); the Text Editor's TSV export also offers a working **Export Filter** combo (issue #1028 Slice B): selecting one of the 11 WinForms `ToolTranslateROM.InitExportFilter` categories (All / Unit / Class / Item / Sound Room / Support Talk / Skill / Battle Talk / Death Quote / Ending / Chapter Text) limits the export to exactly the text-id set the matching per-form `MakeVarsIDArray` produces — `ExportFilterCore.BuildFilteredTextIds` ports the fixed-table walks, the version-specific Sound Room / Support Talk / ED offsets, the Battle Talk / Death Quote event-pointer-when-0 follow, the FE7 Death-Quote tutorial tables, the SkillSystem branch (FE8U `FindSkillPointer("TEXT")` + FE8N ver1/ver2 icon-pointer chains via the new `SkillSystemTextScanner`), and the Chapter Text EventCond scan (TEXT/CONVERSATION_TEXT/SYSTEM_TEXT/ONELINE_TEXT + POINTER_EVENT recursion + MENUEXTENDS/UNITSSHORTTEXT/TALKGROUP expansion + FE8 special-pattern, via the extended `EventScriptReferenceScanner.CollectEventCondTextIds`); an invalid/All index exports everything (closes #1028, with #1108 carving out the rich-text jumps); and the Text Editor's **Write** path now mirrors WinForms `TextForm.WriteText` (issue #1028 Slice D): when the edited text contains a character the Huffman dictionary cannot encode and the AntiHuffman (un-Huffman) patch is missing, it shows the **Bad Character** popup (`TextBadCharPopupView`, ja/zh/ko) and aborts with **no ROM mutation** unless the patch is installed — the six un-Huffman ROM signatures now live once in the cross-platform `PatchDetection.SearchAntiHuffmanPatch` (the Avalonia `PatchDetectionService.DetectAntiHuffman` delegates to it). The WinForms `EtcCacheTextID` now implements the same `ITextIDCache` interface (no behavior change), `CoreState.UseTextIDCache` is typed `ITextIDCache`, and the cache is (re)created on every ROM load (it is ROM/path/language-sensitive). The Text Editor's **Search Free Area** scan and per-text **cross-reference** list are now DEFINITIVE (issue #1027): `MakeVarsIDArrayCore` faithfully ports WinForms `U.MakeVarsIDArray` — the typed text-id∪song-id union over units/classes/items, every map's EventCond scripts, the menu-definition (6 roots) and status-R-menu recursive chains, world-map events, support/battle/haiku/ED/skill tables, the FE8N Ver3 skill table, the asmmap symbol records (`TEXTBATCH`/`TEXTBATCHSHORT`/`EVENT` via `AsmMapTextSymbolReader`), installed-patch `ADDR`/`STRUCT` `TEXT`/`SONG`/`EVENT` references (`PatchTextRefScannerCore`), and the user/system/FE8-reserved text-id cache (`ITextIDCache.EnumerateUsedTextIds`). `TextFreeAreaCore.FindUnreferencedTextIds` returns the complement (using WinForms' raw-id `ConvertMaps` mask) and GUARDS against false positives: when the event-scan prerequisites (active ROM + EventScript + comment cache) are not met it reports *prerequisites-missing* instead of a misleading partial list. The patch source is a faithful read-only subset — `$GREP`/`$FGREP`/`$FREEAREA` macro-resolved patch addresses are carved out (Ref #1027). The Map Exit Point editor's **Data Expansion** (Expand List) button now grows the selected map's per-map exit-point block by one blank row via `DataExpansionCore.ExpandTableTo` — it relocates the block to free space, repoints the per-map pointer slot, zero-fills the new row, preserves the terminator, and is undoable; blank/no-list and corrupt/unterminated blocks are refused (issue #773). The **Sound Room editor's "List Expansion" button is wired** (issue #1450): it prompts for a new entry count (capped at 255, or 1000 when the `soundroom_over255` patch is detected via the new Core `PatchDetection.SearchSoundRoomExpandsPatch`, mirroring WinForms `SoundRoomForm`'s `AddressListExpandsButton_255`→`_1000` rename) and grows the `0xFFFFFFFF`-terminated sound-room pointer table (`sound_room_pointer`, `sound_room_datasize` entries) via `DataExpansionCore.ExpandTableTo(... ExpandOptions{ Fill = First, Repoint = RawAndLdrAll })` — **FIRST-fill** copies row 0 into every new row (WinForms `MoveToFreeSapceForm.NewDataInit` seeds new list-expansion rows from an existing row, NOT zeros, so the SoundRoom reload scanner's empty-run stop does not hide them) with an all-reference (raw + ARM-Thumb LDR) repoint, defensive snapshot + canonical-slot audit guard, byte-identical fault restore, and one undo transaction; the loader scan cap rises to the effective cap (255/1000) so a patched ROM expanded past the old 512-row ceiling reloads fully the per-row structural-edit context-menu parity is delivered by #1539: the shared `AddressListControl` gains an **opt-in** `EnableStructuralEdit(blockSize, reload, …)` that appends WinForms-parity **Copy block / Paste (Ctrl+V) / Swap Up (Ctrl+Up) / Swap Down (Ctrl+Down) / Invalidate (DEL)** menu items (ports `InputFormRef.MakeGeneralAddressListContextMenu`), with the clipboard wire format (`AddressList@SoundRoomForm XX XX …`, `ToString("X")` no-leading-zero) round-tripped byte-for-byte with WinForms via the pure `AddressListClipboardCore.Serialize`/`TryParse` (hardened to reject non-hex/over-0xFF tokens); SoundRoomViewer is the first consumer (block size = `sound_room_datasize`, FE6 header `AddressList@SoundRoomFE6Form`), the 100+ copy-only editors stay untouched, every op validates the full `[addr, addr+blockSize)` range and runs under `UndoService` with byte-identical rollback (drag-drop reorder is the one deferred sub-feature — Ctrl+Up/Down swap covers the reorder parity). Table-expansion in Core also exposes `DataExpansionCore.RepointAllReferences(rom, oldBase, newBase, undo)` — an opt-in, all-reference rescan that repoints EVERY pointer to a moved table base: raw 32-bit pointers (`U.GrepPointerAll`) AND ARM Thumb LDR literal-pool loads (`U.GrepPointerAllOnLDR`, ported to Core with EOF-safety guards in #781), de-duplicated and undoable, safe on a no-reference ROM. This closes the former `DataExpansionCore` LDR-rescan known-gap (the single-slot repoint in `ExpandTable`/`ExpandTableTo` is still correct for unshared tables). The Instrument (SongInstrument) editor's **Expand List** button now grows the loaded song's voicegroup (instrument set) to the full 128 12-byte records via `RepointAllReferences`: it relocates the voicegroup to free space, copies the defined-prefix instruments verbatim, fills **every** added record (through record 127) from instrument 0 and moves the original stop record to position 128 (matching WinForms `MoveToFreeSapceForm` — the voicegroup is a fixed-size instrument set with no terminator, so all 128 records are filled, which makes a re-expand idempotent), then repoints **every** song header that shares the voicegroup (so the other songs are not corrupted — the #782 shared-voicegroup win); it is undoable, the button only enables for a song-context voicegroup with fewer than 128 defined instruments, and empty/already-128 sets are refused (a freshly-expanded voicegroup reads as a full 128, so a second click is a no-op) (issue #780). The **Song Instrument** (SongInstrument) editor's DirectSound **Wave Export/Import** is now wired on all four DirectSound tabs — **N00** (DirectSound), **N08** (DirectSound Fixed Freq), **N10** (DirectSound Reverse, #1001 PR1) and **N18** (DirectSound Fixed Freq Reverse, #1001 PR1) — via the cross-platform `SongDirectSoundWavCore` seam (the Reverse/Fixed-Freq variants share the identical on-ROM sample layout, so they reuse the seam verbatim): Export decodes the GBA sample (DPCM or 8-bit PCM) to a WAV file; Import appends a new GBA sample to ROM free space and repoints the voice entry's own P4 slot under one `UndoService` record, each gated on the loaded ROM header byte. The **Song Track** editor's Import Music File button now also accepts a raw RIFF **`.wav`** — `SongTrackWaveImportCore.ImportWaveAsSong` (#1001 PR1) builds an entire one-track song (sample + two-row voicegroup + VOL/KEYSH/VOICE/TIE/rests/EOT/FINE track + song header) and repoints the selected song-table entry, as one validate-before-mutate transaction with a byte-identical fault restore. The same Import button now also dispatches an **`.instrument`** instrument set (PR2, closes #1001 — reuses `SongInstrumentSetCore.ImportAll` to append the voicegroup then repoints the selected song header's voicegroup pointer `+4` at it, mirroring WinForms `SongUtil.ImportInstrument`) and a **`.s`** SondFont source (`SongTrackSImportCore.ImportS` — ports the WinForms `SongUtil.ImportS` m4a/mid2agb assembler: `.equ`/`.global`/labels/`.byte`/`.word`/`.end` with `.include`/`.section`/`.align` ignored, a `DataTable`-backed expression evaluator with longest-`.equ`-name-first substitution; the user first picks the instrument set through the `SongTrackImportSelectInstrument` browser as a **pick-and-return** — so `voicegroup000`/`MusicVoices` resolve to the chosen set, also satisfying #1002 — every encoded label id is bounds-checked before indexing, the whole `.s` is validate-all-before-mutate with `file:line` errors and ZERO mutation on failure, and the append + repoint run as one transaction with a byte-identical fault restore). The gates use the header byte **as loaded from ROM** (not the mutable tab/category), so switching to another DirectSound tab in-memory cannot repoint a voice of the wrong on-ROM type; the non-DirectSound N03 (Wave Memory) tab stays deferred. The DirectSound **Wave Import** path now opens a real conversion dialog (`SongInstrumentImportWaveView`, #1448 — ports WinForms `SongInstrumentImportWaveForm`, replacing the empty stub): sox resample/normalize (Hz / channel / strip-silence / volume), optional **DPCM compression** with a lookahead-quality selector, and a **Preview** showing the size delta plus the DPCM **SNR (dB)**. The pure pieces live in Core (`SongWaveConvertCore.WavToDPCMByte`/`CalculateSNR`/`LoadWavS`/`Convert`/`Preview`, with `SongDirectSoundWavCore.ImportSampleBytes` appending the ready sample); the resample step calls the user-configured external `sox` (`SongSoxConvertCore`, path from `Config.at("sox")` — clear error when unset, no bundled resampler). DPCM is offered only when the m4a HQ-mixer patch is installed (`HasHqMixer`), matching the WF hide/block behavior, and Cancel is a strict no-op. The same editor's **Export Instrument** button now performs a recursive read-only **instrument-set (voicegroup) export** via the new `SongInstrumentSetCore.ExportAll` (port of WinForms `SongInstrumentForm.ExportAllLow`/`ExportOneLow`): it writes a TSV index of the voicegroup plus per-voice side files — `.DirectSound.bin` (DPCM/uncompressed length-aware), 16-byte `.Wave.bin`, 128-byte `.Multi.keys.bin` — and recurses into Drum (0x80) / Multisample (0x40) sub-voicegroups (`.Drum.instrument`/`.Multi.instrument`), emitting `@SELF+offset` for in-range self-references (including nonzero offsets) and `@BROKENDATA` for nested out-of-range pointers. The matching **Import Instrument** button (PR 2 of 2, closes #1057) now performs the recursive ROM-mutating inverse via `SongInstrumentSetCore.ImportAll` (port of WinForms `SongInstrumentForm.ImportAllLow`/`ImportOneLow`/`WriteBackData`): it parses + validates the WHOLE import graph (root index + every nested `.Drum`/`.Multi` voicegroup + every side `.bin` / 128-byte keymap, with `@SELF+offset` parsed from the raw token, hex-validated, **12-byte-aligned**, and range-checked against the imported blob, and `@BROKENDATA` mapped to `@SELF+0`) **before mutating a single byte**, then appends the whole set in **one transaction** under the caller's ambient undo scope via a two-phase deferred write-back (allocate every voicegroup + side-data base FIRST, then resolve all pointer slots via `write_p32` with 4-byte-aligned offsets), repoints the loaded voicegroup's song reference(s) to the imported base via `RepointAllReferences`, and on ANY fault restores the ROM **byte-identical** (length-aware snapshot restore, the #885/#923 pattern) so a failed import changes zero bytes and an Undo restores ROM length + every pointer slot (issue #1057). The **World Map Image** editor's two **List Expand** buttons (Border tab and Icon Data tab) now grow their fixed-`RomInfo`-pointer tables — the 12-byte border table (`worldmap_county_border_pointer`) and the 16-byte icon-data table (`worldmap_icon_data_pointer`) — to a prompted row count via `DataExpansionCore.ExpandTableTo` **then** `RepointAllReferences`: `ExpandTableTo` relocates the table to free space, copies existing rows verbatim, zero-fills the new rows, writes the `0xFFFFFFFF` terminator and single-slot-repoints the canonical pointer, after which `RepointAllReferences` repoints every other raw-pointer / ARM-Thumb-LDR reference to the old base (matching WinForms `InputFormRef.ExpandsArea` → `MoveToFreeSapceForm.SearchPointer`); the expand is undoable, and on a clean ROM with no secondary references `RepointAllReferences` returns 0 which is treated as success (the canonical pointer is already repointed). The grown list is rendered directly from the post-expand row count rather than by re-scanning (the appended rows are zero-filled, so a re-scan would stop at the first new row). The list's comment/lint cache repoint is forward-only (not reversed on rollback — accepted WinForms parity); the SkillAssignment (issue #834) and Magic FEditor/CSA Creator (issue #837) list-expands that #825 left as follow-ups have since been wired (both described below). The Event Unit (FE8) editor's **New Allocation** button now allocates a real, editable unit-list block instead of opening a stub: a modal count-picker (NumericUpDown Min=1/Max=50/Value=1 — WinForms `EventUnitNewAllocForm` parity) chooses the row count, then `MapEventUnitCore.AllocNewUnitList` writes `count * eventunit_data_size + 1` bytes (each row's B0=1, trailing 0x00 terminator — byte-for-byte WinForms `EventUnitForm.CreateNewData`) via the shared free-space allocation seam (`MapEventUnitCore.AppendBinaryDataHeadless`, also now used by `EventCondViewModel`), under an undo scope; the new "NEW" group entry is tracked in a session list that survives map/group refresh (WinForms `NewAllocData`/`AppendNoWriteNewData` parity). WinForms writes **no** cond pointer for a NEW block — it is a reserved editing convenience the user references from the event script — so to match that, Core `MapEventUnitCore.GetUnitGroupsForMap` now also runs an event-script `POINTER_UNIT` scan (mirroring WinForms `EventCondForm.MakeUnitPointerEventScan`: it disassembles the map's START/END-event scripts via the Core `EventScript` disassembler and collects `ArgType.POINTER_UNIT` targets, de-duped with the direct cond-slot lists), so a unit list referenced from the event script shows up in the Avalonia group list on the next reload — the load-bearing reachability fix that makes the NEW block discoverable end-to-end (issue #776). The Event Unit (FE8) editor's **Random Monster** jump now opens the Monster Probability viewer pre-selected on the class_id-indexed row: `JumpMonsterProb_Click` reads B1 from the LIVE displayed control (`ClassIDBox.Value`, like WinForms `this.B1.Value` and the sibling jump handlers — not the VM's cached class_id, which only syncs on load / Write and would be stale if the user edits B1 before writing), resolves that class_id ROW INDEX to the matching Monster Probability entry address (`MonsterProbabilityViewerViewModel.ResolveAddressByClassIndex` over `LoadMonsterProbabilityList`'s 12-byte stride) and `Navigate`s to it (WinForms `InputFormRef.JumpForm<MonsterProbabilityForm>(B1, "AddressList", B1)` selectedID==class_id parity), falling back to a plain open when out of range; a navigation requested before the viewer's list loads is stashed and replayed so the preselection sticks on a freshly-opened window (issue #1018). The standalone **Instrument Selection** (SongTrack instrument-set) browser is now populated from the new cross-platform `InstrumentSetCore` — it scans the ROM for known native-instrument-map / voicegroup signatures (NIMAP / NIMAP2 / AllInstrument) above the compressed-image borderline and lists each discovered instrument set (seeded with the song's "Current" pointer), auto-selecting the first discovered set, mirroring WinForms `SongTrackImportSelectInstrumentForm.PickupInstrument()` (issue #787). The **Song Track** editor's **Import Music File** button (and the dedicated **MIDI Import** window, which now lists the real song table and shows the selected song's header + table-slot address) perform real MIDI→GBA write-back via the cross-platform `SongMidiCore.ImportMidiFile` seam (the same one the CLI `--import-midi` uses): the converted song is appended to ROM free space and the song-table ENTRY pointer slot is repointed at it (matching the CLI contract — the VM resolves `tableBase + songId*8`, honouring a custom read-start base, instead of clobbering the dereferenced header), all under one `UndoService` record so a mid-import failure or Undo restores the ROM byte-identical (issue #972). The **Skill Configuration (FE8N)** editor's Unit Skill List tab now makes the FE8N v1 skill row's 16 ext-bytes **B16..B31** editable (16 `NumericUpDown` inputs synced via the view's code-behind — `UpdateUI()` pushes the new ViewModel `Ext0..Ext15` values into the controls and `WriteButton_Click()` pulls them back, the same mechanism as the editor's other fields): selecting a skill row loads them and **Write** persists them as a PURE in-place byte write at `addr+16..+31` (no table relocation), tracked in the same undo scope as the rest of the row — FE8N v1's N00..N03 sub-tabs all union onto these same 16 bytes, so the one editable tab is faithful and complete (issue #790). Building on that, the same Unit Skill List tab now shows a **read-only inline unit-name preview** next to each of the 16 B16..B31 ext-byte inputs (WinForms **N00** decoration parity): each ext-byte is treated as a one-based unit ID and the resolved unit name is rendered beside it via the existing Core `NameResolver.GetUnitNameByOneBasedId` (no new Core code, no writes). The preview refreshes live from each input's **current** value (not the not-yet-written ViewModel property), is cleared when no row is loaded, and renders empty for a value of 0; this is decoration only — the Class/Item/Other-tab decorations and skill icons remain deferred (issue #793). The **ROM Translation Tool**'s "Import Font" action now auto-generates missing glyphs cross-platform: the previously WinForms-only bitmap auto-generation (`ImageUtil.AutoGenerateFont` + `Image4ToByte`) is replaced by the platform-neutral `IFontRasterizer` seam, whose `SkiaFontRasterizer` (in `FEBuilderGBA.SkiaSharp`) reproduces the WF GDI algorithm byte-for-byte (16×16 base render, text-font 16×22 scale + (-2, offset) composite, item-font 16×16 scale + (-1, 2+offset) composite with the 4-neighbour outline ring, `R<0xA0` threshold, and the exact 64-byte 2bpp pack); the Core `ToolTranslateROMCore.ImportFonts` threads the rasterizer through the per-glyph port loop and the Avalonia editor exposes the "Auto-Generate Missing Fonts" checkbox + "Use Font Name" + "Font Size" inputs (the legacy `ImportFontFromROMs` ROM-port-only overload is kept binary-compatible) (issue #796). The **Battle Animation Palette** editor now renders the WinForms `DrawSample` battle-animation **sample-preview grid** cross-platform (closing the last viable Avalonia↔WinForms preview parity gap): the new Core `BattleAnimeRendererCore.RenderSampleBattleAnime` LZ77-decompresses the animation's palette block, slices the selected palette-type (Player/Enemy/Other/4th, the cross-platform equivalent of WinForms `ImageUtil.SwapPalette`), renders each frame via `RenderSingleFrame`, crops it to a 90×90 window at source (100,30) (matching WinForms `SCALE_90`), blank-checks the crop (`IsBlankImage`, threshold 10), and walks 12 cells with a single persistent section/frame cursor into the 360×290 grid — re-rendering on entry-load and on palette-type change, hosted in a `GbaImageControl`; Phase 1 previews the saved ROM palette (WinForms parity) and live-spinner re-render stays a follow-up (issue #822). The **Unit Palette** editor now reuses that same renderer to preview a **class's battle animation recolored with the UNIT palette being edited** (closing the `:100`/`:113` deferred-preview gap): the Edit tab gains a Class selector + sub-palette (Palette Type) combo + read-only resolved Battle-Animation-ID field + a `GbaImageControl` sample preview; the new Core `ClassFormCore.GetAnimeIDByClassID` resolves the class → battle-anime ID (the WinForms `ImageBattleAnimeForm.GetAnimeIDByClassID` chain: `p32(classAddr + 48)` for FE6 / `+52` for FE7-8, then `u16(animeSetting + 2)`), and the new Core `BattleAnimeRendererCore.GetUnitPaletteAddr` resolves the unit-palette slot address (WinForms `ImageUnitPaletteForm.GetPaletteAddr` = `p32(IDToAddr(paletteno-1) + 12)`); a palette-override overload of `RenderSampleBattleAnime` then renders the 12-cell grid against THAT unit-palette block instead of the animation record's own `rec+0x1C` palette — mirroring WinForms `DrawBattleAnime`'s `custompalette>0 → palettes = GetPaletteAddr(custompalette)` override — while still applying the `paletteIndex` sub-palette slice (the two are independent: `paletteno` = unit-palette slot / custompalette; `paletteIndex` = enemy/ally sub-palette / `SwapPalette`). The preview re-renders on entry-load, class change, palette-slot change, and palette-type change; the rendered base is the unit palette, not the anime palette (issue #840). The editor now also **live-recolors** that sample preview while editing R/G/B colors (closing the #840 deferred `OnChangeColor` follow-up): each user R/G/B spinner change packs the 16 in-memory colors into the EXACT 32-byte RGB555 block via `UnitPaletteWriteCore.PackRgb555` and feeds it to a new optional `overridePaletteBlock` 4th parameter on `BattleAnimeRendererCore.RenderSampleBattleAnime` (used as the palette directly, bypassing ROM resolution; null/non-32 falls back byte-identical to the saved path), mirroring WinForms `ImageUnitPaletteForm.OnChangeColor`; the edited block is applied only when the previewed sub-palette equals the editable block (`PaletteTypeIndex == 0`, the block the spinners edit) and per-spinner re-renders are suppressed during bulk entry-loads/imports via the `IsLoading` guard (issue #1022). The **World Map Image** editor now renders five **reuse-based live previews** cross-platform (the `#500`/`#769` deferred world-map previews, NV5a): the **Event** map (`worldmap_event_image_pointer`/`_tsa_pointer`/`_palette_pointer`, 32×20 tiles = 256×160), the **Mini** map (`worldmap_mini_image_pointer`/`_mini_palette_pointer`, 8×8 = 64×64), **Point 1** (`worldmap_icon1_pointer`, 32×8 = 256×64), **Point 2** (`worldmap_icon2_pointer`, 12×4 = 96×32), and the **Road** strip (`worldmap_road_tile_pointer`, 1×15 = 8×120) — the last three sharing `worldmap_icon_palette_pointer`. The new Core `ImageWorldMapCore` dereferences each canonical pointer **pointer-to-pointer** (`p32` first) behind `U.isSafetyOffset` + a 4-byte LZ77-header guard (#818/#827) on every LZ77 stream, then composes via the EXISTING Core decode primitives: the event preview LZ77-decompresses **both** the image **and** the header-TSA stream and reads the event palette as **64 colors / 4 sub-palettes** (so `ImageUtilCore.DecodeHeaderTSA` can select the per-tile sub-palette via TSA bits 12–15), while mini/point/road each decode a single LZ77 image through `ImageUtilCore.LoadROMTiles4bpp` with a 16-color palette. Each preview is hosted in a `GbaImageControl` with a read-only **Export PNG** button gated by a per-preview render-success flag; every resolver is null-safe (a bad/truncated/missing pointer clears the preview, never throws). The main field map (a linear palette-map decode = a new primitive, NV5b) and the county border (an OAM-from-pointer blit, NV5c) stay deferred follow-ups (issue #843). The **main field map** preview (NV5b) is now rendered too via a NEW **FE8-only** Core primitive: `ImageUtilCore.ByteToImage16TilePaletteMap` decodes the big field map (a fixed **480×320**, 4bpp tiles) by selecting each tile's 16-color sub-palette from a linear **palette-map** nibble stream (one nibble per tile, two tiles per byte, ×0x10 sub-palette base) into a single 256-color palette, honouring the WinForms per-row `+4` off-screen-margin quirk and rendering **partially (never throwing) on a short image** — a faithful, pure (zero-decompression) port of WinForms `ImageUtil.ByteToImage16TilePaletteMap`. `ImageWorldMapCore.TryRenderMainFieldMap` gates on `RomInfo.version == 8` (FE6 routes to `DrawWorldMapFE6`, and FE7's `worldmap_big_palettemap_pointer` is a TSA-12-split not a palette-map — both return `null`), LZ77-decompresses **only** the palette-map while reading the **image (76,800 B) and palette (512 B) RAW**, and requires the full fixed regions (incomplete → `null`). The Main Field Map tab hosts it in a `GbaImageControl` with a read-only **Export PNG** button gated on a successful FE8 render; the county border (NV5c, an OAM-from-pointer blit) is the remaining deferred world-map follow-up (issue #846). The **World Map Image** editor's **Border (国境) tab** now renders a live **AP (Animated Parts) preview** (NV5c, issue #849): `ImageWorldMapCore.TryRenderBorder` LZ77-decompresses the parts image (P0), dereferences the 16-color palette pointer `worldmap_county_border_palette_pointer` via `p32` (pointer-to-pointer, matching WinForms `DrawBorderBitmap`) and reads the 32-byte palette RAW, renders the parts sheet via the new `ImageUtilCore.ByteToImage16Tile` (palette index 0 = transparent), parses the AP data at P4 via the new cross-platform `ImageUtilAPCore`, blits AP frame 0 + frame 1 (OAM-blit with the WF G4 bit-math: 2-D SharpTable, 9-bit/8-bit sign extension, transparent-index-0 blit, paletteShift) onto a transparent 256×160 canvas, composites the AP layer over the event world map, and returns a 256×160 RGBA result. FE8-only: `worldmap_county_border_palette_pointer == 0` on FE6/FE7 → null/disabled. The Border Export PNG button is gated on `CanExportBorder`; Import stays disabled (read-only cross-platform viewer). The **Item** editors (ItemEditor for FE7/FE8 and ItemFE6) now wire all four **new-alloc** buttons — Stat Bonuses (P12) and Effectiveness (P16) in both editors — which were previously deferred warning labels: when an item's sub-data pointer is 0 (and the item index > 0), the "New-alloc" button allocates a fresh block of the EXACT WinForms `InputFormRef.AllocEvent` default template (StatBooster = `byte[20]` with `[1]=5`; Effectiveness = `byte[12]` all `0x01` except `[11]=0`, or the SkillSystems-rework variant `[1]=6,[2]=1,[5]=6,[6]=2` selected via `PatchDetectionService.SkillSystemsClassTypeRework`) through the now-wired `CoreState.AppendBinaryData` seam (#796), converts the returned offset with `U.toPointer`, and writes it into the item field — block-write + pointer-write share one `ROM.BeginUndoScope`, so a single undo reverts both (the field returns to 0); a non-zero pointer is left untouched (no-clobber). The cross-platform alloc + exact templates live in Core `ItemAllocCore` (issue #831). The **Skill Assignment (Class)** editor's **Make Selected Class Independent** button is now wired (one of the SkillAssignment list-expand follow-ups deferred from #825): when the selected class's per-class level-up skill table is shared with one or more other classes, the button clones that table verbatim into a fresh free-space block and repoints **only** the selected class's pointer slot via a **single** `write_p32` — deliberately the inverse of `RepointAllReferences`, so every other sharing class stays on the intact original table (mirroring WinForms `SkillAssignmentClassSkillSystemForm.IndependenceButton_Click` → `PatchUtil.WriteIndependence`); the clone-block + pointer-write share one `ROM.BeginUndoScope` so a single undo reverts both, and the WinForms guards (in-bounds slot, safe resolved pointer, empty-list confirm) are preserved. The cross-platform clone + single-slot repoint live in Core `SkillAssignmentIndependenceCore`; the spurious master Class-Skill *List Expand* button was removed (the master table is class-count-bound and has no WinForms expand semantics), and the N1 level-up *List Expand* (single-slot `DataExpansionCore.ExpandTable`) is unchanged (issue #834). The **Skill Assignment - Unit (CSkillSys)** editor — opened from the Unit editor's *Edit Skills* button when a CSkillSys (0.9x / 3.00) patch is detected — is no longer an inert placeholder (issue #1451): it is now the full master/detail editor matching WinForms `SkillAssignmentUnitCSkillSysForm`, mirroring the already-ported Class sibling (#415) — a per-unit master list (`p32(0xB2A61C)`, stride 4, `W0` u16 personal skill, 1-based `UnitForm.GetUnitName` labels) with skill name/description/icon preview, a per-unit level-up (N1) sub-list (`p32(0xB2A7FC) + unitId*4` -> 0x0000/0xFFFF-terminated `{level, skill}` u16 rows) with Reload / Expand / Make-Independent / zero-pointer panels, and a master Write that commits both the `W0` personal skill and the edited per-unit level-up pointer under one `UndoService` scope; the N1 group hides entirely when the optional per-unit level-up table is absent (old patches — WF `UnitLevelUpSkill.Hide()`), bulk Import/Export stay disabled (WF stubs them), and the dead duplicate placeholder ViewModel was removed. The **Magic FEditor** and **CSA Magic Creator** editors' **List Expansion** buttons are now wired (the last of the #825 list-expand follow-ups): each click grows BOTH the magic-effect pointer table (`magic_effect_pointer`, 4-byte entries, unconditional) and the CSA spell table (5×4=20-byte entries, conditional) to a fixed 254 rows via the all-reference path — `DataExpansionCore.ExpandTableTo` (relocate + copy verbatim + zero-fill + `0xFFFFFFFF` terminator + single-slot canonical repoint) **then** `RepointAllReferences` (repoint every other raw-pointer / ARM-Thumb-LDR reference to the old base, so the FEditor/CSA patch ASM's literal-pool loads don't dangle), exactly mirroring WinForms `ImageMagicFEditorForm`/`ImageMagicCSACreatorForm` `MagicListExpandsButton_Click` → `InputFormRef.ExpandsArea` → `MoveToFreeSapceForm.SearchPointer`; the CSA spell-table-pointer discovery + `NOT_FOUND` clean-abort runs FIRST (before the table-1 expand) so a ROM without the CSA table is rejected with zero mutation, the whole expand is one undo transaction, and the button hides once the table is already expanded. `RepointAllReferences` omits the event-aware `GrepPointerAllOnEvent` pass that WinForms `SearchPointer` includes (the documented #781 limitation) — acceptable here because these are ASM-referenced graphics/animation tables, not event-script data; the shared two-table expand logic lives in Core `MagicListExpandCore` (issue #837). The **Footstep Sounds** editor's **List Expansion** button is now wired (#1449): it grows the per-class footstep-sound Switch2 jump-table to cover all classes via Core `SoundFootStepsExpandCore.Expand` (delegating the table mutation to the shared `ItemUsagePointerCore.Switch2Expands`) and, on FE8, applies the `PlaySoundStepByClass` ASM hardcode fix (`{0x1c,0xe0}` at `0x7B198` for FE8J/multibyte or `0x78d84` for FE8U/single-byte) under the same undo scope so the engine reads the relocated table; the list now sizes from the Switch2 `count + 1` metadata (matching WinForms `SoundFootStepsForm`) instead of stopping at the first NULL entry. The **Map Settings (FE6) editor's "List Expand" (リストの拡張) button is wired** (issue #1085): it prompts for a new entry count and grows the chapter/map-setting table (`map_setting_pointer`, `map_setting_datasize` entries) via the new options-based `DataExpansionCore.ExpandTableTo(rom, ptr, size, currentCount, newCount, ExpandOptions{ Fill = First, Repoint = RawAndLdrAll })` — **FIRST-fill** copies row 0 into every new visible row `[currentCount, newCount)` (a zero-filled row is invalid, so `MapSettingCore.MakeMapIDList` would stop there and the expand would be a no-op), the terminator stays the natural invalid row at index `newCount`, and the list grows by EXACTLY `newCount − currentCount`; **complete reference repointing** then repoints every raw 32-bit + ARM-Thumb LDR literal-pool reference to the moved base across the whole ROM (the map-setting base is read from multiple engine sites, so a single-slot repoint would corrupt chapter loading). The orchestrator `MapSettingCore.ExpandMapSettingTable` runs under one undo scope with a byte-identical (length-aware) fault restore and an audit guard (records the repointed slot list in `ExpandResult.RepointedSlots`, asserts the canonical pointer slot is covered, fails loudly on a zero or implausibly-large hit count). An empirical per-ROM scan (FE6/FE7J/FE7U/FE8J/FE8U) proves every reference to the base is a raw/LDR hit (5–6 slots each) with ZERO non-raw event refs, justifying the omission of the event-aware repoint pass for this engine table. The legacy `ExpandTableTo(..., bool fullZeroTerminatorRow)` overload is kept as a thin compatibility shim (`Fill = Zero, Repoint = PointerSlotOnly`) so the #501/#1078 callers stay byte-identical. The Map-Style change popup + BGM ♪ play buttons remain documented deferrals; FE7/FE8 share a different Map Settings view with no Expand affordance (follow-up). The **Magic FEditor** editor now renders a **live magic-effect frame preview** (240×128) in the "Display Example" row: the new Core `MagicEffectRendererCore.RenderMagicFrame` decodes the FEditor magic-animation script starting from P0 (frame-data pointer), finds the Nth 0x86 frame record (28-byte stride, command byte at [+3], 0x85 skip, 0x80 terminator, 00 01 00 80 continuation), reads the BG palette (+24) and OBJ palette (+20) RAW, LZ77-decompresses the BG tiles (+16) and OBJ tiles (+4) with header+truncation guards, decodes both 4bpp sheets (index 0 = transparent), scales the 256×BG_height BG sheet left-240px vertically to 240×128 (WinForms `ImageUtil.Scale` parity), then blits the back OAM layer (P12 base + relative offset at record +12) followed by the front OAM layer (P4 base + relative offset at record +8) both with `isMagicOAM=true` via the existing `BattleAnimeRendererCore.DrawOAMSprites`; the result is hosted in a `GbaImageControl` that replaces the former `#500` deferred label. A read-only **Export PNG** button gated on render success completes the view. Frame index and P0/P4/P12 changes re-render the preview; Import/Open-source/Select-source remain disabled (out of scope). FE-gate: the preview clears automatically when no FEditor/CSA_Creator magic-system patch is detected (issue #852). The **Event Map Change** editor now renders a live **change-overlay preview** (NV6-PR2, issue #857): `MapRenderCore.RenderChangeMap` reads the RAW (uncompressed) `width × height` u16 tile-index array from P8 (B3/B4 = width/height from the 12-byte change record), resolves the OBJ tileset, 512-byte palette, and LZ77 config descriptors from the selected map's `map_setting` fields (`obj_plist & 0xFF` → OBJECT plist, `palette_plist` → new `PlistType.PALETTE` via `map_pal_pointer`, `config_plist` → CONFIG plist), composites via the shared `RenderTsaComposite` helper with `opaqueIndex0=true`, and displays the result in a `GbaImageControl`; a read-only **Export PNG** button gated on `CanExportChange` completes the view. `MapRenderCore` was refactored to extract the common TSA-composite loop into a private `RenderTsaComposite` helper shared by both `RenderMapImage` (PR1/#855) and the new `RenderChangeMap` (PR2/#857), keeping the 13 existing `RenderMapImage` tests green. Out-of-range config descriptors skip the offending tile (partial render); width/height of zero or oversized dimensions (`> MAX_CHANGE_TILES`) return null without allocating. See `docs/avalonia-gap-analysis.md` for details. The **World Map Image** editor's Main Field Map tab now supports full **FE8 Import/Export round-trips** (issue #875): the new Core `ImageUtilCore.EncodePaletteMap16Tile` is the byte-exact inverse of `ByteToImage16TilePaletteMap`'s nibble walk (WF `ImageUtil.ImageToPaletteMap` port — even tile → low nibble; odd → high nibble; `+4` per-row margin; `(width/2+4)×height` buffer), while `ImageWorldMapCore.ImportMainFieldMap` writes the image (76,800 B) and palette (128 B) **RAW in-place** via `rom.write_range` and the LZ77 palette-map via `WriteCompressedToROM` under a single ambient undo scope — byte-exact WF `ImportButton_Click` parity, with the WF duplicate-pointer write intentionally omitted. `ImageWorldMapCore.ImportDarkPalette` writes only the 128-byte dark palette in-place (`DarkMAPImportButton_Click` parity). `TryRenderDarkFieldMap` mirrors `TryRenderMainFieldMap` but reads `worldmap_big_dpalette_pointer` (`DrawDarkWorldMap` parity). All three buttons (**Import Image**, **Dark Map Import (Palette)**, **Dark Map Export**) are now wired and FE8-gated via `CanImportMain`/`CanImportDark`/`CanExportDark` bindings. The four single-LZ77-stream **strip imports** (Mini / Point1 / Point2 / Road) are wired image-only via `ImageWorldMapCore.ImportIconStrip` (4bpp-encode → LZ77 → free-space append + repoint the single image pointer; the shared palette is nearest-color-remapped onto, never written), FE8-gated via `CanImportMini`/`CanImportPoint1`/`CanImportPoint2`/`CanImportRoad` (issue #1000). The **Event** tab's **Import Image** (two-stream TSA) and the legacy Main **Export Image** are now wired too (issue #1064 PR 1): `ImageWorldMapCore.ImportEvent` auto-reduces a **240×160** source RGBA via `DecreaseColorConvertCore.Convert(maxPalette:4, yohaku:16, reserve1st:true, ignoreTSA:false)` (the method-4 "World Map (event)" preset) to a banked **256×160** canvas + 4-bank palette, validates the result (256×160, ≤4 banks, one bank per 8×8 tile via the public `ValidateEventBankedIndices`, unique tiles ≤1024) rejecting with **zero mutation**, splits the banked indices into local 4bpp pixels (`&0x0F`) + per-tile bank (`/16`), encodes the multi-bank TSA via `ImageImportCore.EncodeTSAMultiPalette` + header-TSA via `EncodeHeaderTSA(…, margin:2)`, then writes **ZIMAGE** (LZ77 tiles) to `worldmap_event_image_pointer`, **ZHEADERTSA** (LZ77 — compressed, matching `TryRenderEvent`) to `worldmap_event_tsa_pointer`, and the **RAW 64-color / 128-byte** palette to `worldmap_event_palette_pointer` — validate-all-before-mutate, one ambient undo scope, and a byte-identical (length-aware) fault restore (a failed import, incl. a partial-write fault after ZIMAGE, mutates zero bytes); the Event Import button is FE8-gated via `CanImportEvent` (all three event pointers resolvable) and the legacy Main **Export Image** is wired to the same read-only `MainExport_Click` PNG path as the **Export PNG** button (`CanExportMain`). The **Border (国境) image Import** (OAM/AP assembly) is now wired too (issue #1064 PR 2, which **closes both #1064 and #1000**): the new pure `ImageUtilBorderAPCore.AssembleBorderAP` ports WinForms `ImageUtilBorderAP.ImportBorder` — it packs the two already-decoded **248×160** indexed sheets (the chosen border sheet + its **`_NAME`** companion) into ONE **256×40** (5-tile) seat (reusing the `BattleAnimeOAMImportCore` seat / tile-dedup helpers), rejects when the combined tiles overflow the single seat (WinForms `images.Count >= 2`), clamps the origin (`x≥60→60`, `y≥50→50`), converts the 12-byte battle OAM → 6-byte AP OAM (`BattleOAMToAPOAM` — recenter via the max-rectangle + Y≥0x80 origin shift) and builds the AP-data block (header SHORTs `frame_list-ap_data`=4 / `anime_list-ap_data`=8, the two frame records + two anime scripts `(4,frameIndex)`→`(0,0xffff)`) — all over `byte[]` with **no** ROM mutation, no `Form`/`File.*`/dialogs (the three-concern split: pure assembly / filename+input errors as `Error` returns / ROM writes elsewhere). `ImageWorldMapCore.ImportBorder` is the **FE8-only** (`version == 8` + non-zero `worldmap_county_border_palette_pointer`) ROM-write seam: it LZ77-writes the seat image → the selected border record's **P0** and raw-writes the AP block → **P4**, repointing both under ONE ambient undo scope with a byte-identical (length-aware) fault restore (a failed import — incl. a partial-write fault after the image write but during the AP write — mutates zero bytes). The Avalonia Border tab resolves the `_NAME` companion (`{name}_NAME{ext}` in the same folder, error if missing), remaps **both** sheets onto the **existing** ROM border palette (image+AP only — the palette is NOT written, so encoded indices use existing-ROM colors) and routes through the VM `ImportBorder` driver, FE8-gated via `CanImportBorder` (FE8 + county-border palette pointer + a selected record). With this, **no** World Map Image button stays a deferred KnownGap stub. DecreaseColor + OpenSource + SelectSource were wired in #1013. The **Magic FEditor** editor now supports full **Import** of FEditor magic-animation scripts (issue #881, completing #878): `MagicEffectImportCore.ParseMagicScript` parses the `.txt` tokens (O/B/wait, C<hex>/S<hex>, ~~~), the view loads each referenced PNG via `ImageImportService.LoadAndQuantizeFromFile`, and `MagicEffectImportCore.ImportMagicScript` assembles OAM + tiles via `BattleAnimeOAMImportCore.AssembleOAM` (isMagic=true, #883), LZ77-compresses both OBJ and BG tile sheets, allocates all regions via `RecycleAddress` ambient-undo overloads, and writes the complete frame-data table + OAM arrays + image/palette pointers into the ROM slot under ONE undo scope; validate-before-mutate ensures no partial writes on parse or assembly errors, and snapshot-restore is applied on any write failure. FE-gate: Import is disabled without the FEditor/CSA_Creator magic-system patch. The **CSA Magic Creator** editor's **Export Magic Animation** + **Open Source File** + **Open Source Folder** buttons are now wired (issue #886, #500 Part 1): Export produces a `.txt` script + per-frame OBJ/BG PNGs mirroring WF `ImageUtilMagicCSACreator.Export` — it reuses `MagicEffectExportCore.ExportMagicScriptLines(isCsa:true)` (single ordered walk) for the OBJ render and the `.txt` lines (identical to FEditor), and the new `MagicEffectExportCore.RenderCsaBgFrameSlot` for the CSA-specific TSA-composited BG render: the +28 LZ77-compressed TSA arrangement is LZ77-decompressed, each tile in the TSA u16 array is looked up in the decoded 4bpp tilesheet with optional H/V flip, and the result is composited into a 240×160 (or 240×64) RGBA canvas (mirroring WF `ExportBGFrameImage`'s `ByteToImage16Tile` path); the BG hash is `rawBgPtr + rawTsaPtr` (vs. FEditor's `rawBgPtr` only), the CSA frame stride is 32 bytes (vs. FEditor's 28). Open Source File / Open Source Folder read the same `EtcCacheResource` key `"MagicAnimation_" + hex(idx+1)` as the FEditor view and reveal the cached source `.txt` file or its containing folder (best-effort shell open). Export/OpenSource/SelectSource are CSA-gated (`MagicKind == CsaCreator`). The **CSA Magic Creator** editor's **Import Magic Animation** button is now fully wired (issue #889, completing #500 image-import): the new Core `MagicEffectCSAImportCore.ImportCsaMagicScript` reuses `MagicEffectImportCore.ParseMagicScript` (shared with #885 FEditor import), assembles OBJ OAM via `BattleAnimeOAMImportCore.AssembleOAM` (isMagic=true), encodes BG tiles + TSA map via `ImageImportCore.EncodeTSA` (240px wide CSA BG, matching `MagicEffectExportCore.CSA_BG_EXPORT_WIDTH`=240), LZ77-compresses tiles and TSA independently, and writes the **32-byte** CSA 0x86 frame record with the TSA pointer at +28 (vs. the 28-byte FEditor layout); BGScaleMode auto-inserts a 0x53 scale command at the first 64-px BG frame and appends the cancel command before the final terminator; all five #885 import lessons (5×C00 header, 256-wide BG crop, explicit O+B+time triple, CoreState.ROM guard, validate-before-mutate) are applied; CSA-gated; `DoImport(path, pngLoader)` is injectable for headless tests. Editor (#500) remains a stub. The four SkillConfig skill editors that store a fixed 16×16 4bpp skill icon — **Skill Config (SkillSystem)**, **Skill Config (CSkillSys 0.9.x)**, **Skill Config (FE8N v2)**, and **Skill Config (FE8N v3)** — now wire their **Image Import** / **Image Export** buttons through the shared `SkillConfigIconIoHelper`: Import opens a file dialog, requires a 16×16 image, remaps it to the in-ROM skill palette (never overwriting the palette — WinForms parity), encodes it to 128 raw 4bpp bytes via `ImageImportCore.EncodeDirectTiles4bpp` (byte-for-byte identical to WinForms `ImageUtil.ImageToByte16Tile`), and writes them **in-place** at the icon byte-address (no LZ77, no pointer relocation) under one undo scope with a ROM-identity + region-safety guard and rollback on any partial-write failure; Export renders the 128-byte icon through the same 4bpp-tile decode path the view uses and saves it as PNG (read-only). The four animation-bearing SkillConfig views (SkillSystem, FE8N Ver2, FE8N Ver3, FE8U-C SkillSys 0.9x) also wire their **Animation Export** button through the read-only cross-platform `SkillSystemsAnimeExportCore` seam (ported from WinForms `ImageUtilSkillSystemsAnimeCreator.Export`): it resolves the anime-config via `SkipCode` (FE8J direct; FE8U skips the embedded `skillanimtemplate*.dmp` program and detects the defender variant), walks the frame list to the `0xFFFF` terminator, LZ77-decompresses each frame OBJ + TSA, renders it 240x(>=160) via `ImageUtilCore.DecodeTSA`, and writes either a `.txt` script (optional `D`/`S{sound}` header + `{wait} g{id}.png` lines) with per-frame PNGs, or an animated GIF (frame delays via `U.GameFrameSecToGifFrameSec`); FE8N Ver1 (no animation pointer) stays a stub (#910). The matching **Animation Import** button is wired through the ROM-mutating `SkillSystemsAnimeImportCore` seam + shared `SkillConfigAnimeImportHelper` (SLICE 1 #916 FE8J + SLICE 2 #917 FE8U): it parses the `.txt` script, loads + quantizes each frame PNG to 16 colours, forces 240x160, `EncodeTSA`+LZ77-compresses the tiles/TSA while writing the **palette RAW (0x20 bytes, never compressed)**, dedups frames by **filename**, terminates the frames table with a **4-byte `0xFFFF,0xFFFF`**, writes `sound_id` as a **raw u32** (default `0x3d1`), and repoints the slot under one ambient undo scope with a validate-before-mutate + snapshot-restore corruption guard. **FE8U** additionally prepends the per-skill program template (`config/patch2/FE8U/skill/skillanimtemplate*.dmp`, defender vs attack selected by the leading `D` line) — read ONCE in the validate phase and prepended verbatim before the config block, via the shared `FE8USkillTemplate` constants that the export `SkipCode` skips (so the two seams can never drift); a missing/unreadable `.dmp` returns a clean no-mutation error, and the repointed slot points to the template start so re-export round-trips. FE8N Ver1 import stays a stub (#913). Re-importing a skill animation now **recycles the old anime region** instead of leaking it (#914): the read-only Core `SkillSystemsAnimeImportCore.EnumerateOldAnimeRegions` — a literal port of WinForms `RecycleOldAnime` — enumerates the slot's CURRENT anime sub-regions (per-frame OBJ/TSA/palette + frames table + the three pointer lists + the program/config block) in the validate phase BEFORE the snapshot clone, and threads them into the write's `RecycleAddress` pool via the new `recycleOldRegion` parameter (default `true` for single-import) so the new allocations REUSE the freed region; a zero/garbage slot enumerates EMPTY (a true no-op, so the path is a strict superset of fresh-allocate), the enumeration is strictly read-only so the #885 byte-identity-on-fault guarantee still holds. **Bulk import now recycles too, safely (#929):** a read-only `SkillSystemsAnimeImportCore.BuildSkillAnimeRegionRefcount` pre-pass over the original state — keyed on normalized data address (`Address.Addr`), counting SLOT ownership via a per-slot `HashSet` so a slot reusing a frame id stays `count==1`, only a region owned by ≥2 distinct slots reaches `count>1` — builds the shared set (the `SubConfilctArea`-equivalent skill-anime lacked), and each per-skill import runs `recycleOldRegion:true, excludeRegions:shared` via the new exclude-aware `EnumerateOldAnimeRegions(rom, addr, IReadOnlySet<uint> excludeRegionAddrs)` overload (the original `(rom, addr)` overload delegates with `null`, no behavior change) so a cross-slot shared sub-region is NEVER recycled/overwritten (no co-owner corruption) while unshared old regions are reclaimed (no unbounded ROM growth on re-import). Conservative static pre-pass: an originally-shared region stays excluded for the whole transaction (safe, may leak; dynamic reclaim out of scope). The **Skill Config (SkillSystem)** view's **Bulk Export** button is now wired through the read-only cross-platform `SkillConfigSkillSystemBulkExportCore` seam (SLICE 1 of #920; ported from WinForms `SkillConfigSkillSystemForm.ExportAllData`): it dereferences BOTH the text- and anime-pointer LOCATIONS to their bases, walks the `i < 255`-capped row count via `Rom.getBlockDataCount`, writes a `*.SkillConfig.tsv` with one `textID<TAB>animePtr` (hex) row per skill, and for each EXTENDED-area anime pointer renders the animation via the merged `SkillSystemsAnimeExportCore.ExportSkillAnimation` seam (#912) and writes an `anime{i:hex}/anime.txt` script + per-frame PNGs (each unique `IImage` disposed once, the #912 hygiene lesson). Read-only — zero ROM mutation, no undo. The matching **Bulk Import** button (SLICE 2 of #923 / #885) is now wired through the BULK-ATOMIC cross-platform `SkillConfigSkillSystemBulkImportCore` seam (ported from WinForms `SkillConfigSkillSystemForm.ImportAllData`): it reads the `*.SkillConfig.tsv`, derefs BOTH pointer LOCATIONS (after a `+3` isSafetyOffset guard), walks the `i < 255`-capped count, and for each skill with an `anime{i:hex}/anime.txt` re-imports the animation via the merged `SkillSystemsAnimeImportCore.ImportSkillAnimation` (additive `manageSnapshot` param). The whole multi-skill import is ONE ATOMIC transaction — either every skill commits (exactly ONE undo record) or the ROM is restored byte-identical to the pre-bulk snapshot (ZERO undo records). The approved #923 plan's 3 HIGH corruption fixes are baked in: a length-aware restore that down-resizes `rom.Data` back to the snapshot length before the in-place copy (a per-skill import can grow the ROM via `RecycleAddress` → `write_resize_data`); return-value fault detection (`ImportSkillAnimation` signals failure by RETURNING a non-empty string, not only by throwing); and ONE ambient `BeginUndoScope` wrapping the loop with every per-skill import running `manageSnapshot:false` so it composes into the bulk scope (asserted alive across all skills via `Rom.IsAmbientUndoScopeActive`). textID is written only when non-zero (M1); the optional recycle conversion runs through the ported pure `SkillConfigSkillTextIDRecycle.Convert` (M2); a validate-all-before-mutate pass pre-loads every script + PNG (+ FE8U `.dmp` template) so any failure mutates zero bytes (M3); malformed `< 2`-field TSV rows are skipped (L1). The Core seam owns the undo scope so the view does NOT open a UI UndoService scope (that would clobber the non-reentrant ambient scope) (#923). Bulk now RECYCLES old anime regions safely (#929): a read-only `BuildSkillAnimeRegionRefcount` pre-pass over the original state builds the set of regions owned by ≥2 distinct slots and each per-skill import runs `recycleOldRegion:true, excludeRegions:shared`, so a cross-slot shared sub-region is never recycled/overwritten while unshared old regions are reclaimed (no unbounded ROM growth on re-import). Each view re-derives its icon byte-address fresh under the live ROM — SkillSystem from the striped table `IconBaseAddress + 128 * id`, CSkillSys by re-dereferencing the GBA pointer at entry+0, and FE8N v2/v3 from `p32(icon_pointer) + 128 * (0x100 + id)` with the per-skill palette (W2==0 → system-weapon palette, else icon palette). **FE8N v1 is intentionally left read-only** (its WinForms form has no icon I/O and its address derivation lacks the `0x100` page offset, so wiring it would write to the wrong slot) (issue #898). The **TSA Tile Editor**'s **Main Image Import** button is now wired (tilesheet-only, issue #901): mirroring WinForms `image1_Import` — whose ImageFormRef is built with `tsa_pointer=0` so its Import falls to the `ImageToByte16Tile` branch and writes ONLY the ZImg pointer — the new Core `TSAImageImportCore.ImportTSAImage` validates the import is the SAME SIZE as the existing tilesheet (derived from the ZImg pointer via a `CalcLZ77ImageToSizePointer` port), encodes the remapped indexed pixels to plain 4bpp tiles via `ImageImportCore.EncodeDirectTiles4bpp` (byte-identical to WinForms `ImageUtil.ImageToByte16Tile`, confirmed in #898), LZ77-compresses, and writes a SINGLE region (recycle old tilesheet → `RecycleAddress.WriteAmbient` → `write_p32(ZImg)` → blackout) under one undo scope — **TSA and palette pointers are left byte-for-byte untouched** (a data-loss bug the plan review averted). The **TSA Cell** tab adds per-cell editing (Tile ID + H/V flip + palette bank) for BOTH non-header TSA (clean row-major grid, issue #1005) AND header-TSA (the `{masterHeaderX, masterHeaderY}` 2-byte header + 32-wide bottom-to-top stride, issue #1071): the shared `ImageUtilCore.DecodeHeaderTSAToCells` / `SerializeHeaderTSA` (the EXACT inverse, preserving the original header) drive `ImageTSAEditorCore.WriteHeaderTsaCells` — raw TSA overwritten in place (same-size, no growth), LZ77 TSA recompressed + appended + the pointer slot repointed, all under ONE undo scope with a byte-identical (length-aware) fault restore; for header-TSA the editable region is constrained to the header region (cells outside it are non-selectable, not silently ignored), and a corrupt/unreadable header stays non-editable. The view's `MainImageImport_Click` loads + remaps the PNG to the active palette via `ImageImportService.LoadAndRemapFromFile(strictSize)`, rolls back + restores the rendered previews on any error, and refreshes the battle/chip previews on success; the raw-tilesheet **Image Export** (`image1_Export`) stays a deferred KnownGap (distinct from the already-wired #808 read-only TSA-composited Export PNG) (issue #901). The **Unit Palette** editor's **Export Image** + **Import Image** buttons are now wired (issue #904): **Export Image** delegates to the SamplePreview `GbaImageControl.ExportPng` (the same recolored class battle-anime sample grid WinForms `ImageUnitPaletteForm.ExportButton_Click` exports as `DrawBitmap`) — read-only, never touches the ROM; **Import Image** loads a ≤16-color image and writes its palette back via the existing `UnitPaletteWriteCore` path (WinForms `ImportButton_Click` = `MakePaletteBitmapToUIEx` → `PaletteWrite`). The new Core `UnitPaletteImportCore.TryExtractIndexOrdered` does the heavy lifting: it reads the palette **in index order** (index 0 = transparent/backdrop is semantic) — preferring a loader-preserved indexed GBA palette, else deriving the order from the RGBA pixels by first-appearance scan — and **rejects** (no UI/ROM change) any image with more than 16 distinct colors (it deliberately does NOT call `DecreaseColorCore.Quantize`, which never rejects and would scramble the index order). The 16 index-ordered RGB555 triples populate the editor's 16 R/G/B `NumericUpDown` controls (the source of truth `PaletteWrite_Click` reads — not the VM channels, which would cause a stale write), and the import then reuses `PaletteWrite_Click` so the LZ77 compress + in-place-or-reallocate + P12 repoint all run under that handler's single undo scope; a guard requires a selected palette entry first (mirroring `PaletteWrite`). The **SkillConfig FE8N Ver2/Ver3 per-skill sub-list tabs** (Unit/Class/Item/Item2[/Composite]) are now **editable** (issue #930): each tab embeds the reusable `SkillSubListEditorView` which edits one per-skill null-terminated 1-byte-ID array through its pointer slot (`+4/+8/+12/+16/+20`) via the merged Core `NullTerminatedByteListCore` (#926/#928) — every Add/Remove/Set-ID routes through a single `WriteByteList(slot, ids, undo)` call (fork-on-write of shared arrays, fresh-allocate on a NULL slot, slot-repoint), undo-wrapped under the host's shared `UndoService`; the Ver2 **Item2** tab is gated on `HasItem2` (stride>=20, so `+16` can't corrupt the adjacent row) and the Ver3 **Composite** tab decorates ids via the main-list `ResolveCompositeName` text; after any sub-list op the host re-runs `LoadEntry` to re-sync its cached Px offsets. The FE8N Ver1 Class/Item/Other placeholder tabs are clarified as the same shared B16..B31 ext-bytes already editable on the Unit tab. The **Data Address Editor** dispatcher (DumpStruct) now has a fully functional **Export** + **Import** wired to the SAME Core struct-data seam the CLI `--export-data`/`--import-data` commands use (`StructExportCore`): the CSV/TSV/EA Export buttons resolve the struct table from the focused address via `StructExportCore.ResolveTableAt`, show a file-save dialog, and write `StructExportCore.ExportToTSV`/`ExportToCSV`/`ExportToEA` output (byte-identical to the CLI) — falling back to the honest hex-dump preview banner only when the address is inside no known table (STRUCT/NMM are not Core-backed and always preview); the Import button resolves the table the same way, opens a file dialog, parses with `StructExportCore.ImportFromTSV` (hex index from the first column) and writes via `StructExportCore.WriteTable`, all inside one `UndoService` Begin/Commit scope (Rollback on any parse/write failure, so the mutation is undoable and never half-applied). The shared `TableExportImportHelper` gained address-resolved, format-aware `ExportTableByAddressAsync`/`ImportTableByAddressAsync` overloads for this (closes #439). The **AI Script** editor's **List Expand** button is now wired (previously a no-op info dialog): it grows the active AI pointer table (ai1 when the filter is AI1, ai2 when AI2) to a prompted slot count via `AIScriptViewModel.ExpandList` under one `UndoService` scope — `DataExpansionCore.ExpandTableTo` relocates the table to free space, copies the existing slots verbatim, zero-fills the new slots, writes the `0xFFFFFFFF` terminator, wipes the old region and repoints the canonical base slot `ai*[0]`, after which `ExpandList` repoints the two additional consecutive base-pointer slots `ai*[1]`/`ai*[2]` that WinForms `AIScriptForm.AddressListExpandsEventNoCopyPointer` also repoints (isPointer-guarded, so ROMs without them are unaffected); `newCount` < current count is refused with no mutation, and the list reloads from the repointed base (issue #1020). The **Unit Palette** editor's **Expand List** button is now wired with a **predicate-aware** path (completing #1067's second half): `DataExpansionCore.ExpandTableTo` gained an optional `fullZeroTerminatorRow` flag (default `false` = byte-identical to the existing `0xFFFFFFFF` 4-byte terminator used by #501 and the editors above) that, when `true`, reserves and writes a FULL `entrySize`-byte all-zero terminator row instead — required because the Unit-Palette row scan accepts `P12==0 && name!=0` as a valid row and a bare `0xFFFFFFFF` dword would be a phantom valid entry. `ImageUnitPaletteViewModel.ExpandList` uses the editor's **real** row count (excluding the trailing `Unit Palette Editor` sentinel), FIRST-fills each new 16-byte row from a non-empty **template** row's 12 identifier bytes + clears its `P12` (WinForms `ExpandsFillOption.FIRST` + `AddressListExpandsEventNoCopyP12`, so the new rows are scan-visible as `P12==0 && name!=0`), grows the table with `fullZeroTerminatorRow: true`, then runs `RepointAllReferences` (raw 32-bit + ARM-Thumb LDR-literal) to repoint every reference to the moved base — all under one `UndoService` scope via the `NumberInputDialog` count prompt (max 512). A ROM with no non-empty template row is refused with zero mutation (validate-before-mutate), and `newCount` < current / > 512 are rejected (issue #1078). The **Color Reduction Tool** (`DecreaseColorTSAToolView`) is now a fully functional file→file PNG color reducer instead of a placeholder shell: input/output file pickers (`StorageProvider`), a **Method** preset combo (0=manual no-op + presets 1..0xA mirroring WinForms `DecreaseColorTSAToolForm.Method_SelectedIndexChanged`) that drives `DecreaseColorConvertCore.GetMethodPreset` to populate Width/Height/Margin/Palette-banks/Size-method/Reserve/IgnoreTSA, and a **Reduce** action calling `DecreaseColorConvertCore.ReduceColorFile`. `InitMethod(int)` now actually applies the caller's preset (closing the long-standing "mode index stored but never consumed" bug), so the `ImageBG`/`ImageBattleBG`/`World Map Image` "Color Reduce" buttons (modes 1/2/3/4) land on a populated form; combo labels are code-populated via `R._()` for ja/zh localization (issue #998). The **Setup/Init Wizard**'s 8 auto-download/install buttons (VBA-M, mGBA, Event Assembler+lyn, Sappy/VG Music Studio, no$gba+arm-none-eabi-as, gba_mus_riper+sox+midfix4agb, Git) are now wired cross-platform via the new Core `DownloadInstallCore` + `U.HttpDownloadFile` seam: each click shows a **confirmation dialog naming the source URL + an elevation note** (Git is never silent — its installer runs under UAC), then downloads to a per-call **temp staging dir**, extracts/places the file, **locates the expected executable by match-glob**, validates it with the SAME Browse-mode check the manual pickers use (`File.Exists` / `GitUtil.ProbeGit`), and only THEN stages the path (forcing the step's mode to `Path` so the existing atomic `ApplyAll` persists it) and **atomically** moves it into the tool dir — on ANY failure (download/extract/missing-exe) nothing is placed and a clear error is shown, so a pre-existing install is never clobbered and the config path is never persisted on failure. Bundled buttons are all-or-none. Auto-download is **Windows-only** (the buttons are disabled-with-tooltip on other platforms, and Git routes to Browse/manual install there). The Event Assembler + Sappy Dropbox sources are **best-effort** with a visible Browse fallback. The download/extract/locate/Git-install flow is fully unit-tested with an injected download/installer step (no live network or installer in CI) (closes #1031); Special OAM editor (`OAMSPView`, port of WinForms `OAMSPForm`) — a read-only discovery + hex-inspection tool that finds special-OAM sprite-assembly pointer arrays by scanning the ROM's ARM-Thumb LDR literal-pool loads (cached per-ROM via `SpecialOamScanCore` + `PointerToolAutoSearchCore.BuildLdrMap`), labels them from the `oam_name_` resource, and dumps the selected entry's pointer array + OAM12 sub-blocks as hex (no sprite image — matching WinForms, which renders none for these entries) (#1179). The **In-ROM Magic Animation** editor (port of WinForms `ImageRomAnimeForm`, #1176) lists every `romanime_` spell/magic animation, renders a live per-frame preview (plain-TSA decode over the LZ77 image via the new Core `RomAnimeCore.TryRenderFrame`), and round-trips a single frame to/from PNG (`RomAnimeCore.ImportFrame` quantizes via `ImageImportService.LoadAndQuantize`, re-encodes 4bpp tiles + TSA, LZ77-writes and repoints the frame's image/TSA/palette slots under one undo scope with byte-identical fault restore); the multi-frame `.txt` animation-script + GIF subsystem is tracked as a follow-up. The **Font** editor (main game-font glyph editor, issue #1165) lists every glyph in the item/serif font hash table as a visual glyph grid (each row icon is the rendered 16x16 2bpp glyph), previews the selected glyph, exports/imports a single glyph as PNG, and bulk-exports/imports the whole font as a `.fontall.txt` manifest + per-glyph PNGs - all through the new Core `FontGlyphRenderCore` (enumerate/render/encode/import with find-or-append struct, repoint, ambient undo, byte-identical fault restore) plus `FontBulkExportCore`/`FontBulkImportCore` (atomic all-or-restore bulk); the `.ttf`/`.otf` desktop-font auto-generation is tracked as a follow-up; the Event Template windows (Template 1-6 + the Templates browser, plus EventTemplateImpl) are now real one-click event-byte generators backed by cross-platform `EventTemplateCore` (ports WinForms `EventScriptInnerControl.ConverteventTextToBin` + `EventTemplateImpl`) — each button/template generates real event bytes with a disassembled hex preview you copy into the event editor; context-required templates (XXXX/YYYY placeholders needing the parent editor map/label) are flagged and never emit partial bytes, with CALL/in-editor-insert deferred (#1434). The **Event Script editor** (#1435) is no longer a read-only disassembly viewer: backed by the cross-platform `EventScriptEditorCore` engine it now supports structural authoring — insert (from the command catalog or raw hex), delete, move up/down, import-from-text (append/replace), and a **Write-All** that re-serializes the resized script and writes it back under one undo scope (in place when it fits, otherwise relocate to free space + repoint all references via `RepointAllReferences`, refusing to relocate when no inbound reference is found so a script reachable only via an event-table/struct/hardcoded path is never orphaned); the engine is script-type agnostic (Procs/AI via `ScriptType`) with the Procs/AI view-wiring tracked as a follow-up. The **Skill Assignment - Unit (FE8N)** editor is no longer inert (#1452): when an FE8N-family skill patch (FE8N / FE8N Ver2 / FE8N Ver3 / Yugudora / Midori) is detected via `PatchDetectionService` and a unit address has been navigated to (`UnitEditorView`'s **Edit Skills** now opens it with `WindowManager.Navigate<...>(unitAddr)`), the view hides the "no patch installed" warning, reveals the field grid + Write button, and edits the open unit's Personal Skill / Skill Set 1 / Skill Set 2 bytes (struct offsets 0x27/0x28/0x29) - the same three bytes the WinForms `SkillAssignmentUnitFE8NForm` writes; the reveal/load logic is shared by `Opened` and `NavigateTo` so a reused window refreshes for the new unit, and the parent Unit Editor re-syncs its in-memory bytes on child close so a later parent Write cannot clobber the skill edit (the per-bit icon-picker UI of the WinForms form stays out of scope). |
| `FEBuilderGBA.Tests` | net9.0-windows | Unit and integration tests |
| `FEBuilderGBA.Core.Tests` | net9.0 | Cross-platform Core unit tests (runs on Linux/macOS/Windows). Includes the SkiaSharp Android version-guard (declared/runtime/restored-graph 2.88.x pin) + render byte-parity smoke tests (image decode/PNG exact, font within tolerance) that the advisory emulator-parity CI (#1125) now runs on API-34 `x86_64` (the only CI-bootable ABI — `x86` dropped at API 31+) |
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

**Public Resources:** [FE-Repo](https://github.com/Klokinator/FE-Repo) (graphics) and [FE-Repo-Music-No-Preview](https://github.com/laqieer/FE-Repo-Music-No-Preview) (music) are included as submodules in `resources/`. **Run `git submodule update --init resources/FE-Repo` (graphics) and `git submodule update --init resources/FE-Repo-Music-No-Preview` (music) before using the FE-Repo buttons** — an uninitialized submodule leaves an empty placeholder directory; the browser now detects this and shows an actionable *"FE-Repo not found. Run: git submodule update --init resources/FE-Repo"* message (the music browser shows the music submodule's command) with a **Copy git command** button instead of an empty tree (#1380). Browse and insert resources directly from the portrait editor (FE-Repo button), the Portrait Import Wizard (FE-Repo button + PNG/BMP drag-and-drop + advanced palette options: Auto-quantize / Share with target slot / Custom palette file + Fuchidori black-outline checkbox — #662 + Detail expander for eye/mouth block coords B20-B23 on FE7/FE8 — #663 Slice A + eye/mouth crop NumericUpDowns and a frame selector 0-10 with WF status labels — #707 Slice A + a per-frame live preview pane that composites the selected frame's eye/mouth crop onto the 96x80 face as the crop/block/frame NUDs change, porting WF `GenPreviewMainChar` — #975), and the Song Exchange tool (FE-Repo-Music button) in both WinForms and Avalonia. **Per-editor FE-Repo buttons (#1380):** each graphics editor's FE-Repo button behaves exactly like its Import button but sources the file from the correct FE-Repo subfolder for that graphics type (resolved by the shared Core helper `FERepoResourceBrowser.GetFERepoFolderForEditor`). Wired so far: Unit Wait Icon, Unit Move Icon, Item Icon (WinForms + Avalonia), plus Background Image and Generic Enemy Portrait (Avalonia); **and (#1393) the Battle Background editor (`ImageBattleBGView`, seeded to *Battle Frames &amp; Backgrounds*) and the Big CG editor (`BigCGViewerView`, seeded to *Background CGs*) in Avalonia** — each routes the picked file through that editor's existing import path with `strictSize` so a wrong-sized asset is rejected, not silently cropped. #1393 also corrected the Background Image / CG resolver mapping from the empty *CG Images* folder to the populated *Background CGs* folder. **Final batch (#1397):** the remaining editors that map cleanly to a verified, populated, dimension-matched FE-Repo folder were wired in one pass — WinForms **CG** (`ImageCGForm`/`ImageCGFE7UForm` → *Background CGs*, 256×160), **BG** (`ImageBGForm` → *Background CGs*), **Battle Background** (`ImageBattleBGForm` → *Battle Frames &amp; Backgrounds*, 240×160), **FE6 Portrait** (`ImagePortraitFE6Form` → *Portrait Repository*) and **Generic Enemy Portrait** (`ImageGenericEnemyPortraitForm` → *Special - Generic Minimugs*, 32×32); Avalonia **Battle Background** (`BattleBGViewerView`), **Portrait** (`PortraitViewerView`, `ImagePortraitFE6View` → *Portrait Repository*) and the four writable **SkillConfig** skill-icon editors (`SkillConfigSkillSystemView`, `SkillConfigFE8NVer2SkillView`, `SkillConfigFE8NVer3SkillView`, `SkillConfigFE8UCSkillSys09xView` → *Special - Skill Icons*, 16×16 — remapped onto the ROM's 16-color skill palette so a 17+-color sheet is reduced, not corrupted). Editors with **no** dimension-matched/populated source folder or whose import is a script/palette/multi-strip rather than a single image are intentionally **dropped as Unsupported** (button hidden via `FERepoPickHelper.IsSupported`): the Avalonia CG editors (their 240×160 import does not match the 256×160-only *Background CGs* folder), the Magic FEditor/CSA editors and battle-anime/spell-anime/SkillConfig-v1 (script/package import), palette editors (palette, not image), TSA/MapStyle/RomAnime/MapActionAnimation (variable/context dimension), chapter-title (256×16 strip), terrain/battle-screen/system-icon (multi-strip/multiplexed), Font/OP (per-glyph), and the World Map editors — each because seeding a wrong/empty path is worse than no button. **FE-Repo-Music button on the Song editors (#1383):** the Song Track Editor and the Song Exchange tool (both WinForms + Avalonia) gained an **FE-Repo-Music** button — the music sibling of the per-editor FE-Repo buttons. It opens the FE-Repo-Music browser and imports the selected music file through the *exact same* music-import path as the regular Import Music File button (`SongTrackForm.ImportMusicFileToSong` in WinForms; `SongTrackView.ImportMusicPath` in Avalonia) — no second import code path. In WinForms, Song Track imports the selected song directly (reusing the AutoDrag → Import flow) and Song Exchange calls the shared `ImportMusicFileToSong`; in Avalonia, Song Exchange (a ROM→ROM transplant with no per-song instrument picker of its own) navigates to the Song Track Editor for the selected destination song and hands it the chosen path (`SongTrackView.ImportMusicFromExternal`), so both Song editors funnel through the one dispatcher. The button is shown only when the FE-Repo-Music submodule is checked out (`FERepoResourceBrowser.IsMusicRepoAvailable`, reusing the #1380 empty-placeholder-as-not-found guard). The WinForms Song Exchange button was upgraded from clipboard-copy to a real import.

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
# Open a decomp project directory and report its mode + resolved preview ROM
dotnet run --project FEBuilderGBA.CLI -- --project=path/to/decomp --rom-info
# Resolve an address to a decomp project symbol (.map/ELF/.sym/JSON over shipped)
dotnet run --project FEBuilderGBA.CLI -- --project=path/to/decomp --resolve-addr=0x08000100
# Decomp diff-to-source migration assistant: classify built-vs-edited ROM changes
# (range/symbol/category/source-file/confidence) for migrating edits back to source.
# Advisory + READ-ONLY — never writes the ROM or source.
dotnet run --project FEBuilderGBA.CLI -- --migrate-diff --project=path/to/decomp --rom2=edited.gba --out=migrate.tsv
dotnet run --project FEBuilderGBA.CLI -- --list-tables
dotnet run --project FEBuilderGBA.CLI -- --export-palette --rom=rom.gba --addr=0x5524 --out=palette.pal --colors=16
dotnet run --project FEBuilderGBA.CLI -- --import-palette --rom=rom.gba --addr=0x5524 --in=palette.pal

# Decomp asset export: export assets to a decomp source-tree (never inserts into ROM)
# --kind=palette  → JASC .pal (faithful lossless round-trip via gbagfx)
# --kind=graphics → indexed PNG (color type 3, palette indices preserved) + sidecar .pal
# --kind=map      → .mar tilemap (raw u16<<3 entries, WF SaveAsMAR parity) + sidecar JSON
# --kind=text     → texts.txt + textdefs.txt (migration aid; not a lossless macro round-trip)
# --kind=shop     → shops.event (EA .event migration aid; recreates each u16 ITEM_NONE-terminated
#                   shop list at its source address; #1149). Shops have no manifest-owned C-array
#                   row table, so this is an EXPORT/migration path, NOT source-backed in-place editing.
# Note: MIDI/portrait/battle-animation exports use existing commands:
#   --export-midi, --export-portrait-all, --export-battle-anime
dotnet run --project FEBuilderGBA.CLI -- --export-asset --kind=palette --rom=rom.gba --addr=0x5524 --colors=16 --out=gfx/palette.pal
dotnet run --project FEBuilderGBA.CLI -- --export-asset --kind=graphics --project=decomp/ --addr=0x123000 --width=64 --height=64 --palette-addr=0x124000 --out=gfx/tiles.png
dotnet run --project FEBuilderGBA.CLI -- --export-asset --kind=map --rom=rom.gba --addr=0x200000 --out=map/chapter1.mar
dotnet run --project FEBuilderGBA.CLI -- --export-asset --kind=mapchange --rom=rom.gba --addr=0x300000 --width=15 --height=10 --out=map/chapter1.change
dotnet run --project FEBuilderGBA.CLI -- --export-asset --kind=mapanime2pal --rom=rom.gba --addr=0x400000 --count=16 --out=map/chapter1.mapanime2pal
dotnet run --project FEBuilderGBA.CLI -- --export-asset --kind=objtiles --rom=rom.gba --addr=0x400000 --out=map/chapter1.objtiles
dotnet run --project FEBuilderGBA.CLI -- --export-asset --kind=mapchipconfig --rom=rom.gba --addr=0x500000 --out=map/chapter1.mapchipconfig
dotnet run --project FEBuilderGBA.CLI -- --export-asset --kind=text --rom=rom.gba --out=text/
dotnet run --project FEBuilderGBA.CLI -- --export-asset --kind=shop --rom=rom.gba --out=shops/

# Decomp music: export a voicegroup (M4A instrument set) as reviewable decomp source macro asm
# (sound/voicegroups/voicegroupNNN.s, using asm/macros/music_voice.inc). READ-ONLY — never mutates the ROM.
# Pick the voicegroup by a song id (resolved from the song header) or by a raw ROM address.
dotnet run --project FEBuilderGBA.CLI -- --export-voicegroup --rom=rom.gba --song-id=1 --out=sound/voicegroups/voicegroup001.s
dotnet run --project FEBuilderGBA.CLI -- --export-voicegroup --project=decomp/ --voicegroup-addr=0x207470 --number=42 --out=sound/voicegroups/voicegroup042.s
# Supported voice types are emitted as the exact music_voice.inc macros: voice_directsound /
# voice_directsound_no_resample / voice_directsound_alt, voice_square_1, voice_square_2,
# voice_programmable_wave, voice_noise, voice_keysplit, voice_keysplit_all. KeySplit/drum
# sub-voicegroups and DirectSound sample pointers are emitted as VALID raw 0x08XXXXXX macro args
# plus an "unresolved pointer" diagnostic (no guessed decomp symbol, no inlined sub-table — a
# documented manual step). An unknown/0x18 voice type becomes a commented placeholder + diagnostic,
# NEVER a wrong macro. This is an EXPORT/source-helper (a reviewable .s), NOT a byte-pinned round-trip
# and NOT a full M4A re-assembler; wire the emitted voicegroupNNN.s into the decomp build via
# songs.mk / a song's -G voicegroup number. Output paths are confined under the project root in
# --project mode.

# Decomp battle animations: export a FEBuilder/FEditor-decoded battle animation as reviewable decomp
# source (banim_<TAG>_motion.s, using fireemblem8u's include/banim_code.inc + include/banim_code_frame.inc)
# plus .pal palette sidecars + a .json registration manifest. READ-ONLY — never mutates the ROM.
# Pick the animation by its 0-based table id or by a raw 32-byte-record ROM address.
dotnet run --project FEBuilderGBA.CLI -- --export-battle-anim-decomp --rom=rom.gba --animation-id=1 --out=banim/banim_arcf_ar1_motion.s --tag=arcf_ar1
dotnet run --project FEBuilderGBA.CLI -- --export-battle-anim-decomp --project=decomp/ --banim-addr=0xC00028 --out=data/banim/banim_anim000_motion.s
# The per-mode SCRIPT stream is FULLY emitted: 0x86 frame commands -> banim_code_frame
# duration,sheet,frame,oam; all 0x85 control/sound commands -> banim_code_85 <low24> (the generic
# macro with the exact payload — no guessed named macro); 0x80000000 -> banim_code_end_mode. The
# 12-byte OAM entries become banim_frame_oam (both +20 oam_r and +24 oam_l sides — shared sides are
# aliased, differing sides emit both); affine matrix entries are emitted as raw .hword + a diagnostic.
# The mode table is the upstream 24-word layout (12 mode offsets + 12 trailing zeros). Raw sheet
# graphics pointers (0x08XXXXXX), version-specific UnHuffman frame pointers, and unknown command
# bytes become VALID-arg/commented placeholders + actionable diagnostics, NEVER guessed macros.
# This is an EXPORT/migration aid (a reviewable .s + sidecars), NOT a byte-pinned round-trip: the
# banim_data[]/BattleAnimDef/linker-script registration step and graphics-sheet + TSA asset import
# remain MANUAL (see the emitted .json manifest's checklist; TSA shared tilesets cannot be
# regenerated from a single PNG). GUI (Avalonia/WinForms) wiring is a documented follow-up. Output
# paths are confined under the project root in --project mode. Scoped to FE8U.

# Decomp .mar map LAYOUT re-import + round-trip verify (never mutates the ROM):
# --import-asset    → reconstruct the RAW UNCOMPRESSED tilemap blob ([w][h] + w*h raw u16 LE)
#                     from an edited .mar (+ its sidecar .mar.json); the build re-compresses from source.
# --roundtrip-asset → validate + prove the .mar u16 LAYOUT body round-trips byte-identically.
# The .mar map LAYOUT is now export AND import/verify and is lossless for raw tilemap u16 entries
# < 0x2000 (palette/flag bits 13-15 clear).
# --export-asset --kind=map REJECTS a tilemap with any raw u16 entry >= 0x2000 (the <<3 .mar encoding
# would truncate its top 3 bits — the palette/flag bits) rather than emit a silently-lossy .mar, so every exported .mar is
# guaranteed to round-trip. The compressed ROM bytes are NOT byte-pinned (FEBuilder's LZ77 packer is
# non-canonical, so the decomp build re-compresses from source). OBJ/TSA/tile-animations and the
# 12-byte map-change RECORD chain remain export-only / manual; the map-change OVERLAY tile-data
# block, by contrast, is now source export/import/round-trip + ROM-verify (see --kind=mapchange below).
dotnet run --project FEBuilderGBA.CLI -- --import-asset --kind=map --in=map/chapter1.mar --out=map/chapter1.tmap_raw.bin
dotnet run --project FEBuilderGBA.CLI -- --roundtrip-asset --kind=map --in=map/chapter1.mar

# Decomp map-change OVERLAY tile data block (#1355): a RAW UNCOMPRESSED u16 LE array of width*height
# config-descriptor indices (the record +8 change pointer's target) — NOT the .mar tile layout and
# NOT the 12-byte change-RECORD chain (terminator/flagID/PLIST metadata).
# --export-asset --kind=mapchange  → read the live ROM overlay block (--addr=change_mar, --width, --height)
#                                     into a .change file + .change.json sidecar (format "febuilder-mapchange-u16").
# --import-asset --kind=mapchange  → identity copy of the validated .change body to a raw blob (NO header, NO shift, NO LZ77).
# --roundtrip-asset --kind=mapchange → structure-exact identity proof (body length == width*height*2).
# --verify-asset --kind=mapchange  → byte-exact ROM-backed mismatch proof (reads the ROM READ-ONLY; the
#                                     ONLY ROM-backed verification — export/import never touch the ROM).
# srcAddr in the sidecar is provenance metadata ONLY (no symbol/owner is fabricated).
dotnet run --project FEBuilderGBA.CLI -- --import-asset --kind=mapchange --in=map/chapter1.change --out=map/chapter1.change_raw.bin
dotnet run --project FEBuilderGBA.CLI -- --roundtrip-asset --kind=mapchange --in=map/chapter1.change
dotnet run --project FEBuilderGBA.CLI -- --verify-asset --kind=mapchange --rom=rom.gba --addr=0x300000 --width=15 --height=10 --in=map/chapter1.change

# Decomp map tile-animation-2 PALETTE block (#1360): the structural TWIN of --kind=mapchange — a RAW
# UNCOMPRESSED u16 LE array of `count` 15-bit GBA colors (count*2 bytes), reached by each anime-2 entry's
# +0 pointer — NOT the anime-2 ENTRY/PLIST table and NOT LZ77. Single --count descriptor (1..255), no width/height.
# --export-asset --kind=mapanime2pal  → read the live ROM palette block (--addr=anime2 entry +0 ptr, --count)
#                                        into a .mapanime2pal file + .json sidecar (format "febuilder-mapanime2-pal-u16").
# --import-asset --kind=mapanime2pal  → identity copy of the validated body to a raw blob (NO header, NO shift, NO LZ77).
# --roundtrip-asset --kind=mapanime2pal → structure-exact identity proof (body length == count*2).
# --verify-asset --kind=mapanime2pal  → byte-exact ROM-backed mismatch proof (reads the ROM READ-ONLY).
# The CLI takes EXPLICIT --addr/--count (no entry-index auto-resolve); srcAddr in the sidecar is provenance ONLY.
dotnet run --project FEBuilderGBA.CLI -- --import-asset --kind=mapanime2pal --in=map/chapter1.mapanime2pal --out=map/chapter1.mapanime2pal_raw.bin
dotnet run --project FEBuilderGBA.CLI -- --roundtrip-asset --kind=mapanime2pal --in=map/chapter1.mapanime2pal
dotnet run --project FEBuilderGBA.CLI -- --verify-asset --kind=mapanime2pal --rom=rom.gba --addr=0x400000 --count=16 --in=map/chapter1.mapanime2pal

# Decomp OBJ tileset LZ77 decompressed-payload source (#1371): exports the DECOMPRESSED 4bpp payload,
# NOT a byte-pinned LZ77 stream (FEBuilder's packer is non-canonical; the build re-compresses).
# --addr is the DEREFERENCED OBJ LZ77 stream address (NOT RomInfo.map_obj_pointer).
# FE7 obj2 split is out of scope (a separate stream/--addr). NOT chipset TSA/config, NOT tile animations 1/2.
# --export-asset --kind=objtiles  → LZ77-decompress the live ROM block at --addr into a .objtiles file
#                                    + .json sidecar (format "febuilder-objtiles-lz77", with length + provenance srcAddr).
# --import-asset --kind=objtiles  → identity copy of the validated decompressed body to a raw blob (NO ROM read, NO LZ77).
# --roundtrip-asset --kind=objtiles → structure-exact identity proof (body length == sidecar length).
# --verify-asset --kind=objtiles  → read-only decompress-and-byte-compare ROM mismatch proof (re-decompresses the ROM block).
dotnet run --project FEBuilderGBA.CLI -- --export-asset --kind=objtiles --rom=rom.gba --addr=0x400000 --out=map/chapter1.objtiles
dotnet run --project FEBuilderGBA.CLI -- --import-asset --kind=objtiles --in=map/chapter1.objtiles --out=map/chapter1.objtiles_raw.bin
dotnet run --project FEBuilderGBA.CLI -- --roundtrip-asset --kind=objtiles --in=map/chapter1.objtiles
dotnet run --project FEBuilderGBA.CLI -- --verify-asset --kind=objtiles --rom=rom.gba --addr=0x400000 --in=map/chapter1.objtiles

# Decomp chipset TSA/config LZ77 decompressed-payload source (#1375): the structural twin of objtiles.
# Exports the DECOMPRESSED chipset config payload, NOT a byte-pinned LZ77 stream (the build re-compresses).
# --addr is the DEREFERENCED config LZ77 stream address (e.g. the CONFIG-PLIST pointer dereferenced,
# NOT RomInfo.map_config_pointer); FE7 split layouts use a separate per-plist --addr.
# NOT the anime-1/anime-2 entry tables, NOT the map-change record chain, NOT the .mar layout.
# --export-asset --kind=mapchipconfig  → LZ77-decompress the live ROM block at --addr into a .mapchipconfig
#                                         file + .json sidecar (format "febuilder-mapchipconfig-lz77", length + provenance srcAddr).
# --import-asset --kind=mapchipconfig  → identity copy of the validated decompressed body to a raw blob (NO ROM read, NO LZ77).
# --roundtrip-asset --kind=mapchipconfig → structure-exact identity proof (body length == sidecar length).
# --verify-asset --kind=mapchipconfig  → read-only decompress-and-byte-compare ROM mismatch proof (re-decompresses the ROM block).
dotnet run --project FEBuilderGBA.CLI -- --export-asset --kind=mapchipconfig --rom=rom.gba --addr=0x500000 --out=map/chapter1.mapchipconfig
dotnet run --project FEBuilderGBA.CLI -- --import-asset --kind=mapchipconfig --in=map/chapter1.mapchipconfig --out=map/chapter1.mapchipconfig_raw.bin
dotnet run --project FEBuilderGBA.CLI -- --roundtrip-asset --kind=mapchipconfig --in=map/chapter1.mapchipconfig
dotnet run --project FEBuilderGBA.CLI -- --verify-asset --kind=mapchipconfig --rom=rom.gba --addr=0x500000 --in=map/chapter1.mapchipconfig

# Decomp map tile-animation-1 per-entry GRAPHICS block (#1389): the structural TWIN of --kind=mapchange/mapanime2pal
# — a RAW UNCOMPRESSED 4bpp tile-byte block sized by the entry's +2 u16 length (NOT LZ77: the WF read/import/rebuild
# paths treat it as raw ImageToByte16Tile bytes / a rebuild IMG block, so this is NOT the objtiles/mapchipconfig
# decompress pattern). --addr is the anime-1 entry +4 graphics pointer (DEREFERENCED; the inverse of anime-2's +0)
# and --length is the entry +2 byte length. NOT the anime-1 ENTRY/PLIST table (pointer-per-row, no clean source
# owner — stays guarded, tracked by #1389) and NOT the .mar layout.
# --export-asset --kind=mapanime1gfx   → copy the raw ROM block at --addr (--length bytes) into a .mapanime1gfx
#                                         file + .json sidecar (format "febuilder-mapanime1gfx-raw4bpp", length + provenance srcAddr).
# --import-asset --kind=mapanime1gfx   → identity copy of the validated raw body to a blob (NO ROM read, NO LZ77).
# --roundtrip-asset --kind=mapanime1gfx → structure-exact identity proof (body length == sidecar length).
# --verify-asset --kind=mapanime1gfx   → read-only RAW byte-compare ROM mismatch proof (no decompression).
dotnet run --project FEBuilderGBA.CLI -- --export-asset --kind=mapanime1gfx --rom=rom.gba --addr=0x600000 --length=512 --out=map/chapter1.mapanime1gfx
dotnet run --project FEBuilderGBA.CLI -- --import-asset --kind=mapanime1gfx --in=map/chapter1.mapanime1gfx --out=map/chapter1.mapanime1gfx_raw.bin
dotnet run --project FEBuilderGBA.CLI -- --roundtrip-asset --kind=mapanime1gfx --in=map/chapter1.mapanime1gfx
dotnet run --project FEBuilderGBA.CLI -- --verify-asset --kind=mapanime1gfx --rom=rom.gba --addr=0x600000 --length=512 --in=map/chapter1.mapanime1gfx

# Decomp source-backed table writer: rewrite the owning C array element (or JSON
# element) of a structured table entry instead of mutating the preview ROM (the
# source is the source of truth). The table must declare a source owner in the
# manifest tables[]. Supported formats: C struct array (format="cstruct" or unset)
# AND JSON (format="json"). Only plain INTEGER value tokens are rewritten — C
# integer literals and JSON integer Number tokens; JSON floats/exponents (and C
# floats/macros/expressions/strings) are reported as UnsupportedField, not
# rewritten. Every other byte of the file (comments, trailing commas, whitespace,
# line endings, BOM) is preserved. On success the project is flagged "needs rebuild".
#
# Coverage (#1132 + #1141 + #1148): items, units (alias: characters), classes, and
# chapter settings (map_settings). Signed
# fields (unit base stats, class promotion gains) are driven off the manifest
# fields[].signed flag — pass the two's-complement magnitude (e.g. --value=255 for
# an int8 -1); a negative value is re-emitted as a "-N" decimal. --field/--value
# are REPEATABLE: each --field must be paired with a FOLLOWING --value (other flags
# may appear between them; a 2nd --field before its --value, or an unpaired
# --field/--value, is a usage error) so multiple fields update one entry
# atomically.
#
# Chapter / map scope (#1148): chapter SETTINGS (map_settings) are source-backed —
# the scalar struct fields (weather, fog, PLIST index bytes, BGM ids, difficulty
# bytes, chapter number, clear-condition text ids, escape markers, etc.) rewrite the
# owning C array element (or a flat-numeric JSON element). The .mar map LAYOUT is now
# source import + round-trip verify (--import-asset/--roundtrip-asset --kind=map, PR #1346),
# and the map-change OVERLAY tile-data block is source export/import/round-trip + read-only
# byte-exact ROM verify (--export-asset/--import-asset/--roundtrip-asset/--verify-asset
# --kind=mapchange, PR #1357) — neither mutates the preview ROM. The OBJ tileset
# (LZ77 decompressed-payload) is now source export/import/round-trip + read-only
# decompress-and-byte-compare ROM verify (--export-asset/--import-asset/
# --roundtrip-asset/--verify-asset --kind=objtiles, #1371/PR #1372), and the chipset
# TSA/config (LZ77 decompressed-payload, its structural twin) is now source export/import/
# round-trip + read-only decompress-and-byte-compare ROM verify (--export-asset/--import-asset/
# --roundtrip-asset/--verify-asset --kind=mapchipconfig, #1375) — neither
# mutates the preview ROM. The map tile-animation-1 per-entry GRAPHICS block (a RAW 4bpp block
# sized by the entry +2 length, reached by the entry +4 pointer) is now source export/import/
# round-trip + read-only RAW byte-compare ROM verify (--kind=mapanime1gfx, #1389 — RAW, NOT LZ77;
# --addr is the DEREFERENCED +4 graphics pointer, --length the +2 byte length). ROM-only/manual:
# the event/difficulty POINTER fields (D0/EventDataPtr, D96–D108) and the remaining POINTER-HEAVY
# map STRUCTURAL tables (palette, the tile-animation-1 ENTRY/PLIST table #1389, the anime-2
# ENTRY/PLIST table #1390) and the 12-byte map-change RECORD chain #1391 (structured pointer
# metadata: terminator/flagID/PLIST — NOT the now-source-backed per-entry data blocks)
# — these structural tables stay guarded (pointer-per-row/record, ambiguous source ownership, they
# need a manifest source owner not yet defined; tracked as narrower sub-issues under #1375).
# Migrate the dereferenced data blocks via --export-asset #1133/#1140, and the nested `chapter_settings.json`
# shape (nested objects / bools /
# enum strings / split obj1Id|obj2Id are reported UnsupportedField/Manual, never
# silently corrupted — flat top-level Number fields in that file still rewrite).
# Shops: their lists CAN now be rewritten IN-PLACE in source via `--write-shop` (#1347)
# when the manifest declares a `u16-list` list-owner for the shop's resolved DATA symbol
# (variable-length ITEM_NONE-terminated u16 lists reached via scattered hensei/worldmap/
# event-cond pointers). With no decomp .map/.elf carrying the symbol AND no manifest
# list-owner, this degrades to the `--export-asset --kind=shop` EA .event export above (an
# export/migration aid, #1149). Support data (support_units,
# support_attributes, support_talks) is source-writable when the manifest declares a
# source owner for those tables (#1149).
#
# Exit codes: 0 = source rewritten; 2 = ROM-only / manual / not owned; 1 = usage/parse error.
dotnet run --project FEBuilderGBA.CLI -- --write-source --project=path/to/decomp --table=items --id=1 --field=might --value=0x0A
dotnet run --project FEBuilderGBA.CLI -- --write-source --project=path/to/decomp --table=units --id=1 --field=hp --value=18 --field=pow --value=7
dotnet run --project FEBuilderGBA.CLI -- --write-source --project=path/to/decomp --table=map_settings --id=1 --field=Weather --value=4
# support_units b0 is a LEADING field — safe with a minimal fields[] = [b0]:
dotnet run --project FEBuilderGBA.CLI -- --write-source --project=path/to/decomp --table=support_units --id=5 --field=b0 --value=0x06
# support_attributes b1 / support_talks w4 are NON-leading — for positional initializers the
# owner must declare the full ordered prefix up to them (or use designated .bN= initializers):
dotnet run --project FEBuilderGBA.CLI -- --write-source --project=path/to/decomp --table=support_attributes --id=2 --field=b1 --value=0x05
dotnet run --project FEBuilderGBA.CLI -- --write-source --project=path/to/decomp --table=support_talks --id=3 --field=w4 --value=0x1A0
dotnet run --project FEBuilderGBA.CLI -- --write-source --project=path/to/decomp --table=items --id=1 --field=might --value=10 --out-diff=change.diff

# Source-backed SHOP LIST writer (#1347): rewrite an owning u16 ITEM_NONE-terminated list
# in place. Resolve the owner by --symbol=<name> directly, or by --shop-addr=<ROM offset>
# (resolved to a list symbol via the project .map/.elf/.sym + a manifest u16-list owner).
# --items=<id:qty,...> ids/qtys are hex-or-dec 0..255, id != 0; an empty --items= empties
# the shop. The whole list targets a RAW-HEX list (the export format); a list containing a
# non-literal macro element is REFUSED (no-clobber) — export to raw hex or edit by hand.
# Exit codes: 0 = source rewritten (or no-op); 2 = any advisory/no-write outcome (not owned, ROM-only,
# manual, unsupported-field/no-clobber refusal, rejected path-escape, malformed manifest, not decomp mode);
# 1 = usage/parse error (and unexpected-error / source-not-found).
dotnet run --project FEBuilderGBA.CLI -- --write-shop --project=path/to/decomp --symbol=ItemList_WM_FluornArmory --items=0x01:5,0x02:3
dotnet run --project FEBuilderGBA.CLI -- --write-shop --project=path/to/decomp --shop-addr=0xB2A18 --items=0x16:1

# Build decomp project ROM (run the manifest-declared build command).
# Security gate: --yes is required to actually execute the build command.
# Without --yes, prints the command that would run (dry-run) and exits 0.
# Exit codes: 0 = build succeeded (or dry-run); 1 = build failed / usage error; 2 = not opted in.
dotnet run --project FEBuilderGBA.CLI -- --build-project --project=path/to/decomp --yes
dotnet run --project FEBuilderGBA.CLI -- --build-project --project=path/to/decomp --reload --yes
dotnet run --project FEBuilderGBA.CLI -- --build-project --project=path/to/decomp --reload --yes --timeout=300000

# Build SkiaSharp image backend
dotnet build FEBuilderGBA.SkiaSharp/FEBuilderGBA.SkiaSharp.csproj

# Build Avalonia GUI
dotnet build FEBuilderGBA.Avalonia/FEBuilderGBA.Avalonia.csproj

# Run Avalonia GUI with a ROM
dotnet run --project FEBuilderGBA.Avalonia -- --rom path/to/rom.gba

# Open a decomp project directory (loads its built ROM as a read-only preview)
dotnet run --project FEBuilderGBA.Avalonia -- --project path/to/decomp

# Run Avalonia smoke test (loads ROM, opens editors, selects items, verifies no crash)
dotnet run --project FEBuilderGBA.Avalonia -- --rom path/to/rom.gba --smoke-test

# Run Avalonia data verification (loads ROM, opens editors, cross-checks ViewModel data vs raw ROM + NumericUpDown UI display + text encoding)
dotnet run --project FEBuilderGBA.Avalonia -- --rom path/to/rom.gba --data-verify

# Run full data verification (iterates ALL list items per editor, per-field cross-check via GetFieldOffsetMap)
dotnet run --project FEBuilderGBA.Avalonia -- --rom path/to/rom.gba --data-verify-full

# Capture Avalonia screenshots of all editors (saves PNGs to --screenshot-dir)
dotnet run --project FEBuilderGBA.Avalonia -- --rom path/to/rom.gba --screenshot-all --screenshot-dir=./screenshots

# Optionally select a non-default tab (by AutomationId) before each capture, so a
# specific tab is shown in the PNG (editors without a matching tab are unchanged)
dotnet run --project FEBuilderGBA.Avalonia -- --rom path/to/rom.gba --screenshot-all --screenshot-tab=TextViewer_Translate_Tab --screenshot-dir=./screenshots

# Optionally force an IsVisible-toggled panel visible (by AutomationId) before each
# capture, so a category sub-panel normally hidden behind a selection state shows up
# in the PNG (editors without a matching control are unchanged). E.g. render the
# EventCond TUTORIAL panel (where the Text ID IdFieldControl lives):
dotnet run --project FEBuilderGBA.Avalonia -- --rom path/to/rom.gba --screenshot-all --screenshot-show-panel=EventCond_TutorialPanel_Border --screenshot-dir=./screenshots

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

### Decomp Project Support (preview)

FEBuilderGBA can open a **decompilation project** directory (a source tree that
builds a `.gba` ROM, e.g. a `fireemblem8u` / `fe6` decomp). The built ROM is
loaded as a **read-only preview** — the source is the source of truth, so saving
over the built ROM is intentionally blocked (edit the source and rebuild instead).
A project is detected by a `febuilder.project.json` manifest or by heuristics
(`ROM :=` / `BUILD_NAME :=` in the root `Makefile`, `*.sha1`, `ldscript.txt`,
`src/`+`asm/`, `tools/agbcc/`). The built ROM is resolved from the manifest
`builtRom`, the Makefile ROM stem, or a `*.gba` that has a same-stem `*.elf`
sibling.

- **Avalonia GUI:** File → *Open Decomp Project...* (or `--project path/to/decomp`
  at launch). When a project is open, a *"Source-backed project · ROM is a build
  preview"* badge appears in the toolbar.
- **CLI:** `--project=<dir> --rom-info` reports the resolved preview ROM plus a
  `Mode: Decomp (preview ROM <path>)` line and a `Symbols:` artifact breakdown.
- **CLI:** `--project=<dir> --resolve-addr=<hex>` resolves an address to a decomp
  project symbol, discovered from the project's `.map` / ELF / `.sym` / JSON
  artifacts (precedence `.map` → ELF → `.sym` → JSON; the project symbol wins at
  the same address) layered over the shipped asmmap. It prints the symbol name,
  its source artifact, and the in-span offset. The same merged symbol table backs
  the Pointer Tool "What is this address?" lookup when a project is open.
- **CLI:** `--migrate-diff --project=<dir> --rom2=<editedRom> [--out=report.tsv]`
  is the **diff-to-source migration assistant** (advisory + READ-ONLY). It diffs the
  project's built/baseline ROM against a FEBuilder-edited ROM, coalesces the changed
  byte ranges (small-gap merge), and classifies each range for a contributor
  migrating the edit back to source: nearest symbol (via the slice-2 resolver),
  category (struct-table / graphics-palette / compressed / map / text / music /
  unknown), a source-file suggestion (the `.map` object path / section when known),
  and an **honest confidence** — High only when a covering project symbol names the
  whole range, Medium for an inferred known-range, Low + *"manual migration required"*
  for compressed / unknown / ambiguous spans. It tracks `ChangedBytes` separately
  from the coalesced span and NEVER writes the ROM or source — the edited ROM is read
  but never treated as canonical final output.
- **CLI:** `--write-source --project=<dir> --table=<name> --id=<n> --field=<f> --value=<v>`
  is the **source-backed table writer** (#1132, extended in #1141). When a structured
  table declares a source owner in the manifest `tables[]` section (with
  `writePolicy: source`), this rewrites the owning **C array element** (`format: cstruct`)
  **or JSON element** (`format: json`) IN-PLACE — changing only the value token(s) for the
  requested field and leaving every other byte of the file identical (comments, trailing
  commas, whitespace, line endings, BOM preserved). Only **integer** value tokens are
  rewritten — C integer literals and JSON integer Number tokens; C hex tokens stay hex,
  decimal stays decimal. Non-integer values — macro/identifier/expression (C),
  string/bool/object/array (JSON), and **JSON floats/exponents** — are reported as
  UnsupportedField, not normalized (single-field intent → hard fail; bulk → skipped). Both
  designated-initializer (`.field = N`) and positional C elements are supported, and both
  JSON array and object-map forms. **Coverage: `items`, `units` (alias `characters`),
  `classes`, and `map_settings` (chapter settings, #1148).** **Signed fields** (unit base stats, class promotion gains) are driven off
  `fields[].signed` (+ optional `width` 1/2/4) — pass the two's-complement magnitude
  (e.g. `--value=255` for an int8 -1); a value that reinterprets negative is re-emitted as a
  `-N` decimal. The no-op check is **width-aware**: an existing bit-pattern literal (e.g.
  `0xFF` / `255` for an int8) is reinterpreted at the field width before comparing, so
  requesting its equivalent (`255` ⇔ `0xFF` ⇔ -1) is recognized as a no-op and the token is
  left untouched (no churn); an existing explicit `-N` literal is treated as already-signed.
  A **malformed/truncated JSON source** (e.g. a missing closing `]`) is validated against the
  whole document first and rejected with no write (the file stays byte-identical). `--field`/`--value`
  are **REPEATABLE**: each `--field` must be paired with a FOLLOWING `--value` (other flags may
  appear between them; a second `--field` before its `--value`, or an unpaired `--field`/`--value`,
  is a usage error) so multiple fields of one entry update in a single atomic source write.
  **Chapter / map (#1148):** `map_settings` scalar fields are source-backed. The **`.mar` map
  LAYOUT** is now source import + round-trip verify (`--import-asset`/`--roundtrip-asset --kind=map`,
  #1148/PR #1346 — lossless u16 layout body for raw entries < 0x2000; the compressed container is
  re-derived by the build, not byte-pinned), and the **map-change OVERLAY** tile-data block is source
  export/import/round-trip + read-only byte-exact ROM verify (`--export-asset`/`--import-asset`/
  `--roundtrip-asset`/`--verify-asset --kind=mapchange`, #1355/PR #1357 — source-level structure-exact
  AND byte-exact ROM compare, NOT a byte-pinned ROM re-insertion). The **map tile-animation-2 PALETTE**
  block — a raw uncompressed `u16` array of `count` 15-bit GBA colors reached by each anime-2 entry's
  `+0` pointer — joins the same raw-u16 source-backed classes with export/import/round-trip + read-only
  byte-exact ROM verify (`--kind=mapanime2pal`, #1360); the **OBJ tileset** (LZ77 decompressed-payload)
  joins the same raw source-backed classes with export/import/round-trip + read-only decompress-and-byte-compare
  ROM verify (`--kind=objtiles`, #1371/PR #1372 — decompressed-payload equivalence, NOT compressed-stream
  byte identity; `--addr` is the DEREFERENCED OBJ LZ77 stream address); the **chipset TSA/config** block —
  its structural twin, a single LZ77 stream reached by one dereferenced CONFIG-PLIST pointer — joins the
  same LZ77 decompressed-payload source-backed classes with export/import/round-trip + read-only
  decompress-and-byte-compare ROM verify (`--kind=mapchipconfig`, #1375 — decompressed-payload equivalence,
  NOT compressed-stream byte identity; `--addr` is the DEREFERENCED config LZ77 stream address, NOT
  `RomInfo.map_config_pointer`); the **map tile-animation-1 per-entry GRAPHICS** block — a RAW uncompressed
  4bpp tile-byte block sized by the entry's `+2` length, reached by each anime-1 entry's `+4` pointer (the
  inverse of anime-2's `+0`) — joins the same RAW source-backed classes (NOT LZ77, NOT the objtiles/mapchipconfig
  decompress pattern: the WF read/import/rebuild paths treat it as raw `ImageToByte16Tile` 4bpp bytes / a
  rebuild `IMG` block) with export/import/round-trip + read-only RAW byte-compare ROM verify
  (`--kind=mapanime1gfx`, #1389 — `--addr` is the DEREFERENCED `+4` graphics pointer, `--length` the `+2` byte
  length). The remaining pointer-heavy map paths stay guarded. ROM-only/manual remain the
  event/difficulty POINTER fields (D0/EventDataPtr, D96–D108) and the remaining POINTER-HEAVY map STRUCTURAL
  tables (palette, the tile-animation-1 ENTRY/PLIST table #1389, the anime-2 ENTRY/PLIST table #1390) and the
  12-byte map-change RECORD chain #1391 (structured pointer metadata — terminator/flagID/PLIST — NOT the overlay
  tile-data block nor the anime-2 palette block nor the anime-1 graphics block nor the OBJ tileset nor the chipset
  TSA/config, all five now source-backed; these structural tables stay guarded — pointer-per-row/record, ambiguous
  source ownership, they need a manifest source owner not yet defined, tracked as narrower sub-issues under #1375 —
  migrate the dereferenced data blocks via `--export-asset` #1133/#1140), and the **nested** `chapter_settings.json`
  shape (nested objects / bools / enum strings / split `obj1Id`|`obj2Id` are reported
  UnsupportedField/Manual, never silently corrupted; flat top-level Number fields still rewrite).
  **Shop lists are source-writable in place via `--write-shop` (#1347)** when the manifest declares a `u16-list` list-owner for the shop's resolved DATA symbol (variable-length `ITEM_NONE`-terminated `u16` lists reached via scattered hensei/worldmap/event-cond pointers). The owner is resolved by `--symbol=<name>` directly, or by `--shop-addr=<ROM offset>` mapped to a list symbol via the project `.map`/`.elf`/`.sym` + the manifest list-owner (strict exact-or-span-covering match). The whole `{…}` body is re-serialized to the requested vector + a fresh terminator. **Symbolic `ITEM_*` lists (#1354):** when the existing source list uses `ITEM_*` macro names (the canonical FE8U `worldmap_shop_data.c` item-id-only form, e.g. `{ ITEM_SWORD_IRON, ITEM_NONE, }`), the writer re-serializes it SYMBOLICALLY — resolving id↔macro from the constants header (an **enum** or `#define` table, typically `include/constants/items.h`). Discovery precedence: the owner's `constantsHeader` (project-relative), then the manifest top-level `artifacts.itemConstants`, then the conventional default `include/constants/items.h`; an EXPLICIT path that is absolute / escapes the root / missing / unparseable refuses (it does NOT fall back to the default — wrong-universe danger). Symbolic lists are **item-id-only**: each entry's quantity must be `0` (a non-zero quantity is an actionable refusal — keep quantity 0 or migrate to a raw-hex list), and an id with no `ITEM_*` constant is refused. A plain-hex list still re-serializes to a raw-hex vector + a `0x0000` terminator; a list containing an UNKNOWN or AMBIGUOUS macro element is REFUSED (no-clobber — export to raw hex or edit by hand). With no decomp symbol AND no manifest list-owner, this degrades to the `--export-asset --kind=shop` migration export (#1149). **Support data** (`support_units`, `support_attributes`, `support_talks`) **is source-writable** when the manifest `tables[]` section declares a source owner for those tables (#1149); use byte-offset field names (`b0`..`b23` / `b0`..`b31` / `b0`..`b7` / `b0`+`w4`+…) in `--field`. **Positional-initializer constraint:** for a source that uses positional `{ … }` C initializers, the writer maps a byte-offset field name to a positional index by the ORDER of the owner's `fields[]` array — so a field's declared index must equal its real token position. Editing only a **leading prefix** (`b0`, then `b0`+`b1`, …) is always safe with a minimal `fields[]` that lists just those leading fields in order. Editing a **non-leading** field positionally (e.g. `b7` or `w4`) requires the owner declare the full ordered prefix up to that index (e.g. `b0`..`b7`) — OR use designated `.bN = …` initializers, which are matched by name and need no ordered list. Declaring the full ordered struct layout (e.g. all of `b0`..`b23` for `support_units`) is the safest, easiest guarantee but is not strictly required when you only edit a leading prefix. On success the project is flagged **needs rebuild**. `--out-diff=<path>`
  optionally writes a before/after diff of the changed element. Exit codes: `0` = source
  rewritten; `2` = ROM-only / manual / not owned / unsupported field / path rejected;
  `1` = usage / parse error / source not found.
- **Avalonia GUI:** in decomp mode the **Items**, **Units**, **Classes**, **Map Settings
  (chapter settings, #1148)**, **Support Unit** (FE7/8 + FE6), **Support Attribute**, and
  **Support Talk** (FE8/FE7/FE6, #1149) editor Write buttons route to the source writer when the
  matching table is source-owned (showing e.g. *"Support unit source updated. Project needs rebuild."*)
  instead of mutating the preview ROM; an owned-but-unsupported / ROM-only entry shows a ROM-only notice
  instead of a silent ROM write. The **Support Unit Editor** (FE7/FE8) ROM-save path now ports WinForms
  `SupportUnitForm.AutoCollect` (#1455): an **"Auto-adjust partner values"** checkbox (default on, matching
  WinForms) makes Write mirror each edited partner's initial value / growth rate into that partner's
  *reciprocal* support slot and recompute the partner count (B21), so both sides of a support pair stay in
  sync (the FELint reciprocity check). All reciprocal writes share the editor's undo scope (one undo step).
  The checkbox is disabled in decomp mode (reciprocal mirroring is a ROM-byte mutation — edit the source and
  rebuild). **Shop editors** (Item Shop Viewer): in decomp mode all three
  mutating operations (Write, Append Slot, Remove Last Slot) now **route to the owning decomp source
  list** when the selected shop's ROM address resolves to a manifest `u16-list` owner (symbol-resolved
  via the project `.map`/`.elf`/`.sym`) — covering **both** literal raw-hex lists **and** resolvable
  symbolic `ITEM_*` item-id-only lists (#1354) — showing *"Wrote shop list
  to source. Rebuild to refresh the preview."* and never touching the preview ROM (#1347 Slice 5a).
  When the shop is unresolved/unowned, the requested write needs a nonzero quantity on a symbolic list,
  or the source list contains an **unknown/ambiguous macro** element,
  the editor keeps the #1149 ROM-only/manual notice (no ROM write, no clobber) and the carried reason
  is shown — migrate via `--export-asset --kind=shop`. **#1148 pointer-edit guard:** when the user edits ONLY an
  unsupported chapter pointer field (e.g. EventDataPtr / a difficulty pointer), the gate shows an
  explicit ROM-only/manual notice and does NOT mutate the preview ROM (rather than a misleading
  "no change needed"). **#1382 Import Map (CSV):** the Visual Map Editor toolbar now has an
  *"Import Map (CSV)"* button that round-trips the *"Export Map (CSV)"* format. The CSV header
  (`# FEBuilderGBA Map Export: width=N, height=M`) and row-major decimal u16 MAR grid are parsed,
  validated (strict W×H match required — resize is not supported; select a map of matching dimensions
  or edit the CSV), and applied under a single LZ77 compress + write + repoint undo scope, mirroring
  the existing tile-paint write path. Blocked in decomp mode (same guard as tile writes).
  **#1387 Tiled (.tmx) import/export:** the Visual Map Editor toolbar adds *"Export Map (Tiled .tmx)"*
  and *"Import Map (Tiled .tmx)"* buttons so maps can be authored in [Tiled](https://www.mapeditor.org/).
  **Import** parses a `.tmx` tile layer in any common encoding — plain CSV, the default `<tile gid=".."/>`
  XML, Base64, Base64+gzip and Base64+zlib — and applies it through the exact same `ApplyMapGrid`
  path as the CSV import (one undo scope, strict W×H match, blocked in decomp mode). **Export** emits a
  three-file Tiled project from one save dialog: `foo.tmx` (default `<tile gid>` XML layer), `foo.tsx`
  (32-column, 16×16 chipset tileset) and `foo.png` (the chipset image, rendered by the same path as the
  live tile palette so Tiled matches the in-game render). **GID ↔ MAR convention:** `gid - 1 ==
  chipsetIndex == MAR >> 2` on a 32-column chipset grid (`firstgid = 1`); per-tile flip/rotation flags
  are masked off on import (GBA chipset indices carry no flip). Empty cells (`gid 0`) normalize to
  chipset 0, so textual GID equality is not claimed for empty tiles — round-trip equality is on the
  decoded MAR grid. The three-file export requires a local file path (desktop); it fails cleanly with a
  message on sandboxed storage providers (import still works via the stream API). The parse/emit +
  encoding decoders live in pure Core `MapTmxCore` (mirrors `MapExportCsv`). **#1148 map-asset guard:** the raw map ASSET editors (Visual Map Editor tile
  write, Map Style OBJ/palette/chipset import + write, Event Map Change write/import/expand) surface
  an export-only/manual notice in decomp mode instead of silently writing the build-preview ROM —
  migrate those via the asset-export pipeline. The toolbar badge gains a *" · needs rebuild"* suffix
  after a source write. The classic (non-decomp) ROM write path is byte-for-byte unchanged. The
  Avalonia editors map their UI fields to conventional decomp C names — units: `hp`/`pow`/`skl`/`spd`/
  `def`/`res`/`lck`/`con` (signed base stats), `level`, `affinity`, `growthHp`/`growthPow`/… ; classes:
  `baseHp`/`baseStr`(+`basePow`)/…, `maxHp`/…, `classPower`, `growthHp`/…, and signed
  `promoHp`/`promoStr`/… ; map_settings: `Weather`/`FogLevel`/`PLISTObj`/`PLISTMap`/…/`BGM1`/…/
  `ChapterNumber`/`TextGoal`/`EscapeMarkerX`/… (pointers excluded). The View intersects these with
  the manifest owner's declared `fields`, so a manifest that omits a field simply skips it.
- **Avalonia "UPS Apply" dialog** (`ToolUPSOpenSimpleView`, #1460) — now functional (was a dead
  address-label stub). Mirrors WinForms `ToolUPSOpenSimpleForm`: pick a distributed `.ups` patch and a
  clean (unmodified) original ROM — the original is **auto-detected by the UPS's recorded source CRC32**
  (`UPSUtilCore.GetUPSSrcCRC32` + `ToolTranslateROMCore.FindOrignalROMByCRC32` over the UPS dir, the app
  base directory and the loaded ROM's directory) — then **Apply** validates the original is an official
  clean ROM (CRC32 vs `ToolTranslateROMCore.GetROMBaseTable`, mirroring WinForms `CheckOrignalROM`) and
  applies the patch via the same Core `UPSUtilCore.ApplyUPS` pipeline used by CLI `--applyups` and the
  main-window `.ups` drag-drop. The patched ROM is then loaded into the main window (optionally saved as
  `.gba`); a non-fatal CRC warning prompts before committing, matching WinForms.
- **CLI:** `--decomp-audit [--format=tsv|md] [--out=path]` prints the **round-trip
  coverage matrix** below (no ROM). `--nmm-to-manifest --in=x.nmm [--table=name]` parses a
  No$gba memory map into a manifest `tables[]` entry JSON — a **schema aid, not a
  writability path**: pointer / var-length / odd-size fields survive flagged
  `"unsupported": true` (never dropped), and unsupported-field warnings go to stderr
  (exit 2 when the NMM header is unusable). `--manifest-to-nmm --project=<dir>
  --table=<name>` emits `.nmm` text for a manifest table owner, flagging pointer/var
  fields as unsafe via stderr warnings. `--validate-asset --kind=<graphics|palette|
  portrait|icon|map> --in=<srcAsset>` is a **structural + index-level** import validator
  (NEVER reads the ROM): an indexed PNG is checked for color type 3, tile alignment,
  palette size, and in-range pixel indices; a JASC `.pal` for its header/count/color
  triples; a `.mar` against its `.mar.json` sidecar (length == w*h*2 and the `<<3`
  low-3-bits-zero invariant). It exits `0` on no errors (warnings allowed), `2` on errors.
  `--validate-asset --kind=portrait-package --path=<dir> [--allow-main-only]
  [--project=<dir>]` is the **multi-file portrait PACKAGE validator** (also ROM-free): it
  requires exactly one composite sheet PNG in the directory, reuses the single-PNG
  structural checks, then verifies the 128×112 slot geometry (mini/eye/mouth slots fit;
  a 96×80 main-mug-only sheet is `INCOMPLETE_PACKAGE` unless `--allow-main-only`), the
  4bpp (≤16-color) portrait palette cap, and **palette consistency** between the sheet's
  embedded PLTE and an optional JASC `.pal` sidecar (count + per-entry RGB). `--project`
  confines `--path` to the decomp project root without loading the preview ROM. Honest
  residuals: the NMM bridge informs the schema but does not make pointer fields
  source-writable; the single-PNG validator is structural + index-level; the portrait
  PACKAGE validator covers sheet-slot geometry + palette consistency; the `.mar` check
  needs the sidecar for the exact length assertion. **Portrait package write-back +
  round-trip** (#1374): `--import-asset --kind=portrait-package --path=<srcDir>
  --out=<destDir> [--allow-main-only] [--overwrite] [--project=<dir>]` validates the source
  package (refusing on any error) then identity-copies the sheet PNG + name-matched JASC
  sidecar into an **unambiguous owner** directory — a clean/empty destination, or (with
  `--overwrite`) an existing single-package owner; a multi-PNG / different-layout destination
  is refused (`AMBIGUOUS_OWNER`). `--roundtrip-asset --kind=portrait-package --path=<srcDir>
  --expect=<baselineDir> [--allow-main-only]` validates both sides and proves the source is
  **byte-identical to the required baseline** (the oracle — NOT a self-compare, so a
  validation-valid tamper genuinely mismatches). Both paths are ROM-free and never mutate the
  preview ROM. Residual: no ROM byte-pin (there is no canonical ROM→128×112-sheet builder, so
  the preview ROM is never the source of truth for a portrait package).

#### Round-trip coverage matrix

The decomp round-trip coverage matrix below is generated by
`--decomp-audit --format=md` (single source of truth:
`DecompRoundTripAuditCore.BuildMatrix()`; a cross-project test asserts this block stays
byte-identical). It maps each editor/action to how its edit migrates back to source —
**SourceBackedWriter** (in-place C/JSON row rewrite), **SourceTreeExporter** (export an
asset + rebuild), **ImportPreviewOnly** (view only), **ManualMigration** (hand-edit
required for variable-length / pointer / raw-binary data), **RomOnlyUnsupported**.

<!-- decomp-audit-matrix:start -->

| Editor | Table | Action | Coverage | Notes |
| --- | --- | --- | --- | --- |
| Item Editor | items | Row save | SourceBackedWriter | Main structured-row save only |
| Unit Editor | units | Row save | SourceBackedWriter | Main structured-row save only (manifest alias: characters) |
| Class Editor | classes | Row save | SourceBackedWriter | Main structured-row save only |
| Map Settings Editor | map_settings | Row save | SourceBackedWriter | Main structured-row save only |
| Support Unit Editor | support_units | Row save | SourceBackedWriter | Main structured-row save only |
| Support Attribute Editor | support_attributes | Row save | SourceBackedWriter | Main structured-row save only |
| Support Talk Editor | support_talks | Row save | SourceBackedWriter | Main structured-row save only |
| Map Settings Editor | map_settings | Chapter pointer fields (EventDataPtr, difficulty) | ManualMigration | Pointer fields (D0/EventDataPtr, D96-D108 difficulty) are not source-backed |
| Palette Editor | palette | Palette export | SourceTreeExporter | JASC .pal export (faithful, lossless round-trip) |
| Graphics Editor | graphics | Graphics export | SourceTreeExporter | Indexed PNG (color type 3) + sidecar .pal |
| Portrait Editor | portrait | Portrait export | SourceTreeExporter | Export via --export-portrait-all (PNG package) |
| Portrait Editor | portrait_package | Portrait package import/round-trip | SourceTreeExporter | Source-tree write-back of a validated 128x112 composite sheet + name-matched JASC sidecar — import (--import-asset --kind=portrait-package, identity copy into an unambiguous project-confined owner; --overwrite an existing owner, ambiguous/multi-PNG destinations refused) + structural round-trip against an explicit baseline (--roundtrip-asset --kind=portrait-package --expect=<baselineDir>); reuses the #1350/#1353 portrait-package validator; never mutates the preview ROM. Source-level structure-exact identity vs a supplied baseline; NO ROM byte-pin (no canonical ROM->128x112-sheet builder exists, so the preview ROM is never the source of truth) |
| Icon Editor | icon | Icon export | SourceTreeExporter | Indexed PNG via graphics exporter (16x16 tiles) |
| Map Editor | map | Map layout export | SourceTreeExporter | .mar tilemap + sidecar .mar.json — export AND re-import/verify (lossless u16 layout body for raw entries < 0x2000, i.e. palette/flag bits 13-15 clear); compressed container re-derived by the build, not byte-pinned |
| Map Editor | map | Map layout import/verify | SourceTreeExporter | Re-import .mar to raw uncompressed tilemap blob + roundtrip-verify; never mutates the preview ROM |
| Map Editor | map_change_overlay | Map-change overlay import/verify | SourceTreeExporter | Raw uncompressed u16 overlay tile data block — export (--export-asset --kind=mapchange) + import (--import-asset) + byte-exact ROM verify (--verify-asset --kind=mapchange) + structural roundtrip; never mutates the preview ROM. Source-level structure-exact identity AND byte-exact ROM compare; NOT the .mar layout and NOT the 12-byte change-record chain |
| Map Editor | map_tileanime2_palette | Map tile-animation-2 palette import/verify | SourceTreeExporter | Raw uncompressed u16 palette data block (count*2 bytes reached by each anime-2 entry's +0 pointer) — export (--export-asset --kind=mapanime2pal) + import (--import-asset) + byte-exact ROM verify (--verify-asset --kind=mapanime2pal) + structural roundtrip; never mutates the preview ROM. Source-level structure-exact identity AND byte-exact ROM compare; NOT the anime2 entry/PLIST table and NOT LZ77 |
| Map Editor | map_obj_tileset | OBJ tileset import/verify | SourceTreeExporter | LZ77 OBJ tile block — export the DECOMPRESSED 4bpp payload (--export-asset --kind=objtiles) + import (--import-asset) + read-only decompress-and-byte-compare ROM verify (--verify-asset --kind=objtiles) + structural roundtrip; never mutates the preview ROM. Decompressed-payload equivalence, NOT compressed-stream byte identity (FEBuilder's LZ77 packer is non-canonical so the build re-compresses); --addr is the DEREFERENCED OBJ LZ77 stream address (FE7 obj2 secondary tileset is a separate stream/address, never concatenated). NOT chipset TSA/config, NOT tile animations 1/2 |
| Map Editor | map_chipset_config | Chipset TSA/config import/verify | SourceTreeExporter | LZ77 chipset TSA/config block — export the DECOMPRESSED config payload (--export-asset --kind=mapchipconfig) + import (--import-asset) + read-only decompress-and-byte-compare ROM verify (--verify-asset --kind=mapchipconfig) + structural roundtrip; never mutates the preview ROM. Decompressed-payload equivalence, NOT compressed-stream byte identity (FEBuilder's LZ77 packer is non-canonical so the build re-compresses); --addr is the DEREFERENCED config LZ77 stream address (e.g. the CONFIG-PLIST pointer dereferenced, NOT RomInfo.map_config_pointer; FE7 split layouts use a separate per-plist --addr). NOT the anime-1/anime-2 entry tables, NOT the map-change record chain, NOT the .mar layout |
| Map Editor | map_tileanime1_graphics | Map tile-animation-1 graphics import/verify | SourceTreeExporter | Raw uncompressed 4bpp graphics data block (the entry +2 byte length, reached by each anime-1 entry's +4 pointer — the inverse of anime-2's +0) — export (--export-asset --kind=mapanime1gfx --addr=<deref +4> --length=<+2>) + import (--import-asset) + byte-exact ROM verify (--verify-asset --kind=mapanime1gfx) + structural roundtrip; never mutates the preview ROM. Source-level structure-exact identity AND byte-exact RAW ROM compare; the WF read/import/rebuild paths treat this block as raw ImageToByte16Tile 4bpp bytes (a rebuild IMG block), NOT LZ77 (so NOT the objtiles/mapchipconfig decompress pattern). NOT the anime-1 ENTRY/PLIST table and NOT the .mar layout |
| Text Editor | text | Text export | SourceTreeExporter | texts.txt + textdefs.txt (migration format, not lossless macro round-trip) |
| Item Shop Editor | shops | Shop list save | ManualMigration | Decomp-mode GUI save now routes to SOURCE when the shop's ROM address resolves to a manifest u16-list owner (symbol-resolved) for BOTH literal raw-hex lists AND resolvable symbolic ITEM_* item-id-only lists (#1354) (#1347 Slice 5a); otherwise ROM-only/manual (variable-length ITEM_NONE-terminated lists via scattered hensei/worldmap/event-cond pointers; nonzero-quantity symbolic writes, unknown/ambiguous macros, and unresolved/unnamed shops degrade to --export-asset --kind=shop) |
| Item Shop Editor | shops | Shop list export | SourceTreeExporter | EA .event migration artifact via --export-asset --kind=shop; recreates each u16 ITEM_NONE-terminated list at its source address (migration aid, not source-backed in-place editing, not a byte-pinned round-trip) |
| Item Shop Editor | shops | Shop list source save | SourceBackedWriter | In-place source-backed rewrite of a u16 ITEM_NONE-terminated list (manifest list-owner: format=u16-list, symbol-resolved) via --write-shop; requires decomp-mode .map/.elf carrying the list symbol AND a manifest list-owner; degrades to --export-asset --kind=shop otherwise (#1347). Supports BOTH a LITERAL raw-hex list AND a SYMBOLIC ITEM_* (item-id-only, quantity 0) list whose macro names resolve from the constants header (owner.constantsHeader / artifacts.itemConstants / include/constants/items.h); a non-zero quantity or an id with no ITEM_* constant is an actionable refusal, not a clobber (#1354) |
| Map Editor | map_asset_binaries | Raw map asset save (GUI: anim/map-change record chain) | ManualMigration | GUI raw-ROM-save path for the remaining POINTER-HEAVY map STRUCTURAL tables: the tile-animation-1 ENTRY/PLIST table (8-byte entry rows + wait/length metadata; the per-entry raw 4bpp GRAPHICS block via the +4 pointer is now source-backed export/import/verify above), the anime-2 ENTRY/PLIST table (8-byte entry rows + wait/count/startIndex metadata; the per-entry raw PALETTE block via the +0 pointer is source-backed above) AND the 12-byte map-change RECORD chain (0xFF-terminated, width/height/flagID/PLIST metadata; the per-record raw overlay block via the +8 pointer is source-backed above) — NOT the map-change overlay tile data block NOR the anime-2 PALETTE block NOR the anime-1 GRAPHICS block NOR the OBJ tileset NOR the chipset TSA/config (all five source-backed export/import/verify above) and NOT the .mar tile layout. These ENTRY/PLIST/RECORD tables stay guarded (pointer-per-row/record, ambiguous source ownership — they need a manifest source owner not yet defined; sub-issues #1389/#1390/#1391 track each); migrate the dereferenced data blocks via --export-asset |
| Event Editor | chapter_event_pointers | Event/difficulty pointer fields | ManualMigration | Chapter pointer fields (EventDataPtr, difficulty pointers) are not source-backed |
| Battle Animation Editor | battle_anime | Animation view | ImportPreviewOnly | Preview-only in decomp mode; no source write-back (export via --export-battle-anime) |
| Song Table Editor | song_table | Song view | ImportPreviewOnly | Preview-only; song data edits must be made in source by hand |
| Magic Editor | magic_effects | Magic view | ImportPreviewOnly | Preview-only; magic effect edits are not source-backed yet |
| Hex Editor | raw_rom | Raw byte edit | RomOnlyUnsupported | Arbitrary ROM bytes; not representable as a clean source edit |
| Patch Manager | patches | Patch install/uninstall | RomOnlyUnsupported | ASM/binary patches apply to the built ROM; not a decomp source migration |

<!-- decomp-audit-matrix:end -->

Slice 1 (#1129) delivered open + preview; slice 2 (#1130) adds address-to-source
symbol resolution; slice 3 (#1131) adds the diff-to-source migration assistant;
the source-backed table writer is #1132, extended to JSON + units/classes + signed
fields + multi-field in #1141 (full-document JSON validation + width-aware signed no-op
hardening in #1145). Asset exporters (#1133) and in-app build/reload (#1134)
round out the suite.

`--decomp-audit --summary` prints the per-tier coverage counts (with an explicit
`Unclassified = N` line and the size of the maintained editor inventory). The matrix is
**complete relative to the maintained audit inventory** — a maintained classification, not
exhaustive byte-level runtime round-trip proof; full byte-level round-trip editing of every
format remains partial by design (#1150).

> **Decomp feature inventory & release status:** the full decomp project-mode feature set —
> and the PRs/commits that landed each slice — is enumerated in
> [docs/DECOMP-FEATURE-INVENTORY.md](docs/DECOMP-FEATURE-INVENTORY.md). These features
> currently live on **`master`, ahead of any tagged release** (the latest tag is
> `ver_20260204.22`; all decomp work landed after it), so they are not part of an existing
> released build yet. Build from `master` to use them.

### Running on Android (experimental)

Two distinct paths exist, both covered in detail in [docs/CROSS_PLATFORM.md → Running on Android](docs/CROSS_PLATFORM.md#running-on-android):

- **Emulation (Gamenative/Winlator)** — run the Windows *desktop* build under Wine + Box86/Box64. User-side, **experimental / unsupported / community-tested**; try the Avalonia `win-x64` build first.
- **Native Android app** — a separate port of the Avalonia GUI, tracked as exploration epic [#1070](https://github.com/laqieer/FEBuilderGBA/issues/1070). Not shipped. See the evidence-backed feasibility assessment in [docs/ANDROID.md](docs/ANDROID.md) (Avalonia.Android lifetime, SkiaSharp native pin, SAF ROM access, `config/` bundling, the multi-window→single-activity gap) and the authored head skeleton in [`FEBuilderGBA.Android/`](FEBuilderGBA.Android/README.md).
  - **Stream-based ROM I/O for SAF (#1124)** — `ROM.LoadFromStream`/`SaveToStream` (+ async) share the byte-level seam with the existing path `Load`/`Save`, so the Avalonia head can open/save a ROM picked via `IStorageProvider` even when the SAF `content://` handle has no local filesystem path (it retains the `IStorageFile` and reads/writes through `OpenReadAsync`/`OpenWriteAsync`). Desktop path I/O is unchanged. The auto-save sidecar is redirected into app-private `{BaseDirectory}/autosave/` on Android (where the ROM's parent dir is not writable); the log and `config.xml` already resolve under `BaseDirectory` (`Context.FilesDir` on Android via #1123).
  - **Single-activity navigation model (#1122)** — the desktop multi-window editor model is reworked behind an `INavigationService` abstraction so the same `FEBuilderGBA.Avalonia/Services/WindowManager` API drives two backends. Desktop uses `DesktopNavigationService` (the original `.Show()`/`.ShowDialog()` multi-window behavior, **verbatim** — regression-safe), while Android uses `AndroidNavigationService`, a **single-view page/view-stack host with a back stack** built on a pure, desktop-unit-tested `NavigationStack` (modal-as-page, `PickFromEditor` result-await). `App` sets a `Views/MainView` shell under `ISingleViewApplicationLifetime` so the booted Android app presents the editor launcher. **Build-only validated** (no device): the nav-stack core is unit-tested, desktop nav is regression-verified behavior-identical, and the Android head builds to a `MainView`; the on-device runtime UX (touch + per-editor attached-`Window` dialogs/file pickers) is tracked under #1070.

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
│   ├── SongExchangeCore.cs                Cross-ROM song transplant (InstrumentMap/Rip/Burn + sample recycle)
│   ├── MapConvertCore.cs                  Map tile conversion
│   ├── NameResolver.cs                    Entity name resolution with caching
│   ├── SongNameResolverCore.cs            Song name resolution (Sound Room name + SE-list fallback)
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

### FE8 Skill Systems

Several FE8 editors — **Spell Menu Extensions**, the **Skill** editors, and **Effectiveness (Skill Systems Rework)** — only show data once a community **Skill System** is installed on the ROM. Recommended sources:

- **FE8U** (US/International): [FireEmblemUniverse/SkillSystem_FE8](https://github.com/FireEmblemUniverse/SkillSystem_FE8) (the canonical Event Assembler buildfile) or [MokhaLeee/fe8u-cskillsys-kernel](https://github.com/MokhaLeee/fe8u-cskillsys-kernel) (a modern C kernel).
- **FE8J** (Japan): [ngmansion/FE8N](https://github.com/ngmansion/FE8N) (the de facto standard FE8J hacking base).

These projects distribute **patches / source**, not the game — apply them to a clean FE8 ROM you dumped yourself. See the [Skill Systems (FE8) wiki page](https://github.com/laqieer/FEBuilderGBA/wiki/Skill-Systems) for installation details.

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
