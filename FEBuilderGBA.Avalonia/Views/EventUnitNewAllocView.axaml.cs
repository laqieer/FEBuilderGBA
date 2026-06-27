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
    public partial class EventUnitNewAllocView : TranslatedWindow, IEditorView
    {
        readonly EventUnitNewAllocViewModel _vm = new();

        public string ViewTitle => "Unit Allocation Editor";
        public bool IsLoaded => _vm.IsLoaded;

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
                Close((uint?)count);
            }
            catch (Exception ex)
            {
                Log.ErrorF("EventUnitNewAllocView.OK_Click failed: {0}", ex.Message);
                Close((uint?)null);
            }
        }

        void Cancel_Click(object? sender, RoutedEventArgs e)
        {
            Close((uint?)null);
        }

        // IEditorView members (kept so existing Open<>/Navigate<> entry points
        // and the navigation-target manifest still compile). This window is a
        // modal picker, so these are no-ops.
        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
    }
}
