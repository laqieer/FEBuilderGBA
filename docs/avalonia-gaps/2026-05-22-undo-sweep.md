---
generated: "2026-05-21T20:10:03Z"
git-sha: faa4cc8ac
sweep-type: undo
---

# Avalonia vs WinForms — Undo Coverage Sweep

This report inventories every ROM-write callsite in
`FEBuilderGBA.Avalonia/` and classifies its undo coverage. WinForms is
the ground truth — every WF call to `Program.ROM.SetU8/16/32(addr, val,
undo)` takes an `Undo` argument so the compiler enforces undo plumbing
at every callsite. Avalonia uses a different pattern
(`UndoService.Begin(name)` opens a scope `rom.write_u*` calls register
against automatically), but the migration applied this only inside
`EventScriptPopupViewModel`.

**Methodology:**

- Roslyn scans every `.cs` file under `FEBuilderGBA.Avalonia/`,
  excluding `GapSweep/`, `obj/`, and `bin/`.
- Each `InvocationExpressionSyntax` whose method name is in
  {`write_u8`, `write_u16`, `write_u32`, `write_p32`, `SetU8`, `SetU16`,
  `SetU32`, `SetData`} AND whose receiver resolves to a ROM reference
  (`rom`, `ROM`, `Program.ROM`, or `CoreState.ROM`) is captured as a
  write callsite.
- `EditorFormRef.WriteFields(rom, addr, values, fields)` and the
  singular `EditorFormRef.WriteField(...)` are also captured — the
  bulk-write helper through which most AV ViewModels funnel their
  writes. One report row per WriteFields call regardless of how
  many actual bytes the helper writes internally.
- For each callsite we find the enclosing class and method, then
  classify by walking the method body for `Begin`/`Commit`/`Rollback`
  invocations on any UndoService identifier
  (`_undoService`, `undoService`, `UndoService`).
- UndoService discovery: simple name match on field/property/local of
  type `UndoService`. No semantic model — see the AmbiguousScope tier
  for the one-level helper-call disclaimer.

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
| Total write callsites | 1030 | 100% |
| NoUndoServiceField (no plumbing) | 1017 | 98.7% |
| MissingScope (unwrapped) | 0 | 0.0% |
| AmbiguousScope (verify) | 0 | 0.0% |
| Covered (healthy) | 13 | 1.3% |

## Highest priority — VMs with NO undo plumbing at all

These ViewModels have no `UndoService` field/property/local. Every write here bypasses the undo buffer. The fix sequence is: (1) add a `UndoService _undoService = new();` field, (2) wrap each Save / Write handler in `_undoService.Begin/Commit`. Grouped by enclosing class.

### `MapSettingViewModel` — 99 callsites

| File | Line | Method | Write | Note |
|---|---:|---|---|---|
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingViewModel.cs` | 519 | `WriteMapSetting` | `rom.write_u32(addr + 0, CpPointer)` | class 'MapSettingViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingViewModel.cs` | 520 | `WriteMapSetting` | `rom.write_u16(addr + 4, ObjectTypePLIST)` | class 'MapSettingViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingViewModel.cs` | 522 | `WriteMapSetting` | `rom.write_u8(addr + 6, PalettePLIST)` | class 'MapSettingViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingViewModel.cs` | 523 | `WriteMapSetting` | `rom.write_u8(addr + 7, ChipsetConfigPLIST)` | class 'MapSettingViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingViewModel.cs` | 524 | `WriteMapSetting` | `rom.write_u8(addr + 8, MapPointerPLIST)` | class 'MapSettingViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingViewModel.cs` | 525 | `WriteMapSetting` | `rom.write_u8(addr + 9, TileAnimation1PLIST)` | class 'MapSettingViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingViewModel.cs` | 526 | `WriteMapSetting` | `rom.write_u8(addr + 10, TileAnimation2PLIST)` | class 'MapSettingViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingViewModel.cs` | 527 | `WriteMapSetting` | `rom.write_u8(addr + 11, MapChangePLIST)` | class 'MapSettingViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingViewModel.cs` | 528 | `WriteMapSetting` | `rom.write_u8(addr + 12, FogLevel)` | class 'MapSettingViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingViewModel.cs` | 529 | `WriteMapSetting` | `rom.write_u8(addr + 13, BattlePreparation)` | class 'MapSettingViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingViewModel.cs` | 530 | `WriteMapSetting` | `rom.write_u8(addr + 14, ChapterTitleImage)` | class 'MapSettingViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingViewModel.cs` | 531 | `WriteMapSetting` | `rom.write_u8(addr + 15, ChapterTitleImage2)` | class 'MapSettingViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingViewModel.cs` | 532 | `WriteMapSetting` | `rom.write_u8(addr + 16, InitialX)` | class 'MapSettingViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingViewModel.cs` | 533 | `WriteMapSetting` | `rom.write_u8(addr + 17, InitialY)` | class 'MapSettingViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingViewModel.cs` | 534 | `WriteMapSetting` | `rom.write_u8(addr + 18, Weather)` | class 'MapSettingViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingViewModel.cs` | 535 | `WriteMapSetting` | `rom.write_u8(addr + 19, BattleBGLookup)` | class 'MapSettingViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingViewModel.cs` | 537 | `WriteMapSetting` | `rom.write_u16(addr + 20, DifficultyAdjustment)` | class 'MapSettingViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingViewModel.cs` | 538 | `WriteMapSetting` | `rom.write_u16(addr + 22, PlayerPhaseBGM)` | class 'MapSettingViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingViewModel.cs` | 539 | `WriteMapSetting` | `rom.write_u16(addr + 24, EnemyPhaseBGM)` | class 'MapSettingViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingViewModel.cs` | 540 | `WriteMapSetting` | `rom.write_u16(addr + 26, NpcPhaseBGM)` | class 'MapSettingViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingViewModel.cs` | 541 | `WriteMapSetting` | `rom.write_u16(addr + 28, PlayerPhaseBGM2)` | class 'MapSettingViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingViewModel.cs` | 542 | `WriteMapSetting` | `rom.write_u16(addr + 30, EnemyPhaseBGM2)` | class 'MapSettingViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingViewModel.cs` | 543 | `WriteMapSetting` | `rom.write_u16(addr + 32, NpcPhaseBGM2)` | class 'MapSettingViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingViewModel.cs` | 544 | `WriteMapSetting` | `rom.write_u16(addr + 34, PlayerPhaseBGMFlag4)` | class 'MapSettingViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingViewModel.cs` | 545 | `WriteMapSetting` | `rom.write_u16(addr + 36, EnemyPhaseBGMFlag4)` | class 'MapSettingViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingViewModel.cs` | 546 | `WriteMapSetting` | `rom.write_u16(addr + 38, UnknownW38)` | class 'MapSettingViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingViewModel.cs` | 547 | `WriteMapSetting` | `rom.write_u16(addr + 40, UnknownW40)` | class 'MapSettingViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingViewModel.cs` | 548 | `WriteMapSetting` | `rom.write_u16(addr + 42, UnknownW42)` | class 'MapSettingViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingViewModel.cs` | 550 | `WriteMapSetting` | `rom.write_u8(addr + 44, BreakableWallHP)` | class 'MapSettingViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingViewModel.cs` | 551 | `WriteMapSetting` | `rom.write_u8(addr + 45, RatingAEliwoodNormal)` | class 'MapSettingViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingViewModel.cs` | 552 | `WriteMapSetting` | `rom.write_u8(addr + 46, RatingAEliwoodHard)` | class 'MapSettingViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingViewModel.cs` | 553 | `WriteMapSetting` | `rom.write_u8(addr + 47, RatingAHectorNormal)` | class 'MapSettingViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingViewModel.cs` | 554 | `WriteMapSetting` | `rom.write_u8(addr + 48, RatingAHectorHard)` | class 'MapSettingViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingViewModel.cs` | 555 | `WriteMapSetting` | `rom.write_u8(addr + 49, RatingBEliwoodNormal)` | class 'MapSettingViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingViewModel.cs` | 556 | `WriteMapSetting` | `rom.write_u8(addr + 50, RatingBEliwoodHard)` | class 'MapSettingViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingViewModel.cs` | 557 | `WriteMapSetting` | `rom.write_u8(addr + 51, RatingBHectorNormal)` | class 'MapSettingViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingViewModel.cs` | 558 | `WriteMapSetting` | `rom.write_u8(addr + 52, RatingBHectorHard)` | class 'MapSettingViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingViewModel.cs` | 559 | `WriteMapSetting` | `rom.write_u8(addr + 53, RatingCEliwoodNormal)` | class 'MapSettingViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingViewModel.cs` | 560 | `WriteMapSetting` | `rom.write_u8(addr + 54, RatingCEliwoodHard)` | class 'MapSettingViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingViewModel.cs` | 561 | `WriteMapSetting` | `rom.write_u8(addr + 55, RatingCHectorNormal)` | class 'MapSettingViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingViewModel.cs` | 562 | `WriteMapSetting` | `rom.write_u8(addr + 56, RatingCHectorHard)` | class 'MapSettingViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingViewModel.cs` | 563 | `WriteMapSetting` | `rom.write_u8(addr + 57, RatingDEliwoodNormal)` | class 'MapSettingViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingViewModel.cs` | 564 | `WriteMapSetting` | `rom.write_u8(addr + 58, RatingDEliwoodHard)` | class 'MapSettingViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingViewModel.cs` | 565 | `WriteMapSetting` | `rom.write_u8(addr + 59, RatingDHectorNormal)` | class 'MapSettingViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingViewModel.cs` | 566 | `WriteMapSetting` | `rom.write_u8(addr + 60, RatingDHectorHard)` | class 'MapSettingViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingViewModel.cs` | 567 | `WriteMapSetting` | `rom.write_u8(addr + 61, UnknownB61)` | class 'MapSettingViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingViewModel.cs` | 569 | `WriteMapSetting` | `rom.write_u16(addr + 62, RatingAEliwoodNormalW)` | class 'MapSettingViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingViewModel.cs` | 570 | `WriteMapSetting` | `rom.write_u16(addr + 64, RatingAEliwoodHardW)` | class 'MapSettingViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingViewModel.cs` | 571 | `WriteMapSetting` | `rom.write_u16(addr + 66, RatingAHectorNormalW)` | class 'MapSettingViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingViewModel.cs` | 572 | `WriteMapSetting` | `rom.write_u16(addr + 68, RatingAHectorHardW)` | class 'MapSettingViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingViewModel.cs` | 573 | `WriteMapSetting` | `rom.write_u16(addr + 70, RatingBEliwoodNormalW)` | class 'MapSettingViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingViewModel.cs` | 574 | `WriteMapSetting` | `rom.write_u16(addr + 72, RatingBEliwoodHardW)` | class 'MapSettingViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingViewModel.cs` | 575 | `WriteMapSetting` | `rom.write_u16(addr + 74, RatingBHectorNormalW)` | class 'MapSettingViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingViewModel.cs` | 576 | `WriteMapSetting` | `rom.write_u16(addr + 76, RatingBHectorHardW)` | class 'MapSettingViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingViewModel.cs` | 577 | `WriteMapSetting` | `rom.write_u16(addr + 78, RatingCEliwoodNormalW)` | class 'MapSettingViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingViewModel.cs` | 578 | `WriteMapSetting` | `rom.write_u16(addr + 80, RatingCEliwoodHardW)` | class 'MapSettingViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingViewModel.cs` | 579 | `WriteMapSetting` | `rom.write_u16(addr + 82, RatingCHectorNormalW)` | class 'MapSettingViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingViewModel.cs` | 580 | `WriteMapSetting` | `rom.write_u16(addr + 84, RatingCHectorHardW)` | class 'MapSettingViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingViewModel.cs` | 581 | `WriteMapSetting` | `rom.write_u16(addr + 86, RatingDEliwoodNormalW)` | class 'MapSettingViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingViewModel.cs` | 582 | `WriteMapSetting` | `rom.write_u16(addr + 88, RatingDEliwoodHardW)` | class 'MapSettingViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingViewModel.cs` | 583 | `WriteMapSetting` | `rom.write_u16(addr + 90, RatingDHectorNormalW)` | class 'MapSettingViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingViewModel.cs` | 584 | `WriteMapSetting` | `rom.write_u16(addr + 92, RatingDHectorHardW)` | class 'MapSettingViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingViewModel.cs` | 585 | `WriteMapSetting` | `rom.write_u16(addr + 94, UnknownW94)` | class 'MapSettingViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingViewModel.cs` | 587 | `WriteMapSetting` | `rom.write_u32(addr + 96, DiffPtrEliwoodNormal)` | class 'MapSettingViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingViewModel.cs` | 588 | `WriteMapSetting` | `rom.write_u32(addr + 100, DiffPtrEliwoodHard)` | class 'MapSettingViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingViewModel.cs` | 589 | `WriteMapSetting` | `rom.write_u32(addr + 104, DiffPtrHectorNormal)` | class 'MapSettingViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingViewModel.cs` | 590 | `WriteMapSetting` | `rom.write_u32(addr + 108, DiffPtrHectorHard)` | class 'MapSettingViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingViewModel.cs` | 592 | `WriteMapSetting` | `rom.write_u16(addr + 112, MapNameText1)` | class 'MapSettingViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingViewModel.cs` | 593 | `WriteMapSetting` | `rom.write_u16(addr + 114, MapNameText2)` | class 'MapSettingViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingViewModel.cs` | 595 | `WriteMapSetting` | `rom.write_u8(addr + 116, EventIdPLIST)` | class 'MapSettingViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingViewModel.cs` | 596 | `WriteMapSetting` | `rom.write_u8(addr + 117, WorldMapAutoEvent)` | class 'MapSettingViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingViewModel.cs` | 597 | `WriteMapSetting` | `rom.write_u8(addr + 118, UnknownB118)` | class 'MapSettingViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingViewModel.cs` | 598 | `WriteMapSetting` | `rom.write_u8(addr + 119, UnknownB119)` | class 'MapSettingViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingViewModel.cs` | 599 | `WriteMapSetting` | `rom.write_u8(addr + 120, UnknownB120)` | class 'MapSettingViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingViewModel.cs` | 600 | `WriteMapSetting` | `rom.write_u8(addr + 121, UnknownB121)` | class 'MapSettingViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingViewModel.cs` | 601 | `WriteMapSetting` | `rom.write_u8(addr + 122, UnknownB122)` | class 'MapSettingViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingViewModel.cs` | 602 | `WriteMapSetting` | `rom.write_u8(addr + 123, UnknownB123)` | class 'MapSettingViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingViewModel.cs` | 603 | `WriteMapSetting` | `rom.write_u8(addr + 124, UnknownB124)` | class 'MapSettingViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingViewModel.cs` | 604 | `WriteMapSetting` | `rom.write_u8(addr + 125, UnknownB125)` | class 'MapSettingViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingViewModel.cs` | 605 | `WriteMapSetting` | `rom.write_u8(addr + 126, UnknownB126)` | class 'MapSettingViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingViewModel.cs` | 606 | `WriteMapSetting` | `rom.write_u8(addr + 127, UnknownB127)` | class 'MapSettingViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingViewModel.cs` | 607 | `WriteMapSetting` | `rom.write_u8(addr + 128, ChapterNumber)` | class 'MapSettingViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingViewModel.cs` | 608 | `WriteMapSetting` | `rom.write_u8(addr + 129, UnknownB129)` | class 'MapSettingViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingViewModel.cs` | 609 | `WriteMapSetting` | `rom.write_u8(addr + 130, UnknownB130)` | class 'MapSettingViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingViewModel.cs` | 610 | `WriteMapSetting` | `rom.write_u8(addr + 131, UnknownB131)` | class 'MapSettingViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingViewModel.cs` | 611 | `WriteMapSetting` | `rom.write_u8(addr + 132, UnknownB132)` | class 'MapSettingViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingViewModel.cs` | 612 | `WriteMapSetting` | `rom.write_u8(addr + 133, UnknownB133)` | class 'MapSettingViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingViewModel.cs` | 613 | `WriteMapSetting` | `rom.write_u8(addr + 134, VictoryBGMEnemyCount)` | class 'MapSettingViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingViewModel.cs` | 614 | `WriteMapSetting` | `rom.write_u8(addr + 135, BlackoutBeforeStart)` | class 'MapSettingViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingViewModel.cs` | 616 | `WriteMapSetting` | `rom.write_u16(addr + 136, ClearConditionText)` | class 'MapSettingViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingViewModel.cs` | 617 | `WriteMapSetting` | `rom.write_u16(addr + 138, DetailClearConditionText)` | class 'MapSettingViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingViewModel.cs` | 619 | `WriteMapSetting` | `rom.write_u8(addr + 140, SpecialDisplay)` | class 'MapSettingViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingViewModel.cs` | 620 | `WriteMapSetting` | `rom.write_u8(addr + 141, TurnCountDisplay)` | class 'MapSettingViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingViewModel.cs` | 621 | `WriteMapSetting` | `rom.write_u8(addr + 142, DefenseUnitMark)` | class 'MapSettingViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingViewModel.cs` | 622 | `WriteMapSetting` | `rom.write_u8(addr + 143, EscapeMarkerX)` | class 'MapSettingViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingViewModel.cs` | 623 | `WriteMapSetting` | `rom.write_u8(addr + 144, EscapeMarkerY)` | class 'MapSettingViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingViewModel.cs` | 624 | `WriteMapSetting` | `rom.write_u8(addr + 145, UnknownB145)` | class 'MapSettingViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingViewModel.cs` | 625 | `WriteMapSetting` | `rom.write_u8(addr + 146, UnknownB146)` | class 'MapSettingViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingViewModel.cs` | 626 | `WriteMapSetting` | `rom.write_u8(addr + 147, UnknownB147)` | class 'MapSettingViewModel' has no UndoService field/property/local |

### `MapSettingFE7UViewModel` — 97 callsites

