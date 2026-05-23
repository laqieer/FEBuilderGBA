// SPDX-License-Identifier: GPL-3.0-or-later
// Phase 1+2+4+5 gap-sweep regression tests for ImageMagicFEditorView (#418).
//
// Closes the 34-control / 25 WF-only-labels gap surfaced by the gap-sweep
// methodology on ImageMagicFEditorForm (HIGH density 37/3, -91.9%). After
// this PR the view rebuilds to a 3-row 2-column shell that mirrors the
// WF panel3 / panel5 / panel8 / DragTargetPanel surfaces, with patch-aware
// list-expansion and a KnownGap NavigationTarget for the
// ToolAnimationCreator jump (tracked at #500).
using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.GapSweep;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests.GapSweep;

/// <summary>
/// Tests proving the ImageMagicFEditor parity raise (#418) is permanent.
/// Density target: AV control count ≥ ceil(WF * 0.75) = 28.
///
/// Marked [Collection("SharedState")] because the VM tests mutate
/// CoreState.ROM and CoreState.CommentCache to plant synthetic ROMs.
/// </summary>
[Collection("SharedState")]
public class ImageMagicFEditorParityTests
{
    // -----------------------------------------------------------------
    // Density (Phase 1) — AV control count must reach MEDIUM verdict.
    // -----------------------------------------------------------------

    /// <summary>
    /// The WF Designer reports 37 user-facing controls; the MEDIUM
    /// threshold is `ceil(37 * 0.75) = 28`. Falling below this re-enters
    /// HIGH territory.
    /// </summary>
    [Fact]
    public void View_AvControlCount_AtOrAboveMediumVerdict()
    {
        string axamlPath = Path.Combine(FindRepoRoot(),
            "FEBuilderGBA.Avalonia", "Views", "ImageMagicFEditorView.axaml");
        Assert.True(File.Exists(axamlPath), $"AXAML not found at {axamlPath}");

        var doc = XDocument.Load(axamlPath);
        int avCount = ControlDensityScanner.CountAvControlsInDocument(doc);

        const int WfControlCount = 37;
        int mediumThreshold = (int)Math.Ceiling(WfControlCount * 0.75); // 28
        Assert.True(avCount >= mediumThreshold,
            $"AV control count {avCount} must be >= {mediumThreshold} (75% of WF={WfControlCount})");
    }

    // -----------------------------------------------------------------
    // Phase 5 — control surface assertions (static AXAML inspection).
    // -----------------------------------------------------------------

    [Fact]
    public void View_HasTopReadConfigBar()
    {
        string axaml = ReadAxaml();
        Assert.Contains("ImageMagicFEditor_ReadStart_Input", axaml);
        Assert.Contains("ImageMagicFEditor_ReadCount_Input", axaml);
        Assert.Contains("ImageMagicFEditor_ReloadList_Button", axaml);
        Assert.Contains("Click=\"ReloadList_Click\"", axaml);
    }

    [Fact]
    public void View_HasAddressWriteBar()
    {
        string axaml = ReadAxaml();
        Assert.Contains("ImageMagicFEditor_Address_Input", axaml);
        Assert.Contains("ImageMagicFEditor_BlockSize_Input", axaml);
        Assert.Contains("ImageMagicFEditor_SelectedAddress_Input", axaml);
        Assert.Contains("ImageMagicFEditor_Write_Button", axaml);
        Assert.Contains("Click=\"Write_Click\"", axaml);
    }

    [Fact]
    public void View_HasMainEntryListPanel()
    {
        string axaml = ReadAxaml();
        Assert.Contains("ImageMagicFEditor_Entry_List", axaml);
        Assert.Contains("ImageMagicFEditor_NameHeader_Label", axaml);
        Assert.Contains("ImageMagicFEditor_MagicListExpand_Button", axaml);
        Assert.Contains("Click=\"MagicListExpand_Click\"", axaml);
    }

