using System;
using global::Avalonia.Controls;
using global::Avalonia.Input;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Controls;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;
using FEBuilderGBA.Core;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class SummonsDemonKingViewerView : TranslatedWindow, IEditorView, IDataVerifiableView
    {
        readonly SummonsDemonKingViewerViewModel _vm = new();
        readonly UndoService _undoService = new();

        public string ViewTitle => "Demon King Summon";
        public bool IsLoaded => _vm.CanWrite;

        public SummonsDemonKingViewerView()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            Opened += (_, _) => LoadList();
        }

        void LoadList()
        {
            _vm.IsLoading = true;
            try
            {
                var items = _vm.LoadSummonsDemonKingList();
                EntryList.SetItems(items);
            }
            catch (Exception ex)
            {
                Log.Error("SummonsDemonKingViewerView.LoadList failed: {0}", ex.Message);
            }
            finally { _vm.IsLoading = false; _vm.MarkClean(); }
        }

        void OnSelected(uint addr)
        {
            _vm.IsLoading = true;
            try
            {
                _vm.LoadSummonsDemonKing(addr);
                UpdateUI();
            }
            catch (Exception ex)
            {
                Log.Error("SummonsDemonKingViewerView.OnSelected failed: {0}", ex.Message);
            }
            finally { _vm.IsLoading = false; _vm.MarkClean(); }
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            UnitIdBox.Value = _vm.UnitId;
            try { UnitIdBox.NameText = SupportUnitNavigation.ResolveUnitTableName(CoreState.ROM, _vm.UnitId); }
            catch { /* SupportUnitNavigation may fail without ROM — leave prior text */ }
            ClassIdBox.Value = _vm.ClassId;
            try { ClassIdBox.NameText = NameResolver.GetClassName(_vm.ClassId); }
            catch { /* NameResolver may fail without ROM — leave prior text */ }
            Unknown1Box.Value = _vm.Commander;
            B3Box.Value = _vm.LevelGrowth;
            W4Box.Value = _vm.Coordinates;
            B6Box.Value = _vm.Special;
            B7Box.Value = _vm.Padding7;
            P8Box.Text = $"0x{_vm.AIPointer:X08}";
            B12Box.Value = _vm.Item1;
            B13Box.Value = _vm.Item2;
            B14Box.Value = _vm.Item3;
            B15Box.Value = _vm.Item4;
            B16Box.Value = _vm.PrimaryAI;
            B17Box.Value = _vm.SecondaryAI;
            B18Box.Value = _vm.TargetRecoveryAI;
            B19Box.Value = _vm.RetreatAI;
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.CanWrite) return;
            _undoService.Begin("Edit Demon King Summon");
            try
            {
                _vm.UnitId = UnitIdBox.Value;
                _vm.ClassId = ClassIdBox.Value;
                _vm.Commander = (uint)(Unknown1Box.Value ?? 0);
                _vm.LevelGrowth = (uint)(B3Box.Value ?? 0);
                _vm.Coordinates = (uint)(W4Box.Value ?? 0);
                _vm.Special = (uint)(B6Box.Value ?? 0);
                _vm.Padding7 = (uint)(B7Box.Value ?? 0);
                _vm.AIPointer = ParseHexText(P8Box.Text);
                _vm.Item1 = (uint)(B12Box.Value ?? 0);
                _vm.Item2 = (uint)(B13Box.Value ?? 0);
                _vm.Item3 = (uint)(B14Box.Value ?? 0);
                _vm.Item4 = (uint)(B15Box.Value ?? 0);
                _vm.PrimaryAI = (uint)(B16Box.Value ?? 0);
                _vm.SecondaryAI = (uint)(B17Box.Value ?? 0);
                _vm.TargetRecoveryAI = (uint)(B18Box.Value ?? 0);
                _vm.RetreatAI = (uint)(B19Box.Value ?? 0);
                _vm.WriteSummonsDemonKing();
                _undoService.Commit();
                _vm.MarkClean();
                CoreState.Services?.ShowInfo("Demon king summon data written.");
            }
            catch (Exception ex) { _undoService.Rollback(); Log.Error("SummonsDemonKingViewerView.Write: {0}", ex.Message); }
        }

        // -- IdFieldControl handlers (#360) ----------------------------------

        static uint UnitAddrFor(uint unitId)
        {
            var rom = CoreState.ROM;
            if (rom?.RomInfo == null) return 0;
            uint unitPtr = rom.RomInfo.unit_pointer;
            if (unitPtr == 0) return 0;
            uint baseAddr = rom.p32(unitPtr);
            if (!U.isSafetyOffset(baseAddr, rom)) return 0;
            uint dataSize = rom.RomInfo.unit_datasize;
            if (dataSize == 0) return 0;
            if (rom.RomInfo.version == 6) baseAddr += dataSize;
            uint entryAddr = baseAddr + unitId * dataSize;
            if (!U.isSafetyOffset(entryAddr, rom)) return 0;
            if (!U.isSafetyOffset(entryAddr + dataSize - 1, rom)) return 0;
            return entryAddr;
        }

        static uint ClassAddrFor(uint classId)
        {
            var rom = CoreState.ROM;
            if (rom?.RomInfo == null) return 0;
            uint classPtr = rom.RomInfo.class_pointer;
            if (classPtr == 0) return 0;
            uint baseAddr = rom.p32(classPtr);
            if (!U.isSafetyOffset(baseAddr, rom)) return 0;
            uint dataSize = rom.RomInfo.class_datasize;
            if (dataSize == 0) return 0;
            uint entryAddr = baseAddr + classId * dataSize;
            if (!U.isSafetyOffset(entryAddr, rom)) return 0;
            if (!U.isSafetyOffset(entryAddr + dataSize - 1, rom)) return 0;
            return entryAddr;
        }

        void UnitId_Jump(object? sender, RoutedEventArgs e)
        {
            try
            {
                uint addr = UnitAddrFor(UnitIdBox.Value);
                if (addr == 0) return;
                WindowManager.Instance.Navigate<UnitEditorView>(addr);
            }
            catch (Exception ex) { Log.Error("SummonsDemonKingViewerView.UnitId_Jump failed: {0}", ex.Message); }
        }

        async void UnitId_Pick(object? sender, RoutedEventArgs e)
        {
            try
            {
                uint addr = UnitAddrFor(UnitIdBox.Value);
                var result = await WindowManager.Instance.PickFromEditor<UnitEditorView>(addr, this);
                if (result != null) UnitIdBox.Value = (uint)result.Index;
            }
            catch (Exception ex) { Log.Error("SummonsDemonKingViewerView.UnitId_Pick failed: {0}", ex.Message); }
        }

        void UnitId_ValueChanged(object? sender, IdFieldValueChangedEventArgs e)
        {
            try { UnitIdBox.NameText = SupportUnitNavigation.ResolveUnitTableName(CoreState.ROM, e.NewValue); }
            catch { /* SupportUnitNavigation may fail without ROM — leave prior text */ }
        }

        void ClassId_Jump(object? sender, RoutedEventArgs e)
        {
            try
            {
                uint addr = ClassAddrFor(ClassIdBox.Value);
                if (addr == 0) return;
                if (CoreState.ROM?.RomInfo?.version == 6)
                    WindowManager.Instance.Navigate<ClassFE6View>(addr);
                else
                    WindowManager.Instance.Navigate<ClassEditorView>(addr);
            }
            catch (Exception ex) { Log.Error("SummonsDemonKingViewerView.ClassId_Jump failed: {0}", ex.Message); }
        }

        async void ClassId_Pick(object? sender, RoutedEventArgs e)
        {
            try
            {
                uint addr = ClassAddrFor(ClassIdBox.Value);
                PickResult? result;
                if (CoreState.ROM?.RomInfo?.version == 6)
                    result = await WindowManager.Instance.PickFromEditor<ClassFE6View>(addr, this);
                else
                    result = await WindowManager.Instance.PickFromEditor<ClassEditorView>(addr, this);
                if (result != null) ClassIdBox.Value = (uint)result.Index;
            }
            catch (Exception ex) { Log.Error("SummonsDemonKingViewerView.ClassId_Pick failed: {0}", ex.Message); }
        }

        void ClassId_ValueChanged(object? sender, IdFieldValueChangedEventArgs e)
        {
            try { ClassIdBox.NameText = NameResolver.GetClassName(e.NewValue); }
            catch { /* NameResolver may fail without ROM — leave prior text */ }
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
        public ViewModelBase? DataViewModel => _vm;

        static uint ParseHexText(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return 0;
            text = text.Trim();
            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) text = text[2..];
            return uint.TryParse(text, System.Globalization.NumberStyles.HexNumber, null, out uint v) ? v : 0;
        }
    }
}
