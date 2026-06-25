using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class MapPointerNewPLISTPopupView : TranslatedWindow, IEditorView, IDataVerifiableView
    {
        readonly MapPointerNewPLISTPopupViewModel _vm = new();

        public string ViewTitle => "Map Pointer - New PLIST";
        public bool IsLoaded => _vm.IsLoaded;
        public ViewModelBase? DataViewModel => _vm;

        public MapPointerNewPLISTPopupView()
        {
            InitializeComponent();
            DataContext = _vm;
            _vm.Initialize();
            // Populate the Extend-state note, PLIST maximum and the initial
            // usage info from the active ROM (WF MapPointerNewPLISTPopupForm.InitUI).
            _vm.InitUI(CoreState.ROM);
        }

        void PlistId_ValueChanged(object? sender, NumericUpDownValueChangedEventArgs e)
        {
            // WF PLISTnumericUpDown_ValueChanged → PlistToName: refresh the
            // read-only info box and IsAlreadyUse for the new value.
            uint plist = (uint)(e.NewValue ?? 0m);
            _vm.UpdatePlistInfo(CoreState.ROM, plist);
        }

        void Extend_Click(object? sender, RoutedEventArgs e)
        {
            // The Avalonia PLIST split/extend editor flow is not wired, so this
            // button is hidden (ExtendVisible=false). Direct the user to the
            // Map Pointer editor if it is somehow invoked (#1433).
            CoreState.Services?.ShowInfo(
                R._("Use the Map Pointer editor to split/extend the PLIST range."));
        }

        void OK_Click(object? sender, RoutedEventArgs e)
        {
            _vm.PlistId = (uint)(PlistIdInput.Value ?? 0);

            // WF OKButton_Click: confirm before overwriting an already-used /
            // reserved / out-of-range PLIST; abort on No.
            if (_vm.IsAlreadyUse)
            {
                bool yes = CoreState.Services?.ShowYesNo(
                    MapPointerPlistUsageCore.OverwriteConfirmMessage()) ?? false;
                if (!yes)
                {
                    return;
                }
            }

            Close(_vm.PlistId);
        }

        void Cancel_Click(object? sender, RoutedEventArgs e) => Close(null);

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
    }
}
