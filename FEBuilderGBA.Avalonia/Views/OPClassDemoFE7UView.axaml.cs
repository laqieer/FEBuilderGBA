using System;
using System.Collections.Generic;
using global::Avalonia.Controls;
using global::Avalonia.Input;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;
using FEBuilderGBA.Core;

namespace FEBuilderGBA.Avalonia.Views
{
    /// <summary>
    /// Avalonia counterpart of WinForms <c>OPClassDemoFE7UForm</c>.
    /// Gap-sweep fix (#421) raises the AXAML control surface from 26 to
    /// MEDIUM-verdict density (>= 42 of WF's 56 controls), exposes all 17
    /// main-entry fields (including W8/B15-B17/B19/B22/B23 which the prior
    /// view omitted), and wires the N2 (animation command sequence)
    /// sub-list. Both Write_Click and NWrite_Click open separate undo
    /// scopes (<c>"Edit OP Class Demo (FE7U)"</c> and
    /// <c>"Edit OP Class Demo Anime Command"</c>) so the two surfaces
    /// undo independently.
    ///
    /// Pointer fields P0 (English name) and P24 (anime block) round-trip
    /// through <c>rom.write_p32</c> via the <c>EditorFormRef</c>
    /// <c>FieldType.Pointer</c> codec.
    /// </summary>
    public partial class OPClassDemoFE7UView : TranslatedWindow, IEditorView, IDataVerifiableView
    {
        readonly OPClassDemoFE7UViewModel _vm = new();
        readonly UndoService _undoService = new();
        bool _suppressN2Change;

        public string ViewTitle => "OP Class Demo (FE7U) Editor";
        public bool IsLoaded => _vm.CanWrite;
        public ViewModelBase? DataViewModel => _vm;

        public OPClassDemoFE7UView()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            DescTextIdBox.ValueChanged += OnDescTextIdChanged;
            N2CommandRawBox.ValueChanged += OnN2CommandRawChanged;
            Opened += (_, _) => LoadList();
        }

        void OnDescTextIdChanged(object? sender, NumericUpDownValueChangedEventArgs e)
        {
            if (_vm.IsLoading) return;
            uint id = (uint)(DescTextIdBox.Value ?? 0);
            try { DescTextPreview.Text = id != 0 ? NameResolver.GetTextById(id) : ""; }
            catch { DescTextPreview.Text = ""; }
        }

        void OnN2CommandRawChanged(object? sender, NumericUpDownValueChangedEventArgs e)
        {
            if (_suppressN2Change) return;
            // Sync the description combo + wait-unit label to the raw command.
            uint cmd = (uint)(N2CommandRawBox.Value ?? 0);
            SyncN2CommandDescription(cmd);
        }

        void SyncN2CommandDescription(uint cmd)
        {
            _suppressN2Change = true;
            try
            {
                // Combo items are 1-indexed (item 0 = cmd 1, item 7 = cmd 8).
                if (cmd >= 1 && cmd <= 8)
                    N2CommandCombo.SelectedIndex = (int)cmd - 1;
                else
                    N2CommandCombo.SelectedIndex = -1;

                // Per WF N2_AddressList_SelectedIndexChanged: cmd 3 and 8 use
                // raw "00" argument (no wait); other commands display "/60 (sec)".
                if (cmd == 0x03 || cmd == 0x08)
                {
                    N2WaitLabel.Content = "Argument";
                    N2WaitUnitLabel.Content = "00";
                }
                else
                {
                    N2WaitLabel.Content = "Wait Frames";
                    N2WaitUnitLabel.Content = "/60 (sec)";
                }
            }
            finally { _suppressN2Change = false; }
        }

