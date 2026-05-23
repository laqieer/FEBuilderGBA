---
generated: "2026-05-23T02:16:17Z"
git-sha: 424bc5722
sweep-type: undo
---

# Avalonia vs WinForms — Undo Coverage Sweep

This report inventories every ROM-write callsite in
`FEBuilderGBA.Avalonia/` and classifies its undo coverage. WinForms is
the ground truth — every WF call to `Program.ROM.SetU8/16/32(addr, val,
undo)` takes an `Undo` argument so the compiler enforces undo plumbing
at every callsite. Avalonia uses two complementary patterns:

1. `UndoService.Begin(name)` opens a scope `rom.write_u*` calls register
   against automatically — used VM-internally by `EventScriptPopupViewModel`.
2. The View code-behind wraps a `_vm.WriteX()` invocation in
   `_undoService.Begin/Commit/Rollback` so every write inside the VM's
   `WriteX` method executes under the View's ambient scope — used by
   ~30 editor Views (ItemEditorView, MapSettingView, ClassEditorView,
   UnitEditorView, etc).

The Phase 5 scanner now models both patterns. Pass 1 does same-method
bracketing inside each file; pass 2 cross-references View files for
`_vm.Method(...)` invocations wrapped in Begin/Commit and upgrades the
matching VM-side write rows to Covered.

**Methodology:**

- Roslyn scans every `.cs` file under `FEBuilderGBA.Avalonia/`,
  excluding `GapSweep/`, `obj/`, and `bin/`.
- Each `InvocationExpressionSyntax` whose method name is in
  {`write_u8`, `write_u16`, `write_u32`, `write_p32`, `write_range`,
  `write_fill`, `write_resize_data`, `SetU8`, `SetU16`, `SetU32`,
  `SetData`} AND whose receiver resolves to a ROM reference
  (`rom`, `ROM`, `Program.ROM`, or `CoreState.ROM`) is captured as a
  write callsite.
- Pass 2 cross-references the View files for `_vm.Method(...)` calls
  wrapped in `_undoService.Begin/Commit`. Any VM write whose enclosing
  method matches such a call is upgraded from `MissingScope`/
  `NoUndoServiceField` to `Covered`. The pairing convention is
  `XEditorView` ↔ `XEditorViewModel`.
- `EditorFormRef.WriteFields(rom, addr, values, fields)` and the
  singular `EditorFormRef.WriteField(...)` are also captured — the
  bulk-write helper through which most AV ViewModels funnel their
  writes. One report row per WriteFields call regardless of how
  many actual bytes the helper writes internally.
- For each callsite we find the enclosing class and method, then
  classify by walking the method body for `Begin`/`Commit`/`Rollback`
  invocations on any field/property/local of declared type
  `UndoService`.
- UndoService receiver discovery is type-driven (not name-driven):
  every field, property, or local-variable declaration whose Type
  identifier is `UndoService` is recognised, regardless of the
  identifier's name (so `_undo`, `_undoService`, `Tracker` all work).
- View→VM receiver discovery in Pass 2 is also type-driven: every
  identifier on a View class declared with a `*ViewModel`-shaped
  type (case-insensitive trailing-identifier match) is recognised.
  No semantic model — see the AmbiguousScope tier for the
  one-level helper-call disclaimer.

**Coverage tiers** (highest priority first):

- `NoUndoServiceField` — class has no UndoService field at all. The
  whole VM is unplumbed; the fix requires introducing a service field
  before any individual write can be wrapped.
- `MissingScope` — class has UndoService but THIS write is not inside a
  `Begin(...)` scope.
- `AmbiguousScope` — write lives in a helper method; the caller MAY
  wrap a scope (one-level heuristic only — verify manually).
- `Covered` — write is inside a `Begin`/`Commit` (or `Begin`/`Rollback`)
  scope in the same method, OR the write passes an explicit `Undo`
  trailing argument (WinForms-style).

Methodology lives in `FEBuilderGBA.Avalonia/GapSweep/UndoCoverageScanner.cs`.
Regenerate with `FEBuilderGBA.Avalonia --gap-sweep-undo --out=<path>`.

## Summary

| Tier | Count | % of total |
|---|---:|---:|
| Total write callsites | 1042 | 100% |
| NoUndoServiceField (no plumbing) | 199 | 19.1% |
| MissingScope (unwrapped) | 2 | 0.2% |
| AmbiguousScope (verify) | 0 | 0.0% |
| Covered (healthy) | 841 | 80.7% |

## Highest priority — VMs with NO undo plumbing at all

These ViewModels have no `UndoService` field/property/local. Every write here bypasses the undo buffer. The fix sequence is: (1) add a `UndoService _undoService = new();` field, (2) wrap each Save / Write handler in `_undoService.Begin/Commit`. Grouped by enclosing class.

### `ClassFE6ViewModel` — 51 callsites

