// SPDX-License-Identifier: GPL-3.0-or-later
// #1539 — AddressListControl opt-in structural-edit context menu (Paste/Swap/Clear).
//
// Verifies the shared AddressListControl's WinForms-parity structural-edit menu:
//   - default control is copy-only (no Paste/Swap/Clear) until EnableStructuralEdit;
//   - EnableStructuralEdit appends the expected menu items and is idempotent;
//   - Paste validates the WinForms clipboard header + byte count, then writes the
//     block under undo + reloads (and rejects a mismatched header without mutation);
//   - Swap crosses two adjacent UNDERLYING rows (correct under a filter);
//   - Clear zero-fills the selected row's block;
//   - the optional row-0 guard blocks a write when the row id is 0.
//
// All operations run against a tiny synthetic in-memory ROM (no real .gba needed);
// CoreState.ROM/Undo/Services are mutated, so [Collection("SharedState")].
using System.Collections.Generic;
using System.Linq;
using global::Avalonia.Controls;
using global::Avalonia.Headless.XUnit;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.Controls;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests;

[Collection("SharedState")]
public class AddressListControlStructuralEditTests
{
    const int BlockSize = 4;
    // Base must clear the 0x0..0x200 danger zone that U.isSafetyOffset rejects.
    const uint Base = 0x300;

    /// <summary>A Yes-returning IAppServices so confirmation dialogs proceed in headless tests.</summary>
    sealed class YesServices : IAppServices
    {
        public bool LastWasError;
        public void ShowError(string message) { LastWasError = true; }
        public void ShowInfo(string message) { }
        public bool ShowQuestion(string message) => true;
        public bool ShowYesNo(string message) => true;
        public void RunOnUIThread(System.Action action) => action();
        public bool IsMainThread() => true;
    }

    sealed class NoServices : IAppServices
    {
        public void ShowError(string message) { }
        public void ShowInfo(string message) { }
        public bool ShowQuestion(string message) => false;
        public bool ShowYesNo(string message) => false;
        public void RunOnUIThread(System.Action action) => action();
        public bool IsMainThread() => true;
    }

    /// <summary>Build a 4 KB ROM with N BlockSize rows starting at Base, each row's
    /// first byte = its index+1 so we can tell rows apart.</summary>
    static ROM MakeRom(int rows)
    {
        var data = new byte[0x1000];
        for (int i = 0; i < rows; i++)
        {
            uint a = Base + (uint)(i * BlockSize);
            data[a] = (byte)(i + 1); // distinctive marker
        }
        var rom = new ROM();
        rom.SwapNewROMDataDirect(data);
        CoreState.ROM = rom;
        CoreState.Undo = new Undo();
        return rom;
    }

    static List<AddrResult> MakeItems(int rows)
    {
        var list = new List<AddrResult>();
        for (int i = 0; i < rows; i++)
            list.Add(new AddrResult(Base + (uint)(i * BlockSize), $"Row {i}", (uint)i));
        return list;
    }

    static AddressListControl MakeEnabledControl(int rows, IAppServices services, bool useSwap = true, bool useClear = true,
        System.Func<uint, bool>? guard = null)
    {
        CoreState.Services = services;
        var items = MakeItems(rows);
        var control = new AddressListControl();
        control.SetItems(items);
        control.EnableStructuralEdit(
            BlockSize,
            reload: () => MakeItems(rows),
            writeProtectId00: guard,
            useSwap: useSwap,
            useClear: useClear,
            clipboardListName: "AddressList",
            clipboardFormName: "SoundRoomForm");
        return control;
    }

    static int MenuItemCount(AddressListControl control)
    {
        var list = control.FindControl<ListBox>("AddressList");
        var menu = list!.ContextMenu;
        return menu!.Items.OfType<MenuItem>().Count();
    }

    // ---- default copy-only -------------------------------------------

    [AvaloniaFact]
    public void Default_IsCopyOnly_ThreeMenuItems()
    {
        var control = new AddressListControl();
        Assert.False(control.StructuralEditEnabled);
        // The AXAML menu ships 3 copy items (Copy Address/Name/Hex Data).
        Assert.Equal(3, MenuItemCount(control));
    }

    // ---- EnableStructuralEdit appends items + idempotent --------------

    [AvaloniaFact]
    public void Enable_AppendsStructuralItems()
    {
        var control = MakeEnabledControl(3, new YesServices());
        Assert.True(control.StructuralEditEnabled);
        // 3 copy + Copy(block) + Paste + Swap Up + Swap Down + Invalidate = 8.
        Assert.Equal(8, MenuItemCount(control));
    }

    [AvaloniaFact]
    public void Enable_NoSwapNoClear_AddsOnlyCopyAndPaste()
    {
        var control = MakeEnabledControl(3, new YesServices(), useSwap: false, useClear: false);
        // 3 copy + Copy(block) + Paste = 5.
        Assert.Equal(5, MenuItemCount(control));
    }

    [AvaloniaFact]
    public void Enable_IsIdempotent()
    {
        var control = MakeEnabledControl(3, new YesServices());
        int after1 = MenuItemCount(control);
        // Second call must be a no-op.
        control.EnableStructuralEdit(BlockSize, () => MakeItems(3), useSwap: true, useClear: true);
        Assert.Equal(after1, MenuItemCount(control));
    }

    // ---- Paste --------------------------------------------------------

