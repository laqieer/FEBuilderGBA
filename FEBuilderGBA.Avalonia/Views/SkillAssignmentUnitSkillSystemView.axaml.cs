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
                ZeroPointerText.Text = R._("No level-up table is allocated for this unit.\nPress Reload to refresh after editing the address.");
            }
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
