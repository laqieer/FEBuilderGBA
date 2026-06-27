// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using global::Avalonia.Media.Imaging;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    /// <summary>
    /// Avalonia counterpart of WinForms `SkillConfigFE8NSkillForm` (FE8N v1
    /// / yugudora skill patch). Phase 1/2/4 gap-sweep fix (#390) raises the
    /// AXAML control surface from 33 to MEDIUM-verdict density (>= 85) and
    /// wires W0 + W2 + B4..B15 write under a single UndoService scope.
    ///
    /// Image/Animation Import/Export buttons are disabled by design (#1008) —
    /// WF SkillConfigFE8NSkillForm is render-only (no animation pointer, no
    /// icon I/O), and this variant's icon address lacks the 0x100 page offset
    /// v2/v3 use. Their click handlers are kept as no-ops (disabled buttons
    /// never fire them; keeping them satisfies the wired-or-inert audit).
    ///
    /// The Unit sub-list tab is an editable B16..B31 ext-byte editor (#790) —
    /// FE8N v1's N00..N03 sub-tabs all union onto the SAME 16 row bytes, so the
    /// one editable tab is faithful + complete. The Class/Item/Other tabs remain
    /// informational placeholders (same 16 bytes; KnownGap #374).
    /// </summary>
    public partial class SkillConfigFE8NSkillView : TranslatedWindow, IEditorView, IDataVerifiableView
    {
        readonly SkillConfigFE8NSkillViewModel _vm = new();
        readonly UndoService _undoService = new();
        bool _suppressPointerChange;
        bool _suppressChangeTypeChange;
        Bitmap? _currentIconBitmap;

        public string ViewTitle => "Skill Configuration (FE8N)";
        public bool IsLoaded => _vm.IsLoaded;
        public ViewModelBase? DataViewModel => _vm;

        public SkillConfigFE8NSkillView()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            Opened += (_, _) => LoadList();
            Closed += (_, _) => DisposeBitmap(ref _currentIconBitmap);
        }

        static void DisposeBitmap(ref Bitmap? bmp)
        {
            if (bmp == null) return;
            try { bmp.Dispose(); } catch { /* swallow */ }
            bmp = null;
        }

        void LoadList()
        {
            _vm.IsLoading = true;
            try
            {
                var items = _vm.LoadList();

                // Populate the FilterComboBox with one entry per discovered
                // FE8N page (WF parity: each pointer is the address of a
                // u32 GBA pointer slot).
                _suppressPointerChange = true;
                try
                {
                    FilterComboBox.Items.Clear();
                    for (int i = 0; i < _vm.IconPointers.Length; i++)
                    {
                        // Show the dereferenced page base for readability,
                        // matching WF's `U.ToHexString(p)` format.
                        ROM rom = CoreState.ROM;
                        uint pageBase = 0;
                        try
                        {
                            if (rom != null && U.isSafetyOffset(_vm.IconPointers[i] + 3, rom))
                            {
                                uint gba = rom.u32(_vm.IconPointers[i]);
                                if (U.isSafetyPointer(gba)) pageBase = U.toOffset(gba);
                            }
                        }
                        catch { /* ignore */ }
                        var cbi = new ComboBoxItem
                        {
                            Content = new TextBlock { Text = $"0x{pageBase:X08}" }
                        };
                        FilterComboBox.Items.Add(cbi);
                    }
                    if (_vm.IconPointers.Length > 0)
                    {
                        FilterComboBox.SelectedIndex = _vm.SelectedPointerIndex;
                    }
                }
                finally { _suppressPointerChange = false; }

                // CLEAR the unit-name previews BEFORE populating the list (#795
                // review fix). SetItemsWithIcons() → SelectFirst() → OnSelected()
                // → UpdateUI() repopulates the 16 previews for the freshly
                // selected first row, so the clear MUST run first — otherwise it
                // would clobber the just-loaded row's names and the first row
                // would show blank previews. With an empty list no row is
                // selected, so the previews stay cleared (#793 refinement:
                // refresh-on-row-load PLUS clear-when-no-entry).
                ClearExtNames();

                // Wire the AddressList using the FE8N v1 icon loader (W0-driven).
                EntryList.SetItemsWithIcons(items, i => FE8NVer1IconLoader(items, i));

                // #743: route through the unified EditorTopBarWithInputs (TopBar).
                TopBar.ReadStartAddress = _vm.ReadStartAddress;
                TopBar.ReadCount = (int)_vm.ReadCount;
                BlockSizeBox.Value = _vm.BlockSize;

                _suppressChangeTypeChange = true;
                try
                {
                    ChangeTypeComboBox.SelectedIndex = 0;
                }
                finally { _suppressChangeTypeChange = false; }
            }
            catch (Exception ex)
            {
                Log.ErrorF("SkillConfigFE8NSkillView.LoadList failed: {0}", ex.Message);
            }
            finally { _vm.IsLoading = false; _vm.MarkClean(); }
        }

        static Bitmap? FE8NVer1IconLoader(System.Collections.Generic.List<AddrResult> items, int index)
        {
            if (index < 0 || index >= items.Count) return null;
            try
            {
                ROM rom = CoreState.ROM;
                if (rom == null) return null;
                uint rowAddr = items[index].addr;
                if (!U.isSafetyOffset(rowAddr + 3, rom)) return null;
                // W0 (the row's icon ID) drives the icon address.
                uint w0 = rom.u16(rowAddr + 0);
                using var img = PreviewIconHelper.LoadFE8NVer1SkillIcon(w0);
                return img != null ? ImageConversionHelper.ToAvaloniaBitmap(img) : null;
            }
            catch { return null; }
        }

        // #743: routed event from the unified EditorTopBarWithInputs Reload button.
        void OnTopBarReloadRequested(object? sender, RoutedEventArgs e) => LoadList();

        void FilterComboBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (_suppressPointerChange) return;
            int idx = FilterComboBox.SelectedIndex;
            if (idx < 0) return;
            _vm.IsLoading = true;
            try
            {
                var items = _vm.SelectPointer(idx);
                // CLEAR previews BEFORE repopulating so a switch to an empty page
                // doesn't leave stale unit-name labels from the previous page
                // (#795 review fix). When the new page is non-empty,
                // SetItemsWithIcons() → SelectFirst() → OnSelected() → UpdateUI()
                // repopulates them for the new first row, so the clear (running
                // first) is preserved only when there is no entry to select.
                ClearExtNames();
                EntryList.SetItemsWithIcons(items, i => FE8NVer1IconLoader(items, i));
                // #743: route through the unified EditorTopBarWithInputs (TopBar).
                TopBar.ReadStartAddress = _vm.ReadStartAddress;
                TopBar.ReadCount = (int)_vm.ReadCount;
            }
            catch (Exception ex)
            {
                Log.ErrorF("SkillConfigFE8NSkillView.FilterComboBox_SelectionChanged failed: {0}", ex.Message);
            }
            finally { _vm.IsLoading = false; _vm.MarkClean(); }
        }

        void ChangeTypeComboBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (_suppressChangeTypeChange) return;
            // WF parity: ChangeType selection switches the active sub-tab in
            // the right-hand TabControl. Map combobox index to tab index:
            //   0 (Unit)  -> Unit Skill List   tab (index 1)
            //   1 (Class) -> Class Skill List  tab (index 2)
            //   2 (Item)  -> Item Skill List   tab (index 3)
            //   3 (Other) -> Other Skill List  tab (index 4)
            // Tab 0 (Skill Detail) is the functional editor; the 4 sub-list
            // tabs are KnownGap placeholders (#374) but the navigation
            // affordance still mirrors WF.
            int comboIndex = ChangeTypeComboBox.SelectedIndex;
            if (comboIndex < 0 || comboIndex > 3) return;
            int tabIndex = comboIndex + 1; // shift past Skill Detail tab[0]
            if (MainTabControl != null && tabIndex < MainTabControl.ItemCount)
            {
                MainTabControl.SelectedIndex = tabIndex;
            }
        }

        void OnSelected(uint addr)
        {
            _vm.IsLoading = true;
            try
            {
                _vm.LoadEntry(addr);
                UpdateUI();
            }
            catch (Exception ex)
            {
                Log.ErrorF("SkillConfigFE8NSkillView.OnSelected failed: {0}", ex.Message);
            }
            finally { _vm.IsLoading = false; _vm.MarkClean(); }
        }

        void UpdateUI()
        {
            // Selection-bar widgets.
            AddressBox.Value = _vm.CurrentAddr;
            BlockSizeBox.Value = _vm.BlockSize;
            SelectedAddressLabel.Content = $"0x{_vm.CurrentAddr:X08}";

            // Per-row fields.
            IconIdBox.Value = _vm.IconId;
            TextDetailBox.Value = _vm.TextDetail;
            CondUnit1Box.Value = _vm.CondUnit1;
            CondUnit2Box.Value = _vm.CondUnit2;
            CondUnit3Box.Value = _vm.CondUnit3;
            CondUnit4Box.Value = _vm.CondUnit4;
            CondClass1Box.Value = _vm.CondClass1;
            CondClass2Box.Value = _vm.CondClass2;
            CondClass3Box.Value = _vm.CondClass3;
            CondClass4Box.Value = _vm.CondClass4;
            CondItem1Box.Value = _vm.CondItem1;
            CondItem2Box.Value = _vm.CondItem2;
            CondItem3Box.Value = _vm.CondItem3;
            CondItem4Box.Value = _vm.CondItem4;

            // Text preview from W2.
            try
            {
                string textPreview = _vm.TextDetail != 0
                    ? NameResolver.GetTextById(_vm.TextDetail) ?? ""
                    : "";
                TextDetailTextBox.Text = textPreview;
                // Split-string description: the WF ParseTextToSkillName
                // extracts the part between U+300E (LEFT WHITE CORNER BRACKET
                // 『) and U+300F (RIGHT WHITE CORNER BRACKET 』). NOTE: white
                // corner brackets - NOT 「」 (U+300C / U+300D regular corner
                // brackets) which would not match real FE8N skill texts.
                string splitName = "";
                if (!string.IsNullOrEmpty(textPreview))
                {
                    int s = textPreview.IndexOf('『');
                    int eIdx = textPreview.IndexOf('』', s + 1);
                    if (s >= 0 && eIdx > s)
                        splitName = textPreview.Substring(s + 1, eIdx - s - 1);
                }
                SplitStringTextBox.Text = splitName;
            }
            catch
            {
                TextDetailTextBox.Text = "";
                SplitStringTextBox.Text = "";
            }

            // Icon address label - derive from rom.p32(icon_pointer) + 128 * IconId.
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo != null)
            {
                try
                {
                    uint iconBaseAddr = rom.p32(rom.RomInfo.icon_pointer);
                    uint iconAddr = iconBaseAddr + 128u * _vm.IconId;
                    IconAddrLabel.Content = $"0x{iconAddr:X08}";
                }
                catch { IconAddrLabel.Content = ""; }
            }
            else
            {
                IconAddrLabel.Content = "";
            }

            // Animation pointer (KnownGap #500 - read-only display only).
            AnimationPointerBox.Value = 0;

            // Sub-list tab base addresses. FE8N v1's N00..N03 tabs all union
            // onto the SAME raw bytes B16..B31 of the row (no separate pointer
            // table), so we surface the row's own offset + 16 as the "sub-list
            // base". The Unit tab is now an editable B16..B31 editor (#790);
            // the Class/Item/Other tabs remain informational (same 16 bytes).
            uint subBase = _vm.CurrentAddr + 16u;
            UnitTabBaseAddrLabel.Content = $"Sub-list base: 0x{subBase:X08}";
            UnitTabCountLabel.Content = "Entry count: 16 bytes (B16..B31)";
            ClassTabBaseAddrLabel.Content = $"Sub-list base: 0x{subBase:X08}";
            ClassTabCountLabel.Content = "Entry count: 16 bytes (B16..B31)";
            ItemTabBaseAddrLabel.Content = $"Sub-list base: 0x{subBase:X08}";
            ItemTabCountLabel.Content = "Entry count: 16 bytes (B16..B31)";
            OtherTabBaseAddrLabel.Content = $"Sub-list base: 0x{subBase:X08}";
            OtherTabCountLabel.Content = "Entry count: 16 bytes (B16..B31)";

            // Push the 16 editable ext-bytes B16..B31 into the Unit tab inputs (#790).
            ExtB16Box.Value = _vm.Ext0;
            ExtB17Box.Value = _vm.Ext1;
            ExtB18Box.Value = _vm.Ext2;
            ExtB19Box.Value = _vm.Ext3;
            ExtB20Box.Value = _vm.Ext4;
            ExtB21Box.Value = _vm.Ext5;
            ExtB22Box.Value = _vm.Ext6;
            ExtB23Box.Value = _vm.Ext7;
            ExtB24Box.Value = _vm.Ext8;
            ExtB25Box.Value = _vm.Ext9;
            ExtB26Box.Value = _vm.Ext10;
            ExtB27Box.Value = _vm.Ext11;
            ExtB28Box.Value = _vm.Ext12;
            ExtB29Box.Value = _vm.Ext13;
            ExtB30Box.Value = _vm.Ext14;
            ExtB31Box.Value = _vm.Ext15;

            // Refresh the 16 read-only unit-name previews (#793 — WF N00
            // decoration). Each name is derived from the BOX's CURRENT value
            // (not the VM prop), which is the value we just pushed above. The
            // per-NUD ValueChanged handler keeps them live while the user
            // edits; this initial sweep covers the row-load path.
            RefreshAllExtNames();

            // Icon image render.
            try
            {
                using var img = PreviewIconHelper.LoadFE8NVer1SkillIcon(_vm.IconId);
                Bitmap? bmp = img != null ? ImageConversionHelper.ToAvaloniaBitmap(img) : null;
                SetIconBitmap(bmp);
            }
            catch { SetIconBitmap(null); }
        }

        void WriteButton_Click(object? sender, RoutedEventArgs e)
        {
            // Early-guard so we don't create no-op undo entries when the
            // VM hasn't loaded an entry yet.
            if (!_vm.IsLoaded || _vm.CurrentAddr == 0) return;

            _undoService.Begin("Edit Skill Config (FE8N)");
            try
            {
                _vm.IconId = (uint)(IconIdBox.Value ?? 0);
                _vm.TextDetail = (uint)(TextDetailBox.Value ?? 0);
                _vm.CondUnit1 = (uint)(CondUnit1Box.Value ?? 0);
                _vm.CondUnit2 = (uint)(CondUnit2Box.Value ?? 0);
                _vm.CondUnit3 = (uint)(CondUnit3Box.Value ?? 0);
                _vm.CondUnit4 = (uint)(CondUnit4Box.Value ?? 0);
                _vm.CondClass1 = (uint)(CondClass1Box.Value ?? 0);
                _vm.CondClass2 = (uint)(CondClass2Box.Value ?? 0);
                _vm.CondClass3 = (uint)(CondClass3Box.Value ?? 0);
                _vm.CondClass4 = (uint)(CondClass4Box.Value ?? 0);
                _vm.CondItem1 = (uint)(CondItem1Box.Value ?? 0);
                _vm.CondItem2 = (uint)(CondItem2Box.Value ?? 0);
                _vm.CondItem3 = (uint)(CondItem3Box.Value ?? 0);
                _vm.CondItem4 = (uint)(CondItem4Box.Value ?? 0);

                // Ext-bytes B16..B31 (#790).
                _vm.Ext0 = (uint)(ExtB16Box.Value ?? 0);
                _vm.Ext1 = (uint)(ExtB17Box.Value ?? 0);
                _vm.Ext2 = (uint)(ExtB18Box.Value ?? 0);
                _vm.Ext3 = (uint)(ExtB19Box.Value ?? 0);
                _vm.Ext4 = (uint)(ExtB20Box.Value ?? 0);
                _vm.Ext5 = (uint)(ExtB21Box.Value ?? 0);
                _vm.Ext6 = (uint)(ExtB22Box.Value ?? 0);
                _vm.Ext7 = (uint)(ExtB23Box.Value ?? 0);
                _vm.Ext8 = (uint)(ExtB24Box.Value ?? 0);
                _vm.Ext9 = (uint)(ExtB25Box.Value ?? 0);
                _vm.Ext10 = (uint)(ExtB26Box.Value ?? 0);
                _vm.Ext11 = (uint)(ExtB27Box.Value ?? 0);
                _vm.Ext12 = (uint)(ExtB28Box.Value ?? 0);
                _vm.Ext13 = (uint)(ExtB29Box.Value ?? 0);
                _vm.Ext14 = (uint)(ExtB30Box.Value ?? 0);
                _vm.Ext15 = (uint)(ExtB31Box.Value ?? 0);
                _vm.Write();
                _undoService.Commit();
                _vm.MarkClean();
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.ErrorF("SkillConfigFE8NSkillView.Write failed: {0}", ex.Message);
            }
        }

        void SetIconBitmap(Bitmap? bmp)
        {
            if (_currentIconBitmap != null && !ReferenceEquals(_currentIconBitmap, bmp))
            {
                try { _currentIconBitmap.Dispose(); } catch { /* swallow */ }
            }
            _currentIconBitmap = bmp;
            IconImage.Source = bmp;
        }

        // -----------------------------------------------------------
        // Ext-byte unit-name preview (#793 — WF N00 decoration parity).
        //
        // Each B16..B31 ext-byte is treated as a one-based unit ID; WF's N00
        // (Unit) sub-list shows that unit's name, so the Avalonia Unit tab
        // decorates each byte with a READ-ONLY resolved unit-name TextBlock.
        // Read-only: this NEVER writes to the ROM and does NOT touch the
        // #790 write path (WriteButton_Click / _vm.Write).
        //
        // CRITICAL (#793 refinement): the preview is derived from the BOX's
        // CURRENT value, NOT the VM's Ext{i} prop. The VM props are only
        // re-synced from the boxes in WriteButton_Click, so a handler reading
        // _vm.Ext{i} would show the OLD name while the user is mid-edit.
        // Reading box.Value keeps the preview live.
        // -----------------------------------------------------------

        // Lazily-built box -> name-label pairing for the 16 ext-bytes. Cached
        // after the first build (the named controls are created in XAML and
        // never replaced).
        (NumericUpDown Box, TextBlock Label)[]? _extNamePairs;

        (NumericUpDown Box, TextBlock Label)[] ExtNamePairs => _extNamePairs ??= new[]
        {
            (ExtB16Box, ExtB16NameLabel), (ExtB17Box, ExtB17NameLabel),
            (ExtB18Box, ExtB18NameLabel), (ExtB19Box, ExtB19NameLabel),
            (ExtB20Box, ExtB20NameLabel), (ExtB21Box, ExtB21NameLabel),
            (ExtB22Box, ExtB22NameLabel), (ExtB23Box, ExtB23NameLabel),
            (ExtB24Box, ExtB24NameLabel), (ExtB25Box, ExtB25NameLabel),
            (ExtB26Box, ExtB26NameLabel), (ExtB27Box, ExtB27NameLabel),
            (ExtB28Box, ExtB28NameLabel), (ExtB29Box, ExtB29NameLabel),
            (ExtB30Box, ExtB30NameLabel), (ExtB31Box, ExtB31NameLabel),
        };

        /// <summary>
        /// Resolve the read-only unit-name preview for a single ext-byte value.
        /// WF N00 decoration: the byte is a one-based unit ID. A value of 0
        /// renders empty (no unit), NOT "???". Static + pure so the resolver
        /// contract can be unit-tested directly (#793).
        /// </summary>
        internal static string FormatExtUnitName(uint value)
        {
            // Explicit 0-guard: 0 means "no unit" → empty preview (the resolver
            // already returns "" for 0, but the guard makes the contract local
            // and independent of NameResolver's internals).
            if (value == 0) return "";
            try { return NameResolver.GetUnitNameByOneBasedId(value) ?? ""; }
            catch { return ""; }
        }

        /// <summary>Refresh one preview label from its paired NUD's current value.</summary>
        static void RefreshExtName(NumericUpDown box, TextBlock label)
        {
            label.Text = FormatExtUnitName((uint)(box.Value ?? 0));
        }

        /// <summary>Refresh all 16 ext-byte unit-name previews from the box values.</summary>
        void RefreshAllExtNames()
        {
            foreach (var (box, label) in ExtNamePairs)
            {
                RefreshExtName(box, label);
            }
        }

        /// <summary>Clear all 16 ext-byte unit-name previews (no entry loaded).</summary>
        void ClearExtNames()
        {
            foreach (var (_, label) in ExtNamePairs)
            {
                label.Text = "";
            }
        }

        /// <summary>
        /// Live-refresh the paired unit-name preview whenever an ext-byte NUD
        /// changes. Reads the BOX value (not the VM prop) so the name tracks
        /// the in-progress edit. Skipped while UpdateUI() is bulk-pushing
        /// values (the explicit RefreshAllExtNames() sweep there covers load).
        /// </summary>
        void ExtByteBox_ValueChanged(object? sender, NumericUpDownValueChangedEventArgs e)
        {
            if (_vm.IsLoading) return;
            if (sender is not NumericUpDown box) return;
            foreach (var (b, label) in ExtNamePairs)
            {
                if (ReferenceEquals(b, box))
                {
                    RefreshExtName(box, label);
                    return;
                }
            }
        }

        // -----------------------------------------------------------
        // Disabled-by-design handlers — kept wired so the AutomationIds are
        // enumerable from headless tests and the wired-or-inert audit passes.
        // The buttons are IsEnabled="False" in AXAML so these handlers never
        // fire at runtime. FE8N Ver1 has no animation pointer or icon I/O in
        // WinForms (render-only); disabled by design (#1008).
        // -----------------------------------------------------------

        // #898 / #1008 — FE8N v1 is intentionally read-only for skill icons.
        // The WF SkillConfigFE8NSkillForm has NO icon import/export (render-only),
        // and this variant's icon address derivation lacks the 0x100 page offset
        // that v2/v3 use, so wiring it to the shared SkillConfigIconIoHelper would
        // write to the wrong slot. Button is disabled by design (#1008) —
        // this handler is kept only to satisfy the wired-or-inert audit.
        void ImageImport_Click(object? sender, RoutedEventArgs e)
        {
            // Disabled by design (#1008): FE8N Ver1 is read-only for icon I/O.
            // Icon address lacks the 0x100 page offset v2/v3 use; WF form is render-only.
            // Button is IsEnabled="False"; handler kept only to satisfy the wired-or-inert audit.
            Log.Debug("SkillConfigFE8NSkillView.ImageImport_Click: button disabled by design (#1008), read-only variant");
        }

        void ImageExport_Click(object? sender, RoutedEventArgs e)
        {
            // Disabled by design (#1008): FE8N Ver1 is read-only for icon I/O.
            // Icon address lacks the 0x100 page offset v2/v3 use; WF form is render-only.
            // Button is IsEnabled="False"; handler kept only to satisfy the wired-or-inert audit.
            Log.Debug("SkillConfigFE8NSkillView.ImageExport_Click: button disabled by design (#1008), read-only variant");
        }

        void AnimationImport_Click(object? sender, RoutedEventArgs e)
        {
            // Disabled by design (#1008): FE8N Ver1 has no animation pointer or
            // animation I/O in WinForms (render-only). Button is IsEnabled="False";
            // this handler is kept only to satisfy the wired-or-inert audit.
            Log.Debug("SkillConfigFE8NSkillView.AnimationImport_Click: button disabled by design (#1008)");
        }

        void AnimationExport_Click(object? sender, RoutedEventArgs e)
        {
            // Disabled by design (#1008): FE8N Ver1 has no animation pointer or
            // animation I/O in WinForms (render-only). Button is IsEnabled="False";
            // this handler is kept only to satisfy the wired-or-inert audit.
            Log.Debug("SkillConfigFE8NSkillView.AnimationExport_Click: button disabled by design (#1008)");
        }

        void JumpToEditor_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                // #1115: FE8N Ver1 (yugudora) skills are render-only — there is NO
                // per-skill animation pointer in EITHER WinForms (SkillConfigFE8NSkillForm
                // has zero animation code) OR Avalonia (AnimationPointerBox is hard-coded
                // to 0; Animation Import/Export are disabled by design, #1008). So there
                // is no skill animation to seed the Creator from. This is WF parity, NOT
                // a regression — the 4 anime-capable variants (Ver2/Ver3/CSkillSys09x/
                // SkillSystem) DO seed via SkillConfigAnimeJumpHelper.
                CoreState.Services?.ShowInfo(R._("FE8N Ver1 skills are render-only and have no animation to edit in the Animation Creator."));
            }
            catch (Exception ex)
            {
                // Core Log.Error is params string[] (string.Join, NO composite
                // formatting) — a literal "{0}" would be logged verbatim, so use a
                // single interpolated string with the full exception (#969 precedent).
                Log.Error($"SkillConfigFE8NSkillView.JumpToEditor_Click failed: {ex}");
            }
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
    }
}