| File | Line | Method | Write | Note |
|---|---:|---|---|---|
| `FEBuilderGBA.Avalonia/ViewModels/ClassFE6ViewModel.cs` | 448 | `WriteEntry` | `rom.write_u16(addr + 0, NameId)` | class 'ClassFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ClassFE6ViewModel.cs` | 449 | `WriteEntry` | `rom.write_u16(addr + 2, DescId)` | class 'ClassFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ClassFE6ViewModel.cs` | 452 | `WriteEntry` | `rom.write_u8(addr + 4, ClassId)` | class 'ClassFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ClassFE6ViewModel.cs` | 453 | `WriteEntry` | `rom.write_u8(addr + 5, PromotionLevel)` | class 'ClassFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ClassFE6ViewModel.cs` | 454 | `WriteEntry` | `rom.write_u8(addr + 6, WaitIcon)` | class 'ClassFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ClassFE6ViewModel.cs` | 455 | `WriteEntry` | `rom.write_u8(addr + 7, WalkSpeed)` | class 'ClassFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ClassFE6ViewModel.cs` | 456 | `WriteEntry` | `rom.write_u16(addr + 8, PortraitId)` | class 'ClassFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ClassFE6ViewModel.cs` | 457 | `WriteEntry` | `rom.write_u8(addr + 10, SortOrder)` | class 'ClassFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ClassFE6ViewModel.cs` | 460 | `WriteEntry` | `rom.write_u8(addr + 11, BaseHp)` | class 'ClassFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ClassFE6ViewModel.cs` | 461 | `WriteEntry` | `rom.write_u8(addr + 12, BaseStr)` | class 'ClassFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ClassFE6ViewModel.cs` | 462 | `WriteEntry` | `rom.write_u8(addr + 13, BaseSkl)` | class 'ClassFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ClassFE6ViewModel.cs` | 463 | `WriteEntry` | `rom.write_u8(addr + 14, BaseSpd)` | class 'ClassFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ClassFE6ViewModel.cs` | 464 | `WriteEntry` | `rom.write_u8(addr + 15, BaseDef)` | class 'ClassFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ClassFE6ViewModel.cs` | 465 | `WriteEntry` | `rom.write_u8(addr + 16, BaseRes)` | class 'ClassFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ClassFE6ViewModel.cs` | 466 | `WriteEntry` | `rom.write_u8(addr + 17, BaseCon)` | class 'ClassFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ClassFE6ViewModel.cs` | 467 | `WriteEntry` | `rom.write_u8(addr + 18, BaseMov)` | class 'ClassFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ClassFE6ViewModel.cs` | 470 | `WriteEntry` | `rom.write_u8(addr + 19, MaxHp)` | class 'ClassFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ClassFE6ViewModel.cs` | 471 | `WriteEntry` | `rom.write_u8(addr + 20, MaxStr)` | class 'ClassFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ClassFE6ViewModel.cs` | 472 | `WriteEntry` | `rom.write_u8(addr + 21, MaxSkl)` | class 'ClassFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ClassFE6ViewModel.cs` | 473 | `WriteEntry` | `rom.write_u8(addr + 22, MaxSpd)` | class 'ClassFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ClassFE6ViewModel.cs` | 474 | `WriteEntry` | `rom.write_u8(addr + 23, MaxDef)` | class 'ClassFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ClassFE6ViewModel.cs` | 475 | `WriteEntry` | `rom.write_u8(addr + 24, MaxRes)` | class 'ClassFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ClassFE6ViewModel.cs` | 476 | `WriteEntry` | `rom.write_u8(addr + 25, MaxCon)` | class 'ClassFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ClassFE6ViewModel.cs` | 477 | `WriteEntry` | `rom.write_u8(addr + 26, ClassPower)` | class 'ClassFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ClassFE6ViewModel.cs` | 480 | `WriteEntry` | `rom.write_u8(addr + 27, GrowHp)` | class 'ClassFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ClassFE6ViewModel.cs` | 481 | `WriteEntry` | `rom.write_u8(addr + 28, GrowStr)` | class 'ClassFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ClassFE6ViewModel.cs` | 482 | `WriteEntry` | `rom.write_u8(addr + 29, GrowSkl)` | class 'ClassFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ClassFE6ViewModel.cs` | 483 | `WriteEntry` | `rom.write_u8(addr + 30, GrowSpd)` | class 'ClassFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ClassFE6ViewModel.cs` | 484 | `WriteEntry` | `rom.write_u8(addr + 31, GrowDef)` | class 'ClassFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ClassFE6ViewModel.cs` | 485 | `WriteEntry` | `rom.write_u8(addr + 32, GrowRes)` | class 'ClassFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ClassFE6ViewModel.cs` | 486 | `WriteEntry` | `rom.write_u8(addr + 33, GrowLck)` | class 'ClassFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ClassFE6ViewModel.cs` | 489 | `WriteEntry` | `rom.write_u8(addr + 34, (uint)(byte)(sbyte)PromoHp)` | class 'ClassFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ClassFE6ViewModel.cs` | 490 | `WriteEntry` | `rom.write_u8(addr + 35, (uint)(byte)(sbyte)PromoStr)` | class 'ClassFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ClassFE6ViewModel.cs` | 493 | `WriteEntry` | `rom.write_u8(addr + 36, Ability1)` | class 'ClassFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ClassFE6ViewModel.cs` | 494 | `WriteEntry` | `rom.write_u8(addr + 37, Ability2)` | class 'ClassFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ClassFE6ViewModel.cs` | 495 | `WriteEntry` | `rom.write_u8(addr + 38, Ability3)` | class 'ClassFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ClassFE6ViewModel.cs` | 496 | `WriteEntry` | `rom.write_u8(addr + 39, Ability4)` | class 'ClassFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ClassFE6ViewModel.cs` | 499 | `WriteEntry` | `rom.write_u8(addr + 40, WepSword)` | class 'ClassFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ClassFE6ViewModel.cs` | 500 | `WriteEntry` | `rom.write_u8(addr + 41, WepLance)` | class 'ClassFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ClassFE6ViewModel.cs` | 501 | `WriteEntry` | `rom.write_u8(addr + 42, WepAxe)` | class 'ClassFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ClassFE6ViewModel.cs` | 502 | `WriteEntry` | `rom.write_u8(addr + 43, WepBow)` | class 'ClassFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ClassFE6ViewModel.cs` | 503 | `WriteEntry` | `rom.write_u8(addr + 44, WepStaff)` | class 'ClassFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ClassFE6ViewModel.cs` | 504 | `WriteEntry` | `rom.write_u8(addr + 45, WepAnima)` | class 'ClassFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ClassFE6ViewModel.cs` | 505 | `WriteEntry` | `rom.write_u8(addr + 46, WepLight)` | class 'ClassFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ClassFE6ViewModel.cs` | 506 | `WriteEntry` | `rom.write_u8(addr + 47, WepDark)` | class 'ClassFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ClassFE6ViewModel.cs` | 509 | `WriteEntry` | `rom.write_u32(addr + 48, BattleAnimePtr)` | class 'ClassFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ClassFE6ViewModel.cs` | 510 | `WriteEntry` | `rom.write_u32(addr + 52, MoveCostPtr)` | class 'ClassFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ClassFE6ViewModel.cs` | 511 | `WriteEntry` | `rom.write_u32(addr + 56, TerrainAvoidPtr)` | class 'ClassFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ClassFE6ViewModel.cs` | 512 | `WriteEntry` | `rom.write_u32(addr + 60, TerrainDefPtr)` | class 'ClassFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ClassFE6ViewModel.cs` | 513 | `WriteEntry` | `rom.write_u32(addr + 64, TerrainResPtr)` | class 'ClassFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ClassFE6ViewModel.cs` | 516 | `WriteEntry` | `rom.write_u32(addr + 68, UnknownD68)` | class 'ClassFE6ViewModel' has no UndoService field/property/local |

### `MapSettingFE6ViewModel` — 50 callsites

| File | Line | Method | Write | Note |
|---|---:|---|---|---|
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE6ViewModel.cs` | 182 | `WriteMapSetting` | `rom.write_u32(addr + 0, CpPointer)` | class 'MapSettingFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE6ViewModel.cs` | 183 | `WriteMapSetting` | `rom.write_u16(addr + 4, ObjectTypePLIST)` | class 'MapSettingFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE6ViewModel.cs` | 184 | `WriteMapSetting` | `rom.write_u8(addr + 7, ChipsetConfigPLIST)` | class 'MapSettingFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE6ViewModel.cs` | 184 | `WriteMapSetting` | `rom.write_u8(addr + 6, PalettePLIST)` | class 'MapSettingFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE6ViewModel.cs` | 185 | `WriteMapSetting` | `rom.write_u8(addr + 9, TileAnimation1PLIST)` | class 'MapSettingFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE6ViewModel.cs` | 185 | `WriteMapSetting` | `rom.write_u8(addr + 8, MapPointerPLIST)` | class 'MapSettingFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE6ViewModel.cs` | 186 | `WriteMapSetting` | `rom.write_u8(addr + 11, MapChangePLIST)` | class 'MapSettingFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE6ViewModel.cs` | 186 | `WriteMapSetting` | `rom.write_u8(addr + 10, TileAnimation2PLIST)` | class 'MapSettingFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE6ViewModel.cs` | 187 | `WriteMapSetting` | `rom.write_u8(addr + 13, BattlePreparation)` | class 'MapSettingFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE6ViewModel.cs` | 187 | `WriteMapSetting` | `rom.write_u8(addr + 12, FogLevel)` | class 'MapSettingFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE6ViewModel.cs` | 188 | `WriteMapSetting` | `rom.write_u8(addr + 15, UnknownB15)` | class 'MapSettingFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE6ViewModel.cs` | 188 | `WriteMapSetting` | `rom.write_u8(addr + 14, ChapterTitleImage)` | class 'MapSettingFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE6ViewModel.cs` | 189 | `WriteMapSetting` | `rom.write_u8(addr + 17, UnknownB17)` | class 'MapSettingFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE6ViewModel.cs` | 189 | `WriteMapSetting` | `rom.write_u8(addr + 16, UnknownB16)` | class 'MapSettingFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE6ViewModel.cs` | 190 | `WriteMapSetting` | `rom.write_u8(addr + 19, BattleBGLookup)` | class 'MapSettingFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE6ViewModel.cs` | 190 | `WriteMapSetting` | `rom.write_u8(addr + 18, Weather)` | class 'MapSettingFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE6ViewModel.cs` | 191 | `WriteMapSetting` | `rom.write_u8(addr + 21, EnemyPhaseBGM)` | class 'MapSettingFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE6ViewModel.cs` | 191 | `WriteMapSetting` | `rom.write_u8(addr + 20, PlayerPhaseBGM)` | class 'MapSettingFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE6ViewModel.cs` | 192 | `WriteMapSetting` | `rom.write_u8(addr + 23, HardBoost)` | class 'MapSettingFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE6ViewModel.cs` | 192 | `WriteMapSetting` | `rom.write_u8(addr + 22, NpcPhaseBGM)` | class 'MapSettingFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE6ViewModel.cs` | 193 | `WriteMapSetting` | `rom.write_u8(addr + 25, BreakableWallHP)` | class 'MapSettingFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE6ViewModel.cs` | 193 | `WriteMapSetting` | `rom.write_u8(addr + 24, UnknownB24)` | class 'MapSettingFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE6ViewModel.cs` | 194 | `WriteMapSetting` | `rom.write_u8(addr + 27, UnknownB27)` | class 'MapSettingFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE6ViewModel.cs` | 194 | `WriteMapSetting` | `rom.write_u8(addr + 26, UnknownB26)` | class 'MapSettingFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE6ViewModel.cs` | 195 | `WriteMapSetting` | `rom.write_u8(addr + 29, UnknownB29)` | class 'MapSettingFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE6ViewModel.cs` | 195 | `WriteMapSetting` | `rom.write_u8(addr + 28, UnknownB28)` | class 'MapSettingFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE6ViewModel.cs` | 196 | `WriteMapSetting` | `rom.write_u8(addr + 31, UnknownB31)` | class 'MapSettingFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE6ViewModel.cs` | 196 | `WriteMapSetting` | `rom.write_u8(addr + 30, UnknownB30)` | class 'MapSettingFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE6ViewModel.cs` | 197 | `WriteMapSetting` | `rom.write_u16(addr + 34, EnemyPhaseBGMW)` | class 'MapSettingFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE6ViewModel.cs` | 197 | `WriteMapSetting` | `rom.write_u16(addr + 32, PlayerPhaseBGMW)` | class 'MapSettingFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE6ViewModel.cs` | 198 | `WriteMapSetting` | `rom.write_u16(addr + 38, UnknownW38)` | class 'MapSettingFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE6ViewModel.cs` | 198 | `WriteMapSetting` | `rom.write_u16(addr + 36, NpcPhaseBGMW)` | class 'MapSettingFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE6ViewModel.cs` | 199 | `WriteMapSetting` | `rom.write_u16(addr + 42, UnknownW42)` | class 'MapSettingFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE6ViewModel.cs` | 199 | `WriteMapSetting` | `rom.write_u16(addr + 40, UnknownW40)` | class 'MapSettingFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE6ViewModel.cs` | 200 | `WriteMapSetting` | `rom.write_u16(addr + 46, UnknownW46)` | class 'MapSettingFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE6ViewModel.cs` | 200 | `WriteMapSetting` | `rom.write_u16(addr + 44, UnknownW44)` | class 'MapSettingFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE6ViewModel.cs` | 201 | `WriteMapSetting` | `rom.write_u16(addr + 50, UpperArmyText)` | class 'MapSettingFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE6ViewModel.cs` | 201 | `WriteMapSetting` | `rom.write_u16(addr + 48, ClearConditionText)` | class 'MapSettingFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE6ViewModel.cs` | 202 | `WriteMapSetting` | `rom.write_u16(addr + 54, EnemyBannerFlag)` | class 'MapSettingFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE6ViewModel.cs` | 202 | `WriteMapSetting` | `rom.write_u16(addr + 52, LowerArmyText)` | class 'MapSettingFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE6ViewModel.cs` | 203 | `WriteMapSetting` | `rom.write_u16(addr + 56, ChapterTitleText)` | class 'MapSettingFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE6ViewModel.cs` | 204 | `WriteMapSetting` | `rom.write_u8(addr + 58, EventIdPLIST)` | class 'MapSettingFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE6ViewModel.cs` | 205 | `WriteMapSetting` | `rom.write_u8(addr + 59, WorldMapAutoEvent)` | class 'MapSettingFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE6ViewModel.cs` | 206 | `WriteMapSetting` | `rom.write_u16(addr + 60, WorldMapPlaceName)` | class 'MapSettingFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE6ViewModel.cs` | 207 | `WriteMapSetting` | `rom.write_u8(addr + 62, ChapterNumber)` | class 'MapSettingFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE6ViewModel.cs` | 208 | `WriteMapSetting` | `rom.write_u8(addr + 63, WorldMapX)` | class 'MapSettingFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE6ViewModel.cs` | 209 | `WriteMapSetting` | `rom.write_u8(addr + 64, WorldMapY)` | class 'MapSettingFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE6ViewModel.cs` | 210 | `WriteMapSetting` | `rom.write_u8(addr + 65, WorldMapPointX)` | class 'MapSettingFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE6ViewModel.cs` | 211 | `WriteMapSetting` | `rom.write_u8(addr + 66, WorldMapPointY)` | class 'MapSettingFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE6ViewModel.cs` | 212 | `WriteMapSetting` | `rom.write_u8(addr + 67, VictoryBGMEnemyCount)` | class 'MapSettingFE6ViewModel' has no UndoService field/property/local |

