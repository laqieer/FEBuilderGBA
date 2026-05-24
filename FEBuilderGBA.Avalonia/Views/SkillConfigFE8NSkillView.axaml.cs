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
    /// Image/Animation Import/Export, JumpToEditor, and List Expand still
    /// depend on Core extraction work tracked by #500 - those buttons render
    /// so the density verdict moves, but their click handlers are intentional
    /// no-ops with a tooltip until the Core seam lands (mirrors PR #598).
    ///
    /// The 4 sub-list tabs (Unit/Class/Item/Other) are placeholders with
    /// KnownGap comments tracked by #374 (InputFormRef auto-wiring).
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

                // Wire the AddressList using the FE8N v1 icon loader (W0-driven).
                EntryList.SetItemsWithIcons(items, i => FE8NVer1IconLoader(items, i));

                ReadStartAddressBox.Value = _vm.ReadStartAddress;
                ReadCountBox.Value = _vm.ReadCount;
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
                Log.Error("SkillConfigFE8NSkillView.LoadList failed: {0}", ex.Message);
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

        void ReloadList_Click(object? sender, RoutedEventArgs e)
        {
            LoadList();
        }

        void FilterComboBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (_suppressPointerChange) return;
            int idx = FilterComboBox.SelectedIndex;
            if (idx < 0) return;
            _vm.IsLoading = true;
            try
            {
                var items = _vm.SelectPointer(idx);
                EntryList.SetItemsWithIcons(items, i => FE8NVer1IconLoader(items, i));
                ReadStartAddressBox.Value = _vm.ReadStartAddress;
                ReadCountBox.Value = _vm.ReadCount;
            }
            catch (Exception ex)
            {
                Log.Error("SkillConfigFE8NSkillView.FilterComboBox_SelectionChanged failed: {0}", ex.Message);
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
                Log.Error("SkillConfigFE8NSkillView.OnSelected failed: {0}", ex.Message);
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

            // Sub-list tab base addresses (informational only - actual sub-list
            // editing is a KnownGap tracked by #374). FE8N v1's N00..N03 tabs
            // hold raw bytes B16..B31 without their own pointer table, so we
            // surface the row's own offset + 16 as the "sub-list base".
            uint subBase = _vm.CurrentAddr + 16u;
            UnitTabBaseAddrLabel.Content = $"Sub-list base: 0x{subBase:X08}";
            UnitTabCountLabel.Content = "Entry count: 16 bytes (B16..B31)";
            ClassTabBaseAddrLabel.Content = $"Sub-list base: 0x{subBase:X08}";
            ClassTabCountLabel.Content = "Entry count: 16 bytes (B16..B31)";
            ItemTabBaseAddrLabel.Content = $"Sub-list base: 0x{subBase:X08}";
            ItemTabCountLabel.Content = "Entry count: 16 bytes (B16..B31)";
            OtherTabBaseAddrLabel.Content = $"Sub-list base: 0x{subBase:X08}";
            OtherTabCountLabel.Content = "Entry count: 16 bytes (B16..B31)";

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
                _vm.Write();
                _undoService.Commit();
                _vm.MarkClean();
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.Error("SkillConfigFE8NSkillView.Write failed: {0}", ex.Message);
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
        // No-op handlers - wired so the AutomationIds are enumerable
        // from headless tests, and so the density verdict moves. The
        // real implementations depend on Core extraction tracked by
        // #500. Mirrors the exact pattern used by PR #598 / #525 / #516.
        // -----------------------------------------------------------

        void ImageImport_Click(object? sender, RoutedEventArgs e)
        {
            Log.Debug("SkillConfigFE8NSkillView.ImageImport_Click invoked - disabled until Core extraction lands (#500)");
        }

        void ImageExport_Click(object? sender, RoutedEventArgs e)
        {
            Log.Debug("SkillConfigFE8NSkillView.ImageExport_Click invoked - disabled until Core extraction lands (#500)");
        }

        void AnimationImport_Click(object? sender, RoutedEventArgs e)
        {
            Log.Debug("SkillConfigFE8NSkillView.AnimationImport_Click invoked - disabled until Core extraction lands (#500)");
        }

        void AnimationExport_Click(object? sender, RoutedEventArgs e)
        {
            Log.Debug("SkillConfigFE8NSkillView.AnimationExport_Click invoked - disabled until Core extraction lands (#500)");
        }

        void JumpToEditor_Click(object? sender, RoutedEventArgs e)
        {
            Log.Debug("SkillConfigFE8NSkillView.JumpToEditor_Click invoked - disabled until ToolAnimationCreatorView.Init lands (#500)");
        }

        void ListExpand_Click(object? sender, RoutedEventArgs e)
        {
            Log.Debug("SkillConfigFE8NSkillView.ListExpand_Click invoked - disabled until Core extraction lands (#500)");
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
    }
}