| File | Line | Method | Write | Note |
|---|---:|---|---|---|
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7UViewModel.cs` | 393 | `WriteMapSetting` | `rom.write_u32(addr + 0, ChapterPointer)` | class 'MapSettingFE7UViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7UViewModel.cs` | 395 | `WriteMapSetting` | `rom.write_u16(addr + 4, ObjectTypePLIST)` | class 'MapSettingFE7UViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7UViewModel.cs` | 398 | `WriteMapSetting` | `rom.write_u8(addr + 6, PalettePLIST)` | class 'MapSettingFE7UViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7UViewModel.cs` | 399 | `WriteMapSetting` | `rom.write_u8(addr + 7, ChipsetConfigPLIST)` | class 'MapSettingFE7UViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7UViewModel.cs` | 400 | `WriteMapSetting` | `rom.write_u8(addr + 8, MapPointerPLIST)` | class 'MapSettingFE7UViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7UViewModel.cs` | 401 | `WriteMapSetting` | `rom.write_u8(addr + 9, TileAnimation1PLIST)` | class 'MapSettingFE7UViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7UViewModel.cs` | 402 | `WriteMapSetting` | `rom.write_u8(addr + 10, TileAnimation2PLIST)` | class 'MapSettingFE7UViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7UViewModel.cs` | 403 | `WriteMapSetting` | `rom.write_u8(addr + 11, MapChangePLIST)` | class 'MapSettingFE7UViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7UViewModel.cs` | 404 | `WriteMapSetting` | `rom.write_u8(addr + 12, FogLevel)` | class 'MapSettingFE7UViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7UViewModel.cs` | 405 | `WriteMapSetting` | `rom.write_u8(addr + 13, BattlePreparation)` | class 'MapSettingFE7UViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7UViewModel.cs` | 406 | `WriteMapSetting` | `rom.write_u8(addr + 14, ChapterTitleImage)` | class 'MapSettingFE7UViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7UViewModel.cs` | 407 | `WriteMapSetting` | `rom.write_u8(addr + 15, ChapterTitleImage2)` | class 'MapSettingFE7UViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7UViewModel.cs` | 408 | `WriteMapSetting` | `rom.write_u8(addr + 16, Padding16)` | class 'MapSettingFE7UViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7UViewModel.cs` | 409 | `WriteMapSetting` | `rom.write_u8(addr + 17, Padding17)` | class 'MapSettingFE7UViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7UViewModel.cs` | 410 | `WriteMapSetting` | `rom.write_u8(addr + 18, Weather)` | class 'MapSettingFE7UViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7UViewModel.cs` | 411 | `WriteMapSetting` | `rom.write_u8(addr + 19, BattleBG)` | class 'MapSettingFE7UViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7UViewModel.cs` | 414 | `WriteMapSetting` | `rom.write_u16(addr + 20, DifficultyAdjust)` | class 'MapSettingFE7UViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7UViewModel.cs` | 415 | `WriteMapSetting` | `rom.write_u16(addr + 22, PlayerPhaseBGM)` | class 'MapSettingFE7UViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7UViewModel.cs` | 416 | `WriteMapSetting` | `rom.write_u16(addr + 24, EnemyPhaseBGM)` | class 'MapSettingFE7UViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7UViewModel.cs` | 417 | `WriteMapSetting` | `rom.write_u16(addr + 26, NpcBGM)` | class 'MapSettingFE7UViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7UViewModel.cs` | 418 | `WriteMapSetting` | `rom.write_u16(addr + 28, PlayerPhaseBGM2)` | class 'MapSettingFE7UViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7UViewModel.cs` | 419 | `WriteMapSetting` | `rom.write_u16(addr + 30, EnemyPhaseBGM2)` | class 'MapSettingFE7UViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7UViewModel.cs` | 420 | `WriteMapSetting` | `rom.write_u16(addr + 32, NpcBGM2)` | class 'MapSettingFE7UViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7UViewModel.cs` | 421 | `WriteMapSetting` | `rom.write_u16(addr + 34, PlayerPhaseBGMFlag4)` | class 'MapSettingFE7UViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7UViewModel.cs` | 422 | `WriteMapSetting` | `rom.write_u16(addr + 36, EnemyPhaseBGMFlag4)` | class 'MapSettingFE7UViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7UViewModel.cs` | 423 | `WriteMapSetting` | `rom.write_u16(addr + 38, PrologueBGMCommon)` | class 'MapSettingFE7UViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7UViewModel.cs` | 424 | `WriteMapSetting` | `rom.write_u16(addr + 40, PrologueBGMEliwood)` | class 'MapSettingFE7UViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7UViewModel.cs` | 425 | `WriteMapSetting` | `rom.write_u16(addr + 42, PrologueBGMHector)` | class 'MapSettingFE7UViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7UViewModel.cs` | 428 | `WriteMapSetting` | `rom.write_u8(addr + 44, BreakableWallHP)` | class 'MapSettingFE7UViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7UViewModel.cs` | 429 | `WriteMapSetting` | `rom.write_u8(addr + 45, RatingAEliwoodNormal)` | class 'MapSettingFE7UViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7UViewModel.cs` | 430 | `WriteMapSetting` | `rom.write_u8(addr + 46, RatingAEliwoodHard)` | class 'MapSettingFE7UViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7UViewModel.cs` | 431 | `WriteMapSetting` | `rom.write_u8(addr + 47, RatingAHectorNormal)` | class 'MapSettingFE7UViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7UViewModel.cs` | 432 | `WriteMapSetting` | `rom.write_u8(addr + 48, RatingAHectorHard)` | class 'MapSettingFE7UViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7UViewModel.cs` | 433 | `WriteMapSetting` | `rom.write_u8(addr + 49, RatingBEliwoodNormal)` | class 'MapSettingFE7UViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7UViewModel.cs` | 434 | `WriteMapSetting` | `rom.write_u8(addr + 50, RatingBEliwoodHard)` | class 'MapSettingFE7UViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7UViewModel.cs` | 435 | `WriteMapSetting` | `rom.write_u8(addr + 51, RatingBHectorNormal)` | class 'MapSettingFE7UViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7UViewModel.cs` | 436 | `WriteMapSetting` | `rom.write_u8(addr + 52, RatingBHectorHard)` | class 'MapSettingFE7UViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7UViewModel.cs` | 437 | `WriteMapSetting` | `rom.write_u8(addr + 53, RatingCEliwoodNormal)` | class 'MapSettingFE7UViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7UViewModel.cs` | 438 | `WriteMapSetting` | `rom.write_u8(addr + 54, RatingCEliwoodHard)` | class 'MapSettingFE7UViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7UViewModel.cs` | 439 | `WriteMapSetting` | `rom.write_u8(addr + 55, RatingCHectorNormal)` | class 'MapSettingFE7UViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7UViewModel.cs` | 440 | `WriteMapSetting` | `rom.write_u8(addr + 56, RatingCHectorHard)` | class 'MapSettingFE7UViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7UViewModel.cs` | 441 | `WriteMapSetting` | `rom.write_u8(addr + 57, RatingDEliwoodNormal)` | class 'MapSettingFE7UViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7UViewModel.cs` | 442 | `WriteMapSetting` | `rom.write_u8(addr + 58, RatingDEliwoodHard)` | class 'MapSettingFE7UViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7UViewModel.cs` | 443 | `WriteMapSetting` | `rom.write_u8(addr + 59, RatingDHectorNormal)` | class 'MapSettingFE7UViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7UViewModel.cs` | 444 | `WriteMapSetting` | `rom.write_u8(addr + 60, RatingDHectorHard)` | class 'MapSettingFE7UViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7UViewModel.cs` | 445 | `WriteMapSetting` | `rom.write_u8(addr + 61, UnknownB61)` | class 'MapSettingFE7UViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7UViewModel.cs` | 448 | `WriteMapSetting` | `rom.write_u16(addr + 62, RatingAEliwoodNormalW)` | class 'MapSettingFE7UViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7UViewModel.cs` | 449 | `WriteMapSetting` | `rom.write_u16(addr + 64, RatingAEliwoodHardW)` | class 'MapSettingFE7UViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7UViewModel.cs` | 450 | `WriteMapSetting` | `rom.write_u16(addr + 66, RatingAHectorNormalW)` | class 'MapSettingFE7UViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7UViewModel.cs` | 451 | `WriteMapSetting` | `rom.write_u16(addr + 68, RatingAHectorHardW)` | class 'MapSettingFE7UViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7UViewModel.cs` | 452 | `WriteMapSetting` | `rom.write_u16(addr + 70, RatingBEliwoodNormalW)` | class 'MapSettingFE7UViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7UViewModel.cs` | 453 | `WriteMapSetting` | `rom.write_u16(addr + 72, RatingBEliwoodHardW)` | class 'MapSettingFE7UViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7UViewModel.cs` | 454 | `WriteMapSetting` | `rom.write_u16(addr + 74, RatingBHectorNormalW)` | class 'MapSettingFE7UViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7UViewModel.cs` | 455 | `WriteMapSetting` | `rom.write_u16(addr + 76, RatingBHectorHardW)` | class 'MapSettingFE7UViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7UViewModel.cs` | 456 | `WriteMapSetting` | `rom.write_u16(addr + 78, RatingCEliwoodNormalW)` | class 'MapSettingFE7UViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7UViewModel.cs` | 457 | `WriteMapSetting` | `rom.write_u16(addr + 80, RatingCEliwoodHardW)` | class 'MapSettingFE7UViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7UViewModel.cs` | 458 | `WriteMapSetting` | `rom.write_u16(addr + 82, RatingCHectorNormalW)` | class 'MapSettingFE7UViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7UViewModel.cs` | 459 | `WriteMapSetting` | `rom.write_u16(addr + 84, RatingCHectorHardW)` | class 'MapSettingFE7UViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7UViewModel.cs` | 460 | `WriteMapSetting` | `rom.write_u16(addr + 86, RatingDEliwoodNormalW)` | class 'MapSettingFE7UViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7UViewModel.cs` | 461 | `WriteMapSetting` | `rom.write_u16(addr + 88, RatingDEliwoodHardW)` | class 'MapSettingFE7UViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7UViewModel.cs` | 462 | `WriteMapSetting` | `rom.write_u16(addr + 90, RatingDHectorNormalW)` | class 'MapSettingFE7UViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7UViewModel.cs` | 463 | `WriteMapSetting` | `rom.write_u16(addr + 92, RatingDHectorHardW)` | class 'MapSettingFE7UViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7UViewModel.cs` | 464 | `WriteMapSetting` | `rom.write_u16(addr + 94, UnknownW94)` | class 'MapSettingFE7UViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7UViewModel.cs` | 467 | `WriteMapSetting` | `rom.write_u32(addr + 96, EliwoodNormalPtr)` | class 'MapSettingFE7UViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7UViewModel.cs` | 468 | `WriteMapSetting` | `rom.write_u32(addr + 100, EliwoodHardPtr)` | class 'MapSettingFE7UViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7UViewModel.cs` | 469 | `WriteMapSetting` | `rom.write_u32(addr + 104, HectorNormalPtr)` | class 'MapSettingFE7UViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7UViewModel.cs` | 470 | `WriteMapSetting` | `rom.write_u32(addr + 108, HectorHardPtr)` | class 'MapSettingFE7UViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7UViewModel.cs` | 473 | `WriteMapSetting` | `rom.write_u16(addr + 112, ChapterTitleEliwoodTextId)` | class 'MapSettingFE7UViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7UViewModel.cs` | 474 | `WriteMapSetting` | `rom.write_u16(addr + 114, ChapterTitleHectorTextId)` | class 'MapSettingFE7UViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7UViewModel.cs` | 475 | `WriteMapSetting` | `rom.write_u16(addr + 116, ChapterTitleEliwoodText2Id)` | class 'MapSettingFE7UViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7UViewModel.cs` | 476 | `WriteMapSetting` | `rom.write_u16(addr + 118, ChapterTitleHectorText2Id)` | class 'MapSettingFE7UViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7UViewModel.cs` | 479 | `WriteMapSetting` | `rom.write_u8(addr + 120, EventPLIST)` | class 'MapSettingFE7UViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7UViewModel.cs` | 480 | `WriteMapSetting` | `rom.write_u8(addr + 121, WorldMapAutoEvent)` | class 'MapSettingFE7UViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7UViewModel.cs` | 483 | `WriteMapSetting` | `rom.write_u16(addr + 122, FortuneDialogOpeningTextId)` | class 'MapSettingFE7UViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7UViewModel.cs` | 484 | `WriteMapSetting` | `rom.write_u16(addr + 124, FortuneDialogEliwoodTextId)` | class 'MapSettingFE7UViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7UViewModel.cs` | 485 | `WriteMapSetting` | `rom.write_u16(addr + 126, FortuneDialogHectorTextId)` | class 'MapSettingFE7UViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7UViewModel.cs` | 486 | `WriteMapSetting` | `rom.write_u16(addr + 128, FortuneDialogConfirmTextId)` | class 'MapSettingFE7UViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7UViewModel.cs` | 489 | `WriteMapSetting` | `rom.write_u8(addr + 130, FortunePortrait)` | class 'MapSettingFE7UViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7UViewModel.cs` | 490 | `WriteMapSetting` | `rom.write_u8(addr + 131, FortuneFee)` | class 'MapSettingFE7UViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7UViewModel.cs` | 491 | `WriteMapSetting` | `rom.write_u8(addr + 132, PrepScreenChNo1)` | class 'MapSettingFE7UViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7UViewModel.cs` | 492 | `WriteMapSetting` | `rom.write_u8(addr + 133, PrepScreenChNo2)` | class 'MapSettingFE7UViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7UViewModel.cs` | 493 | `WriteMapSetting` | `rom.write_u8(addr + 134, UnknownB134)` | class 'MapSettingFE7UViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7UViewModel.cs` | 494 | `WriteMapSetting` | `rom.write_u8(addr + 135, UnknownB135)` | class 'MapSettingFE7UViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7UViewModel.cs` | 495 | `WriteMapSetting` | `rom.write_u8(addr + 136, UnknownB136)` | class 'MapSettingFE7UViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7UViewModel.cs` | 496 | `WriteMapSetting` | `rom.write_u8(addr + 137, UnknownB137)` | class 'MapSettingFE7UViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7UViewModel.cs` | 497 | `WriteMapSetting` | `rom.write_u8(addr + 138, VictoryBGMEnemyCount)` | class 'MapSettingFE7UViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7UViewModel.cs` | 498 | `WriteMapSetting` | `rom.write_u8(addr + 139, DarkenBeforeStartEvent)` | class 'MapSettingFE7UViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7UViewModel.cs` | 501 | `WriteMapSetting` | `rom.write_u16(addr + 140, ClearConditionTextId)` | class 'MapSettingFE7UViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7UViewModel.cs` | 502 | `WriteMapSetting` | `rom.write_u16(addr + 142, DetailClearConditionTextId)` | class 'MapSettingFE7UViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7UViewModel.cs` | 505 | `WriteMapSetting` | `rom.write_u8(addr + 144, SpecialDisplay)` | class 'MapSettingFE7UViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7UViewModel.cs` | 506 | `WriteMapSetting` | `rom.write_u8(addr + 145, TurnCountDisplay)` | class 'MapSettingFE7UViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7UViewModel.cs` | 507 | `WriteMapSetting` | `rom.write_u8(addr + 146, DefenseUnitMark)` | class 'MapSettingFE7UViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7UViewModel.cs` | 508 | `WriteMapSetting` | `rom.write_u8(addr + 147, EscapeMarkerX)` | class 'MapSettingFE7UViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7UViewModel.cs` | 509 | `WriteMapSetting` | `rom.write_u8(addr + 148, EscapeMarkerY)` | class 'MapSettingFE7UViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7UViewModel.cs` | 510 | `WriteMapSetting` | `rom.write_u8(addr + 149, UnknownB149)` | class 'MapSettingFE7UViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7UViewModel.cs` | 511 | `WriteMapSetting` | `rom.write_u8(addr + 150, UnknownB150)` | class 'MapSettingFE7UViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7UViewModel.cs` | 512 | `WriteMapSetting` | `rom.write_u8(addr + 151, UnknownB151)` | class 'MapSettingFE7UViewModel' has no UndoService field/property/local |

### `MapSettingFE7ViewModel` — 95 callsites

| File | Line | Method | Write | Note |
|---|---:|---|---|---|
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7ViewModel.cs` | 379 | `WriteMapSetting` | `rom.write_u32(addr + 0, CpPointer)` | class 'MapSettingFE7ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7ViewModel.cs` | 380 | `WriteMapSetting` | `rom.write_u16(addr + 4, ObjectTypePLIST)` | class 'MapSettingFE7ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7ViewModel.cs` | 382 | `WriteMapSetting` | `rom.write_u8(addr + 6, PalettePLIST)` | class 'MapSettingFE7ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7ViewModel.cs` | 383 | `WriteMapSetting` | `rom.write_u8(addr + 7, ChipsetConfigPLIST)` | class 'MapSettingFE7ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7ViewModel.cs` | 384 | `WriteMapSetting` | `rom.write_u8(addr + 8, MapPointerPLIST)` | class 'MapSettingFE7ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7ViewModel.cs` | 385 | `WriteMapSetting` | `rom.write_u8(addr + 9, TileAnimation1PLIST)` | class 'MapSettingFE7ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7ViewModel.cs` | 386 | `WriteMapSetting` | `rom.write_u8(addr + 10, TileAnimation2PLIST)` | class 'MapSettingFE7ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7ViewModel.cs` | 387 | `WriteMapSetting` | `rom.write_u8(addr + 11, MapChangePLIST)` | class 'MapSettingFE7ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7ViewModel.cs` | 388 | `WriteMapSetting` | `rom.write_u8(addr + 12, FogLevel)` | class 'MapSettingFE7ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7ViewModel.cs` | 389 | `WriteMapSetting` | `rom.write_u8(addr + 13, BattlePreparation)` | class 'MapSettingFE7ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7ViewModel.cs` | 390 | `WriteMapSetting` | `rom.write_u8(addr + 14, ChapterTitleImage)` | class 'MapSettingFE7ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7ViewModel.cs` | 391 | `WriteMapSetting` | `rom.write_u8(addr + 15, ChapterTitleImage2)` | class 'MapSettingFE7ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7ViewModel.cs` | 392 | `WriteMapSetting` | `rom.write_u8(addr + 16, InitialX)` | class 'MapSettingFE7ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7ViewModel.cs` | 393 | `WriteMapSetting` | `rom.write_u8(addr + 17, InitialY)` | class 'MapSettingFE7ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7ViewModel.cs` | 394 | `WriteMapSetting` | `rom.write_u8(addr + 18, Weather)` | class 'MapSettingFE7ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7ViewModel.cs` | 395 | `WriteMapSetting` | `rom.write_u8(addr + 19, BattleBGLookup)` | class 'MapSettingFE7ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7ViewModel.cs` | 397 | `WriteMapSetting` | `rom.write_u16(addr + 20, DifficultyAdjustment)` | class 'MapSettingFE7ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7ViewModel.cs` | 398 | `WriteMapSetting` | `rom.write_u16(addr + 22, PlayerPhaseBGM)` | class 'MapSettingFE7ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7ViewModel.cs` | 399 | `WriteMapSetting` | `rom.write_u16(addr + 24, EnemyPhaseBGM)` | class 'MapSettingFE7ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7ViewModel.cs` | 400 | `WriteMapSetting` | `rom.write_u16(addr + 26, NpcPhaseBGM)` | class 'MapSettingFE7ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7ViewModel.cs` | 401 | `WriteMapSetting` | `rom.write_u16(addr + 28, PlayerPhaseBGM2)` | class 'MapSettingFE7ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7ViewModel.cs` | 402 | `WriteMapSetting` | `rom.write_u16(addr + 30, EnemyPhaseBGM2)` | class 'MapSettingFE7ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7ViewModel.cs` | 403 | `WriteMapSetting` | `rom.write_u16(addr + 32, NpcPhaseBGM2)` | class 'MapSettingFE7ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7ViewModel.cs` | 404 | `WriteMapSetting` | `rom.write_u16(addr + 34, PlayerPhaseBGMFlag4)` | class 'MapSettingFE7ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7ViewModel.cs` | 405 | `WriteMapSetting` | `rom.write_u16(addr + 36, EnemyPhaseBGMFlag4)` | class 'MapSettingFE7ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7ViewModel.cs` | 406 | `WriteMapSetting` | `rom.write_u16(addr + 38, UnknownW38)` | class 'MapSettingFE7ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7ViewModel.cs` | 407 | `WriteMapSetting` | `rom.write_u16(addr + 40, UnknownW40)` | class 'MapSettingFE7ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7ViewModel.cs` | 408 | `WriteMapSetting` | `rom.write_u16(addr + 42, UnknownW42)` | class 'MapSettingFE7ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7ViewModel.cs` | 410 | `WriteMapSetting` | `rom.write_u8(addr + 44, BreakableWallHP)` | class 'MapSettingFE7ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7ViewModel.cs` | 411 | `WriteMapSetting` | `rom.write_u8(addr + 45, RatingAEliwoodNormal)` | class 'MapSettingFE7ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7ViewModel.cs` | 412 | `WriteMapSetting` | `rom.write_u8(addr + 46, RatingAEliwoodHard)` | class 'MapSettingFE7ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7ViewModel.cs` | 413 | `WriteMapSetting` | `rom.write_u8(addr + 47, RatingAHectorNormal)` | class 'MapSettingFE7ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7ViewModel.cs` | 414 | `WriteMapSetting` | `rom.write_u8(addr + 48, RatingAHectorHard)` | class 'MapSettingFE7ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7ViewModel.cs` | 415 | `WriteMapSetting` | `rom.write_u8(addr + 49, RatingBEliwoodNormal)` | class 'MapSettingFE7ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7ViewModel.cs` | 416 | `WriteMapSetting` | `rom.write_u8(addr + 50, RatingBEliwoodHard)` | class 'MapSettingFE7ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7ViewModel.cs` | 417 | `WriteMapSetting` | `rom.write_u8(addr + 51, RatingBHectorNormal)` | class 'MapSettingFE7ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7ViewModel.cs` | 418 | `WriteMapSetting` | `rom.write_u8(addr + 52, RatingBHectorHard)` | class 'MapSettingFE7ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7ViewModel.cs` | 419 | `WriteMapSetting` | `rom.write_u8(addr + 53, RatingCEliwoodNormal)` | class 'MapSettingFE7ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7ViewModel.cs` | 420 | `WriteMapSetting` | `rom.write_u8(addr + 54, RatingCEliwoodHard)` | class 'MapSettingFE7ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7ViewModel.cs` | 421 | `WriteMapSetting` | `rom.write_u8(addr + 55, RatingCHectorNormal)` | class 'MapSettingFE7ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7ViewModel.cs` | 422 | `WriteMapSetting` | `rom.write_u8(addr + 56, RatingCHectorHard)` | class 'MapSettingFE7ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7ViewModel.cs` | 423 | `WriteMapSetting` | `rom.write_u8(addr + 57, RatingDEliwoodNormal)` | class 'MapSettingFE7ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7ViewModel.cs` | 424 | `WriteMapSetting` | `rom.write_u8(addr + 58, RatingDEliwoodHard)` | class 'MapSettingFE7ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7ViewModel.cs` | 425 | `WriteMapSetting` | `rom.write_u8(addr + 59, RatingDHectorNormal)` | class 'MapSettingFE7ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7ViewModel.cs` | 426 | `WriteMapSetting` | `rom.write_u8(addr + 60, RatingDHectorHard)` | class 'MapSettingFE7ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7ViewModel.cs` | 427 | `WriteMapSetting` | `rom.write_u8(addr + 61, UnknownB61)` | class 'MapSettingFE7ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7ViewModel.cs` | 429 | `WriteMapSetting` | `rom.write_u16(addr + 62, RatingAEliwoodNormalW)` | class 'MapSettingFE7ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7ViewModel.cs` | 430 | `WriteMapSetting` | `rom.write_u16(addr + 64, RatingAEliwoodHardW)` | class 'MapSettingFE7ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7ViewModel.cs` | 431 | `WriteMapSetting` | `rom.write_u16(addr + 66, RatingAHectorNormalW)` | class 'MapSettingFE7ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7ViewModel.cs` | 432 | `WriteMapSetting` | `rom.write_u16(addr + 68, RatingAHectorHardW)` | class 'MapSettingFE7ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7ViewModel.cs` | 433 | `WriteMapSetting` | `rom.write_u16(addr + 70, RatingBEliwoodNormalW)` | class 'MapSettingFE7ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7ViewModel.cs` | 434 | `WriteMapSetting` | `rom.write_u16(addr + 72, RatingBEliwoodHardW)` | class 'MapSettingFE7ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7ViewModel.cs` | 435 | `WriteMapSetting` | `rom.write_u16(addr + 74, RatingBHectorNormalW)` | class 'MapSettingFE7ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7ViewModel.cs` | 436 | `WriteMapSetting` | `rom.write_u16(addr + 76, RatingBHectorHardW)` | class 'MapSettingFE7ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7ViewModel.cs` | 437 | `WriteMapSetting` | `rom.write_u16(addr + 78, RatingCEliwoodNormalW)` | class 'MapSettingFE7ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7ViewModel.cs` | 438 | `WriteMapSetting` | `rom.write_u16(addr + 80, RatingCEliwoodHardW)` | class 'MapSettingFE7ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7ViewModel.cs` | 439 | `WriteMapSetting` | `rom.write_u16(addr + 82, RatingCHectorNormalW)` | class 'MapSettingFE7ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7ViewModel.cs` | 440 | `WriteMapSetting` | `rom.write_u16(addr + 84, RatingCHectorHardW)` | class 'MapSettingFE7ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7ViewModel.cs` | 441 | `WriteMapSetting` | `rom.write_u16(addr + 86, RatingDEliwoodNormalW)` | class 'MapSettingFE7ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7ViewModel.cs` | 442 | `WriteMapSetting` | `rom.write_u16(addr + 88, RatingDEliwoodHardW)` | class 'MapSettingFE7ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7ViewModel.cs` | 443 | `WriteMapSetting` | `rom.write_u16(addr + 90, RatingDHectorNormalW)` | class 'MapSettingFE7ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7ViewModel.cs` | 444 | `WriteMapSetting` | `rom.write_u16(addr + 92, RatingDHectorHardW)` | class 'MapSettingFE7ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7ViewModel.cs` | 445 | `WriteMapSetting` | `rom.write_u16(addr + 94, UnknownW94)` | class 'MapSettingFE7ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7ViewModel.cs` | 447 | `WriteMapSetting` | `rom.write_u32(addr + 96, DiffPtrEliwoodNormal)` | class 'MapSettingFE7ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7ViewModel.cs` | 448 | `WriteMapSetting` | `rom.write_u32(addr + 100, DiffPtrEliwoodHard)` | class 'MapSettingFE7ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7ViewModel.cs` | 449 | `WriteMapSetting` | `rom.write_u32(addr + 104, DiffPtrHectorNormal)` | class 'MapSettingFE7ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7ViewModel.cs` | 450 | `WriteMapSetting` | `rom.write_u32(addr + 108, DiffPtrHectorHard)` | class 'MapSettingFE7ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7ViewModel.cs` | 452 | `WriteMapSetting` | `rom.write_u16(addr + 112, MapNameText1)` | class 'MapSettingFE7ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7ViewModel.cs` | 453 | `WriteMapSetting` | `rom.write_u16(addr + 114, MapNameText2)` | class 'MapSettingFE7ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7ViewModel.cs` | 455 | `WriteMapSetting` | `rom.write_u8(addr + 116, EventIdPLIST)` | class 'MapSettingFE7ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7ViewModel.cs` | 456 | `WriteMapSetting` | `rom.write_u8(addr + 117, WorldMapAutoEvent)` | class 'MapSettingFE7ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7ViewModel.cs` | 457 | `WriteMapSetting` | `rom.write_u16(addr + 118, FortuneTextOpening)` | class 'MapSettingFE7ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7ViewModel.cs` | 458 | `WriteMapSetting` | `rom.write_u16(addr + 120, FortuneTextEliwood)` | class 'MapSettingFE7ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7ViewModel.cs` | 459 | `WriteMapSetting` | `rom.write_u16(addr + 122, FortuneTextHector)` | class 'MapSettingFE7ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7ViewModel.cs` | 460 | `WriteMapSetting` | `rom.write_u16(addr + 124, FortuneTextConfirm)` | class 'MapSettingFE7ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7ViewModel.cs` | 461 | `WriteMapSetting` | `rom.write_u8(addr + 126, FortunePortrait)` | class 'MapSettingFE7ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7ViewModel.cs` | 462 | `WriteMapSetting` | `rom.write_u8(addr + 127, FortuneFee)` | class 'MapSettingFE7ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7ViewModel.cs` | 463 | `WriteMapSetting` | `rom.write_u8(addr + 128, ChapterNumber)` | class 'MapSettingFE7ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7ViewModel.cs` | 464 | `WriteMapSetting` | `rom.write_u8(addr + 129, UnknownB129)` | class 'MapSettingFE7ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7ViewModel.cs` | 465 | `WriteMapSetting` | `rom.write_u8(addr + 130, UnknownB130)` | class 'MapSettingFE7ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7ViewModel.cs` | 466 | `WriteMapSetting` | `rom.write_u8(addr + 131, UnknownB131)` | class 'MapSettingFE7ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7ViewModel.cs` | 467 | `WriteMapSetting` | `rom.write_u8(addr + 132, UnknownB132)` | class 'MapSettingFE7ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7ViewModel.cs` | 468 | `WriteMapSetting` | `rom.write_u8(addr + 133, UnknownB133)` | class 'MapSettingFE7ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7ViewModel.cs` | 469 | `WriteMapSetting` | `rom.write_u8(addr + 134, VictoryBGMEnemyCount)` | class 'MapSettingFE7ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7ViewModel.cs` | 470 | `WriteMapSetting` | `rom.write_u8(addr + 135, BlackoutBeforeStart)` | class 'MapSettingFE7ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7ViewModel.cs` | 472 | `WriteMapSetting` | `rom.write_u16(addr + 136, ClearConditionText)` | class 'MapSettingFE7ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7ViewModel.cs` | 473 | `WriteMapSetting` | `rom.write_u16(addr + 138, DetailClearConditionText)` | class 'MapSettingFE7ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7ViewModel.cs` | 475 | `WriteMapSetting` | `rom.write_u8(addr + 140, SpecialDisplay)` | class 'MapSettingFE7ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7ViewModel.cs` | 476 | `WriteMapSetting` | `rom.write_u8(addr + 141, TurnCountDisplay)` | class 'MapSettingFE7ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7ViewModel.cs` | 477 | `WriteMapSetting` | `rom.write_u8(addr + 142, DefenseUnitMark)` | class 'MapSettingFE7ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7ViewModel.cs` | 478 | `WriteMapSetting` | `rom.write_u8(addr + 143, EscapeMarkerX)` | class 'MapSettingFE7ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7ViewModel.cs` | 479 | `WriteMapSetting` | `rom.write_u8(addr + 144, EscapeMarkerY)` | class 'MapSettingFE7ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7ViewModel.cs` | 480 | `WriteMapSetting` | `rom.write_u8(addr + 145, UnknownB145)` | class 'MapSettingFE7ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7ViewModel.cs` | 481 | `WriteMapSetting` | `rom.write_u8(addr + 146, UnknownB146)` | class 'MapSettingFE7ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapSettingFE7ViewModel.cs` | 482 | `WriteMapSetting` | `rom.write_u8(addr + 147, UnknownB147)` | class 'MapSettingFE7ViewModel' has no UndoService field/property/local |

