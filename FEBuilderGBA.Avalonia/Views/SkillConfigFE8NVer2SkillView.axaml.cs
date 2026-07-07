// SPDX-License-Identifier: GPL-3.0-or-later
using global::Avalonia;
using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using global::Avalonia.Media.Imaging;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    /// <summary>
    /// Avalonia counterpart of WinForms `SkillConfigFE8NVer2SkillForm`.
    /// Phase 1/2/4/5/6 gap-sweep fix (#396) raises the AXAML control surface
    /// from 17 to MEDIUM-verdict density and wires the textId + palette +
    /// 3-4 sub-pointer + animation pointer write under a single UndoService
    /// scope. Real image/animation import/export, bulk import/export, list
    /// expand, and editor-jump still depend on Core extraction work tracked
    /// by #500 - those buttons render so the density verdict moves, but
    /// their click handlers are intentional no-ops with a tooltip until the
    /// Core seam lands (mirrors the pattern established by PR #516 / #525).
    ///
    /// The 4 sub-list tabs (Unit/Class/Item/Item2) are placeholders with
    /// KnownGap comments tracked by #374 (InputFormRef auto-wiring); they
    /// surface the resolved sub-list pointer + entry count from the parent
    /// skill row but do not yet support editing or list expansion.
    /// </summary>
    public partial class SkillConfigFE8NVer2SkillView : TranslatedUserControl, IEmbeddableEditor, IDataVerifiableView
    {
        readonly SkillConfigFE8NVer2SkillViewModel _vm = new();
        readonly UndoService _undoService = new();
        bool _hasLoadedList;
        readonly SkillConfigAnimePreview _animePreview = new();
        bool _suppressZoomChange;
        bool _suppressFrameChange;
        Bitmap? _currentIconBitmap;
        Bitmap? _currentPreviewBitmap;

        public string ViewTitle => "Skill Configuration (FE8N v2)";
        public new bool IsLoaded => _vm.IsLoaded;
        public EditorDescriptor Descriptor => new("Skill Configuration (FE8N v2)", 980, 780, SizeToContent: global::Avalonia.Controls.SizeToContent.WidthAndHeight);
        public event EventHandler? CloseRequested;
        public ViewModelBase? DataViewModel => _vm;
        public void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);

        public SkillConfigFE8NVer2SkillView()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;

            // #930 — wire the 4 embedded sub-list editors: inject the host's
            // shared UndoService, set titles, and re-sync on any mutation (C1).
            UnitSubEditor.UndoService = _undoService;
            ClassSubEditor.UndoService = _undoService;
            ItemSubEditor.UndoService = _undoService;
            Item2SubEditor.UndoService = _undoService;
            UnitSubEditor.SetTitle(R._("Unit Skill Sub-list"));
            ClassSubEditor.SetTitle(R._("Class Skill Sub-list"));
            ItemSubEditor.SetTitle(R._("Item Skill Sub-list"));
            Item2SubEditor.SetTitle(R._("Item2 Skill Sub-list"));
            // Per-instance AutomationId prefixes so the 4 embedded editors don't
            // collide within this view (the inner controls share static ids).
            UnitSubEditor.ApplyAutomationIdPrefix("SkillConfigFE8NVer2Skill_UnitSubEditor");
            ClassSubEditor.ApplyAutomationIdPrefix("SkillConfigFE8NVer2Skill_ClassSubEditor");
            ItemSubEditor.ApplyAutomationIdPrefix("SkillConfigFE8NVer2Skill_ItemSubEditor");
            Item2SubEditor.ApplyAutomationIdPrefix("SkillConfigFE8NVer2Skill_Item2SubEditor");
            UnitSubEditor.Changed += OnSubListChanged;
            ClassSubEditor.Changed += OnSubListChanged;
            ItemSubEditor.Changed += OnSubListChanged;
            Item2SubEditor.Changed += OnSubListChanged;

        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnDetachedFromVisualTree(e);
            DisposeBitmap(ref _currentIconBitmap);
            DisposeBitmap(ref _currentPreviewBitmap);
            _animePreview.Clear();
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

        /// <summary>
        /// Load the 4 sub-list editors against the per-skill pointer slots.
        /// Unit +4 / Class +8 / Item +12 / Item2 +16. Item2's canEdit is gated
        /// on HasItem2: when stride &lt; 20, addr+16 is the next row's first u32,
        /// so the editor is not loaded/editable (B2).
        /// </summary>
        void LoadSubEditors()
        {
            uint row = _vm.CurrentRowAddr;
            if (row == 0) return;
            UnitSubEditor.Load(row + 4, NameResolver.GetUnitName, true);
            ClassSubEditor.Load(row + 8, NameResolver.GetClassName, true);
            ItemSubEditor.Load(row + 12, NameResolver.GetItemName, true);
            Item2SubEditor.Load(row + 16, NameResolver.GetItemName, _vm.HasItem2);
        }

        /// <summary>
        /// C1 — after any sub-list op repoints a Px slot, re-run the host's
        /// LoadEntry to re-read the now-updated Px offsets into its cache (so a
        /// subsequent main-row Write is idempotent w.r.t. the repoint instead of
        /// reverting it + orphaning the new array), then reload the editors.
        /// </summary>
        void OnSubListChanged()
        {
            uint row = _vm.CurrentRowAddr;
            if (row == 0) return;
            _vm.IsLoading = true;
            try
            {
                _vm.LoadEntry(row);
                UpdateUI();
            }
            catch (Exception ex)
            {
                Log.ErrorF("SkillConfigFE8NVer2SkillView.OnSubListChanged failed: {0}", ex.Message);
            }
            finally { _vm.IsLoading = false; _vm.MarkClean(); }
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
                EntryList.SetItemsWithIcons(items, i => ListIconLoaders.FE8NVer2SkillIconLoader(items, i));
                // #743: unified top-bar surfaces ReadStart / ReadCount via CLR properties.
                if (TopBar != null)
                {
                    TopBar.ReadStartAddress = _vm.ReadStartAddress;
                    TopBar.ReadCount = (int)_vm.ReadCount;
                }
                BlockSizeBox.Value = _vm.BlockSize;

                // Show / hide the Item2 row + tab based on detected stride.
                Item2PointerPanel.IsVisible = _vm.HasItem2;
                Item2Tab.IsVisible = _vm.HasItem2;

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
                Log.ErrorF("SkillConfigFE8NVer2SkillView.LoadList failed: {0}", ex.Message);
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
                Log.ErrorF("SkillConfigFE8NVer2SkillView.OnSelected failed: {0}", ex.Message);
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
            AnimationPointerBox.Value = _vm.AnimationPointer;

            // Load the 4 embedded sub-list editors against the per-skill pointer
            // SLOTs (CurrentRowAddr + 4/8/12/16). Item2 is gated on HasItem2 (B2).
            LoadSubEditors();

            // Icon Image render.
            try
            {
                if (rom != null && _vm.SkillBaseAddress != 0)
                {
                    using var img = PreviewIconHelper.LoadFE8NVer2SkillIcon(_vm.SelectedId, _vm.Palette);
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
                // #1010 — render the per-frame preview via the cross-platform
                // READ-ONLY SkillSystemsAnimeExportCore decode (cached by anime
                // pointer in _animePreview).
                bool hasFrames = _animePreview.Load(CoreState.ROM, _vm.AnimationPointer);
                int frameCount = _animePreview.FrameCount;
                // Clamp SelectedFrame into range BEFORE rendering (a shorter
                // animation on a same-row pointer change).
                if (frameCount > 0 && _vm.SelectedFrame >= (uint)frameCount) _vm.SelectedFrame = (uint)(frameCount - 1);
                _suppressFrameChange = true;
                try
                {
                    ShowFrameUpDown.Maximum = Math.Max(0, frameCount - 1);
                    ShowFrameUpDown.Value = _vm.SelectedFrame;
                }
                finally { _suppressFrameChange = false; }
                BinInfoBox.Text = _vm.BinInfoText;
                SetPreviewBitmap(hasFrames ? _animePreview.TryGetFrameBitmap((int)_vm.SelectedFrame) : null);
            }
            else
            {
                _animePreview.Clear();
                SetPreviewBitmap(null);
                BinInfoBox.Text = "";
            }
        }

        void WriteButton_Click(object? sender, RoutedEventArgs e)
        {
            // Early-guard so we don't create no-op undo entries when the
            // VM hasn't loaded an entry yet.
            if (!_vm.IsLoaded || _vm.CurrentAddr == 0) return;

            _undoService.Begin("Edit Skill Config (FE8N v2)");
            try
            {
                _vm.TextDetail = (uint)(TextDetailBox.Value ?? 0);
                _vm.Palette = (uint)(PaletteBox.Value ?? 0);
                _vm.UnitSkillPointer = (uint)(UnitSkillPointerBox.Value ?? 0);
                _vm.ClassSkillPointer = (uint)(ClassSkillPointerBox.Value ?? 0);
                _vm.ItemSkillPointer = (uint)(ItemSkillPointerBox.Value ?? 0);
                if (_vm.HasItem2)
                {
                    _vm.Item2SkillPointer = (uint)(Item2SkillPointerBox.Value ?? 0);
                }
                _vm.AnimationPointer = (uint)(AnimationPointerBox.Value ?? 0);
                _vm.Write();
                _undoService.Commit();
                _vm.MarkClean();
                // #1010 — the animation pointer may have changed; drop the
                // cached decode and re-render the preview for the new pointer.
                _animePreview.Clear();
                UpdateUI();
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.ErrorF("SkillConfigFE8NVer2SkillView.Write failed: {0}", ex.Message);
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
        // #500. Mirrors the exact pattern used by PR #525 / #516.
        // -----------------------------------------------------------

        // #898 — real skill-icon Image Import/Export via the shared
        // SkillConfigIconIoHelper. FE8N v2 stores the icon at the WF-standard
        // rom.p32(icon_pointer) + 128 * (0x100 + id) slot, and selects the
        // palette on the per-skill W2/Palette field (mirrors WinForms
        // SkillConfigFE8NVer2SkillForm.GetSkillPaletteAddress): ==0 ->
        // system_weapon_icon_palette_pointer, else icon_palette_pointer.
        // Both base+palette are re-derived fresh here under the live ROM.
        bool TryResolveIconAddrs(ROM rom, out uint iconByteAddr, out uint paletteAddr)
        {
            iconByteAddr = 0;
            paletteAddr = 0;
            if (rom?.RomInfo == null) return false;
            if (!U.isSafetyOffset(rom.RomInfo.icon_pointer + 3, rom)) return false;

            uint iconBaseAddr = rom.p32(rom.RomInfo.icon_pointer);
            if (!U.isSafetyOffset(iconBaseAddr, rom)) return false;
            iconByteAddr = iconBaseAddr + SkillConfigIconIoHelper.IconByteSize * (0x100 + _vm.SelectedId);

            uint palettePointerAddr = (_vm.Palette == 0)
                ? rom.RomInfo.system_weapon_icon_palette_pointer
                : rom.RomInfo.icon_palette_pointer;
            if (!U.isSafetyOffset(palettePointerAddr + 3, rom)) return false;
            paletteAddr = rom.p32(palettePointerAddr);
            return U.isSafetyOffset(paletteAddr, rom);
        }

        async void ImageImport_Click(object? sender, RoutedEventArgs e)
        {
            ROM rom = CoreState.ROM;
            if (rom == null || !_vm.IsLoaded || _vm.SkillBaseAddress == 0) return;
            if (!TryResolveIconAddrs(rom, out uint iconByteAddr, out uint paletteAddr)) return;

            string? err = await SkillConfigIconIoHelper.ImportIconAsync(
                TopLevel.GetTopLevel(this) as Window, rom, iconByteAddr, paletteAddr, _undoService);
            if (err == null) return; // user cancelled — do not refresh.
            if (err != "")
            {
                Log.Notify("SkillConfigFE8NVer2SkillView.ImageImport_Click: " + err);
                return;
            }

            UpdateUI();
            LoadList();
            EntryList.SelectAddress(_vm.CurrentAddr);
        }

        // #1397 — FE-Repo button: pick a 16x16 skill icon from the FE-Repo
        // "Special - Skill Icons" folder and route it through the SAME
        // path-taking import core (16x16 strict + remap onto the ROM palette →
        // 17+-color sheets reduced, not corrupted). No second import path.
        async void FERepo_Click(object? sender, RoutedEventArgs e)
        {
            ROM rom = CoreState.ROM;
            if (rom == null || !_vm.IsLoaded || _vm.SkillBaseAddress == 0) return;
            if (!TryResolveIconAddrs(rom, out uint iconByteAddr, out uint paletteAddr)) return;

            string? path = await FERepoPickHelper.PickForEditor(TopLevel.GetTopLevel(this) as Window,
                FERepoResourceBrowser.FERepoEditorKind.SkillIcon);
            if (string.IsNullOrEmpty(path)) return;

            string? err = SkillConfigIconIoHelper.ImportIconFromPath(
                rom, iconByteAddr, paletteAddr, _undoService, path);
            if (err == null) return;
            if (err != "")
            {
                Log.Notify("SkillConfigFE8NVer2SkillView.FERepo_Click: " + err);
                return;
            }

            UpdateUI();
            LoadList();
            EntryList.SelectAddress(_vm.CurrentAddr);
        }

        async void ImageExport_Click(object? sender, RoutedEventArgs e)
        {
            ROM rom = CoreState.ROM;
            if (rom == null || !_vm.IsLoaded || _vm.SkillBaseAddress == 0) return;
            if (!TryResolveIconAddrs(rom, out uint iconByteAddr, out uint paletteAddr)) return;

            await SkillConfigIconIoHelper.ExportIconAsync(TopLevel.GetTopLevel(this) as Window, rom, iconByteAddr, paletteAddr);
        }

        // #913 SLICE 1 — real skill-anime import via SkillSystemsAnimeImportCore
        // (FE8J path; FE8U shows a clean not-supported message, ZERO mutation).
        async void AnimationImport_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.IsLoaded) return;
            bool ok = await SkillConfigAnimeImportHelper.ImportAsync(
                TopLevel.GetTopLevel(this) as Window, _vm.AnimationPointer, _undoService);
            if (!ok) return;

            // #1010 — the animation bytes changed; drop the cached decode.
            _animePreview.Clear();
            OnSelected(_vm.CurrentAddr);
            LoadList();
            EntryList.SelectAddress(_vm.CurrentAddr);
        }

        // #910 — real animation export via SkillSystemsAnimeExportCore.
        async void AnimationExport_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.IsLoaded) return;
            await SkillConfigAnimeExportHelper.ExportAsync(TopLevel.GetTopLevel(this) as Window, _vm.AnimationPointer, _vm.SelectedId);
        }

        void JumpToEditor_Click(object? sender, RoutedEventArgs e)
        {
            // #1115: seed the Animation Creator from the selected skill's animation
            // (read-only). Probe-before-open so a 0/empty pointer shows an honest
            // message instead of a blank Creator. Replaces the #996 carve-out.
            if (!_vm.IsLoaded) return;
            SkillConfigAnimeJumpHelper.JumpToCreator(
                _vm.SelectedId, _vm.AnimationPointer, "SkillConfigFE8NVer2SkillView");
        }

        void ShowFrameUpDown_ValueChanged(object? sender, NumericUpDownValueChangedEventArgs e)
        {
            if (_suppressFrameChange) return;
            if (!_vm.IsAnimationValid) return;
            _vm.SelectedFrame = (uint)(ShowFrameUpDown.Value ?? 0);
            BinInfoBox.Text = _vm.BinInfoText;
            // #1010 — render the selected frame from the cached decode.
            SetPreviewBitmap(_animePreview.TryGetFrameBitmap((int)_vm.SelectedFrame));
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