    [AvaloniaFact]
    public void Paste_ValidHeader_WritesBlock()
    {
        var rom = MakeRom(3);
        var control = MakeEnabledControl(3, new YesServices());
        control.SelectByIndex(1); // row 1 at Base+4

        string text = AddressListClipboardCore.Serialize("AddressList", "SoundRoomForm",
            new byte[] { 0xAA, 0xBB, 0xCC, 0xDD });
        bool committed = control.PasteFromText(text);

        Assert.True(committed);
        uint a = Base + 4;
        Assert.Equal(0xAAu, rom.u8(a));
        Assert.Equal(0xBBu, rom.u8(a + 1));
        Assert.Equal(0xCCu, rom.u8(a + 2));
        Assert.Equal(0xDDu, rom.u8(a + 3));
    }

    [AvaloniaFact]
    public void Paste_WrongHeader_NoMutation()
    {
        var rom = MakeRom(3);
        var control = MakeEnabledControl(3, new YesServices());
        control.SelectByIndex(1);
        uint before = rom.u8(Base + 4);

        // Wrong form name in the header — must be rejected.
        string text = AddressListClipboardCore.Serialize("AddressList", "SoundRoomFE6Form",
            new byte[] { 0xAA, 0xBB, 0xCC, 0xDD });
        bool committed = control.PasteFromText(text);

        Assert.False(committed);
        Assert.Equal(before, rom.u8(Base + 4));
    }

    [AvaloniaFact]
    public void Paste_UserDeclines_NoMutation()
    {
        var rom = MakeRom(3);
        var control = MakeEnabledControl(3, new NoServices()); // ShowYesNo -> false
        control.SelectByIndex(1);
        uint before = rom.u8(Base + 4);

        string text = AddressListClipboardCore.Serialize("AddressList", "SoundRoomForm",
            new byte[] { 0xAA, 0xBB, 0xCC, 0xDD });
        bool committed = control.PasteFromText(text);

        Assert.False(committed);
        Assert.Equal(before, rom.u8(Base + 4));
    }

    // ---- Swap ---------------------------------------------------------

    [AvaloniaFact]
    public void Swap_Down_CrossesAdjacentRows()
    {
        var rom = MakeRom(3);
        var control = MakeEnabledControl(3, new YesServices());
        control.SelectByIndex(0); // row 0 (marker 1) at Base
        // row 0 first byte = 1, row 1 first byte = 2.
        Assert.Equal(1u, rom.u8(Base));
        Assert.Equal(2u, rom.u8(Base + 4));

        control.SwapData(true); // swap with the row below

        // After swap row 0 holds the old row-1 bytes (marker 2) and vice versa.
        Assert.Equal(2u, rom.u8(Base));
        Assert.Equal(1u, rom.u8(Base + 4));
    }

    [AvaloniaFact]
    public void Swap_Up_AtTop_NoMutation()
    {
        var rom = MakeRom(3);
        var control = MakeEnabledControl(3, new YesServices());
        control.SelectByIndex(0);
        uint before = rom.u8(Base);
        control.SwapData(false); // no row above row 0
        Assert.Equal(before, rom.u8(Base));
    }

    [AvaloniaFact]
    public void Swap_UnderFilter_OperatesOnUnderlyingRows()
    {
        var rom = MakeRom(3);
        var control = MakeEnabledControl(3, new YesServices());
        // Filter so only "Row 1" is visible — its neighbours are hidden, so swap
        // should NOT silently reorder hidden rows.
        control.ApplySearchFilter("Row 1");
        control.SelectByIndex(1); // select underlying row 1 (now the only visible)
        uint before0 = rom.u8(Base);
        uint before1 = rom.u8(Base + 4);
        control.SwapData(true); // neighbour (row 2) is hidden -> suppressed
        Assert.Equal(before0, rom.u8(Base));
        Assert.Equal(before1, rom.u8(Base + 4));
    }

    // ---- Clear --------------------------------------------------------

    [AvaloniaFact]
    public void Clear_ZeroFillsSelectedRow()
    {
        var rom = MakeRom(3);
        var control = MakeEnabledControl(3, new YesServices());
        control.SelectByIndex(2); // row 2 (marker 3) at Base+8
        Assert.Equal(3u, rom.u8(Base + 8));
        control.ClearData();
        for (uint i = 0; i < BlockSize; i++)
            Assert.Equal(0u, rom.u8(Base + 8 + i));
    }

    // ---- Row-0 guard --------------------------------------------------

    [AvaloniaFact]
    public void RowZeroGuard_BlocksWriteWhenIdIsZero()
    {
        // Build a ROM whose row 0 id (first u16) is 0; guard denies id==0.
        var data = new byte[0x1000];
        // row 0 first u16 left as 0; mark others.
        data[Base + 4] = 2;
        var rom = new ROM();
        rom.SwapNewROMDataDirect(data);
        CoreState.ROM = rom;
        CoreState.Undo = new Undo();
        CoreState.Services = new YesServices();

        var items = MakeItems(3);
        var control = new AddressListControl();
        control.SetItems(items);
        control.EnableStructuralEdit(BlockSize, () => MakeItems(3),
            writeProtectId00: id => id != 0, // deny when id == 0
            useSwap: true, useClear: true,
            clipboardListName: "AddressList", clipboardFormName: "SoundRoomForm");

        control.SelectByIndex(0); // row 0, id == 0 -> guard denies
        control.ClearData();
        // No mutation: marker at row 1 still present, row 0 untouched (already 0 anyway,
        // so assert via the guard's ShowError side effect).
        var svc = (YesServices)CoreState.Services;
        Assert.True(svc.LastWasError);
    }
}