### `UnitFE6ViewModel` — 42 callsites

| File | Line | Method | Write | Note |
|---|---:|---|---|---|
| `FEBuilderGBA.Avalonia/ViewModels/UnitFE6ViewModel.cs` | 393 | `WriteUnit` | `rom.write_u16(addr + 0, NameId)` | class 'UnitFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/UnitFE6ViewModel.cs` | 394 | `WriteUnit` | `rom.write_u16(addr + 2, DescId)` | class 'UnitFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/UnitFE6ViewModel.cs` | 395 | `WriteUnit` | `rom.write_u8(addr + 4, UnitId)` | class 'UnitFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/UnitFE6ViewModel.cs` | 396 | `WriteUnit` | `rom.write_u8(addr + 5, ClassId)` | class 'UnitFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/UnitFE6ViewModel.cs` | 397 | `WriteUnit` | `rom.write_u16(addr + 6, PortraitId)` | class 'UnitFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/UnitFE6ViewModel.cs` | 398 | `WriteUnit` | `rom.write_u8(addr + 8, MapFace)` | class 'UnitFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/UnitFE6ViewModel.cs` | 399 | `WriteUnit` | `rom.write_u8(addr + 9, Affinity)` | class 'UnitFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/UnitFE6ViewModel.cs` | 400 | `WriteUnit` | `rom.write_u8(addr + 10, SortOrder)` | class 'UnitFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/UnitFE6ViewModel.cs` | 401 | `WriteUnit` | `rom.write_u8(addr + 11, Level)` | class 'UnitFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/UnitFE6ViewModel.cs` | 404 | `WriteUnit` | `rom.write_u8(addr + 12, (uint)(byte)HP)` | class 'UnitFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/UnitFE6ViewModel.cs` | 405 | `WriteUnit` | `rom.write_u8(addr + 13, (uint)(byte)Str)` | class 'UnitFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/UnitFE6ViewModel.cs` | 406 | `WriteUnit` | `rom.write_u8(addr + 14, (uint)(byte)Skl)` | class 'UnitFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/UnitFE6ViewModel.cs` | 407 | `WriteUnit` | `rom.write_u8(addr + 15, (uint)(byte)Spd)` | class 'UnitFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/UnitFE6ViewModel.cs` | 408 | `WriteUnit` | `rom.write_u8(addr + 16, (uint)(byte)Def)` | class 'UnitFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/UnitFE6ViewModel.cs` | 409 | `WriteUnit` | `rom.write_u8(addr + 17, (uint)(byte)Res)` | class 'UnitFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/UnitFE6ViewModel.cs` | 410 | `WriteUnit` | `rom.write_u8(addr + 18, (uint)(byte)Lck)` | class 'UnitFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/UnitFE6ViewModel.cs` | 411 | `WriteUnit` | `rom.write_u8(addr + 19, (uint)(byte)Con)` | class 'UnitFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/UnitFE6ViewModel.cs` | 414 | `WriteUnit` | `rom.write_u8(addr + 20, WepSword)` | class 'UnitFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/UnitFE6ViewModel.cs` | 415 | `WriteUnit` | `rom.write_u8(addr + 21, WepLance)` | class 'UnitFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/UnitFE6ViewModel.cs` | 416 | `WriteUnit` | `rom.write_u8(addr + 22, WepAxe)` | class 'UnitFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/UnitFE6ViewModel.cs` | 417 | `WriteUnit` | `rom.write_u8(addr + 23, WepBow)` | class 'UnitFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/UnitFE6ViewModel.cs` | 418 | `WriteUnit` | `rom.write_u8(addr + 24, WepStaff)` | class 'UnitFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/UnitFE6ViewModel.cs` | 419 | `WriteUnit` | `rom.write_u8(addr + 25, WepAnima)` | class 'UnitFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/UnitFE6ViewModel.cs` | 420 | `WriteUnit` | `rom.write_u8(addr + 26, WepLight)` | class 'UnitFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/UnitFE6ViewModel.cs` | 421 | `WriteUnit` | `rom.write_u8(addr + 27, WepDark)` | class 'UnitFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/UnitFE6ViewModel.cs` | 424 | `WriteUnit` | `rom.write_u8(addr + 28, GrowHP)` | class 'UnitFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/UnitFE6ViewModel.cs` | 425 | `WriteUnit` | `rom.write_u8(addr + 29, GrowStr)` | class 'UnitFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/UnitFE6ViewModel.cs` | 426 | `WriteUnit` | `rom.write_u8(addr + 30, GrowSkl)` | class 'UnitFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/UnitFE6ViewModel.cs` | 427 | `WriteUnit` | `rom.write_u8(addr + 31, GrowSpd)` | class 'UnitFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/UnitFE6ViewModel.cs` | 428 | `WriteUnit` | `rom.write_u8(addr + 32, GrowDef)` | class 'UnitFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/UnitFE6ViewModel.cs` | 429 | `WriteUnit` | `rom.write_u8(addr + 33, GrowRes)` | class 'UnitFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/UnitFE6ViewModel.cs` | 430 | `WriteUnit` | `rom.write_u8(addr + 34, GrowLck)` | class 'UnitFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/UnitFE6ViewModel.cs` | 433 | `WriteUnit` | `rom.write_u8(addr + 35, Unk35)` | class 'UnitFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/UnitFE6ViewModel.cs` | 434 | `WriteUnit` | `rom.write_u8(addr + 36, Unk36)` | class 'UnitFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/UnitFE6ViewModel.cs` | 435 | `WriteUnit` | `rom.write_u8(addr + 37, Unk37)` | class 'UnitFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/UnitFE6ViewModel.cs` | 436 | `WriteUnit` | `rom.write_u8(addr + 38, Unk38)` | class 'UnitFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/UnitFE6ViewModel.cs` | 437 | `WriteUnit` | `rom.write_u8(addr + 39, Unk39)` | class 'UnitFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/UnitFE6ViewModel.cs` | 440 | `WriteUnit` | `rom.write_u8(addr + 40, Ability1)` | class 'UnitFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/UnitFE6ViewModel.cs` | 441 | `WriteUnit` | `rom.write_u8(addr + 41, Ability2)` | class 'UnitFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/UnitFE6ViewModel.cs` | 442 | `WriteUnit` | `rom.write_u8(addr + 42, Ability3)` | class 'UnitFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/UnitFE6ViewModel.cs` | 443 | `WriteUnit` | `rom.write_u8(addr + 43, Ability4)` | class 'UnitFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/UnitFE6ViewModel.cs` | 446 | `WriteUnit` | `rom.write_u32(addr + 44, SupportPtr)` | class 'UnitFE6ViewModel' has no UndoService field/property/local |

### `ImagePortraitViewModel` — 13 callsites

| File | Line | Method | Write | Note |
|---|---:|---|---|---|
| `FEBuilderGBA.Avalonia/ViewModels/ImagePortraitViewModel.cs` | 222 | `Write` | `rom.write_u32(addr + 0, PortraitImagePtr)` | class 'ImagePortraitViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ImagePortraitViewModel.cs` | 223 | `Write` | `rom.write_u32(addr + 4, MiniPortraitPtr)` | class 'ImagePortraitViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ImagePortraitViewModel.cs` | 224 | `Write` | `rom.write_u32(addr + 8, PalettePtr)` | class 'ImagePortraitViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ImagePortraitViewModel.cs` | 225 | `Write` | `rom.write_u32(addr + 12, MouthFramesPtr)` | class 'ImagePortraitViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ImagePortraitViewModel.cs` | 226 | `Write` | `rom.write_u32(addr + 16, ClassCardPtr)` | class 'ImagePortraitViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ImagePortraitViewModel.cs` | 227 | `Write` | `rom.write_u8(addr + 20, MouthX)` | class 'ImagePortraitViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ImagePortraitViewModel.cs` | 228 | `Write` | `rom.write_u8(addr + 21, MouthY)` | class 'ImagePortraitViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ImagePortraitViewModel.cs` | 229 | `Write` | `rom.write_u8(addr + 22, EyeX)` | class 'ImagePortraitViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ImagePortraitViewModel.cs` | 230 | `Write` | `rom.write_u8(addr + 23, EyeY)` | class 'ImagePortraitViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ImagePortraitViewModel.cs` | 231 | `Write` | `rom.write_u8(addr + 24, Status)` | class 'ImagePortraitViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ImagePortraitViewModel.cs` | 232 | `Write` | `rom.write_u8(addr + 25, Unused25)` | class 'ImagePortraitViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ImagePortraitViewModel.cs` | 233 | `Write` | `rom.write_u8(addr + 26, Unused26)` | class 'ImagePortraitViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ImagePortraitViewModel.cs` | 234 | `Write` | `rom.write_u8(addr + 27, Unused27)` | class 'ImagePortraitViewModel' has no UndoService field/property/local |

### `ImageCGFE7UViewModel` — 7 callsites

| File | Line | Method | Write | Note |
|---|---:|---|---|---|
| `FEBuilderGBA.Avalonia/ViewModels/ImageCGFE7UViewModel.cs` | 86 | `Write` | `rom.write_u8(addr + 0, ImageType)` | class 'ImageCGFE7UViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ImageCGFE7UViewModel.cs` | 87 | `Write` | `rom.write_u8(addr + 1, Reserved1)` | class 'ImageCGFE7UViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ImageCGFE7UViewModel.cs` | 88 | `Write` | `rom.write_u8(addr + 2, Reserved2)` | class 'ImageCGFE7UViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ImageCGFE7UViewModel.cs` | 89 | `Write` | `rom.write_u8(addr + 3, Reserved3)` | class 'ImageCGFE7UViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ImageCGFE7UViewModel.cs` | 90 | `Write` | `rom.write_u32(addr + 4, SplitImagePtr)` | class 'ImageCGFE7UViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ImageCGFE7UViewModel.cs` | 91 | `Write` | `rom.write_u32(addr + 8, TSAPtr)` | class 'ImageCGFE7UViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ImageCGFE7UViewModel.cs` | 92 | `Write` | `rom.write_u32(addr + 12, PalettePtr)` | class 'ImageCGFE7UViewModel' has no UndoService field/property/local |

### `EventForceSortieFE7ViewModel` — 2 callsites

| File | Line | Method | Write | Note |
|---|---:|---|---|---|
| `FEBuilderGBA.Avalonia/ViewModels/EventForceSortieFE7ViewModel.cs` | 97 | `Write` | `EditorFormRef.WriteFields(rom, a, values, _fields)` | class 'EventForceSortieFE7ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/EventForceSortieFE7ViewModel.cs` | 111 | `WriteSubEntry` | `EditorFormRef.WriteFields(rom, a, subValues, _subFields)` | class 'EventForceSortieFE7ViewModel' has no UndoService field/property/local |

