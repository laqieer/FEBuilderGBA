using global::Avalonia;
using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    /// <summary>
    /// 4-slot colour-override picker for the event-script <c>UNIT_COLOR</c>
    /// argument (#1444). Ports WinForms <c>EventUnitColorForm</c>. Reachable
    /// both standalone (main menu) and as a result-returning dialog launched
    /// from the Avalonia event-script editor (mirrors
    /// <c>EventScriptInnerControl</c> → <c>EventUnitColorForm.JumpTo</c> →
    /// <c>ApplyButton.Tag</c>): when launched for a UNIT_COLOR argument the
    /// dialog is seeded with the current value and, on Apply, closes returning
    /// the packed <see cref="uint"/>.
    /// </summary>
    public partial class EventUnitColorView : TranslatedUserControl, IEmbeddableEditor
    {
        readonly EventUnitColorViewModel _vm = new();

        public string ViewTitle => "Unit Color";
        public new bool IsLoaded => _vm.IsLoaded;
        public EditorDescriptor Descriptor => new("Unit Color", 520, 320, SizeToContent: global::Avalonia.Controls.SizeToContent.WidthAndHeight, CanResize: false);
        public event EventHandler? CloseRequested;
        public object? DialogResult { get; private set; }
        public void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);

        public EventUnitColorView()
        {
            InitializeComponent();
            DataContext = _vm;
            _vm.IsLoading = true;
            _vm.Initialize();
            _vm.IsLoading = false;
            _vm.MarkClean();
        }

        void Apply_Click(object? sender, RoutedEventArgs e)
        {
            // Return the packed value to a ShowDialog<uint?> caller; harmless
            // (ignored result) for a standalone main-menu open. Box as the exact
            // uint? the caller awaits — a boxed plain uint cannot be unboxed to
            // uint? and would surface as null.
            DialogResult = (uint?)_vm.Result; RequestClose();
        }

        /// <summary>
        /// Seed the picker from a packed UNIT_COLOR value (the event editor
        /// supplies the argument's current value before ShowDialog).
        /// </summary>
        public void NavigateTo(uint address)
        {
            try
            {
                _vm.IsLoading = true;
                _vm.Seed(address);
            }
            catch (Exception ex)
            {
                Log.Error("EventUnitColorView.NavigateTo failed: ", ex.ToString());
            }
            finally
            {
                _vm.IsLoading = false;
                _vm.MarkClean();
            }
        }

        public void SelectFirstItem() { /* no list — value picker */ }
    }
}
