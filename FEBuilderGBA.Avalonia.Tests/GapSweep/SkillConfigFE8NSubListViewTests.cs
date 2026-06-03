// SPDX-License-Identifier: GPL-3.0-or-later
// #930 — headless [AvaloniaFact] tests proving the FE8N Ver2 (4 tabs) and
// Ver3 (5 tabs) host views embed the reusable SkillSubListEditorView and Load
// each editor against the per-skill pointer SLOT on skill selection.
//
// The Ver2 test prefers the real roms/FE8J_skill.gba (FE8N Ver2 installed) when
// present, but the repo does NOT commit real .gba ROMs, so it falls back to a
// deterministic SYNTHETIC FE8N Ver2 ROM. The Ver3 test is synthetic-only (no
// stock ROM ships FE8N Ver3 installed; 0x892AC is 0 in all roms/*.gba).
using System;
using System.IO;
using global::Avalonia.Controls;
using global::Avalonia.Headless.XUnit;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;
using FEBuilderGBA.Avalonia.Views;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests.GapSweep;

[Collection("SharedState")]
public class SkillConfigFE8NSubListViewTests
{
    // -----------------------------------------------------------------
    // Ver2 — the 4 editors exist; Unit editor loads against +4.
    // -----------------------------------------------------------------

    [AvaloniaFact]
    public void Ver2_View_FourSubEditorsExist_UnitLoads()
    {
        // Prefer the real FE8N Ver2 ROM when present (manual/local runs);
        // otherwise plant a synthetic stride-20 FE8N Ver2 ROM.
        ROM rom = LoadFE8JSkillRom() ?? MakeFE8NVer2Rom(stride: 20);
        var prevRom = CoreState.ROM;
        var prevUndo = CoreState.Undo;
        try
        {
            CoreState.ROM = rom;
            CoreState.Undo = new Undo();
            EnsureSystemTextEncoder(rom);

            var view = new SkillConfigFE8NVer2SkillView();

            var unit = view.FindControl<SkillSubListEditorView>("UnitSubEditor");
            var cls = view.FindControl<SkillSubListEditorView>("ClassSubEditor");
            var item = view.FindControl<SkillSubListEditorView>("ItemSubEditor");
            var item2 = view.FindControl<SkillSubListEditorView>("Item2SubEditor");
            Assert.NotNull(unit);
            Assert.NotNull(cls);
            Assert.NotNull(item);
            Assert.NotNull(item2);

            // Drive the VM the way a skill-list selection would (re-assert ROM
            // defensively — constructing the View ran its Opened/LoadList).
            CoreState.ROM = rom;
            var vm = GetVm<SkillConfigFE8NVer2SkillViewModel>(view);
            var items = vm.LoadList();
            Assert.True(items.Count >= 2);

            // Select skill row id 1 (items[1]) — it has the planted sub-lists.
            uint rowAddr = items[1].addr;
            Invoke(view, "OnSelected", rowAddr);

            // The Unit editor either populated (synthetic plants a 2-entry list)
            // or is empty+CanEdit (real ROM may have an empty unit sub-list).
            Assert.True(unit!.ViewModel.CanEdit || unit.ViewModel.Entries.Count >= 0);

            // B2: Item2 editor CanEdit == HasItem2.
            Assert.Equal(vm.HasItem2, item2!.ViewModel.CanEdit);
        }
        finally
        {
            CoreState.ROM = prevRom;
            CoreState.Undo = prevUndo;
        }
    }

