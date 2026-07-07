using System;
using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class MapPointerView : TranslatedUserControl, IEmbeddableEditor, IDataVerifiableView
    {
        readonly MapPointerViewModel _vm = new();
        readonly UndoService _undoService = new();


        bool _hasLoadedList;
        public string ViewTitle => "Map Pointer Editor";
        public new bool IsLoaded => _vm.CanWrite;

        public EditorDescriptor Descriptor => new("Map Pointer Editor", 1305, 532, SizeToContent: true);

        public event EventHandler? CloseRequested;
        public ViewModelBase? DataViewModel => _vm;


        public void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);
        public MapPointerView()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            PlistTypeCombo.SelectionChanged += PlistType_Changed;        }


        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)

        {

            base.OnAttachedToVisualTree(e);

            if (!_hasLoadedList)

            {

                _hasLoadedList = true;

                InitFilter();

            }

        }

        void InitFilter()
        {
            var names = _vm.GetPlistTypeNames();
            PlistTypeCombo.ItemsSource = names;
            if (names.Count > 0)
                PlistTypeCombo.SelectedIndex = 0;
            // Always load — SelectionChanged may not fire if Avalonia auto-selects index 0
            LoadList(Math.Max(0, PlistTypeCombo.SelectedIndex));
            RefreshSplitPanel();
        }

        // Show the PLIST Split panel only when the ROM is NOT yet split — WF
        // hides the "PLIST分割" panel once IsPlistSplits()==true (#1432).
        void RefreshSplitPanel()
        {
            bool canSplit = _vm.CanSplit;
            PlistSplitPanel.IsVisible = canSplit;
            if (canSplit)
            {
                PlistSplitExplainLabel.Text = R._(
                    "PLIST Split\r\nPLIST tables pack several purposes into one shared array, " +
                    "so splitting them by purpose increases the number of usable PLIST slots " +
                    "(each table is expanded to 256 entries / 0xFF).\r\n" +
                    "(This is a destructive operation — be sure to back up your ROM first.)");
                PlistSplitButton.Content = R._("PLIST Split");
            }
        }

        void PlistType_Changed(object? sender, SelectionChangedEventArgs e)
        {
            LoadList(PlistTypeCombo.SelectedIndex);
        }

        void LoadList(int typeIndex = 0)
        {
            _vm.IsLoading = true;
            try
            {
                var items = _vm.LoadMapPointerList(typeIndex);
                EntryList.SetItems(items);
            }
            catch (Exception ex)
            {
                Log.Error("MapPointerView.LoadList failed: " + ex);
            }
            finally { _vm.IsLoading = false; _vm.MarkClean(); }
        }

        void OnSelected(uint addr)
        {
            _vm.IsLoading = true;
            try
            {
                _vm.LoadMapPointer(addr);
                // Authoritatively carry the selected row's PLIST slot id (the
                // list builds rows as new AddrResult(addr, name, i), so tag ==
                // the slot id). This drives the slot-0 write-protection guard
                // and mirrors WinForms reading the selected list row (#1416).
                _vm.SelectedId = EntryList.SelectedItem?.tag ?? _vm.SelectedId;
                UpdateUI();
            }
            catch (Exception ex)
            {
                Log.Error("MapPointerView.OnSelected failed: " + ex);
            }
            finally { _vm.IsLoading = false; _vm.MarkClean(); }
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            MapDataPointerBox.Text = $"0x{_vm.MapDataPointer:X08}";
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.CanWrite) return;
            _undoService.Begin("Edit Map Pointer");
            try
            {
                _vm.MapDataPointer = ParseHexText(MapDataPointerBox.Text);
                string? error = _vm.WriteMapPointer();
                if (error != null)
                {
                    // WF parity: slot 0 (reserved NULL) write was rejected — no
                    // ROM mutation occurred, so discard the (empty) undo scope
                    // and surface the stop error (#1416).
                    _undoService.Rollback();
                    CoreState.Services?.ShowError(error);
                    return;
                }
                _undoService.Commit();
                _vm.MarkClean();
                CoreState.Services?.ShowInfo("Map Pointer data written.");
            }
            catch (Exception ex) { _undoService.Rollback(); Log.Error("MapPointerView.Write: " + ex); }
        }

        // PLIST Split/Expand — port of WinForms MapPointerForm.PListSplitsExpandsButton_Click
        // (#1432). Confirm (destructive op), run the atomic Core split, then on
        // success reload the list (limit becomes 256) and hide the panel.
        void PlistSplit_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.CanSplit) return;

            bool yes = CoreState.Services?.ShowYesNo(
                R._("Split the PLIST tables?\r\n" +
                    "This is a destructive operation — be sure to back up your ROM first.")) ?? false;
            if (!yes) return;

            // The Core helper (MapPlistSplitCore.Split) owns its own snapshot +
            // undo scope and leaves the ROM byte-identical on any fault, so the
            // View does NOT open a redundant outer UndoService scope here.
            string? error = _vm.SplitPlist();
            if (error != null)
            {
                CoreState.Services?.ShowError(error);
                return;
            }

            // Re-init: split-PLIST ROMs are byte-indexed (limit 256), so reload
            // the filter list and refresh the now-hidden split panel.
            InitFilter();
            CoreState.Services?.ShowInfo(R._("PLIST tables split."));
        }

        public void NavigateTo(uint address)
        {
            EntryList.SelectAddress(address);
        }

        static uint ParseHexText(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return 0;
            text = text.Trim();
            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) text = text[2..];
            return uint.TryParse(text, System.Globalization.NumberStyles.HexNumber, null, out uint v) ? v : 0;
        }

        public void SelectFirstItem()
        {
            EntryList.SelectFirst();
        }
    }
}
