using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class AIASMRangeView : TranslatedWindow, IEditorView, IDataVerifiableView
    {
        readonly AIASMRangeViewModel _vm = new();
        readonly UndoService _undoService = new();

        public string ViewTitle => "AI Range Editor";
        public bool IsLoaded => _vm.IsLoaded;

        public AIASMRangeView()
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
                Log.Error("AIASMRangeView.LoadList failed: {0}", ex.Message);
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
                Log.Error("AIASMRangeView.OnSelected failed: {0}", ex.Message);
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
            X1Box.Value = _vm.X1;
            Y1Box.Value = _vm.Y1;
            X2Box.Value = _vm.X2;
            Y2Box.Value = _vm.Y2;
        }

        void OnWrite(object? sender, RoutedEventArgs e)
        {
            _undoService.Begin("Edit AI Range");
            try
            {
                _vm.X1 = (uint)(X1Box.Value ?? 0);
                _vm.Y1 = (uint)(Y1Box.Value ?? 0);
                _vm.X2 = (uint)(X2Box.Value ?? 0);
                _vm.Y2 = (uint)(Y2Box.Value ?? 0);
                _vm.Write();
                _undoService.Commit();
                _vm.MarkClean();
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.Error("AIASMRangeView.Write failed: {0}", ex.Message);
            }
        }

        public void NavigateTo(uint address)
        {
            // #1414: standalone LoadList() now holds only the addr-0 placeholder, so
            // SelectAddress(realAddr) can't find the row a parent (the AIScript
            // per-param dispatch) supplies. When the list can't select a non-zero
            // parent pointer, load it directly so Write() targets the SUPPLIED
            // address instead of being a no-op. addr 0 keeps the placeholder/no-op.
            if (EntryList.SelectAddress(address) || address == 0) return;
            OnSelected(address);
        }
        public void SelectFirstItem() => EntryList.SelectFirst();
        public ViewModelBase? DataViewModel => _vm;
    }
}
