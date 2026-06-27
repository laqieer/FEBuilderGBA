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
    /// Avalonia counterpart of WinForms SkillAssignmentUnitSkillSystemForm.
    /// Gap-sweep #995 raises the AXAML control surface from a single-placeholder
    /// stub to a functional master/detail editor. Master + N1 ROM writes are
    /// wrapped in View-owned UndoService scopes.
    ///
    /// The per-unit level-up (N1) table is OPTIONAL: old SkillSystems patches
    /// lack the unit-based level-up table. When it is unavailable the View hides
    /// the ENTIRE N1 group — the Level-up Top Address row, the Reload button, the
    /// N1 sub-list, and the N1 detail/write controls — by binding the group's
    /// <c>IsVisible</c> to the VM's
    /// <see cref="SkillAssignmentUnitSkillSystemViewModel.HasLevelUpTable"/> flag
    /// (mirroring WF <c>UnitLevelUpSkill.Hide()</c> when
    /// <c>FindAssignUnitLevelUpSkillPointer() == U.NOT_FOUND</c>), so no dead
    /// level-up UI is shown.
    /// </summary>
    public partial class SkillAssignmentUnitSkillSystemView : TranslatedWindow, IEditorView, IDataVerifiableView
    {
        readonly SkillAssignmentUnitSkillSystemViewModel _vm = new();
        readonly UndoService _undoService = new();
        Bitmap _unitIconBitmap;
        Bitmap _n1IconBitmap;

        public string ViewTitle => "Skill Assignment (Unit)";
        public bool IsLoaded => _vm.IsLoaded;
        public ViewModelBase DataViewModel => _vm;

        public SkillAssignmentUnitSkillSystemView()
        {
            InitializeComponent();
            // Bind DataContext so AXAML {Binding LevelUpEntries} resolves.
            DataContext = _vm;
            EntryList.SelectedAddressChanged += OnSelected;
            Opened += (_, _) => LoadList();
            Closed += (_, _) =>
            {
                DisposeBitmap(ref _unitIconBitmap);
                DisposeBitmap(ref _n1IconBitmap);
            };
        }

        static void DisposeBitmap(ref Bitmap bmp)
        {
            if (bmp == null) return;
            try { bmp.Dispose(); } catch (System.Exception ex) { Log.Debug("SkillAssignmentUnitSkillSystemView dispose bmp: {0}", ex.Message); }
            bmp = null;
        }

        void LoadList()
        {
            _vm.IsLoading = true;
            try
            {
                var items = _vm.LoadList();
                // Mirror WF AddressList OwnerDraw=DrawUnitAndText. The row label
                // prefix "0xXX" IS the 1-based WF uid (0 = empty sentinel,
                // 1 = Eirika on FE8) — the SAME value WF feeds to
                // UnitForm.GetUnitName / DrawUnitAndText. U.atoh parses that
                // prefix, so pass it DIRECTLY to ResolveUnitPortraitIdByOneBasedId
                // (no +1). For prefix 0 the resolver returns 0 (no portrait),
                // keeping the name + portrait consistent with the row label.
                EntryList.SetItemsWithIcons(items, (idx) =>
                {
                    if (idx < 0 || idx >= items.Count) return null;
                    try
                    {
                        uint unitId = U.atoh(items[idx].name);
                        uint portraitId = PreviewIconHelper.ResolveUnitPortraitIdByOneBasedId(unitId);
                        if (portraitId == 0) return null;
                        using var img = PreviewIconHelper.LoadPortraitMini(portraitId);
                        return ImageConversionHelper.ToAvaloniaBitmap(img);
                    }
                    catch { return null; }
                });
                if (TopBar != null)
                {
                    TopBar.ReadStartAddress = _vm.ReadStartAddress;
                    TopBar.ReadCount = (int)_vm.ReadCount;
                }
                UpdateMasterPanelVisibility();
            }
            catch (Exception ex)
            {
                Log.Error("SkillAssignmentUnitSkillSystemView.LoadList failed: {0}", ex.Message);
            }
            finally { _vm.IsLoading = false; _vm.MarkClean(); }
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
                if (_vm.LevelUpEntries.Count > 0)
                {
                    N1EntryList.SelectedIndex = 0;
                }
            }
            catch (Exception ex)
            {
                Log.Error("SkillAssignmentUnitSkillSystemView.OnSelected failed: {0}", ex.Message);
            }
            finally { _vm.IsLoading = false; _vm.MarkClean(); }
        }

        void UpdateUI()
        {
            AddressBox.Value = _vm.CurrentAddr;
            BlockSizeBox.Value = _vm.MasterBlockSize;
            SelectedAddressLabel.Content = $"0x{_vm.CurrentAddr:X08}";

            UnitSkillBox.Value = _vm.UnitSkill;
            SkillNameBox.Text = NameResolver.GetSkillName(_vm.UnitSkill);
            SkillTextBox.Text = ResolveSkillText(_vm.UnitSkill);
            RefreshUnitIcon();

            XLevelUpAddrBox.Value = _vm.XLevelUpAddr;
            N1ReadCountBox.Value = (uint)_vm.LevelUpEntries.Count;

            UpdateMasterPanelVisibility();
        }

        void UpdateMasterPanelVisibility()
        {
            ZeroPointerPanel.IsVisible = _vm.IsZeroPointer;
            if (string.IsNullOrEmpty(ZeroPointerText.Text))
            {
                ZeroPointerText.Text = R._("No level-up table is allocated for this unit.\nPress List Expand to allocate one.");
            }
            IndependencePanel.IsVisible = _vm.IsIndependenceVisible;
        }

        void RefreshUnitIcon()
        {
            try
            {
                ROM rom = CoreState.ROM;
                if (rom != null && _vm.IconBaseAddress != 0 && _vm.UnitSkill > 0)
                {
                    using var img = PreviewIconHelper.LoadSkillIcon(_vm.UnitSkill, _vm.IconBaseAddress);
                    Bitmap bmp = img != null ? ImageConversionHelper.ToAvaloniaBitmap(img) : null;
                    SetUnitIcon(bmp);
                }
                else
                {
                    SetUnitIcon(null);
                }
            }
            catch { SetUnitIcon(null); }
        }

        void SetUnitIcon(Bitmap bmp)
        {
            if (_unitIconBitmap != null && !ReferenceEquals(_unitIconBitmap, bmp))
            {
                try { _unitIconBitmap.Dispose(); } catch (System.Exception ex) { Log.Debug("SkillAssignmentUnitSkillSystemView dispose unit icon: {0}", ex.Message); }
            }
            _unitIconBitmap = bmp;
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
                try { _n1IconBitmap.Dispose(); } catch (System.Exception ex) { Log.Debug("SkillAssignmentUnitSkillSystemView dispose n1 icon: {0}", ex.Message); }
            }
            _n1IconBitmap = bmp;
            N1SkillIconImage.Source = bmp;
        }

        string ResolveSkillText(uint skillId) => _vm.ResolveSkillTextById(skillId);

        void MasterWriteButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_vm.IsLoaded || _vm.CurrentAddr == 0) return;
            _undoService.Begin("Edit Skill Assignment (Unit)");
            try
            {
                _vm.UnitSkill = (uint)(UnitSkillBox.Value ?? 0);
                _vm.XLevelUpAddr = (uint)(XLevelUpAddrBox.Value ?? 0);
                _vm.WriteMaster();
                _undoService.Commit();
                _vm.MarkClean();
                RefreshUnitIcon();
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.Error("SkillAssignmentUnitSkillSystemView.MasterWrite failed: {0}", ex.Message);
            }
        }

        void N1EntryList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (N1EntryList.SelectedItem is not SkillAssignmentUnitSkillSystemViewModel.LevelUpEntry entry) return;
            try
            {
                _vm.IsLoading = true;
                _vm.LoadLevelUpEntry(entry.Addr);
                UpdateN1UI();
            }
            catch (Exception ex)
            {
                Log.Error("SkillAssignmentUnitSkillSystemView.N1EntryList_SelectionChanged failed: {0}", ex.Message);
            }
            finally { _vm.IsLoading = false; }
        }

        void UpdateN1UI()
        {
            N1AddressBox.Value = _vm.SelectedLevelUpAddr;
            N1BlockSizeBox.Value = _vm.LevelUpBlockSize;
            N1SelectedAddressLabel.Content = $"0x{_vm.SelectedLevelUpAddr:X08}";

            N1B0Box.Value = _vm.LevelUpRaw;
            N1B1Box.Value = _vm.LevelUpSkill;

            N1SkillNameBox.Text = NameResolver.GetSkillName(_vm.LevelUpSkill);
            N1SkillTextBox.Text = ResolveSkillText(_vm.LevelUpSkill);
            RefreshN1Icon();
            UpdateMasterPanelVisibility();
        }

        void N1WriteButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_vm.IsLoaded || _vm.SelectedLevelUpAddr == 0) return;
            _undoService.Begin("Edit Skill Assignment Unit Level-up Entry");
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
                Log.Error("SkillAssignmentUnitSkillSystemView.N1Write failed: {0}", ex.Message);
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
                Log.Error("SkillAssignmentUnitSkillSystemView.N1ReloadList failed: {0}", ex.Message);
            }
        }

        // #1604: N1 level-up list-expand — allocate-on-null OR grow. Mirrors WF
        // SkillAssignmentUnitSkillSystemForm.N1_InputFormRef_AddressListExpandsEvent
        // and the sibling Class view's N1ListExpand_Click. SINGLE-slot repoint
        // (this unit's LEVELUP+4 only) so sharing units stay on their table.
        void N1ListExpand_Click(object sender, RoutedEventArgs e)
        {
            _undoService.Begin("Expand Skill Assignment Unit Level-up Table");
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
                Log.Error($"SkillAssignmentUnitSkillSystemView.N1ListExpand failed: {ex}");
            }
        }

        // #1604: "Make Selected Unit Independent" — mirrors WF
        // SkillAssignmentUnitSkillSystemForm.IndependenceButton_Click ->
        // PatchUtil.WriteIndependence. Clones the SHARED level-up table into a
        // fresh free-space block and repoints ONLY this unit's pointer slot (a
        // SINGLE write_p32, NOT RepointAllReferences), so every other sharing
        // unit stays on the intact original table. Mirror the WF empty-list
        // confirm + ReloadList + reselect.
        void Independence_Click(object sender, RoutedEventArgs e)
        {
            if (!_vm.IsLoaded) return;

            // WF: when the list is empty, confirm before separating an empty
            // table. A "No" aborts.
            if (_vm.IsSelectedLevelUpListEmpty())
            {
                bool yes = CoreState.Services?.ShowYesNo(R._(
                    "The list is empty. Separating an empty list has no effect. Make it independent anyway?")) == true;
                if (!yes) return;
            }

            _undoService.Begin("Make Skill Assignment Unit Independent");
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

                // Mirror WF ReloadList + reselect the same unit so the now-
                // independent table is shown.
                uint unitId = _vm.SelectedId;
                LoadList();
                EntryList.SelectAddress(_vm.AssignUnitBaseAddress + unitId * _vm.MasterBlockSize);

                CoreState.Services?.ShowInfo($"Unit is now independent. New table: 0x{newPointer:X08}.");
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.Error($"SkillAssignmentUnitSkillSystemView.Independence failed: {ex}");
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
                    Title = "Export Skill Assignment (Unit) data",
                    SuggestedFileName = "SkillAssignmentUnit.tsv",
                    FileTypeChoices = new[]
                    {
                        new FilePickerFileType("TSV") { Patterns = new[] { "*.tsv", "*.SkillAssignmentUnit.tsv" } },
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
                Log.Error($"SkillAssignmentUnitSkillSystemView.BulkExport failed: {ex}");
            }
        }

        async void BulkImport_Click(object sender, RoutedEventArgs e)
        {
            // Pick the file BEFORE opening the undo scope — the file picker is
            // async and keeping the ambient-undo scope open across the await
            // would inadvertently record ROM writes from other UI actions while
            // the picker is showing.
            try
            {
                var sp = StorageProvider;
                if (sp == null) return;
                var files = await sp.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = "Import Skill Assignment (Unit) data",
                    AllowMultiple = false,
                    FileTypeFilter = new[]
                    {
                        new FilePickerFileType("TSV") { Patterns = new[] { "*.tsv", "*.SkillAssignmentUnit.tsv" } },
                    },
                });
                if (files == null || files.Count == 0) return;
                string path = files[0].Path.LocalPath;
                if (string.IsNullOrEmpty(path)) return;

                // Now open the undo scope and perform the actual import.
                _undoService.Begin("Bulk Import Skill Assignment (Unit) data");
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
                    Log.Error($"SkillAssignmentUnitSkillSystemView.BulkImport failed: {ex}");
                }
            }
            catch (Exception ex)
            {
                Log.Error($"SkillAssignmentUnitSkillSystemView.BulkImport file picker failed: {ex}");
            }
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
                Log.Error("SkillAssignmentUnitSkillSystemView.LearnInfo failed: {0}", ex.Message);
            }
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
    }
}