    [Fact]
    public void View_HasFiveSpinners_P0_P4_P8_P12_P16()
    {
        string axaml = ReadAxaml();
        // The five frame/anime spinners + their exact WF labels.
        Assert.Contains("ImageMagicFEditor_P0_Input", axaml);
        Assert.Contains("ImageMagicFEditor_P4_Input", axaml);
        Assert.Contains("ImageMagicFEditor_P8_Input", axaml);
        Assert.Contains("ImageMagicFEditor_P12_Input", axaml);
        Assert.Contains("ImageMagicFEditor_P16_Input", axaml);
        // Exact WF Designer label text.
        Assert.Contains("FrameData", axaml);
        Assert.Contains("OBJRightToLeft", axaml);
        Assert.Contains("OBJLeftToRight", axaml);
        Assert.Contains("OBJBGRightToLeft", axaml);
        Assert.Contains("OBBGLeftToRight", axaml);
    }

    [Fact]
    public void View_HasDimComboBox_AndZoomComboBox()
    {
        string axaml = ReadAxaml();
        Assert.Contains("ImageMagicFEditor_Dim_Combo", axaml);
        Assert.Contains("ImageMagicFEditor_Zoom_Combo", axaml);
    }

    [Fact]
    public void View_HasMagicCommentTextBox_AndFrameSpinner()
    {
        string axaml = ReadAxaml();
        Assert.Contains("ImageMagicFEditor_Comment_Input", axaml);
        Assert.Contains("ImageMagicFEditor_Frame_Input", axaml);
    }

    [Fact]
    public void View_HasOpenSource_SelectSource_Editor_LinkButtons()
    {
        string axaml = ReadAxaml();
        Assert.Contains("ImageMagicFEditor_JumpEditor_Button", axaml);
        Assert.Contains("ImageMagicFEditor_OpenSource_Button", axaml);
        Assert.Contains("ImageMagicFEditor_SelectSource_Button", axaml);
        Assert.Contains("ImageMagicFEditor_LinkInternet_Label", axaml);
    }

    [Fact]
    public void View_HasImportExport_Buttons()
    {
        string axaml = ReadAxaml();
        Assert.Contains("ImageMagicFEditor_MagicAnimeImport_Button", axaml);
        Assert.Contains("ImageMagicFEditor_MagicAnimeExport_Button", axaml);
    }

    [Fact]
    public void View_HasPatchNoticeLabel()
    {
        string axaml = ReadAxaml();
        Assert.Contains("ImageMagicFEditor_PatchNotice_Label", axaml);
    }

    [Fact]
    public void View_HasSamplePreviewLabel_AndPreviewImage()
    {
        string axaml = ReadAxaml();
        Assert.Contains("ImageMagicFEditor_SamplePreview_Label", axaml);
        Assert.Contains("ImageMagicFEditor_PreviewImage", axaml);
    }

    // -----------------------------------------------------------------
    // Roslyn AST walk — assert write handlers use _undoService.
    // -----------------------------------------------------------------

    [Fact]
    public void View_WriteHandler_UsesUndoService()
    {
        string source = ReadCodeBehind();
        Assert.Matches(new Regex(@"void\s+Write_Click[\s\S]*?_undoService\.Begin",
            RegexOptions.Compiled), source);
        Assert.Matches(new Regex(@"void\s+Write_Click[\s\S]*?_undoService\.Commit",
            RegexOptions.Compiled), source);
        Assert.Matches(new Regex(@"void\s+Write_Click[\s\S]*?_undoService\.Rollback",
            RegexOptions.Compiled), source);
    }

    [Fact]
    public void View_MagicListExpandHandler_UsesUndoService()
    {
        string source = ReadCodeBehind();
        Assert.Matches(new Regex(@"void\s+MagicListExpand_Click[\s\S]*?_undoService\.Begin",
            RegexOptions.Compiled), source);
        Assert.Matches(new Regex(@"void\s+MagicListExpand_Click[\s\S]*?_undoService\.Commit",
            RegexOptions.Compiled), source);
        Assert.Matches(new Regex(@"void\s+MagicListExpand_Click[\s\S]*?_undoService\.Rollback",
            RegexOptions.Compiled), source);
    }

    // -----------------------------------------------------------------
    // Deferred buttons — disabled + tooltipped per scope discipline.
    // -----------------------------------------------------------------

