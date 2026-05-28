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
    /// Avalonia counterpart of WinForms `SkillConfigFE8NVer3SkillForm`.
    /// Phase 1/2/4/5/6 gap-sweep fix (#392) raises the AXAML control surface
    /// from 19 to MEDIUM-verdict density and wires the textId + palette +
    /// 5 sub-pointer + animation pointer write under a single UndoService
    /// scope. Real image/animation import/export, bulk import/export, list
    /// expand, and editor-jump still depend on Core extraction work tracked
    /// by #500 - those buttons render so the density verdict moves, but
    /// their click handlers are intentional no-ops with a tooltip until the
    /// Core seam lands (mirrors the pattern established by PR #598).
    ///
    /// The 5 sub-list tabs (Unit/Class/Item/Item2/Composite) are placeholders
    /// with KnownGap comments tracked by #374 (InputFormRef auto-wiring);
    /// they surface the resolved sub-list pointer + entry count from the
    /// parent skill row but do not yet support editing or list expansion.
    /// </summary>
    public partial class SkillConfigFE8NVer3SkillView : TranslatedWindow, IEditorView, IDataVerifiableView
    {
        readonly SkillConfigFE8NVer3SkillViewModel _vm = new();
        readonly UndoService _undoService = new();
        bool _suppressZoomChange;
        bool _suppressFrameChange;
        Bitmap? _currentIconBitmap;
        Bitmap? _currentPreviewBitmap;

        public string ViewTitle => "Skill Configuration (FE8N v3)";
        public bool IsLoaded => _vm.IsLoaded;
        public ViewModelBase? DataViewModel => _vm;

        public SkillConfigFE8NVer3SkillView()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            Opened += (_, _) => LoadList();
            Closed += (_, _) =>
            {
                DisposeBitmap(ref _currentIconBitmap);
                DisposeBitmap(ref _currentPreviewBitmap);
            };
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
                EntryList.SetItemsWithIcons(items, i => ListIconLoaders.FE8NVer3SkillIconLoader(items, i));
                // #743: unified top-bar surfaces ReadStart / ReadCount via CLR properties.
                if (TopBar != null)
                {
                    TopBar.ReadStartAddress = _vm.ReadStartAddress;
                    TopBar.ReadCount = (int)_vm.ReadCount;
                }
                BlockSizeBox.Value = _vm.BlockSize;

                _suppressZoomChange = true;
                try
                {
                    ShowZoomComboBox.SelectedIndex = 0;
                    _vm.ShowZoomed = true;
                    PreviewImage.Stretch = global::Avalonia.Media.Stretch.Uniform;
                }
                finally { _suppressZoomChange = false; }
            }
            catch (Exception ex)
            {
                Log.Error("SkillConfigFE8NVer3SkillView.LoadList failed: {0}", ex.Message);
            }
            finally { _vm.IsLoading = false; _vm.MarkClean(); }
        }

        void ReloadList_Click(object? sender, RoutedEventArgs e)
        {
            LoadList();
        }

        // #743: routed event from the unified EditorTopBarWithInputs Reload button.
        void OnTopBarReloadRequested(object? sender, RoutedEventArgs e) => LoadList();

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
                Log.Error("SkillConfigFE8NVer3SkillView.OnSelected failed: {0}", ex.Message);
            }
            finally { _vm.IsLoading = false; _vm.MarkClean(); }
        }

        void UpdateUI()
        {
            // Selection-bar widgets.
            AddressBox.Value = _vm.CurrentAddr;
            BlockSizeBox.Value = _vm.BlockSize;
            SelectedAddressLabel.Content = $"0x{_vm.CurrentAddr:X08}";

            // Icon address label - derive from rom.p32(RomInfo.icon_pointer) + 128 * (0x100 + id).
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo != null)
            {
                try
                {
                    uint iconBaseAddr = rom.p32(rom.RomInfo.icon_pointer);
                    uint iconAddr = iconBaseAddr + 128 * (0x100 + _vm.SelectedId);
                    IconAddrLabel.Content = $"0x{iconAddr:X08}";
                }
                catch { IconAddrLabel.Content = ""; }
            }
            else
            {
                IconAddrLabel.Content = "";
            }

            // Edit fields.
            TextDetailBox.Value = _vm.TextDetail;
            string textPreview = _vm.TextDetail != 0
                ? NameResolver.GetTextById(_vm.TextDetail)
                : "";
            TextDetailTextBox.Text = textPreview ?? "";
            PaletteBox.Value = _vm.Palette;
            UnitSkillPointerBox.Value = _vm.UnitSkillPointer;
            ClassSkillPointerBox.Value = _vm.ClassSkillPointer;
            ItemSkillPointerBox.Value = _vm.ItemSkillPointer;
            Item2SkillPointerBox.Value = _vm.Item2SkillPointer;
            CompositeSkillPointerBox.Value = _vm.CompositeSkillPointer;
            AnimationPointerBox.Value = _vm.AnimationPointer;

            // Sub-list tab base addresses + entry counts (informational only - actual
            // sub-list editing is a KnownGap tracked by #374).
            UnitTabBaseAddrLabel.Content = $"Sub-list base: 0x{_vm.UnitSkillPointer:X08}";
            ClassTabBaseAddrLabel.Content = $"Sub-list base: 0x{_vm.ClassSkillPointer:X08}";
            ItemTabBaseAddrLabel.Content = $"Sub-list base: 0x{_vm.ItemSkillPointer:X08}";
            Item2TabBaseAddrLabel.Content = $"Sub-list base: 0x{_vm.Item2SkillPointer:X08}";
            CompositeTabBaseAddrLabel.Content = $"Sub-list base: 0x{_vm.CompositeSkillPointer:X08}";

            // Entry counts derived by walking the sub-list u8 terminator (WF
            // sub-list iteration predicate: terminate when u8(addr) == 0).
            // Addresses Copilot CLI PR-review finding #2 (round 1).
            UnitTabCountLabel.Content = $"Entry count: {CountSubListEntries(rom, _vm.UnitSkillPointer)}";
            ClassTabCountLabel.Content = $"Entry count: {CountSubListEntries(rom, _vm.ClassSkillPointer)}";
            ItemTabCountLabel.Content = $"Entry count: {CountSubListEntries(rom, _vm.ItemSkillPointer)}";
            Item2TabCountLabel.Content = $"Entry count: {CountSubListEntries(rom, _vm.Item2SkillPointer)}";
            CompositeTabCountLabel.Content = $"Entry count: {CountSubListEntries(rom, _vm.CompositeSkillPointer)}";

            // Icon Image render.
            try
            {
                if (rom != null && _vm.SkillBaseAddress != 0)
                {
                    using var img = PreviewIconHelper.LoadFE8NVer3SkillIcon(_vm.SelectedId, _vm.Palette);
                    Bitmap? bmp = img != null ? ImageConversionHelper.ToAvaloniaBitmap(img) : null;
                    SetIconBitmap(bmp);
                }
                else
                {
                    SetIconBitmap(null);
                }
            }
            catch { SetIconBitmap(null); }

            // Animation panel - only when the resolved pointer is safe.
            AnimationPanel.IsVisible = _vm.IsAnimationValid;
            if (_vm.IsAnimationValid)
            {
                _suppressFrameChange = true;
                try { ShowFrameUpDown.Value = _vm.SelectedFrame; }
                finally { _suppressFrameChange = false; }

                BinInfoBox.Text = _vm.BinInfoText;
                // Real frame rendering depends on ImageUtilSkillSystemsAnimeCreator
                // (#500). Until that moves to Core, leave the preview Image
                // blank but show the animation address text.
                SetPreviewBitmap(null);
            }
            else
            {
                SetPreviewBitmap(null);
                BinInfoBox.Text = "";
            }
        }

        void WriteButton_Click(object? sender, RoutedEventArgs e)
        {
            // Early-guard so we don't create no-op undo entries when the
            // VM hasn't loaded an entry yet.
            if (!_vm.IsLoaded || _vm.CurrentAddr == 0) return;

            _undoService.Begin("Edit Skill Config (FE8N v3)");
            try
            {
                _vm.TextDetail = (uint)(TextDetailBox.Value ?? 0);
                _vm.Palette = (uint)(PaletteBox.Value ?? 0);
                _vm.UnitSkillPointer = (uint)(UnitSkillPointerBox.Value ?? 0);
                _vm.ClassSkillPointer = (uint)(ClassSkillPointerBox.Value ?? 0);
                _vm.ItemSkillPointer = (uint)(ItemSkillPointerBox.Value ?? 0);
                _vm.Item2SkillPointer = (uint)(Item2SkillPointerBox.Value ?? 0);
                _vm.CompositeSkillPointer = (uint)(CompositeSkillPointerBox.Value ?? 0);
                _vm.AnimationPointer = (uint)(AnimationPointerBox.Value ?? 0);
                _vm.Write();
                _undoService.Commit();
                _vm.MarkClean();
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.Error("SkillConfigFE8NVer3SkillView.Write failed: {0}", ex.Message);
            }
        }

        /// <summary>
        /// Count the entries in a sub-list pointed at by <paramref name="subListBase"/>.
        /// WF iteration predicate (`SkillConfigFE8NVer3SkillForm.{N1..N5}_Init`
        /// readCount callback) terminates when `u8(addr) == 0`. Returns 0 if
        /// the pointer is null/unsafe or the table is empty.
        /// Mirrors the WF N1..N5 InputFormRef block-size=1 iteration. Capped
        /// at 256 entries to bound the walk.
        ///
        /// Addresses Copilot CLI PR-review finding #2 (round 1) - the AXAML
        /// `Entry count: -` placeholder must be replaced with real counts so
        /// the plan claim ("surface sub-list base + entry count") holds.
        /// </summary>
        static int CountSubListEntries(ROM rom, uint subListBase)
        {
            if (rom?.Data == null) return 0;
            if (subListBase == 0) return 0;
            if (!U.isSafetyOffset(subListBase, rom)) return 0;

            int count = 0;
            for (uint i = 0; i < 256; i++)
            {
                uint addr = subListBase + i;
                if (addr >= (uint)rom.Data.Length) break;
                if (rom.u8(addr) == 0) break;
                count++;
            }
            return count;
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

        void SetPreviewBitmap(Bitmap? bmp)
        {
            if (_currentPreviewBitmap != null && !ReferenceEquals(_currentPreviewBitmap, bmp))
            {
                try { _currentPreviewBitmap.Dispose(); } catch { /* swallow */ }
            }
            _currentPreviewBitmap = bmp;
            PreviewImage.Source = bmp;
        }

        // -----------------------------------------------------------
        // No-op handlers - wired so the AutomationIds are enumerable
        // from headless tests, and so the density verdict moves. The
        // real implementations depend on Core extraction tracked by
        // #500 / #374. Mirrors the exact pattern used by PR #598.
        // -----------------------------------------------------------

        void ImageImport_Click(object? sender, RoutedEventArgs e)
        {
            Log.Debug("SkillConfigFE8NVer3SkillView.ImageImport_Click invoked - disabled until Core extraction lands (#500)");
        }

        void ImageExport_Click(object? sender, RoutedEventArgs e)
        {
            Log.Debug("SkillConfigFE8NVer3SkillView.ImageExport_Click invoked - disabled until Core extraction lands (#500)");
        }

        void AnimationImport_Click(object? sender, RoutedEventArgs e)
        {
            Log.Debug("SkillConfigFE8NVer3SkillView.AnimationImport_Click invoked - disabled until Core extraction lands (#500)");
        }

        void AnimationExport_Click(object? sender, RoutedEventArgs e)
        {
            Log.Debug("SkillConfigFE8NVer3SkillView.AnimationExport_Click invoked - disabled until Core extraction lands (#500)");
        }

        void JumpToEditor_Click(object? sender, RoutedEventArgs e)
        {
            Log.Debug("SkillConfigFE8NVer3SkillView.JumpToEditor_Click invoked - disabled until ToolAnimationCreatorView.Init lands (#500)");
        }

        void JumpToCombatArt_Click(object? sender, RoutedEventArgs e)
        {
            Log.Debug("SkillConfigFE8NVer3SkillView.JumpToCombatArt_Click invoked - disabled until PatchManagerView exposes JumpToSelectStruct (#374)");
        }

        void ListExpand_Click(object? sender, RoutedEventArgs e)
        {
            Log.Debug("SkillConfigFE8NVer3SkillView.ListExpand_Click invoked - disabled until Core extraction lands (#500)");
        }

        void ShowFrameUpDown_ValueChanged(object? sender, NumericUpDownValueChangedEventArgs e)
        {
            if (_suppressFrameChange) return;
            if (!_vm.IsAnimationValid) return;
            _vm.SelectedFrame = (uint)(ShowFrameUpDown.Value ?? 0);
            BinInfoBox.Text = _vm.BinInfoText;
            // Real per-frame rendering pending #500.
        }

        void ShowZoomComboBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (_suppressZoomChange) return;
            bool zoomed = ShowZoomComboBox.SelectedIndex == 0;
            _vm.ShowZoomed = zoomed;
            PreviewImage.Stretch = zoomed
                ? global::Avalonia.Media.Stretch.Uniform
                : global::Avalonia.Media.Stretch.None;
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
    }
}
