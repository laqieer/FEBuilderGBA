# Nightmare module coverage inventory

Issue #2114 records an offline, pinned inventory for `laqieer/Fire-Emblem-Nightmare-Modules` at commit `e854861a7c16dadd3f2ea26be4b8c3d71c0e9c10`. The inventory uses GitHub tree path metadata only; module file contents are not cloned, executed, built, or applied.

## Counts and hash

- FE6 Nightmare Modules: 102 `.nmm` paths
- FE7 Nightmare Modules: 373 `.nmm` paths
- FE8 Nightmare modules: 280 `.nmm` paths
- Total: 755 `.nmm` paths
- Path-list SHA-256: `a8019f8688da5766c740e4620a662914670fc21a002a0803c28f90cfe0661c6a`

The hash is computed over the sorted path list joined with LF and a trailing LF. The full inert manifest is `FEBuilderGBA.Avalonia.Tests/TestData/nightmare-module-coverage.json`; tests read it offline.

## Dispositions

- `existing-avalonia-surface`: already covered by an existing Avalonia/catalog editor or cross-platform scanner.
- `integrated-surface`: covered by issue #2114 implementation work.
- `patch-manager`: hard-code tuning/randomizer-oriented tables covered by Patch Manager or hard-code surfaces.
- `explicit-exclusion`: prototype or duplicate beta modules that are not authoritative coverage targets.
- `missing`: no FEBuilderGBA surface identified. The pinned manifest contains zero `missing` paths.

## Family mapping

The manifest maps each path exactly once to one collapsed family: animation, arena, audio, battle-screen, beta, chapter-unit, character-class, event, graphics, hardcode-tuning, item, map, menu, promotion, quote, shop, support, or summon when present.

## Triangle attack quote decisions

- `FE6 Nightmare Modules/Quote editor/Triangle attack quote editor.nmm` is the only genuine gap. It is now integrated into `EventBattleTalkFE6` as a direct fixed table at `0x666528`, 6 records, stride 16, writable B0 Character, B1 Chapter, W4 Text ID, and B8 Event/flag byte.
- FE7 triangle paths, including `FE7 Nightmare Modules/Quote Editors/Triangle Attack Convo Editor.nmm`, map to the existing `EventBattleTalkFE7` quote surface (4 records, stride 12, direct `0xC9F130`).
