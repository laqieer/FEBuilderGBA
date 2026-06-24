// SPDX-License-Identifier: GPL-3.0-or-later
// VM + golden-builder parity tests for the MapTileAnimation PLIST-label rewire
// (#952, T5 slice B = bug #11).
//
//  * MapTileAnimationView (STEP 1): LoadMapTileAnimationList resolves each
//    slot index (an ANIMATION PLIST id) to "ANIME1/ANIME2 MapName" /
//    NULL / -EMPTY- / UNK — never a bare 0x… pointer or the old
//    "{id} TileAnim 0x…" string. Lockstep with the golden builder
//    ListParityHelper.BuildMapTileAnimationList (reached via BuildReferenceList).
//  * MapTileAnimation2 (STEP 2): LoadPlistList filter labels resolve to
//    "ANIME2 MapName" via the shared resolver — never the old raw
//    "タイルアニメーション2 パレットアニメ:{plist}" PLIST-hex string. Lockstep with
//    the golden builder ListParityHelper.BuildMapTileAnimation2FilterList.
//  * Smoke guard: the list-building / filter-building source no longer emits a
//    bare 0x{...:X08} row label or the old raw label strings.

using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Tests;

public class MapTileAnimationResolverParityTests : IClassFixture<RomFixture>
{
    readonly RomFixture _rom;

    public MapTileAnimationResolverParityTests(RomFixture rom)
    {
        _rom = rom;
    }

    // -----------------------------------------------------------------
    // STEP 1: MapTileAnimationView (the simple, menu-reachable editor).
    // -----------------------------------------------------------------

    [Fact]
    public void MapTileAnimationView_List_ProducesResolvedLabels()
    {
        if (!_rom.IsAvailable) return; // skip

        var vm = new MapTileAnimationViewModel();
        List<AddrResult> rows = vm.LoadMapTileAnimationList();
        Assert.NotEmpty(rows);
        foreach (AddrResult r in rows)
        {
            AssertResolvedLabel(r.name, "MapTileAnimationView list");
            // The old format was "{id} TileAnim 0x…" — must be gone.
            Assert.DoesNotContain("TileAnim", r.name);
        }

        // Prove the resolver actually named at least one ANIME slot (split) or
        // resolved to NULL/-EMPTY-/UNK (non-split / empty) rather than a raw row.
        bool anyResolved = rows.Exists(r =>
            r.name.Contains(" ANIME1 ") || r.name.Contains(" ANIME2 ") ||
            r.name.EndsWith(" NULL") || r.name.Contains(" -EMPTY-") ||
            r.name.Contains(" UNK"));
        Assert.True(anyResolved,
            "expected at least one ANIME1/ANIME2/NULL/-EMPTY-/UNK resolved label");
    }

    [Fact]
    public void MapTileAnimationView_VM_And_GoldenBuilder_Lockstep()
    {
        if (!_rom.IsAvailable) return;

        var vm = new MapTileAnimationViewModel();
        List<AddrResult> vmRows = vm.LoadMapTileAnimationList();
        List<AddrResult> golden = ListParityHelper.BuildReferenceList("MapTileAnimationView");

        Assert.Equal(golden.Count, vmRows.Count);
        for (int i = 0; i < vmRows.Count; i++)
        {
            Assert.Equal(golden[i].name, vmRows[i].name);
            Assert.Equal(golden[i].addr, vmRows[i].addr);
        }
    }

    // -----------------------------------------------------------------
    // STEP 2: MapTileAnimation2 filter labels.
    // -----------------------------------------------------------------

