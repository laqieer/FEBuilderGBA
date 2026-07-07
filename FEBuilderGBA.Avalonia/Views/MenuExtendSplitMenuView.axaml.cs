using System;
using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class MenuExtendSplitMenuView : TranslatedUserControl, IEmbeddableEditor, IDataVerifiableView
    {
        readonly MenuExtendSplitMenuViewModel _vm = new();
        readonly UndoService _undoService = new();


        bool _hasLoadedList;
        public string ViewTitle => "Menu Extend Split";
        public new bool IsLoaded => _vm.IsLoaded;

        public EditorDescriptor Descriptor => new("Menu Extend Split", 1257, 604, SizeToContent: true);

        public event EventHandler? CloseRequested;
        public ViewModelBase? DataViewModel => _vm;


        public void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);
        public MenuExtendSplitMenuView()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;        }


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
            try
            {
                var items = _vm.LoadList();
                EntryList.SetItems(items);
            }
            catch (Exception ex)
            {
                Log.Error("MenuExtendSplitMenuView.LoadList failed: " + ex);
            }
        }

        void OnSelected(uint addr)
        {
            try
            {
                _vm.LoadEntry(addr);
                UpdateUI();
            }
            catch (Exception ex)
            {
                Log.Error("MenuExtendSplitMenuView.OnSelected failed: " + ex);
            }
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            CommandPtrLabel.Text = $"0x{_vm.CommandPtr:X08}";
            PosXBox.Value = _vm.PosX;
            PosYBox.Value = _vm.PosY;
            WidthBox.Value = _vm.Width;
            StyleBox.Value = _vm.Style;
            Str0Box.Value = _vm.String0;
            Str1Box.Value = _vm.String1;
            Str2Box.Value = _vm.String2;
            Str3Box.Value = _vm.String3;
            Str4Box.Value = _vm.String4;
            Str5Box.Value = _vm.String5;
            Str6Box.Value = _vm.String6;
            Str7Box.Value = _vm.String7;

            // Commands 5..7 only exist on an 8-command menu.
            bool showExtra = _vm.StringCount >= 8;
            Str5Label.IsVisible = Str5Box.IsVisible = showExtra;
            Str6Label.IsVisible = Str6Box.IsVisible = showExtra;
            Str7Label.IsVisible = Str7Box.IsVisible = showExtra;
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.IsLoaded) return;

            _vm.PosX = (uint)(PosXBox.Value ?? 0);
            _vm.PosY = (uint)(PosYBox.Value ?? 0);
            _vm.Width = (uint)(WidthBox.Value ?? 0);
            _vm.Style = (uint)(StyleBox.Value ?? 0);
            _vm.String0 = (uint)(Str0Box.Value ?? 0);
            _vm.String1 = (uint)(Str1Box.Value ?? 0);
            _vm.String2 = (uint)(Str2Box.Value ?? 0);
            _vm.String3 = (uint)(Str3Box.Value ?? 0);
            _vm.String4 = (uint)(Str4Box.Value ?? 0);
            _vm.String5 = (uint)(Str5Box.Value ?? 0);
            _vm.String6 = (uint)(Str6Box.Value ?? 0);
            _vm.String7 = (uint)(Str7Box.Value ?? 0);
            _undoService.Begin("Edit Split Menu");
            try
            {
                if (_vm.Write())
                {
                    _undoService.Commit();
                    _vm.MarkClean();
                    CoreState.Services?.ShowInfo("Split menu data written.");
                }
                else
                {
                    _undoService.Rollback();
                    CoreState.Services?.ShowError(
                        "No data was written: the header or command-array pointer is unsafe, the command array does not fit in ROM, or the EventMenuCommand patch is not installed (FE8 only).");
                }
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.Error("MenuExtendSplitMenuView.Write failed: " + ex);
            }
        }

        void NewAlloc_Click(object? sender, RoutedEventArgs e)
        {
            _undoService.Begin("New Split Menu");
            try
            {
                uint addr = _vm.NewAlloc();
                if (addr == U.NOT_FOUND)
                {
                    _undoService.Rollback();
                    CoreState.Services?.ShowError(
                        "Could not create a new split menu: no free space, or the EventMenuCommand patch is not installed (FE8 only).");
                    return;
                }
                _undoService.Commit();

                // The new header is a STANDALONE free-space allocation — it is
                // NOT part of the contiguous menu_definiton_split_pointer run,
                // so it won't appear in the master list (SelectAddress would
                // no-op). Load it directly into the editor instead.
                _vm.LoadEntry(addr);
                UpdateUI();
                CoreState.Services?.ShowInfo($"New split menu allocated at 0x{addr:X08}.");
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.Error("MenuExtendSplitMenuView.NewAlloc failed: " + ex);
            }
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
    }
}