### `ClassEditorViewModel` — 75 callsites

| File | Line | Method | Write | Note |
|---|---:|---|---|---|
| `FEBuilderGBA.Avalonia/ViewModels/ClassEditorViewModel.cs` | 670 | `WriteClass` | `rom.write_u16(addr + 0, NameId)` | class 'ClassEditorViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ClassEditorViewModel.cs` | 671 | `WriteClass` | `rom.write_u16(addr + 2, DescId)` | class 'ClassEditorViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ClassEditorViewModel.cs` | 672 | `WriteClass` | `rom.write_u8(addr + 4, ClassNumber)` | class 'ClassEditorViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ClassEditorViewModel.cs` | 673 | `WriteClass` | `rom.write_u8(addr + 5, PromotionLevel)` | class 'ClassEditorViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ClassEditorViewModel.cs` | 674 | `WriteClass` | `rom.write_u8(addr + 6, WaitIcon)` | class 'ClassEditorViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ClassEditorViewModel.cs` | 675 | `WriteClass` | `rom.write_u8(addr + 7, WalkSpeed)` | class 'ClassEditorViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ClassEditorViewModel.cs` | 676 | `WriteClass` | `rom.write_u16(addr + 8, PortraitId)` | class 'ClassEditorViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ClassEditorViewModel.cs` | 677 | `WriteClass` | `rom.write_u8(addr + 10, SortOrder)` | class 'ClassEditorViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ClassEditorViewModel.cs` | 679 | `WriteClass` | `rom.write_u8(addr + 11, BaseHp)` | class 'ClassEditorViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ClassEditorViewModel.cs` | 680 | `WriteClass` | `rom.write_u8(addr + 12, BaseStr)` | class 'ClassEditorViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ClassEditorViewModel.cs` | 681 | `WriteClass` | `rom.write_u8(addr + 13, BaseSkl)` | class 'ClassEditorViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ClassEditorViewModel.cs` | 682 | `WriteClass` | `rom.write_u8(addr + 14, BaseSpd)` | class 'ClassEditorViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ClassEditorViewModel.cs` | 683 | `WriteClass` | `rom.write_u8(addr + 15, BaseDef)` | class 'ClassEditorViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ClassEditorViewModel.cs` | 684 | `WriteClass` | `rom.write_u8(addr + 16, BaseRes)` | class 'ClassEditorViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ClassEditorViewModel.cs` | 685 | `WriteClass` | `rom.write_u8(addr + 17, BaseCon)` | class 'ClassEditorViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ClassEditorViewModel.cs` | 686 | `WriteClass` | `rom.write_u8(addr + 18, BaseMov)` | class 'ClassEditorViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ClassEditorViewModel.cs` | 688 | `WriteClass` | `rom.write_u8(addr + 19, MaxHp)` | class 'ClassEditorViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ClassEditorViewModel.cs` | 689 | `WriteClass` | `rom.write_u8(addr + 20, MaxStr)` | class 'ClassEditorViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ClassEditorViewModel.cs` | 690 | `WriteClass` | `rom.write_u8(addr + 21, MaxSkl)` | class 'ClassEditorViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ClassEditorViewModel.cs` | 691 | `WriteClass` | `rom.write_u8(addr + 22, MaxSpd)` | class 'ClassEditorViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ClassEditorViewModel.cs` | 692 | `WriteClass` | `rom.write_u8(addr + 23, MaxDef)` | class 'ClassEditorViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ClassEditorViewModel.cs` | 693 | `WriteClass` | `rom.write_u8(addr + 24, MaxRes)` | class 'ClassEditorViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ClassEditorViewModel.cs` | 694 | `WriteClass` | `rom.write_u8(addr + 25, MaxCon)` | class 'ClassEditorViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ClassEditorViewModel.cs` | 695 | `WriteClass` | `rom.write_u8(addr + 26, ClassPower)` | class 'ClassEditorViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ClassEditorViewModel.cs` | 697 | `WriteClass` | `rom.write_u8(addr + 27, GrowHp)` | class 'ClassEditorViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ClassEditorViewModel.cs` | 698 | `WriteClass` | `rom.write_u8(addr + 28, GrowStr)` | class 'ClassEditorViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ClassEditorViewModel.cs` | 699 | `WriteClass` | `rom.write_u8(addr + 29, GrowSkl)` | class 'ClassEditorViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ClassEditorViewModel.cs` | 700 | `WriteClass` | `rom.write_u8(addr + 30, GrowSpd)` | class 'ClassEditorViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ClassEditorViewModel.cs` | 701 | `WriteClass` | `rom.write_u8(addr + 31, GrowDef)` | class 'ClassEditorViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ClassEditorViewModel.cs` | 702 | `WriteClass` | `rom.write_u8(addr + 32, GrowRes)` | class 'ClassEditorViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ClassEditorViewModel.cs` | 703 | `WriteClass` | `rom.write_u8(addr + 33, GrowLck)` | class 'ClassEditorViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ClassEditorViewModel.cs` | 705 | `WriteClass` | `rom.write_u8(addr + 34, (uint)(byte)(sbyte)PromoHp)` | class 'ClassEditorViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ClassEditorViewModel.cs` | 706 | `WriteClass` | `rom.write_u8(addr + 35, (uint)(byte)(sbyte)PromoStr)` | class 'ClassEditorViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ClassEditorViewModel.cs` | 712 | `WriteClass` | `rom.write_u8(addr + 36, Ability1)` | class 'ClassEditorViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ClassEditorViewModel.cs` | 713 | `WriteClass` | `rom.write_u8(addr + 37, Ability2)` | class 'ClassEditorViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ClassEditorViewModel.cs` | 714 | `WriteClass` | `rom.write_u8(addr + 38, Ability3)` | class 'ClassEditorViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ClassEditorViewModel.cs` | 715 | `WriteClass` | `rom.write_u8(addr + 39, Ability4)` | class 'ClassEditorViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ClassEditorViewModel.cs` | 718 | `WriteClass` | `rom.write_u8(addr + 40, WepRankSword)` | class 'ClassEditorViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ClassEditorViewModel.cs` | 719 | `WriteClass` | `rom.write_u8(addr + 41, WepRankLance)` | class 'ClassEditorViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ClassEditorViewModel.cs` | 720 | `WriteClass` | `rom.write_u8(addr + 42, WepRankAxe)` | class 'ClassEditorViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ClassEditorViewModel.cs` | 721 | `WriteClass` | `rom.write_u8(addr + 43, WepRankBow)` | class 'ClassEditorViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ClassEditorViewModel.cs` | 722 | `WriteClass` | `rom.write_u8(addr + 44, WepRankStaff)` | class 'ClassEditorViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ClassEditorViewModel.cs` | 723 | `WriteClass` | `rom.write_u8(addr + 45, WepRankAnima)` | class 'ClassEditorViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ClassEditorViewModel.cs` | 724 | `WriteClass` | `rom.write_u8(addr + 46, WepRankLight)` | class 'ClassEditorViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ClassEditorViewModel.cs` | 725 | `WriteClass` | `rom.write_u8(addr + 47, WepRankDark)` | class 'ClassEditorViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ClassEditorViewModel.cs` | 730 | `WriteClass` | `rom.write_u32(addr + 48, BattleAnimePtr)` | class 'ClassEditorViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ClassEditorViewModel.cs` | 731 | `WriteClass` | `rom.write_u32(addr + 52, MoveCostPtr)` | class 'ClassEditorViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ClassEditorViewModel.cs` | 732 | `WriteClass` | `rom.write_u32(addr + 56, TerrainAvoidPtr)` | class 'ClassEditorViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ClassEditorViewModel.cs` | 733 | `WriteClass` | `rom.write_u32(addr + 60, TerrainDefPtr)` | class 'ClassEditorViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ClassEditorViewModel.cs` | 734 | `WriteClass` | `rom.write_u32(addr + 64, TerrainResPtr)` | class 'ClassEditorViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ClassEditorViewModel.cs` | 735 | `WriteClass` | `rom.write_u32(addr + 68, UnknownD80)` | class 'ClassEditorViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ClassEditorViewModel.cs` | 740 | `WriteClass` | `rom.write_u8(addr + 36, (uint)(byte)(sbyte)PromoSkl)` | class 'ClassEditorViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ClassEditorViewModel.cs` | 741 | `WriteClass` | `rom.write_u8(addr + 37, (uint)(byte)(sbyte)PromoSpd)` | class 'ClassEditorViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ClassEditorViewModel.cs` | 742 | `WriteClass` | `rom.write_u8(addr + 38, (uint)(byte)(sbyte)PromoDef)` | class 'ClassEditorViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ClassEditorViewModel.cs` | 743 | `WriteClass` | `rom.write_u8(addr + 39, (uint)(byte)(sbyte)PromoRes)` | class 'ClassEditorViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ClassEditorViewModel.cs` | 746 | `WriteClass` | `rom.write_u8(addr + 40, Ability1)` | class 'ClassEditorViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ClassEditorViewModel.cs` | 747 | `WriteClass` | `rom.write_u8(addr + 41, Ability2)` | class 'ClassEditorViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ClassEditorViewModel.cs` | 748 | `WriteClass` | `rom.write_u8(addr + 42, Ability3)` | class 'ClassEditorViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ClassEditorViewModel.cs` | 749 | `WriteClass` | `rom.write_u8(addr + 43, Ability4)` | class 'ClassEditorViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ClassEditorViewModel.cs` | 752 | `WriteClass` | `rom.write_u8(addr + 44, WepRankSword)` | class 'ClassEditorViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ClassEditorViewModel.cs` | 753 | `WriteClass` | `rom.write_u8(addr + 45, WepRankLance)` | class 'ClassEditorViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ClassEditorViewModel.cs` | 754 | `WriteClass` | `rom.write_u8(addr + 46, WepRankAxe)` | class 'ClassEditorViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ClassEditorViewModel.cs` | 755 | `WriteClass` | `rom.write_u8(addr + 47, WepRankBow)` | class 'ClassEditorViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ClassEditorViewModel.cs` | 756 | `WriteClass` | `rom.write_u8(addr + 48, WepRankStaff)` | class 'ClassEditorViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ClassEditorViewModel.cs` | 757 | `WriteClass` | `rom.write_u8(addr + 49, WepRankAnima)` | class 'ClassEditorViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ClassEditorViewModel.cs` | 758 | `WriteClass` | `rom.write_u8(addr + 50, WepRankLight)` | class 'ClassEditorViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ClassEditorViewModel.cs` | 759 | `WriteClass` | `rom.write_u8(addr + 51, WepRankDark)` | class 'ClassEditorViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ClassEditorViewModel.cs` | 762 | `WriteClass` | `rom.write_u32(addr + 52, BattleAnimePtr)` | class 'ClassEditorViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ClassEditorViewModel.cs` | 763 | `WriteClass` | `rom.write_u32(addr + 56, MoveCostPtr)` | class 'ClassEditorViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ClassEditorViewModel.cs` | 764 | `WriteClass` | `rom.write_u32(addr + 60, MoveCostRainPtr)` | class 'ClassEditorViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ClassEditorViewModel.cs` | 765 | `WriteClass` | `rom.write_u32(addr + 64, MoveCostSnowPtr)` | class 'ClassEditorViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ClassEditorViewModel.cs` | 766 | `WriteClass` | `rom.write_u32(addr + 68, TerrainAvoidPtr)` | class 'ClassEditorViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ClassEditorViewModel.cs` | 767 | `WriteClass` | `rom.write_u32(addr + 72, TerrainDefPtr)` | class 'ClassEditorViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ClassEditorViewModel.cs` | 768 | `WriteClass` | `rom.write_u32(addr + 76, TerrainResPtr)` | class 'ClassEditorViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ClassEditorViewModel.cs` | 769 | `WriteClass` | `rom.write_u32(addr + 80, UnknownD80)` | class 'ClassEditorViewModel' has no UndoService field/property/local |

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

### `MoveCostFE6ViewModel` — 51 callsites

| File | Line | Method | Write | Note |
|---|---:|---|---|---|
| `FEBuilderGBA.Avalonia/ViewModels/MoveCostFE6ViewModel.cs` | 294 | `WriteMoveCost` | `rom.write_u8(moveCostAddr + 0, B0)` | class 'MoveCostFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MoveCostFE6ViewModel.cs` | 295 | `WriteMoveCost` | `rom.write_u8(moveCostAddr + 1, B1)` | class 'MoveCostFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MoveCostFE6ViewModel.cs` | 296 | `WriteMoveCost` | `rom.write_u8(moveCostAddr + 2, B2)` | class 'MoveCostFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MoveCostFE6ViewModel.cs` | 297 | `WriteMoveCost` | `rom.write_u8(moveCostAddr + 3, B3)` | class 'MoveCostFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MoveCostFE6ViewModel.cs` | 298 | `WriteMoveCost` | `rom.write_u8(moveCostAddr + 4, B4)` | class 'MoveCostFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MoveCostFE6ViewModel.cs` | 299 | `WriteMoveCost` | `rom.write_u8(moveCostAddr + 5, B5)` | class 'MoveCostFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MoveCostFE6ViewModel.cs` | 300 | `WriteMoveCost` | `rom.write_u8(moveCostAddr + 6, B6)` | class 'MoveCostFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MoveCostFE6ViewModel.cs` | 301 | `WriteMoveCost` | `rom.write_u8(moveCostAddr + 7, B7)` | class 'MoveCostFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MoveCostFE6ViewModel.cs` | 302 | `WriteMoveCost` | `rom.write_u8(moveCostAddr + 8, B8)` | class 'MoveCostFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MoveCostFE6ViewModel.cs` | 303 | `WriteMoveCost` | `rom.write_u8(moveCostAddr + 9, B9)` | class 'MoveCostFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MoveCostFE6ViewModel.cs` | 304 | `WriteMoveCost` | `rom.write_u8(moveCostAddr + 10, B10)` | class 'MoveCostFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MoveCostFE6ViewModel.cs` | 305 | `WriteMoveCost` | `rom.write_u8(moveCostAddr + 11, B11)` | class 'MoveCostFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MoveCostFE6ViewModel.cs` | 306 | `WriteMoveCost` | `rom.write_u8(moveCostAddr + 12, B12)` | class 'MoveCostFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MoveCostFE6ViewModel.cs` | 307 | `WriteMoveCost` | `rom.write_u8(moveCostAddr + 13, B13)` | class 'MoveCostFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MoveCostFE6ViewModel.cs` | 308 | `WriteMoveCost` | `rom.write_u8(moveCostAddr + 14, B14)` | class 'MoveCostFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MoveCostFE6ViewModel.cs` | 309 | `WriteMoveCost` | `rom.write_u8(moveCostAddr + 15, B15)` | class 'MoveCostFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MoveCostFE6ViewModel.cs` | 310 | `WriteMoveCost` | `rom.write_u8(moveCostAddr + 16, B16)` | class 'MoveCostFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MoveCostFE6ViewModel.cs` | 311 | `WriteMoveCost` | `rom.write_u8(moveCostAddr + 17, B17)` | class 'MoveCostFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MoveCostFE6ViewModel.cs` | 312 | `WriteMoveCost` | `rom.write_u8(moveCostAddr + 18, B18)` | class 'MoveCostFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MoveCostFE6ViewModel.cs` | 313 | `WriteMoveCost` | `rom.write_u8(moveCostAddr + 19, B19)` | class 'MoveCostFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MoveCostFE6ViewModel.cs` | 314 | `WriteMoveCost` | `rom.write_u8(moveCostAddr + 20, B20)` | class 'MoveCostFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MoveCostFE6ViewModel.cs` | 315 | `WriteMoveCost` | `rom.write_u8(moveCostAddr + 21, B21)` | class 'MoveCostFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MoveCostFE6ViewModel.cs` | 316 | `WriteMoveCost` | `rom.write_u8(moveCostAddr + 22, B22)` | class 'MoveCostFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MoveCostFE6ViewModel.cs` | 317 | `WriteMoveCost` | `rom.write_u8(moveCostAddr + 23, B23)` | class 'MoveCostFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MoveCostFE6ViewModel.cs` | 318 | `WriteMoveCost` | `rom.write_u8(moveCostAddr + 24, B24)` | class 'MoveCostFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MoveCostFE6ViewModel.cs` | 319 | `WriteMoveCost` | `rom.write_u8(moveCostAddr + 25, B25)` | class 'MoveCostFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MoveCostFE6ViewModel.cs` | 320 | `WriteMoveCost` | `rom.write_u8(moveCostAddr + 26, B26)` | class 'MoveCostFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MoveCostFE6ViewModel.cs` | 321 | `WriteMoveCost` | `rom.write_u8(moveCostAddr + 27, B27)` | class 'MoveCostFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MoveCostFE6ViewModel.cs` | 322 | `WriteMoveCost` | `rom.write_u8(moveCostAddr + 28, B28)` | class 'MoveCostFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MoveCostFE6ViewModel.cs` | 323 | `WriteMoveCost` | `rom.write_u8(moveCostAddr + 29, B29)` | class 'MoveCostFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MoveCostFE6ViewModel.cs` | 324 | `WriteMoveCost` | `rom.write_u8(moveCostAddr + 30, B30)` | class 'MoveCostFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MoveCostFE6ViewModel.cs` | 325 | `WriteMoveCost` | `rom.write_u8(moveCostAddr + 31, B31)` | class 'MoveCostFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MoveCostFE6ViewModel.cs` | 326 | `WriteMoveCost` | `rom.write_u8(moveCostAddr + 32, B32)` | class 'MoveCostFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MoveCostFE6ViewModel.cs` | 327 | `WriteMoveCost` | `rom.write_u8(moveCostAddr + 33, B33)` | class 'MoveCostFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MoveCostFE6ViewModel.cs` | 328 | `WriteMoveCost` | `rom.write_u8(moveCostAddr + 34, B34)` | class 'MoveCostFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MoveCostFE6ViewModel.cs` | 329 | `WriteMoveCost` | `rom.write_u8(moveCostAddr + 35, B35)` | class 'MoveCostFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MoveCostFE6ViewModel.cs` | 330 | `WriteMoveCost` | `rom.write_u8(moveCostAddr + 36, B36)` | class 'MoveCostFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MoveCostFE6ViewModel.cs` | 331 | `WriteMoveCost` | `rom.write_u8(moveCostAddr + 37, B37)` | class 'MoveCostFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MoveCostFE6ViewModel.cs` | 332 | `WriteMoveCost` | `rom.write_u8(moveCostAddr + 38, B38)` | class 'MoveCostFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MoveCostFE6ViewModel.cs` | 333 | `WriteMoveCost` | `rom.write_u8(moveCostAddr + 39, B39)` | class 'MoveCostFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MoveCostFE6ViewModel.cs` | 334 | `WriteMoveCost` | `rom.write_u8(moveCostAddr + 40, B40)` | class 'MoveCostFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MoveCostFE6ViewModel.cs` | 335 | `WriteMoveCost` | `rom.write_u8(moveCostAddr + 41, B41)` | class 'MoveCostFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MoveCostFE6ViewModel.cs` | 336 | `WriteMoveCost` | `rom.write_u8(moveCostAddr + 42, B42)` | class 'MoveCostFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MoveCostFE6ViewModel.cs` | 337 | `WriteMoveCost` | `rom.write_u8(moveCostAddr + 43, B43)` | class 'MoveCostFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MoveCostFE6ViewModel.cs` | 338 | `WriteMoveCost` | `rom.write_u8(moveCostAddr + 44, B44)` | class 'MoveCostFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MoveCostFE6ViewModel.cs` | 339 | `WriteMoveCost` | `rom.write_u8(moveCostAddr + 45, B45)` | class 'MoveCostFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MoveCostFE6ViewModel.cs` | 340 | `WriteMoveCost` | `rom.write_u8(moveCostAddr + 46, B46)` | class 'MoveCostFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MoveCostFE6ViewModel.cs` | 341 | `WriteMoveCost` | `rom.write_u8(moveCostAddr + 47, B47)` | class 'MoveCostFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MoveCostFE6ViewModel.cs` | 342 | `WriteMoveCost` | `rom.write_u8(moveCostAddr + 48, B48)` | class 'MoveCostFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MoveCostFE6ViewModel.cs` | 343 | `WriteMoveCost` | `rom.write_u8(moveCostAddr + 49, B49)` | class 'MoveCostFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MoveCostFE6ViewModel.cs` | 344 | `WriteMoveCost` | `rom.write_u8(moveCostAddr + 50, B50)` | class 'MoveCostFE6ViewModel' has no UndoService field/property/local |

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