### `ImageImportValidator` — 2 callsites

| File | Line | Method | Write | Note |
|---|---:|---|---|---|
| `FEBuilderGBA.Avalonia/Services/ImageImportValidator.cs` | 701 | `ImportBigCG` | `rom.write_p32(tableAddr, tileAddr)` | class 'ImageImportValidator' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/Services/ImageImportValidator.cs` | 703 | `ImportBigCG` | `rom.write_u32(tableAddr + (uint)(i * 4), 0)` | class 'ImageImportValidator' has no UndoService field/property/local |

### `MoveToFreeSpaceViewViewModel` — 2 callsites

| File | Line | Method | Write | Note |
|---|---:|---|---|---|
| `FEBuilderGBA.Avalonia/ViewModels/MoveToFreeSpaceViewViewModel.cs` | 110 | `ExecuteMove` | `rom.write_u8(dst + i, rom.u8(srcAddr + i))` | class 'MoveToFreeSpaceViewViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MoveToFreeSpaceViewViewModel.cs` | 114 | `ExecuteMove` | `rom.write_u8(srcAddr + i, 0xFF)` | class 'MoveToFreeSpaceViewViewModel' has no UndoService field/property/local |

### `TextCharCodeViewModel` — 2 callsites

| File | Line | Method | Write | Note |
|---|---:|---|---|---|
| `FEBuilderGBA.Avalonia/ViewModels/TextCharCodeViewModel.cs` | 139 | `Write` | `rom.write_u16(CurrentAddr + 0, (ushort)CharCode)` | class 'TextCharCodeViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/TextCharCodeViewModel.cs` | 140 | `Write` | `rom.write_u16(CurrentAddr + 2, (ushort)TerminatorValue)` | class 'TextCharCodeViewModel' has no UndoService field/property/local |

