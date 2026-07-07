using System;
using global::Avalonia;
using global::Avalonia.Controls;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    /// <summary>
    /// Read-only "Flags-Used-in-Chapter" tool (issue #1192 — port of WinForms
    /// <c>ToolUseFlagForm</c>). Pick a chapter on the left; the middle list shows
    /// every event-flag referenced by that chapter's event conditions, the event
    /// scripts they reach, and its map changes (via the cross-platform
    /// <see cref="UseFlagScanCore"/>). Selecting a usage shows its detail; a
    /// double-click / Enter jumps to the referencing bytes in the Hex Editor.
    /// Strictly read-only — no ROM mutation, no undo.
    /// </summary>
    public partial class ToolUseFlagView : TranslatedUserControl, IEmbeddableEditor
    {
        readonly ToolUseFlagViewModel _vm = new();
        bool _hasLoadedList;

        public string ViewTitle => "Flags Used in Chapter";
        public new bool IsLoaded => _vm.IsLoaded;
        public EditorDescriptor Descriptor => new("Flags Used in Chapter", 1113, 720, SizeToContent: false);
        public event EventHandler? CloseRequested;
        public void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);

        public ToolUseFlagView()
        {
            InitializeComponent();
            DataContext = _vm;

            MapList.SelectedAddressChanged += OnMapSelected;
            EntryList.SelectedAddressChanged += OnUsageSelected;
            EntryList.SelectionConfirmed += OnUsageConfirmed;

        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            if (!_hasLoadedList)
            {
                _hasLoadedList = true;
                LoadMapList();
            }
        }

        // Populate the chapter selector. SetItems auto-selects the first row
        // (AddressListControl.SetItems calls SelectFirst internally), which fires
        // OnMapSelected → the flag list is non-empty on open. No explicit
        // SelectFirst needed.
        void LoadMapList()
        {
            try
            {
                MapList.SetItems(_vm.LoadMapList());
            }
            catch (Exception ex)
            {
                Log.Error("ToolUseFlagView.LoadMapList failed: " + ex.ToString());
            }
        }

        // A chapter was picked — re-scan and repopulate the flag-usage list. The
        // map id is the selected row's AddrResult.tag (MapSettingCore.MakeMapIDList
        // encodes the map id in tag), matching how the other map-selector views
        // resolve it.
        void OnMapSelected(uint mapAddr)
        {
            try
            {
                AddrResult? selected = MapList.SelectedItem;
                if (selected == null) return;
                uint mapId = selected.tag;
                EntryList.SetItems(_vm.LoadForChapter(mapId));
            }
            catch (Exception ex)
            {
                Log.Error("ToolUseFlagView.OnMapSelected failed: " + ex.ToString());
            }
        }

        // Index-keyed detail load so duplicate flag-id rows each resolve their own
        // detail.
        void OnUsageSelected(uint addr)
        {
            try
            {
                _vm.LoadEntryByIndex(EntryList.SelectedOriginalIndex);
            }
            catch (Exception ex)
            {
                Log.Error("ToolUseFlagView.OnUsageSelected failed: " + ex.ToString());
            }
        }

        // Double-click / Enter: jump to the referencing bytes in the Hex Editor.
        void OnUsageConfirmed(PickResult pick)
        {
            try
            {
                if (_vm.TryGetJumpOffset(pick.Index, out uint offset))
                    WindowManager.Instance.Navigate<HexEditorView>(offset);
            }
            catch (Exception ex)
            {
                Log.Error("ToolUseFlagView.OnUsageConfirmed failed: " + ex.ToString());
            }
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => MapList.SelectFirst();
    }
}
