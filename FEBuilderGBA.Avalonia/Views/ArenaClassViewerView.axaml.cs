using System;
using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Input;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Controls;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;
using FEBuilderGBA.Core;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class ArenaClassViewerView : TranslatedUserControl, IEmbeddableEditor, IDataVerifiableView
    {
        readonly ArenaClassViewerViewModel _vm = new();
        readonly UndoService _undoService = new();
        bool _hasLoadedList;

        public string ViewTitle => "Arena Class";
        public new bool IsLoaded => _vm.CanWrite;
        public EditorDescriptor Descriptor => new("Arena Class Editor", 1201, 738, SizeToContent: global::Avalonia.Controls.SizeToContent.WidthAndHeight);
        public event EventHandler? CloseRequested;

        public ArenaClassViewerView()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            WeaponTypeCombo.SelectionChanged += WeaponType_Changed;
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            if (!_hasLoadedList)
            {
                _hasLoadedList = true;
                InitFilter();
            }
        }

        void InitFilter()
        {
            var names = _vm.GetWeaponTypeNames();
            WeaponTypeCombo.ItemsSource = names;
            if (names.Count > 0)
                WeaponTypeCombo.SelectedIndex = 0;
            // Always load — SelectionChanged may not fire if Avalonia auto-selects index 0
            LoadList(Math.Max(0, WeaponTypeCombo.SelectedIndex));
        }

        void WeaponType_Changed(object? sender, SelectionChangedEventArgs e)
        {
            LoadList(WeaponTypeCombo.SelectedIndex);
        }

        void LoadList(int typeIndex = 0)
        {
            _vm.IsLoading = true;
            try
            {
                var items = _vm.LoadArenaClassList(typeIndex);
                // #939: the row prefix is the row INDEX, not the class id. Key
                // the icon off the real class id (u8 at entry+0 — the value
                // already shown in the row's "(0x..)" suffix).
                EntryList.SetItemsWithIcons(items, i => ListIconLoaders.ClassIconLoader(items, i, ClassIdOf));
            }
            catch (Exception ex)
            {
                Log.ErrorF("ArenaClassViewerView.LoadList failed: {0}", ex.Message);
            }
            finally { _vm.IsLoading = false; _vm.MarkClean(); }
        }

        void OnSelected(uint addr)
        {
            _vm.IsLoading = true;
            try
            {
                _vm.LoadArenaClass(addr);
                UpdateUI();
            }
            catch (Exception ex)
            {
                Log.ErrorF("ArenaClassViewerView.OnSelected failed: {0}", ex.Message);
            }
            finally { _vm.IsLoading = false; _vm.MarkClean(); }
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            ClassIdBox.Value = _vm.ClassId;
            try { ClassIdBox.NameText = NameResolver.GetClassName(_vm.ClassId); }
            catch { /* NameResolver may fail without ROM — leave prior text */ }
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.CanWrite) return;
            _undoService.Begin("Edit Arena Class");
            try
            {
                _vm.ClassId = ClassIdBox.Value;
                _vm.WriteArenaClass();
                _undoService.Commit();
                _vm.MarkClean();
                CoreState.Services?.ShowInfo("Arena Class data written.");
            }
            catch (Exception ex) { _undoService.Rollback(); Log.ErrorF("ArenaClassViewerView.Write: {0}", ex.Message); }
        }

        // #939: resolve the real class id (u8 at entry+0) for the list icon.
        // Guards a null ROM by returning 0 → the loader returns null (no icon),
        // never throws.
        static uint ClassIdOf(AddrResult r)
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return 0;
            if (!U.isSafetyOffset(r.addr, rom)) return 0;
            return rom.u8(r.addr);
        }

        // -- IdFieldControl handlers (#360) ----------------------------------

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
            catch (Exception ex) { Log.ErrorF("ArenaClassViewerView.ClassId_Jump failed: {0}", ex.Message); }
        }

        async void ClassId_Pick(object? sender, RoutedEventArgs e)
        {
            try
            {
                uint addr = ClassAddrFor(ClassIdBox.Value);
                PickResult? result;
                if (CoreState.ROM?.RomInfo?.version == 6)
                    result = await WindowManager.Instance.PickFromEditor<ClassFE6View>(addr, TopLevel.GetTopLevel(this) as Window);
                else
                    result = await WindowManager.Instance.PickFromEditor<ClassEditorView>(addr, TopLevel.GetTopLevel(this) as Window);
                if (result != null) ClassIdBox.Value = (uint)result.Index;
            }
            catch (Exception ex) { Log.ErrorF("ArenaClassViewerView.ClassId_Pick failed: {0}", ex.Message); }
        }

        void ClassId_ValueChanged(object? sender, IdFieldValueChangedEventArgs e)
        {
            try { ClassIdBox.NameText = NameResolver.GetClassName(e.NewValue); }
            catch { /* NameResolver may fail without ROM — leave prior text */ }
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
        public ViewModelBase? DataViewModel => _vm;
        public void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);
    }
}