### `Command85PointerViewModel` — 1 callsite

| File | Line | Method | Write | Note |
|---|---:|---|---|---|
| `FEBuilderGBA.Avalonia/ViewModels/Command85PointerViewModel.cs` | 66 | `Write` | `EditorFormRef.WriteFields(rom, CurrentAddr, values, _fields)` | class 'Command85PointerViewModel' has no UndoService field/property/local |

### `EventBattleDataFE7ViewModel` — 1 callsite

| File | Line | Method | Write | Note |
|---|---:|---|---|---|
| `FEBuilderGBA.Avalonia/ViewModels/EventBattleDataFE7ViewModel.cs` | 62 | `Write` | `EditorFormRef.WriteFields(rom, a, values, _fields)` | class 'EventBattleDataFE7ViewModel' has no UndoService field/property/local |

### `EventBattleTalkFE6ViewModel` — 1 callsite

| File | Line | Method | Write | Note |
|---|---:|---|---|---|
| `FEBuilderGBA.Avalonia/ViewModels/EventBattleTalkFE6ViewModel.cs` | 95 | `Write` | `EditorFormRef.WriteFields(rom, a, values, _fields)` | class 'EventBattleTalkFE6ViewModel' has no UndoService field/property/local |

### `EventBattleTalkFE7ViewModel` — 1 callsite