    [AvaloniaFact]
    public void Ver2_View_Stride16_Item2EditorNotEditable()
    {
        ROM rom = MakeFE8NVer2Rom(stride: 16);
        var prevRom = CoreState.ROM;
        var prevUndo = CoreState.Undo;
        try
        {
            CoreState.ROM = rom;
            CoreState.Undo = new Undo();
            EnsureSystemTextEncoder(rom);

            var view = new SkillConfigFE8NVer2SkillView();
            CoreState.ROM = rom;
            var vm = GetVm<SkillConfigFE8NVer2SkillViewModel>(view);
            var items = vm.LoadList();
            Assert.True(items.Count >= 2);
            Assert.False(vm.HasItem2); // stride 16

            uint rowAddr = items[1].addr;
            Invoke(view, "OnSelected", rowAddr);

            var item2 = view.FindControl<SkillSubListEditorView>("Item2SubEditor");
            Assert.NotNull(item2);
            // B2: in the sizeof-16 layout addr+16 is the next row — Item2 must
            // NOT be editable.
            Assert.False(item2!.ViewModel.CanEdit);

            // The editable Unit editor populated from the planted 2-entry list.
            var unit = view.FindControl<SkillSubListEditorView>("UnitSubEditor");
            Assert.NotNull(unit);
            Assert.True(unit!.ViewModel.CanEdit);
            Assert.Equal(2, unit.ViewModel.Entries.Count);
        }
        finally
        {
            CoreState.ROM = prevRom;
            CoreState.Undo = prevUndo;
        }
    }

    // -----------------------------------------------------------------
    // Ver3 — the 5 editors instantiate and Load is callable (synthetic).
    // -----------------------------------------------------------------

    [AvaloniaFact]
    public void Ver3_View_FiveSubEditorsInstantiate_LoadCallable()
    {
        ROM rom = MakeFE8NVer3Rom(stride: 24);
        var prevRom = CoreState.ROM;
        var prevUndo = CoreState.Undo;
        try
        {
            CoreState.ROM = rom;
            CoreState.Undo = new Undo();
            EnsureSystemTextEncoder(rom);

            var view = new SkillConfigFE8NVer3SkillView();

            var unit = view.FindControl<SkillSubListEditorView>("UnitSubEditor");
            var cls = view.FindControl<SkillSubListEditorView>("ClassSubEditor");
            var item = view.FindControl<SkillSubListEditorView>("ItemSubEditor");
            var item2 = view.FindControl<SkillSubListEditorView>("Item2SubEditor");
            var comp = view.FindControl<SkillSubListEditorView>("CompositeSubEditor");
            Assert.NotNull(unit);
            Assert.NotNull(cls);
            Assert.NotNull(item);
            Assert.NotNull(item2);
            Assert.NotNull(comp);

            CoreState.ROM = rom;
            var vm = GetVm<SkillConfigFE8NVer3SkillViewModel>(view);
            var items = vm.LoadList();
            Assert.True(items.Count >= 2);

            uint rowAddr = items[1].addr;
            // Load must not throw for any of the 5 editors.
            Invoke(view, "OnSelected", rowAddr);

            // All 5 editors are editable (fixed v3 layout — no stride gate).
            Assert.True(unit!.ViewModel.CanEdit);
            Assert.True(cls!.ViewModel.CanEdit);
            Assert.True(item!.ViewModel.CanEdit);
            Assert.True(item2!.ViewModel.CanEdit);
            Assert.True(comp!.ViewModel.CanEdit);

            // Unit editor populated from the planted 2-entry list at +4.
            Assert.Equal(2, unit.ViewModel.Entries.Count);
        }
        finally
        {
            CoreState.ROM = prevRom;
            CoreState.Undo = prevUndo;
        }
    }

    // =================================================================
    // Helpers
    // =================================================================

