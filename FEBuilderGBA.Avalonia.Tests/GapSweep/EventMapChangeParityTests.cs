// SPDX-License-Identifier: GPL-3.0-or-later
// Phase 7 gap-sweep regression tests for EventMapChangeView (#423).
//
// Covers the 53 gaps the issue called out: 33 missing controls (density)
// + 20 missing labels. EventMapChangeForm has no JumpForm callsites, so
// no INavigationTargetSource manifest entries are required.
using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using FEBuilderGBA.Avalonia.GapSweep;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests.GapSweep;

/// <summary>
/// Tests proving the EventMapChange parity raise (#423) is permanent.
/// Marked [Collection("SharedState")] because the tests mutate
/// CoreState.ROM and CoreState.CommentCache.
/// </summary>
[Collection("SharedState")]
public class EventMapChangeParityTests
{
    // -----------------------------------------------------------------
    // Density (Phase 1) — AV control count must reach the MEDIUM verdict.
    // -----------------------------------------------------------------

    /// <summary>
    /// WF Designer.cs has 36 controls. The MEDIUM threshold is 75% × 36 = 27.
    /// </summary>
    [Fact]
    public void View_AvControlCount_AtOrAboveMediumVerdict()
    {
        string repoRoot = FindRepoRoot();
        string axamlPath = Path.Combine(repoRoot, "FEBuilderGBA.Avalonia", "Views",
            "EventMapChangeView.axaml");
        Assert.True(File.Exists(axamlPath), $"AXAML not found at {axamlPath}");

        var doc = XDocument.Load(axamlPath);
        int avCount = ControlDensityScanner.CountAvControlsInDocument(doc);

        const int WfControlCount = 36;
        int mediumThreshold = (int)Math.Ceiling(WfControlCount * 0.75); // 27
        Assert.True(avCount >= mediumThreshold,
            $"AV control count {avCount} must be >= {mediumThreshold} (75% of WF={WfControlCount})");
    }

    // -----------------------------------------------------------------
    // View structural checks — AutomationIds the gap-fix surfaces.
    // -----------------------------------------------------------------

    [Fact]
    public void View_AxamlContains_RequiredAutomationIds()
    {
        string repoRoot = FindRepoRoot();
        string axamlPath = Path.Combine(repoRoot, "FEBuilderGBA.Avalonia", "Views",
            "EventMapChangeView.axaml");
        string content = File.ReadAllText(axamlPath);

        string[] required = {
            // Read-config bar
            // #668: NUD-based ReadStart/ReadCount inputs migrated to
            // read-only EditorTopBar slots; *_Input ids renamed to
            // *_Label.
            "EventMapChange_ReadStart_Label",
            "EventMapChange_ReadCount_Label",
            "EventMapChange_ReloadList_Button",
            // Address panel
            "EventMapChange_BlockSize_Input",
            "EventMapChange_SelectedAddr_Input",
            "EventMapChange_Write_Button",
            // Byte fields
            "EventMapChange_B0_Input",
            "EventMapChange_B1_Input",
            "EventMapChange_B2_Input",
            "EventMapChange_B3_Input",
            "EventMapChange_B4_Input",
            "EventMapChange_B5_Input",
            "EventMapChange_B6_Input",
            "EventMapChange_B7_Input",
            "EventMapChange_P8_Input",
            // Comment + supplemental
            "EventMapChange_Comment_Input",
            "EventMapChange_MapPicture_Image",
            "EventMapChange_MapList_List",
            // Pointer-import + list-expand stubs
            "EventMapChange_PointerImport_Button",
            "EventMapChange_ListExpands_Button",
            // Backward compat — preserve the existing IDs.
            "EventMapChange_Entry_List",
            "EventMapChange_Addr_Label",
        };
        foreach (var id in required)
        {
            Assert.Contains(id, content);
        }
    }

    // -----------------------------------------------------------------
    // ViewModel — LoadMapList delegates to MapSettingCore.
    // -----------------------------------------------------------------

