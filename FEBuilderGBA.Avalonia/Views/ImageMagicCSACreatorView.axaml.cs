using System;
using System.Collections.Generic;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    /// <summary>
    /// Avalonia counterpart of WinForms <c>ImageMagicCSACreatorForm</c>.
    /// Gap-sweep fix (#417) raises the AXAML control surface from 3 to
    /// MEDIUM-verdict density (&gt;=28 of WF's 37 controls), wires the
    /// read-config / selection-bar / dim selector / animation buttons,
    /// and carries <c>AddrResult.tag</c> separately from the CSA struct
    /// address so the dim/no-dim/empty pointer write targets the correct
    /// pointer-table slot (Copilot CLI plan review #1).
    ///
    /// Real CSA animation import/export, list expansion, source open/select,
    /// JumpEditor, and live preview rendering remain WF-coupled and are
    /// surfaced as disabled buttons with tooltips referencing the open
    /// follow-up <c>#500</c> (ToolAnimationCreator real-init flow).
    /// </summary>
    public partial class ImageMagicCSACreatorView : TranslatedWindow, IEditorView, IDataVerifiableView
    {
        readonly ImageMagicCSACreatorViewModel _vm = new();
        readonly UndoService _undoService = new();

        public string ViewTitle => "CSA Magic Creator";
        public bool IsLoaded => _vm.IsLoaded;
        public ViewModelBase? DataViewModel => _vm;

        public ImageMagicCSACreatorView()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            Opened += (_, _) => LoadList();
        }

        void LoadList()
        {
            _vm.IsLoading = true;
            try
            {
                var items = _vm.LoadList();
                EntryList.SetItems(items);

                // Populate the top read-config bar so the UI reflects the
                // actual scan state (Copilot bot inline review #1 round 2:
                // start address = pointer-table base, count = spell-data cap).
                // The values come from the ViewModel after LoadList resolves
                // the magic system. If no system is detected, both stay 0.
                var rom = CoreState.ROM;
                uint readStart = 0;
                if (rom?.RomInfo != null && _vm.MagicKind == MagicSystemKind.CsaCreator)
                {
                    readStart = rom.p32(rom.RomInfo.magic_effect_pointer);
                }
                ReadStartAddressBox.Value = readStart;
                ReadCountBox.Value = _vm.SpellDataCount;

                if (items.Count == 0)
                {
                    ClearDetailPanel();
                    UpdateUI();
                }
            }
            catch (Exception ex)
            {
                Log.Error("ImageMagicCSACreatorView.LoadList failed: {0}", ex.Message);
            }
            finally
            {
                _vm.IsLoading = false;
                _vm.MarkClean();
            }
        }

        void ClearDetailPanel()
        {
            _vm.IsLoaded = false;
            _vm.CurrentAddr = 0;
            _vm.TagAddress = 0;
            _vm.P0 = 0;
            _vm.P4 = 0;
            _vm.P8 = 0;
            _vm.P12 = 0;
            _vm.P16 = 0;
            _vm.DimMode = 2;
            _vm.Comment = "";
            _vm.Frame = 0;
            _vm.Zoom = 0;
            _vm.BinInfo = "";
        }

        void ReloadList_Click(object? sender, RoutedEventArgs e) => LoadList();

        void OnSelected(uint addr)
        {
            _vm.IsLoading = true;
            try
            {
                // Carry AddrResult.tag from the AddressListControl - the dim
                // selector targets this slot (Copilot CLI plan review #1).
                uint tagAddr = 0;
                var selected = EntryList.SelectedItem;
                if (selected != null) tagAddr = selected.tag;
                _vm.LoadEntry(addr, tagAddr);
                UpdateUI();
            }
            catch (Exception ex)
            {
                Log.Error("ImageMagicCSACreatorView.OnSelected failed: {0}", ex.Message);
            }
            finally
            {
                _vm.IsLoading = false;
                _vm.MarkClean();
            }
        }

        void UpdateUI()
        {
            // The selection bar shows TWO different addresses (mirrors WF
            // panel5): AddressBox = the pointer-table slot (TagAddress), and
            // SelectedAddressLabel = the CSA struct itself (CurrentAddr).
            // Showing the same address in both was a Copilot CLI inline
            // review finding on PR #547.
            AddrLabel.Text = string.Format("0x{0:X08}", _vm.CurrentAddr);
            AddressBox.Value = _vm.TagAddress;
            SelectedAddressLabel.Content = string.Format("0x{0:X08}", _vm.CurrentAddr);
            P0Box.Text = string.Format("0x{0:X08}", _vm.P0);
            P4Box.Text = string.Format("0x{0:X08}", _vm.P4);
            P8Box.Text = string.Format("0x{0:X08}", _vm.P8);
            P12Box.Text = string.Format("0x{0:X08}", _vm.P12);
            P16Box.Text = string.Format("0x{0:X08}", _vm.P16);
            DimComboBox.SelectedIndex = (int)_vm.DimMode;
            CommentBox.Text = _vm.Comment;
            FrameBox.Value = _vm.Frame;
            ZoomComboBox.SelectedIndex = (int)_vm.Zoom;
            BinInfoBox.Text = _vm.BinInfo;
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.IsLoaded || _vm.CurrentAddr == 0) return;
            uint reloadAddr = _vm.CurrentAddr;
            uint reloadTag = _vm.TagAddress;
            _undoService.Begin("Edit CSA Magic Entry");
            try
            {
                _vm.P0 = ParseHexText(P0Box.Text);
                _vm.P4 = ParseHexText(P4Box.Text);
                _vm.P8 = ParseHexText(P8Box.Text);
                _vm.P12 = ParseHexText(P12Box.Text);
                _vm.P16 = ParseHexText(P16Box.Text);
                int dimIdx = DimComboBox.SelectedIndex;
                if (dimIdx < 0) dimIdx = 2;
                _vm.DimMode = (uint)dimIdx;
                _vm.Comment = CommentBox.Text ?? "";

                _vm.Write();
                _undoService.Commit();
                _vm.MarkClean();

                // Reload so the BinInfo / SelectedAddress / RGB labels reflect
                // the new ROM bytes (mirrors the MapTileAnimation2View pattern).
                _vm.IsLoading = true;
                try
                {
                    _vm.LoadEntry(reloadAddr, reloadTag);
                    UpdateUI();
                }
                finally
                {
                    _vm.IsLoading = false;
                    _vm.MarkClean();
                }
                CoreState.Services?.ShowInfo("CSA Magic entry written.");
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.Error("ImageMagicCSACreatorView.Write failed: {0}", ex.Message);
                // Surface the error to the user so it's actionable instead
                // of silently logged (Copilot bot inline review #2 round 2).
                CoreState.Services?.ShowError($"Write failed: {ex.Message}");
            }
        }

        // ---- deferred affordances (#500) ----

        void ListExpand_Click(object? sender, RoutedEventArgs e) =>
            Log.Debug("ImageMagicCSACreatorView.ListExpand_Click invoked - disabled until #500 lands");

        void Import_Click(object? sender, RoutedEventArgs e) =>
            Log.Debug("ImageMagicCSACreatorView.Import_Click invoked - disabled until #500 lands");

        void Export_Click(object? sender, RoutedEventArgs e) =>
            Log.Debug("ImageMagicCSACreatorView.Export_Click invoked - disabled until #500 lands");

        void OpenSource_Click(object? sender, RoutedEventArgs e) =>
            Log.Debug("ImageMagicCSACreatorView.OpenSource_Click invoked - disabled until #500 lands");

        void SelectSource_Click(object? sender, RoutedEventArgs e) =>
            Log.Debug("ImageMagicCSACreatorView.SelectSource_Click invoked - disabled until #500 lands");

        void Editor_Click(object? sender, RoutedEventArgs e) =>
            Log.Debug("ImageMagicCSACreatorView.Editor_Click invoked - disabled until #500 lands");

        static uint ParseHexText(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return 0;
            text = text.Trim();
            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) text = text[2..];
            return uint.TryParse(text, System.Globalization.NumberStyles.HexNumber, null, out uint v) ? v : 0;
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
    }
}
