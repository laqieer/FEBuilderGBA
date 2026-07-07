using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    /// <summary>
    /// Modal count-picker for EventUnit New(Alloc) (#776). Mirrors WF
    /// <c>EventUnitNewAllocForm</c>: a single NumericUpDown (Min=1, Max=50,
    /// Value=1) plus OK/Cancel. The caller awaits
    /// <c>ShowDialog&lt;uint?&gt;(owner)</c>, which returns the chosen count
    /// or <c>null</c> on Cancel/close.
    /// </summary>
    public partial class EventUnitNewAllocView : TranslatedUserControl, IEmbeddableEditor
    {
        readonly EventUnitNewAllocViewModel _vm = new();

        public string ViewTitle => "Unit Allocation Editor";
        public new bool IsLoaded => _vm.IsLoaded;
        public EditorDescriptor Descriptor => new("Unit Allocation Editor", 320, 180, SizeToContent: global::Avalonia.Controls.SizeToContent.WidthAndHeight, CanResize: false);
        public event EventHandler? CloseRequested;
        public object? DialogResult { get; private set; }
        public void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);

        /// <summary>
        /// The count selected via the picker (parity with WF
        /// <c>EventUnitNewAllocForm.AllocCount</c>) for non-ShowDialog
        /// callers. Set when OK is clicked.
        /// </summary>
        public uint AllocCount => _vm.AllocCount;

        public EventUnitNewAllocView()
        {
            InitializeComponent();
            DataContext = _vm;
            // Localize the labels (R._ so ja/zh users see translated text).
            PromptLabel.Text = R._("How many units?");
            CountLabel.Text = R._("Count:");
        }

        void OK_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                uint count = (uint)(AllocCountBox.Value ?? 1);
                _vm.AllocCount = count;
                _vm.IsLoaded = true;
                DialogResult = (uint?)count; RequestClose();
            }
            catch (Exception ex)
            {
                Log.ErrorF("EventUnitNewAllocView.OK_Click failed: {0}", ex.Message);
                DialogResult = (uint?)null; RequestClose();
            }
        }

        void Cancel_Click(object? sender, RoutedEventArgs e)
        {
            DialogResult = (uint?)null; RequestClose();
        }

        // IEditorView members (kept so existing Open<>/Navigate<> entry points
        // and the navigation-target manifest still compile). This window is a
        // modal picker, so these are no-ops.
        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
    }
}
