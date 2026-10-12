// SPDX-License-Identifier: GPL-3.0-or-later
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;
using FEBuilderGBA.Avalonia.Views;
using global::Avalonia.Controls;
using global::Avalonia.Headless.XUnit;
using global::Avalonia.Interactivity;
using global::Avalonia.Threading;
using global::Avalonia.VisualTree;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests;

[Collection("SharedState")]
public class MoveCostEditorIndependenceTests
{
    sealed class ConfirmingServices : IAppServices
    {
        public bool ShowYesNo(string message) => true;
        public bool ShowQuestion(string message) => throw new NotSupportedException();
        public void ShowError(string message) => throw new InvalidOperationException(message);
        public void ShowInfo(string message) => throw new NotSupportedException();
        public void RunOnUIThread(Action action) => Dispatcher.UIThread.Post(action);
        public bool IsMainThread() => Dispatcher.UIThread.CheckAccess();
    }

    static void DetachAndClose(MoveCostEditorView view)
    {
        view.GetHeadlessHost().Content = null;
        view.Close();
    }

    const uint ClassBase = 0x20000;
    const uint SharedTable = 0x30000;
    const uint ClassCount = 4;
    static ROM MakeRom(bool shareCostTable = true, string gameCode = "BE8E01")
    {
        var data = new byte[0x1000000];
        Array.Fill(data, (byte)0xFF, data.Length / 2, data.Length / 2);
        var rom = new ROM();
        rom.LoadLow("synthetic-movecost-avalonia.gba", data, gameCode);
        uint moveCostPointerOffset = rom.RomInfo.version == 6 ? 52u : 56u;
        rom.write_p32(rom.RomInfo.class_pointer, ClassBase);
        for (uint id = 0; id < ClassCount; id++)
            rom.write_u8(ClassBase + id * rom.RomInfo.class_datasize + 4, id == 0 ? (byte)0 : (byte)1);
        rom.write_p32(ClassBase + moveCostPointerOffset, SharedTable);
        rom.write_p32(ClassBase + rom.RomInfo.class_datasize + moveCostPointerOffset,
            shareCostTable ? SharedTable : SharedTable + 0x1000);
        return rom;
    }

    [Theory]
    [InlineData(true, CostType.MoveCostNormal, true)]
    [InlineData(false, CostType.MoveCostNormal, false)]
    [InlineData(true, CostType.TerrainRecovery, false)]
    [InlineData(true, CostType.BallistaMoveCost, false)]
    public void Independence_IsOfferedOnlyForSharedClassSpecificTables(
        bool shared, CostType costType, bool expected)
    {
        ROM? previousRom = CoreState.ROM;
        Undo? previousUndo = CoreState.Undo;
        try
        {
            CoreState.ROM = MakeRom(shared);
            var vm = new MoveCostEditorViewModel();
            vm.BuildCostTypeItems();
            Assert.NotEmpty(vm.LoadClassList());

            vm.LoadMoveCost(ClassBase, costType);

            Assert.Equal(expected, vm.CanMakeIndependent);
        }
        finally
        {
            CoreState.ROM = previousRom;
            CoreState.Undo = previousUndo;
        }
    }

    [Fact]
    public void MakeIndependent_UndoServiceCommit_RestoresSharedPointer()
    {
        ROM? previousRom = CoreState.ROM;
        Undo? previousUndo = CoreState.Undo;
        var previousAppend = CoreState.AppendBinaryData;
        ROM rom = MakeRom();
        try
        {
            CoreState.ROM = rom;
            CoreState.Undo = new Undo();
            CoreState.AppendBinaryData = (data, undo) =>
            {
                uint offset = rom.FindFreeSpace((uint)(rom.Data.Length / 2), (uint)data.Length);
                if (offset == U.NOT_FOUND) return U.NOT_FOUND;
                if (undo != null) rom.write_range(offset, data, undo);
                else rom.write_range(offset, data);
                return offset;
            };

            var vm = new MoveCostEditorViewModel();
            vm.BuildCostTypeItems();
            Assert.NotEmpty(vm.LoadClassList());
            vm.LoadMoveCost(ClassBase, CostType.MoveCostNormal);
            Assert.True(vm.CanMakeIndependent);

            uint moveCostPointerOffset = rom.RomInfo.version == 6 ? 52u : 56u;
            uint selectedPointerSlot = ClassBase + moveCostPointerOffset;
            uint originalPointer = rom.u32(selectedPointerSlot);
            var undoService = new UndoService();
            undoService.Begin("Make Move Cost Independent");
            Undo.UndoData? undoData = undoService.GetActiveUndoData();
            Assert.NotNull(undoData);

            uint cloneOffset = vm.MakeIndependent(undoData);
            Assert.NotEqual(U.NOT_FOUND, cloneOffset);
            undoService.Commit();
            Assert.Equal(U.toPointer(cloneOffset), rom.u32(selectedPointerSlot));

            CoreState.Undo.RunUndo();

            Assert.Equal(originalPointer, rom.u32(selectedPointerSlot));
            Assert.True(MoveCostIndependenceCore.IsTableShared(
                rom, ClassBase, selectedPointerSlot, ClassCount));
        }
        finally
        {
            CoreState.AppendBinaryData = previousAppend;
            CoreState.ROM = previousRom;
            CoreState.Undo = previousUndo;
        }
    }