### `SongInstrumentViewModel` — 46 callsites

| File | Line | Method | Write | Note |
|---|---:|---|---|---|
| `FEBuilderGBA.Avalonia/ViewModels/SongInstrumentViewModel.cs` | 375 | `Write` | `rom.write_u8(addr, HeaderByte)` | class 'SongInstrumentViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/SongInstrumentViewModel.cs` | 380 | `Write` | `rom.write_u8(addr + 1, 0)` | class 'SongInstrumentViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/SongInstrumentViewModel.cs` | 381 | `Write` | `rom.write_u8(addr + 2, 0)` | class 'SongInstrumentViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/SongInstrumentViewModel.cs` | 382 | `Write` | `rom.write_u8(addr + 3, 0)` | class 'SongInstrumentViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/SongInstrumentViewModel.cs` | 383 | `Write` | `rom.write_u32(addr + 4, WavePtr)` | class 'SongInstrumentViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/SongInstrumentViewModel.cs` | 384 | `Write` | `rom.write_u8(addr + 8, Attack)` | class 'SongInstrumentViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/SongInstrumentViewModel.cs` | 385 | `Write` | `rom.write_u8(addr + 9, Decay)` | class 'SongInstrumentViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/SongInstrumentViewModel.cs` | 386 | `Write` | `rom.write_u8(addr + 10, Sustain)` | class 'SongInstrumentViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/SongInstrumentViewModel.cs` | 387 | `Write` | `rom.write_u8(addr + 11, Release)` | class 'SongInstrumentViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/SongInstrumentViewModel.cs` | 391 | `Write` | `rom.write_u8(addr + 1, Sweep)` | class 'SongInstrumentViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/SongInstrumentViewModel.cs` | 392 | `Write` | `rom.write_u8(addr + 2, DutyLen)` | class 'SongInstrumentViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/SongInstrumentViewModel.cs` | 393 | `Write` | `rom.write_u8(addr + 3, EnvStep)` | class 'SongInstrumentViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/SongInstrumentViewModel.cs` | 394 | `Write` | `rom.write_u32(addr + 4, 0)` | class 'SongInstrumentViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/SongInstrumentViewModel.cs` | 395 | `Write` | `rom.write_u8(addr + 8, Attack)` | class 'SongInstrumentViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/SongInstrumentViewModel.cs` | 396 | `Write` | `rom.write_u8(addr + 9, Decay)` | class 'SongInstrumentViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/SongInstrumentViewModel.cs` | 397 | `Write` | `rom.write_u8(addr + 10, Sustain)` | class 'SongInstrumentViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/SongInstrumentViewModel.cs` | 398 | `Write` | `rom.write_u8(addr + 11, Release)` | class 'SongInstrumentViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/SongInstrumentViewModel.cs` | 402 | `Write` | `rom.write_u8(addr + 1, 0)` | class 'SongInstrumentViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/SongInstrumentViewModel.cs` | 403 | `Write` | `rom.write_u8(addr + 2, 0)` | class 'SongInstrumentViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/SongInstrumentViewModel.cs` | 404 | `Write` | `rom.write_u8(addr + 3, 0)` | class 'SongInstrumentViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/SongInstrumentViewModel.cs` | 405 | `Write` | `rom.write_u32(addr + 4, WavePtr)` | class 'SongInstrumentViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/SongInstrumentViewModel.cs` | 406 | `Write` | `rom.write_u8(addr + 8, Attack)` | class 'SongInstrumentViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/SongInstrumentViewModel.cs` | 407 | `Write` | `rom.write_u8(addr + 9, Decay)` | class 'SongInstrumentViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/SongInstrumentViewModel.cs` | 408 | `Write` | `rom.write_u8(addr + 10, Sustain)` | class 'SongInstrumentViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/SongInstrumentViewModel.cs` | 409 | `Write` | `rom.write_u8(addr + 11, Release)` | class 'SongInstrumentViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/SongInstrumentViewModel.cs` | 413 | `Write` | `rom.write_u8(addr + 1, 0)` | class 'SongInstrumentViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/SongInstrumentViewModel.cs` | 414 | `Write` | `rom.write_u8(addr + 2, 0)` | class 'SongInstrumentViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/SongInstrumentViewModel.cs` | 415 | `Write` | `rom.write_u8(addr + 3, 0)` | class 'SongInstrumentViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/SongInstrumentViewModel.cs` | 416 | `Write` | `rom.write_u8(addr + 4, Period)` | class 'SongInstrumentViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/SongInstrumentViewModel.cs` | 417 | `Write` | `rom.write_u8(addr + 5, 0)` | class 'SongInstrumentViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/SongInstrumentViewModel.cs` | 418 | `Write` | `rom.write_u8(addr + 6, 0)` | class 'SongInstrumentViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/SongInstrumentViewModel.cs` | 419 | `Write` | `rom.write_u8(addr + 7, 0)` | class 'SongInstrumentViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/SongInstrumentViewModel.cs` | 420 | `Write` | `rom.write_u8(addr + 8, Attack)` | class 'SongInstrumentViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/SongInstrumentViewModel.cs` | 421 | `Write` | `rom.write_u8(addr + 9, Decay)` | class 'SongInstrumentViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/SongInstrumentViewModel.cs` | 422 | `Write` | `rom.write_u8(addr + 10, Sustain)` | class 'SongInstrumentViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/SongInstrumentViewModel.cs` | 423 | `Write` | `rom.write_u8(addr + 11, Release)` | class 'SongInstrumentViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/SongInstrumentViewModel.cs` | 427 | `Write` | `rom.write_u8(addr + 1, 0)` | class 'SongInstrumentViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/SongInstrumentViewModel.cs` | 428 | `Write` | `rom.write_u8(addr + 2, 0)` | class 'SongInstrumentViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/SongInstrumentViewModel.cs` | 429 | `Write` | `rom.write_u8(addr + 3, 0)` | class 'SongInstrumentViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/SongInstrumentViewModel.cs` | 430 | `Write` | `rom.write_u32(addr + 4, KeyMapPtr)` | class 'SongInstrumentViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/SongInstrumentViewModel.cs` | 431 | `Write` | `rom.write_u32(addr + 8, SubInstrPtr)` | class 'SongInstrumentViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/SongInstrumentViewModel.cs` | 435 | `Write` | `rom.write_u8(addr + 1, 0)` | class 'SongInstrumentViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/SongInstrumentViewModel.cs` | 436 | `Write` | `rom.write_u8(addr + 2, 0)` | class 'SongInstrumentViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/SongInstrumentViewModel.cs` | 437 | `Write` | `rom.write_u8(addr + 3, 0)` | class 'SongInstrumentViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/SongInstrumentViewModel.cs` | 438 | `Write` | `rom.write_u32(addr + 4, SubInstrPtr)` | class 'SongInstrumentViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/SongInstrumentViewModel.cs` | 439 | `Write` | `rom.write_u32(addr + 8, 0)` | class 'SongInstrumentViewModel' has no UndoService field/property/local |

### `UnitEditorViewModel` — 46 callsites

| File | Line | Method | Write | Note |
|---|---:|---|---|---|
| `FEBuilderGBA.Avalonia/ViewModels/UnitEditorViewModel.cs` | 549 | `WriteUnit` | `rom.write_u16(addr + 0, NameId)` | class 'UnitEditorViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/UnitEditorViewModel.cs` | 550 | `WriteUnit` | `rom.write_u16(addr + 2, DescId)` | class 'UnitEditorViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/UnitEditorViewModel.cs` | 551 | `WriteUnit` | `rom.write_u8(addr + 4, UnitId)` | class 'UnitEditorViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/UnitEditorViewModel.cs` | 552 | `WriteUnit` | `rom.write_u8(addr + 5, ClassId)` | class 'UnitEditorViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/UnitEditorViewModel.cs` | 553 | `WriteUnit` | `rom.write_u16(addr + 6, PortraitId)` | class 'UnitEditorViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/UnitEditorViewModel.cs` | 554 | `WriteUnit` | `rom.write_u8(addr + 8, MapFace)` | class 'UnitEditorViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/UnitEditorViewModel.cs` | 555 | `WriteUnit` | `rom.write_u8(addr + 9, Affinity)` | class 'UnitEditorViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/UnitEditorViewModel.cs` | 556 | `WriteUnit` | `rom.write_u8(addr + 10, SortOrder)` | class 'UnitEditorViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/UnitEditorViewModel.cs` | 557 | `WriteUnit` | `rom.write_u8(addr + 11, Level)` | class 'UnitEditorViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/UnitEditorViewModel.cs` | 560 | `WriteUnit` | `rom.write_u8(addr + 12, (uint)(byte)HP)` | class 'UnitEditorViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/UnitEditorViewModel.cs` | 561 | `WriteUnit` | `rom.write_u8(addr + 13, (uint)(byte)Str)` | class 'UnitEditorViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/UnitEditorViewModel.cs` | 562 | `WriteUnit` | `rom.write_u8(addr + 14, (uint)(byte)Skl)` | class 'UnitEditorViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/UnitEditorViewModel.cs` | 563 | `WriteUnit` | `rom.write_u8(addr + 15, (uint)(byte)Spd)` | class 'UnitEditorViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/UnitEditorViewModel.cs` | 564 | `WriteUnit` | `rom.write_u8(addr + 16, (uint)(byte)Def)` | class 'UnitEditorViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/UnitEditorViewModel.cs` | 565 | `WriteUnit` | `rom.write_u8(addr + 17, (uint)(byte)Res)` | class 'UnitEditorViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/UnitEditorViewModel.cs` | 566 | `WriteUnit` | `rom.write_u8(addr + 18, (uint)(byte)Lck)` | class 'UnitEditorViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/UnitEditorViewModel.cs` | 567 | `WriteUnit` | `rom.write_u8(addr + 19, (uint)(byte)Con)` | class 'UnitEditorViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/UnitEditorViewModel.cs` | 570 | `WriteUnit` | `rom.write_u8(addr + 20, WepSword)` | class 'UnitEditorViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/UnitEditorViewModel.cs` | 571 | `WriteUnit` | `rom.write_u8(addr + 21, WepLance)` | class 'UnitEditorViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/UnitEditorViewModel.cs` | 572 | `WriteUnit` | `rom.write_u8(addr + 22, WepAxe)` | class 'UnitEditorViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/UnitEditorViewModel.cs` | 573 | `WriteUnit` | `rom.write_u8(addr + 23, WepBow)` | class 'UnitEditorViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/UnitEditorViewModel.cs` | 574 | `WriteUnit` | `rom.write_u8(addr + 24, WepStaff)` | class 'UnitEditorViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/UnitEditorViewModel.cs` | 575 | `WriteUnit` | `rom.write_u8(addr + 25, WepAnima)` | class 'UnitEditorViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/UnitEditorViewModel.cs` | 576 | `WriteUnit` | `rom.write_u8(addr + 26, WepLight)` | class 'UnitEditorViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/UnitEditorViewModel.cs` | 577 | `WriteUnit` | `rom.write_u8(addr + 27, WepDark)` | class 'UnitEditorViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/UnitEditorViewModel.cs` | 580 | `WriteUnit` | `rom.write_u8(addr + 28, GrowHP)` | class 'UnitEditorViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/UnitEditorViewModel.cs` | 581 | `WriteUnit` | `rom.write_u8(addr + 29, GrowStr)` | class 'UnitEditorViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/UnitEditorViewModel.cs` | 582 | `WriteUnit` | `rom.write_u8(addr + 30, GrowSkl)` | class 'UnitEditorViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/UnitEditorViewModel.cs` | 583 | `WriteUnit` | `rom.write_u8(addr + 31, GrowSpd)` | class 'UnitEditorViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/UnitEditorViewModel.cs` | 584 | `WriteUnit` | `rom.write_u8(addr + 32, GrowDef)` | class 'UnitEditorViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/UnitEditorViewModel.cs` | 585 | `WriteUnit` | `rom.write_u8(addr + 33, GrowRes)` | class 'UnitEditorViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/UnitEditorViewModel.cs` | 586 | `WriteUnit` | `rom.write_u8(addr + 34, GrowLck)` | class 'UnitEditorViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/UnitEditorViewModel.cs` | 589 | `WriteUnit` | `rom.write_u8(addr + 35, Unk35)` | class 'UnitEditorViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/UnitEditorViewModel.cs` | 590 | `WriteUnit` | `rom.write_u8(addr + 36, Unk36)` | class 'UnitEditorViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/UnitEditorViewModel.cs` | 591 | `WriteUnit` | `rom.write_u8(addr + 37, Unk37)` | class 'UnitEditorViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/UnitEditorViewModel.cs` | 592 | `WriteUnit` | `rom.write_u8(addr + 38, Unk38)` | class 'UnitEditorViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/UnitEditorViewModel.cs` | 593 | `WriteUnit` | `rom.write_u8(addr + 39, Unk39)` | class 'UnitEditorViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/UnitEditorViewModel.cs` | 596 | `WriteUnit` | `rom.write_u8(addr + 40, Ability1)` | class 'UnitEditorViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/UnitEditorViewModel.cs` | 597 | `WriteUnit` | `rom.write_u8(addr + 41, Ability2)` | class 'UnitEditorViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/UnitEditorViewModel.cs` | 598 | `WriteUnit` | `rom.write_u8(addr + 42, Ability3)` | class 'UnitEditorViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/UnitEditorViewModel.cs` | 599 | `WriteUnit` | `rom.write_u8(addr + 43, Ability4)` | class 'UnitEditorViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/UnitEditorViewModel.cs` | 602 | `WriteUnit` | `rom.write_u32(addr + 44, SupportPtr)` | class 'UnitEditorViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/UnitEditorViewModel.cs` | 607 | `WriteUnit` | `rom.write_u8(addr + 48, TalkGroup)` | class 'UnitEditorViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/UnitEditorViewModel.cs` | 608 | `WriteUnit` | `rom.write_u8(addr + 49, Unk49)` | class 'UnitEditorViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/UnitEditorViewModel.cs` | 609 | `WriteUnit` | `rom.write_u8(addr + 50, Unk50)` | class 'UnitEditorViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/UnitEditorViewModel.cs` | 610 | `WriteUnit` | `rom.write_u8(addr + 51, Unk51)` | class 'UnitEditorViewModel' has no UndoService field/property/local |