    [Fact]
    public void ViewModel_LoadMapList_WithNoRom_ReturnsEmpty()
    {
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = null;
            var vm = new EventMapChangeViewModel();
            var list = vm.LoadMapList();
            Assert.NotNull(list);
            Assert.Empty(list);
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void ViewModel_LoadMapList_WithSyntheticFe8u_ReturnsAtLeastOneMap()
    {
        var prevRom = CoreState.ROM;
        try
        {
            ROM rom = MakeSyntheticFe8uRomWithOneMapAndChangeData(
                mapChangePlist: 3,
                changeDataOffset: 0x00900000u);
            CoreState.ROM = rom;
            var vm = new EventMapChangeViewModel();
            var list = vm.LoadMapList();
            Assert.NotEmpty(list);
        }
        finally { CoreState.ROM = prevRom; }
    }

    // -----------------------------------------------------------------
    // ViewModel — LoadEntryForMap drives map → plist → addr chain.
    // -----------------------------------------------------------------

    [Fact]
    public void ViewModel_LoadEntryForMap_ResolvesChangeDataAddress()
    {
        var prevRom = CoreState.ROM;
        try
        {
            uint expectedAddr = 0x00900000u;
            ROM rom = MakeSyntheticFe8uRomWithOneMapAndChangeData(
                mapChangePlist: 3,
                changeDataOffset: expectedAddr);
            CoreState.ROM = rom;

            var vm = new EventMapChangeViewModel();
            bool ok = vm.LoadEntryForMap(0u);
            Assert.True(ok);
            Assert.Equal(expectedAddr, vm.CurrentAddr);
            Assert.True(vm.IsLoaded);
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void ViewModel_LoadEntryForMap_PlistFF_ReturnsFalse()
    {
        var prevRom = CoreState.ROM;
        try
        {
            ROM rom = MakeSyntheticFe8uRomWithOneMapAndChangeData(
                mapChangePlist: 0xFF,
                changeDataOffset: 0u);
            CoreState.ROM = rom;

            var vm = new EventMapChangeViewModel();
            bool ok = vm.LoadEntryForMap(0u);
            Assert.False(ok);
        }
        finally { CoreState.ROM = prevRom; }
    }

    // -----------------------------------------------------------------
    // ViewModel — P8 is pointer-aware (rom.p32 / rom.write_p32).
    // Copilot CLI v1 blocking concern #1: the field must round-trip
    // through pointer semantics, NOT raw u32. If P8 is treated as raw
    // u32, a value like 0x08123456 reads back unchanged but a value
    // 0x00123456 reads back as 0x08123456 (rebased on read). The test
    // round-trips a real GBA pointer.
    // -----------------------------------------------------------------

    [Fact]
    public void ViewModel_P8_RoundTripsAsPointer()
    {
        var prevRom = CoreState.ROM;
        try
        {
            uint changeAddr = 0x00900000u;
            ROM rom = MakeSyntheticFe8uRomWithOneMapAndChangeData(
                mapChangePlist: 3,
                changeDataOffset: changeAddr);
            CoreState.ROM = rom;

            var vm = new EventMapChangeViewModel();
            bool ok = vm.LoadEntryForMap(0u);
            Assert.True(ok);

            uint expectedPointer = 0x08123456u;
            // Plant a pointer value directly at addr+8 so LoadEventMapChange
            // reads it via p32 (which strips the 0x08000000 prefix).
            rom.write_u32(changeAddr + 8, expectedPointer);
            vm.LoadEventMapChange(changeAddr);
            // P8 must be stored as the rebased offset (0x00123456), since
            // rom.p32 strips the 0x08000000 base. WriteEntry must put back
            // 0x08123456 (re-applying the base via rom.write_p32).
            Assert.Equal(0x00123456u, vm.P8);

            // Verify WriteEntry round-trips. Write a new P8 value, call
            // WriteEntry, then read raw bytes back to assert the GBA
            // pointer prefix is correctly re-applied.
            vm.P8 = 0x00234567u;
            vm.WriteEntry();
            uint raw = rom.u32(changeAddr + 8);
            Assert.Equal(0x08234567u, raw);
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void ViewModel_WriteEntry_WritesB0ThroughP8()
    {
        var prevRom = CoreState.ROM;
        try
        {
            uint changeAddr = 0x00900000u;
            ROM rom = MakeSyntheticFe8uRomWithOneMapAndChangeData(
                mapChangePlist: 3,
                changeDataOffset: changeAddr);
            CoreState.ROM = rom;

            var vm = new EventMapChangeViewModel();
            vm.LoadEntryForMap(0u);

            vm.B0 = 0xAA;
            vm.B1 = 0xBB;
            vm.B2 = 0xCC;
            vm.B3 = 0x05;
            vm.B4 = 0x06;
            vm.B5 = 0x11;
            vm.B6 = 0x22;
            vm.B7 = 0x33;
            vm.P8 = 0x00345678u;
            vm.WriteEntry();

            Assert.Equal(0xAAu, rom.u8(changeAddr + 0));
            Assert.Equal(0xBBu, rom.u8(changeAddr + 1));
            Assert.Equal(0xCCu, rom.u8(changeAddr + 2));
            Assert.Equal(0x05u, rom.u8(changeAddr + 3));
            Assert.Equal(0x06u, rom.u8(changeAddr + 4));
            Assert.Equal(0x11u, rom.u8(changeAddr + 5));
            Assert.Equal(0x22u, rom.u8(changeAddr + 6));
            Assert.Equal(0x33u, rom.u8(changeAddr + 7));
            Assert.Equal(0x08345678u, rom.u32(changeAddr + 8));
        }
        finally { CoreState.ROM = prevRom; }
    }

    // -----------------------------------------------------------------
    // Stale-state regression (Copilot CLI re-review on issue #423):
    // selecting a map with no change-data after a valid map must NOT
    // leave the VM holding the previous entry's CurrentAddr / IsLoaded.
    // Otherwise a subsequent Write would corrupt the previous entry.
    // -----------------------------------------------------------------

    [Fact]
    public void ViewModel_LoadEntryForMap_FailureClearsVmState()
    {
        var prevRom = CoreState.ROM;
        try
        {
            // 1. Load a valid map with change-data.
            uint validAddr = 0x00900000u;
            ROM rom = MakeSyntheticFe8uRomWithOneMapAndChangeData(
                mapChangePlist: 3, changeDataOffset: validAddr);
            CoreState.ROM = rom;

            var vm = new EventMapChangeViewModel();
            bool ok = vm.LoadEntryForMap(0u);
            Assert.True(ok, "Precondition: valid map should load");
            Assert.Equal(validAddr, vm.CurrentAddr);
            Assert.True(vm.IsLoaded);

            // 2. Re-plant the per-map plist byte as 0xFF (no change-data)
            //    and re-call LoadEntryForMap for the same map.
            rom.Data[(int)0x00800000u + 11] = 0xFF;
            bool ok2 = vm.LoadEntryForMap(0u);
            Assert.False(ok2);

            // 3. The VM must have been reset — no stale CurrentAddr that
            //    a stray WriteEntry would write zeros to.
            Assert.Equal(0u, vm.CurrentAddr);
            Assert.False(vm.IsLoaded);

            // 4. WriteEntry must not write anything when the VM is clear
            //    (the CurrentAddr=0 guard short-circuits).
            byte[] beforeBytes = new byte[12];
            Array.Copy(rom.Data, (int)validAddr, beforeBytes, 0, 12);
            vm.WriteEntry();
            byte[] afterBytes = new byte[12];
            Array.Copy(rom.Data, (int)validAddr, afterBytes, 0, 12);
            Assert.Equal(beforeBytes, afterBytes);
        }
        finally { CoreState.ROM = prevRom; }
    }

    /// <summary>
    /// The View's Write_Click must refuse the write when the VM is in
    /// the "no entry loaded" state. Verified via the source-string
    /// check (mirrors View_WriteClick_WrapsInUndoScope's approach
    /// since opening an Avalonia window in xunit requires the app
    /// handle).
    /// </summary>
    [Fact]
    public void View_WriteClick_RefusesWhenNoEntryLoaded()
    {
        string repoRoot = FindRepoRoot();
        string codeBehindPath = Path.Combine(repoRoot, "FEBuilderGBA.Avalonia", "Views",
            "EventMapChangeView.axaml.cs");
        Assert.True(File.Exists(codeBehindPath));
        string source = File.ReadAllText(codeBehindPath);

        // The Write_Click handler must contain a guard that short-circuits
        // when the VM is in the "no entry loaded" state — before opening
        // the undo scope. Use the same regex style as the other view
        // assertions.
        Assert.Matches(
            new Regex(@"void\s+Write_Click\([^)]*\)\s*\{[\s\S]*?(!_vm\.IsLoaded|_vm\.CurrentAddr\s*==\s*0)[\s\S]*?return\s*;",
                RegexOptions.Singleline),
            source);
    }

    [Fact]
    public void ViewModel_ClearEntry_ResetsAllWritableState()
    {
        var prevRom = CoreState.ROM;
        try
        {
            uint validAddr = 0x00900000u;
            ROM rom = MakeSyntheticFe8uRomWithOneMapAndChangeData(
                mapChangePlist: 3, changeDataOffset: validAddr);
            CoreState.ROM = rom;

            var vm = new EventMapChangeViewModel();
            vm.LoadEntryForMap(0u);
            vm.B0 = 0xAA;
            vm.B1 = 0xBB;
            vm.P8 = 0x00123456u;
            vm.Comment = "stash";
            Assert.True(vm.IsLoaded);
            Assert.NotEqual(0u, vm.CurrentAddr);

            vm.ClearEntry();
            Assert.Equal(0u, vm.CurrentAddr);
            Assert.Equal(0u, vm.SelectAddress);
            Assert.False(vm.IsLoaded);
            Assert.Equal(0u, vm.B0);
            Assert.Equal(0u, vm.B1);
            Assert.Equal(0u, vm.P8);
            Assert.Equal(string.Empty, vm.Comment);
        }
        finally { CoreState.ROM = prevRom; }
    }

    // -----------------------------------------------------------------
    // Comment cache parity (mirror ImageBG behavior).
    // -----------------------------------------------------------------

    [Fact]
    public void ViewModel_SaveComment_PersistsThroughCommentCache()
    {
        var prevCache = CoreState.CommentCache;
        try
        {
            CoreState.CommentCache = new HeadlessEtcCache();
            var vm = new EventMapChangeViewModel
            {
                CurrentAddr = 0x12345u
            };
            vm.SaveComment("map change comment");

            vm.Comment = string.Empty;
            vm.RefreshComment();
            Assert.Equal("map change comment", vm.Comment);
        }
        finally
        {
            CoreState.CommentCache = prevCache;
        }
    }

    [Fact]
    public void ViewModel_SaveComment_NoopWhenAddrIsZero()
    {
        var prevCache = CoreState.CommentCache;
        try
        {
            CoreState.CommentCache = new HeadlessEtcCache();
            var vm = new EventMapChangeViewModel(); // CurrentAddr = 0
            vm.SaveComment("should not persist");
            Assert.Equal(string.Empty, CoreState.CommentCache.S_At(0));
        }
        finally
        {
            CoreState.CommentCache = prevCache;
        }
    }

    // -----------------------------------------------------------------
    // Undo coverage — the View's Write_Click handler must open and
    // commit an UndoService scope around _vm.WriteEntry. Copilot CLI v1
    // blocking concern #3.
    // -----------------------------------------------------------------

    [Fact]
    public void View_WriteClick_WrapsInUndoScope()
    {
        string repoRoot = FindRepoRoot();
        string codeBehindPath = Path.Combine(repoRoot, "FEBuilderGBA.Avalonia", "Views",
            "EventMapChangeView.axaml.cs");
        Assert.True(File.Exists(codeBehindPath), $"code-behind not found at {codeBehindPath}");

        string source = File.ReadAllText(codeBehindPath);

        Assert.Matches(
            new Regex(@"void\s+Write_Click\([^)]*\)\s*\{[\s\S]*?_undoService\.Begin\(", RegexOptions.Singleline),
            source);
        Assert.Matches(
            new Regex(@"void\s+Write_Click\([^)]*\)\s*\{[\s\S]*?_undoService\.Commit\(\)", RegexOptions.Singleline),
            source);
        Assert.Matches(
            new Regex(@"void\s+Write_Click\([^)]*\)\s*\{[\s\S]*?_undoService\.Rollback\(\)", RegexOptions.Singleline),
            source);
    }

    /// <summary>
    /// Phase 5 cross-reference assertion: the UndoCoverageScanner's
    /// View→VM walker must classify <c>EventMapChangeViewModel.WriteEntry</c>
    /// as <see cref="UndoCoverage.Covered"/> after the matching
    /// <c>Write_Click</c> handler is wired to wrap it in a Begin/Commit
    /// scope.
    /// </summary>
    [Fact]
    public void ViewModel_WriteEntry_RegisteredInUndoCoverage()
    {
        string repoRoot = FindRepoRoot();
        string viewSrc = File.ReadAllText(Path.Combine(repoRoot,
            "FEBuilderGBA.Avalonia", "Views", "EventMapChangeView.axaml.cs"));

        var covered = new System.Collections.Generic.HashSet<(string, string)>();
        UndoCoverageScanner.ExtractViewCoveredVmMethods(viewSrc, covered);
        Assert.Contains(("EventMapChangeViewModel", "WriteEntry"), covered);
    }

    // -----------------------------------------------------------------
    // Helpers.
    // -----------------------------------------------------------------

    /// <summary>
    /// Build a minimal FE8U ROM image with:
    ///   - A single map setting at <c>mapTableBase</c> with
    ///     <c>plist[11] = mapChangePlist</c> and a valid first-dword
    ///     pointer (so MakeMapIDList accepts it).
    ///   - A CHANGE PLIST table at a known address whose entry 3 points
    ///     to <paramref name="changeDataOffset"/>.
    ///   - Seeded change-data bytes at <paramref name="changeDataOffset"/>.
    ///
    /// The FE8U <c>map_setting_pointer</c> is resolved by
    /// <c>U.FindROMPointer(rom, callback, candidates)</c> against a
    /// version-specific list of slots. We plant the pointer at each
    /// candidate slot to make sure detection succeeds.
    /// </summary>
    static ROM MakeSyntheticFe8uRomWithOneMapAndChangeData(byte mapChangePlist, uint changeDataOffset)
    {
        var bytes = new byte[0x1100000];

        uint mapTableBase = 0x00800000u;
        uint plistTableBase = 0x00880000u;

        // ---- 1. Plant FE8U-specific signatures so detection succeeds.
        // rom.u32(0x12) < 0xE is the FE8U callback predicate; the
        // bytes at offset 0x12 are zero by default which satisfies it.

        // Plant the FE8U map_setting_pointer candidate slots so the
        // primary slot 0x0B5F98 picks up our table.
        uint[] mapSettingCandidates = { 0x0B5F98u, 0x0B61C0u, 0x0B6328u, 0x0B6500u, 0x03462Cu, 0xB5E68u };
        foreach (var slot in mapSettingCandidates)
        {
            WriteU32(bytes, (int)slot, mapTableBase | 0x08000000u);
        }

        // For U.FindROMPointer secondary check: a + 0x100 must be safe.
        // Our mapTableBase = 0x00800000, so 0x00800100 must exist — it does
        // (bytes is 0x1100000 long).

        // ---- 2. Lay out a single valid map setting record.
        // The map_setting_datasize for FE8U is 148. WF treats a pointer in
        // the first dword as valid.
        uint mapSettingDataSize = 148u;
        int mapRecordBase = (int)mapTableBase;
        WriteU32(bytes, mapRecordBase + 0, 0x08123456u); // valid pointer-shaped first dword
        // PLIST validation: at least one of offset 4 or 8 needs to be a
        // non-zero, non-0xFFFFFFFF u32. We use 0x00000001. NOTE: offset 8
        // u32 writes overlap offsets 8-11, so the mapChangePlist byte at
        // offset 11 must be written AFTER these dword writes — otherwise
        // the WriteU32 at offset 8 stomps the plist byte (MSB = 0).
        WriteU32(bytes, mapRecordBase + 4, 0x00000001u);
        WriteU32(bytes, mapRecordBase + 8, 0x00000001u);
        bytes[mapRecordBase + 11] = mapChangePlist;
        // Stamp a sane weather byte (offset 12) so IsMapSettingValid passes.
        bytes[mapRecordBase + 12] = 0x00;

        // Terminator at the next slot — make sure the first dword is NOT
        // a pointer and weather (offset 12) is invalid (>= 0xE) so
        // IsMapSettingValid returns false.
        int termBase = (int)(mapTableBase + mapSettingDataSize);
        WriteU32(bytes, termBase + 0, 0x00000000u);
        WriteU32(bytes, termBase + 4, 0x00000000u);
        WriteU32(bytes, termBase + 8, 0x00000000u);
        bytes[termBase + 12] = 0xFF;

        // ---- 3. Plant the CHANGE PLIST pointer table.
        // The FE8U map_mapchange_pointer is hardcoded at 0x0346AC.
        WriteU32(bytes, (int)0x0346ACu, plistTableBase | 0x08000000u);

        // Plant the PLIST entry 3 -> changeDataOffset (or zero).
        // PLIST entry 0/1/2 = 0; entry 3 = pointer to changeDataOffset.
        for (int i = 0; i < 4; i++)
        {
            WriteU32(bytes, (int)(plistTableBase + i * 4u), 0u);
        }
        if (changeDataOffset != 0u)
        {
            WriteU32(bytes, (int)(plistTableBase + 3 * 4u), changeDataOffset | 0x08000000u);
        }

        // ---- 4. Seed the change data block.
        if (changeDataOffset != 0u)
        {
            // First byte != 0xFF so the WF validity check accepts.
            bytes[(int)changeDataOffset] = 0x01;
        }

        var rom = new ROM();
        rom.LoadLow("synthetic-fe8u.gba", bytes, "BE8E01");
        return rom;
    }

    static void WriteU32(byte[] data, int offset, uint value)
    {
        data[offset + 0] = (byte)(value & 0xFF);
        data[offset + 1] = (byte)((value >> 8) & 0xFF);
        data[offset + 2] = (byte)((value >> 16) & 0xFF);
        data[offset + 3] = (byte)((value >> 24) & 0xFF);
    }

    static string FindRepoRoot()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "FEBuilderGBA.sln")))
        {
            dir = Path.GetDirectoryName(dir);
        }
        if (dir == null)
            throw new InvalidOperationException("Could not find FEBuilderGBA.sln from test base directory");
        return dir;
    }
}
