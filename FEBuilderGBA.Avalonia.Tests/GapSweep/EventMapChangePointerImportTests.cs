// SPDX-License-Identifier: GPL-3.0-or-later
// #961 W2c — EventMapChange pointer-import + FE7 obj2 high-byte VM tests.
//
// Two gaps closed:
//   1. PointerImport: EventMapChangeViewModel.ImportChangeDataFromPointer copies
//      a SOURCE change-data block (sized by the current record's W×H×2) into ROM
//      free space and repoints the current record's P8 — all ambient-undo so the
//      View's UndoService scope captures it; rollback restores byte-identity.
//   2. FE7 obj2 high byte: RenderChangePreview resolves (obj_plist >> 8) & 0xFF as
//      the secondary obj2 tileset and passes its offset to
//      MapRenderCore.RenderChangeMap. FE6/FE8 (high byte 0) are unchanged.
//
// Synthetic-ROM helpers mirror EventMapChangeListExpandTests (FE8U "BE8E01") plus
// a dedicated FE7U "AE7E01" builder for the obj2 path. Marked
// [Collection("SharedState")] because the tests mutate CoreState.ROM / .Undo.

using System;
using System.IO;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.ViewModels;
using FEBuilderGBA.SkiaSharp;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests.GapSweep;

[Collection("SharedState")]
public class EventMapChangePointerImportTests : IDisposable
{
    const uint SIZE = 12u;

    // FE8U layout (shared with EventMapChangeListExpandTests).
    const uint TableBase  = 0x00900000u; // change-data record block
    const uint SrcData    = 0x00A00000u; // SOURCE tile bytes to import
    const uint FreeRegion = 0x00B00000u; // 0xFF free run for the append
    const byte MapChangePlist = 3;

    readonly ROM? _savedRom;
    readonly Undo? _savedUndo;
    readonly IImageService? _savedService;

    public EventMapChangePointerImportTests()
    {
        _savedRom = CoreState.ROM;
        _savedUndo = CoreState.Undo;
        _savedService = CoreState.ImageService;
    }

    public void Dispose()
    {
        CoreState.ROM = _savedRom;
        CoreState.Undo = _savedUndo;
        CoreState.ImageService = _savedService;
    }

    // ================================================================
    // PointerImport — happy path: copies SOURCE bytes to free space,
    // repoints P8, ambient-undo captures it, rollback restores.
    // ================================================================

    [Fact]
    public void ImportChangeDataFromPointer_CopiesSource_RepointsP8_UnderUndo()
    {
        ROM rom = MakeFe8uRom();
        CoreState.ROM = rom;
        CoreState.Undo = new Undo();

        // One change record (W=2, H=3 → 12 u16 = 24 source bytes) + 0xFF terminator.
        const int W = 2, H = 3;
        PlantChangeRecord(rom, TableBase, no: 1, x: 0, y: 0, w: W, h: H, p8: 0u);
        rom.Data[(int)(TableBase + SIZE)] = 0xFF; // terminator

        // Plant a distinctive SOURCE block of W*H*2 bytes.
        int srcLen = W * H * 2;
        for (int i = 0; i < srcLen; i++) rom.Data[(int)SrcData + i] = (byte)(0xA0 + i);
        // Free region the RecycleAddress append will land in.
        PlantFreeRegion(rom, FreeRegion, 0x2000);

        var vm = new EventMapChangeViewModel();
        Assert.True(vm.LoadEntryForMap(0u));
        Assert.Equal((uint)W, vm.B3);
        Assert.Equal((uint)H, vm.B4);

        byte[] snap = (byte[])rom.Data.Clone();

        // Import the SOURCE under an ambient undo scope (mirrors the View).
        var ud = CoreState.Undo!.NewUndoData("import test");
        string err;
        using (ROM.BeginUndoScope(ud))
        {
            err = vm.ImportChangeDataFromPointer(SrcData);
        }
        Assert.Equal("", err);
        CoreState.Undo.Push(ud);

        // P8 now points at a NEW free-space address (not 0).
        uint newP8 = vm.P8;
        Assert.NotEqual(0u, newP8);

        // #964 review: the in-memory P8 must be a ROM OFFSET (< 0x08000000),
        // NOT a GBA pointer — matching the load path (rom.p32 → U.toOffset) and
        // every consumer (RenderChangePreview, GetDataReport). A regression here
        // (P8 = U.toPointer(newAddr)) would flip P8 to ≥ 0x08000000.
        Assert.True(newP8 < 0x08000000u,
            $"P8 must be a ROM offset (< 0x08000000) after import, was 0x{newP8:X08}");

        uint newOffset = U.toOffset(newP8); // idempotent on an offset
        Assert.Equal(newP8, newOffset);     // proves P8 is already an offset
        Assert.True(U.isSafetyOffset(newOffset, rom));
        Assert.NotEqual(SrcData, newOffset); // a COPY, not an alias

        // The slot's P8 (CurrentAddr+8) was written to the new address, and the
        // in-memory P8 AGREES with the offset actually written to the ROM slot.
        uint slotOffset = U.toOffset(rom.p32(vm.CurrentAddr + 8));
        Assert.Equal(newOffset, slotOffset);
        Assert.Equal(newP8, slotOffset); // in-memory P8 == ROM slot offset

        // The copied bytes are byte-identical to the SOURCE.
        for (int i = 0; i < srcLen; i++)
            Assert.Equal((byte)(0xA0 + i), rom.u8(newOffset + (uint)i));

        // Rollback restores byte-identity.
        CoreState.Undo.RunUndo();
        Assert.Equal(snap, rom.Data);
    }