    [Fact]
    public void MapTileAnimation2_FilterLabels_ProduceResolvedAnime2Labels()
    {
        if (!_rom.IsAvailable) return;

        var vm = new MapTileAnimation2ViewModel();
        List<MapTileAnimation2Core.PlistRow> rows = vm.LoadPlistList();
        // Some ROMs may legitimately have no anime2 PLISTs; only assert on the
        // rows that exist.
        foreach (var row in rows)
        {
            // The old raw label must be gone.
            Assert.DoesNotContain("パレットアニメ", row.Display);
            AssertResolvedLabel(row.Display, "MapTileAnimation2 filter");
        }

        // When rows exist, at least one should resolve to an ANIME2 map name
        // (the non-broken case) or carry the broken suffix.
        if (rows.Count > 0)
        {
            bool anyResolved = rows.Exists(r =>
                r.Display.StartsWith("ANIME2 ") || r.Display.Contains("("));
            Assert.True(anyResolved,
                "expected at least one resolved ANIME2 / broken-suffix filter label");
        }
    }

    [Fact]
    public void MapTileAnimation2_FilterVM_And_GoldenBuilder_Lockstep()
    {
        if (!_rom.IsAvailable) return;

        var vm = new MapTileAnimation2ViewModel();
        List<MapTileAnimation2Core.PlistRow> vmRows = vm.LoadPlistList();
        List<AddrResult> golden = ListParityHelper.BuildMapTileAnimation2FilterList(_rom.ROM!);

        Assert.Equal(vmRows.Count, golden.Count);
        for (int i = 0; i < vmRows.Count; i++)
        {
            // The golden filter builder copies PlistRow.Display verbatim, so the
            // VM filter combo strings and the golden labels must match exactly.
            Assert.Equal(vmRows[i].Display, golden[i].name);
            Assert.Equal(vmRows[i].Plist, golden[i].tag);
        }
    }

    // -----------------------------------------------------------------
    // #955 W1c: MapTileAnimation1 anime1 PLIST filter labels.
    // -----------------------------------------------------------------

    [Fact]
    public void MapTileAnimation1_FilterLabels_ProduceResolvedAnime1Labels()
    {
        if (!_rom.IsAvailable) return;

        var vm = new MapTileAnimation1ViewModel();
        List<MapTileAnimation1Core.PlistRow> rows = vm.LoadPlistList();
        // Some ROMs may legitimately have no anime1 PLISTs; only assert on the
        // rows that exist.
        foreach (var row in rows)
        {
            AssertResolvedLabel(row.Display, "MapTileAnimation1 filter");
        }

        // When rows exist, at least one should resolve to an ANIME1 map name
        // (the non-broken case) or carry the broken suffix.
        if (rows.Count > 0)
        {
            bool anyResolved = rows.Exists(r =>
                r.Display.StartsWith("ANIME1 ") || r.Display.Contains("("));
            Assert.True(anyResolved,
                "expected at least one resolved ANIME1 / broken-suffix filter label");
        }
    }

    [Fact]
    public void MapTileAnimation1_FilterVM_And_GoldenBuilder_Lockstep()
    {
        if (!_rom.IsAvailable) return;

        var vm = new MapTileAnimation1ViewModel();
        List<MapTileAnimation1Core.PlistRow> vmRows = vm.LoadPlistList();
        List<AddrResult> golden = ListParityHelper.BuildMapTileAnimation1FilterList(_rom.ROM!);

        Assert.Equal(vmRows.Count, golden.Count);
        for (int i = 0; i < vmRows.Count; i++)
        {
            // The golden filter builder copies PlistRow.Display verbatim, so the
            // VM filter combo strings and the golden labels must match exactly.
            Assert.Equal(vmRows[i].Display, golden[i].name);
            Assert.Equal(vmRows[i].Plist, golden[i].tag);
        }
    }

    [Fact]
    public void MapTileAnimation1_List_And_GoldenBuilder_Lockstep()
    {
        if (!_rom.IsAvailable) return;

        // The VM's no-arg LoadList() (used by the IDataVerifiable contract)
        // picks the first non-broken PLIST and scans its data table. The golden
        // builder BuildMapTileAnimation1List (reached via BuildReferenceList)
        // must match it byte-for-byte.
        var vm = new MapTileAnimation1ViewModel();
        List<AddrResult> vmRows = vm.LoadList();
        List<AddrResult> golden = ListParityHelper.BuildReferenceList("MapTileAnimation1View");

        Assert.Equal(golden.Count, vmRows.Count);
        for (int i = 0; i < vmRows.Count; i++)
        {
            Assert.Equal(golden[i].name, vmRows[i].name);
            Assert.Equal(golden[i].addr, vmRows[i].addr);
        }
    }