| File | Line | Method | Write | Note |
|---|---:|---|---|---|
| `FEBuilderGBA.Avalonia/ViewModels/EventBattleTalkFE7ViewModel.cs` | 99 | `Write` | `EditorFormRef.WriteFields(rom, a, values, _fields)` | class 'EventBattleTalkFE7ViewModel' has no UndoService field/property/local |

### `EventBattleTalkViewModel` — 1 callsite

| File | Line | Method | Write | Note |
|---|---:|---|---|---|
| `FEBuilderGBA.Avalonia/ViewModels/EventBattleTalkViewModel.cs` | 94 | `Write` | `EditorFormRef.WriteFields(rom, a, values, _fields)` | class 'EventBattleTalkViewModel' has no UndoService field/property/local |

### `EventFinalSerifFE7ViewModel` — 1 callsite

| File | Line | Method | Write | Note |
|---|---:|---|---|---|
| `FEBuilderGBA.Avalonia/ViewModels/EventFinalSerifFE7ViewModel.cs` | 68 | `Write` | `EditorFormRef.WriteFields(rom, a, values, _fields)` | class 'EventFinalSerifFE7ViewModel' has no UndoService field/property/local |

### `EventForceSortieViewModel` — 1 callsite

| File | Line | Method | Write | Note |
|---|---:|---|---|---|
| `FEBuilderGBA.Avalonia/ViewModels/EventForceSortieViewModel.cs` | 77 | `Write` | `EditorFormRef.WriteFields(rom, a, values, _fields)` | class 'EventForceSortieViewModel' has no UndoService field/property/local |

### `EventFunctionPointerFE7ViewModel` — 1 callsite

| File | Line | Method | Write | Note |
|---|---:|---|---|---|
| `FEBuilderGBA.Avalonia/ViewModels/EventFunctionPointerFE7ViewModel.cs` | 72 | `Write` | `EditorFormRef.WriteFields(rom, a, values, _fields)` | class 'EventFunctionPointerFE7ViewModel' has no UndoService field/property/local |

### `EventFunctionPointerViewModel` — 1 callsite

| File | Line | Method | Write | Note |
|---|---:|---|---|---|
| `FEBuilderGBA.Avalonia/ViewModels/EventFunctionPointerViewModel.cs` | 66 | `Write` | `EditorFormRef.WriteFields(rom, a, values, _fields)` | class 'EventFunctionPointerViewModel' has no UndoService field/property/local |

### `EventHaikuFE6ViewModel` — 1 callsite

| File | Line | Method | Write | Note |
|---|---:|---|---|---|
| `FEBuilderGBA.Avalonia/ViewModels/EventHaikuFE6ViewModel.cs` | 104 | `Write` | `EditorFormRef.WriteFields(rom, a, values, _fields)` | class 'EventHaikuFE6ViewModel' has no UndoService field/property/local |

### `EventHaikuFE7ViewModel` — 1 callsite

| File | Line | Method | Write | Note |
|---|---:|---|---|---|
| `FEBuilderGBA.Avalonia/ViewModels/EventHaikuFE7ViewModel.cs` | 98 | `Write` | `EditorFormRef.WriteFields(rom, a, values, _fields)` | class 'EventHaikuFE7ViewModel' has no UndoService field/property/local |

### `EventHaikuViewModel` — 1 callsite

| File | Line | Method | Write | Note |
|---|---:|---|---|---|
| `FEBuilderGBA.Avalonia/ViewModels/EventHaikuViewModel.cs` | 86 | `Write` | `EditorFormRef.WriteFields(rom, a, values, _fields)` | class 'EventHaikuViewModel' has no UndoService field/property/local |

### `EventMoveDataFE7ViewModel` — 1 callsite

| File | Line | Method | Write | Note |
|---|---:|---|---|---|
| `FEBuilderGBA.Avalonia/ViewModels/EventMoveDataFE7ViewModel.cs` | 54 | `Write` | `EditorFormRef.WriteFields(rom, CurrentAddr, values, _fields)` | class 'EventMoveDataFE7ViewModel' has no UndoService field/property/local |

### `EventTalkGroupFE7ViewModel` — 1 callsite

| File | Line | Method | Write | Note |
|---|---:|---|---|---|
| `FEBuilderGBA.Avalonia/ViewModels/EventTalkGroupFE7ViewModel.cs` | 53 | `Write` | `EditorFormRef.WriteFields(rom, CurrentAddr, values, _fields)` | class 'EventTalkGroupFE7ViewModel' has no UndoService field/property/local |

### `ImageGenericEnemyPortraitViewModel` — 1 callsite

| File | Line | Method | Write | Note |
|---|---:|---|---|---|
| `FEBuilderGBA.Avalonia/ViewModels/ImageGenericEnemyPortraitViewModel.cs` | 63 | `Write` | `rom.write_u32(addr + 0, ImagePointer)` | class 'ImageGenericEnemyPortraitViewModel' has no UndoService field/property/local |

### `SkillAssignmentClassCSkillSysViewViewModel` — 1 callsite

| File | Line | Method | Write | Note |
|---|---:|---|---|---|
| `FEBuilderGBA.Avalonia/ViewModels/SkillAssignmentClassCSkillSysViewViewModel.cs` | 43 | `Write` | `EditorFormRef.WriteFields(rom, addr, values, _fields)` | class 'SkillAssignmentClassCSkillSysViewViewModel' has no UndoService field/property/local |

### `SkillAssignmentUnitCSkillSysViewViewModel` — 1 callsite

| File | Line | Method | Write | Note |
|---|---:|---|---|---|
| `FEBuilderGBA.Avalonia/ViewModels/SkillAssignmentUnitCSkillSysViewViewModel.cs` | 43 | `Write` | `EditorFormRef.WriteFields(rom, addr, values, _fields)` | class 'SkillAssignmentUnitCSkillSysViewViewModel' has no UndoService field/property/local |