    // ================================================================
    // PointerImport — refusal cases (NO mutation).
    // ================================================================

    [Fact]
    public void ImportChangeDataFromPointer_NoEntryLoaded_ReturnsError_NoMutation()
    {
        ROM rom = MakeFe8uRom();
        CoreState.ROM = rom;
        CoreState.Undo = new Undo();

        var vm = new EventMapChangeViewModel(); // never loaded → CurrentAddr == 0
        byte[] snap = (byte[])rom.Data.Clone();

        var ud = CoreState.Undo!.NewUndoData("t");
        string err;
        using (ROM.BeginUndoScope(ud)) { err = vm.ImportChangeDataFromPointer(SrcData); }
        Assert.False(string.IsNullOrEmpty(err));
        Assert.Equal(snap, rom.Data);
    }

    [Fact]
    public void ImportChangeDataFromPointer_ZeroWidthOrHeight_ReturnsError_NoMutation()
    {
        ROM rom = MakeFe8uRom();
        CoreState.ROM = rom;
        CoreState.Undo = new Undo();

        // Record with W=0 → nothing to copy.
        PlantChangeRecord(rom, TableBase, no: 1, x: 0, y: 0, w: 0, h: 3, p8: 0u);
        rom.Data[(int)(TableBase + SIZE)] = 0xFF;

        var vm = new EventMapChangeViewModel();
        Assert.True(vm.LoadEntryForMap(0u));
        byte[] snap = (byte[])rom.Data.Clone();

        var ud = CoreState.Undo!.NewUndoData("t");
        string err;
        using (ROM.BeginUndoScope(ud)) { err = vm.ImportChangeDataFromPointer(SrcData); }
        Assert.False(string.IsNullOrEmpty(err));
        Assert.Equal(snap, rom.Data);
    }

    [Fact]
    public void ImportChangeDataFromPointer_SourcePastRomEnd_ReturnsError_NoMutation()
    {
        ROM rom = MakeFe8uRom();
        CoreState.ROM = rom;
        CoreState.Undo = new Undo();

        const int W = 4, H = 4;
        PlantChangeRecord(rom, TableBase, no: 1, x: 0, y: 0, w: W, h: H, p8: 0u);
        rom.Data[(int)(TableBase + SIZE)] = 0xFF;
        PlantFreeRegion(rom, FreeRegion, 0x2000);

        var vm = new EventMapChangeViewModel();
        Assert.True(vm.LoadEntryForMap(0u));
        byte[] snap = (byte[])rom.Data.Clone();

        // Source very near EOF so W*H*2 = 32 bytes runs off the end.
        uint nearEof = (uint)rom.Data.Length - 4;
        var ud = CoreState.Undo!.NewUndoData("t");
        string err;
        using (ROM.BeginUndoScope(ud)) { err = vm.ImportChangeDataFromPointer(nearEof); }
        Assert.False(string.IsNullOrEmpty(err));
        Assert.Equal(snap, rom.Data);
    }

