// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using global::Avalonia.Media.Imaging;
using global::Avalonia.Platform.Storage;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    /// <summary>
    /// Avalonia counterpart of WinForms SkillAssignmentClassSkillSystemForm.
    /// Gap-sweep #416 raises the AXAML control surface to MEDIUM-verdict
    /// density. Master + N1 ROM writes are wrapped in View-owned UndoService
    /// scopes. List expand for the N1 sub-list handles both first-allocation
    /// (pointer == 0) and growth (pointer > 0).
    /// </summary>
    public partial class SkillAssignmentClassSkillSystemView : TranslatedWindow, IEditorView, IDataVerifiableView
    {
        readonly SkillAssignmentClassSkillSystemViewModel _vm = new();
        readonly UndoService _undoService = new();
        Bitmap _classIconBitmap;
        Bitmap _n1IconBitmap;
        bool _suppressN1B0Change;
        bool _suppressDifficultyChange;
        bool _suppressLevelChange;

        public string ViewTitle => "Skill Assignment (Class)";
        public bool IsLoaded => _vm.IsLoaded;
        public ViewModelBase DataViewModel => _vm;

        public SkillAssignmentClassSkillSystemView()
        {
            InitializeComponent();
            // Bind DataContext so AXAML {Binding LevelUpEntries} resolves
            // (Copilot bot review on PR #555).
            DataContext = _vm;
            EntryList.SelectedAddressChanged += OnSelected;
            Opened += (_, _) => LoadList();
            Closed += (_, _) =>
            {
                DisposeBitmap(ref _classIconBitmap);
                DisposeBitmap(ref _n1IconBitmap);
            };
        }

        static void DisposeBitmap(ref Bitmap bmp)
        {
            if (bmp == null) return;
            try { bmp.Dispose(); } catch (System.Exception ex) { Log.DebugF("SkillAssignmentClassSkillSystemView dispose bmp: {0}", ex.Message); }
            bmp = null;
        }

        void LoadList()
        {
            _vm.IsLoading = true;
            try
            {
                var items = _vm.LoadList();
                // Use ClassIconLoader (one wait-icon per class index) to mirror the WF AddressList
                // OwnerDraw=DrawClassAndText (Copilot bot review on PR #555).
                EntryList.SetItemsWithIcons(items, i => ListIconLoaders.ClassIconLoader(items, i));
                // #743: unified top-bar surfaces ReadStart / ReadCount via CLR properties.
                if (TopBar != null)
                {
                    TopBar.ReadStartAddress = _vm.ReadStartAddress;
                    TopBar.ReadCount = (int)_vm.ReadCount;
                }
                UpdateMasterPanelVisibility();
            }
            catch (Exception ex)
            {
                Log.ErrorF("SkillAssignmentClassSkillSystemView.LoadList failed: {0}", ex.Message);
            }
            finally { _vm.IsLoading = false; _vm.MarkClean(); }
        }

        void ReloadList_Click(object sender, RoutedEventArgs e) => LoadList();

        // #743: routed event from the unified EditorTopBarWithInputs Reload button.
        void OnTopBarReloadRequested(object? sender, RoutedEventArgs e) => LoadList();

        void OnSelected(uint addr)
        {
            _vm.IsLoading = true;
            try
            {
                _vm.LoadEntry(addr);
                UpdateUI();
                if (_vm.LevelUpEntries.Count > 0)
                {
                    N1EntryList.SelectedIndex = 0;
                }
            }
            catch (Exception ex)
            {
                Log.ErrorF("SkillAssignmentClassSkillSystemView.OnSelected failed: {0}", ex.Message);
            }
            finally { _vm.IsLoading = false; _vm.MarkClean(); }
        }

        void UpdateUI()
        {
            AddressBox.Value = _vm.CurrentAddr;
            BlockSizeBox.Value = _vm.MasterBlockSize;
            SelectedAddressLabel.Content = $"0x{_vm.CurrentAddr:X08}";

            ClassSkillBox.Value = _vm.ClassSkill;
            SkillNameBox.Text = NameResolver.GetSkillName(_vm.ClassSkill);
            SkillTextBox.Text = ResolveSkillText(_vm.ClassSkill);
            RefreshClassIcon();

            XLevelUpAddrBox.Value = _vm.XLevelUpAddr;
            N1ReadCountBox.Value = (uint)_vm.LevelUpEntries.Count;

            UpdateMasterPanelVisibility();
        }

        void UpdateMasterPanelVisibility()
        {
            ZeroPointerPanel.IsVisible = _vm.IsZeroPointer;
            if (string.IsNullOrEmpty(ZeroPointerText.Text))
            {
                ZeroPointerText.Text = R._("No level-up table is allocated for this class.\nPress List Expand to allocate one.");
            }
            IndependencePanel.IsVisible = _vm.IsIndependenceVisible;
            XLevelAddPanel.IsVisible = _vm.IsClassSkillExtendsActive && !_vm.IsLv255;
            Lv255Panel.IsVisible = _vm.IsLv255;
            // Set the LV255 info-text from code rather than AXAML so the
            // l10n scanner does not trip on a multi-line literal that would
            // not match the existing ja/zh translation entries (which key
            // by literal backslash-n sequences).
            if (_vm.IsLv255 && string.IsNullOrEmpty(Lv255InfoBox.Text))
            {
                Lv255InfoBox.Text = R._("Level 255 is set.\nIf promoted from a lower class to this one, the skill will NOT be granted.\nThe skill is granted only when LOADed directly as this class.");
            }
        }

        void RefreshClassIcon()
        {
            try
            {
                ROM rom = CoreState.ROM;
                if (rom != null && _vm.IconBaseAddress != 0 && _vm.ClassSkill > 0)
                {
                    using var img = PreviewIconHelper.LoadSkillIcon(_vm.ClassSkill, _vm.IconBaseAddress);
                    Bitmap bmp = img != null ? ImageConversionHelper.ToAvaloniaBitmap(img) : null;
                    SetClassIcon(bmp);
                }
                else
                {
                    SetClassIcon(null);
                }
            }
            catch { SetClassIcon(null); }
        }

        void SetClassIcon(Bitmap bmp)
        {
            if (_classIconBitmap != null && !ReferenceEquals(_classIconBitmap, bmp))
            {
                try { _classIconBitmap.Dispose(); } catch (System.Exception ex) { Log.DebugF("SkillAssignmentClassSkillSystemView dispose class icon: {0}", ex.Message); }
            }
            _classIconBitmap = bmp;
            SkillIconImage.Source = bmp;
        }

        void RefreshN1Icon()
        {
            try
            {
                ROM rom = CoreState.ROM;
                if (rom != null && _vm.IconBaseAddress != 0 && _vm.LevelUpSkill > 0)
                {
                    using var img = PreviewIconHelper.LoadSkillIcon(_vm.LevelUpSkill, _vm.IconBaseAddress);
                    Bitmap bmp = img != null ? ImageConversionHelper.ToAvaloniaBitmap(img) : null;
                    SetN1Icon(bmp);
                }
                else
                {
                    SetN1Icon(null);
                }
            }
            catch { SetN1Icon(null); }
        }

        void SetN1Icon(Bitmap bmp)
        {
            if (_n1IconBitmap != null && !ReferenceEquals(_n1IconBitmap, bmp))
            {
                try { _n1IconBitmap.Dispose(); } catch (System.Exception ex) { Log.DebugF("SkillAssignmentClassSkillSystemView dispose n1 icon: {0}", ex.Message); }
            }
            _n1IconBitmap = bmp;
            N1SkillIconImage.Source = bmp;
        }

        // Route through the VM so we use the SkillSystems text-pointer base
        // (textBase + 2 * skillId), mirroring WinForms GetSkillText. The old
        // implementation incorrectly used NameResolver.GetSkillName which
        // returned the skill *name* not its full description text
        // (Copilot CLI review on PR #555).
        string ResolveSkillText(uint skillId) => _vm.ResolveSkillTextById(skillId);

        void MasterWriteButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_vm.IsLoaded || _vm.CurrentAddr == 0) return;
            _undoService.Begin("Edit Skill Assignment (Class)");
            try
            {
                _vm.ClassSkill = (uint)(ClassSkillBox.Value ?? 0);
                _vm.XLevelUpAddr = (uint)(XLevelUpAddrBox.Value ?? 0);
                _vm.WriteMaster();
                _undoService.Commit();
                _vm.MarkClean();
                RefreshClassIcon();
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.ErrorF("SkillAssignmentClassSkillSystemView.MasterWrite failed: {0}", ex.Message);
            }
        }

        void N1EntryList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (N1EntryList.SelectedItem is not SkillAssignmentClassSkillSystemViewModel.LevelUpEntry entry) return;
            try
            {
                _vm.IsLoading = true;
                _vm.LoadLevelUpEntry(entry.Addr);
                UpdateN1UI();
            }
            catch (Exception ex)
            {
                Log.ErrorF("SkillAssignmentClassSkillSystemView.N1EntryList_SelectionChanged failed: {0}", ex.Message);
            }
            finally { _vm.IsLoading = false; }
        }

        void UpdateN1UI()
        {
            N1AddressBox.Value = _vm.SelectedLevelUpAddr;
            N1BlockSizeBox.Value = _vm.LevelUpBlockSize;
            N1SelectedAddressLabel.Content = $"0x{_vm.SelectedLevelUpAddr:X08}";

            _suppressN1B0Change = true;
            _suppressDifficultyChange = true;
            _suppressLevelChange = true;
            try
            {
                N1B0Box.Value = _vm.LevelUpRaw;
                N1B1Box.Value = _vm.LevelUpSkill;
                LevelValueBox.Value = _vm.LevelValue;
                PlayerOnlyCheckBox.IsChecked = _vm.IsPlayerOnly;
                EnemyOnlyCheckBox.IsChecked = _vm.IsEnemyOnly;
                NormalHardCheckBox.IsChecked = _vm.IsNormalHard;
                HardOnlyCheckBox.IsChecked = _vm.IsHardOnly;
            }
            finally
            {
                _suppressN1B0Change = false;
                _suppressDifficultyChange = false;
                _suppressLevelChange = false;
            }

            N1SkillNameBox.Text = NameResolver.GetSkillName(_vm.LevelUpSkill);
            N1SkillTextBox.Text = ResolveSkillText(_vm.LevelUpSkill);
            RefreshN1Icon();
            UpdateMasterPanelVisibility();
        }

        void N1WriteButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_vm.IsLoaded || _vm.SelectedLevelUpAddr == 0) return;
            _undoService.Begin("Edit Skill Assignment Level-up Entry");
            try
            {
                _vm.LevelUpRaw = (uint)(N1B0Box.Value ?? 0);
                _vm.LevelUpSkill = (uint)(N1B1Box.Value ?? 0);
                _vm.WriteLevelUp();
                _undoService.Commit();
                _vm.MarkClean();
                RefreshN1Icon();
                UpdateMasterPanelVisibility();
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.ErrorF("SkillAssignmentClassSkillSystemView.N1Write failed: {0}", ex.Message);
            }
        }

        void N1ReloadList_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                ROM rom = CoreState.ROM;
                if (rom == null) return;
                _vm.LoadLevelUpList(rom);
                N1ReadCountBox.Value = (uint)_vm.LevelUpEntries.Count;
                if (_vm.LevelUpEntries.Count > 0)
                {
                    N1EntryList.SelectedIndex = 0;
                }
            }
            catch (Exception ex)
            {
                Log.ErrorF("SkillAssignmentClassSkillSystemView.N1ReloadList failed: {0}", ex.Message);
            }
        }

        void N1ListExpand_Click(object sender, RoutedEventArgs e)
        {
            _undoService.Begin("Expand Skill Assignment Level-up Table");
            try
            {
                var result = _vm.ExpandLevelUpList();
                if (!result.Success)
                {
                    _undoService.Rollback();
                    CoreState.Services?.ShowError(result.Error ?? "Expand failed.");
                    return;
                }
                _undoService.Commit();
                N1ReadCountBox.Value = (uint)_vm.LevelUpEntries.Count;
                XLevelUpAddrBox.Value = _vm.XLevelUpAddr;
                UpdateMasterPanelVisibility();
                CoreState.Services?.ShowInfo($"Level-up table expanded. New base: 0x{result.NewBaseAddress:X08}, count: {result.NewDataCount}.");
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.ErrorF("SkillAssignmentClassSkillSystemView.N1ListExpand failed: {0}", ex.Message);
            }
        }

        // #834: "Make Selected Class Independent" — mirrors WF
        // SkillAssignmentClassSkillSystemForm.IndependenceButton_Click
        // (Form.cs:551-595) -> PatchUtil.WriteIndependence. Clones the SHARED
        // level-up table into a fresh free-space block and repoints ONLY this
        // class's pointer slot (a SINGLE write_p32, NOT RepointAllReferences),
        // so every other sharing class stays on the intact original table.
        // Mirror the WF empty-list confirm + ReloadList + JumpTo(classid).
        void Independence_Click(object sender, RoutedEventArgs e)
        {
            if (!_vm.IsLoaded) return;

            // WF: when the list is empty, confirm before separating an empty
            // table (Form.cs:576-583). Mirror that prompt; a "No" aborts.
            if (_vm.IsSelectedLevelUpListEmpty())
            {
                bool yes = CoreState.Services?.ShowYesNo(R._(
                    "The list is empty. Separating an empty list has no effect. Make it independent anyway?")) == true;
                if (!yes) return;
            }

            _undoService.Begin("Make Skill Assignment Class Independent");
            try
            {
                uint newPointer = _vm.MakeIndependent(_undoService.GetActiveUndoData());
                if (newPointer == 0)
                {
                    _undoService.Rollback();
                    return;
                }
                _undoService.Commit();
                _vm.MarkClean();

                // Mirror WF ReloadList + InputFormRef.JumpTo(classid): refresh
                // the master list and reselect the same class so the now-
                // independent table is shown.
                uint classId = _vm.SelectedId;
                LoadList();
                EntryList.SelectAddress(_vm.AssignClassBaseAddress + classId * _vm.MasterBlockSize);

                CoreState.Services?.ShowInfo($"Class is now independent. New table: 0x{newPointer:X08}.");
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.ErrorF("SkillAssignmentClassSkillSystemView.Independence failed: {0}", ex.Message);
            }
        }

        void N1B0_ValueChanged(object sender, NumericUpDownValueChangedEventArgs e)
        {
            if (_suppressN1B0Change) return;
            try
            {
                _suppressDifficultyChange = true;
                _suppressLevelChange = true;
                _vm.LevelUpRaw = (uint)(N1B0Box.Value ?? 0);
                _vm.RefreshDifficultyFlagsFromRaw();
                LevelValueBox.Value = _vm.LevelValue;
                PlayerOnlyCheckBox.IsChecked = _vm.IsPlayerOnly;
                EnemyOnlyCheckBox.IsChecked = _vm.IsEnemyOnly;
                NormalHardCheckBox.IsChecked = _vm.IsNormalHard;
                HardOnlyCheckBox.IsChecked = _vm.IsHardOnly;
                UpdateMasterPanelVisibility();
            }
            finally
            {
                _suppressDifficultyChange = false;
                _suppressLevelChange = false;
            }
        }

        void DifficultyCheckBox_CheckedChanged(object sender, RoutedEventArgs e)
        {
            if (_suppressDifficultyChange) return;
            try
            {
                _suppressN1B0Change = true;
                _vm.IsPlayerOnly = PlayerOnlyCheckBox.IsChecked == true;
                _vm.IsEnemyOnly = EnemyOnlyCheckBox.IsChecked == true;
                _vm.IsNormalHard = NormalHardCheckBox.IsChecked == true;
                _vm.IsHardOnly = HardOnlyCheckBox.IsChecked == true;
                _vm.LevelValue = (uint)(LevelValueBox.Value ?? 0);
                _vm.ApplyDifficultyFlagsToRaw();
                N1B0Box.Value = _vm.LevelUpRaw;
            }
            finally { _suppressN1B0Change = false; }
        }

        void LevelValueBox_ValueChanged(object sender, NumericUpDownValueChangedEventArgs e)
        {
            if (_suppressLevelChange) return;
            try
            {
                _suppressN1B0Change = true;
                _vm.LevelValue = (uint)(LevelValueBox.Value ?? 0);
                _vm.ApplyDifficultyFlagsToRaw();
                N1B0Box.Value = _vm.LevelUpRaw;
            }
            finally { _suppressN1B0Change = false; }
        }

        void LearnInfo_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "https://dw.ngmansion.xyz/doku.php?id=en:guide_febuildergba_learnskillinfo",
                    UseShellExecute = true,
                };
                System.Diagnostics.Process.Start(psi);
            }
            catch (Exception ex)
            {
                Log.ErrorF("SkillAssignmentClassSkillSystemView.LearnInfo failed: {0}", ex.Message);
            }
        }

        async void BulkExport_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var sp = StorageProvider;
                if (sp == null) return;
                var file = await sp.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = "Export Skill Assignment (Class) data",
                    SuggestedFileName = "SkillAssignmentClass.tsv",
                    FileTypeChoices = new[]
                    {
                        new FilePickerFileType("TSV") { Patterns = new[] { "*.tsv", "*.SkillAssignmentClass.tsv" } },
                    },
                });
                if (file == null) return;
                string path = file.Path.LocalPath;
                if (string.IsNullOrEmpty(path)) return;
                bool ok = _vm.ExportAllData(path);
                if (ok) CoreState.Services?.ShowInfo("Exported.");
                else CoreState.Services?.ShowError("Export failed.");
            }
            catch (Exception ex)
            {
                Log.ErrorF("SkillAssignmentClassSkillSystemView.BulkExport failed: {0}", ex.Message);
            }
        }

        async void BulkImport_Click(object sender, RoutedEventArgs e)
        {
            // Pick the file BEFORE opening the undo scope - the file picker
            // is async and keeping the ambient-undo scope open across the
            // await would inadvertently record ROM writes from other UI
            // actions while the picker is showing (Copilot CLI review on
            // PR #555).
            try
            {
                var sp = StorageProvider;
                if (sp == null) return;
                var files = await sp.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = "Import Skill Assignment (Class) data",
                    AllowMultiple = false,
                    FileTypeFilter = new[]
                    {
                        new FilePickerFileType("TSV") { Patterns = new[] { "*.tsv", "*.SkillAssignmentClass.tsv" } },
                    },
                });
                if (files == null || files.Count == 0) return;
                string path = files[0].Path.LocalPath;
                if (string.IsNullOrEmpty(path)) return;

                // Now open the undo scope and perform the actual import.
                _undoService.Begin("Bulk Import Skill Assignment (Class) data");
                try
                {
                    bool ok = _vm.ImportAllData(path);
                    if (!ok)
                    {
                        _undoService.Rollback();
                        CoreState.Services?.ShowError("Import failed.");
                        return;
                    }
                    _undoService.Commit();
                    LoadList();
                    CoreState.Services?.ShowInfo("Imported.");
                }
                catch (Exception ex)
                {
                    _undoService.Rollback();
                    Log.ErrorF("SkillAssignmentClassSkillSystemView.BulkImport failed: {0}", ex.Message);
                }
            }
            catch (Exception ex)
            {
                Log.ErrorF("SkillAssignmentClassSkillSystemView.BulkImport file picker failed: {0}", ex.Message);
            }
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
    }
}
