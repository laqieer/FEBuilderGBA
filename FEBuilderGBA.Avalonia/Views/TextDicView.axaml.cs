using System;
using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Input;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;
using FEBuilderGBA.Core;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class TextDicView : TranslatedUserControl, IEmbeddableEditor, IDataVerifiableView
    {
        readonly TextDicViewModel _vm = new();
        readonly UndoService _undoService = new();
        bool _hasLoadedList;

        public string ViewTitle => "Text Dictionary";
        public new bool IsLoaded => _vm.IsLoaded;
        public EditorDescriptor Descriptor => new("Text Dictionary", 1155, 661, SizeToContent: global::Avalonia.Controls.SizeToContent.WidthAndHeight);
        public event EventHandler? CloseRequested;
        public ViewModelBase? DataViewModel => _vm;
        public void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);

        public TextDicView()
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
            var items = _vm.BuildList();
            EntryList.SetItems(items);
            _vm.Initialize();
        }

        void OnSelected(uint address)
        {
            _vm.LoadEntry(address);
            UpdateUI();
        }

        void UpdateUI()
        {
            TitleIndexBox.Value = (decimal)_vm.TitleIndex;
            ChapterIndexBox.Value = (decimal)_vm.ChapterIndex;
            TextId1Box.Value = (decimal)_vm.TextId1;
            TextId2Box.Value = (decimal)_vm.TextId2;
            Flag1Box.Value = (decimal)_vm.Flag1;
            Flag2Box.Value = (decimal)_vm.Flag2;
            UnitIdBox.Value = (decimal)_vm.UnitId;
            ClassIdBox.Value = (decimal)_vm.ClassId;
            Text1PreviewLabel.Text = _vm.Text1Preview;
            Text2PreviewLabel.Text = _vm.Text2Preview;
            UnitNameLabel.Text = _vm.UnitName;
            ClassNameLabel.Text = _vm.ClassNameDisplay;
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.IsLoaded || _vm.CurrentAddr == 0) return;

            _vm.TitleIndex = (uint)(TitleIndexBox.Value ?? 0);
            _vm.ChapterIndex = (uint)(ChapterIndexBox.Value ?? 0);
            _vm.TextId1 = (uint)(TextId1Box.Value ?? 0);
            _vm.TextId2 = (uint)(TextId2Box.Value ?? 0);
            _vm.Flag1 = (uint)(Flag1Box.Value ?? 0);
            _vm.Flag2 = (uint)(Flag2Box.Value ?? 0);
            _vm.UnitId = (uint)(UnitIdBox.Value ?? 0);
            _vm.ClassId = (uint)(ClassIdBox.Value ?? 0);

            _undoService.Begin("Edit Text Dictionary");
            try
            {
                _vm.Write();
                _undoService.Commit();
                _vm.MarkClean();
                LoadList();
                CoreState.Services?.ShowInfo("Text dictionary data written.");
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.ErrorF("TextDicView.Write_Click failed: {0}", ex.Message);
            }
        }

        void OnUnitIdLinkClick(object? sender, PointerPressedEventArgs e)
        {
            try
            {
                // UnitId is 1-based; UnitAddrForOneBased applies the (id-1)
                // index + FE6 dummy-entry skip so Jump lands on the right unit
                // (matches the name preview, which already uses
                // GetUnitNameByOneBasedId) (#937).
                uint addr = SupportUnitNavigation.UnitAddrForOneBased(CoreState.ROM, (uint)(UnitIdBox.Value ?? 0));
                if (addr == 0) return;
                WindowManager.Instance.Navigate<UnitEditorView>(addr);
            }
            catch (Exception ex) { Log.ErrorF("OnUnitIdLinkClick failed: {0}", ex.Message); }
        }

        void OnClassIdLinkClick(object? sender, PointerPressedEventArgs e)
        {
            try
            {
                var rom = CoreState.ROM;
                if (rom?.RomInfo == null) return;
                uint classId = (uint)(ClassIdBox.Value ?? 0);
                uint baseAddr = rom.p32(rom.RomInfo.class_pointer);
                if (!U.isSafetyOffset(baseAddr)) return;
                uint addr = baseAddr + classId * rom.RomInfo.class_datasize;
                if (rom.RomInfo.version == 6)
                    WindowManager.Instance.Navigate<ClassFE6View>(addr);
                else
                    WindowManager.Instance.Navigate<ClassEditorView>(addr);
            }
            catch (Exception ex) { Log.ErrorF("OnClassIdLinkClick failed: {0}", ex.Message); }
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
    }
}
