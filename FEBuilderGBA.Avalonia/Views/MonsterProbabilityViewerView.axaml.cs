using System;
using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Controls;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;
using FEBuilderGBA.Core;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class MonsterProbabilityViewerView : TranslatedUserControl, IEmbeddableEditor, IDataVerifiableView
    {
        readonly MonsterProbabilityViewerViewModel _vm = new();
        readonly UndoService _undoService = new();
        bool _hasLoadedList;

        // True once Opened -> LoadList() has populated EntryList. A NavigateTo
        // request that arrives BEFORE the list loads (the WindowManager.Navigate
        // flow opens the window then immediately calls NavigateTo, but Avalonia
        // raises Opened asynchronously after layout) is stashed in
        // _pendingNavigateAddr and replayed once LoadList completes — otherwise
        // EntryList.SelectAddress no-ops against the still-empty list (#1018).
        bool _listLoaded;
        uint? _pendingNavigateAddr;

        public string ViewTitle => "Monster Probability";
        public new bool IsLoaded => _vm.CanWrite;
        public EditorDescriptor Descriptor => new("Monster Probability Editor", 1203, 552, SizeToContent: true);
        public event EventHandler? CloseRequested;

        public MonsterProbabilityViewerView()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            if (!_hasLoadedList)
            {
                _hasLoadedList = true;
                LoadList();
            }
        }

        void LoadList()
        {
            _vm.IsLoading = true;
            try
            {
                var items = _vm.LoadMonsterProbabilityList();
                EntryList.SetItems(items);
                _listLoaded = true;
            }
            catch (Exception ex)
            {
                Log.ErrorF("MonsterProbabilityViewerView.LoadList failed: {0}", ex.Message);
            }
            finally { _vm.IsLoading = false; _vm.MarkClean(); }

            // Replay a navigation requested before the list was ready.
            if (_pendingNavigateAddr is uint pending)
            {
                _pendingNavigateAddr = null;
                EntryList.SelectAddress(pending);
            }
        }

        void OnSelected(uint addr)
        {
            _vm.IsLoading = true;
            try
            {
                _vm.LoadMonsterProbability(addr);
                UpdateUI();
            }
            catch (Exception ex)
            {
                Log.ErrorF("MonsterProbabilityViewerView.OnSelected failed: {0}", ex.Message);
            }
            finally { _vm.IsLoading = false; _vm.MarkClean(); }
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            ClassId1Box.Value = _vm.ClassId1;
            ClassId2Box.Value = _vm.ClassId2;
            ClassId3Box.Value = _vm.ClassId3;
            ClassId4Box.Value = _vm.ClassId4;
            ClassId5Box.Value = _vm.ClassId5;
            // #950 T4: inline class-name preview for each migrated Class ID field.
            try
            {
                ClassId1Box.NameText = NameResolver.GetClassName(_vm.ClassId1);
                ClassId2Box.NameText = NameResolver.GetClassName(_vm.ClassId2);
                ClassId3Box.NameText = NameResolver.GetClassName(_vm.ClassId3);
                ClassId4Box.NameText = NameResolver.GetClassName(_vm.ClassId4);
                ClassId5Box.NameText = NameResolver.GetClassName(_vm.ClassId5);
            }
            catch (Exception ex) { Log.ErrorF("MonsterProbabilityViewerView.UpdateUI ClassName: {0}", ex.Message); }
            Prob1Box.Value = _vm.Prob1;
            Prob2Box.Value = _vm.Prob2;
            Prob3Box.Value = _vm.Prob3;
            Prob4Box.Value = _vm.Prob4;
            Prob5Box.Value = _vm.Prob5;
            Unknown1Box.Value = _vm.Unknown1;
            Unknown2Box.Value = _vm.Unknown2;
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.CanWrite) return;
            _undoService.Begin("Edit Monster Probability");
            try
            {
                // #950 T4: IdFieldControl.Value is a non-nullable uint.
                _vm.ClassId1 = ClassId1Box.Value;
                _vm.ClassId2 = ClassId2Box.Value;
                _vm.ClassId3 = ClassId3Box.Value;
                _vm.ClassId4 = ClassId4Box.Value;
                _vm.ClassId5 = ClassId5Box.Value;
                _vm.Prob1 = (uint)(Prob1Box.Value ?? 0);
                _vm.Prob2 = (uint)(Prob2Box.Value ?? 0);
                _vm.Prob3 = (uint)(Prob3Box.Value ?? 0);
                _vm.Prob4 = (uint)(Prob4Box.Value ?? 0);
                _vm.Prob5 = (uint)(Prob5Box.Value ?? 0);
                _vm.Unknown1 = (uint)(Unknown1Box.Value ?? 0);
                _vm.Unknown2 = (uint)(Unknown2Box.Value ?? 0);
                _vm.WriteMonsterProbability();
                _undoService.Commit();
                _vm.MarkClean();
                CoreState.Services?.ShowInfo("Monster probability data written.");
            }
            catch (Exception ex) { _undoService.Rollback(); Log.ErrorF("MonsterProbabilityViewerView.Write: {0}", ex.Message); }
        }

        // ============================================================
        // Class ID picker helpers (#950 T4). The 5 monster-class slots
        // (B0..B4) each Jump to / Pick from the Class editor
        // (ClassFE6View for FE6, ClassEditorView otherwise), mirroring the
        // ItemPromotion/ArenaClass class IdFieldControl wiring.
        // ============================================================

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

        void JumpToClassEditor(IdFieldControl box)
        {
            try
            {
                uint addr = ClassAddrFor(box.Value);
                if (addr == 0) return;
                if (CoreState.ROM?.RomInfo?.version == 6)
                    WindowManager.Instance.Navigate<ClassFE6View>(addr);
                else
                    WindowManager.Instance.Navigate<ClassEditorView>(addr);
            }
            catch (Exception ex) { Log.ErrorF("MonsterProbabilityViewerView.JumpToClassEditor: {0}", ex.Message); }
        }

        async System.Threading.Tasks.Task PickClassIdInto(IdFieldControl box)
        {
            try
            {
                uint addr = ClassAddrFor(box.Value);
                PickResult? result;
                if (CoreState.ROM?.RomInfo?.version == 6)
                    result = await WindowManager.Instance.PickFromEditor<ClassFE6View>(addr, TopLevel.GetTopLevel(this) as Window);
                else
                    result = await WindowManager.Instance.PickFromEditor<ClassEditorView>(addr, TopLevel.GetTopLevel(this) as Window);
                if (result != null) box.Value = (uint)result.Index;
            }
            catch (Exception ex) { Log.ErrorF("MonsterProbabilityViewerView.PickClassIdInto: {0}", ex.Message); }
        }

        static void RefreshClassName(IdFieldControl box, uint value)
        {
            try { box.NameText = NameResolver.GetClassName(value); }
            catch { /* NameResolver may fail without ROM */ }
        }

        void ClassId1_Jump(object? sender, RoutedEventArgs e) => JumpToClassEditor(ClassId1Box);
        async void ClassId1_Pick(object? sender, RoutedEventArgs e) => await PickClassIdInto(ClassId1Box);
        void ClassId1_ValueChanged(object? sender, IdFieldValueChangedEventArgs e) => RefreshClassName(ClassId1Box, e.NewValue);

        void ClassId2_Jump(object? sender, RoutedEventArgs e) => JumpToClassEditor(ClassId2Box);
        async void ClassId2_Pick(object? sender, RoutedEventArgs e) => await PickClassIdInto(ClassId2Box);
        void ClassId2_ValueChanged(object? sender, IdFieldValueChangedEventArgs e) => RefreshClassName(ClassId2Box, e.NewValue);

        void ClassId3_Jump(object? sender, RoutedEventArgs e) => JumpToClassEditor(ClassId3Box);
        async void ClassId3_Pick(object? sender, RoutedEventArgs e) => await PickClassIdInto(ClassId3Box);
        void ClassId3_ValueChanged(object? sender, IdFieldValueChangedEventArgs e) => RefreshClassName(ClassId3Box, e.NewValue);

        void ClassId4_Jump(object? sender, RoutedEventArgs e) => JumpToClassEditor(ClassId4Box);
        async void ClassId4_Pick(object? sender, RoutedEventArgs e) => await PickClassIdInto(ClassId4Box);
        void ClassId4_ValueChanged(object? sender, IdFieldValueChangedEventArgs e) => RefreshClassName(ClassId4Box, e.NewValue);

        void ClassId5_Jump(object? sender, RoutedEventArgs e) => JumpToClassEditor(ClassId5Box);
        async void ClassId5_Pick(object? sender, RoutedEventArgs e) => await PickClassIdInto(ClassId5Box);
        void ClassId5_ValueChanged(object? sender, IdFieldValueChangedEventArgs e) => RefreshClassName(ClassId5Box, e.NewValue);

        public void NavigateTo(uint address)
        {
            // When the list has not yet loaded (WindowManager.Navigate calls
            // NavigateTo synchronously right after Show(), before Avalonia
            // raises Opened), stash the address so LoadList() can replay the
            // selection — a direct SelectAddress would no-op (#1018).
            if (!_listLoaded)
            {
                _pendingNavigateAddr = address;
                return;
            }
            EntryList.SelectAddress(address);
        }
        public void SelectFirstItem() => EntryList.SelectFirst();
        public ViewModelBase? DataViewModel => _vm;
        public void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);
    }
}