### `UnitFE7ViewModel` — 46 callsites

| File | Line | Method | Write | Note |
|---|---:|---|---|---|
| `FEBuilderGBA.Avalonia/ViewModels/UnitFE7ViewModel.cs` | 242 | `WriteUnit` | `rom.write_u16(addr + 0, (ushort)NameId)` | class 'UnitFE7ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/UnitFE7ViewModel.cs` | 243 | `WriteUnit` | `rom.write_u16(addr + 2, (ushort)DescId)` | class 'UnitFE7ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/UnitFE7ViewModel.cs` | 244 | `WriteUnit` | `rom.write_u8(addr + 4, (byte)UnitId)` | class 'UnitFE7ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/UnitFE7ViewModel.cs` | 245 | `WriteUnit` | `rom.write_u8(addr + 5, (byte)ClassId)` | class 'UnitFE7ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/UnitFE7ViewModel.cs` | 246 | `WriteUnit` | `rom.write_u16(addr + 6, (ushort)PortraitId)` | class 'UnitFE7ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/UnitFE7ViewModel.cs` | 247 | `WriteUnit` | `rom.write_u8(addr + 8, (byte)MapFace)` | class 'UnitFE7ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/UnitFE7ViewModel.cs` | 248 | `WriteUnit` | `rom.write_u8(addr + 9, (byte)Affinity)` | class 'UnitFE7ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/UnitFE7ViewModel.cs` | 249 | `WriteUnit` | `rom.write_u8(addr + 10, (byte)SortOrder)` | class 'UnitFE7ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/UnitFE7ViewModel.cs` | 250 | `WriteUnit` | `rom.write_u8(addr + 11, (byte)Level)` | class 'UnitFE7ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/UnitFE7ViewModel.cs` | 253 | `WriteUnit` | `rom.write_u8(addr + 12, (byte)(sbyte)HP)` | class 'UnitFE7ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/UnitFE7ViewModel.cs` | 254 | `WriteUnit` | `rom.write_u8(addr + 13, (byte)(sbyte)Str)` | class 'UnitFE7ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/UnitFE7ViewModel.cs` | 255 | `WriteUnit` | `rom.write_u8(addr + 14, (byte)(sbyte)Skl)` | class 'UnitFE7ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/UnitFE7ViewModel.cs` | 256 | `WriteUnit` | `rom.write_u8(addr + 15, (byte)(sbyte)Spd)` | class 'UnitFE7ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/UnitFE7ViewModel.cs` | 257 | `WriteUnit` | `rom.write_u8(addr + 16, (byte)(sbyte)Def)` | class 'UnitFE7ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/UnitFE7ViewModel.cs` | 258 | `WriteUnit` | `rom.write_u8(addr + 17, (byte)(sbyte)Res)` | class 'UnitFE7ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/UnitFE7ViewModel.cs` | 259 | `WriteUnit` | `rom.write_u8(addr + 18, (byte)(sbyte)Lck)` | class 'UnitFE7ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/UnitFE7ViewModel.cs` | 260 | `WriteUnit` | `rom.write_u8(addr + 19, (byte)(sbyte)Con)` | class 'UnitFE7ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/UnitFE7ViewModel.cs` | 263 | `WriteUnit` | `rom.write_u8(addr + 20, (byte)WepSword)` | class 'UnitFE7ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/UnitFE7ViewModel.cs` | 264 | `WriteUnit` | `rom.write_u8(addr + 21, (byte)WepLance)` | class 'UnitFE7ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/UnitFE7ViewModel.cs` | 265 | `WriteUnit` | `rom.write_u8(addr + 22, (byte)WepAxe)` | class 'UnitFE7ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/UnitFE7ViewModel.cs` | 266 | `WriteUnit` | `rom.write_u8(addr + 23, (byte)WepBow)` | class 'UnitFE7ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/UnitFE7ViewModel.cs` | 267 | `WriteUnit` | `rom.write_u8(addr + 24, (byte)WepStaff)` | class 'UnitFE7ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/UnitFE7ViewModel.cs` | 268 | `WriteUnit` | `rom.write_u8(addr + 25, (byte)WepAnima)` | class 'UnitFE7ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/UnitFE7ViewModel.cs` | 269 | `WriteUnit` | `rom.write_u8(addr + 26, (byte)WepLight)` | class 'UnitFE7ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/UnitFE7ViewModel.cs` | 270 | `WriteUnit` | `rom.write_u8(addr + 27, (byte)WepDark)` | class 'UnitFE7ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/UnitFE7ViewModel.cs` | 273 | `WriteUnit` | `rom.write_u8(addr + 28, (byte)GrowHP)` | class 'UnitFE7ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/UnitFE7ViewModel.cs` | 274 | `WriteUnit` | `rom.write_u8(addr + 29, (byte)GrowSTR)` | class 'UnitFE7ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/UnitFE7ViewModel.cs` | 275 | `WriteUnit` | `rom.write_u8(addr + 30, (byte)GrowSKL)` | class 'UnitFE7ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/UnitFE7ViewModel.cs` | 276 | `WriteUnit` | `rom.write_u8(addr + 31, (byte)GrowSPD)` | class 'UnitFE7ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/UnitFE7ViewModel.cs` | 277 | `WriteUnit` | `rom.write_u8(addr + 32, (byte)GrowDEF)` | class 'UnitFE7ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/UnitFE7ViewModel.cs` | 278 | `WriteUnit` | `rom.write_u8(addr + 33, (byte)GrowRES)` | class 'UnitFE7ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/UnitFE7ViewModel.cs` | 279 | `WriteUnit` | `rom.write_u8(addr + 34, (byte)GrowLCK)` | class 'UnitFE7ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/UnitFE7ViewModel.cs` | 282 | `WriteUnit` | `rom.write_u8(addr + 35, (byte)LowerClassPalette)` | class 'UnitFE7ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/UnitFE7ViewModel.cs` | 283 | `WriteUnit` | `rom.write_u8(addr + 36, (byte)UpperClassPalette)` | class 'UnitFE7ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/UnitFE7ViewModel.cs` | 284 | `WriteUnit` | `rom.write_u8(addr + 37, (byte)LowerClassAnime)` | class 'UnitFE7ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/UnitFE7ViewModel.cs` | 285 | `WriteUnit` | `rom.write_u8(addr + 38, (byte)UpperClassAnime)` | class 'UnitFE7ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/UnitFE7ViewModel.cs` | 288 | `WriteUnit` | `rom.write_u8(addr + 39, (byte)Unk39)` | class 'UnitFE7ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/UnitFE7ViewModel.cs` | 291 | `WriteUnit` | `rom.write_u8(addr + 40, (byte)Ability1)` | class 'UnitFE7ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/UnitFE7ViewModel.cs` | 292 | `WriteUnit` | `rom.write_u8(addr + 41, (byte)Ability2)` | class 'UnitFE7ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/UnitFE7ViewModel.cs` | 293 | `WriteUnit` | `rom.write_u8(addr + 42, (byte)Ability3)` | class 'UnitFE7ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/UnitFE7ViewModel.cs` | 294 | `WriteUnit` | `rom.write_u8(addr + 43, (byte)Ability4)` | class 'UnitFE7ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/UnitFE7ViewModel.cs` | 297 | `WriteUnit` | `rom.write_u32(addr + 44, SupportPtr)` | class 'UnitFE7ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/UnitFE7ViewModel.cs` | 300 | `WriteUnit` | `rom.write_u8(addr + 48, (byte)TalkGroup)` | class 'UnitFE7ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/UnitFE7ViewModel.cs` | 301 | `WriteUnit` | `rom.write_u8(addr + 49, (byte)Unk49)` | class 'UnitFE7ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/UnitFE7ViewModel.cs` | 302 | `WriteUnit` | `rom.write_u8(addr + 50, (byte)Unk50)` | class 'UnitFE7ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/UnitFE7ViewModel.cs` | 303 | `WriteUnit` | `rom.write_u8(addr + 51, (byte)Unk51)` | class 'UnitFE7ViewModel' has no UndoService field/property/local |

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

### `ItemEditorViewModel` — 26 callsites

| File | Line | Method | Write | Note |
|---|---:|---|---|---|
| `FEBuilderGBA.Avalonia/ViewModels/ItemEditorViewModel.cs` | 484 | `WriteItem` | `rom.write_u16(addr + 0, NameId)` | class 'ItemEditorViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ItemEditorViewModel.cs` | 485 | `WriteItem` | `rom.write_u16(addr + 2, DescId)` | class 'ItemEditorViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ItemEditorViewModel.cs` | 486 | `WriteItem` | `rom.write_u16(addr + 4, UseDescId)` | class 'ItemEditorViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ItemEditorViewModel.cs` | 487 | `WriteItem` | `rom.write_u8(addr + 6, ItemNumber)` | class 'ItemEditorViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ItemEditorViewModel.cs` | 488 | `WriteItem` | `rom.write_u8(addr + 7, WeaponType)` | class 'ItemEditorViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ItemEditorViewModel.cs` | 489 | `WriteItem` | `rom.write_u8(addr + 8, Trait1)` | class 'ItemEditorViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ItemEditorViewModel.cs` | 490 | `WriteItem` | `rom.write_u8(addr + 9, Trait2)` | class 'ItemEditorViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ItemEditorViewModel.cs` | 491 | `WriteItem` | `rom.write_u8(addr + 10, Trait3)` | class 'ItemEditorViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ItemEditorViewModel.cs` | 492 | `WriteItem` | `rom.write_u8(addr + 11, Trait4)` | class 'ItemEditorViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ItemEditorViewModel.cs` | 493 | `WriteItem` | `rom.write_u32(addr + 12, StatBonusesPtr)` | class 'ItemEditorViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ItemEditorViewModel.cs` | 494 | `WriteItem` | `rom.write_u32(addr + 16, EffectivenessPtr)` | class 'ItemEditorViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ItemEditorViewModel.cs` | 495 | `WriteItem` | `rom.write_u8(addr + 20, Uses)` | class 'ItemEditorViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ItemEditorViewModel.cs` | 496 | `WriteItem` | `rom.write_u8(addr + 21, Might)` | class 'ItemEditorViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ItemEditorViewModel.cs` | 497 | `WriteItem` | `rom.write_u8(addr + 22, Hit)` | class 'ItemEditorViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ItemEditorViewModel.cs` | 498 | `WriteItem` | `rom.write_u8(addr + 23, Weight)` | class 'ItemEditorViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ItemEditorViewModel.cs` | 499 | `WriteItem` | `rom.write_u8(addr + 24, Crit)` | class 'ItemEditorViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ItemEditorViewModel.cs` | 500 | `WriteItem` | `rom.write_u8(addr + 25, Range)` | class 'ItemEditorViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ItemEditorViewModel.cs` | 501 | `WriteItem` | `rom.write_u16(addr + 26, Price)` | class 'ItemEditorViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ItemEditorViewModel.cs` | 502 | `WriteItem` | `rom.write_u8(addr + 28, WeaponRank)` | class 'ItemEditorViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ItemEditorViewModel.cs` | 503 | `WriteItem` | `rom.write_u8(addr + 29, Icon)` | class 'ItemEditorViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ItemEditorViewModel.cs` | 504 | `WriteItem` | `rom.write_u8(addr + 30, UsageEffect)` | class 'ItemEditorViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ItemEditorViewModel.cs` | 505 | `WriteItem` | `rom.write_u8(addr + 31, DamageEffect)` | class 'ItemEditorViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ItemEditorViewModel.cs` | 506 | `WriteItem` | `rom.write_u8(addr + 32, WeaponExp)` | class 'ItemEditorViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ItemEditorViewModel.cs` | 510 | `WriteItem` | `rom.write_u8(addr + 33, Unk33)` | class 'ItemEditorViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ItemEditorViewModel.cs` | 511 | `WriteItem` | `rom.write_u8(addr + 34, Unk34)` | class 'ItemEditorViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ItemEditorViewModel.cs` | 512 | `WriteItem` | `rom.write_u8(addr + 35, Unk35)` | class 'ItemEditorViewModel' has no UndoService field/property/local |

### `ItemFE6ViewModel` — 22 callsites

| File | Line | Method | Write | Note |
|---|---:|---|---|---|
| `FEBuilderGBA.Avalonia/ViewModels/ItemFE6ViewModel.cs` | 178 | `WriteItem` | `rom.write_u16(addr + 0, NameId)` | class 'ItemFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ItemFE6ViewModel.cs` | 179 | `WriteItem` | `rom.write_u16(addr + 2, DescId)` | class 'ItemFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ItemFE6ViewModel.cs` | 180 | `WriteItem` | `rom.write_u16(addr + 4, UseDescId)` | class 'ItemFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ItemFE6ViewModel.cs` | 181 | `WriteItem` | `rom.write_u8(addr + 6, ItemNumber)` | class 'ItemFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ItemFE6ViewModel.cs` | 182 | `WriteItem` | `rom.write_u8(addr + 7, WeaponType)` | class 'ItemFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ItemFE6ViewModel.cs` | 183 | `WriteItem` | `rom.write_u8(addr + 8, Trait1)` | class 'ItemFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ItemFE6ViewModel.cs` | 184 | `WriteItem` | `rom.write_u8(addr + 9, Trait2)` | class 'ItemFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ItemFE6ViewModel.cs` | 185 | `WriteItem` | `rom.write_u8(addr + 10, Trait3)` | class 'ItemFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ItemFE6ViewModel.cs` | 186 | `WriteItem` | `rom.write_u8(addr + 11, Trait4)` | class 'ItemFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ItemFE6ViewModel.cs` | 187 | `WriteItem` | `rom.write_u32(addr + 12, StatBonusesPtr)` | class 'ItemFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ItemFE6ViewModel.cs` | 188 | `WriteItem` | `rom.write_u32(addr + 16, EffectivenessPtr)` | class 'ItemFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ItemFE6ViewModel.cs` | 189 | `WriteItem` | `rom.write_u8(addr + 20, Uses)` | class 'ItemFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ItemFE6ViewModel.cs` | 190 | `WriteItem` | `rom.write_u8(addr + 21, Might)` | class 'ItemFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ItemFE6ViewModel.cs` | 191 | `WriteItem` | `rom.write_u8(addr + 22, Hit)` | class 'ItemFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ItemFE6ViewModel.cs` | 192 | `WriteItem` | `rom.write_u8(addr + 23, Weight)` | class 'ItemFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ItemFE6ViewModel.cs` | 193 | `WriteItem` | `rom.write_u8(addr + 24, Crit)` | class 'ItemFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ItemFE6ViewModel.cs` | 194 | `WriteItem` | `rom.write_u8(addr + 25, Range)` | class 'ItemFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ItemFE6ViewModel.cs` | 195 | `WriteItem` | `rom.write_u16(addr + 26, Price)` | class 'ItemFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ItemFE6ViewModel.cs` | 196 | `WriteItem` | `rom.write_u8(addr + 28, WeaponRank)` | class 'ItemFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ItemFE6ViewModel.cs` | 197 | `WriteItem` | `rom.write_u8(addr + 29, Icon)` | class 'ItemFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ItemFE6ViewModel.cs` | 198 | `WriteItem` | `rom.write_u8(addr + 30, UsageEffect)` | class 'ItemFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ItemFE6ViewModel.cs` | 199 | `WriteItem` | `rom.write_u8(addr + 31, DamageEffect)` | class 'ItemFE6ViewModel' has no UndoService field/property/local |

### `EventCondViewModel` — 19 callsites

| File | Line | Method | Write | Note |
|---|---:|---|---|---|
| `FEBuilderGBA.Avalonia/ViewModels/EventCondViewModel.cs` | 432 | `WriteCondRecord` | `rom.write_u32(CondRecordAddr, EventPtr)` | class 'EventCondViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/EventCondViewModel.cs` | 441 | `WriteCondRecord` | `rom.write_u8(CondRecordAddr + 0, (byte)CondType)` | class 'EventCondViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/EventCondViewModel.cs` | 442 | `WriteCondRecord` | `rom.write_u8(CondRecordAddr + 1, (byte)SubType)` | class 'EventCondViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/EventCondViewModel.cs` | 443 | `WriteCondRecord` | `rom.write_u8(CondRecordAddr + 2, (byte)FlagId)` | class 'EventCondViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/EventCondViewModel.cs` | 444 | `WriteCondRecord` | `rom.write_u8(CondRecordAddr + 3, (byte)EventPtr)` | class 'EventCondViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/EventCondViewModel.cs` | 445 | `WriteCondRecord` | `rom.write_u8(CondRecordAddr + 4, (byte)ExtraB8)` | class 'EventCondViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/EventCondViewModel.cs` | 446 | `WriteCondRecord` | `rom.write_u8(CondRecordAddr + 5, (byte)ExtraB9)` | class 'EventCondViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/EventCondViewModel.cs` | 451 | `WriteCondRecord` | `rom.write_u8(CondRecordAddr + 0, (byte)CondType)` | class 'EventCondViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/EventCondViewModel.cs` | 452 | `WriteCondRecord` | `rom.write_u8(CondRecordAddr + 1, (byte)SubType)` | class 'EventCondViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/EventCondViewModel.cs` | 453 | `WriteCondRecord` | `rom.write_u16(CondRecordAddr + 2, (ushort)FlagId)` | class 'EventCondViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/EventCondViewModel.cs` | 454 | `WriteCondRecord` | `rom.write_u32(CondRecordAddr + 4, EventPtr)` | class 'EventCondViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/EventCondViewModel.cs` | 458 | `WriteCondRecord` | `rom.write_u8(CondRecordAddr + 8, (byte)ExtraB8)` | class 'EventCondViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/EventCondViewModel.cs` | 459 | `WriteCondRecord` | `rom.write_u8(CondRecordAddr + 9, (byte)ExtraB9)` | class 'EventCondViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/EventCondViewModel.cs` | 460 | `WriteCondRecord` | `rom.write_u8(CondRecordAddr + 10, (byte)ExtraB10)` | class 'EventCondViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/EventCondViewModel.cs` | 461 | `WriteCondRecord` | `rom.write_u8(CondRecordAddr + 11, (byte)ExtraB11)` | class 'EventCondViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/EventCondViewModel.cs` | 466 | `WriteCondRecord` | `rom.write_u8(CondRecordAddr + 12, (byte)ExtraB12)` | class 'EventCondViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/EventCondViewModel.cs` | 467 | `WriteCondRecord` | `rom.write_u8(CondRecordAddr + 13, (byte)ExtraB13)` | class 'EventCondViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/EventCondViewModel.cs` | 468 | `WriteCondRecord` | `rom.write_u8(CondRecordAddr + 14, (byte)ExtraB14)` | class 'EventCondViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/EventCondViewModel.cs` | 469 | `WriteCondRecord` | `rom.write_u8(CondRecordAddr + 15, (byte)ExtraB15)` | class 'EventCondViewModel' has no UndoService field/property/local |

### `BattleTerrainViewerViewModel` — 15 callsites