    [Fact]
    public void Fe6_LoadAndWriteStopAtThe54ByteTableBoundary()
    {
        ROM? previousRom = CoreState.ROM;
        Undo? previousUndo = CoreState.Undo;
        var previousAppend = CoreState.AppendBinaryData;
        try
        {
            ROM rom = MakeRom(gameCode: "AFEJ01");
            const uint tableSize = 54;
            const byte sentinel = 0xA5;
            for (uint i = 0; i < tableSize; i++)
                rom.write_u8(SharedTable + i, (byte)i);
            rom.write_u8(SharedTable + tableSize, sentinel);
            CoreState.ROM = rom;
            CoreState.Undo = new Undo();
            CoreState.AppendBinaryData = null;

            uint selectedPointerSlot = ClassBase + 52;
            uint otherPointerSlot = ClassBase + rom.RomInfo.class_datasize + 52;
            uint sharedPointer = rom.u32(selectedPointerSlot);
            var undo = CoreState.Undo.NewUndoData("FE6 move cost independence");
            uint cloneOffset;
            using (ROM.BeginUndoScope(undo))
                cloneOffset = MoveCostIndependenceCore.MakeIndependent(
                    rom, ClassBase, selectedPointerSlot, ClassCount, undo);

            Assert.NotEqual(U.NOT_FOUND, cloneOffset);
            Assert.Equal(U.toPointer(cloneOffset), rom.u32(selectedPointerSlot));
            Assert.Equal(sharedPointer, rom.u32(otherPointerSlot));
            rom.write_u8(cloneOffset + tableSize, sentinel);

            var vm = new MoveCostEditorViewModel();
            vm.BuildCostTypeItems();
            Assert.NotEmpty(vm.LoadClassList());
            vm.LoadMoveCost(ClassBase, CostType.MoveCostNormal);

            Assert.True(vm.CanWrite);
            Assert.Equal(54, vm.SupportedTerrainCount);
            Assert.True(vm.IsTerrainEnabled(53));
            Assert.False(vm.IsTerrainEnabled(54));
            Assert.Equal(53, vm.GetCost(53));
            Assert.Equal(0, vm.GetCost(54));
            Assert.Equal((byte)53, rom.u8(cloneOffset + 53));
            Assert.Equal(sentinel, rom.u8(cloneOffset + tableSize));

            vm.SetCost(53, 0x3A);
            vm.SetCost(54, 0x77);
            vm.WriteMoveCost();

            Assert.Equal((byte)0x3A, rom.u8(cloneOffset + 53));
            Assert.Equal((byte)53, rom.u8(SharedTable + 53));
            Assert.Equal(sentinel, rom.u8(cloneOffset + tableSize));
        }
        finally
        {
            CoreState.AppendBinaryData = previousAppend;
            CoreState.ROM = previousRom;
            CoreState.Undo = previousUndo;
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MakeIndependent_PreservesPendingEditsAndWritesOnlyTheClone(bool editBeforeIndependence)
    {
        ROM? previousRom = CoreState.ROM;
        Undo? previousUndo = CoreState.Undo;
        var previousAppend = CoreState.AppendBinaryData;
        try
        {
            ROM rom = MakeRom();
            CoreState.ROM = rom;
            CoreState.Undo = new Undo();
            CoreState.AppendBinaryData = null;
            var vm = new MoveCostEditorViewModel();
            vm.BuildCostTypeItems();
            vm.LoadClassList();
            vm.LoadMoveCost(ClassBase, CostType.MoveCostNormal);
            if (editBeforeIndependence) vm.SetCost(0, 73);
            Assert.Equal(editBeforeIndependence, vm.IsDirty);

            var undo = CoreState.Undo.NewUndoData("Independence with pending edits");
            uint clone;
            using (ROM.BeginUndoScope(undo))
                clone = vm.MakeIndependent(undo);

            Assert.NotEqual(U.NOT_FOUND, clone);
            Assert.Equal(clone, vm.MoveCostAddr);
            byte expected = editBeforeIndependence ? (byte)73 : (byte)0;
            Assert.Equal(expected, vm.GetCost(0));
            Assert.Equal(editBeforeIndependence, vm.IsDirty);
            Assert.False(vm.CanMakeIndependent);
            Assert.Equal((byte)0, rom.u8(SharedTable));
            Assert.Equal((byte)0, rom.u8(clone));

            vm.WriteMoveCost();

            Assert.Equal(expected, rom.u8(clone));
            Assert.Equal((byte)0, rom.u8(SharedTable));
        }
        finally
        {
            CoreState.AppendBinaryData = previousAppend;
            CoreState.ROM = previousRom;
            CoreState.Undo = previousUndo;
        }
    }

    [AvaloniaFact]
    public void IndependenceClick_PreservesDirtyEditsAndUndoRefreshesWrittenValues()
    {
        var previousRom = CoreState.ROM;
        var previousUndo = CoreState.Undo;
        var previousAppend = CoreState.AppendBinaryData;
        var previousServices = CoreState.Services;
        var previousInvalidation = Undo.OnAllFormsInvalidated;
        MoveCostEditorView? view = null;
        try
        {
            ROM rom = MakeRom();
            CoreState.ROM = rom;
            CoreState.Undo = new Undo();
            CoreState.AppendBinaryData = null;
            CoreState.Services = new ConfirmingServices();
            view = new MoveCostEditorView();
            view.Show();
            view.NavigateTo(ClassBase);
            var firstCost = view.GetVisualDescendants().OfType<NumericUpDown>()
                .Single(control => control.Tag is int index && index == 0);
            firstCost.Value = 73;

            view.FindControl<Button>("IndependenceButton")!
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();

            uint clone = rom.p32(ClassBase + 56);
            Assert.NotEqual(SharedTable, clone);
            Assert.Equal(73m, firstCost.Value);
            Assert.True(view.DataViewModel!.IsDirty);
            view.FindControl<Button>("WriteButton")!
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.False(view.DataViewModel.IsDirty);
            Assert.Equal((byte)73, rom.u8(clone));
            Assert.Equal((byte)0, rom.u8(SharedTable));

            CoreState.Undo.RunUndo();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(clone, rom.p32(ClassBase + 56));
            Assert.Equal(0m, firstCost.Value);
            CoreState.Undo.RunUndo();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(SharedTable, rom.p32(ClassBase + 56));
            Assert.True(view.FindControl<Button>("IndependenceButton")!.IsVisible);
        }
        finally
        {
            if (view != null) DetachAndClose(view);
            Undo.OnAllFormsInvalidated = previousInvalidation;
            CoreState.Services = previousServices;
            CoreState.AppendBinaryData = previousAppend;
            CoreState.ROM = previousRom;
            CoreState.Undo = previousUndo;
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Undo_RefreshesAttachedEditorTableAndIndependenceAvailability(bool detachBeforeDispatch)
    {
        ROM? previousRom = CoreState.ROM;
        Undo? previousUndo = CoreState.Undo;
        var previousAppend = CoreState.AppendBinaryData;
        var previousInvalidation = Undo.OnAllFormsInvalidated;
        MoveCostEditorView? view = null;
        try
        {
            ROM rom = MakeRom();
            CoreState.ROM = rom;
            CoreState.Undo = new Undo();
            CoreState.AppendBinaryData = null;
            var vm = new MoveCostEditorViewModel();
            vm.BuildCostTypeItems();
            vm.LoadClassList();
            vm.LoadMoveCost(ClassBase, CostType.MoveCostNormal);
            var undoService = new UndoService();
            undoService.Begin("Make Move Cost Independent");
            uint clone = vm.MakeIndependent(undoService.GetActiveUndoData()!);
            Assert.NotEqual(U.NOT_FOUND, clone);
            undoService.Commit();

            view = new MoveCostEditorView();
            view.Show();
            view.NavigateTo(ClassBase);
            var tableLabel = view.FindControl<TextBlock>("MoveCostAddrLabel")!;
            var independence = view.FindControl<Button>("IndependenceButton")!;
            Assert.Equal($"Table: 0x{clone:X08}", tableLabel.Text);
            Assert.False(independence.IsVisible);
            var firstCost = view.GetVisualDescendants().OfType<NumericUpDown>()
                .Single(control => control.Tag is int index && index == 0);
            firstCost.Value = 73;
            Undo.OnAllFormsInvalidated?.Invoke();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(73m, firstCost.Value);

            CoreState.Undo.RunUndo();
            if (detachBeforeDispatch)
                DetachAndClose(view);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(U.toPointer(SharedTable), rom.u32(ClassBase + 56));
            if (detachBeforeDispatch)
            {
                Assert.Equal($"Table: 0x{clone:X08}", tableLabel.Text);
                Assert.Equal(previousInvalidation, Undo.OnAllFormsInvalidated);
                view.Show();
            }
            Assert.Equal($"Table: 0x{SharedTable:X08}", tableLabel.Text);
            Assert.True(independence.IsVisible);
            Assert.Equal(0m, firstCost.Value);
            DetachAndClose(view);
            Assert.Equal(previousInvalidation, Undo.OnAllFormsInvalidated);
            view.Show();
            firstCost.Value = 91;
            Undo.OnAllFormsInvalidated?.Invoke();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(91m, firstCost.Value);
            DetachAndClose(view);
            view = null;
            Assert.Equal(previousInvalidation, Undo.OnAllFormsInvalidated);
        }
        finally
        {
            if (view != null)
                DetachAndClose(view);
            Undo.OnAllFormsInvalidated = previousInvalidation;
            CoreState.AppendBinaryData = previousAppend;
            CoreState.ROM = previousRom;
            CoreState.Undo = previousUndo;
        }
    }
}