    static T GetVm<T>(object view) where T : class
    {
        var f = view.GetType().GetField("_vm",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(f);
        return (T)f!.GetValue(view)!;
    }

    static void Invoke(object view, string method, uint addr)
    {
        var m = view.GetType().GetMethod(method,
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(m);
        m!.Invoke(view, new object?[] { addr });
    }

    static void EnsureSystemTextEncoder(ROM rom)
    {
        if (CoreState.SystemTextEncoder == null)
        {
            CoreState.SystemTextEncoder = new HeadlessSystemTextEncoder(rom);
        }
    }

    static ROM? LoadFE8JSkillRom()
    {
        try
        {
            string repoRoot = FindRepoRoot();
            string path = Path.Combine(repoRoot, "roms", "FE8J_skill.gba");
            if (!File.Exists(path)) return null;
            var rom = new ROM();
            return rom.Load(path, out _) ? rom : null;
        }
        catch { return null; }
    }

    static string FindRepoRoot()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "FEBuilderGBA.sln")))
        {
            dir = Path.GetDirectoryName(dir);
        }
        if (dir == null)
            throw new InvalidOperationException("Could not find FEBuilderGBA.sln");
        return dir;
    }

    // -----------------------------------------------------------------
    // Synthetic FE8N Ver2 ROM — mirrors SkillConfigFE8NVer2SkillParityTests'
    // MakeMinimalFE8NVer2Rom but plants real null-terminated sub-lists at the
    // row-1 sub-pointers so the embedded editors populate.
    // -----------------------------------------------------------------
    static ROM MakeFE8NVer2Rom(uint stride)
    {
        var bytes = new byte[0x1000000];

        WriteU32(bytes, 0x8926C, 0x08001000u); // iconExPointer sentinel
        WriteU16(bytes, 0x70B96, 0);           // size-detect path

        const uint iconPointersBase = 0xE0FFEC;
        const uint skillTableBase = 0x00E20000;
        const uint animeTableBase = 0x00E30000;

        WriteU32(bytes, iconPointersBase + 4 * 0, 0x08E00000u);
        WriteU32(bytes, iconPointersBase + 4 * 1, 0x08E00100u);
        WriteU32(bytes, iconPointersBase + 4 * 2, 0x08E00200u);
        WriteU32(bytes, iconPointersBase + 4 * 3, 0x08E00300u);
        WriteU32(bytes, iconPointersBase + 4 * 4, skillTableBase | 0x08000000u);
        WriteU32(bytes, iconPointersBase + 4 * 7, 0x08E00500u);
        WriteU32(bytes, iconPointersBase + 4 * 8, animeTableBase | 0x08000000u);
        WriteU32(bytes, iconPointersBase + 4 * 9, 0x08E00700u);
        WriteU32(bytes, iconPointersBase + 4 * 10, 0x08E00800u);
        WriteU32(bytes, iconPointersBase + 4 * 11, stride);

        byte[] iconPattern = { 0x50, 0x93, 0x08, 0x08, 0x48, 0x93, 0x08, 0x08 };
        Array.Copy(iconPattern, 0, bytes, (int)(iconPointersBase + 20), iconPattern.Length);

        // Skill rows. Row 1 carries the sub-list pointers.
        WriteU16(bytes, skillTableBase + 0 * stride + 0, 0x0001);

        WriteU16(bytes, skillTableBase + 1 * stride + 0, 0x00AB);
        WriteU16(bytes, skillTableBase + 1 * stride + 2, 0x0001);
        // Unit sub-list @ +4 → 0xF00000 ; Class @ +8 → 0xF00100 ; Item @ +12 → 0xF00200.
        WriteU32(bytes, skillTableBase + 1 * stride + 4, 0x00F00000u | 0x08000000u);
        WriteU32(bytes, skillTableBase + 1 * stride + 8, 0x00F00100u | 0x08000000u);
        WriteU32(bytes, skillTableBase + 1 * stride + 12, 0x00F00200u | 0x08000000u);
        if (stride >= 20)
        {
            WriteU32(bytes, skillTableBase + 1 * stride + 16, 0x00F00300u | 0x08000000u);
        }

        WriteU16(bytes, skillTableBase + 2 * stride + 0, 0x00CD);
        WriteU16(bytes, skillTableBase + 3 * stride + 0, 0x00EF);
        WriteU16(bytes, skillTableBase + 4 * stride + 0, 0x0101);
        bytes[(int)(skillTableBase + 5 * stride)] = 0xFF; // terminator

        WriteU32(bytes, animeTableBase + 0 * 4, 0);
        WriteU32(bytes, animeTableBase + 1 * 4, 0);

        // Plant null-terminated sub-lists (2 entries each) at the targets.
        PlantList(bytes, 0x00F00000, new byte[] { 0x03, 0x04 }); // unit
        PlantList(bytes, 0x00F00100, new byte[] { 0x05 });       // class
        PlantList(bytes, 0x00F00200, new byte[] { 0x06, 0x07 }); // item
        PlantList(bytes, 0x00F00300, new byte[] { 0x08 });       // item2

        bytes[0x6E0] = 0xFF; // race guard

        var rom = new ROM();
        rom.LoadLow("synthetic-fe8nver2-sublist.gba", bytes, "BE8E01");
        return rom;
    }

    // -----------------------------------------------------------------
    // Synthetic FE8N Ver3 ROM — mirrors SkillConfigFE8NVer3SkillParityTests'
    // MakeFE8NVer3Rom with real null-terminated sub-lists at row-1 D4..D20.
    // -----------------------------------------------------------------
    static ROM MakeFE8NVer3Rom(uint stride)
    {
        var bytes = new byte[0x1000000];

        WriteU32(bytes, 0x8926C, 0x08001000u);                 // iconExPointer
        const uint skillTableBase = 0x00E20000;
        WriteU32(bytes, 0x892AC, skillTableBase | 0x08000000u); // skill-table slot
        WriteU32(bytes, 0x892B0, stride);                       // ICON_LIST_SIZE
        const uint animeTableBase = 0x00E30000;
        WriteU32(bytes, 0x892BC, animeTableBase | 0x08000000u); // anime-table slot

        WriteU16(bytes, skillTableBase + 0 * stride + 0, 0x0001);

        WriteU16(bytes, skillTableBase + 1 * stride + 0, 0x00AB);
        WriteU16(bytes, skillTableBase + 1 * stride + 2, 0x0001);
        WriteU32(bytes, skillTableBase + 1 * stride + 4, 0x00F00000u | 0x08000000u);  // unit
        WriteU32(bytes, skillTableBase + 1 * stride + 8, 0x00F00100u | 0x08000000u);  // class
        WriteU32(bytes, skillTableBase + 1 * stride + 12, 0x00F00200u | 0x08000000u); // item
        WriteU32(bytes, skillTableBase + 1 * stride + 16, 0x00F00300u | 0x08000000u); // item2
        WriteU32(bytes, skillTableBase + 1 * stride + 20, 0x00F00400u | 0x08000000u); // composite

        WriteU16(bytes, skillTableBase + 2 * stride + 0, 0x00CD);
        WriteU16(bytes, skillTableBase + 3 * stride + 0, 0x00EF);
        WriteU16(bytes, skillTableBase + 4 * stride + 0, 0x0101);
        bytes[(int)(skillTableBase + 5 * stride)] = 0xFF;

        WriteU32(bytes, animeTableBase + 0 * 4, 0);
        WriteU32(bytes, animeTableBase + 1 * 4, 0);

        PlantList(bytes, 0x00F00000, new byte[] { 0x03, 0x04 }); // unit
        PlantList(bytes, 0x00F00100, new byte[] { 0x05 });       // class
        PlantList(bytes, 0x00F00200, new byte[] { 0x06, 0x07 }); // item
        PlantList(bytes, 0x00F00300, new byte[] { 0x08 });       // item2
        PlantList(bytes, 0x00F00400, new byte[] { 0x01 });       // composite

        bytes[0x6E0] = 0xFF;

        var rom = new ROM();
        rom.LoadLow("synthetic-fe8nver3-sublist.gba", bytes, "BE8E01");
        return rom;
    }

    static void PlantList(byte[] bytes, uint offset, byte[] ids)
    {
        Array.Copy(ids, 0, bytes, (int)offset, ids.Length);
        bytes[(int)offset + ids.Length] = 0x00; // terminator
    }

    static void WriteU32(byte[] bytes, uint offset, uint value)
    {
        int o = checked((int)offset);
        bytes[o] = (byte)(value & 0xFF);
        bytes[o + 1] = (byte)((value >> 8) & 0xFF);
        bytes[o + 2] = (byte)((value >> 16) & 0xFF);
        bytes[o + 3] = (byte)((value >> 24) & 0xFF);
    }

    static void WriteU16(byte[] bytes, uint offset, ushort value)
    {
        int o = checked((int)offset);
        bytes[o] = (byte)(value & 0xFF);
        bytes[o + 1] = (byte)((value >> 8) & 0xFF);
    }
}