    [Theory]
    [InlineData("ImageMagicFEditor_MagicAnimeImport_Button")]
    [InlineData("ImageMagicFEditor_MagicAnimeExport_Button")]
    [InlineData("ImageMagicFEditor_OpenSource_Button")]
    [InlineData("ImageMagicFEditor_SelectSource_Button")]
    [InlineData("ImageMagicFEditor_MagicListExpand_Button")]
    public void View_DeferredButton_IsDisabledAndReferencesFollowupIssue(string id)
    {
        // Simple regex check on the raw AXAML text — looks at the
        // Button element with the given AutomationId AutomationId and
        // verifies the same element carries IsEnabled="False" plus a
        // ToolTip.Tip attribute mentioning #500.
        string axaml = ReadAxaml();
        // Match the <Button … AutomationId="<id>" … /> element body
        // (everything between this opening tag's start and the next
        // unrelated tag's start) so we can inspect its attributes.
        var pattern = new Regex(
            @"<Button[^>]*AutomationId=""" + Regex.Escape(id) + @"""[^>]*?/>",
            RegexOptions.Singleline);
        var match = pattern.Match(axaml);
        Assert.True(match.Success,
            $"Expected a <Button AutomationId=\"{id}\" .../> element");
        Assert.Contains("IsEnabled=\"False\"", match.Value);
        Assert.Contains("ToolTip.Tip=", match.Value);
        Assert.Contains("#500", match.Value);
    }

    // -----------------------------------------------------------------
    // ViewModel — synthetic ROM behavior tests.
    // -----------------------------------------------------------------

    [Fact]
    public void ViewModel_LoadEntry_NullRom_DoesNotThrow()
    {
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = null;
            var vm = new ImageMagicFEditorViewModel();
            // Just make sure it tolerates a missing ROM.
            vm.LoadEntry(0x100u);
            Assert.False(vm.IsLoaded);
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void ViewModel_LoadEntry_ReadsDimPointer_AsKind()
    {
        ROM rom = MakeMinimalFe8uRom();
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            // Plant FE8U FEditor signature + CSA spell table so
            // `SearchMagicSystem` reports dimAddr = 0x95D7ED, no_dimAddr
            // = 0x95D8EF. Per WF semantics
            // (`AddressList_SelectedIndexChanged` in
            // ImageMagicFEditorForm.cs:142-153) the entry's pointer
            // value 0x95D7ED maps to DimComboBox index 0 = `dim_pc`
            // and 0x95D8EF maps to index 1 = `dim`. We plant 0x95D8EF
            // at the POINTER-SLOT and assert the VM reports `Dim`.
            PlantFEditorSignatureFe8u(rom);
            uint pointerSlot = 0x00400000u;
            uint csaEntry = 0x00410000u;
            BitConverter.GetBytes(0x95D8EFu | 0x08000000u)
                .CopyTo(rom.Data, pointerSlot);

            var vm = new ImageMagicFEditorViewModel();
            // The VM must initialize MagicSystemDetected lazily.
            vm.LoadEntry(csaEntry, pointerSlot);

            Assert.Equal(ImageMagicFEditorViewModel.DimPointerKind.Dim,
                vm.DimPointer);
            Assert.Equal(csaEntry, vm.CurrentAddr);
            Assert.Equal(pointerSlot, vm.PointerSlotAddr);
            Assert.True(vm.IsLoaded);
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void ViewModel_LoadEntry_ReadsDimPointer_DimPc_Kind()
    {
        // Complement to the Dim case: planting 0x95D7ED at the pointer
        // slot should map to DimComboBox index 0 = `dim_pc`.
        ROM rom = MakeMinimalFe8uRom();
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            PlantFEditorSignatureFe8u(rom);
            uint pointerSlot = 0x00400000u;
            uint csaEntry = 0x00410000u;
            BitConverter.GetBytes(0x95D7EDu | 0x08000000u)
                .CopyTo(rom.Data, pointerSlot);

            var vm = new ImageMagicFEditorViewModel();
            vm.LoadEntry(csaEntry, pointerSlot);
            Assert.Equal(ImageMagicFEditorViewModel.DimPointerKind.DimPc,
                vm.DimPointer);
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void ViewModel_Write_PersistsDimPointer_AsGbaPointer()
    {
        ROM rom = MakeMinimalFe8uRom();
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            PlantFEditorSignatureFe8u(rom);

            uint pointerSlot = 0x00400000u;
            uint csaEntry = 0x00410000u;
            var vm = new ImageMagicFEditorViewModel();
            // Trigger patch detection before setting properties directly
            // (LoadEntry would do this implicitly; here we test the Write
            // path against a manually-set state).
            vm.RefreshPatchState();
            vm.CurrentAddr = csaEntry;
            vm.PointerSlotAddr = pointerSlot;
            // `Dim` maps to NoDimAddr per WF WriteDim() semantics.
            vm.DimPointer = ImageMagicFEditorViewModel.DimPointerKind.Dim;
            vm.Write();

            // The POINTER-SLOT (not the CSA entry) must now hold the
            // GBA pointer to the no_dim address resolved from the
            // FEditor signature row.
            uint raw = rom.u32(pointerSlot);
            Assert.Equal(0x95D8EFu | 0x08000000u, raw);
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void ViewModel_Write_PersistsP0ThroughP16_ToCsaEntry()
    {
        // The CSA spell-table entry holds five u32 fields at
        // CurrentAddr+0/4/8/12/16. Write() must persist them so user
        // edits to FrameData / OBJRightToLeft / OBJLeftToRight /
        // OBJBGRightToLeft / OBBGLeftToRight aren't silently dropped
        // (Copilot bot review on PR #554 #6).
        ROM rom = MakeMinimalFe8uRom();
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            PlantFEditorSignatureFe8u(rom);
            uint pointerSlot = 0x00400000u;
            uint csaEntry = 0x00410000u;
            var vm = new ImageMagicFEditorViewModel();
            vm.RefreshPatchState();
            vm.CurrentAddr = csaEntry;
            vm.PointerSlotAddr = pointerSlot;
            vm.DimPointer = ImageMagicFEditorViewModel.DimPointerKind.Empty;
            vm.P0 = 0x11111111u;
            vm.P4 = 0x22222222u;
            vm.P8 = 0x33333333u;
            vm.P12 = 0x44444444u;
            vm.P16 = 0x55555555u;
            vm.Write();

            Assert.Equal(0x11111111u, rom.u32(csaEntry + 0));
            Assert.Equal(0x22222222u, rom.u32(csaEntry + 4));
            Assert.Equal(0x33333333u, rom.u32(csaEntry + 8));
            Assert.Equal(0x44444444u, rom.u32(csaEntry + 12));
            Assert.Equal(0x55555555u, rom.u32(csaEntry + 16));
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void ViewModel_Write_PersistsCommentToCache()
    {
        // Comments key off the CSA entry address (mirrors WF
        // CommentCache.Update(Address.Value, ...) where Address.Value
        // is the CSA entry address, not the pointer slot).
        ROM rom = MakeMinimalFe8uRom();
        var prevRom = CoreState.ROM;
        var prevCache = CoreState.CommentCache;
        try
        {
            CoreState.ROM = rom;
            var fake = new FakeEtcCache();
            CoreState.CommentCache = fake;

            uint pointerSlot = 0x00400000u;
            uint csaEntry = 0x00410000u;
            var vm = new ImageMagicFEditorViewModel();
            vm.CurrentAddr = csaEntry;
            vm.PointerSlotAddr = pointerSlot;
            vm.DimPointer = ImageMagicFEditorViewModel.DimPointerKind.Empty;
            vm.Comment = "test comment";
            vm.Write();

            Assert.Equal("test comment", fake.At(csaEntry));
            Assert.Equal("", fake.At(pointerSlot)); // not keyed by slot.
        }
        finally
        {
            CoreState.ROM = prevRom;
            CoreState.CommentCache = prevCache;
        }
    }

    [Fact]
    public void ViewModel_LoadEntry_ReadsCommentFromCache()
    {
        ROM rom = MakeMinimalFe8uRom();
        var prevRom = CoreState.ROM;
        var prevCache = CoreState.CommentCache;
        try
        {
            CoreState.ROM = rom;
            var fake = new FakeEtcCache();
            uint pointerSlot = 0x00400000u;
            uint csaEntry = 0x00410000u;
            fake.Update(csaEntry, "planted comment");
            CoreState.CommentCache = fake;

            var vm = new ImageMagicFEditorViewModel();
            vm.LoadEntry(csaEntry, pointerSlot);

            Assert.Equal("planted comment", vm.Comment);
        }
        finally
        {
            CoreState.ROM = prevRom;
            CoreState.CommentCache = prevCache;
        }
    }

    [Fact]
    public void ViewModel_NavigationTargets_HasJumpToToolAnimationCreator()
    {
        var vm = new ImageMagicFEditorViewModel();
        var source = (INavigationTargetSource)vm;
        var targets = source.GetNavigationTargets();

        Assert.Single(targets);
        var t = targets[0];
        Assert.Equal("JumpToToolAnimationCreator", t.CommandName);
        Assert.Equal("ToolAnimationCreatorView", t.TargetViewType.Name);
        Assert.Equal("#500", t.IssueRef);
    }

    // -----------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------

    static ROM MakeMinimalFe8uRom()
    {
        var rom = new ROM();
        var data = new byte[0x1100000];
        rom.LoadLow("synthetic-fe8u.gba", data, "BE8E01");
        return rom;
    }

    /// <summary>
    /// Plant the FE8U FEditor magic signature + CSA spell table pattern
    /// so `ImageUtilMagicCore.SearchMagicSystem` returns FEditorAdv with
    /// dimAddr=0x95D7ED and no_dimAddr=0x95D8EF.
    /// </summary>
    static void PlantFEditorSignatureFe8u(ROM rom)
    {
        byte[] sig = {
            0x01, 0x00, 0x00, 0x00, 0x90, 0xD7, 0x95, 0x08,
            0x03, 0x00, 0x00, 0x00, 0x39, 0xD9, 0x95, 0x08,
        };
        Array.Copy(sig, 0, rom.Data, 0x95d780, sig.Length);

        byte[] csaPat = {
            0x01, 0xB4, 0x7D, 0xE7, 0x34, 0xFF, 0x03, 0x02,
            0x80, 0xD7, 0x95, 0x08, 0x1A, 0xE1, 0x03, 0x02,
        };
        Array.Copy(csaPat, 0, rom.Data, 0x00200000, csaPat.Length);
        BitConverter.GetBytes(0x00100000u | 0x08000000u)
            .CopyTo(rom.Data, 0x00200000 + csaPat.Length);
    }

    static string ReadAxaml() => File.ReadAllText(Path.Combine(FindRepoRoot(),
        "FEBuilderGBA.Avalonia", "Views", "ImageMagicFEditorView.axaml"));

    static string ReadCodeBehind() => File.ReadAllText(Path.Combine(FindRepoRoot(),
        "FEBuilderGBA.Avalonia", "Views", "ImageMagicFEditorView.axaml.cs"));

    /// <summary>Walk parents from test bin dir until we find FEBuilderGBA.sln.</summary>
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

    /// <summary>
    /// In-memory IEtcCache used by the Comment round-trip tests so we
    /// can exercise CoreState.CommentCache without depending on the
    /// WinForms-only EtcCacheText file-backed implementation.
    /// </summary>
    class FakeEtcCache : IEtcCache
    {
        readonly System.Collections.Generic.Dictionary<uint, string> _map = new();
        public void RemoveOverRange(uint range) { }
        public void RemoveRange(uint start, uint end) { }
        public bool CheckFast(uint num) => _map.ContainsKey(num);
        public string At(uint num, string def = "")
        {
            return _map.TryGetValue(num, out string v) ? v : def;
        }
        public string S_At(uint num) => At(num);
        public bool TryGetValue(uint num, out string out_data)
        {
            return _map.TryGetValue(num, out out_data!);
        }
        public void Update(uint addr, string comment) => _map[addr] = comment;
        public void Remove(uint addr) => _map.Remove(addr);
    }
}
