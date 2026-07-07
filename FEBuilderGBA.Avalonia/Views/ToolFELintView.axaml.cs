using System;
using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    /// <summary>
    /// Read-only Lint (FELint) GUI tool (issue #1168 — port of WinForms
    /// <c>ToolFELintForm</c>). Auto-runs the cross-platform <see cref="FELintScanner"/>
    /// on open, lists each finding, shows the selected finding's detail, and lets the
    /// user double-click/Enter a jumpable finding to open it in the Hex Editor.
    /// </summary>
    public partial class ToolFELintView : TranslatedUserControl, IEmbeddableEditor
    {
        readonly ToolFELintViewModel _vm = new();
        bool _hasLoadedList;

        public string ViewTitle => "FELint GUI";
        public new bool IsLoaded => _vm.IsLoaded;
        public EditorDescriptor Descriptor => new("FELint GUI", 1099, 788, SizeToContent: global::Avalonia.Controls.SizeToContent.WidthAndHeight);
        public event EventHandler? CloseRequested;
        public void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);

        public ToolFELintView()
        {
            InitializeComponent();
            DataContext = _vm;
            EntryList.SelectedAddressChanged += OnSelected;
            EntryList.SelectionConfirmed += OnConfirmed;
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            if (!_hasLoadedList)
            {
                _hasLoadedList = true;
                LoadList();
            }
        }

        void LoadList()
        {
            try
            {
                var items = _vm.LoadList();
                EntryList.SetItems(items);
            }
            catch (Exception ex)
            {
                Log.Error("ToolFELintView.LoadList failed: " + ex.ToString());
            }
        }

        // Selection changes resolve the detail panel by the row's original index, NOT by
        // address, so duplicate-address findings (e.g. multiple SYSTEM_MAP_ID errors) each
        // show their own message.
        void OnSelected(uint addr)
        {
            try
            {
                _vm.LoadEntryByIndex(EntryList.SelectedOriginalIndex);
            }
            catch (Exception ex)
            {
                Log.Error("ToolFELintView.OnSelected failed: " + ex.ToString());
            }
        }

        // Double-click / Enter: jump to the Hex Editor for a real, jumpable finding.
        // System/global rows (header sentinel, "no problems") are no-ops.
        void OnConfirmed(PickResult pick)
        {
            try
            {
                if (_vm.TryGetJumpOffset(pick.Index, out uint offset))
                    WindowManager.Instance.Navigate<HexEditorView>(offset);
            }
            catch (Exception ex)
            {
                Log.Error("ToolFELintView.OnConfirmed failed: " + ex.ToString());
            }
        }

        void Rescan_Click(object? sender, RoutedEventArgs e)
        {
            LoadList();
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
    }
}