| File | Line | Method | Write | Note |
|---|---:|---|---|---|
| `FEBuilderGBA.Avalonia/ViewModels/BattleTerrainViewerViewModel.cs` | 121 | `WriteBattleTerrain` | `rom.write_u8(addr + 0, (byte)NameChar0)` | class 'BattleTerrainViewerViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/BattleTerrainViewerViewModel.cs` | 122 | `WriteBattleTerrain` | `rom.write_u8(addr + 1, (byte)NameChar1)` | class 'BattleTerrainViewerViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/BattleTerrainViewerViewModel.cs` | 123 | `WriteBattleTerrain` | `rom.write_u8(addr + 2, (byte)NameChar2)` | class 'BattleTerrainViewerViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/BattleTerrainViewerViewModel.cs` | 124 | `WriteBattleTerrain` | `rom.write_u8(addr + 3, (byte)NameChar3)` | class 'BattleTerrainViewerViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/BattleTerrainViewerViewModel.cs` | 125 | `WriteBattleTerrain` | `rom.write_u8(addr + 4, (byte)NameChar4)` | class 'BattleTerrainViewerViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/BattleTerrainViewerViewModel.cs` | 126 | `WriteBattleTerrain` | `rom.write_u8(addr + 5, (byte)NameChar5)` | class 'BattleTerrainViewerViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/BattleTerrainViewerViewModel.cs` | 127 | `WriteBattleTerrain` | `rom.write_u8(addr + 6, (byte)NameChar6)` | class 'BattleTerrainViewerViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/BattleTerrainViewerViewModel.cs` | 128 | `WriteBattleTerrain` | `rom.write_u8(addr + 7, (byte)NameChar7)` | class 'BattleTerrainViewerViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/BattleTerrainViewerViewModel.cs` | 129 | `WriteBattleTerrain` | `rom.write_u8(addr + 8, (byte)NameChar8)` | class 'BattleTerrainViewerViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/BattleTerrainViewerViewModel.cs` | 130 | `WriteBattleTerrain` | `rom.write_u8(addr + 9, (byte)NameChar9)` | class 'BattleTerrainViewerViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/BattleTerrainViewerViewModel.cs` | 131 | `WriteBattleTerrain` | `rom.write_u8(addr + 10, (byte)NameChar10)` | class 'BattleTerrainViewerViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/BattleTerrainViewerViewModel.cs` | 132 | `WriteBattleTerrain` | `rom.write_u8(addr + 11, (byte)NameChar11)` | class 'BattleTerrainViewerViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/BattleTerrainViewerViewModel.cs` | 133 | `WriteBattleTerrain` | `rom.write_u32(addr + 12, ImagePointer)` | class 'BattleTerrainViewerViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/BattleTerrainViewerViewModel.cs` | 134 | `WriteBattleTerrain` | `rom.write_u32(addr + 16, PalettePointer)` | class 'BattleTerrainViewerViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/BattleTerrainViewerViewModel.cs` | 135 | `WriteBattleTerrain` | `rom.write_u32(addr + 20, UnknownD20)` | class 'BattleTerrainViewerViewModel' has no UndoService field/property/local |

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

### `ImageUnitPaletteViewModel` — 13 callsites

| File | Line | Method | Write | Note |
|---|---:|---|---|---|
| `FEBuilderGBA.Avalonia/ViewModels/ImageUnitPaletteViewModel.cs` | 116 | `Write` | `rom.write_u8(addr + 0, Id0)` | class 'ImageUnitPaletteViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ImageUnitPaletteViewModel.cs` | 117 | `Write` | `rom.write_u8(addr + 1, Id1)` | class 'ImageUnitPaletteViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ImageUnitPaletteViewModel.cs` | 118 | `Write` | `rom.write_u8(addr + 2, Id2)` | class 'ImageUnitPaletteViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ImageUnitPaletteViewModel.cs` | 119 | `Write` | `rom.write_u8(addr + 3, Id3)` | class 'ImageUnitPaletteViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ImageUnitPaletteViewModel.cs` | 120 | `Write` | `rom.write_u8(addr + 4, Id4)` | class 'ImageUnitPaletteViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ImageUnitPaletteViewModel.cs` | 121 | `Write` | `rom.write_u8(addr + 5, Id5)` | class 'ImageUnitPaletteViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ImageUnitPaletteViewModel.cs` | 122 | `Write` | `rom.write_u8(addr + 6, Id6)` | class 'ImageUnitPaletteViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ImageUnitPaletteViewModel.cs` | 123 | `Write` | `rom.write_u8(addr + 7, Id7)` | class 'ImageUnitPaletteViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ImageUnitPaletteViewModel.cs` | 124 | `Write` | `rom.write_u8(addr + 8, Id8)` | class 'ImageUnitPaletteViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ImageUnitPaletteViewModel.cs` | 125 | `Write` | `rom.write_u8(addr + 9, Id9)` | class 'ImageUnitPaletteViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ImageUnitPaletteViewModel.cs` | 126 | `Write` | `rom.write_u8(addr + 10, Id10)` | class 'ImageUnitPaletteViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ImageUnitPaletteViewModel.cs` | 127 | `Write` | `rom.write_u8(addr + 11, Id11)` | class 'ImageUnitPaletteViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ImageUnitPaletteViewModel.cs` | 128 | `Write` | `rom.write_u32(addr + 12, PalettePointer)` | class 'ImageUnitPaletteViewModel' has no UndoService field/property/local |

### `PortraitViewerViewModel` — 13 callsites

| File | Line | Method | Write | Note |
|---|---:|---|---|---|
| `FEBuilderGBA.Avalonia/ViewModels/PortraitViewerViewModel.cs` | 111 | `WritePortrait` | `rom.write_u32(addr + 0, ImagePointer)` | class 'PortraitViewerViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/PortraitViewerViewModel.cs` | 112 | `WritePortrait` | `rom.write_u32(addr + 4, MapPointer)` | class 'PortraitViewerViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/PortraitViewerViewModel.cs` | 113 | `WritePortrait` | `rom.write_u32(addr + 8, PalettePointer)` | class 'PortraitViewerViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/PortraitViewerViewModel.cs` | 114 | `WritePortrait` | `rom.write_u32(addr + 12, MouthPointer)` | class 'PortraitViewerViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/PortraitViewerViewModel.cs` | 115 | `WritePortrait` | `rom.write_u32(addr + 16, ClassCardPointer)` | class 'PortraitViewerViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/PortraitViewerViewModel.cs` | 116 | `WritePortrait` | `rom.write_u8(addr + 20, (byte)MouthX)` | class 'PortraitViewerViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/PortraitViewerViewModel.cs` | 117 | `WritePortrait` | `rom.write_u8(addr + 21, (byte)MouthY)` | class 'PortraitViewerViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/PortraitViewerViewModel.cs` | 118 | `WritePortrait` | `rom.write_u8(addr + 22, (byte)EyeX)` | class 'PortraitViewerViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/PortraitViewerViewModel.cs` | 119 | `WritePortrait` | `rom.write_u8(addr + 23, (byte)EyeY)` | class 'PortraitViewerViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/PortraitViewerViewModel.cs` | 120 | `WritePortrait` | `rom.write_u8(addr + 24, (byte)State)` | class 'PortraitViewerViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/PortraitViewerViewModel.cs` | 121 | `WritePortrait` | `rom.write_u8(addr + 25, (byte)Padding25)` | class 'PortraitViewerViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/PortraitViewerViewModel.cs` | 122 | `WritePortrait` | `rom.write_u8(addr + 26, (byte)Padding26)` | class 'PortraitViewerViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/PortraitViewerViewModel.cs` | 123 | `WritePortrait` | `rom.write_u8(addr + 27, (byte)Padding27)` | class 'PortraitViewerViewModel' has no UndoService field/property/local |

### `TextDicViewModel` — 8 callsites

| File | Line | Method | Write | Note |
|---|---:|---|---|---|
| `FEBuilderGBA.Avalonia/ViewModels/TextDicViewModel.cs` | 153 | `Write` | `rom.write_u8(CurrentAddr + 0, (byte)TitleIndex)` | class 'TextDicViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/TextDicViewModel.cs` | 154 | `Write` | `rom.write_u8(CurrentAddr + 1, (byte)ChapterIndex)` | class 'TextDicViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/TextDicViewModel.cs` | 155 | `Write` | `rom.write_u16(CurrentAddr + 2, (ushort)TextId1)` | class 'TextDicViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/TextDicViewModel.cs` | 156 | `Write` | `rom.write_u16(CurrentAddr + 4, (ushort)TextId2)` | class 'TextDicViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/TextDicViewModel.cs` | 157 | `Write` | `rom.write_u16(CurrentAddr + 6, (ushort)Flag1)` | class 'TextDicViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/TextDicViewModel.cs` | 158 | `Write` | `rom.write_u16(CurrentAddr + 8, (ushort)Flag2)` | class 'TextDicViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/TextDicViewModel.cs` | 159 | `Write` | `rom.write_u8(CurrentAddr + 10, (byte)UnitId)` | class 'TextDicViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/TextDicViewModel.cs` | 160 | `Write` | `rom.write_u8(CurrentAddr + 11, (byte)ClassId)` | class 'TextDicViewModel' has no UndoService field/property/local |

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

### `ImagePortraitFE6ViewModel` — 7 callsites

| File | Line | Method | Write | Note |
|---|---:|---|---|---|
| `FEBuilderGBA.Avalonia/ViewModels/ImagePortraitFE6ViewModel.cs` | 102 | `Write` | `rom.write_u32(addr + 0, PortraitImagePtr)` | class 'ImagePortraitFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ImagePortraitFE6ViewModel.cs` | 103 | `Write` | `rom.write_u32(addr + 4, MiniPortraitPtr)` | class 'ImagePortraitFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ImagePortraitFE6ViewModel.cs` | 104 | `Write` | `rom.write_u32(addr + 8, PalettePtr)` | class 'ImagePortraitFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ImagePortraitFE6ViewModel.cs` | 105 | `Write` | `rom.write_u8(addr + 12, MouthX)` | class 'ImagePortraitFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ImagePortraitFE6ViewModel.cs` | 106 | `Write` | `rom.write_u8(addr + 13, MouthY)` | class 'ImagePortraitFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ImagePortraitFE6ViewModel.cs` | 107 | `Write` | `rom.write_u8(addr + 14, Unused14)` | class 'ImagePortraitFE6ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ImagePortraitFE6ViewModel.cs` | 108 | `Write` | `rom.write_u8(addr + 15, Unused15)` | class 'ImagePortraitFE6ViewModel' has no UndoService field/property/local |

### `MapChangeViewModel` — 7 callsites

| File | Line | Method | Write | Note |
|---|---:|---|---|---|
| `FEBuilderGBA.Avalonia/ViewModels/MapChangeViewModel.cs` | 96 | `WriteMapChange` | `rom.write_u32(CurrentAddr, ChangePointer)` | class 'MapChangeViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapChangeViewModel.cs` | 154 | `WriteChangeRecord` | `rom.write_u8(a + 0, RecChangeID)` | class 'MapChangeViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapChangeViewModel.cs` | 155 | `WriteChangeRecord` | `rom.write_u8(a + 1, RecX)` | class 'MapChangeViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapChangeViewModel.cs` | 156 | `WriteChangeRecord` | `rom.write_u8(a + 2, RecY)` | class 'MapChangeViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapChangeViewModel.cs` | 157 | `WriteChangeRecord` | `rom.write_u8(a + 3, RecWidth)` | class 'MapChangeViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapChangeViewModel.cs` | 158 | `WriteChangeRecord` | `rom.write_u8(a + 4, RecHeight)` | class 'MapChangeViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/MapChangeViewModel.cs` | 159 | `WriteChangeRecord` | `rom.write_u32(a + 8, RecTileDataPtr)` | class 'MapChangeViewModel' has no UndoService field/property/local |

### `ImageTSAAnime2ViewModel` — 5 callsites

| File | Line | Method | Write | Note |
|---|---:|---|---|---|
| `FEBuilderGBA.Avalonia/ViewModels/ImageTSAAnime2ViewModel.cs` | 88 | `Write` | `rom.write_u16(addr + 0, Unknown0)` | class 'ImageTSAAnime2ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ImageTSAAnime2ViewModel.cs` | 89 | `Write` | `rom.write_u16(addr + 2, Unknown2)` | class 'ImageTSAAnime2ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ImageTSAAnime2ViewModel.cs` | 90 | `Write` | `rom.write_u16(addr + 4, Unknown4)` | class 'ImageTSAAnime2ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ImageTSAAnime2ViewModel.cs` | 91 | `Write` | `rom.write_u16(addr + 6, Unknown6)` | class 'ImageTSAAnime2ViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ImageTSAAnime2ViewModel.cs` | 92 | `Write` | `rom.write_u32(addr + 8, TSAHeaderPointer)` | class 'ImageTSAAnime2ViewModel' has no UndoService field/property/local |

### `SongTrackViewModel` — 5 callsites

| File | Line | Method | Write | Note |
|---|---:|---|---|---|
| `FEBuilderGBA.Avalonia/ViewModels/SongTrackViewModel.cs` | 180 | `Write` | `rom.write_u8(addr + 0, (byte)TrackCount)` | class 'SongTrackViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/SongTrackViewModel.cs` | 181 | `Write` | `rom.write_u8(addr + 1, (byte)NumBlks)` | class 'SongTrackViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/SongTrackViewModel.cs` | 182 | `Write` | `rom.write_u8(addr + 2, (byte)Priority)` | class 'SongTrackViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/SongTrackViewModel.cs` | 183 | `Write` | `rom.write_u8(addr + 3, (byte)Reverb)` | class 'SongTrackViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/SongTrackViewModel.cs` | 184 | `Write` | `rom.write_u32(addr + 4, InstrumentAddr)` | class 'SongTrackViewModel' has no UndoService field/property/local |

### `TextViewerViewModel` — 5 callsites

| File | Line | Method | Write | Note |
|---|---:|---|---|---|
| `FEBuilderGBA.Avalonia/ViewModels/TextViewerViewModel.cs` | 403 | `WriteText` | `rom.write_u32(writePointer, text0Pointer)` | class 'TextViewerViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/TextViewerViewModel.cs` | 452 | `WriteText` | `rom.write_u32(writePointer, FETextEncode.ConvertPointerToUnHuffmanPatchPointer(U.toPointer(currentDataAddr)))` | class 'TextViewerViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/TextViewerViewModel.cs` | 454 | `WriteText` | `rom.write_u32(writePointer, U.toPointer(currentDataAddr))` | class 'TextViewerViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/TextViewerViewModel.cs` | 474 | `WriteText` | `rom.write_u32(writePointer, FETextEncode.ConvertPointerToUnHuffmanPatchPointer(U.toPointer(newAddr)))` | class 'TextViewerViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/TextViewerViewModel.cs` | 476 | `WriteText` | `rom.write_p32(writePointer, newAddr)` | class 'TextViewerViewModel' has no UndoService field/property/local |

### `BattleBGViewerViewModel` — 3 callsites

| File | Line | Method | Write | Note |
|---|---:|---|---|---|
| `FEBuilderGBA.Avalonia/ViewModels/BattleBGViewerViewModel.cs` | 62 | `WriteBattleBG` | `rom.write_u32(addr + 0, ImagePointer)` | class 'BattleBGViewerViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/BattleBGViewerViewModel.cs` | 63 | `WriteBattleBG` | `rom.write_u32(addr + 4, TSAPointer)` | class 'BattleBGViewerViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/BattleBGViewerViewModel.cs` | 64 | `WriteBattleBG` | `rom.write_u32(addr + 8, PalettePointer)` | class 'BattleBGViewerViewModel' has no UndoService field/property/local |

### `BigCGViewerViewModel` — 3 callsites

| File | Line | Method | Write | Note |
|---|---:|---|---|---|
| `FEBuilderGBA.Avalonia/ViewModels/BigCGViewerViewModel.cs` | 62 | `WriteBigCG` | `rom.write_u32(addr + 0, TablePointer)` | class 'BigCGViewerViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/BigCGViewerViewModel.cs` | 63 | `WriteBigCG` | `rom.write_u32(addr + 4, TSAPointer)` | class 'BigCGViewerViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/BigCGViewerViewModel.cs` | 64 | `WriteBigCG` | `rom.write_u32(addr + 8, PalettePointer)` | class 'BigCGViewerViewModel' has no UndoService field/property/local |

### `ChapterTitleViewerViewModel` — 3 callsites

| File | Line | Method | Write | Note |
|---|---:|---|---|---|
| `FEBuilderGBA.Avalonia/ViewModels/ChapterTitleViewerViewModel.cs` | 61 | `WriteChapterTitle` | `rom.write_u32(addr + 0, SaveImagePointer)` | class 'ChapterTitleViewerViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ChapterTitleViewerViewModel.cs` | 62 | `WriteChapterTitle` | `rom.write_u32(addr + 4, ChapterImagePointer)` | class 'ChapterTitleViewerViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ChapterTitleViewerViewModel.cs` | 63 | `WriteChapterTitle` | `rom.write_u32(addr + 8, TitleImagePointer)` | class 'ChapterTitleViewerViewModel' has no UndoService field/property/local |

### `ImageBattleAnimeViewModel` — 3 callsites

| File | Line | Method | Write | Note |
|---|---:|---|---|---|
| `FEBuilderGBA.Avalonia/ViewModels/ImageBattleAnimeViewModel.cs` | 454 | `Write` | `rom.write_u8(addr + 0, WeaponType)` | class 'ImageBattleAnimeViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ImageBattleAnimeViewModel.cs` | 455 | `Write` | `rom.write_u8(addr + 1, Special)` | class 'ImageBattleAnimeViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ImageBattleAnimeViewModel.cs` | 456 | `Write` | `rom.write_u16(addr + 2, AnimationNumber)` | class 'ImageBattleAnimeViewModel' has no UndoService field/property/local |

### `ImageBattleBGViewModel` — 3 callsites

| File | Line | Method | Write | Note |
|---|---:|---|---|---|
| `FEBuilderGBA.Avalonia/ViewModels/ImageBattleBGViewModel.cs` | 76 | `Write` | `rom.write_u32(addr + 0, ImagePointer)` | class 'ImageBattleBGViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ImageBattleBGViewModel.cs` | 77 | `Write` | `rom.write_u32(addr + 4, TSAPointer)` | class 'ImageBattleBGViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ImageBattleBGViewModel.cs` | 78 | `Write` | `rom.write_u32(addr + 8, PalettePointer)` | class 'ImageBattleBGViewModel' has no UndoService field/property/local |

### `ImageMapActionAnimationViewModel` — 3 callsites

| File | Line | Method | Write | Note |
|---|---:|---|---|---|
| `FEBuilderGBA.Avalonia/ViewModels/ImageMapActionAnimationViewModel.cs` | 111 | `Write` | `rom.write_u32(addr + 0, AnimationPointer)` | class 'ImageMapActionAnimationViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ImageMapActionAnimationViewModel.cs` | 112 | `Write` | `rom.write_u16(addr + 4, Padding1)` | class 'ImageMapActionAnimationViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/ImageMapActionAnimationViewModel.cs` | 113 | `Write` | `rom.write_u16(addr + 6, Padding2)` | class 'ImageMapActionAnimationViewModel' has no UndoService field/property/local |

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

### `SMEPromoListViewModel` — 2 callsites

| File | Line | Method | Write | Note |
|---|---:|---|---|---|
| `FEBuilderGBA.Avalonia/ViewModels/SMEPromoListViewModel.cs` | 134 | `WriteEntry` | `CoreState.ROM.write_u8(addr, (uint)B0)` | class 'SMEPromoListViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/SMEPromoListViewModel.cs` | 135 | `WriteEntry` | `CoreState.ROM.write_u8(addr + 1, (uint)B1)` | class 'SMEPromoListViewModel' has no UndoService field/property/local |

### `SongTableViewModel` — 2 callsites

| File | Line | Method | Write | Note |
|---|---:|---|---|---|
| `FEBuilderGBA.Avalonia/ViewModels/SongTableViewModel.cs` | 91 | `WriteSong` | `rom.write_u32(CurrentAddr + 0, SongHeaderPointer)` | class 'SongTableViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/SongTableViewModel.cs` | 92 | `WriteSong` | `rom.write_u32(CurrentAddr + 4, PlayerType)` | class 'SongTableViewModel' has no UndoService field/property/local |

### `TextCharCodeViewModel` — 2 callsites

| File | Line | Method | Write | Note |
|---|---:|---|---|---|
| `FEBuilderGBA.Avalonia/ViewModels/TextCharCodeViewModel.cs` | 139 | `Write` | `rom.write_u16(CurrentAddr + 0, (ushort)CharCode)` | class 'TextCharCodeViewModel' has no UndoService field/property/local |
| `FEBuilderGBA.Avalonia/ViewModels/TextCharCodeViewModel.cs` | 140 | `Write` | `rom.write_u16(CurrentAddr + 2, (ushort)TerminatorValue)` | class 'TextCharCodeViewModel' has no UndoService field/property/local |

### `AIASMCALLTALKViewModel` — 1 callsite

| File | Line | Method | Write | Note |
|---|---:|---|---|---|
| `FEBuilderGBA.Avalonia/ViewModels/AIASMCALLTALKViewModel.cs` | 71 | `Write` | `EditorFormRef.WriteFields(rom, addr, values, _fields)` | class 'AIASMCALLTALKViewModel' has no UndoService field/property/local |

### `AIASMCoordinateViewModel` — 1 callsite

| File | Line | Method | Write | Note |
|---|---:|---|---|---|
| `FEBuilderGBA.Avalonia/ViewModels/AIASMCoordinateViewModel.cs` | 71 | `Write` | `EditorFormRef.WriteFields(rom, addr, values, _fields)` | class 'AIASMCoordinateViewModel' has no UndoService field/property/local |

### `AIASMRangeViewModel` — 1 callsite

| File | Line | Method | Write | Note |
|---|---:|---|---|---|
| `FEBuilderGBA.Avalonia/ViewModels/AIASMRangeViewModel.cs` | 71 | `Write` | `EditorFormRef.WriteFields(rom, addr, values, _fields)` | class 'AIASMRangeViewModel' has no UndoService field/property/local |

### `AIMapSettingViewModel` — 1 callsite

| File | Line | Method | Write | Note |
|---|---:|---|---|---|
| `FEBuilderGBA.Avalonia/ViewModels/AIMapSettingViewModel.cs` | 80 | `Write` | `EditorFormRef.WriteFields(rom, addr, values, _fields)` | class 'AIMapSettingViewModel' has no UndoService field/property/local |

### `AIPerformItemViewModel` — 1 callsite

| File | Line | Method | Write | Note |
|---|---:|---|---|---|
| `FEBuilderGBA.Avalonia/ViewModels/AIPerformItemViewModel.cs` | 76 | `Write` | `EditorFormRef.WriteFields(rom, addr, values, _fields)` | class 'AIPerformItemViewModel' has no UndoService field/property/local |

### `AIPerformStaffViewModel` — 1 callsite