    [Fact]
    public void ImportChangeDataFromPointer_AcceptsGbaPointer_AndRawOffset_Equivalently()
    {
        ROM rom = MakeFe8uRom();
        CoreState.ROM = rom;
        CoreState.Undo = new Undo();

        const int W = 1, H = 1;
        PlantChangeRecord(rom, TableBase, no: 1, x: 0, y: 0, w: W, h: H, p8: 0u);
        rom.Data[(int)(TableBase + SIZE)] = 0xFF;
        rom.Data[(int)SrcData + 0] = 0xDE;
        rom.Data[(int)SrcData + 1] = 0xAD;
        PlantFreeRegion(rom, FreeRegion, 0x2000);

        var vm = new EventMapChangeViewModel();
        Assert.True(vm.LoadEntryForMap(0u));

        // Pass a GBA pointer (>= 0x08000000) — VM normalises via U.toOffset.
        var ud = CoreState.Undo!.NewUndoData("t");
        string err;
        using (ROM.BeginUndoScope(ud)) { err = vm.ImportChangeDataFromPointer(U.toPointer(SrcData)); }
        Assert.Equal("", err);
        CoreState.Undo!.Push(ud);

        uint newOffset = U.toOffset(vm.P8);
        Assert.Equal((uint)0xDE, rom.u8(newOffset + 0));
        Assert.Equal((uint)0xAD, rom.u8(newOffset + 1));
    }

    // ================================================================
    // FE7 obj2 high byte — RenderChangePreview resolves the secondary
    // tileset for FE7 (obj_plist high byte) and renders a non-null image.
    // ================================================================

    [Fact]
    public void RenderChangePreview_FE7_Obj2HighByte_ResolvesSecondTileset()
    {
        CoreState.ImageService = new SkiaImageService();
        ROM rom = MakeFe7uRomWithObj2();
        CoreState.ROM = rom;
        CoreState.Undo = new Undo();

        var vm = new EventMapChangeViewModel();
        Assert.True(vm.LoadEntryForMap(0u), "VM must load the FE7 change entry");

        IImage img = vm.RenderChangePreview();
        // The render must succeed (non-null) AND the export gate is set — proving
        // the obj2 high-byte path resolved the second tileset and concatenated it
        // (the single config descriptor references a tile that lives in obj2).
        Assert.NotNull(img);
        Assert.True(vm.CanExportChange);
    }

    [Fact]
    public void RenderChangePreview_FE7_Obj2HighByteUnresolvable_ReturnsNull()
    {
        CoreState.ImageService = new SkiaImageService();
        // Build the FE7 ROM but blank the obj2 PLIST entry so the high byte points
        // at a null/invalid slot → the whole render must fail (WF bail parity).
        ROM rom = MakeFe7uRomWithObj2(breakObj2: true);
        CoreState.ROM = rom;
        CoreState.Undo = new Undo();

        var vm = new EventMapChangeViewModel();
        Assert.True(vm.LoadEntryForMap(0u));

        IImage img = vm.RenderChangePreview();
        Assert.Null(img);
        Assert.False(vm.CanExportChange);
    }

    // ================================================================
    // Wiring parity — the View handler is no longer the stub.
    // ================================================================

    [Fact]
    public void View_PointerImport_Click_IsWiredNotStub()
    {
        string repoRoot = FindRepoRoot();
        string path = Path.Combine(repoRoot, "FEBuilderGBA.Avalonia", "Views",
            "EventMapChangeView.axaml.cs");
        Assert.True(File.Exists(path));
        string src = File.ReadAllText(path);

        Assert.DoesNotContain("Pointer import is not yet implemented", src);
        Assert.Contains("ImportChangeDataFromPointer", src);
        Assert.Contains("_undoService.Begin(", src);
        Assert.Contains("_undoService.Rollback()", src);
        Assert.Contains("NumberInputDialog.Show", src);

        // #964 review: the dialog max must be the GBA cartridge ceiling so a GBA
        // pointer (≥ 0x08000000) is enterable — NOT Data.Length-1 (which both
        // capped below 0x08000000 and underflowed to 0xFFFFFFFF when Length==0).
        Assert.Contains("0x09FFFFFF", src);
        Assert.DoesNotContain("(CoreState.ROM?.Data?.Length ?? 1) - 1", src);
        // The ceiling chosen must itself admit a GBA pointer.
        const uint dialogMax = 0x09FFFFFFu;
        uint someGbaPointer = 0x08000000u + 0x1000u;
        Assert.True(someGbaPointer >= 0u && someGbaPointer <= dialogMax,
            "A GBA pointer must fall within the dialog [min,max] range");
    }