        void LoadList()
        {
            _vm.IsLoading = true;
            try
            {
                var items = _vm.LoadList();
                EntryList.SetItemsWithIcons(items, i => ListIconLoaders.ClassIconLoader(items, i));
                if (!string.IsNullOrEmpty(_vm.UnavailableMessage))
                    UnavailableLabel.Text = _vm.UnavailableMessage;
            }
            catch (Exception ex)
            {
                Log.Error("OPClassDemoFE7UView.LoadList failed: {0}", ex.Message);
            }
            finally { _vm.IsLoading = false; _vm.MarkClean(); }
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
                Log.Error("OPClassDemoFE7UView.OnSelected failed: {0}", ex.Message);
            }
            finally { _vm.IsLoading = false; _vm.MarkClean(); }
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            AddressBox.Value = _vm.CurrentAddr;
            SelectedAddressLabel.Content = $"0x{_vm.CurrentAddr:X08}";

            EnglishNamePtrBox.Value = _vm.EnglishNamePointer;
            DescTextIdBox.Value = _vm.DescriptionTextId;
            try { DescTextPreview.Text = _vm.DescriptionTextId != 0 ? NameResolver.GetTextById(_vm.DescriptionTextId) : ""; }
            catch { DescTextPreview.Text = ""; }
            JpNamePtrBox.Value = _vm.JapaneseNamePointer;
            JpNameLenBox.Value = _vm.JapaneseNameLength;
            ClassIdBox.Value = _vm.ClassId;
            AllyEnemyColorBox.Value = _vm.AllyEnemyColor;
            BattleAnimeBox.Value = _vm.BattleAnime;
            MagicEffectBox.Value = _vm.MagicEffect;
            Unknown15Box.Value = _vm.Unknown15;
            Unknown16Box.Value = _vm.Unknown16;
            Unknown17Box.Value = _vm.Unknown17;
            Unknown19Box.Value = _vm.Unknown19;
            TerrainLeftBox.Value = _vm.TerrainLeft;
            TerrainRightBox.Value = _vm.TerrainRight;
            Unknown22Box.Value = _vm.Unknown22;
            Unknown23Box.Value = _vm.Unknown23;
            AnimePtrBox.Value = _vm.AnimePointer;