| File | Line | Method | Write | Note |
|---|---:|---|---|---|
| `FEBuilderGBA.Avalonia/ViewModels/AIPerformStaffViewModel.cs` | 76 | `Write` | `EditorFormRef.WriteFields(rom, addr, values, _fields)` | class 'AIPerformStaffViewModel' has no UndoService field/property/local |

### `AIStealItemViewModel` — 1 callsite

| File | Line | Method | Write | Note |
|---|---:|---|---|---|
| `FEBuilderGBA.Avalonia/ViewModels/AIStealItemViewModel.cs` | 73 | `Write` | `EditorFormRef.WriteFields(rom, addr, values, _fields)` | class 'AIStealItemViewModel' has no UndoService field/property/local |

### `AITargetViewModel` — 1 callsite

| File | Line | Method | Write | Note |
|---|---:|---|---|---|
| `FEBuilderGBA.Avalonia/ViewModels/AITargetViewModel.cs` | 146 | `Write` | `EditorFormRef.WriteFields(rom, addr, values, _fields)` | class 'AITargetViewModel' has no UndoService field/property/local |

### `AITilesViewModel` — 1 callsite

| File | Line | Method | Write | Note |
|---|---:|---|---|---|
| `FEBuilderGBA.Avalonia/ViewModels/AITilesViewModel.cs` | 58 | `Write` | `EditorFormRef.WriteFields(rom, addr, values, _fields)` | class 'AITilesViewModel' has no UndoService field/property/local |

### `AIUnitsViewModel` — 1 callsite

| File | Line | Method | Write | Note |
|---|---:|---|---|---|
| `FEBuilderGBA.Avalonia/ViewModels/AIUnitsViewModel.cs` | 63 | `Write` | `EditorFormRef.WriteFields(rom, addr, values, _fields)` | class 'AIUnitsViewModel' has no UndoService field/property/local |

### `AOERANGEViewModel` — 1 callsite

| File | Line | Method | Write | Note |
|---|---:|---|---|---|
| `FEBuilderGBA.Avalonia/ViewModels/AOERANGEViewModel.cs` | 65 | `Write` | `EditorFormRef.WriteFields(rom, addr, values, _fields)` | class 'AOERANGEViewModel' has no UndoService field/property/local |

### `ArenaClassViewerViewModel` — 1 callsite

| File | Line | Method | Write | Note |
|---|---:|---|---|---|
| `FEBuilderGBA.Avalonia/ViewModels/ArenaClassViewerViewModel.cs` | 82 | `WriteArenaClass` | `EditorFormRef.WriteFields(rom, addr, values, _fields)` | class 'ArenaClassViewerViewModel' has no UndoService field/property/local |

### `ArenaEnemyWeaponViewerViewModel` — 1 callsite

| File | Line | Method | Write | Note |
|---|---:|---|---|---|
| `FEBuilderGBA.Avalonia/ViewModels/ArenaEnemyWeaponViewerViewModel.cs` | 63 | `WriteArenaEnemyWeapon` | `EditorFormRef.WriteFields(rom, addr, values, _fields)` | class 'ArenaEnemyWeaponViewerViewModel' has no UndoService field/property/local |

### `CCBranchEditorViewModel` — 1 callsite

| File | Line | Method | Write | Note |
|---|---:|---|---|---|
| `FEBuilderGBA.Avalonia/ViewModels/CCBranchEditorViewModel.cs` | 150 | `WriteCCBranch` | `EditorFormRef.WriteFields(rom, CurrentAddr, values, _fields)` | class 'CCBranchEditorViewModel' has no UndoService field/property/local |

### `Command85PointerViewModel` — 1 callsite

| File | Line | Method | Write | Note |
|---|---:|---|---|---|
| `FEBuilderGBA.Avalonia/ViewModels/Command85PointerViewModel.cs` | 66 | `Write` | `EditorFormRef.WriteFields(rom, CurrentAddr, values, _fields)` | class 'Command85PointerViewModel' has no UndoService field/property/local |

### `EDSensekiCommentViewModel` — 1 callsite

| File | Line | Method | Write | Note |
|---|---:|---|---|---|
| `FEBuilderGBA.Avalonia/ViewModels/EDSensekiCommentViewModel.cs` | 79 | `WriteEntry` | `EditorFormRef.WriteFields(rom, CurrentAddr, values, _fields)` | class 'EDSensekiCommentViewModel' has no UndoService field/property/local |

### `EDStaffRollViewModel` — 1 callsite

| File | Line | Method | Write | Note |
|---|---:|---|---|---|
| `FEBuilderGBA.Avalonia/ViewModels/EDStaffRollViewModel.cs` | 71 | `WriteEDStaffRoll` | `EditorFormRef.WriteFields(rom, CurrentAddr, values, _fields)` | class 'EDStaffRollViewModel' has no UndoService field/property/local |

### `EDViewModel` — 1 callsite

| File | Line | Method | Write | Note |
|---|---:|---|---|---|
| `FEBuilderGBA.Avalonia/ViewModels/EDViewModel.cs` | 79 | `WriteED` | `EditorFormRef.WriteFields(rom, CurrentAddr, values, _fields)` | class 'EDViewModel' has no UndoService field/property/local |

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

### `EventUnitFE6ViewModel` — 1 callsite

| File | Line | Method | Write | Note |
|---|---:|---|---|---|
| `FEBuilderGBA.Avalonia/ViewModels/EventUnitFE6ViewModel.cs` | 156 | `WriteEntry` | `EditorFormRef.WriteFields(rom, addr, values, Fields)` | class 'EventUnitFE6ViewModel' has no UndoService field/property/local |

### `EventUnitFE7ViewModel` — 1 callsite

| File | Line | Method | Write | Note |
|---|---:|---|---|---|
| `FEBuilderGBA.Avalonia/ViewModels/EventUnitFE7ViewModel.cs` | 156 | `WriteEntry` | `EditorFormRef.WriteFields(rom, addr, values, Fields)` | class 'EventUnitFE7ViewModel' has no UndoService field/property/local |

### `EventUnitViewModel` — 1 callsite

| File | Line | Method | Write | Note |
|---|---:|---|---|---|
| `FEBuilderGBA.Avalonia/ViewModels/EventUnitViewModel.cs` | 174 | `WriteEntry` | `EditorFormRef.WriteFields(rom, addr, values, Fields)` | class 'EventUnitViewModel' has no UndoService field/property/local |

### `ExtraUnitFE8UViewModel` — 1 callsite

| File | Line | Method | Write | Note |
|---|---:|---|---|---|
| `FEBuilderGBA.Avalonia/ViewModels/ExtraUnitFE8UViewModel.cs` | 91 | `WriteEntry` | `EditorFormRef.WriteFields(rom, CurrentAddr, values, _fields)` | class 'ExtraUnitFE8UViewModel' has no UndoService field/property/local |

### `ExtraUnitViewModel` — 1 callsite

| File | Line | Method | Write | Note |
|---|---:|---|---|---|
| `FEBuilderGBA.Avalonia/ViewModels/ExtraUnitViewModel.cs` | 78 | `WriteEntry` | `EditorFormRef.WriteFields(rom, CurrentAddr, values, _fields)` | class 'ExtraUnitViewModel' has no UndoService field/property/local |

### `ImageChapterTitleFE7ViewModel` — 1 callsite

| File | Line | Method | Write | Note |
|---|---:|---|---|---|
| `FEBuilderGBA.Avalonia/ViewModels/ImageChapterTitleFE7ViewModel.cs` | 105 | `WriteEntry` | `rom.write_u32(CurrentAddr + 0, P0)` | class 'ImageChapterTitleFE7ViewModel' has no UndoService field/property/local |

### `ImageGenericEnemyPortraitViewModel` — 1 callsite

| File | Line | Method | Write | Note |
|---|---:|---|---|---|
| `FEBuilderGBA.Avalonia/ViewModels/ImageGenericEnemyPortraitViewModel.cs` | 63 | `Write` | `rom.write_u32(addr + 0, ImagePointer)` | class 'ImageGenericEnemyPortraitViewModel' has no UndoService field/property/local |

### `ImageSystemAreaViewModel` — 1 callsite

| File | Line | Method | Write | Note |
|---|---:|---|---|---|
| `FEBuilderGBA.Avalonia/ViewModels/ImageSystemAreaViewModel.cs` | 91 | `Write` | `rom.write_u16(addr + 0, GBAColor)` | class 'ImageSystemAreaViewModel' has no UndoService field/property/local |

### `ItemEffectPointerViewerViewModel` — 1 callsite

| File | Line | Method | Write | Note |
|---|---:|---|---|---|
| `FEBuilderGBA.Avalonia/ViewModels/ItemEffectPointerViewerViewModel.cs` | 66 | `WriteItemEffectPointer` | `EditorFormRef.WriteFields(rom, CurrentAddr, values, _fields)` | class 'ItemEffectPointerViewerViewModel' has no UndoService field/property/local |

### `ItemEffectivenessViewerViewModel` — 1 callsite

| File | Line | Method | Write | Note |
|---|---:|---|---|---|
| `FEBuilderGBA.Avalonia/ViewModels/ItemEffectivenessViewerViewModel.cs` | 69 | `WriteItemEffectiveness` | `EditorFormRef.WriteFields(rom, addr, values, _fields)` | class 'ItemEffectivenessViewerViewModel' has no UndoService field/property/local |

### `ItemPromotionViewerViewModel` — 1 callsite

| File | Line | Method | Write | Note |
|---|---:|---|---|---|
| `FEBuilderGBA.Avalonia/ViewModels/ItemPromotionViewerViewModel.cs` | 65 | `WriteItemPromotion` | `EditorFormRef.WriteFields(rom, CurrentAddr, values, _fields)` | class 'ItemPromotionViewerViewModel' has no UndoService field/property/local |

### `ItemRandomChestViewModel` — 1 callsite

| File | Line | Method | Write | Note |
|---|---:|---|---|---|
| `FEBuilderGBA.Avalonia/ViewModels/ItemRandomChestViewModel.cs` | 85 | `Write` | `EditorFormRef.WriteFields(rom, addr, values, _fields)` | class 'ItemRandomChestViewModel' has no UndoService field/property/local |

### `ItemShopViewerViewModel` — 1 callsite

| File | Line | Method | Write | Note |
|---|---:|---|---|---|
| `FEBuilderGBA.Avalonia/ViewModels/ItemShopViewerViewModel.cs` | 71 | `WriteItemShop` | `EditorFormRef.WriteFields(rom, CurrentAddr, values, _fields)` | class 'ItemShopViewerViewModel' has no UndoService field/property/local |

### `ItemStatBonusesSkillSystemsViewModel` — 1 callsite

| File | Line | Method | Write | Note |
|---|---:|---|---|---|
| `FEBuilderGBA.Avalonia/ViewModels/ItemStatBonusesSkillSystemsViewModel.cs` | 132 | `Write` | `EditorFormRef.WriteFields(rom, CurrentAddr, values, Fields)` | class 'ItemStatBonusesSkillSystemsViewModel' has no UndoService field/property/local |

### `ItemStatBonusesVennoViewModel` — 1 callsite

| File | Line | Method | Write | Note |
|---|---:|---|---|---|
| `FEBuilderGBA.Avalonia/ViewModels/ItemStatBonusesVennoViewModel.cs` | 116 | `Write` | `EditorFormRef.WriteFields(rom, CurrentAddr, values, Fields)` | class 'ItemStatBonusesVennoViewModel' has no UndoService field/property/local |

### `ItemStatBonusesViewerViewModel` — 1 callsite

| File | Line | Method | Write | Note |
|---|---:|---|---|---|
| `FEBuilderGBA.Avalonia/ViewModels/ItemStatBonusesViewerViewModel.cs` | 113 | `WriteItemStatBonuses` | `EditorFormRef.WriteFields(rom, addr, values, _fields)` | class 'ItemStatBonusesViewerViewModel' has no UndoService field/property/local |

### `ItemUsagePointerViewerViewModel` — 1 callsite

| File | Line | Method | Write | Note |
|---|---:|---|---|---|
| `FEBuilderGBA.Avalonia/ViewModels/ItemUsagePointerViewerViewModel.cs` | 74 | `WriteItemUsagePointer` | `EditorFormRef.WriteFields(rom, CurrentAddr, values, _fields)` | class 'ItemUsagePointerViewerViewModel' has no UndoService field/property/local |

### `ItemWeaponEffectViewerViewModel` — 1 callsite

| File | Line | Method | Write | Note |
|---|---:|---|---|---|
| `FEBuilderGBA.Avalonia/ViewModels/ItemWeaponEffectViewerViewModel.cs` | 103 | `WriteItemWeaponEffect` | `EditorFormRef.WriteFields(rom, CurrentAddr, values, _fields)` | class 'ItemWeaponEffectViewerViewModel' has no UndoService field/property/local |

### `ItemWeaponTriangleViewerViewModel` — 1 callsite

| File | Line | Method | Write | Note |
|---|---:|---|---|---|
| `FEBuilderGBA.Avalonia/ViewModels/ItemWeaponTriangleViewerViewModel.cs` | 85 | `WriteItemWeaponTriangle` | `EditorFormRef.WriteFields(rom, addr, values, _fields)` | class 'ItemWeaponTriangleViewerViewModel' has no UndoService field/property/local |

### `LinkArenaDenyUnitViewerViewModel` — 1 callsite

| File | Line | Method | Write | Note |
|---|---:|---|---|---|
| `FEBuilderGBA.Avalonia/ViewModels/LinkArenaDenyUnitViewerViewModel.cs` | 65 | `WriteLinkArenaDenyUnit` | `EditorFormRef.WriteFields(rom, addr, values, _fields)` | class 'LinkArenaDenyUnitViewerViewModel' has no UndoService field/property/local |

### `MapEditorViewModel` — 1 callsite

| File | Line | Method | Write | Note |
|---|---:|---|---|---|
| `FEBuilderGBA.Avalonia/ViewModels/MapEditorViewModel.cs` | 362 | `WriteTile` | `rom.write_p32(_cachedMapPointerEntryAddr, writeAddr)` | class 'MapEditorViewModel' has no UndoService field/property/local |

### `MapExitPointViewModel` — 1 callsite

| File | Line | Method | Write | Note |
|---|---:|---|---|---|
| `FEBuilderGBA.Avalonia/ViewModels/MapExitPointViewModel.cs` | 71 | `WriteMapExitPoint` | `EditorFormRef.WriteFields(rom, CurrentAddr, values, _fields)` | class 'MapExitPointViewModel' has no UndoService field/property/local |

### `MapLoadFunctionViewModel` — 1 callsite

| File | Line | Method | Write | Note |
|---|---:|---|---|---|
| `FEBuilderGBA.Avalonia/ViewModels/MapLoadFunctionViewModel.cs` | 78 | `WriteEntry` | `EditorFormRef.WriteFields(rom, CurrentAddr, values, _fields)` | class 'MapLoadFunctionViewModel' has no UndoService field/property/local |

### `MapPointerViewModel` — 1 callsite

| File | Line | Method | Write | Note |
|---|---:|---|---|---|
| `FEBuilderGBA.Avalonia/ViewModels/MapPointerViewModel.cs` | 96 | `WriteMapPointer` | `rom.write_u32(CurrentAddr, MapDataPointer)` | class 'MapPointerViewModel' has no UndoService field/property/local |

### `MapStyleEditorViewModel` — 1 callsite

| File | Line | Method | Write | Note |
|---|---:|---|---|---|
| `FEBuilderGBA.Avalonia/ViewModels/MapStyleEditorViewModel.cs` | 82 | `Write` | `rom.write_u32(CurrentAddr, ObjPointer)` | class 'MapStyleEditorViewModel' has no UndoService field/property/local |

### `MapTerrainBGLookupTableViewModel` — 1 callsite

| File | Line | Method | Write | Note |
|---|---:|---|---|---|
| `FEBuilderGBA.Avalonia/ViewModels/MapTerrainBGLookupTableViewModel.cs` | 64 | `Write` | `EditorFormRef.WriteFields(rom, CurrentAddr, values, _fields)` | class 'MapTerrainBGLookupTableViewModel' has no UndoService field/property/local |

### `MapTerrainFloorLookupTableViewModel` — 1 callsite

| File | Line | Method | Write | Note |
|---|---:|---|---|---|
| `FEBuilderGBA.Avalonia/ViewModels/MapTerrainFloorLookupTableViewModel.cs` | 64 | `Write` | `EditorFormRef.WriteFields(rom, CurrentAddr, values, _fields)` | class 'MapTerrainFloorLookupTableViewModel' has no UndoService field/property/local |

### `MapTerrainNameEngViewModel` — 1 callsite

| File | Line | Method | Write | Note |
|---|---:|---|---|---|
| `FEBuilderGBA.Avalonia/ViewModels/MapTerrainNameEngViewModel.cs` | 63 | `Write` | `EditorFormRef.WriteFields(rom, CurrentAddr, values, _fields)` | class 'MapTerrainNameEngViewModel' has no UndoService field/property/local |

### `MapTerrainNameViewModel` — 1 callsite

| File | Line | Method | Write | Note |
|---|---:|---|---|---|
| `FEBuilderGBA.Avalonia/ViewModels/MapTerrainNameViewModel.cs` | 93 | `Write` | `rom.write_u32(CurrentAddr, TerrainNamePointer)` | class 'MapTerrainNameViewModel' has no UndoService field/property/local |

### `MapTileAnimation1ViewModel` — 1 callsite

| File | Line | Method | Write | Note |
|---|---:|---|---|---|
| `FEBuilderGBA.Avalonia/ViewModels/MapTileAnimation1ViewModel.cs` | 86 | `Write` | `EditorFormRef.WriteFields(rom, CurrentAddr, values, _fields)` | class 'MapTileAnimation1ViewModel' has no UndoService field/property/local |

### `MapTileAnimation2ViewModel` — 1 callsite

| File | Line | Method | Write | Note |
|---|---:|---|---|---|
| `FEBuilderGBA.Avalonia/ViewModels/MapTileAnimation2ViewModel.cs` | 117 | `Write` | `EditorFormRef.WriteFields(rom, CurrentAddr, values, _fields)` | class 'MapTileAnimation2ViewModel' has no UndoService field/property/local |

### `MapTileAnimationViewModel` — 1 callsite

| File | Line | Method | Write | Note |
|---|---:|---|---|---|
| `FEBuilderGBA.Avalonia/ViewModels/MapTileAnimationViewModel.cs` | 111 | `WriteMapTileAnimation` | `EditorFormRef.WriteFields(rom, addr, values, _fields)` | class 'MapTileAnimationViewModel' has no UndoService field/property/local |

### `MenuCommandViewModel` — 1 callsite

| File | Line | Method | Write | Note |
|---|---:|---|---|---|
| `FEBuilderGBA.Avalonia/ViewModels/MenuCommandViewModel.cs` | 130 | `WriteMenuCommand` | `EditorFormRef.WriteFields(rom, addr, values, _fields)` | class 'MenuCommandViewModel' has no UndoService field/property/local |

### `MenuDefinitionViewModel` — 1 callsite

| File | Line | Method | Write | Note |
|---|---:|---|---|---|
| `FEBuilderGBA.Avalonia/ViewModels/MenuDefinitionViewModel.cs` | 98 | `WriteMenuDefinition` | `EditorFormRef.WriteFields(rom, addr, values, _fields)` | class 'MenuDefinitionViewModel' has no UndoService field/property/local |

### `MenuExtendSplitMenuViewModel` — 1 callsite

| File | Line | Method | Write | Note |
|---|---:|---|---|---|
| `FEBuilderGBA.Avalonia/ViewModels/MenuExtendSplitMenuViewModel.cs` | 97 | `Write` | `EditorFormRef.WriteFields(rom, addr, values, _fields)` | class 'MenuExtendSplitMenuViewModel' has no UndoService field/property/local |

### `MonsterItemViewerViewModel` — 1 callsite

| File | Line | Method | Write | Note |
|---|---:|---|---|---|
| `FEBuilderGBA.Avalonia/ViewModels/MonsterItemViewerViewModel.cs` | 79 | `WriteMonsterItem` | `EditorFormRef.WriteFields(rom, CurrentAddr, values, _fields)` | class 'MonsterItemViewerViewModel' has no UndoService field/property/local |

### `MonsterProbabilityViewerViewModel` — 1 callsite

| File | Line | Method | Write | Note |
|---|---:|---|---|---|
| `FEBuilderGBA.Avalonia/ViewModels/MonsterProbabilityViewerViewModel.cs` | 95 | `WriteMonsterProbability` | `EditorFormRef.WriteFields(rom, CurrentAddr, values, _fields)` | class 'MonsterProbabilityViewerViewModel' has no UndoService field/property/local |

### `MonsterWMapProbabilityViewerViewModel` — 1 callsite

| File | Line | Method | Write | Note |
|---|---:|---|---|---|
| `FEBuilderGBA.Avalonia/ViewModels/MonsterWMapProbabilityViewerViewModel.cs` | 63 | `WriteMonsterWMapProbability` | `EditorFormRef.WriteFields(rom, CurrentAddr, values, _fields)` | class 'MonsterWMapProbabilityViewerViewModel' has no UndoService field/property/local |