    // #964 review: a GBA-pointer-form source at the HIGH end of valid ROM space
    // (well above 0x08000000, i.e. only enterable because the dialog max is the
    // GBA ceiling rather than Data.Length-1) still imports correctly — the VM
    // normalises via U.toOffset and the real bounds check accepts it.
    [Fact]
    public void ImportChangeDataFromPointer_HighGbaPointerSource_WithinCeiling_Imports()
    {
        ROM rom = MakeFe8uRom();
        CoreState.ROM = rom;
        CoreState.Undo = new Undo();

        const int W = 1, H = 1;
        PlantChangeRecord(rom, TableBase, no: 1, x: 0, y: 0, w: W, h: H, p8: 0u);
        rom.Data[(int)(TableBase + SIZE)] = 0xFF;
        rom.Data[(int)SrcData + 0] = 0xBE;
        rom.Data[(int)SrcData + 1] = 0xEF;
        PlantFreeRegion(rom, FreeRegion, 0x2000);

        var vm = new EventMapChangeViewModel();
        Assert.True(vm.LoadEntryForMap(0u));

        // SrcData as a GBA pointer is ~0x08A00000 — far above 0x08000000, so it
        // was UNREACHABLE under the old Data.Length-1 cap, but valid ROM data and
        // within the new 0x09FFFFFF ceiling.
        uint gbaPointer = U.toPointer(SrcData);
        Assert.True(gbaPointer >= 0x08000000u && gbaPointer <= 0x09FFFFFFu);

        var ud = CoreState.Undo!.NewUndoData("t");
        string err;
        using (ROM.BeginUndoScope(ud)) { err = vm.ImportChangeDataFromPointer(gbaPointer); }
        Assert.Equal("", err);
        CoreState.Undo!.Push(ud);

        uint newOffset = U.toOffset(vm.P8);
        Assert.Equal((uint)0xBE, rom.u8(newOffset + 0));
        Assert.Equal((uint)0xEF, rom.u8(newOffset + 1));
    }