### `SkillAssignmentUnitFE8NViewViewModel` — 1 callsite

| File | Line | Method | Write | Note |
|---|---:|---|---|---|
| `FEBuilderGBA.Avalonia/ViewModels/SkillAssignmentUnitFE8NViewViewModel.cs` | 49 | `Write` | `EditorFormRef.WriteFields(rom, addr, values, _fields)` | class 'SkillAssignmentUnitFE8NViewViewModel' has no UndoService field/property/local |

### `SkillAssignmentUnitSkillSystemViewModel` — 1 callsite

| File | Line | Method | Write | Note |
|---|---:|---|---|---|
| `FEBuilderGBA.Avalonia/ViewModels/SkillAssignmentUnitSkillSystemViewModel.cs` | 50 | `Write` | `EditorFormRef.WriteFields(rom, addr, values, _fields)` | class 'SkillAssignmentUnitSkillSystemViewModel' has no UndoService field/property/local |

### `SkillConfigFE8NSkillViewViewModel` — 1 callsite

| File | Line | Method | Write | Note |
|---|---:|---|---|---|
| `FEBuilderGBA.Avalonia/ViewModels/SkillConfigFE8NSkillViewViewModel.cs` | 85 | `Write` | `EditorFormRef.WriteFields(rom, addr, values, _fields)` | class 'SkillConfigFE8NSkillViewViewModel' has no UndoService field/property/local |

### `SkillConfigFE8NVer2SkillViewViewModel` — 1 callsite

| File | Line | Method | Write | Note |
|---|---:|---|---|---|
| `FEBuilderGBA.Avalonia/ViewModels/SkillConfigFE8NVer2SkillViewViewModel.cs` | 59 | `Write` | `EditorFormRef.WriteFields(rom, addr, values, _fields)` | class 'SkillConfigFE8NVer2SkillViewViewModel' has no UndoService field/property/local |

### `SkillConfigFE8NVer3SkillViewViewModel` — 1 callsite

| File | Line | Method | Write | Note |
|---|---:|---|---|---|
| `FEBuilderGBA.Avalonia/ViewModels/SkillConfigFE8NVer3SkillViewViewModel.cs` | 62 | `Write` | `EditorFormRef.WriteFields(rom, addr, values, _fields)` | class 'SkillConfigFE8NVer3SkillViewViewModel' has no UndoService field/property/local |

### `SkillConfigFE8UCSkillSys09xViewViewModel` — 1 callsite

| File | Line | Method | Write | Note |
|---|---:|---|---|---|
| `FEBuilderGBA.Avalonia/ViewModels/SkillConfigFE8UCSkillSys09xViewViewModel.cs` | 46 | `Write` | `EditorFormRef.WriteFields(rom, addr, values, _fields)` | class 'SkillConfigFE8UCSkillSys09xViewViewModel' has no UndoService field/property/local |

### `SkillSystemsEffectivenessReworkClassTypeViewViewModel` — 1 callsite

| File | Line | Method | Write | Note |
|---|---:|---|---|---|
| `FEBuilderGBA.Avalonia/ViewModels/SkillSystemsEffectivenessReworkClassTypeViewViewModel.cs` | 43 | `Write` | `EditorFormRef.WriteFields(rom, addr, values, _fields)` | class 'SkillSystemsEffectivenessReworkClassTypeViewViewModel' has no UndoService field/property/local |

### `TacticianAffinityFE7ViewModel` — 1 callsite

| File | Line | Method | Write | Note |
|---|---:|---|---|---|
| `FEBuilderGBA.Avalonia/ViewModels/TacticianAffinityFE7ViewModel.cs` | 93 | `Write` | `EditorFormRef.WriteFields(rom, CurrentAddr, values, _fields)` | class 'TacticianAffinityFE7ViewModel' has no UndoService field/property/local |

### `UnitActionPointerViewModel` — 1 callsite

| File | Line | Method | Write | Note |
|---|---:|---|---|---|
| `FEBuilderGBA.Avalonia/ViewModels/UnitActionPointerViewModel.cs` | 82 | `WriteEntry` | `EditorFormRef.WriteFields(rom, CurrentAddr, values, _fields)` | class 'UnitActionPointerViewModel' has no UndoService field/property/local |

### `UnitIncreaseHeightViewModel` — 1 callsite

| File | Line | Method | Write | Note |
|---|---:|---|---|---|
| `FEBuilderGBA.Avalonia/ViewModels/UnitIncreaseHeightViewModel.cs` | 115 | `WriteEntry` | `EditorFormRef.WriteFields(rom, CurrentAddr, values, _fields)` | class 'UnitIncreaseHeightViewModel' has no UndoService field/property/local |

### `VennouWeaponLockViewModel` — 1 callsite

| File | Line | Method | Write | Note |
|---|---:|---|---|---|
| `FEBuilderGBA.Avalonia/ViewModels/VennouWeaponLockViewModel.cs` | 145 | `WriteEntry` | `EditorFormRef.WriteFields(rom, CurrentAddr, values, _fields)` | class 'VennouWeaponLockViewModel' has no UndoService field/property/local |

## Missing scope — VMs that have UndoService but skip Begin/Commit on this write

These classes already carry UndoService plumbing but a particular write was added without wrapping it. The fix is local: add `_undoService.Begin("...")` before the write and `_undoService.Commit()` after.

### `BigCGViewerView` — 2 callsites

| File | Line | Method | Write | Note |
|---|---:|---|---|---|
| `FEBuilderGBA.Avalonia/Views/BigCGViewerView.axaml.cs` | 126 | `ImportPng_Click` | `rom.write_p32(tableAddr, tileAddr)` | method 'ImportPng_Click' has an UndoService scope but this write is OUTSIDE it |
| `FEBuilderGBA.Avalonia/Views/BigCGViewerView.axaml.cs` | 129 | `ImportPng_Click` | `rom.write_u32(tableAddr + (uint)(i * 4), 0)` | method 'ImportPng_Click' has an UndoService scope but this write is OUTSIDE it |

## Ambiguous — covered by caller, please verify

The write lives in a helper method; a caller in the same class wraps Begin/Commit. The scanner uses a one-level name-match heuristic, so manual review is required to confirm the helper is actually called from within an active scope at runtime.

_None._

## Covered (healthy)