### `MoveCostEditorViewModel` — 1 callsite

| File | Line | Method | Write | Note |
|---|---:|---|---|---|
| `FEBuilderGBA.Avalonia/ViewModels/MoveCostEditorViewModel.cs` | 417 | `WriteMoveCost` | `rom.write_u8((uint)(addr + i), GetCost(i))` | class 'MoveCostEditorViewModel' has no UndoService field/property/local |

### `OPClassAlphaNameFE6ViewModel` — 1 callsite

| File | Line | Method | Write | Note |
|---|---:|---|---|---|
| `FEBuilderGBA.Avalonia/ViewModels/OPClassAlphaNameFE6ViewModel.cs` | 117 | `WriteEntry` | `EditorFormRef.WriteFields(rom, CurrentAddr, values, _fields)` | class 'OPClassAlphaNameFE6ViewModel' has no UndoService field/property/local |

### `OPClassAlphaNameViewModel` — 1 callsite

| File | Line | Method | Write | Note |
|---|---:|---|---|---|
| `FEBuilderGBA.Avalonia/ViewModels/OPClassAlphaNameViewModel.cs` | 109 | `WriteEntry` | `rom.write_u8((uint)(addr + i), b)` | class 'OPClassAlphaNameViewModel' has no UndoService field/property/local |

### `OPClassDemoFE7UViewModel` — 1 callsite

| File | Line | Method | Write | Note |
|---|---:|---|---|---|
| `FEBuilderGBA.Avalonia/ViewModels/OPClassDemoFE7UViewModel.cs` | 145 | `WriteEntry` | `EditorFormRef.WriteFields(rom, CurrentAddr, values, _fields)` | class 'OPClassDemoFE7UViewModel' has no UndoService field/property/local |

### `OPClassDemoFE7ViewModel` — 1 callsite

| File | Line | Method | Write | Note |
|---|---:|---|---|---|
| `FEBuilderGBA.Avalonia/ViewModels/OPClassDemoFE7ViewModel.cs` | 159 | `WriteEntry` | `EditorFormRef.WriteFields(rom, CurrentAddr, values, _fields)` | class 'OPClassDemoFE7ViewModel' has no UndoService field/property/local |

### `OPClassDemoFE8UViewModel` — 1 callsite

| File | Line | Method | Write | Note |
|---|---:|---|---|---|
| `FEBuilderGBA.Avalonia/ViewModels/OPClassDemoFE8UViewModel.cs` | 132 | `WriteEntry` | `EditorFormRef.WriteFields(rom, CurrentAddr, values, _fields)` | class 'OPClassDemoFE8UViewModel' has no UndoService field/property/local |

### `OPClassDemoViewerViewModel` — 1 callsite

| File | Line | Method | Write | Note |
|---|---:|---|---|---|
| `FEBuilderGBA.Avalonia/ViewModels/OPClassDemoViewerViewModel.cs` | 108 | `WriteOPClassDemo` | `EditorFormRef.WriteFields(rom, CurrentAddr, values, _fields)` | class 'OPClassDemoViewerViewModel' has no UndoService field/property/local |

### `OPClassFontFE8UViewModel` — 1 callsite

| File | Line | Method | Write | Note |
|---|---:|---|---|---|
| `FEBuilderGBA.Avalonia/ViewModels/OPClassFontFE8UViewModel.cs` | 127 | `WriteEntry` | `EditorFormRef.WriteFields(rom, CurrentAddr, values, _fields)` | class 'OPClassFontFE8UViewModel' has no UndoService field/property/local |

### `OPClassFontViewerViewModel` — 1 callsite

| File | Line | Method | Write | Note |
|---|---:|---|---|---|
| `FEBuilderGBA.Avalonia/ViewModels/OPClassFontViewerViewModel.cs` | 109 | `WriteOPClassFont` | `EditorFormRef.WriteFields(rom, CurrentAddr, values, _fields)` | class 'OPClassFontViewerViewModel' has no UndoService field/property/local |

### `OPPrologueViewerViewModel` — 1 callsite

| File | Line | Method | Write | Note |
|---|---:|---|---|---|
| `FEBuilderGBA.Avalonia/ViewModels/OPPrologueViewerViewModel.cs` | 139 | `WriteOPPrologue` | `EditorFormRef.WriteFields(rom, CurrentAddr, values, _fields)` | class 'OPPrologueViewerViewModel' has no UndoService field/property/local |

### `PointerToolViewModel` — 1 callsite

| File | Line | Method | Write | Note |
|---|---:|---|---|---|
| `FEBuilderGBA.Avalonia/ViewModels/PointerToolViewModel.cs` | 221 | `WritePointerValue` | `rom.write_u32(addr, writeVal)` | class 'PointerToolViewModel' has no UndoService field/property/local |

### `SkillAssignmentClassCSkillSysViewViewModel` — 1 callsite

| File | Line | Method | Write | Note |
|---|---:|---|---|---|
| `FEBuilderGBA.Avalonia/ViewModels/SkillAssignmentClassCSkillSysViewViewModel.cs` | 43 | `Write` | `EditorFormRef.WriteFields(rom, addr, values, _fields)` | class 'SkillAssignmentClassCSkillSysViewViewModel' has no UndoService field/property/local |

### `SkillAssignmentClassSkillSystemViewModel` — 1 callsite

| File | Line | Method | Write | Note |
|---|---:|---|---|---|
| `FEBuilderGBA.Avalonia/ViewModels/SkillAssignmentClassSkillSystemViewModel.cs` | 50 | `Write` | `EditorFormRef.WriteFields(rom, addr, values, _fields)` | class 'SkillAssignmentClassSkillSystemViewModel' has no UndoService field/property/local |

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

### `SkillConfigSkillSystemViewModel` — 1 callsite

| File | Line | Method | Write | Note |
|---|---:|---|---|---|
| `FEBuilderGBA.Avalonia/ViewModels/SkillConfigSkillSystemViewModel.cs` | 50 | `Write` | `EditorFormRef.WriteFields(rom, addr, values, _fields)` | class 'SkillConfigSkillSystemViewModel' has no UndoService field/property/local |

### `SkillSystemsEffectivenessReworkClassTypeViewViewModel` — 1 callsite

| File | Line | Method | Write | Note |
|---|---:|---|---|---|
| `FEBuilderGBA.Avalonia/ViewModels/SkillSystemsEffectivenessReworkClassTypeViewViewModel.cs` | 43 | `Write` | `EditorFormRef.WriteFields(rom, addr, values, _fields)` | class 'SkillSystemsEffectivenessReworkClassTypeViewViewModel' has no UndoService field/property/local |

### `SomeClassListViewModel` — 1 callsite

| File | Line | Method | Write | Note |
|---|---:|---|---|---|
| `FEBuilderGBA.Avalonia/ViewModels/SomeClassListViewModel.cs` | 63 | `WriteEntry` | `EditorFormRef.WriteFields(rom, CurrentAddr, values, _fields)` | class 'SomeClassListViewModel' has no UndoService field/property/local |

### `SongInstrumentDirectSoundViewModel` — 1 callsite

| File | Line | Method | Write | Note |
|---|---:|---|---|---|
| `FEBuilderGBA.Avalonia/ViewModels/SongInstrumentDirectSoundViewModel.cs` | 126 | `Write` | `EditorFormRef.WriteFields(rom, CurrentAddr, values, _fields)` | class 'SongInstrumentDirectSoundViewModel' has no UndoService field/property/local |

### `SoundBossBGMViewerViewModel` — 1 callsite

| File | Line | Method | Write | Note |
|---|---:|---|---|---|
| `FEBuilderGBA.Avalonia/ViewModels/SoundBossBGMViewerViewModel.cs` | 83 | `WriteSoundBossBGM` | `EditorFormRef.WriteFields(rom, CurrentAddr, values, _fields)` | class 'SoundBossBGMViewerViewModel' has no UndoService field/property/local |

### `SoundFootStepsViewerViewModel` — 1 callsite

| File | Line | Method | Write | Note |
|---|---:|---|---|---|
| `FEBuilderGBA.Avalonia/ViewModels/SoundFootStepsViewerViewModel.cs` | 74 | `WriteSoundFootSteps` | `EditorFormRef.WriteFields(rom, CurrentAddr, values, _fields)` | class 'SoundFootStepsViewerViewModel' has no UndoService field/property/local |

### `SoundRoomCGViewModel` — 1 callsite

| File | Line | Method | Write | Note |
|---|---:|---|---|---|
| `FEBuilderGBA.Avalonia/ViewModels/SoundRoomCGViewModel.cs` | 69 | `Write` | `EditorFormRef.WriteFields(rom, CurrentAddr, values, _fields)` | class 'SoundRoomCGViewModel' has no UndoService field/property/local |

### `SoundRoomFE6ViewModel` — 1 callsite

| File | Line | Method | Write | Note |
|---|---:|---|---|---|
| `FEBuilderGBA.Avalonia/ViewModels/SoundRoomFE6ViewModel.cs` | 114 | `Write` | `EditorFormRef.WriteFields(rom, CurrentAddr, values, _fields)` | class 'SoundRoomFE6ViewModel' has no UndoService field/property/local |

### `SoundRoomViewerViewModel` — 1 callsite

| File | Line | Method | Write | Note |
|---|---:|---|---|---|
| `FEBuilderGBA.Avalonia/ViewModels/SoundRoomViewerViewModel.cs` | 93 | `WriteSoundRoom` | `EditorFormRef.WriteFields(rom, CurrentAddr, values, fields)` | class 'SoundRoomViewerViewModel' has no UndoService field/property/local |

### `StatusOptionOrderViewModel` — 1 callsite

| File | Line | Method | Write | Note |
|---|---:|---|---|---|
| `FEBuilderGBA.Avalonia/ViewModels/StatusOptionOrderViewModel.cs` | 75 | `WriteStatusOptionOrder` | `EditorFormRef.WriteFields(rom, CurrentAddr, values, _fields)` | class 'StatusOptionOrderViewModel' has no UndoService field/property/local |

### `StatusOptionViewModel` — 1 callsite

| File | Line | Method | Write | Note |
|---|---:|---|---|---|
| `FEBuilderGBA.Avalonia/ViewModels/StatusOptionViewModel.cs` | 142 | `Write` | `EditorFormRef.WriteFields(rom, addr, values, _fields)` | class 'StatusOptionViewModel' has no UndoService field/property/local |

### `StatusParamViewModel` — 1 callsite

| File | Line | Method | Write | Note |
|---|---:|---|---|---|
| `FEBuilderGBA.Avalonia/ViewModels/StatusParamViewModel.cs` | 146 | `WriteStatusParam` | `EditorFormRef.WriteFields(rom, addr, values, _fields)` | class 'StatusParamViewModel' has no UndoService field/property/local |

### `StatusRMenuViewModel` — 1 callsite

| File | Line | Method | Write | Note |
|---|---:|---|---|---|
| `FEBuilderGBA.Avalonia/ViewModels/StatusRMenuViewModel.cs` | 103 | `WriteStatusRMenu` | `EditorFormRef.WriteFields(rom, addr, values, _fields)` | class 'StatusRMenuViewModel' has no UndoService field/property/local |

### `StatusUnitsMenuViewModel` — 1 callsite

| File | Line | Method | Write | Note |
|---|---:|---|---|---|
| `FEBuilderGBA.Avalonia/ViewModels/StatusUnitsMenuViewModel.cs` | 78 | `WriteStatusUnitsMenu` | `EditorFormRef.WriteFields(rom, addr, values, _fields)` | class 'StatusUnitsMenuViewModel' has no UndoService field/property/local |

### `SummonUnitViewerViewModel` — 1 callsite

| File | Line | Method | Write | Note |
|---|---:|---|---|---|
| `FEBuilderGBA.Avalonia/ViewModels/SummonUnitViewerViewModel.cs` | 72 | `WriteSummonUnit` | `EditorFormRef.WriteFields(rom, CurrentAddr, values, _fields)` | class 'SummonUnitViewerViewModel' has no UndoService field/property/local |

### `SummonsDemonKingViewerViewModel` — 1 callsite

| File | Line | Method | Write | Note |
|---|---:|---|---|---|
| `FEBuilderGBA.Avalonia/ViewModels/SummonsDemonKingViewerViewModel.cs` | 141 | `WriteSummonsDemonKing` | `EditorFormRef.WriteFields(rom, CurrentAddr, values, _fields)` | class 'SummonsDemonKingViewerViewModel' has no UndoService field/property/local |

### `SupportAttributeViewModel` — 1 callsite

| File | Line | Method | Write | Note |
|---|---:|---|---|---|
| `FEBuilderGBA.Avalonia/ViewModels/SupportAttributeViewModel.cs` | 98 | `WriteSupportAttribute` | `EditorFormRef.WriteFields(rom, CurrentAddr, values, _fields)` | class 'SupportAttributeViewModel' has no UndoService field/property/local |

### `SupportTalkFE6ViewModel` — 1 callsite

| File | Line | Method | Write | Note |
|---|---:|---|---|---|
| `FEBuilderGBA.Avalonia/ViewModels/SupportTalkFE6ViewModel.cs` | 99 | `WriteSupportTalk` | `EditorFormRef.WriteFields(rom, a, values, _fields)` | class 'SupportTalkFE6ViewModel' has no UndoService field/property/local |

### `SupportTalkFE7ViewModel` — 1 callsite

| File | Line | Method | Write | Note |
|---|---:|---|---|---|
| `FEBuilderGBA.Avalonia/ViewModels/SupportTalkFE7ViewModel.cs` | 103 | `WriteSupportTalk` | `EditorFormRef.WriteFields(rom, a, values, _fields)` | class 'SupportTalkFE7ViewModel' has no UndoService field/property/local |

### `SupportTalkViewModel` — 1 callsite

| File | Line | Method | Write | Note |
|---|---:|---|---|---|
| `FEBuilderGBA.Avalonia/ViewModels/SupportTalkViewModel.cs` | 101 | `WriteSupportTalk` | `EditorFormRef.WriteFields(rom, a, values, _fields)` | class 'SupportTalkViewModel' has no UndoService field/property/local |

### `SupportUnitEditorViewModel` — 1 callsite

| File | Line | Method | Write | Note |
|---|---:|---|---|---|
| `FEBuilderGBA.Avalonia/ViewModels/SupportUnitEditorViewModel.cs` | 168 | `WriteSupportUnit` | `EditorFormRef.WriteFields(rom, a, values, _fields)` | class 'SupportUnitEditorViewModel' has no UndoService field/property/local |

### `SupportUnitFE6ViewModel` — 1 callsite

| File | Line | Method | Write | Note |
|---|---:|---|---|---|
| `FEBuilderGBA.Avalonia/ViewModels/SupportUnitFE6ViewModel.cs` | 182 | `WriteSupportUnit` | `EditorFormRef.WriteFields(rom, a, values, _fields)` | class 'SupportUnitFE6ViewModel' has no UndoService field/property/local |

### `TacticianAffinityFE7ViewModel` — 1 callsite

| File | Line | Method | Write | Note |
|---|---:|---|---|---|
| `FEBuilderGBA.Avalonia/ViewModels/TacticianAffinityFE7ViewModel.cs` | 93 | `Write` | `EditorFormRef.WriteFields(rom, CurrentAddr, values, _fields)` | class 'TacticianAffinityFE7ViewModel' has no UndoService field/property/local |

### `TerrainNameEditorViewModel` — 1 callsite

| File | Line | Method | Write | Note |
|---|---:|---|---|---|
| `FEBuilderGBA.Avalonia/ViewModels/TerrainNameEditorViewModel.cs` | 65 | `WriteTerrainName` | `rom.write_u16(CurrentAddr, TextId)` | class 'TerrainNameEditorViewModel' has no UndoService field/property/local |

### `UnitActionPointerViewModel` — 1 callsite

| File | Line | Method | Write | Note |
|---|---:|---|---|---|
| `FEBuilderGBA.Avalonia/ViewModels/UnitActionPointerViewModel.cs` | 82 | `WriteEntry` | `EditorFormRef.WriteFields(rom, CurrentAddr, values, _fields)` | class 'UnitActionPointerViewModel' has no UndoService field/property/local |

### `UnitCustomBattleAnimeViewModel` — 1 callsite

| File | Line | Method | Write | Note |
|---|---:|---|---|---|
| `FEBuilderGBA.Avalonia/ViewModels/UnitCustomBattleAnimeViewModel.cs` | 91 | `WriteEntry` | `EditorFormRef.WriteFields(rom, addr, values, _fields)` | class 'UnitCustomBattleAnimeViewModel' has no UndoService field/property/local |

### `UnitIncreaseHeightViewModel` — 1 callsite

| File | Line | Method | Write | Note |
|---|---:|---|---|---|
| `FEBuilderGBA.Avalonia/ViewModels/UnitIncreaseHeightViewModel.cs` | 115 | `WriteEntry` | `EditorFormRef.WriteFields(rom, CurrentAddr, values, _fields)` | class 'UnitIncreaseHeightViewModel' has no UndoService field/property/local |

### `UnitPaletteViewModel` — 1 callsite

| File | Line | Method | Write | Note |
|---|---:|---|---|---|
| `FEBuilderGBA.Avalonia/ViewModels/UnitPaletteViewModel.cs` | 93 | `WriteEntry` | `EditorFormRef.WriteFields(rom, addr, values, _fields)` | class 'UnitPaletteViewModel' has no UndoService field/property/local |

### `UnitsShortTextViewModel` — 1 callsite

| File | Line | Method | Write | Note |
|---|---:|---|---|---|
| `FEBuilderGBA.Avalonia/ViewModels/UnitsShortTextViewModel.cs` | 74 | `WriteEntry` | `rom.write_u16(CurrentAddr + 0, (ushort)TextId)` | class 'UnitsShortTextViewModel' has no UndoService field/property/local |

### `VennouWeaponLockViewModel` — 1 callsite

| File | Line | Method | Write | Note |
|---|---:|---|---|---|
| `FEBuilderGBA.Avalonia/ViewModels/VennouWeaponLockViewModel.cs` | 145 | `WriteEntry` | `EditorFormRef.WriteFields(rom, CurrentAddr, values, _fields)` | class 'VennouWeaponLockViewModel' has no UndoService field/property/local |

### `WorldMapBGMViewModel` — 1 callsite

| File | Line | Method | Write | Note |
|---|---:|---|---|---|
| `FEBuilderGBA.Avalonia/ViewModels/WorldMapBGMViewModel.cs` | 75 | `WriteWorldMapBGM` | `EditorFormRef.WriteFields(rom, CurrentAddr, values, _fields)` | class 'WorldMapBGMViewModel' has no UndoService field/property/local |

### `WorldMapEventPointerViewModel` — 1 callsite

| File | Line | Method | Write | Note |
|---|---:|---|---|---|
| `FEBuilderGBA.Avalonia/ViewModels/WorldMapEventPointerViewModel.cs` | 66 | `WriteWorldMapEvent` | `EditorFormRef.WriteFields(rom, CurrentAddr, values, _fields)` | class 'WorldMapEventPointerViewModel' has no UndoService field/property/local |

### `WorldMapPathMoveEditorViewModel` — 1 callsite

| File | Line | Method | Write | Note |
|---|---:|---|---|---|
| `FEBuilderGBA.Avalonia/ViewModels/WorldMapPathMoveEditorViewModel.cs` | 96 | `Write` | `EditorFormRef.WriteFields(rom, addr, values, _fields)` | class 'WorldMapPathMoveEditorViewModel' has no UndoService field/property/local |

### `WorldMapPathViewModel` — 1 callsite

| File | Line | Method | Write | Note |
|---|---:|---|---|---|
| `FEBuilderGBA.Avalonia/ViewModels/WorldMapPathViewModel.cs` | 94 | `Write` | `EditorFormRef.WriteFields(rom, addr, values, _fields)` | class 'WorldMapPathViewModel' has no UndoService field/property/local |

### `WorldMapPointViewModel` — 1 callsite

| File | Line | Method | Write | Note |
|---|---:|---|---|---|
| `FEBuilderGBA.Avalonia/ViewModels/WorldMapPointViewModel.cs` | 162 | `Write` | `EditorFormRef.WriteFields(rom, addr, values, _fields)` | class 'WorldMapPointViewModel' has no UndoService field/property/local |

## Missing scope — VMs that have UndoService but skip Begin/Commit on this write

These classes already carry UndoService plumbing but a particular write was added without wrapping it. The fix is local: add `_undoService.Begin("...")` before the write and `_undoService.Commit()` after.

_None._

## Ambiguous — covered by caller, please verify

The write lives in a helper method; a caller in the same class wraps Begin/Commit. The scanner uses a one-level name-match heuristic, so manual review is required to confirm the helper is actually called from within an active scope at runtime.

_None._

## Covered (healthy)

`13` callsites are inside a Begin/Commit (or Begin/Rollback) scope in the same method body, OR pass an explicit Undo argument. Covered classes: `EventScriptPopupViewModel` (6), `ImagePortraitView` (4), `BigCGViewerView` (2), `OPPrologueViewerView` (1).

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

Classes with at least one detected write: 165.

Writable VMs (matching the triplet convention): 139.  
Writable VMs with zero detected ROM writes: 0.

_No writable VM is missing from the scanner output — pattern coverage is healthy._
