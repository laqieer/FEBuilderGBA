using global::Avalonia;
using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class MapPointerNewPLISTPopupView : TranslatedUserControl, IEmbeddableEditor, IDataVerifiableView
    {
        readonly MapPointerNewPLISTPopupViewModel _vm = new();

        public string ViewTitle => "Map Pointer - New PLIST";
        public new bool IsLoaded => _vm.IsLoaded;
        public EditorDescriptor Descriptor => new("Map Pointer - New PLIST", 953, 423, SizeToContent: global::Avalonia.Controls.SizeToContent.WidthAndHeight, CanResize: false);
        public event EventHandler? CloseRequested;
        public object? DialogResult { get; private set; }
        public ViewModelBase? DataViewModel => _vm;
        public void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);

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
            uint plist = (uint)(PlistIdInput.Value ?? 0);
            // Recompute usage for the committed value before the gate so a value
            // committed without a ValueChanged (e.g. typed + Enter) cannot skip
            // the confirmation with stale IsAlreadyUse state.
            _vm.UpdatePlistInfo(CoreState.ROM, plist);

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

            DialogResult = _vm.PlistId; RequestClose();
        }

        void Cancel_Click(object? sender, RoutedEventArgs e) { DialogResult = null; RequestClose(); }

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
    }
}
