using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class AIUnitsView : TranslatedWindow, IEditorView, IDataVerifiableView
    {
        readonly AIUnitsViewModel _vm = new();
        readonly UndoService _undoService = new();

        public string ViewTitle => "AI Units Evaluation";
        public bool IsLoaded => _vm.IsLoaded;

        public AIUnitsView()
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
                EntryList.SetItemsWithIcons(items, i => ListIconLoaders.UnitPortraitFromAddrU8Loader(items, i));
            }
            catch (Exception ex)
            {
                Log.Error("AIUnitsView.LoadList failed: {0}", ex.Message);
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
                Log.Error("AIUnitsView.OnSelected failed: {0}", ex.Message);
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
            UnitBox.Value = _vm.Unit;
            Unknown1Box.Value = _vm.Unknown1;
        }

        void OnWrite(object? sender, RoutedEventArgs e)
        {
            _undoService.Begin("Edit AI Units");
            try
            {
                _vm.Unit = (uint)(UnitBox.Value ?? 0);
                _vm.Unknown1 = (uint)(Unknown1Box.Value ?? 0);
                _vm.Write();
                _undoService.Commit();
                _vm.MarkClean();
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.Error("AIUnitsView.Write failed: {0}", ex.Message);
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
