using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class ArenaClassViewerView : Window, IEditorView, IDataVerifiableView
    {
        readonly ArenaClassViewerViewModel _vm = new();
        readonly UndoService _undoService = new();

        public string ViewTitle => "Arena Class";
        public bool IsLoaded => _vm.CanWrite;

        public ArenaClassViewerView()
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
                var items = _vm.LoadArenaClassList();
                EntryList.SetItems(items);
            }
            catch (Exception ex)
            {
                Log.Error("ArenaClassViewerView.LoadList failed: {0}", ex.Message);
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
                Log.Error("ArenaClassViewerView.OnSelected failed: {0}", ex.Message);
            }
            finally { _vm.IsLoading = false; _vm.MarkClean(); }
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            ClassIdBox.Value = _vm.ClassId;
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.CanWrite) return;
            _undoService.Begin("Edit Arena Class");
            try
            {
                _vm.ClassId = (uint)(ClassIdBox.Value ?? 0);
                _vm.WriteArenaClass();
                _undoService.Commit();
                _vm.MarkClean();
                CoreState.Services?.ShowInfo("Arena Class data written.");
            }
            catch (Exception ex) { _undoService.Rollback(); Log.Error("ArenaClassViewerView.Write: {0}", ex.Message); }
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
        public ViewModelBase? DataViewModel => _vm;
    }
}
