using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class AIMapSettingView : Window, IEditorView
    {
        readonly AIMapSettingViewModel _vm = new();
        readonly UndoService _undoService = new();

        public string ViewTitle => "AI Map Settings";
        public bool IsLoaded => _vm.IsLoaded;

        public AIMapSettingView()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            WriteButton.Click += OnWrite;
            Opened += (_, _) => LoadList();
        }

        void LoadList()
        {
            try
            {
                _vm.IsLoading = true;
                var items = _vm.LoadList();
                EntryList.SetItems(items);
            }
            catch (Exception ex)
            {
                Log.Error("AIMapSettingView.LoadList failed: {0}", ex.Message);
            }
            finally
            {
                _vm.IsLoading = false;
                _vm.MarkClean();
            }
        }

        void OnSelected(uint addr)
        {
            try
            {
                _vm.IsLoading = true;
                _vm.LoadEntry(addr);
                UpdateUI();
            }
            catch (Exception ex)
            {
                Log.Error("AIMapSettingView.OnSelected failed: {0}", ex.Message);
            }
            finally
            {
                _vm.IsLoading = false;
                _vm.MarkClean();
            }
        }

        void UpdateUI()
        {
            AddrLabel.Text = string.Format("0x{0:X08}", _vm.CurrentAddr);
            Trait1Box.Value = _vm.Trait1;
            Trait2Box.Value = _vm.Trait2;
            Trait3Box.Value = _vm.Trait3;
            Trait4Box.Value = _vm.Trait4;
        }

        void OnWrite(object? sender, RoutedEventArgs e)
        {
            _undoService.Begin("Edit AI Map Setting");
            try
            {
                _vm.Trait1 = (uint)(Trait1Box.Value ?? 0);
                _vm.Trait2 = (uint)(Trait2Box.Value ?? 0);
                _vm.Trait3 = (uint)(Trait3Box.Value ?? 0);
                _vm.Trait4 = (uint)(Trait4Box.Value ?? 0);
                _vm.Write();
                _undoService.Commit();
                _vm.MarkClean();
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.Error("AIMapSettingView.Write failed: {0}", ex.Message);
            }
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
    }
}