`841` callsites are inside a Begin/Commit (or Begin/Rollback) scope in the same method body, OR pass an explicit Undo argument. Covered classes: `MapSettingViewModel` (99), `MapSettingFE7UViewModel` (97), `MapSettingFE7ViewModel` (95), `ClassEditorViewModel` (75), `MoveCostFE6ViewModel` (51), `SongInstrumentViewModel` (46), `UnitEditorViewModel` (46), `UnitFE7ViewModel` (46), `ItemEditorViewModel` (26), `ItemFE6ViewModel` (22), `EventCondViewModel` (19), `BattleTerrainViewerViewModel` (15), `ImageUnitPaletteViewModel` (13), `PortraitViewerViewModel` (13), `TextViewerViewModel` (10), `TextDicViewModel` (8), `ImagePortraitFE6ViewModel` (7), `MapChangeViewModel` (7), `EventScriptPopupViewModel` (6), `ImageTSAAnime2ViewModel` (5), `SongTrackViewModel` (5), `WorldMapEventPointerViewModel` (5), `ImagePortraitView` (4), `BattleBGViewerViewModel` (3), `BigCGViewerViewModel` (3), `ChapterTitleViewerViewModel` (3), `ImageBGViewModel` (3), `ImageBattleAnimeViewModel` (3), `ImageBattleBGViewModel` (3), `ImageMapActionAnimationViewModel` (3), `SMEPromoListViewModel` (2), `SongTableViewModel` (2), `AIASMCALLTALKViewModel` (1), `AIASMCoordinateViewModel` (1), `AIASMRangeViewModel` (1), `AIMapSettingViewModel` (1), `AIPerformItemViewModel` (1), `AIPerformStaffViewModel` (1), `AIStealItemViewModel` (1), `AITargetViewModel` (1), `AITilesViewModel` (1), `AIUnitsViewModel` (1), `AOERANGEViewModel` (1), `ArenaClassViewerViewModel` (1), `ArenaEnemyWeaponViewerViewModel` (1), `CCBranchEditorViewModel` (1), `EDSensekiCommentViewModel` (1), `EDStaffRollViewModel` (1), `EDViewModel` (1), `EventUnitFE6ViewModel` (1), `EventUnitFE7ViewModel` (1), `EventUnitViewModel` (1), `ExtraUnitFE8UViewModel` (1), `ExtraUnitViewModel` (1), `ImageChapterTitleFE7ViewModel` (1), `ImageSystemAreaViewModel` (1), `ItemEffectPointerViewerViewModel` (1), `ItemRandomChestViewModel` (1), `ItemShopViewerViewModel` (1), `ItemStatBonusesSkillSystemsViewModel` (1), `ItemStatBonusesVennoViewModel` (1), `ItemStatBonusesViewerViewModel` (1), `ItemUsagePointerViewerViewModel` (1), `ItemWeaponEffectViewerViewModel` (1), `ItemWeaponTriangleViewerViewModel` (1), `LinkArenaDenyUnitViewerViewModel` (1), `MapEditorViewModel` (1), `MapExitPointViewModel` (1), `MapLoadFunctionViewModel` (1), `MapPointerViewModel` (1), `MapStyleEditorViewModel` (1), `MapTerrainBGLookupTableViewModel` (1), `MapTerrainFloorLookupTableViewModel` (1), `MapTerrainNameEngViewModel` (1), `MapTerrainNameViewModel` (1), `MapTileAnimation1ViewModel` (1), `MapTileAnimation2ViewModel` (1), `MapTileAnimationViewModel` (1), `MenuCommandViewModel` (1), `MenuDefinitionViewModel` (1), `MenuExtendSplitMenuViewModel` (1), `MonsterItemViewerViewModel` (1), `MonsterProbabilityViewerViewModel` (1), `MonsterWMapProbabilityViewerViewModel` (1), `MoveCostEditorViewModel` (1), `OPClassAlphaNameFE6ViewModel` (1), `OPClassAlphaNameViewModel` (1), `OPClassDemoFE7UViewModel` (1), `OPClassDemoFE7ViewModel` (1), `OPClassDemoFE8UViewModel` (1), `OPClassDemoViewerViewModel` (1), `OPClassFontFE8UViewModel` (1), `OPClassFontViewerViewModel` (1), `OPPrologueViewerView` (1), `OPPrologueViewerViewModel` (1), `PointerToolViewModel` (1), `SkillAssignmentClassSkillSystemViewModel` (1), `SkillConfigSkillSystemViewModel` (1), `SomeClassListViewModel` (1), `SongInstrumentDirectSoundViewModel` (1), `SoundBossBGMViewerViewModel` (1), `SoundFootStepsViewerViewModel` (1), `SoundRoomCGViewModel` (1), `SoundRoomFE6ViewModel` (1), `SoundRoomViewerViewModel` (1), `StatusOptionOrderViewModel` (1), `StatusOptionViewModel` (1), `StatusParamViewModel` (1), `StatusRMenuViewModel` (1), `StatusUnitsMenuViewModel` (1), `SummonUnitViewerViewModel` (1), `SummonsDemonKingViewerViewModel` (1), `SupportAttributeViewModel` (1), `SupportTalkFE6ViewModel` (1), `SupportTalkFE7ViewModel` (1), `SupportTalkViewModel` (1), `SupportUnitEditorViewModel` (1), `SupportUnitFE6ViewModel` (1), `TerrainNameEditorViewModel` (1), `ToolASMEditView` (1), `ToolLZ77ViewModel` (1), `UnitCustomBattleAnimeViewModel` (1), `UnitPaletteViewModel` (1), `UnitsShortTextViewModel` (1), `WorldMapBGMViewModel` (1), `WorldMapPathMoveEditorViewModel` (1), `WorldMapPathViewModel` (1), `WorldMapPointViewModel` (1).

## Registry cross-check

This section mirrors the writable-triplet convention used by
`WritableViewModelRegistry` (in `FEBuilderGBA.Avalonia.Tests`): every
concrete `ViewModelBase` subclass that exposes both a `Load*List()` and a
`Write*()` method is considered a writable VM. The Phase 5 scanner
reflects over the loaded Avalonia assembly to derive that list and
flags any VM that the static scan did NOT detect ANY ROM write for —
such a row almost always indicates the scanner's pattern set has missed a
real write API (e.g. PR #380 review caught a `CoreState.ROM.write_u*`
miss that surfaced as an unjustified zero-row warning before the fix).

Classes with at least one detected write: 166.

Writable VMs (matching the triplet convention): 139.  
Writable VMs with zero detected ROM writes: 2.

### Writable VMs with zero detected ROM writes (warning)

Each row below names a ViewModel that the writable-triplet reflection
discovers but the scanner did NOT capture any ROM-write callsite for.
If this list is non-empty after Phase 5 ships, investigate the missing
pattern (the most likely cause is a write API the scanner doesn't yet
recognise — see `WriteMethodNames` + `IsRomReceiver` in
`UndoCoverageScanner.cs`).

| ViewModel | Action |
|---|---|
| `ItemEffectivenessViewerViewModel` | Verify ROM-write API; extend scanner pattern set if needed |
| `ItemPromotionViewerViewModel` | Verify ROM-write API; extend scanner pattern set if needed |
