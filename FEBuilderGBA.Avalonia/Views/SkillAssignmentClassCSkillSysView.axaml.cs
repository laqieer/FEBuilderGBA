// SPDX-License-Identifier: GPL-3.0-or-later
// Code-behind for SkillAssignmentClassCSkillSysView. Rebuilt for gap-sweep
// #415 to a three-pane master-detail layout matching WinForms
// SkillAssignmentClassCSkillSysForm. The VM is the consolidated
// SkillAssignmentClassCSkillSysViewModel (post-#415 — the legacy
// ...ViewViewModel.cs stub was removed). The XView -> XViewModel name
// pairing is what enables UndoCoverageScanner's View-to-VM upgrade pass to
// mark the in-VM write callsites as Covered.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class SkillAssignmentClassCSkillSysView : TranslatedWindow, IEditorView, IDataVerifiableView
    {
        readonly SkillAssignmentClassCSkillSysViewModel _vm = new();
        readonly UndoService _undoService = new();

        // Track currently selected N1 row address. Reset on class change
        // (Copilot-flagged stale-selection guard from PR #544).
        uint _n1SelectedAddr;

        public string ViewTitle => "Skill Assignment - Class (CSkillSys)";
        public bool IsLoaded => _vm.IsLoaded;
        public ViewModelBase? DataViewModel => _vm;

        public SkillAssignmentClassCSkillSysView()
        {
            InitializeComponent();
            ClassListBox.SelectionChanged += (s, e) =>
            {
                if (ClassListBox.SelectedItem is AddrResult ar) OnClassSelected(ar);
            };
            N1ListBox.SelectionChanged += (s, e) =>
            {
                if (N1ListBox.SelectedItem is AddrResult ar) OnN1Selected(ar);
            };
            Opened += (_, _) => Initialize();
            N1B0Box.ValueChanged += (s, e) => OnLevelChanged();
            XLvValueBox.ValueChanged += (s, e) => OnXLevelValueChanged();
            XLvPlayerOnlyCheckBox.IsCheckedChanged += (s, e) => OnLevelModeCheckboxChanged(32);
            XLvEnemyOnlyCheckBox.IsCheckedChanged += (s, e) => OnLevelModeCheckboxChanged(64);
            XLvNormalHardCheckBox.IsCheckedChanged += (s, e) => OnLevelModeCheckboxChanged(96);
            XLvHardOnlyCheckBox.IsCheckedChanged += (s, e) => OnLevelModeCheckboxChanged(128);
        }

        void Initialize()
        {
            try
            {
                _vm.RefreshPatchState();
                UpdateBannerVisibility();
                LoadClassList();
            }
            catch (Exception ex)
            {
                Log.Error("SkillAssignmentClassCSkillSysView.Initialize failed: {0}", ex.Message);
            }
            finally { _vm.IsLoaded = true; }
        }

        void UpdateBannerVisibility()
        {
            // Banner is always visible — gates the form to CSkillSys300.
            PatchBanner.IsVisible = !_vm.IsCSkillSys300Active;
            XLevelAddPanel.IsVisible = false; // populated lazily on selection
        }

        void LoadClassList()
        {
            try
            {
                var items = _vm.LoadClassList();
                ClassListBox.ItemsSource = items;
                ReadStartAddressBox.Value = _vm.ReadStartAddress;
                ReadCountBox.Value = _vm.ReadCount;
                BlockSizeBox.Value = _vm.BlockSize;
                N1BlockSizeBox.Value = _vm.N1BlockSize;
                N1ReadCountBox.Value = _vm.N1ReadCount;
            }
            catch (Exception ex)
            {
                Log.Error("SkillAssignmentClassCSkillSysView.LoadClassList failed: {0}", ex.Message);
            }
        }

        void ReloadList_Click(object? sender, RoutedEventArgs e)
        {
            LoadClassList();
        }

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
                            N1LevelUpAddrBox.Value = ptr;
                            LoadN1Sublist(ptr);
                            UpdateIndependencePanels(ar.tag, (uint)ClassListBox.ItemCount);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error("SkillAssignmentClassCSkillSysView.OnClassSelected failed: {0}", ex.Message);
            }
        }

        void LoadN1Sublist(uint addr)
        {
            try
            {
                _n1SelectedAddr = 0;
                if (addr == 0 || !U.isSafetyOffset(addr))
                {
                    N1ListBox.ItemsSource = new List<AddrResult>();
                    ZeroPointerPanel.IsVisible = (ClassListBox.SelectedIndex > 0 && addr == 0);
                    return;
                }
                ZeroPointerPanel.IsVisible = false;
                var items = _vm.LoadN1List(addr);
                N1ListBox.ItemsSource = items;
            }
            catch (Exception ex)
            {
                Log.Error("SkillAssignmentClassCSkillSysView.LoadN1Sublist failed: {0}", ex.Message);
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
                Log.Error("SkillAssignmentClassCSkillSysView.OnN1Selected failed: {0}", ex.Message);
            }
        }

        void OnN1ReloadList(object? sender, RoutedEventArgs e)
        {
            uint addr = (uint)(N1LevelUpAddrBox.Value ?? 0);
            _vm.LevelUpAddr = addr;
            LoadN1Sublist(addr);
        }

        void UpdateSkillPreview(uint skillId)
        {
            try { SkillNameTextBox.Text = NameResolver.GetSkillName(skillId); }
            catch { SkillNameTextBox.Text = string.Empty; }
            // Skill text comes from CSkillSys data; not extracted to Core yet
            // (parity with #500 deferral) — leave blank for now.
            SkillTextTextBox.Text = "";
        }

        void UpdateSkillPreviewN1(uint skillId)
        {
            try { N1SkillNameTextBox.Text = NameResolver.GetSkillName(skillId); }
            catch { N1SkillNameTextBox.Text = string.Empty; }
            N1SkillTextTextBox.Text = "";
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
                Log.Error("SkillAssignmentClassCSkillSysView.OnWrite failed: {0}", ex.Message);
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
                Log.Error("SkillAssignmentClassCSkillSysView.OnN1Write failed: {0}", ex.Message);
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
                    N1LevelUpAddrBox.Value = newBase;
                    LoadN1Sublist(newBase);
                }
                _vm.MarkClean();
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.Error("SkillAssignmentClassCSkillSysView.OnIndependence failed: {0}", ex.Message);
            }
        }

        void OnN1Expand(object? sender, RoutedEventArgs e)
        {
            // Expand by one row beyond current count.
            uint newCount = (uint)((N1ListBox.Items?.Count ?? 0) + 1);
            _undoService.Begin("Skill Assignment Class CSkillSys Expand Level-up List");
            try
            {
                uint newBase = _vm.ExpandN1List(newCount);
                _undoService.Commit();
                if (newBase != 0)
                {
                    N1LevelUpAddrBox.Value = newBase;
                    LoadN1Sublist(newBase);
                }
                _vm.MarkClean();
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.Error("SkillAssignmentClassCSkillSysView.OnN1Expand failed: {0}", ex.Message);
            }
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
        // No-op stubs (mirror WF — ExportAll/ImportAll are empty in WF).
        // -----------------------------------------------------------------

        void OnExportAll(object? sender, RoutedEventArgs e)
        {
            Log.Notify("SkillAssignmentClassCSkillSysView.OnExportAll: not implemented (mirrors WF stub).");
        }

        void OnImportAll(object? sender, RoutedEventArgs e)
        {
            Log.Notify("SkillAssignmentClassCSkillSysView.OnImportAll: not implemented (mirrors WF stub).");
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
                Log.Error("SkillAssignmentClassCSkillSysView.OnLearnInfo failed: {0}", ex.Message);
            }
        }

        public void NavigateTo(uint address) { }
        public void SelectFirstItem()
        {
            if (ClassListBox.Items != null && ClassListBox.Items.Count > 0)
            {
                ClassListBox.SelectedIndex = 0;
            }
        }
    }
}
