// SPDX-License-Identifier: GPL-3.0-or-later
// Code-behind for SkillAssignmentUnitCSkillSysView. Rebuilt for gap-sweep
// #1451 from an inert placeholder to a three-pane master-detail editor
// matching WinForms SkillAssignmentUnitCSkillSysForm. The VM is the
// consolidated SkillAssignmentUnitCSkillSysViewModel (post-#1451 — the legacy
// ...ViewViewModel.cs stub was removed). The XView -> XViewModel name pairing
// is what enables UndoCoverageScanner's View-to-VM upgrade pass to mark the
// in-VM write callsites as Covered.
//
// This is the UNIT sibling of SkillAssignmentClassCSkillSysView (#415). The
// Unit WinForms form has no X_LV level-breakdown panel (Class-only), but it
// DOES expose a per-unit level-up pointer (X_LevelUpAddr) which the master
// Write commits alongside W0 — see OnWrite.
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
    public partial class SkillAssignmentUnitCSkillSysView : TranslatedWindow, IEditorView, IDataVerifiableView
    {
        readonly SkillAssignmentUnitCSkillSysViewModel _vm = new();
        readonly UndoService _undoService = new();

        // Track currently selected N1 row address. Reset on unit change.
        uint _n1SelectedAddr;

        // Parallel display-string collections so AddrResult.name is visible in
        // the ListBox even though Avalonia bindings require properties.
        readonly ObservableCollection<string> _unitDisplayItems = new();
        readonly ObservableCollection<string> _n1DisplayItems = new();
        List<AddrResult> _unitItems = new();
        List<AddrResult> _n1Items = new();

        public string ViewTitle => "Skill Assignment - Unit (CSkillSys)";
        public bool IsLoaded => _vm.IsLoaded;
        public ViewModelBase? DataViewModel => _vm;

        public SkillAssignmentUnitCSkillSysView()
        {
            InitializeComponent();
            // Bind DataContext so AXAML {Binding HasLevelUpTable} resolves.
            DataContext = _vm;
            UnitListBox.ItemsSource = _unitDisplayItems;
            N1ListBox.ItemsSource = _n1DisplayItems;
            UnitListBox.SelectionChanged += (s, e) =>
            {
                int idx = UnitListBox.SelectedIndex;
                if (idx >= 0 && idx < _unitItems.Count) OnUnitSelected(_unitItems[idx]);
            };
            N1ListBox.SelectionChanged += (s, e) =>
            {
                int idx = N1ListBox.SelectedIndex;
                if (idx >= 0 && idx < _n1Items.Count) OnN1Selected(_n1Items[idx]);
            };
            Opened += (_, _) => Initialize();
            Closed += (_, _) =>
            {
                if (_unitSkillIconBitmap != null) try { _unitSkillIconBitmap.Dispose(); } catch { /* swallow */ }
                if (_n1SkillIconBitmap != null) try { _n1SkillIconBitmap.Dispose(); } catch { /* swallow */ }
                _unitSkillIconBitmap = null;
                _n1SkillIconBitmap = null;
            };
        }

        void Initialize()
        {
            try
            {
                _vm.RefreshPatchState();
                UpdateBannerVisibility();
                InitializeReadConfig();
                LoadUnitList();
            }
            catch (Exception ex)
            {
                Log.Error($"SkillAssignmentUnitCSkillSysView.Initialize failed: {ex}");
            }
            finally { _vm.IsLoaded = true; }
        }

        void UpdateBannerVisibility()
        {
            // Banner shows only when no CSkillSys patch is detected. When the
            // patch is installed the editor is fully functional and the banner
            // is hidden (mirrors the Class variant's CSkillSys gating).
            PatchBanner.IsVisible = !_vm.IsCSkillSysActive;
        }

        void LoadUnitList()
        {
            try
            {
                // Push read-config UI values into the VM so the user's
                // ReadStartAddress / ReadCount overrides drive the list walk.
                // A 0/unset ReadCount means the FULL unit_maxcount.
                _vm.ReadStartAddress = TopBar?.ReadStartAddress ?? 0u;
                _vm.ReadCount = (uint)(TopBar?.ReadCount ?? 0);
                _unitItems = _vm.LoadUnitList();
                _unitDisplayItems.Clear();
                foreach (var item in _unitItems) _unitDisplayItems.Add(item.name);
                BlockSizeBox.Value = _vm.BlockSize;
                N1BlockSizeBox.Value = _vm.N1BlockSize;
            }
            catch (Exception ex)
            {
                Log.Error($"SkillAssignmentUnitCSkillSysView.LoadUnitList failed: {ex}");
            }
        }

        void InitializeReadConfig()
        {
            if (TopBar != null)
            {
                TopBar.ReadStartAddress = _vm.ReadStartAddress;
                TopBar.ReadCount = (int)_vm.ReadCount;
            }
            N1ReadCountBox.Value = _vm.N1ReadCount;
        }

        // #743: routed event from the unified EditorTopBarWithInputs Reload button.
        void OnTopBarReloadRequested(object? sender, RoutedEventArgs e) => LoadUnitList();

        void OnUnitSelected(AddrResult ar)
        {
            // Stale-selection guard: clear N1 selection so a Write_Click can't
            // land in the wrong row when the user switches unit.
            _n1SelectedAddr = 0;
            try
            {
                _vm.SelectedUnitIndex = ar.tag;
                _vm.LoadEntry(ar.addr);
                AddressBox.Value = ar.addr;
                SelectedAddressBox.Value = ar.addr;
                UnitSkillBox.Value = _vm.UnitSkill;
                UpdateSkillPreview(_vm.UnitSkill);

                // Resolve / display the per-unit level-up pointer (GBA form).
                N1LevelUpAddrBox.Value = _vm.XLevelUpAddr;
                LoadN1Sublist(_vm.LevelUpAddr);
                // Use the REAL unit count (unit_maxcount) for share detection, not
                // the loaded-subset count: a user ReadCount override can cap
                // _unitItems below the full table and otherwise hide a genuine
                // shared-pointer warning for units outside the loaded subset.
                uint shareScanCount = CoreState.ROM?.RomInfo?.unit_maxcount ?? (uint)_unitItems.Count;
                if (shareScanCount < (uint)_unitItems.Count) shareScanCount = (uint)_unitItems.Count;
                UpdateIndependencePanels(shareScanCount);
            }
            catch (Exception ex)
            {
                Log.Error($"SkillAssignmentUnitCSkillSysView.OnUnitSelected failed: {ex}");
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
                    // Zero-pointer panel: show for non-sentinel units (index>0)
                    // with an unallocated (0) pointer.
                    ZeroPointerPanel.IsVisible = (UnitListBox.SelectedIndex > 0 && addr == 0);
                    return;
                }
                ZeroPointerPanel.IsVisible = false;
                _n1Items = _vm.LoadN1List(addr);
                foreach (var item in _n1Items) _n1DisplayItems.Add(item.name);
            }
            catch (Exception ex)
            {
                Log.Error($"SkillAssignmentUnitCSkillSysView.LoadN1Sublist failed: {ex}");
            }
        }

        void UpdateIndependencePanels(uint unitCount)
        {
            try
            {
                IndependencePanel.IsVisible = UnitListBox.SelectedIndex > 0
                    && _vm.IsShowIndependencePanel(unitCount);
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
                Log.Error($"SkillAssignmentUnitCSkillSysView.OnN1Selected failed: {ex}");
            }
        }

        void OnN1ReloadList(object? sender, RoutedEventArgs e)
        {
            // The address box displays the GBA pointer form (0x08xxxxxx);
            // normalize to a ROM offset before walking the list.
            uint raw = (uint)(N1LevelUpAddrBox.Value ?? 0);
            uint addr = U.toOffset(raw);
            _vm.LevelUpAddr = addr;
            _vm.XLevelUpAddr = raw;
            _vm.N1ReadCount = (uint)(N1ReadCountBox.Value ?? 0);
            LoadN1Sublist(addr);
        }

        void UpdateSkillPreview(uint skillId)
        {
            ROM? rom = CoreState.ROM;
            SkillNameTextBox.Text = SkillAssignmentUnitCSkillSysViewModel.ResolveSkillName(rom, skillId);
            SkillTextTextBox.Text = SkillAssignmentUnitCSkillSysViewModel.ResolveSkillDescription(rom, skillId);
            UpdateSkillIcon(SkillIconImage, ref _unitSkillIconBitmap, skillId);
        }

        void UpdateSkillPreviewN1(uint skillId)
        {
            ROM? rom = CoreState.ROM;
            N1SkillNameTextBox.Text = SkillAssignmentUnitCSkillSysViewModel.ResolveSkillName(rom, skillId);
            N1SkillTextTextBox.Text = SkillAssignmentUnitCSkillSysViewModel.ResolveSkillDescription(rom, skillId);
            UpdateSkillIcon(N1SkillIconImage, ref _n1SkillIconBitmap, skillId);
        }

        global::Avalonia.Media.Imaging.Bitmap? _unitSkillIconBitmap;
        global::Avalonia.Media.Imaging.Bitmap? _n1SkillIconBitmap;

        void UpdateSkillIcon(global::Avalonia.Controls.Image target,
            ref global::Avalonia.Media.Imaging.Bitmap? current, uint skillId)
        {
            ROM? rom = CoreState.ROM;
            uint iconGbaPtr = SkillAssignmentUnitCSkillSysViewModel.ResolveSkillIconGbaPointer(rom, skillId);
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
        // Write handlers — wrapped in UndoService scope. These are the names
        // UndoCoverageScanner cross-references against
        // SkillAssignmentUnitCSkillSysViewModel.{WriteUnitSkill,
        // WriteLevelUpPointer, WriteN1Entry, MakeIndependent, ExpandN1List}.
        // -----------------------------------------------------------------

        void OnWrite(object? sender, RoutedEventArgs e)
        {
            if (!_vm.IsLoaded || _vm.CurrentAddr == 0) return;
            _vm.UnitSkill = (uint)(UnitSkillBox.Value ?? 0);
            // The per-unit level-up pointer is committed alongside W0 (mirrors
            // WF WriteButton_Click writing X_LevelUpAddr back to the table).
            _vm.XLevelUpAddr = (uint)(N1LevelUpAddrBox.Value ?? 0);
            _undoService.Begin("Edit Skill Assignment Unit CSkillSys");
            try
            {
                _vm.WriteUnitSkill();
                _vm.WriteLevelUpPointer();
                _undoService.Commit();
                _vm.MarkClean();
                UpdateSkillPreview(_vm.UnitSkill);
                // The level-up pointer may have changed: reload the N1 sub-list /
                // zero-pointer panel from the (normalized) pointer so the display
                // isn't stale until the unit is reselected.
                N1LevelUpAddrBox.Value = _vm.XLevelUpAddr;
                LoadN1Sublist(_vm.LevelUpAddr);
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.Error($"SkillAssignmentUnitCSkillSysView.OnWrite failed: {ex}");
            }
        }

        void OnN1Write(object? sender, RoutedEventArgs e)
        {
            if (_n1SelectedAddr == 0) return;
            _vm.N1Level = (uint)(N1B0Box.Value ?? 0);
            _vm.N1Skill = (uint)(N1B1Box.Value ?? 0);
            _undoService.Begin("Edit Skill Assignment Unit CSkillSys Level-up Skill");
            try
            {
                _vm.WriteN1Entry();
                _undoService.Commit();
                _vm.MarkClean();
                // The N1 row label is derived from the ROM {level, skill} bytes,
                // so refresh the just-edited row's label + the right-side preview
                // in place (otherwise the list looks like the write didn't apply
                // until the user reloads/reselects).
                int sel = N1ListBox.SelectedIndex;
                if (sel >= 0 && sel < _n1Items.Count)
                {
                    string newLabel = U.ToHexString(_vm.N1Skill & 0xFF) + " (Lv" + (_vm.N1Level & 0xFF) + ")";
                    _n1Items[sel] = new AddrResult(_n1Items[sel].addr, newLabel, _n1Items[sel].tag);
                    _n1DisplayItems[sel] = newLabel;
                }
                UpdateSkillPreviewN1(_vm.N1Skill);
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.Error($"SkillAssignmentUnitCSkillSysView.OnN1Write failed: {ex}");
            }
        }

        void OnIndependence(object? sender, RoutedEventArgs e)
        {
            _undoService.Begin("Skill Assignment Unit CSkillSys Make Independent");
            try
            {
                uint newBase = _vm.MakeIndependent();
                _undoService.Commit();
                if (newBase != 0)
                {
                    N1LevelUpAddrBox.Value = U.toPointer(newBase);
                    LoadN1Sublist(newBase);
                }
                _vm.MarkClean();
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.Error($"SkillAssignmentUnitCSkillSysView.OnIndependence failed: {ex}");
            }
        }

        void OnN1Expand(object? sender, RoutedEventArgs e)
        {
            // Grow by one beyond the REAL (uncapped) row count, not the loaded
            // subset: a user N1 read-count cap can make _n1Items.Count smaller
            // than the on-ROM list, which would make ExpandN1List a silent no-op
            // (newCount <= oldCount) even though the user clicked Expand.
            uint realCount = _vm.CountN1Rows();
            uint baseCount = Math.Max(realCount, (uint)_n1Items.Count);
            uint newCount = baseCount + 1;
            _undoService.Begin("Skill Assignment Unit CSkillSys Expand Level-up List");
            try
            {
                uint newBase = _vm.ExpandN1List(newCount);
                _undoService.Commit();
                if (newBase != 0)
                {
                    N1LevelUpAddrBox.Value = U.toPointer(newBase);
                    LoadN1Sublist(newBase);
                }
                _vm.MarkClean();
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.Error($"SkillAssignmentUnitCSkillSysView.OnN1Expand failed: {ex}");
            }
        }

        /// <summary>
        /// "Expand List" handler for the master unit list. The CSkillSys per-unit
        /// table size is fixed by the patch; we route to the user feedback log so
        /// the button has visible feedback instead of being silently inert
        /// (mirrors the Class variant's OnClassExpand).
        /// </summary>
        void OnUnitExpand(object? sender, RoutedEventArgs e)
        {
            Log.Notify(
                "SkillAssignmentUnitCSkillSysView.OnUnitExpand: per-unit skill-table "
                + "size is fixed by the CSkillSys patch. To grow the unit table, use "
                + "the Patch Manager (mirrors WF SkillAssignmentUnitCSkillSysForm stub).");
        }

        void OnLearnInfo(object? sender, RoutedEventArgs e)
        {
            const string url = "https://dw.ngmansion.xyz/doku.php?id=en:guide_febuildergba_learnskillinfo";
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Log.Error($"SkillAssignmentUnitCSkillSysView.OnLearnInfo failed: {ex}");
            }
        }

        public void NavigateTo(uint address)
        {
            // Select the unit row whose master entry addr matches.
            for (int i = 0; i < _unitItems.Count; i++)
            {
                if (_unitItems[i].addr == address)
                {
                    UnitListBox.SelectedIndex = i;
                    return;
                }
            }
        }

        public void SelectFirstItem()
        {
            if (_unitDisplayItems.Count > 0)
            {
                UnitListBox.SelectedIndex = 0;
            }
        }
    }
}