    [Fact]
    public void ViewModel_ImportChangeDataFromPointer_MethodExists_IsPublic()
    {
        var m = typeof(EventMapChangeViewModel).GetMethod(
            "ImportChangeDataFromPointer",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        Assert.NotNull(m);
        Assert.Equal(typeof(string), m!.ReturnType);
    }

    // ================================================================
    // Helpers
    // ================================================================

    static void PlantChangeRecord(ROM rom, uint addr, byte no, byte x, byte y,
        byte w, byte h, uint p8)
    {
        rom.Data[(int)addr + 0] = no;
        rom.Data[(int)addr + 1] = x;
        rom.Data[(int)addr + 2] = y;
        rom.Data[(int)addr + 3] = w;
        rom.Data[(int)addr + 4] = h;
        // bytes 5-7 = 0; p32 at +8.
        PlantU32(rom, addr + 8, p8);
    }

    static void PlantFreeRegion(ROM rom, uint start, int length)
    {
        int baseIdx = (int)start;
        for (int i = 0; i < length; i++) rom.Data[baseIdx + i] = 0xFF;
    }

    static void PlantU32(ROM rom, uint addr, uint value)
    {
        int idx = (int)addr;
        rom.Data[idx + 0] = (byte)(value & 0xFF);
        rom.Data[idx + 1] = (byte)((value >> 8) & 0xFF);
        rom.Data[idx + 2] = (byte)((value >> 16) & 0xFF);
        rom.Data[idx + 3] = (byte)((value >> 24) & 0xFF);
    }

    /// <summary>
    /// FE8U ROM (header "BE8E01") with one map setting at 0x800000 whose
    /// mapchange plist (byte +11) = 3, CHANGE plist table at 0x880000 entry 3 →
    /// TableBase. Mirrors EventMapChangeListExpandTests.MakeRom.
    /// </summary>
    static ROM MakeFe8uRom()
    {
        var bytes = new byte[0x1100000];

        uint mapTableBase = 0x00800000u;
        uint plistTableBase = 0x00880000u;

        uint[] mapSettingCandidates = { 0x0B5F98u, 0x0B61C0u, 0x0B6328u, 0x0B6500u, 0x03462Cu, 0xB5E68u };
        foreach (var slot in mapSettingCandidates)
            PlantU32At(bytes, slot, mapTableBase | 0x08000000u);

        uint mapSettingDataSize = 148u;
        int mapRecordBase = (int)mapTableBase;
        PlantU32At(bytes, (uint)mapRecordBase + 0, 0x08123456u);
        PlantU32At(bytes, (uint)mapRecordBase + 4, 0x00000001u);
        PlantU32At(bytes, (uint)mapRecordBase + 8, 0x00000001u);
        bytes[mapRecordBase + 11] = MapChangePlist;
        bytes[mapRecordBase + 12] = 0x00;

        int termBase = (int)(mapTableBase + mapSettingDataSize);
        bytes[termBase + 12] = 0xFF;

        PlantU32At(bytes, 0x0346ACu, plistTableBase | 0x08000000u);
        for (int i = 0; i < 4; i++)
            PlantU32At(bytes, plistTableBase + (uint)i * 4u, 0u);
        PlantU32At(bytes, plistTableBase + 3 * 4u, TableBase | 0x08000000u);

        var rom = new ROM();
        rom.LoadLow("synth-fe8u-961.gba", bytes, "BE8E01");
        return rom;
    }

    /// <summary>
    /// FE7U ROM (header "AE7E01") with one map whose obj_plist (map_setting +4)
    /// has BOTH a low byte (primary OBJ plist=1) and a HIGH byte (obj2 plist=2).
    /// Plants the OBJ/PAL/CONFIG PLIST tables + LZ77 data so RenderChangePreview
    /// can resolve everything. The single config descriptor references a tile that
    /// only exists once obj2 is appended — so a successful non-null render proves
    /// the obj2 concat. When <paramref name="breakObj2"/> the obj2 plist slot is
    /// nulled so the high-byte resolution fails.
    /// </summary>
    static ROM MakeFe7uRomWithObj2(bool breakObj2 = false)
    {
        var bytes = new byte[0x1100000];
        var rom = new ROM();
        rom.LoadLow("synth-fe7u-961.gba", bytes, "AE7E01");

        var info = rom.RomInfo;
        uint mapSettingPtr  = info.map_setting_pointer;
        uint objPtr         = info.map_obj_pointer;
        uint palPtr         = info.map_pal_pointer;
        uint cfgPtr         = info.map_config_pointer;
        uint changePtr      = info.map_mapchange_pointer;

        // Layout (ROM offsets).
        uint mapTableBase   = 0x00800000u;
        uint objTableBase   = 0x00810000u;
        uint palTableBase   = 0x00820000u;
        uint cfgTableBase   = 0x00830000u;
        uint changeTableBase= 0x00840000u;

        uint objData        = 0x00860000u; // primary OBJ LZ77 (2 tiles, all 0)
        uint obj2Data       = 0x00868000u; // obj2 OBJ LZ77 (1 tile, all 0)
        uint palData        = 0x00870000u; // RAW 512 palette
        uint cfgData        = 0x00878000u; // config LZ77 (1 descriptor)
        uint changeData     = 0x00880000u; // RAW u16 change tile array

        // Point the per-version table pointers at our synthetic tables.
        PlantU32At(bytes, mapSettingPtr, mapTableBase | 0x08000000u);
        PlantU32At(bytes, objPtr,        objTableBase | 0x08000000u);
        PlantU32At(bytes, palPtr,        palTableBase | 0x08000000u);
        PlantU32At(bytes, cfgPtr,        cfgTableBase | 0x08000000u);
        PlantU32At(bytes, changePtr,     changeTableBase | 0x08000000u);

        // Map setting record 0:
        //   +0  valid pointer (so the record is "valid")
        //   +4  obj_plist u16 = 0x0201 (low=1 primary, high=2 obj2)
        //   +6  palette_plist = 1
        //   +7  config_plist  = 1
        //   +8  mappointer_plist = 1
        //   +11 mapchange_plist = 1
        int mr = (int)mapTableBase;
        PlantU32At(bytes, (uint)mr + 0, 0x08123456u);
        bytes[mr + 4] = 0x01;            // obj_plist low byte (primary = 1)
        bytes[mr + 5] = 0x02;            // obj_plist high byte (obj2 = 2)
        bytes[mr + 6] = 0x01;            // palette_plist
        bytes[mr + 7] = 0x01;            // config_plist
        bytes[mr + 8] = 0x01;            // mappointer_plist
        bytes[mr + 11] = 0x01;           // mapchange_plist
        // Terminator next record.
        int term = (int)(mapTableBase + info.map_setting_datasize);
        bytes[term + 11] = 0xFF;

        // OBJ plist table: slot 1 → primary objData; slot 2 → obj2Data.
        PlantU32At(bytes, objTableBase + 1 * 4u, objData | 0x08000000u);
        if (!breakObj2)
            PlantU32At(bytes, objTableBase + 2 * 4u, obj2Data | 0x08000000u);
        // else: slot 2 left 0 → obj2 high-byte resolution fails.

        // PAL plist table: slot 1 → palData.
        PlantU32At(bytes, palTableBase + 1 * 4u, palData | 0x08000000u);
        // CONFIG plist table: slot 1 → cfgData.
        PlantU32At(bytes, cfgTableBase + 1 * 4u, cfgData | 0x08000000u);
        // CHANGE plist table: slot 1 → the change record block.
        uint changeRecordBlock = 0x00890000u;
        PlantU32At(bytes, changeTableBase + 1 * 4u, changeRecordBlock | 0x08000000u);

        // Primary OBJ: 2 tiles (64 bytes) all index 0.
        PlantLz77(bytes, objData, new byte[64]);
        // obj2 OBJ: 1 tile (32 bytes) all index 0.
        PlantLz77(bytes, obj2Data, new byte[32]);
        // Palette: 512 raw bytes (color 0 = nonzero so opaque render is visible).
        bytes[(int)palData + 0] = 0x01; bytes[(int)palData + 1] = 0x00;
        // Config: 1 descriptor (8 bytes), four subtiles → TILE INDEX 2 (obj2 tile).
        byte[] cfgRaw = new byte[8];
        for (int s = 0; s < 4; s++) { cfgRaw[s * 2] = 0x02; cfgRaw[s * 2 + 1] = 0x00; }
        PlantLz77(bytes, cfgData, cfgRaw);
        // Change data block (the record's P8 destination): 1×1 RAW u16 = tile 0.
        bytes[(int)changeData + 0] = 0x00; bytes[(int)changeData + 1] = 0x00;

        // The change RECORD (12 bytes) at changeRecordBlock: no=1, x=0, y=0,
        // W=1, H=1, P8 → changeData. Terminator after.
        int crb = (int)changeRecordBlock;
        bytes[crb + 0] = 0x01;       // no
        bytes[crb + 3] = 0x01;       // W
        bytes[crb + 4] = 0x01;       // H
        PlantU32At(bytes, changeRecordBlock + 8, changeData | 0x08000000u);
        bytes[crb + (int)SIZE] = 0xFF; // terminator

        return rom;
    }

    static void PlantLz77(byte[] bytes, uint offset, byte[] raw)
    {
        byte[] comp = LZ77.compress(raw);
        Array.Copy(comp, 0, bytes, (int)offset, comp.Length);
    }

    static void PlantU32At(byte[] bytes, uint addr, uint value)
    {
        int idx = (int)addr;
        bytes[idx + 0] = (byte)(value & 0xFF);
        bytes[idx + 1] = (byte)((value >> 8) & 0xFF);
        bytes[idx + 2] = (byte)((value >> 16) & 0xFF);
        bytes[idx + 3] = (byte)((value >> 24) & 0xFF);
    }

    static string FindRepoRoot()
    {
        string dir = AppContext.BaseDirectory;
        for (int i = 0; i < 12 && dir != null; i++)
        {
            if (File.Exists(Path.Combine(dir, "FEBuilderGBA.sln"))) return dir;
            dir = Path.GetDirectoryName(dir);
        }
        return AppContext.BaseDirectory;
    }
}
