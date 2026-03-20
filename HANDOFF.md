# Handoff: PR #176 — FEditor .bin Import + Battle Animation Export

## Status
- PR #176 is OPEN: https://github.com/laqieer/FEBuilderGBA/pull/176
- Branch: feat/cli-bin-import-export-124
- Copilot CLI reviewed with blockers (11 unresolved threads)
- CI: build passes all platforms, E2E pending

## Blockers to Fix (from Copilot review)

### Critical:
1. **Export uses tile sheets instead of OAM-composed frames** — BattleAnimeExportCore.RenderFrameFromData() calls RenderTileSheet() instead of using BattleAnimeCompositionCore.RenderBattleAnimeFrame(). Fix: use the composition API.

2. **Multi-sheet .bin import broken** — UpdateFrameDataAddresses() always maps to first sheet. Fix: track which sheet each frame references and map correctly.

3. **Palette 4-team recolor missing** — All 4 palette slots get the same player palette. Fix: implement basic recoloring or at minimum preserve the FEditor palette data as-is.

### Non-blocking:
4. Missing E2E help test in CliArgsE2ETests.cs
5. Various code quality items (11 threads total)

## How to Fix

### For export composition:
Read BattleAnimeCompositionCore.RenderBattleAnimeFrame() — it takes ROM + record pointers + section/frame indices. For export, iterate through all sections and frames, calling this API for each unique frame.

### For multi-sheet .bin:
The FEditor .bin frame data already contains graphics pointers. When reading the .dmp file, scan for 0x86 commands to find which graphics pointer maps to which sheet index. Build a mapping dict, then use it in UpdateFrameDataAddresses.

### For palette:
If the FEditor .bin palette data is 0x80 bytes (128 = 4 teams × 16 colors × 2 bytes), use it as-is. Only generate the single-team palette when importing from .txt.

## Files to modify
- FEBuilderGBA.Core/BattleAnimeExportCore.cs — fix RenderFrameFromData
- FEBuilderGBA.Core/BattleAnimeImportCore.cs — fix UpdateFrameDataAddresses + palette
- FEBuilderGBA.E2ETests/Tests/CliArgsE2ETests.cs — add help text test

## After fixes
1. Push fixes as new commit
2. Resolve all 11 review threads
3. Wait for new Copilot bot review, resolve new threads
4. Re-trigger Copilot CLI review
5. Wait for CI green
6. Merge
7. git checkout master && git pull