    // -----------------------------------------------------------------
    // #1403: the row address MUST be the DEREFERENCED struct address
    // (p32(slot)), never the raw PLIST slot address (base + id*4). Storing
    // the raw slot made Load show [ptr_i][ptr_{i+1}] garbage and made Write
    // overwrite slot i and slot i+1's pointers — corrupting the PLIST table.
    // -----------------------------------------------------------------

    [Fact]
    public void MapTileAnimationView_RowAddr_IsDereferencedStruct_NotRawSlot()
    {
        if (!_rom.IsAvailable) return;
        ROM rom = _rom.ROM!;

        uint ptr = rom.RomInfo.map_tileanime1_pointer;
        Assert.NotEqual(0u, ptr);
        uint baseAddr = rom.p32(ptr);

        var vm = new MapTileAnimationViewModel();
        List<AddrResult> rows = vm.LoadMapTileAnimationList();
        Assert.NotEmpty(rows);

        bool anyDiffersFromSlot = false;
        foreach (AddrResult r in rows)
        {
            uint id = r.tag;                       // the PLIST slot index
            uint slotAddr = baseAddr + id * 4u;    // raw slot (the OLD buggy addr)
            uint expectedStruct = rom.p32(slotAddr); // dereferenced struct addr

            // The row address must be the dereferenced struct, not the slot.
            Assert.Equal(expectedStruct, r.addr);

            // For any real ROM the slot pointer != its own slot address, so
            // the corrected addr must differ from the OLD raw-slot addr.
            if (r.addr != slotAddr) anyDiffersFromSlot = true;
        }

        Assert.True(anyDiffersFromSlot,
            "every row addr equalled its raw slot addr — dereference did not happen");
    }

    [Fact]
    public void MapTileAnimationView_LoadWrite_RoundTrip_DoesNotCorruptPlistTable()
    {
        if (!_rom.IsAvailable) return;
        ROM rom = _rom.ROM!;

        uint ptr = rom.RomInfo.map_tileanime1_pointer;
        uint baseAddr = rom.p32(ptr);

        var vm = new MapTileAnimationViewModel();
        List<AddrResult> rows = vm.LoadMapTileAnimationList();
        Assert.NotEmpty(rows);

        AddrResult first = rows[0];
        uint id = first.tag;
        uint slotAddr = baseAddr + id * 4u;
        uint structAddr = first.addr;

        // Snapshot the OLD corruption surface: slot i and slot i+1 (8 bytes at
        // the PLIST table) — Copilot review: prove these are NOT touched on Write.
        uint[] plistBefore = new uint[8];
        for (uint k = 0; k < 8; k++) plistBefore[k] = rom.u8(slotAddr + k);

        // Snapshot the struct bytes (W0@0, W2@2, D4@4) at the dereferenced addr.
        uint[] structBefore = new uint[8];
        for (uint k = 0; k < 8; k++) structBefore[k] = rom.u8(structAddr + k);

        // Load -> write back the SAME values (a no-op edit). With the fix this
        // writes the 8-byte struct at structAddr; with the bug it would have
        // written at slotAddr and corrupted the PLIST pointers.
        vm.LoadMapTileAnimation(structAddr);
        Assert.True(vm.CanWrite);
        vm.WriteMapTileAnimation();

        // The struct bytes are byte-identical after the round-trip.
        for (uint k = 0; k < 8; k++)
            Assert.Equal(structBefore[k], rom.u8(structAddr + k));

        // The PLIST table slots (the old corruption surface) are untouched —
        // UNLESS the struct legitimately overlaps the slot (it must not for a
        // real ROM, since structAddr was dereferenced FROM the slot pointer).
        if (structAddr != slotAddr)
        {
            for (uint k = 0; k < 8; k++)
                Assert.Equal(plistBefore[k], rom.u8(slotAddr + k));
        }
    }