            RebuildN2ListBox();
        }

        void RebuildN2ListBox()
        {
            _suppressN2Change = true;
            try
            {
                var items = new List<string>();
                foreach (var row in _vm.N2Entries)
                    items.Add($"{row.Index:X2}  cmd={row.Command:X2} arg={row.Argument:X2}");
                N2ListBox.ItemsSource = items;
                if (_vm.SelectedN2Index >= 0 && _vm.SelectedN2Index < items.Count)
                    N2ListBox.SelectedIndex = _vm.SelectedN2Index;
                else
                    N2ListBox.SelectedIndex = -1;

                N2AddressBox.Value = _vm.AnimePointer;
                N2SelectedAddressLabel.Content = _vm.N2SelectedAddress != 0
                    ? $"0x{_vm.N2SelectedAddress:X08}" : "";
                N2CommandRawBox.Value = _vm.N2Command;
                N2ArgumentBox.Value = _vm.N2Argument;
                SyncN2CommandDescription(_vm.N2Command);
            }
            finally { _suppressN2Change = false; }
        }

        void N2List_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (_suppressN2Change) return;
            int idx = N2ListBox.SelectedIndex;
            if (idx < 0 || idx >= _vm.N2Entries.Count) return;
            _vm.LoadN2Row(idx);
            _suppressN2Change = true;
            try
            {
                N2SelectedAddressLabel.Content = $"0x{_vm.N2SelectedAddress:X08}";
                N2CommandRawBox.Value = _vm.N2Command;
                N2ArgumentBox.Value = _vm.N2Argument;
                SyncN2CommandDescription(_vm.N2Command);
            }
            finally { _suppressN2Change = false; }
        }

        void N2CommandCombo_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (_suppressN2Change) return;
            int idx = N2CommandCombo.SelectedIndex;
            if (idx < 0) return;
            // Combo index 0 maps to cmd 1, ..., index 7 maps to cmd 8.
            uint cmd = (uint)(idx + 1);
            _suppressN2Change = true;
            try
            {
                N2CommandRawBox.Value = cmd;
                SyncN2CommandDescription(cmd);
            }
            finally { _suppressN2Change = false; }
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.CanWrite) return;
            _undoService.Begin("Edit OP Class Demo (FE7U)");
            try
            {
                _vm.EnglishNamePointer = (uint)(EnglishNamePtrBox.Value ?? 0);
                _vm.DescriptionTextId = (uint)(DescTextIdBox.Value ?? 0);
                _vm.JapaneseNamePointer = (uint)(JpNamePtrBox.Value ?? 0);
                _vm.JapaneseNameLength = (uint)(JpNameLenBox.Value ?? 0);
                _vm.ClassId = (uint)(ClassIdBox.Value ?? 0);
                _vm.AllyEnemyColor = (uint)(AllyEnemyColorBox.Value ?? 0);
                _vm.BattleAnime = (uint)(BattleAnimeBox.Value ?? 0);
                _vm.MagicEffect = (uint)(MagicEffectBox.Value ?? 0);
                _vm.Unknown15 = (uint)(Unknown15Box.Value ?? 0);
                _vm.Unknown16 = (uint)(Unknown16Box.Value ?? 0);
                _vm.Unknown17 = (uint)(Unknown17Box.Value ?? 0);
                _vm.Unknown19 = (uint)(Unknown19Box.Value ?? 0);
                _vm.TerrainLeft = (uint)(TerrainLeftBox.Value ?? 0);
                _vm.TerrainRight = (uint)(TerrainRightBox.Value ?? 0);
                _vm.Unknown22 = (uint)(Unknown22Box.Value ?? 0);
                _vm.Unknown23 = (uint)(Unknown23Box.Value ?? 0);
                _vm.AnimePointer = (uint)(AnimePtrBox.Value ?? 0);
                _vm.WriteEntry();
                // Refresh the N2 sub-list in case the anime pointer changed.
                _vm.LoadN2List();
                _undoService.Commit();
                _vm.MarkClean();
                UpdateUI();
                CoreState.Services?.ShowInfo("OP Class Demo (FE7U) data written.");
            }
            catch (Exception ex) { _undoService.Rollback(); Log.Error("OPClassDemoFE7UView.Write: {0}", ex.Message); }
        }

        void NWrite_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.CanWrite) return;
            if (_vm.SelectedN2Index < 0) return;
            _undoService.Begin("Edit OP Class Demo Anime Command");
            try
            {
                _vm.N2Command = (uint)(N2CommandRawBox.Value ?? 0);
                _vm.N2Argument = (uint)(N2ArgumentBox.Value ?? 0);
                bool ok = _vm.WriteN2Entry();
                if (!ok)
                {
                    _undoService.Rollback();
                    Log.Error("OPClassDemoFE7UView.NWrite: WriteN2Entry rejected (invalid row/pointer)");
                    return;
                }
                _undoService.Commit();
                _vm.MarkClean();
                RebuildN2ListBox();
                CoreState.Services?.ShowInfo("Animation command written.");
            }
            catch (Exception ex) { _undoService.Rollback(); Log.Error("OPClassDemoFE7UView.NWrite: {0}", ex.Message); }
        }

        void OnClassIdLinkClick(object? sender, PointerPressedEventArgs e)
        {
            try
            {
                var rom = CoreState.ROM;
                if (rom?.RomInfo == null) return;
                uint classId = (uint)(ClassIdBox.Value ?? 0);
                uint baseAddr = rom.p32(rom.RomInfo.class_pointer);
                if (!U.isSafetyOffset(baseAddr)) return;
                uint addr = baseAddr + classId * rom.RomInfo.class_datasize;
                if (rom.RomInfo.version == 6)
                    WindowManager.Instance.Navigate<ClassFE6View>(addr);
                else
                    WindowManager.Instance.Navigate<ClassEditorView>(addr);
            }
            catch (Exception ex) { Log.Error("OnClassIdLinkClick failed: {0}", ex.Message); }
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
    }
}
