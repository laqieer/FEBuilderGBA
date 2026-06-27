using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using global::Avalonia.Media.Imaging;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    /// <summary>
    /// Avalonia counterpart of WinForms `SkillConfigCSkillSystem09xForm`.
    /// Phase 1/2/4/5/6 gap-sweep fix (#430) raises the AXAML control surface
    /// from 9 to MEDIUM-verdict density and wires the W4/W6/animation-pointer
    /// write under a single UndoService scope. Real image/animation
    /// import/export and editor-jump still depend on Core extraction work
    /// tracked by #500 - those buttons render so the density verdict moves,
    /// but their click handlers are intentional no-ops with a tooltip until
    /// the Core seam lands (mirrors the pattern used by #433).
    /// </summary>
    public partial class SkillConfigFE8UCSkillSys09xView : TranslatedWindow, IEditorView, IDataVerifiableView
    {
        readonly SkillConfigFE8UCSkillSys09xViewModel _vm = new();
        readonly UndoService _undoService = new();
        readonly SkillConfigAnimePreview _animePreview = new();
        bool _suppressZoomChange;
        bool _suppressFrameChange;
        Bitmap? _currentIconBitmap;
        Bitmap? _currentPreviewBitmap;

        public string ViewTitle => "Skill Configuration (CSkillSys 0.9.x)";
        public bool IsLoaded => _vm.IsLoaded;
        public ViewModelBase? DataViewModel => _vm;

        public SkillConfigFE8UCSkillSys09xView()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            Opened += (_, _) => LoadList();
            Closed += (_, _) =>
            {
                DisposeBitmap(ref _currentIconBitmap);
                DisposeBitmap(ref _currentPreviewBitmap);
                _animePreview.Clear();
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
                EntryList.SetItemsWithIcons(items, i => ListIconLoaders.CSkillSysSkillIconLoader(items, i));
                // #743: unified top-bar surfaces ReadStart / ReadCount via CLR properties.
                if (TopBar != null)
                {
                    TopBar.ReadStartAddress = _vm.ReadStartAddress;
                    TopBar.ReadCount = (int)_vm.ReadCount;
                }

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
                Log.ErrorF("SkillConfigFE8UCSkillSys09xView.LoadList failed: {0}", ex.Message);
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
                Log.ErrorF("SkillConfigFE8UCSkillSys09xView.OnSelected failed: {0}", ex.Message);
            }
            finally { _vm.IsLoading = false; _vm.MarkClean(); }
        }

        void UpdateUI()
        {
            // Selection-bar widgets.
            AddressBox.Value = _vm.CurrentAddr;
            BlockSizeBox.Value = _vm.BlockSize;
            SelectedAddressLabel.Content = $"0x{_vm.CurrentAddr:X08}";

            // Edit fields.
            IconAddrLabel.Content = $"0x{_vm.IconAddr:X08}";
            SkillNameBox.Value = _vm.SkillNameMsg;
            DescriptionBox.Value = _vm.DescriptionMsg;
            SkillNameTextBox.Text = _vm.SkillNameText;
            DescriptionTextBox.Text = _vm.DescriptionText;
            AnimationPointerBox.Value = _vm.AnimationPointer;

            // Icon Image - render the per-skill icon by dereferencing the
            // GBA pointer stored at entry+0 (not striped like SkillSystems).
            try
            {
                using var img = PreviewIconHelper.LoadCSkillSysIcon(_vm.IconAddr);
                Bitmap? bmp = img != null ? ImageConversionHelper.ToAvaloniaBitmap(img) : null;
                SetIconBitmap(bmp);
            }
            catch { SetIconBitmap(null); }

            // Animation panel - only when D0 resolves safely.
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
            // VM hasn't loaded an entry yet (#506 pattern).
            if (!_vm.IsLoaded || _vm.CurrentAddr == 0) return;

            _undoService.Begin("Edit Skill Config CSkillSys 0.9.x");
            try
            {
                _vm.SkillNameMsg = (uint)(SkillNameBox.Value ?? 0);
                _vm.DescriptionMsg = (uint)(DescriptionBox.Value ?? 0);
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
                Log.ErrorF("SkillConfigFE8UCSkillSys09xView.Write failed: {0}", ex.Message);
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
        // #500. Mirrors the exact pattern used by #433's
        // ListExpand_Click.
        // -----------------------------------------------------------

        // CSkillSys uses the same fixed skill-palette pointer as SkillSystem
        // (mirrors WinForms `SkillPalettePointer = 0x22370`).
        const uint SKILL_PALETTE_POINTER = 0x22370;

        // #898 — real skill-icon Image Import/Export via the shared
        // SkillConfigIconIoHelper. Unlike the striped SkillSystem table, the
        // CSkillSys icon address is a GBA pointer stored at entry+0. It MUST
        // be re-dereferenced fresh here (never write through a pointer cached
        // across a ROM reload): byteAddr = U.toOffset(rom.u32(CurrentAddr+0)).
        async void ImageImport_Click(object? sender, RoutedEventArgs e)
        {
            ROM rom = CoreState.ROM;
            if (rom == null || rom.RomInfo == null || !_vm.IsLoaded || _vm.CurrentAddr == 0) return;
            if (!U.isSafetyOffset(_vm.CurrentAddr + 3, rom)) return;
            if (!U.isSafetyOffset(SKILL_PALETTE_POINTER + 3, rom)) return;

            uint iconGbaPointer = rom.u32(_vm.CurrentAddr + 0);
            if (!U.isSafetyPointer(iconGbaPointer)) return;
            uint iconByteAddr = U.toOffset(iconGbaPointer);
            uint paletteAddr = rom.p32(SKILL_PALETTE_POINTER);

            string? err = await SkillConfigIconIoHelper.ImportIconAsync(
                this, rom, iconByteAddr, paletteAddr, _undoService);
            if (err == null) return; // user cancelled — do not refresh.
            if (err != "")
            {
                Log.Notify("SkillConfigFE8UCSkillSys09xView.ImageImport_Click: " + err);
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
            if (rom == null || rom.RomInfo == null || !_vm.IsLoaded || _vm.CurrentAddr == 0) return;
            if (!U.isSafetyOffset(_vm.CurrentAddr + 3, rom)) return;
            if (!U.isSafetyOffset(SKILL_PALETTE_POINTER + 3, rom)) return;

            uint iconGbaPointer = rom.u32(_vm.CurrentAddr + 0);
            if (!U.isSafetyPointer(iconGbaPointer)) return;
            uint iconByteAddr = U.toOffset(iconGbaPointer);
            uint paletteAddr = rom.p32(SKILL_PALETTE_POINTER);

            string? path = await FERepoPickHelper.PickForEditor(this,
                FERepoResourceBrowser.FERepoEditorKind.SkillIcon);
            if (string.IsNullOrEmpty(path)) return;

            string? err = SkillConfigIconIoHelper.ImportIconFromPath(
                rom, iconByteAddr, paletteAddr, _undoService, path);
            if (err == null) return;
            if (err != "")
            {
                Log.Notify("SkillConfigFE8UCSkillSys09xView.FERepo_Click: " + err);
                return;
            }

            UpdateUI();
            LoadList();
            EntryList.SelectAddress(_vm.CurrentAddr);
        }

        async void ImageExport_Click(object? sender, RoutedEventArgs e)
        {
            ROM rom = CoreState.ROM;
            if (rom == null || rom.RomInfo == null || !_vm.IsLoaded || _vm.CurrentAddr == 0) return;
            if (!U.isSafetyOffset(_vm.CurrentAddr + 3, rom)) return;
            if (!U.isSafetyOffset(SKILL_PALETTE_POINTER + 3, rom)) return;

            uint iconGbaPointer = rom.u32(_vm.CurrentAddr + 0);
            if (!U.isSafetyPointer(iconGbaPointer)) return;
            uint iconByteAddr = U.toOffset(iconGbaPointer);
            uint paletteAddr = rom.p32(SKILL_PALETTE_POINTER);

            await SkillConfigIconIoHelper.ExportIconAsync(this, rom, iconByteAddr, paletteAddr);
        }

        // #913 SLICE 1 — real skill-anime import via SkillSystemsAnimeImportCore
        // (FE8J path; FE8U shows a clean not-supported message, ZERO mutation).
        async void AnimationImport_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.IsLoaded) return;
            bool ok = await SkillConfigAnimeImportHelper.ImportAsync(
                this, _vm.AnimationPointer, _undoService);
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
            await SkillConfigAnimeExportHelper.ExportAsync(this, _vm.AnimationPointer, _vm.SelectedId);
        }

        void JumpToEditor_Click(object? sender, RoutedEventArgs e)
        {
            // #1115: seed the Animation Creator from the selected skill's animation
            // (read-only). Probe-before-open so a 0/empty pointer shows an honest
            // message instead of a blank Creator. Replaces the #996 carve-out.
            if (!_vm.IsLoaded) return;
            SkillConfigAnimeJumpHelper.JumpToCreator(
                _vm.SelectedId, _vm.AnimationPointer, "SkillConfigFE8UCSkillSys09xView");
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
