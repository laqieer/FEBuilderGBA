using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using global::Avalonia.Controls;
using global::Avalonia.Controls.Shapes;
using global::Avalonia.Input;
using global::Avalonia.Interactivity;
using global::Avalonia.Media;
using global::Avalonia.Platform.Storage;
using FEBuilderGBA.Avalonia.Controls;
using FEBuilderGBA.Avalonia.Dialogs;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class MapStyleEditorView : TranslatedWindow, IEditorView, IDataVerifiableView
    {
        readonly MapStyleEditorViewModel _vm = new();
        readonly UndoService _undoService = new();
        List<AddrResult> _styleList = new();

        public string ViewTitle => "Map Style Editor";
        public bool IsLoaded => _vm.IsLoaded;

        public MapStyleEditorView()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            // Re-load the palette when either the palette index or the
            // fog flag changes — this keeps the 16-row RGB grid in sync
            // with the user's selection (mirrors WF behavior).
            PaletteCombo.SelectionChanged += (_, _) => ReloadPalette();
            PaletteTypeCombo.SelectionChanged += (_, _) => ReloadPalette();
            MapStyleCombo.SelectionChanged += MapStyle_SelectionChanged;
            Opened += (_, _) => LoadList();
            // Wire ValueChanged handlers on the 48 editable RGB NumericUpDowns
            // so user edits sync into the VM and the swatch updates live.
            // The handler is no-op when _vm.IsLoading is true so programmatic
            // population (LoadEntry / ReloadPalette) does not feed itself.
            WireColorBoxes();
            // Chipset tab wiring (#671).
            WireChipsetControls();
            // Alt+T / Alt+C / Alt+V hotkeys mirror WF MapStyleEditorForm_KeyDown.
            KeyDown += OnChipsetHotKey;
            // Start with the Chipset edit surface disabled — OnSelected will
            // enable it once a successful TryLoadChipsetTSA confirms a valid
            // CONFIG cache + plist (Copilot bot v2 inline review on PR #691).
            // Set synchronously now so we don't race the first OnSelected.
            SetChipsetEditingEnabled(false);

            // OBJ Image Import button gating (Copilot bot v2 inline review
            // item 1 on PR #716). Initially disabled; OnSelected refreshes
            // it once an entry is loaded and the VM resolves a valid
            // OBJ PLIST + OBJ palette. Listening to CanImportObj's
            // OnPropertyChanged keeps the button in sync with both the
            // entry-level reload (ObjAddress2 / _currentObjPlist updates)
            // and the per-style ObjAddress2 mutation. FE7 dual-tileset
            // (obj2) styles are supported via the split write (#976) and
            // gated on the secondary plist being in range.
            if (ObjImportButton != null) ObjImportButton.IsEnabled = false;
            _vm.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(_vm.CanImportObj))
                    RefreshObjImportEnabled();
            };
        }

        /// <summary>
        /// Push the VM's current <see cref="MapStyleEditorViewModel.CanImportObj"/>
        /// value onto the OBJ Image Import button's <c>IsEnabled</c>.
        /// Called from <see cref="OnSelected"/> after every entry load and
        /// from the VM <c>PropertyChanged</c> handler so the button reflects
        /// the latest predicate state.
        /// </summary>
        void RefreshObjImportEnabled()
        {
            if (ObjImportButton != null) ObjImportButton.IsEnabled = _vm.CanImportObj;
        }

        /// <summary>
        /// Wire ValueChanged handlers on the 20 slot NumericUpDowns, the
        /// ChipsetNoInput, and the terrain combo so user edits flow into
        /// the VM. The handlers are no-ops while <c>_vm.IsLoading</c> is
        /// true so programmatic population (LoadEntry / ReadSlotsFromVM /
        /// PopulateTerrainCombo) does not feed itself.
        /// </summary>
        void WireChipsetControls()
        {
            if (ChipsetNoInput != null)
                ChipsetNoInput.ValueChanged += (_, _) => OnChipsetNoChanged();
            if (ConfigTerrainCombo != null)
                ConfigTerrainCombo.SelectionChanged += (_, _) => OnTerrainChanged();

            int[] suffixes = { 0, 2, 4, 6 };
            foreach (int s in suffixes)
            {
                int suffix = s;
                int logical = SuffixToLogicalIndex(suffix);
                var xBox = this.FindControl<NumericUpDown>($"Slot{suffix}_XBox");
                var yBox = this.FindControl<NumericUpDown>($"Slot{suffix}_YBox");
                var palBox = this.FindControl<NumericUpDown>($"Slot{suffix}_PALETTEBox");
                var flipBox = this.FindControl<NumericUpDown>($"Slot{suffix}_FLIPBox");
                var wBox = this.FindControl<NumericUpDown>($"Slot{suffix}_WBox");
                if (xBox != null) xBox.ValueChanged += (_, _) => OnSlotSplitChanged(logical, suffix);
                if (yBox != null) yBox.ValueChanged += (_, _) => OnSlotSplitChanged(logical, suffix);
                if (palBox != null) palBox.ValueChanged += (_, _) => OnSlotSplitChanged(logical, suffix);
                if (flipBox != null) flipBox.ValueChanged += (_, _) => OnSlotSplitChanged(logical, suffix);
                if (wBox != null) wBox.ValueChanged += (_, _) => OnSlotRawWChanged(logical, suffix);
            }
        }

        /// <summary>
        /// Map an AXAML control suffix (0/2/4/6 — WF historical byte offset
        /// into the 8-byte TSA record) to the VM's logical sub-tile index
        /// (0..3). Used by every chipset handler so VM methods always receive
        /// 0..3, never raw byte offsets (v5 plan #3 — slot mapping).
        /// </summary>
        static int SuffixToLogicalIndex(int suffix) => suffix / 2;

        void WireColorBoxes()
        {
            for (int i = 1; i <= 16; i++)
            {
                int row = i; // capture for closure
                var rBox = this.FindControl<NumericUpDown>($"Color{i}_RBox");
                var gBox = this.FindControl<NumericUpDown>($"Color{i}_GBox");
                var bBox = this.FindControl<NumericUpDown>($"Color{i}_BBox");
                if (rBox != null) rBox.ValueChanged += (_, _) => OnColorChannelChanged(row, 'R', rBox);
                if (gBox != null) gBox.ValueChanged += (_, _) => OnColorChannelChanged(row, 'G', gBox);
                if (bBox != null) bBox.ValueChanged += (_, _) => OnColorChannelChanged(row, 'B', bBox);
            }
        }

        void OnColorChannelChanged(int row, char channel, NumericUpDown box)
        {
            // Programmatic load/clear paths set _vm.IsLoading = true to
            // suppress side effects (Copilot v2 non-blocking guidance).
            if (_vm.IsLoading) return;
            // NumericUpDown.Value is decimal? — cast through int before
            // masking to 5 bits to satisfy the 0..0x1F clamp.
            int raw = (int)(box.Value ?? 0m);
            ushort v = (ushort)(raw & 0x1F);
            switch (channel)
            {
                case 'R': _vm.SetColorR(row, v); break;
                case 'G': _vm.SetColorG(row, v); break;
                case 'B': _vm.SetColorB(row, v); break;
            }
            UpdateSwatch(row);
        }

        void LoadList()
        {
            try
            {
                _styleList = _vm.LoadList();
                EntryList.SetItems(_styleList);
                // Mirror EntryList into MapStyleCombo so the top-bar
                // selector is also populated (Copilot bot inline review
                // on MapStyleCombo).
                MapStyleCombo.ItemsSource = _styleList.ConvertAll(r => r.name);
                if (MapStyleCombo.ItemsSource != null && _styleList.Count > 0)
                {
                    MapStyleCombo.SelectedIndex = 0;
                }
            }
            catch (Exception ex)
            {
                Log.Error("MapStyleEditorView.LoadList failed: {0}", ex.Message);
            }
        }

        void OnSelected(uint addr)
        {
            try
            {
                _vm.IsLoading = true;
                _vm.LoadEntry(addr);
                UpdateUI();

                // Deterministic Chipset Tab initial load (v7 #1): explicitly
                // load chipset 0 regardless of whether the NUD value changed.
                // Without this, selecting a new style with ChipsetNoInput
                // already at 0 leaves slot fields/terrain stale or blank.
                _vm.CurrentChipsetNo = 0;
                bool chipsetLoaded = _vm.CanEditChipsetConfig && _vm.TryLoadChipsetTSA(0);
                if (ChipsetNoInput != null)
                    ChipsetNoInput.Value = chipsetLoaded ? 0m : (decimal?)null;
                PopulateTerrainCombo();
                if (chipsetLoaded) ReadSlotsFromVM();
                else ClearChipsetUI();
                SetChipsetEditingEnabled(chipsetLoaded);
                // #710 / Copilot bot v2 item 1: re-evaluate the OBJ Import
                // button after every entry load so it's disabled on
                // unsupported entries (FE7 obj2, unresolved plist, ROM-less
                // state).
                RefreshObjImportEnabled();

                // Sync the top-bar MapStyleCombo to the same entry without
                // recursing — block the SelectionChanged handler while we
                // assign by toggling _vm.IsLoading.
                int idx = _styleList.FindIndex(r => r.addr == addr);
                if (idx >= 0 && MapStyleCombo.SelectedIndex != idx)
                    MapStyleCombo.SelectedIndex = idx;
                _vm.IsLoading = false;
                _vm.MarkClean();
                RefreshChipPreview();
            }
            catch (Exception ex)
            {
                _vm.IsLoading = false;
                Log.Error("MapStyleEditorView.OnSelected failed: {0}", ex.Message);
            }
        }

        /// <summary>
        /// Repaint the Map Chip Preview thumbnail (#670). Pulls the cached
        /// OBJ tile sheet + palette block from the VM and applies the
        /// currently-selected palette index (with fog offset). Clears to
        /// a 1x1 transparent pixel when no preview is available so a
        /// stale image cannot leak from a previous entry.
        /// </summary>
        void RefreshChipPreview()
        {
            if (MapChipPreviewImage == null) return;
            if (_vm.TryRenderObjTileSheet(out var rgba, out var w, out var h))
            {
                MapChipPreviewImage.SetRgbaData(rgba, w, h);
            }
            else
            {
                MapChipPreviewImage.SetRgbaData(new byte[4], 1, 1);
            }
        }

        void MapStyle_SelectionChanged(object? sender, global::Avalonia.Controls.SelectionChangedEventArgs e)
        {
            if (_vm.IsLoading) return;
            int idx = MapStyleCombo.SelectedIndex;
            if (idx < 0 || idx >= _styleList.Count) return;
            // Forward selection to the AddressList so all other UI stays
            // in sync via the existing OnSelected path.
            EntryList.SelectAddress(_styleList[idx].addr);
        }

        void ReloadPalette()
        {
            if (_vm.IsLoading) return;
            int idx = PaletteCombo.SelectedIndex;
            if (idx < 0) idx = 0;
            bool fog = PaletteTypeCombo.SelectedIndex == 1;
            _vm.IsLoading = true;
            try
            {
                // Pass the STABLE base address (PaletteBaseAddress) not
                // the slice (PaletteAddress) -- otherwise the previous
                // slice becomes the new base and the read drifts by
                // idx*0x20 on each selection (Copilot bot v2 inline review).
                bool ok = _vm.LoadPalette(_vm.PaletteBaseAddress, idx, fog);
                if (ok)
                {
                    UpdatePaletteUI();
                    PaletteAddressLabel.Text = $"0x{_vm.PaletteAddress:X08}";
                }
                else
                {
                    // Out-of-bounds / invalid base -- clear stale RGB values
                    // AND the address label so the user doesn't see a wrong
                    // palette OR a stale address (Copilot bot v3 inline review).
                    // ALSO clear VM state so PaletteWrite cannot accidentally
                    // mutate the previous slice (Copilot PR v2 review --
                    // stale-state regression).
                    _vm.ClearPaletteState();
                    ClearPaletteUI();
                    PaletteAddressLabel.Text = "(invalid)";
                }
            }
            finally { _vm.IsLoading = false; }
            // Repaint the chip preview after the palette index/fog flag
            // changes so the thumbnail reflects the new selection.
            RefreshChipPreview();
        }

        void ClearPaletteUI()
        {
            for (int i = 1; i <= 16; i++)
            {
                var rBox = this.FindControl<NumericUpDown>($"Color{i}_RBox");
                var gBox = this.FindControl<NumericUpDown>($"Color{i}_GBox");
                var bBox = this.FindControl<NumericUpDown>($"Color{i}_BBox");
                if (rBox != null) rBox.Value = 0;
                if (gBox != null) gBox.Value = 0;
                if (bBox != null) bBox.Value = 0;
                // Also clear the swatch so the user does not see a stale
                // preview color after a failed reload.
                var rect = this.FindControl<Rectangle>($"Color{i}_Swatch");
                if (rect != null) rect.Fill = new SolidColorBrush(Colors.Black);
            }
        }

        void UpdateUI()
        {
            // Tab 1 -- Map Style.
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            ObjPtrBox.Text = $"0x{_vm.ObjPointer:X08}";
            ConfigPtrLabel.Text = $"0x{_vm.ConfigPointer:X08}";
            ObjAddressLabel.Text = $"0x{_vm.ObjAddress:X08}";
            ObjAddress2Label.Text = _vm.ObjAddress2 != 0 ? $"0x{_vm.ObjAddress2:X08}" : "(none)";
            PaletteAddressLabel.Text = $"0x{_vm.PaletteAddress:X08}";
            TilesetTypeLabel.Text = $"Tileset {_vm.ConfigNo}";

            // Tab 3 -- Chipset.
            ChipsetConfigAddressLabel.Text = $"0x{_vm.ChipsetConfigAddress:X08}";
            // Hidden TextBlock retains the legacy MapStyleEditor_ConfigNo_Label
            // AutomationId for parity scans; the visible NUD ChipsetNoInput
            // is the user-facing editor (set via OnSelected -> TryLoadChipsetTSA).
            ConfigNoLabel.Text = _vm.ConfigNo;

            // Tab 2 -- Palette (populated by LoadEntry's LoadPalette call).
            UpdatePaletteUI();
        }

        void UpdatePaletteUI()
        {
            // Populate the 16 editable RGB rows from the VM's loaded
            // palette state (#660 first slice: NUDs are now editable
            // and the swatch column previews the current color).
            // _vm.IsLoading is set by the caller (OnSelected / ReloadPalette)
            // so the OnColorChannelChanged handler is suppressed during
            // programmatic population (Copilot v2 non-blocking guidance).
            for (int i = 1; i <= 16; i++)
            {
                var rBox = this.FindControl<NumericUpDown>($"Color{i}_RBox");
                var gBox = this.FindControl<NumericUpDown>($"Color{i}_GBox");
                var bBox = this.FindControl<NumericUpDown>($"Color{i}_BBox");
                if (rBox != null) rBox.Value = _vm.GetColorR(i);
                if (gBox != null) gBox.Value = _vm.GetColorG(i);
                if (bBox != null) bBox.Value = _vm.GetColorB(i);
                UpdateSwatch(i);
            }
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            // #1148: the map tileset OBJ/style is a raw source-tree asset in decomp mode.
            if (DecompMapAssetGuard.BlockIfDecomp(R._("map tileset (OBJ)")))
                return;

            _vm.ObjPointer = ParseHexText(ObjPtrBox.Text);

            _undoService.Begin("Edit Map Style");
            try
            {
                _vm.Write();
                _undoService.Commit();
                _vm.MarkClean();
                CoreState.Services.ShowInfo(R._("Map style data written."));
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.Error("MapStyleEditorView.Write_Click failed: {0}", ex.Message);
            }
        }

        /// <summary>
        /// Write the in-memory 16-color palette back to ROM at the resolved
        /// PaletteAddress slice. Wraps the VM write in an undo scope so the
        /// 32-byte mutation is undoable atomically. If the VM refuses the
        /// write (no ROM, unresolved address, out-of-bounds), the undo scope
        /// is rolled back and no success message is shown
        /// (Copilot v2 non-blocking guidance).
        /// </summary>
        void PaletteWrite_Click(object? sender, RoutedEventArgs e)
        {
            // #1148: the map palette is a raw source-tree asset in decomp mode.
            if (DecompMapAssetGuard.BlockIfDecomp(R._("map palette")))
                return;

            if (_vm.PaletteAddress == 0)
            {
                CoreState.Services.ShowError(R._("Palette address not resolved -- select a map style first."));
                return;
            }

            _undoService.Begin("Edit Map Palette");
            try
            {
                bool ok = _vm.WritePalette();
                if (!ok)
                {
                    _undoService.Rollback();
                    CoreState.Services.ShowError(R._("Palette write refused (invalid address or out of bounds)."));
                    return;
                }
                _undoService.Commit();
                _vm.MarkClean();
                // Refresh the cached palette block so the chip preview
                // immediately reflects the just-written values (#670).
                _vm.RefreshCachedPaletteBytes();
                RefreshChipPreview();
                CoreState.Services.ShowInfo(R._("Palette written."));
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.Error("MapStyleEditorView.PaletteWrite_Click failed: {0}", ex.Message);
            }
        }

        /// <summary>
        /// Repaint the swatch Rectangle for the given color row from the
        /// VM's current RGB triplet. The 5-5-5 channel value is scaled to
        /// the 0..255 range via `(v &lt;&lt; 3) | (v &gt;&gt; 2)` (a common
        /// GBA-to-PC color expansion that maps 0x1F to 0xFF exactly).
        /// </summary>
        void UpdateSwatch(int row)
        {
            var rect = this.FindControl<Rectangle>($"Color{row}_Swatch");
            if (rect == null) return;
            byte r = Expand5To8(_vm.GetColorR(row));
            byte g = Expand5To8(_vm.GetColorG(row));
            byte b = Expand5To8(_vm.GetColorB(row));
            rect.Fill = new SolidColorBrush(Color.FromRgb(r, g, b));
        }

        static byte Expand5To8(ushort v5)
        {
            ushort v = (ushort)(v5 & 0x1F);
            return (byte)((v << 3) | (v >> 2));
        }

        // -----------------------------------------------------------------
        // Chipset Tab handlers (#671).
        // -----------------------------------------------------------------

        /// <summary>
        /// Populate the terrain combo from the cached <see cref="TextSourceListCore.MakeMapTerrainNameList"/>
        /// entries. Handles both the multibyte path (4-byte pointer per
        /// entry — dereference + getString) and the FE7U/FE8U 2-byte text-id
        /// path (read u16 + FETextDecode.Direct). Selects the currently-loaded
        /// terrain byte; clamps to 0 if the list is shorter than the byte.
        ///
        /// <para>The Core helper returns <c>AddrResult</c>s with the entry
        /// address but an empty <c>name</c> (it does not deref the per-entry
        /// text). This method enriches each row with the localized terrain
        /// name so the combo shows real labels rather than just hex prefixes
        /// (Copilot bot v2 inline review on PR #691).</para>
        /// </summary>
        void PopulateTerrainCombo()
        {
            if (ConfigTerrainCombo == null) return;

            var rom = CoreState.ROM;
            var items = new List<string>();
            if (rom != null)
            {
                var entries = TextSourceListCore.MakeMapTerrainNameList(rom);
                bool multibyte = rom.RomInfo?.is_multibyte ?? false;
                for (int i = 0; i < entries.Count; i++)
                {
                    string label;
                    try
                    {
                        if (multibyte)
                        {
                            // 4-byte pointer entry: deref to a C string.
                            uint addr = entries[i].addr;
                            if (addr + 4 <= (uint)rom.Data.Length)
                            {
                                uint strPtr = rom.p32(addr);
                                label = U.isSafetyOffset(strPtr, rom)
                                    ? rom.getString(strPtr)
                                    : string.Empty;
                            }
                            else { label = string.Empty; }
                        }
                        else
                        {
                            // 2-byte text-id entry: decode via FETextDecode.
                            uint addr = entries[i].addr;
                            uint textId = (addr + 2 <= (uint)rom.Data.Length) ? rom.u16(addr) : 0u;
                            label = FETextDecode.Direct(textId);
                        }
                    }
                    catch { label = string.Empty; }
                    items.Add($"0x{i:X2} {label}");
                }
            }
            if (items.Count == 0)
            {
                // Empty fallback so the combo isn't unselectable; users
                // can still type terrain values via the NUD path or paste.
                for (int i = 0; i < 256; i++) items.Add($"0x{i:X2}");
            }
            ConfigTerrainCombo.ItemsSource = items;

            int t = _vm.CurrentTerrain;
            if (t < 0 || t >= items.Count) t = 0;
            ConfigTerrainCombo.SelectedIndex = t;
        }

        /// <summary>
        /// Push slot W values + terrain selection from the VM into the
        /// AXAML controls. Called after every successful
        /// <see cref="MapStyleEditorViewModel.TryLoadChipsetTSA"/> (programmatic
        /// load — handlers are suppressed by <c>_vm.IsLoading</c>).
        /// </summary>
        void ReadSlotsFromVM()
        {
            int[] suffixes = { 0, 2, 4, 6 };
            foreach (int s in suffixes)
            {
                ushort w = _vm.GetSlotW(s);
                var (x, y, p, f) = MapStyleEditorViewModel.DecodeTsaWord(w);
                SetNudValue($"Slot{s}_XBox", x);
                SetNudValue($"Slot{s}_YBox", y);
                SetNudValue($"Slot{s}_PALETTEBox", p);
                SetNudValue($"Slot{s}_FLIPBox", f);
                SetNudValue($"Slot{s}_WBox", w);
            }
            if (ConfigTerrainCombo != null && ConfigTerrainCombo.ItemsSource is IEnumerable<string> items)
            {
                int t = _vm.CurrentTerrain;
                int count = 0;
                foreach (var _ in items) count++;
                ConfigTerrainCombo.SelectedIndex = (t >= 0 && t < count) ? t : 0;
            }
        }

        /// <summary>
        /// Wipe the slot/terrain UI when no valid chipset is loaded. Hidden
        /// behind <c>_vm.IsLoading</c> so the handlers don't push the zeros
        /// straight back into the VM.
        /// </summary>
        void ClearChipsetUI()
        {
            int[] suffixes = { 0, 2, 4, 6 };
            foreach (int s in suffixes)
            {
                SetNudValue($"Slot{s}_XBox", 0);
                SetNudValue($"Slot{s}_YBox", 0);
                SetNudValue($"Slot{s}_PALETTEBox", 0);
                SetNudValue($"Slot{s}_FLIPBox", 0);
                SetNudValue($"Slot{s}_WBox", 0);
            }
            if (ConfigTerrainCombo != null) ConfigTerrainCombo.SelectedIndex = -1;
        }

        /// <summary>
        /// Enable/disable the entire Chipset Tab edit surface (20 slot NUDs +
        /// terrain combo + 4 buttons). The Chipset No NumericUpDown stays
        /// enabled so the user can change selection even when the current
        /// chipset failed to load.
        /// </summary>
        void SetChipsetEditingEnabled(bool enabled)
        {
            int[] suffixes = { 0, 2, 4, 6 };
            foreach (int s in suffixes)
            {
                SetCtrlEnabled($"Slot{s}_XBox", enabled);
                SetCtrlEnabled($"Slot{s}_YBox", enabled);
                SetCtrlEnabled($"Slot{s}_PALETTEBox", enabled);
                SetCtrlEnabled($"Slot{s}_FLIPBox", enabled);
                SetCtrlEnabled($"Slot{s}_WBox", enabled);
            }
            if (ConfigTerrainCombo != null) ConfigTerrainCombo.IsEnabled = enabled;
            if (CopyTileButton != null) CopyTileButton.IsEnabled = enabled;
            if (CopyTypeButton != null) CopyTypeButton.IsEnabled = enabled;
            if (PasteButton != null) PasteButton.IsEnabled = enabled;
            if (ConfigWriteButton != null) ConfigWriteButton.IsEnabled = enabled;
        }

        void SetNudValue(string name, int value)
        {
            var box = this.FindControl<NumericUpDown>(name);
            if (box != null) box.Value = value;
        }

        void SetCtrlEnabled(string name, bool enabled)
        {
            var ctrl = this.FindControl<Control>(name);
            if (ctrl != null) ctrl.IsEnabled = enabled;
        }

        void OnChipsetNoChanged()
        {
            if (_vm.IsLoading) return;
            if (ChipsetNoInput == null) return;
            int newNo = (int)(ChipsetNoInput.Value ?? 0m);
            try
            {
                _vm.IsLoading = true;
                bool ok = _vm.TryLoadChipsetTSA(newNo);
                if (ok) ReadSlotsFromVM();
                else ClearChipsetUI();
                SetChipsetEditingEnabled(_vm.CanEditChipsetConfig && ok);
            }
            finally { _vm.IsLoading = false; }
        }

        void OnTerrainChanged()
        {
            if (_vm.IsLoading) return;
            if (ConfigTerrainCombo == null) return;
            int idx = ConfigTerrainCombo.SelectedIndex;
            if (idx < 0) return;
            _vm.CurrentTerrain = idx;
        }

        void OnSlotSplitChanged(int logicalIndex, int suffix)
        {
            if (_vm.IsLoading) return;
            var xBox = this.FindControl<NumericUpDown>($"Slot{suffix}_XBox");
            var yBox = this.FindControl<NumericUpDown>($"Slot{suffix}_YBox");
            var palBox = this.FindControl<NumericUpDown>($"Slot{suffix}_PALETTEBox");
            var flipBox = this.FindControl<NumericUpDown>($"Slot{suffix}_FLIPBox");
            var wBox = this.FindControl<NumericUpDown>($"Slot{suffix}_WBox");
            int x = (int)(xBox?.Value ?? 0m);
            int y = (int)(yBox?.Value ?? 0m);
            int p = (int)(palBox?.Value ?? 0m);
            int f = (int)(flipBox?.Value ?? 0m);
            _vm.SetSlotSplitByLogicalIndex(logicalIndex, x, y, p, f);
            try
            {
                _vm.IsLoading = true;
                ushort w = _vm.GetSlotW(suffix);
                if (wBox != null) wBox.Value = w;
            }
            finally { _vm.IsLoading = false; }
        }

        void OnSlotRawWChanged(int logicalIndex, int suffix)
        {
            if (_vm.IsLoading) return;
            var wBox = this.FindControl<NumericUpDown>($"Slot{suffix}_WBox");
            ushort w = (ushort)((int)(wBox?.Value ?? 0m) & 0xFFFF);
            _vm.SetSlotWByLogicalIndex(logicalIndex, w);
            try
            {
                _vm.IsLoading = true;
                var (x, y, p, f) = MapStyleEditorViewModel.DecodeTsaWord(w);
                SetNudValue($"Slot{suffix}_XBox", x);
                SetNudValue($"Slot{suffix}_YBox", y);
                SetNudValue($"Slot{suffix}_PALETTEBox", p);
                SetNudValue($"Slot{suffix}_FLIPBox", f);
            }
            finally { _vm.IsLoading = false; }
        }

        void CopyTile_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.CanEditChipsetConfig) return;
            _vm.CopyChipset();
            CoreState.Services.ShowInfo(R._("Chipset tile data copied."));
        }

        void CopyType_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.CanEditChipsetConfig) return;
            _vm.CopyTerrain();
            CoreState.Services.ShowInfo(R._("Terrain type copied."));
        }

        /// <summary>
        /// WF-parity Paste behavior: apply staged clipboard values then
        /// immediately invoke ConfigWrite. <see cref="MapStyleEditorViewModel.Paste"/>
        /// returns false when the clipboard is empty — the auto-write is
        /// skipped in that case so we don't push the current state back to
        /// ROM unchanged.
        /// </summary>
        void Paste_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.CanEditChipsetConfig) return;
            bool applied;
            try
            {
                _vm.IsLoading = true;
                applied = _vm.Paste();
                if (applied) ReadSlotsFromVM();
            }
            finally { _vm.IsLoading = false; }
            if (!applied)
            {
                CoreState.Services.ShowError(R._("Nothing to paste — copy a chipset or terrain first."));
                return;
            }
            ConfigWrite_Click(sender, e);
        }

        void ConfigWrite_Click(object? sender, RoutedEventArgs e)
        {
            // #1148: the chipset config (TSA/terrain) is a raw source-tree asset in decomp mode.
            if (DecompMapAssetGuard.BlockIfDecomp(R._("map chipset config")))
                return;

            if (!_vm.CanEditChipsetConfig)
            {
                CoreState.Services.ShowError(R._("CONFIG PLIST not resolved — select a map style first."));
                return;
            }
            _undoService.Begin("Edit Chipset Config");
            try
            {
                bool ok = _vm.WriteChipsetConfig(out string err);
                if (!ok)
                {
                    _undoService.Rollback();
                    CoreState.Services.ShowError(R._("Config write refused ({0}).", err));
                    return;
                }
                _undoService.Commit();
                _vm.MarkClean();
                ChipsetConfigAddressLabel.Text = $"0x{_vm.ChipsetConfigAddress:X08}";
                RefreshChipPreview();
                CoreState.Services.ShowInfo(R._("Chipset config written."));
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.Error("MapStyleEditorView.ConfigWrite_Click failed: {0}", ex.Message);
            }
        }

        /// <summary>
        /// Window-level Alt+T / Alt+C / Alt+V hotkey dispatch (mirrors WF
        /// <c>MapStyleEditorForm_KeyDown</c>). Only fires when the Chipset
        /// edit surface is enabled so background style switches don't
        /// trigger copy/paste mid-load.
        /// </summary>
        void OnChipsetHotKey(object? sender, KeyEventArgs e)
        {
            if (!_vm.CanEditChipsetConfig) return;
            if ((e.KeyModifiers & KeyModifiers.Alt) == 0) return;
            switch (e.Key)
            {
                case Key.T: CopyTile_Click(sender, new RoutedEventArgs()); e.Handled = true; break;
                case Key.C: CopyType_Click(sender, new RoutedEventArgs()); e.Handled = true; break;
                case Key.V: Paste_Click(sender, new RoutedEventArgs()); e.Handled = true; break;
            }
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
        public ViewModelBase? DataViewModel => _vm;

        static uint ParseHexText(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return 0;
            text = text.Trim();
            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) text = text[2..];
            return uint.TryParse(text, System.Globalization.NumberStyles.HexNumber, null, out uint v) ? v : 0;
        }

        // -----------------------------------------------------------------
        // #672 Slice A: Palette Export / Import / Clipboard / OBJ Export /
        // Undo handlers. Redo + OBJ Import + MapChip Export/Import deferred
        // to follow-up issue #692.
        // -----------------------------------------------------------------

        /// <summary>
        /// Pack the 16 in-memory RGB triplets currently displayed in the
        /// palette NUDs into 32 BGR555 LE bytes (the on-disk GBA palette
        /// format). Returns a fresh 32-byte array; safe to round-trip
        /// through <see cref="PaletteFormatConverter.ImportFromFormat"/>.
        /// </summary>
        internal byte[] PackPaletteToBytes()
        {
            byte[] bytes = new byte[32];
            for (int i = 0; i < 16; i++)
            {
                ushort r = (ushort)(_vm.GetColorR(i + 1) & 0x1F);
                ushort g = (ushort)(_vm.GetColorG(i + 1) & 0x1F);
                ushort b = (ushort)(_vm.GetColorB(i + 1) & 0x1F);
                ushort packed = (ushort)(r | (g << 5) | (b << 10));
                bytes[i * 2] = (byte)(packed & 0xFF);
                bytes[i * 2 + 1] = (byte)((packed >> 8) & 0xFF);
            }
            return bytes;
        }

        /// <summary>
        /// Unpack 32 BGR555 LE bytes into the 48 RGB NumericUpDowns + sync
        /// the in-memory VM channels via SetColorR/G/B. Caller is responsible
        /// for guarding <c>_vm.IsLoading</c> when invoking from a programmatic
        /// path (e.g., after a confirmed import).
        /// </summary>
        void UnpackPaletteBytesIntoUI(byte[] palette32)
        {
            for (int i = 0; i < 16; i++)
            {
                ushort packed = (ushort)(palette32[i * 2] | (palette32[i * 2 + 1] << 8));
                ushort r = (ushort)(packed & 0x1F);
                ushort g = (ushort)((packed >> 5) & 0x1F);
                ushort b = (ushort)((packed >> 10) & 0x1F);
                _vm.SetColorR(i + 1, r);
                _vm.SetColorG(i + 1, g);
                _vm.SetColorB(i + 1, b);

                var rBox = this.FindControl<NumericUpDown>($"Color{i + 1}_RBox");
                var gBox = this.FindControl<NumericUpDown>($"Color{i + 1}_GBox");
                var bBox = this.FindControl<NumericUpDown>($"Color{i + 1}_BBox");
                if (rBox != null) rBox.Value = r;
                if (gBox != null) gBox.Value = g;
                if (bBox != null) bBox.Value = b;
                UpdateSwatch(i + 1);
            }
        }

        /// <summary>
        /// Normalize raw palette import bytes per v2 Copilot review item 1:
        /// reject inputs shorter than 32 bytes; truncate to the first 32 for
        /// larger inputs (e.g. ACT files are 768 bytes for 256 colors).
        /// Returns null when the input is too short. Internal so tests can
        /// call it without driving the dialog path.
        /// </summary>
        internal static byte[]? NormalizeImportedPalette(byte[]? bytes)
        {
            if (bytes == null || bytes.Length < 32) return null;
            if (bytes.Length == 32) return bytes;
            byte[] trimmed = new byte[32];
            Buffer.BlockCopy(bytes, 0, trimmed, 0, 32);
            return trimmed;
        }

        async void PaletteExport_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                string? path = await FileDialogHelper.SavePaletteFile(this, "map_style_palette.pal");
                if (string.IsNullOrEmpty(path)) return;

                byte[] gbaBytes = PackPaletteToBytes();
                PaletteFormat fmt = PaletteFormatConverter.FormatFromExtension(System.IO.Path.GetExtension(path));
                byte[] output = PaletteFormatConverter.ExportToFormat(gbaBytes, fmt);
                File.WriteAllBytes(path, output);
                CoreState.Services.ShowInfo(R._("Palette exported to {0}.", System.IO.Path.GetFileName(path)));
            }
            catch (Exception ex)
            {
                Log.Error("MapStyleEditorView.PaletteExport_Click failed: {0}", ex.Message);
                CoreState.Services.ShowError(R._("Export palette failed: {0}", ex.Message));
            }
        }

        async void PaletteImport_Click(object? sender, RoutedEventArgs e)
        {
            // #1148: importing a map palette mutates the build-preview ROM — blocked in decomp mode.
            if (DecompMapAssetGuard.BlockIfDecomp(R._("map palette")))
                return;

            try
            {
                string? path = await FileDialogHelper.OpenPaletteFile(this);
                if (string.IsNullOrEmpty(path)) return;

                byte[] fileData = File.ReadAllBytes(path);
                PaletteFormat fmt = PaletteFormatConverter.DetectFormat(fileData, System.IO.Path.GetExtension(path));
                byte[] palData = (fmt == PaletteFormat.GbaRaw) ? fileData : PaletteFormatConverter.ImportFromFormat(fileData, fmt);

                byte[]? staged = NormalizeImportedPalette(palData);
                if (staged == null)
                {
                    CoreState.Services.ShowError(R._("Palette file must contain at least 16 colors (32 bytes)."));
                    return;
                }

                // Per v1 Copilot review item 2 + v2 plan: stage in a local
                // before touching VM / NUD state. Confirm-before-apply so a
                // user can cancel after seeing the source filename.
                string filename = System.IO.Path.GetFileName(path);
                bool confirm = CoreState.Services.ShowYesNo(
                    R._("Import 16 colors from {0} into current palette? You can still cancel before clicking Palette Write.", filename));
                if (!confirm) return;

                _vm.IsLoading = true;
                try
                {
                    UnpackPaletteBytesIntoUI(staged);
                }
                finally { _vm.IsLoading = false; }
                CoreState.Services.ShowInfo(R._("Imported palette from {0}. Click Palette Write to persist.", filename));
            }
            catch (Exception ex)
            {
                Log.Error("MapStyleEditorView.PaletteImport_Click failed: {0}", ex.Message);
                CoreState.Services.ShowError(R._("Import palette failed: {0}", ex.Message));
            }
        }

        /// <summary>
        /// Regex matching a 64-char hex string (16 colors * 4 hex chars).
        /// Used by <see cref="PaletteClipboard_Click"/> to decide whether to
        /// paste an existing clipboard string back into the palette or copy
        /// the current palette out as 64-char hex.
        /// </summary>
        static readonly Regex HexPalette64 = new(@"^[0-9A-Fa-f]{64}$", RegexOptions.Compiled);

        async void PaletteClipboard_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                var topLevel = TopLevel.GetTopLevel(this);
                if (topLevel?.Clipboard == null)
                {
                    CoreState.Services.ShowError(R._("Clipboard not available."));
                    return;
                }

                string? clipText = await topLevel.Clipboard.GetTextAsync();
                string? trimmed = clipText?.Trim();

                if (!string.IsNullOrEmpty(trimmed) && HexPalette64.IsMatch(trimmed))
                {
                    // Paste path: parse 64-char hex into 32 bytes, confirm,
                    // then apply.
                    byte[] staged = new byte[32];
                    for (int i = 0; i < 32; i++)
                    {
                        staged[i] = byte.Parse(trimmed.Substring(i * 2, 2),
                            System.Globalization.NumberStyles.HexNumber);
                    }

                    bool confirm = CoreState.Services.ShowYesNo(
                        R._("Paste 16 colors from clipboard into current palette? You can still cancel before clicking Palette Write."));
                    if (!confirm) return;

                    _vm.IsLoading = true;
                    try { UnpackPaletteBytesIntoUI(staged); }
                    finally { _vm.IsLoading = false; }
                    CoreState.Services.ShowInfo(R._("Palette pasted from clipboard. Click Palette Write to persist."));
                }
                else
                {
                    // Copy path: pack current palette to 64-char uppercase hex.
                    byte[] bytes = PackPaletteToBytes();
                    var sb = new StringBuilder(64);
                    foreach (byte b in bytes) sb.AppendFormat("{0:X2}", b);
                    await topLevel.Clipboard.SetTextAsync(sb.ToString());
                    CoreState.Services.ShowInfo(R._("Palette copied to clipboard."));
                }
            }
            catch (Exception ex)
            {
                Log.Error("MapStyleEditorView.PaletteClipboard_Click failed: {0}", ex.Message);
                CoreState.Services.ShowError(R._("Clipboard operation failed: {0}", ex.Message));
            }
        }

        async void ObjExport_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                if (!_vm.TryRenderObjTileSheet(out byte[] rgba, out int w, out int h))
                {
                    CoreState.Services.ShowError(R._("No OBJ tile sheet available to export."));
                    return;
                }
                string? path = await FileDialogHelper.SaveImageFile(this, $"map_style_obj_{_vm.ConfigNo}.png");
                if (string.IsNullOrEmpty(path)) return;

                var bitmap = IconBitmapBuilder.FromRgba(rgba, w, h);
                if (bitmap == null)
                {
                    CoreState.Services.ShowError(R._("Failed to build bitmap from OBJ tile sheet."));
                    return;
                }
                using (var stream = File.Create(path))
                {
                    bitmap.Save(stream);
                }
                CoreState.Services.ShowInfo(R._("OBJ tile sheet exported to {0}.", System.IO.Path.GetFileName(path)));
            }
            catch (Exception ex)
            {
                Log.Error("MapStyleEditorView.ObjExport_Click failed: {0}", ex.Message);
                CoreState.Services.ShowError(R._("OBJ export failed: {0}", ex.Message));
            }
        }

        /// <summary>
        /// Per-editor Undo (#672 Slice A): runs <see cref="Undo.RunUndo"/> on
        /// the global <see cref="CoreState.Undo"/> buffer. Guards on Position
        /// > 0 per v2 Copilot review item 2 so a no-op undo doesn't falsely
        /// claim success. After a successful undo, the entry is reloaded so
        /// the palette/chipset/preview surfaces reflect the rolled-back ROM
        /// bytes.
        /// </summary>
        void Undo_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                if (CoreState.Undo == null || CoreState.Undo.Postion <= 0)
                {
                    CoreState.Services.ShowInfo(R._("Nothing to undo."));
                    return;
                }
                CoreState.Undo.RunUndo();
                // Reload the current entry so palette / chipset / preview
                // pick up the rolled-back ROM bytes.
                if (_vm.CurrentAddr != 0)
                {
                    _vm.IsLoading = true;
                    try
                    {
                        _vm.LoadEntry(_vm.CurrentAddr);
                        UpdateUI();
                    }
                    finally { _vm.IsLoading = false; }
                    RefreshChipPreview();
                }
                CoreState.Services.ShowInfo(R._("Undo applied."));
            }
            catch (Exception ex)
            {
                Log.Error("MapStyleEditorView.Undo_Click failed: {0}", ex.Message);
                CoreState.Services.ShowError(R._("Undo failed: {0}", ex.Message));
            }
        }

        /// <summary>
        /// Per-editor Redo (#692 partial slice): runs
        /// <see cref="Undo.RunRedo"/> on the global <see cref="CoreState.Undo"/>
        /// buffer. Mirrors <see cref="Undo_Click"/>: guards on
        /// <see cref="Undo.CanRedo"/>, then verifies the redo actually
        /// advanced the cursor (RunRedo's bool surfaces silent
        /// <see cref="Undo.RollbackROM"/> failures that
        /// <see cref="Undo.Rollback(int)"/> would otherwise hide).
        /// After a successful redo, reload the entry so palette /
        /// chipset / preview reflect the rolled-forward ROM bytes.
        /// </summary>
        void Redo_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                if (CoreState.Undo == null || !CoreState.Undo.CanRedo)
                {
                    CoreState.Services.ShowInfo(R._("Nothing to redo."));
                    return;
                }
                if (!CoreState.Undo.RunRedo())
                {
                    CoreState.Services.ShowError(R._("Redo failed."));
                    return;
                }
                // Reload the current entry so palette / chipset / preview
                // pick up the rolled-forward ROM bytes.
                if (_vm.CurrentAddr != 0)
                {
                    _vm.IsLoading = true;
                    try
                    {
                        _vm.LoadEntry(_vm.CurrentAddr);
                        UpdateUI();
                    }
                    finally { _vm.IsLoading = false; }
                    RefreshChipPreview();
                }
                CoreState.Services.ShowInfo(R._("Redo applied."));
            }
            catch (Exception ex)
            {
                Log.Error("MapStyleEditorView.Redo_Click failed: {0}", ex.Message);
                CoreState.Services.ShowError(R._("Redo failed: {0}", ex.Message));
            }
        }

        /// <summary>
        /// Per-editor MapChip Export (#692 partial slice): writes the
        /// VM's cached decompressed CONFIG buffer to a .MAPCHIP_CONFIG
        /// file. Mirrors WF parity — raw ConfigUZ bytes, no magic header
        /// (the WF importer reads raw bytes with only a 9216-byte
        /// minimum check, so adding a header would break import parity).
        /// Uses <see cref="FileDialogHelper.SaveFile"/> for picker parity
        /// with the rest of the Avalonia layer.
        /// </summary>
        async void MapChipExport_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                byte[]? configClone = _vm.GetCachedConfigClone();
                if (configClone == null || configClone.Length == 0)
                {
                    CoreState.Services.ShowError(
                        R._("No chipset config loaded — select a Map Style entry first."));
                    return;
                }
                string? path = await FileDialogHelper.SaveFile(
                    this,
                    "Export Map Chip Config",
                    "Map Chip Config",
                    "*.MAPCHIP_CONFIG",
                    "mapchip.MAPCHIP_CONFIG");
                // Treat null / empty / whitespace as cancel — FileDialogHelper.SaveFile
                // can return an empty string in some flows, and File.WriteAllBytes
                // would throw on it. Matches the guard pattern used by other
                // Save-path callers (e.g. ToolTranslateROMView). Copilot bot
                // inline review on PR #706.
                if (string.IsNullOrWhiteSpace(path)) return;

                File.WriteAllBytes(path, configClone);
                CoreState.Services.ShowInfo(
                    R._("Exported {0} bytes to {1}.", configClone.Length, System.IO.Path.GetFileName(path)));
            }
            catch (Exception ex)
            {
                Log.Error("MapStyleEditorView.MapChipExport_Click failed: {0}", ex.Message);
                CoreState.Services.ShowError(R._("Map Chip export failed: {0}", ex.Message));
            }
        }

        /// <summary>
        /// Convert indexed pixel data + an RGBA palette into a flat RGBA
        /// buffer of exactly <paramref name="w"/> × <paramref name="h"/>
        /// pixels. Returns null + a human-readable <paramref name="error"/>
        /// when any of the four #710 review-#4 guards fire:
        ///   - <paramref name="indexData"/> is null or shorter than w*h
        ///   - <paramref name="palRgba"/> is null or too short to hold a
        ///     single 4-byte color
        ///   - a pixel references a palette entry whose offset would
        ///     run past the end of <paramref name="palRgba"/>.
        ///
        /// <para>Extracted out of <c>ObjImport_Click</c> so the indexed-
        /// image decode guards can be exercised by unit tests without
        /// driving the full file dialog path (Copilot CLI re-review on
        /// PR #716).</para>
        /// </summary>
        internal static byte[]? ConvertIndexedToRgba(byte[]? indexData, byte[]? palRgba, int w, int h, out string error)
        {
            error = "";

            // Negative dimensions are nonsensical and would make every
            // downstream check unreliable (Copilot bot v2 inline review
            // on PR #716).
            if (w < 0 || h < 0)
            {
                error = $"negative image dimensions ({w}x{h}).";
                return null;
            }

            // Use long arithmetic for the pixel-count + RGBA-buffer size
            // computations so pathological inputs (e.g. width=256 × height=8M)
            // can't wrap past int.MaxValue and turn the bounds check into a
            // false negative (Copilot bot v2 inline review item 2 + 3 on PR
            // #716). The actual byte[] allocation requires int — bound the
            // RGBA size to int.MaxValue and fail early when over.
            long expectedLong = (long)w * (long)h;
            long rgbaSizeLong = expectedLong * 4L;
            if (rgbaSizeLong > int.MaxValue)
            {
                error = $"image too large to decode (RGBA size {rgbaSizeLong} bytes > int.MaxValue).";
                return null;
            }
            int expected = (int)expectedLong;

            if (indexData == null || indexData.LongLength < expectedLong)
            {
                error = $"indexed pixel data is shorter than expected ({indexData?.LongLength ?? 0} < {expectedLong}).";
                return null;
            }
            if (palRgba == null || palRgba.Length < 4)
            {
                error = "indexed image has no usable palette.";
                return null;
            }
            byte[] rgba = new byte[(int)rgbaSizeLong];
            for (int i = 0; i < expected; i++)
            {
                int palIdx = indexData[i];
                int palOff = palIdx * 4;
                if (palOff + 3 >= palRgba.Length)
                {
                    error = $"indexed pixel {i} uses palette entry {palIdx} but palette has only {palRgba.Length / 4} colors.";
                    return null;
                }
                int dstOff = i * 4;
                rgba[dstOff + 0] = palRgba[palOff + 0];
                rgba[dstOff + 1] = palRgba[palOff + 1];
                rgba[dstOff + 2] = palRgba[palOff + 2];
                rgba[dstOff + 3] = palRgba[palOff + 3];
            }
            return rgba;
        }

        // -----------------------------------------------------------------
        // #710 — OBJ Image Import (ImageOnly slice): pick image, validate
        // the WF dimension contract (256 wide × ≥128 tall × multiple-of-8
        // height), remap against the existing OBJ palette (no palette
        // change), 4bpp-encode + LZ77 + write via the OBJECT PLIST path.
        // FE7 obj2-bearing styles (#976) are supported: the encoded sheet
        // is split in half and written to both the primary and obj2 OBJECT
        // PLIST slots under the view's single ambient undo scope.
        // -----------------------------------------------------------------
        async void ObjImport_Click(object? sender, RoutedEventArgs e)
        {
            // #1148: importing the tileset OBJ mutates the build-preview ROM — blocked in decomp mode.
            if (DecompMapAssetGuard.BlockIfDecomp(R._("map tileset (OBJ)")))
                return;

            try
            {
                if (!_vm.CanImportObj)
                {
                    CoreState.Services.ShowError(R._("OBJ image import requires a Map Style entry with a valid OBJ PLIST."));
                    return;
                }

                string? path = await FileDialogHelper.OpenImageFile(this);
                if (string.IsNullOrEmpty(path)) return;

                // Decode the source image through the shared SkiaSharp
                // service used by every other Avalonia importer. We pull
                // RGBA bytes (converting indexed → RGBA when needed) and
                // hand them to the VM, which owns the remap-against-existing-
                // palette step. This keeps the View thin (no Core-level
                // import knowledge) and routes ALL color drift through the
                // VM's remap path so behavior matches the Copilot CLI v1
                // review item 1 (no quantize, no palette mutation).
                byte[] rgba;
                int w, h;
                try
                {
                    var imgService = CoreState.ImageService;
                    if (imgService == null)
                    {
                        CoreState.Services.ShowError(R._("Image service not initialized."));
                        return;
                    }
                    using var image = imgService.LoadImage(path);
                    w = image.Width;
                    h = image.Height;
                    if (image.IsIndexed)
                    {
                        // Copilot bot #4 on PR #716: fail early when the
                        // indexed-image source can't fully cover w*h pixels
                        // or its palette is too short to dereference any
                        // valid index. Without this, an incomplete decode
                        // silently produces large transparent/black regions
                        // and the user gets a "successful" import full of
                        // garbage. Logic extracted into ConvertIndexedToRgba
                        // so it can be unit-tested without driving the full
                        // dialog path.
                        byte[] indexData = image.GetPixelData();
                        byte[] palRgba = image.GetPaletteRGBA();
                        byte[]? decoded = ConvertIndexedToRgba(indexData, palRgba, w, h, out string decErr);
                        if (decoded == null)
                        {
                            CoreState.Services.ShowError(R._("Image decode error: {0}", decErr));
                            return;
                        }
                        rgba = decoded;
                    }
                    else
                    {
                        rgba = image.GetPixelData();
                    }
                }
                catch (Exception ex)
                {
                    Log.Error("MapStyleEditorView.ObjImport_Click decode failed: {0}", ex.Message);
                    CoreState.Services.ShowError(R._("Image decode error: {0}", ex.Message));
                    return;
                }

                _undoService.Begin("Import OBJ Image");
                try
                {
                    if (!_vm.TryImportObjImage(rgba, w, h, out string err))
                    {
                        _undoService.Rollback();
                        CoreState.Services.ShowError(R._("OBJ image import failed: {0}", err));
                        return;
                    }
                    _undoService.Commit();
                    _vm.MarkClean();
                }
                catch (Exception ex)
                {
                    _undoService.Rollback();
                    Log.Error("MapStyleEditorView.ObjImport_Click write failed: {0}", ex.Message);
                    CoreState.Services.ShowError(R._("OBJ image import error: {0}", ex.Message));
                    return;
                }

                // -- Post-commit UI refresh (no rollback path; ROM bytes
                // are already persisted). Failures here only affect the
                // visible state, not durability — log + warn but don't roll
                // back. Mirrors the MapChipImport_Click pattern.
                try
                {
                    ObjAddressLabel.Text = $"0x{_vm.ObjAddress:X08}";
                    ObjPtrBox.Text = $"0x{_vm.ObjPointer:X08}";
                    ObjAddress2Label.Text = _vm.ObjAddress2 != 0 ? $"0x{_vm.ObjAddress2:X08}" : "(none)";
                    RefreshChipPreview();
                    CoreState.Services.ShowInfo(R._("OBJ image imported ({0}x{1}).", w, h));
                }
                catch (Exception ex)
                {
                    Log.Error("MapStyleEditorView.ObjImport_Click UI refresh failed: {0}", ex.Message);
                    CoreState.Services.ShowError(
                        R._("OBJ image imported but UI refresh failed: {0}. Re-select the Map Style entry to refresh the view.", ex.Message));
                }
            }
            catch (Exception ex)
            {
                Log.Error("MapStyleEditorView.ObjImport_Click failed: {0}", ex.Message);
                CoreState.Services.ShowError(R._("OBJ image import failed: {0}", ex.Message));
            }
        }

        // -----------------------------------------------------------------
        // #704 — MapChip Import: read raw .MAPCHIP_CONFIG bytes (no header,
        // ≥ 9216 bytes per WF parity), persist via the CONFIG PLIST write
        // path. Palette bits in TSA words are preserved verbatim because
        // the buffer is written byte-for-byte (the VM's TryWriteConfigBuffer
        // never decodes TSA words during the write).
        // -----------------------------------------------------------------
        async void MapChipImport_Click(object? sender, RoutedEventArgs e)
        {
            // #1148: importing the chipset config (TSA) mutates the build-preview ROM — blocked in decomp mode.
            if (DecompMapAssetGuard.BlockIfDecomp(R._("map chipset config")))
                return;

            try
            {
                var topLevel = TopLevel.GetTopLevel(this);
                if (topLevel?.StorageProvider == null)
                {
                    CoreState.Services.ShowError(R._("Storage provider not available."));
                    return;
                }
                var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = "Import Map Chip Config",
                    AllowMultiple = false,
                    FileTypeFilter = new[]
                    {
                        new FilePickerFileType("Map Chip Config") { Patterns = new[] { "*.MAPCHIP_CONFIG", "*.mapchip_config" } },
                        // Copilot bot v1 inline review: use "*" (not "*.*") so the
                        // filter truly matches every file on every platform —
                        // `*.*` skips extension-less filenames on macOS/Linux,
                        // and mirrors `FileDialogHelper.MakeAllFileType` parity.
                        new FilePickerFileType("All files") { Patterns = new[] { "*" } },
                    },
                });
                if (files == null || files.Count == 0) return;

                byte[] data;
                try
                {
                    await using var s = await files[0].OpenReadAsync();
                    using var ms = new MemoryStream();
                    await s.CopyToAsync(ms);
                    data = ms.ToArray();
                }
                catch (Exception ex)
                {
                    Log.Error("MapStyleEditorView.MapChipImport_Click read failed: {0}", ex.Message);
                    CoreState.Services.ShowError(R._("Failed to read file: {0}", ex.Message));
                    return;
                }

                // Copilot bot v2 inline review: split write/commit from
                // UI-refresh so an exception during post-commit UI work
                // can't trigger a no-op Rollback on an already-committed
                // undo group (which would lie to the user about "import
                // failed" while ROM bytes are already persisted).
                _undoService.Begin("Import Map Chip Config");
                try
                {
                    if (!_vm.TryWriteConfigBuffer(data, out string err))
                    {
                        _undoService.Rollback();
                        CoreState.Services.ShowError(R._("Import failed: {0}", err));
                        return;
                    }
                    _undoService.Commit();
                    _vm.MarkClean();
                }
                catch (Exception ex)
                {
                    _undoService.Rollback();
                    Log.Error("MapStyleEditorView.MapChipImport_Click write failed: {0}", ex.Message);
                    CoreState.Services.ShowError(R._("Import error: {0}", ex.Message));
                    return;
                }

                // -- Post-commit UI refresh (no rollback path; ROM is
                // already persisted at this point). Failures here only
                // affect the visible state, not durability — log + warn
                // but don't roll back.
                try
                {
                    ChipsetConfigAddressLabel.Text = $"0x{_vm.ChipsetConfigAddress:X08}";
                    _vm.IsLoading = true;
                    try
                    {
                        _vm.CurrentChipsetNo = 0;
                        bool chipsetLoaded = _vm.CanEditChipsetConfig && _vm.TryLoadChipsetTSA(0);
                        if (ChipsetNoInput != null)
                            ChipsetNoInput.Value = chipsetLoaded ? 0m : (decimal?)null;
                        PopulateTerrainCombo();
                        if (chipsetLoaded) ReadSlotsFromVM();
                        else ClearChipsetUI();
                        SetChipsetEditingEnabled(chipsetLoaded);
                    }
                    finally { _vm.IsLoading = false; }
                    RefreshChipPreview();
                    CoreState.Services.ShowInfo(R._("Imported {0} bytes of map chip config.", data.Length));
                }
                catch (Exception ex)
                {
                    // Import succeeded but UI refresh failed — tell the user
                    // their data is safe and ask them to reload.
                    Log.Error("MapStyleEditorView.MapChipImport_Click UI refresh failed: {0}", ex.Message);
                    CoreState.Services.ShowError(
                        R._("Import succeeded ({0} bytes) but UI refresh failed: {1}. Re-select the Map Style entry to refresh the view.", data.Length, ex.Message));
                }
            }
            catch (Exception ex)
            {
                Log.Error("MapStyleEditorView.MapChipImport_Click failed: {0}", ex.Message);
                CoreState.Services.ShowError(R._("Map chip import failed: {0}", ex.Message));
            }
        }
    }
}
