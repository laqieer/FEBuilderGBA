using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class AIASMCALLTALKView : TranslatedWindow, IEditorView, IDataVerifiableView
    {
        readonly AIASMCALLTALKViewModel _vm = new();
        readonly UndoService _undoService = new();

        public string ViewTitle => "AI ASM Call Talk";
        public bool IsLoaded => _vm.IsLoaded;

        public AIASMCALLTALKView()
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
                Log.Error("AIASMCALLTALKView.LoadList failed: {0}", ex.Message);
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
                Log.Error("AIASMCALLTALKView.OnSelected failed: {0}", ex.Message);
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
            FromUnitBox.Value = _vm.FromUnit;
            ToUnitBox.Value = _vm.ToUnit;
            Unused2Box.Value = _vm.Unused2;
            Unused3Box.Value = _vm.Unused3;
        }

        void OnWrite(object? sender, RoutedEventArgs e)
        {
            _undoService.Begin("Edit AI ASM Call Talk");
            try
            {
                _vm.FromUnit = (uint)(FromUnitBox.Value ?? 0);
                _vm.ToUnit = (uint)(ToUnitBox.Value ?? 0);
                _vm.Unused2 = (uint)(Unused2Box.Value ?? 0);
                _vm.Unused3 = (uint)(Unused3Box.Value ?? 0);
                _vm.Write();
                _undoService.Commit();
                _vm.MarkClean();
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.Error("AIASMCALLTALKView.Write failed: {0}", ex.Message);
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
            // Mirror WinForms isSafetyOffset gating: never load/write a header/unsafe
            // offset (< 0x200 or past EOF). The VM's LoadEntry/Write only bounds-check
            // Data.Length, so guard here before the direct load (#1414 review).
            if (CoreState.ROM == null || !U.isSafetyOffset(address)) return;
            OnSelected(address);
        }
        public void SelectFirstItem() => EntryList.SelectFirst();
        public ViewModelBase? DataViewModel => _vm;
    }
}
