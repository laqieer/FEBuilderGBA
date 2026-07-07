// SPDX-License-Identifier: GPL-3.0-or-later
// Code-behind for SkillAssignmentClassCSkillSysView. Rebuilt for gap-sweep
// #415 to a three-pane master-detail layout matching WinForms
// SkillAssignmentClassCSkillSysForm. The VM is the consolidated
// SkillAssignmentClassCSkillSysViewModel (post-#415 — the legacy
// ...ViewViewModel.cs stub was removed). The XView -> XViewModel name
// pairing is what enables UndoCoverageScanner's View-to-VM upgrade pass to
// mark the in-VM write callsites as Covered.
using global::Avalonia;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class SkillAssignmentClassCSkillSysView : TranslatedUserControl, IEmbeddableEditor, IDataVerifiableView
    {
        readonly SkillAssignmentClassCSkillSysViewModel _vm = new();
        readonly UndoService _undoService = new();
        bool _hasLoadedList;

        // Track currently selected N1 row address. Reset on class change
        // (Copilot-flagged stale-selection guard from PR #544).
        uint _n1SelectedAddr;

        // Parallel display-string collections so AddrResult.name is visible
        // in the ListBox even though Avalonia bindings require properties.
        readonly ObservableCollection<string> _classDisplayItems = new();
        readonly ObservableCollection<string> _n1DisplayItems = new();
        List<AddrResult> _classItems = new();
        List<AddrResult> _n1Items = new();

        public string ViewTitle => "Skill Assignment - Class (CSkillSys)";
        public new bool IsLoaded => _vm.IsLoaded;
        public EditorDescriptor Descriptor => new("Skill Assignment - Class (CSkillSys)", 1200, 900, SizeToContent: global::Avalonia.Controls.SizeToContent.WidthAndHeight);
        public event EventHandler? CloseRequested;
        public ViewModelBase? DataViewModel => _vm;
        public void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);

        public SkillAssignmentClassCSkillSysView()
        {
            InitializeComponent();
            ClassListBox.ItemsSource = _classDisplayItems;
            N1ListBox.ItemsSource = _n1DisplayItems;
            ClassListBox.SelectionChanged += (s, e) =>
            {
                int idx = ClassListBox.SelectedIndex;
                if (idx >= 0 && idx < _classItems.Count) OnClassSelected(_classItems[idx]);
            };
            N1ListBox.SelectionChanged += (s, e) =>
            {
                int idx = N1ListBox.SelectedIndex;
                if (idx >= 0 && idx < _n1Items.Count) OnN1Selected(_n1Items[idx]);
            };
            N1B0Box.ValueChanged += (s, e) => OnLevelChanged();
            XLvValueBox.ValueChanged += (s, e) => OnXLevelValueChanged();
            XLvPlayerOnlyCheckBox.IsCheckedChanged += (s, e) => OnLevelModeCheckboxChanged(32);
            XLvEnemyOnlyCheckBox.IsCheckedChanged += (s, e) => OnLevelModeCheckboxChanged(64);
            XLvNormalHardCheckBox.IsCheckedChanged += (s, e) => OnLevelModeCheckboxChanged(96);
            XLvHardOnlyCheckBox.IsCheckedChanged += (s, e) => OnLevelModeCheckboxChanged(128);
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnDetachedFromVisualTree(e);
            if (_classSkillIconBitmap != null) try { _classSkillIconBitmap.Dispose(); } catch { /* swallow */ }
            if (_n1SkillIconBitmap != null) try { _n1SkillIconBitmap.Dispose(); } catch { /* swallow */ }
            _classSkillIconBitmap = null;
            _n1SkillIconBitmap = null;
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            if (!_hasLoadedList)
            {
                _hasLoadedList = true;
                Initialize();
            }
        }

        void Initialize()
        {
            try
            {
                _vm.RefreshPatchState();
                UpdateBannerVisibility();
                InitializeReadConfig();
                LoadClassList();
            }
            catch (Exception ex)
            {
                Log.ErrorF("SkillAssignmentClassCSkillSysView.Initialize failed: {0}", ex.Message);
            }
            finally { _vm.IsLoaded = true; }
        }

        void UpdateBannerVisibility()
        {
            // Banner shows only when the CSkillSys 3.00 patch is NOT detected
            // (gates the form to CSkillSys300 per MainFE8Form.cs:715 routing).
            // When the patch is installed the editor is fully functional and
            // the banner is hidden.
            PatchBanner.IsVisible = !_vm.IsCSkillSys300Active;
            XLevelAddPanel.IsVisible = false; // populated lazily on selection
        }

        void LoadClassList()
        {
            try
            {
                // Push read-config UI values into the VM so the user's
                // ReadStartAddress / ReadCount overrides drive the list walk.
                // Copilot CLI PR #552 review #2: previously the VM defaults
                // were copied INTO the boxes, making the controls inert.
                // #743: unified top-bar surfaces ReadStart / ReadCount via CLR properties.
                _vm.ReadStartAddress = TopBar?.ReadStartAddress ?? 0u;
                _vm.ReadCount = (uint)(TopBar?.ReadCount ?? 0);
                _classItems = _vm.LoadClassList();
                _classDisplayItems.Clear();
                foreach (var item in _classItems) _classDisplayItems.Add(item.name);
                BlockSizeBox.Value = _vm.BlockSize;
                N1BlockSizeBox.Value = _vm.N1BlockSize;
            }
            catch (Exception ex)
            {
                Log.ErrorF("SkillAssignmentClassCSkillSysView.LoadClassList failed: {0}", ex.Message);
            }
        }

        void InitializeReadConfig()
        {
            // Seed the boxes once on Initialize so the user sees the VM defaults,
            // but subsequent reloads READ from the boxes (not the VM).
            // #743: unified top-bar surfaces ReadStart / ReadCount via CLR properties.
            if (TopBar != null)
            {
                TopBar.ReadStartAddress = _vm.ReadStartAddress;
                TopBar.ReadCount = (int)_vm.ReadCount;
            }
            N1ReadCountBox.Value = _vm.N1ReadCount;
        }

        void ReloadList_Click(object? sender, RoutedEventArgs e)
        {
            LoadClassList();
        }

        // #743: routed event from the unified EditorTopBarWithInputs Reload button.
        void OnTopBarReloadRequested(object? sender, RoutedEventArgs e) => LoadClassList();

        void OnClassSelected(AddrResult ar)
        {
            // Stale-selection guard: clear N1 selection so a Write_Click
            // can't land in the wrong row when the user switches class.
            _n1SelectedAddr = 0;
            try
            {
                _vm.SelectedClassIndex = ar.tag;
                _vm.LoadEntry(ar.addr);
                AddressBox.Value = ar.addr;
                SelectedAddressBox.Value = ar.addr;
                ClassSkillBox.Value = _vm.ClassSkill;
                UpdateSkillPreview(_vm.ClassSkill);

                // Resolve current level-up pointer for this class.
                ROM? rom = CoreState.ROM;
                if (rom != null)
                {
                    uint slot = rom.p32(SkillAssignmentClassCSkillSysViewModel.gpClassLevelUpSkillTable);
                    if (U.isSafetyOffset(slot))
                    {
                        uint slotAddr = slot + ar.tag * 4;
                        if (slotAddr + 4 <= (uint)rom.Data.Length)
                        {
                            uint ptr = rom.p32(slotAddr);
                            _vm.LevelUpAddr = ptr;
                            // Display GBA pointer form (0x08xxxxxx) to match WinForms
                            // SkillAssignmentClassCSkillSysForm.N1_AddressInput.Value,
                            // even though the internal walker uses the offset form.
                            N1LevelUpAddrBox.Value = U.toPointer(ptr);
                            LoadN1Sublist(ptr);
                            UpdateIndependencePanels(ar.tag, (uint)_classItems.Count);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.ErrorF("SkillAssignmentClassCSkillSysView.OnClassSelected failed: {0}", ex.Message);
            }
        }

        void LoadN1Sublist(uint addr)
        {
            try
            {
                _n1SelectedAddr = 0;
                _n1Items = new List<AddrResult>();
                _n1DisplayItems.Clear();
                if (addr == 0 || !U.isSafetyOffset(addr))
                {
                    ZeroPointerPanel.IsVisible = (ClassListBox.SelectedIndex > 0 && addr == 0);
                    return;
                }
                ZeroPointerPanel.IsVisible = false;
                _n1Items = _vm.LoadN1List(addr);
                foreach (var item in _n1Items) _n1DisplayItems.Add(item.name);
            }
            catch (Exception ex)
            {
                Log.ErrorF("SkillAssignmentClassCSkillSysView.LoadN1Sublist failed: {0}", ex.Message);
            }
        }

        void UpdateIndependencePanels(uint classid, uint classCount)
        {
            try
            {
                IndependencePanel.IsVisible = ClassListBox.SelectedIndex > 0
                    && _vm.IsShowIndependencePanel(classCount);
            }
            catch { IndependencePanel.IsVisible = false; }
        }

        void OnN1Selected(AddrResult ar)
        {
            _n1SelectedAddr = ar.addr;
            try
            {
                _vm.LoadN1Entry(ar.addr);
                N1AddressBox.Value = ar.addr;
                N1SelectedAddressBox.Value = ar.addr;
                N1B0Box.Value = _vm.N1Level;
                N1B1Box.Value = _vm.N1Skill;
                UpdateSkillPreviewN1(_vm.N1Skill);
            }
            catch (Exception ex)
            {
                Log.ErrorF("SkillAssignmentClassCSkillSysView.OnN1Selected failed: {0}", ex.Message);
            }
        }

        void OnN1ReloadList(object? sender, RoutedEventArgs e)
        {
            // Push the N1 read-count into the VM so its walker honors it.
            // Copilot CLI PR #552 review #2: previously this control was inert.
            //
            // The address box displays the GBA pointer form (0x08xxxxxx) per
            // WinForms convention; normalize back to the ROM offset before
            // walking the list so isSafetyOffset() and array indexing work.
            uint raw = (uint)(N1LevelUpAddrBox.Value ?? 0);
            uint addr = U.toOffset(raw);
            _vm.LevelUpAddr = addr;
            _vm.N1ReadCount = (uint)(N1ReadCountBox.Value ?? 0);
            LoadN1Sublist(addr);
        }

        void UpdateSkillPreview(uint skillId)
        {
            // Bind name + description + icon (Copilot CLI PR #552 review #3).
            // Resolvers are static on the VM so they share the same WF
            // (gpSkillInfos + 8 byte per entry) layout used by
            // SkillConfigCSkillSystem09xForm.GetSkillName / GetSkillDesc.
            ROM? rom = CoreState.ROM;
            SkillNameTextBox.Text = SkillAssignmentClassCSkillSysViewModel.ResolveSkillName(rom, skillId);
            SkillTextTextBox.Text = SkillAssignmentClassCSkillSysViewModel.ResolveSkillDescription(rom, skillId);
            UpdateSkillIcon(SkillIconImage, ref _classSkillIconBitmap, skillId);
        }

        void UpdateSkillPreviewN1(uint skillId)
        {
            ROM? rom = CoreState.ROM;
            N1SkillNameTextBox.Text = SkillAssignmentClassCSkillSysViewModel.ResolveSkillName(rom, skillId);
            N1SkillTextTextBox.Text = SkillAssignmentClassCSkillSysViewModel.ResolveSkillDescription(rom, skillId);
            UpdateSkillIcon(N1SkillIconImage, ref _n1SkillIconBitmap, skillId);
        }

        // Tracked Avalonia Bitmap handles for the two skill-icon previews so
        // we can Dispose them on the next swap and on window close.
        global::Avalonia.Media.Imaging.Bitmap? _classSkillIconBitmap;
        global::Avalonia.Media.Imaging.Bitmap? _n1SkillIconBitmap;

        void UpdateSkillIcon(global::Avalonia.Controls.Image target,
            ref global::Avalonia.Media.Imaging.Bitmap? current, uint skillId)
        {
            ROM? rom = CoreState.ROM;
            // Skill-info entry +0 is a GBA pointer (high bit set) to the
            // 4bpp tile data for that skill icon. The id-based offset is
            // implicit (entry size = 8). We hand the raw GBA pointer
            // straight to PreviewIconHelper.LoadCSkillSysIcon — same path
            // used by SkillConfigFE8UCSkillSys09xView.
            uint iconGbaPtr = SkillAssignmentClassCSkillSysViewModel.ResolveSkillIconGbaPointer(rom, skillId);
            global::Avalonia.Media.Imaging.Bitmap? bmp = null;
            if (iconGbaPtr != 0 && rom != null)
            {
                try
                {
                    using var img = PreviewIconHelper.LoadCSkillSysIcon(iconGbaPtr);
                    if (img != null)
                        bmp = ImageConversionHelper.ToAvaloniaBitmap(img);
                }
                catch { bmp = null; }
            }
            if (current != null && !ReferenceEquals(current, bmp))
            {
                try { current.Dispose(); } catch { /* swallow */ }
            }
            current = bmp;
            target.Source = bmp;
        }

        // -----------------------------------------------------------------
        // Write handlers — wrapped in UndoService scope. These are the
        // names UndoCoverageScanner cross-references against
        // SkillAssignmentClassCSkillSysViewModel.{WriteClassSkill,
        // WriteN1Entry, MakeIndependent, ExpandN1List}.
        // -----------------------------------------------------------------

        void OnWrite(object? sender, RoutedEventArgs e)
        {
            if (!_vm.IsLoaded || _vm.CurrentAddr == 0) return;
            _vm.ClassSkill = (uint)(ClassSkillBox.Value ?? 0);
            _undoService.Begin("Edit Skill Assignment Class CSkillSys");
            try
            {
                _vm.WriteClassSkill();
                _undoService.Commit();
                _vm.MarkClean();
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.ErrorF("SkillAssignmentClassCSkillSysView.OnWrite failed: {0}", ex.Message);
            }
        }

        void OnN1Write(object? sender, RoutedEventArgs e)
        {
            if (_n1SelectedAddr == 0) return;
            _vm.N1Level = (uint)(N1B0Box.Value ?? 0);
            _vm.N1Skill = (uint)(N1B1Box.Value ?? 0);
            _undoService.Begin("Edit Skill Assignment Class CSkillSys Level-up Skill");
            try
            {
                _vm.WriteN1Entry();
                _undoService.Commit();
                _vm.MarkClean();
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.ErrorF("SkillAssignmentClassCSkillSysView.OnN1Write failed: {0}", ex.Message);
            }
        }

        void OnIndependence(object? sender, RoutedEventArgs e)
        {
            _undoService.Begin("Skill Assignment Class CSkillSys Make Independent");
            try
            {
                uint newBase = _vm.MakeIndependent();
                _undoService.Commit();
                if (newBase != 0)
                {
                    // Display the GBA pointer form (0x08xxxxxx) for consistency
                    // with the initial-selection / Reload normalization; keep
                    // the offset form for the internal walker.
                    N1LevelUpAddrBox.Value = U.toPointer(newBase);
                    LoadN1Sublist(newBase);
                }
                _vm.MarkClean();
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.ErrorF("SkillAssignmentClassCSkillSysView.OnIndependence failed: {0}", ex.Message);
            }
        }

        void OnN1Expand(object? sender, RoutedEventArgs e)
        {
            // Expand by one row beyond current count.
            uint newCount = (uint)(_n1Items.Count + 1);
            _undoService.Begin("Skill Assignment Class CSkillSys Expand Level-up List");
            try
            {
                uint newBase = _vm.ExpandN1List(newCount);
                _undoService.Commit();
                if (newBase != 0)
                {
                    // Display the GBA pointer form (0x08xxxxxx) for consistency
                    // with the initial-selection / Reload normalization; keep
                    // the offset form for the internal walker.
                    N1LevelUpAddrBox.Value = U.toPointer(newBase);
                    LoadN1Sublist(newBase);
                }
                _vm.MarkClean();
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.ErrorF("SkillAssignmentClassCSkillSysView.OnN1Expand failed: {0}", ex.Message);
            }
        }

        /// <summary>
        /// "Expand List" handler for the master class list. WinForms allows
        /// expanding the class table itself; here we route to the user feedback
        /// banner because table-resize on the class table is a Patch Manager
        /// operation (CSkillSys 3.00 ships with a fixed class count). We log
        /// + show a non-modal status update so the button has visible feedback
        /// instead of being silently inert (Copilot bot review #5/#6).
        /// </summary>
        void OnClassExpand(object? sender, RoutedEventArgs e)
        {
            Log.Notify(
                "SkillAssignmentClassCSkillSysView.OnClassExpand: class-table "
                + "size is fixed by CSkillSys 3.00 patch. To grow the class "
                + "table, use Patch Manager -> CSkillSys 3.00 class-count "
                + "param (mirrors WF SkillAssignmentClassCSkillSysForm stub).");
        }

        // -----------------------------------------------------------------
        // Level packing UI (X_LV add panel) — checkboxes + value spinner
        // sync with the B0 byte. No writes here; the OnN1Write button
        // commits.
        // -----------------------------------------------------------------

        bool _suppressLvChange;

        void OnLevelChanged()
        {
            if (_suppressLvChange) return;
            uint lv = (uint)(N1B0Box.Value ?? 0);
            _suppressLvChange = true;
            try
            {
                if (!_vm.IsClassSkillExtendsActive)
                {
                    XLevelAddPanel.IsVisible = false;
                    XLv255Panel.IsVisible = (lv == 0xFF);
                    return;
                }
                if (lv == 0xFF)
                {
                    XLevelAddPanel.IsVisible = false;
                    XLv255Panel.IsVisible = true;
                }
                else
                {
                    XLevelAddPanel.IsVisible = true;
                    XLv255Panel.IsVisible = false;
                    uint trueLevel = lv & 0x1F;
                    XLvValueBox.Value = trueLevel;
                    XLvPlayerOnlyCheckBox.IsChecked = (lv & 32) == 32;
                    XLvEnemyOnlyCheckBox.IsChecked = (lv & 64) == 64;
                    XLvNormalHardCheckBox.IsChecked = (lv & 96) == 96;
                    XLvHardOnlyCheckBox.IsChecked = (lv & 128) == 128;
                }
            }
            finally { _suppressLvChange = false; }
        }

        void OnXLevelValueChanged()
        {
            if (_suppressLvChange) return;
            _suppressLvChange = true;
            try
            {
                uint lv = (uint)(N1B0Box.Value ?? 0);
                uint trueLevel = (uint)(XLvValueBox.Value ?? 0);
                lv = lv - (lv & 0x1F);
                lv += (trueLevel & 0x1F);
                N1B0Box.Value = lv;
            }
            finally { _suppressLvChange = false; }
        }

        void OnLevelModeCheckboxChanged(uint bit)
        {
            if (_suppressLvChange) return;
            _suppressLvChange = true;
            try
            {
                uint lv = (uint)(N1B0Box.Value ?? 0);
                bool isChecked = bit switch
                {
                    32 => XLvPlayerOnlyCheckBox.IsChecked == true,
                    64 => XLvEnemyOnlyCheckBox.IsChecked == true,
                    96 => XLvNormalHardCheckBox.IsChecked == true,
                    128 => XLvHardOnlyCheckBox.IsChecked == true,
                    _ => false,
                };
                if (isChecked) lv |= bit; else lv &= ~bit;
                N1B0Box.Value = lv;
            }
            finally { _suppressLvChange = false; }
        }

        // -----------------------------------------------------------------
        // #1011: Bulk Export/Import are DISABLED in the view (WF stubs them —
        // ExportAllData/ImportAllData are empty). The buttons carry no Click
        // handler now; they are greyed out with an explanatory tooltip on the
        // wrapping StackPanel rather than advertising a silent no-op.
        // -----------------------------------------------------------------

        void OnLearnInfo(object? sender, RoutedEventArgs e)
        {
            const string url = "https://laqieer.github.io/dw.ngmansion.xyz/wiki/en/guide_febuildergba_learnskillinfo.html";
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Log.ErrorF("SkillAssignmentClassCSkillSysView.OnLearnInfo failed: {0}", ex.Message);
            }
        }

        public void NavigateTo(uint address) { }
        public void SelectFirstItem()
        {
            if (_classDisplayItems.Count > 0)
            {
                ClassListBox.SelectedIndex = 0;
            }
        }
    }
}