    // -----------------------------------------------------------------
    // Smoke guard: list/filter builders must not emit raw labels.
    // -----------------------------------------------------------------

    [Fact]
    public void Builders_DoNotEmit_RawLabels()
    {
        string root = FindRepoRoot();

        // MapTileAnimationViewModel.LoadMapTileAnimationList — no raw 0x…X08
        // pointer label, no "TileAnim" string.
        string vmSrc = Path.Combine(root, "FEBuilderGBA.Avalonia", "ViewModels", "MapTileAnimationViewModel.cs");
        string loadBody = ExtractMethodBody(File.ReadAllText(vmSrc), "LoadMapTileAnimationList");
        Assert.False(string.IsNullOrEmpty(loadBody), "LoadMapTileAnimationList not found");
        Assert.DoesNotContain("\" TileAnim \"", loadBody);
        Assert.DoesNotContain(":X08}", loadBody);

        // ListParityHelper.BuildMapTileAnimationList — same.
        string parity = Path.Combine(root, "FEBuilderGBA.Avalonia", "Services", "ListParityHelper.cs");
        string goldenBody = ExtractMethodBody(File.ReadAllText(parity), "BuildMapTileAnimationList");
        Assert.False(string.IsNullOrEmpty(goldenBody), "BuildMapTileAnimationList not found");
        Assert.DoesNotContain("\" TileAnim \"", goldenBody);
        Assert.DoesNotContain(".ToString(\"X08\")", goldenBody);

        // MapTileAnimation2Core.BuildPlistList — old raw PLIST-hex key gone.
        string coreSrc = Path.Combine(root, "FEBuilderGBA.Core", "MapTileAnimation2Core.cs");
        string plistBody = ExtractMethodBody(File.ReadAllText(coreSrc), "BuildPlistList");
        Assert.False(string.IsNullOrEmpty(plistBody), "BuildPlistList not found");
        Assert.DoesNotContain("パレットアニメ:{0}", plistBody);
    }

    // =================================================================
    // Helpers (mirror MapPListResolverParityTests).
    // =================================================================

    static void AssertResolvedLabel(string label, string ctx)
    {
        Assert.NotNull(label);
        // A resolved row is "{idHex} {TYPE MapName}" / "{idHex} NULL" /
        // "{idHex} -EMPTY-" / "{idHex} UNK" (filter labels omit the id prefix).
        // The id prefix uses U.ToHexString (e.g. "0x01"), which is allowed, and a
        // legitimate map name could even contain a short "0x08" substring — so we
        // forbid ONLY a FULL GBA pointer literal: a word-bounded 8-hex-digit 0x
        // value like "0x08123456" (#954 review — the old bare "0x08" substring
        // check was over-aggressive and could reject a valid label).
        Assert.False(Regex.IsMatch(label, @"\b0x[0-9A-Fa-f]{8}\b"),
            $"{ctx}: row label still contains a raw 0x… pointer: '{label}'");
    }

    static string ExtractMethodBody(string src, string methodName)
    {
        int sig = src.IndexOf(methodName + "(");
        if (sig < 0) return "";
        int open = src.IndexOf('{', sig);
        if (open < 0) return "";
        int depth = 0;
        for (int i = open; i < src.Length; i++)
        {
            if (src[i] == '{') depth++;
            else if (src[i] == '}')
            {
                depth--;
                if (depth == 0) return src.Substring(open, i - open + 1);
            }
        }
        return "";
    }

    static string FindRepoRoot()
    {
        string dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
        for (int i = 0; i < 12; i++)
        {
            if (File.Exists(Path.Combine(dir, "FEBuilderGBA.sln")))
                return dir;
            dir = Path.GetDirectoryName(dir)!;
        }
        throw new System.InvalidOperationException("Repo root not found");
    }
}
