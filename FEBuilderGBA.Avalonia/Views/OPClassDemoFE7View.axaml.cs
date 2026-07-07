using System;
using global::Avalonia;
using System.Collections.Generic;
using System.Threading.Tasks;
using global::Avalonia.Controls;
using global::Avalonia.Input;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Controls;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;
using FEBuilderGBA.Core;

namespace FEBuilderGBA.Avalonia.Views
{
    /// <summary>
    /// Avalonia counterpart of WinForms <c>OPClassDemoFE7Form</c>.
    /// Gap-sweep fix (#414) raises the AXAML control surface from 32 to
    /// MEDIUM-verdict density (>= 48 of WF's 64 controls), exposes all 21
    /// main-entry fields (including W6, B12 JP-name-start, B22, and B25-B27
    /// which the prior view omitted), corrects three field-offset bugs
    /// (B14 = Palette, B23 = Terrain Left, B24 = Terrain Right), and wires
    /// the N2 (animation command sequence) sub-list.
    ///
    /// Both <c>Write_Click</c> and <c>NWrite_Click</c> open separate undo
    /// scopes ("Edit OP Class Demo (FE7)" / "Edit OP Class Demo Anime
    /// Command") so the two surfaces undo independently. After the main
    /// Write commits, <c>LoadN2List()</c> refreshes the N2 sub-list in case
    /// the anime pointer changed (Copilot CLI plan-review #2 finding).
    ///
    /// Pointer fields P0 (English name), P8 (Japanese name image) and P28
    /// (anime block) round-trip through <c>rom.write_p32</c> via the
    /// <c>EditorFormRef</c> <c>FieldType.Pointer</c> codec.
    /// </summary>
    public partial class OPClassDemoFE7View : TranslatedUserControl, IEmbeddableEditor, IDataVerifiableView
    {
        readonly OPClassDemoFE7ViewModel _vm = new();
        readonly UndoService _undoService = new();
        bool _hasLoadedList;
        bool _suppressN2Change;

        public string ViewTitle => "OP Class Demo (FE7) Editor";
        public new bool IsLoaded => _vm.CanWrite;
        public EditorDescriptor Descriptor => new("OP Class Demo (FE7) Editor", 1580, 1020, SizeToContent: true);
        public event EventHandler? CloseRequested;
        public ViewModelBase? DataViewModel => _vm;
        public void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);

        public OPClassDemoFE7View()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            DescTextIdLowBox.ValueChanged += OnDescTextIdLowChanged;
            N2CommandRawBox.ValueChanged += OnN2CommandRawChanged;
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

        void OnDescTextIdLowChanged(object? sender, NumericUpDownValueChangedEventArgs e)
        {
            if (_vm.IsLoading) return;
            uint id = (uint)(DescTextIdLowBox.Value ?? 0);
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
                // Wrap in R._() so the runtime assignment stays localized after
                // the initial TranslatedUserControl.TranslateAll() pass (Copilot PR
                // #537 finding).
                if (cmd == 0x03 || cmd == 0x08)
                {
                    N2WaitLabel.Content = R._("Argument");
                    N2WaitUnitLabel.Content = "00";
                }
                else
                {
                    N2WaitLabel.Content = R._("Wait Frames");
                    N2WaitUnitLabel.Content = R._("/60 (sec)");
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
                Log.ErrorF("OPClassDemoFE7View.LoadList failed: {0}", ex.Message);
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
                Log.ErrorF("OPClassDemoFE7View.OnSelected failed: {0}", ex.Message);
            }
            finally { _vm.IsLoading = false; _vm.MarkClean(); }
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            AddressBox.Value = _vm.CurrentAddr;
            SelectedAddressLabel.Content = $"0x{_vm.CurrentAddr:X08}";

            EnglishNamePtrBox.Value = _vm.EnglishNamePointer;
            DescTextIdLowBox.Value = _vm.DescriptionTextIdLow;
            try { DescTextPreview.Text = _vm.DescriptionTextIdLow != 0 ? NameResolver.GetTextById(_vm.DescriptionTextIdLow) : ""; }
            catch { DescTextPreview.Text = ""; }
            Unknown6Box.Value = _vm.Unknown6;
            JpNamePtrBox.Value = _vm.JapaneseNamePointer;
            JpNameStartBox.Value = _vm.JapaneseNameStart;
            JpNameLenBox.Value = _vm.JapaneseNameLength;
            PaletteIdBox.Value = _vm.PaletteId;
            ClassIdBox.Value = _vm.ClassId;
            // NameResolver returns a fallback on failure (Copilot review #638).
            ClassIdBox.NameText = NameResolver.GetClassName(_vm.ClassId);
            AllyEnemyColorBox.Value = _vm.AllyEnemyColor;
            BattleAnimeBox.Value = _vm.BattleAnime;
            MagicEffectBox.Value = _vm.MagicEffect;
            Unknown19Box.Value = _vm.Unknown19;
            Unknown20Box.Value = _vm.Unknown20;
            Unknown21Box.Value = _vm.Unknown21;
            Unknown22Box.Value = _vm.Unknown22;
            TerrainLeftBox.Value = _vm.TerrainLeft;
            TerrainRightBox.Value = _vm.TerrainRight;
            Unknown25Box.Value = _vm.Unknown25;
            Unknown26Box.Value = _vm.Unknown26;
            Unknown27Box.Value = _vm.Unknown27;
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
            // Clear the ViewModel selection state when the ListBox loses its
            // selection (idx == -1) or the index is out of range. Without
            // this, _vm.SelectedN2Index could keep pointing at a stale row
            // and NWrite_Click would silently write to the wrong slot
            // (Copilot PR #537 review finding).
            if (idx < 0 || idx >= _vm.N2Entries.Count)
            {
                _vm.LoadN2Row(-1);
                _suppressN2Change = true;
                try
                {
                    N2SelectedAddressLabel.Content = "";
                    N2CommandRawBox.Value = 0;
                    N2ArgumentBox.Value = 0;
                    SyncN2CommandDescription(0);
                }
                finally { _suppressN2Change = false; }
                return;
            }
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
            _undoService.Begin("Edit OP Class Demo (FE7)");
            try
            {
                _vm.EnglishNamePointer = (uint)(EnglishNamePtrBox.Value ?? 0);
                _vm.DescriptionTextIdLow = (uint)(DescTextIdLowBox.Value ?? 0);
                _vm.Unknown6 = (uint)(Unknown6Box.Value ?? 0);
                _vm.JapaneseNamePointer = (uint)(JpNamePtrBox.Value ?? 0);
                _vm.JapaneseNameStart = (uint)(JpNameStartBox.Value ?? 0);
                _vm.JapaneseNameLength = (uint)(JpNameLenBox.Value ?? 0);
                _vm.PaletteId = (uint)(PaletteIdBox.Value ?? 0);
                _vm.ClassId = ClassIdBox.Value;
                _vm.AllyEnemyColor = (uint)(AllyEnemyColorBox.Value ?? 0);
                _vm.BattleAnime = (uint)(BattleAnimeBox.Value ?? 0);
                _vm.MagicEffect = (uint)(MagicEffectBox.Value ?? 0);
                _vm.Unknown19 = (uint)(Unknown19Box.Value ?? 0);
                _vm.Unknown20 = (uint)(Unknown20Box.Value ?? 0);
                _vm.Unknown21 = (uint)(Unknown21Box.Value ?? 0);
                _vm.Unknown22 = (uint)(Unknown22Box.Value ?? 0);
                _vm.TerrainLeft = (uint)(TerrainLeftBox.Value ?? 0);
                _vm.TerrainRight = (uint)(TerrainRightBox.Value ?? 0);
                _vm.Unknown25 = (uint)(Unknown25Box.Value ?? 0);
                _vm.Unknown26 = (uint)(Unknown26Box.Value ?? 0);
                _vm.Unknown27 = (uint)(Unknown27Box.Value ?? 0);
                _vm.AnimePointer = (uint)(AnimePtrBox.Value ?? 0);
                _vm.WriteEntry();
                // Refresh the N2 sub-list in case the anime pointer changed
                // (Copilot CLI plan v2 finding).
                _vm.LoadN2List();
                _undoService.Commit();
                _vm.MarkClean();
                UpdateUI();
                CoreState.Services?.ShowInfo("OP Class Demo (FE7) data written.");
            }
            catch (Exception ex) { _undoService.Rollback(); Log.ErrorF("OPClassDemoFE7View.Write: {0}", ex.Message); }
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
                    Log.Error("OPClassDemoFE7View.NWrite: WriteN2Entry rejected (invalid row/pointer)");
                    return;
                }
                _undoService.Commit();
                _vm.MarkClean();
                RebuildN2ListBox();
                CoreState.Services?.ShowInfo("Animation command written.");
            }
            catch (Exception ex) { _undoService.Rollback(); Log.ErrorF("OPClassDemoFE7View.NWrite: {0}", ex.Message); }
        }

        // -- IdFieldControl handlers (#360 final) ---------------------------

        static uint ClassAddrFor(uint classId)
        {
            var rom = CoreState.ROM;
            if (rom?.RomInfo == null) return 0;
            uint classPtr = rom.RomInfo.class_pointer;
            if (classPtr == 0) return 0;
            uint baseAddr = rom.p32(classPtr);
            if (!U.isSafetyOffset(baseAddr, rom)) return 0;
            uint dataSize = rom.RomInfo.class_datasize;
            if (dataSize == 0) return 0;
            uint entryAddr = baseAddr + classId * dataSize;
            if (!U.isSafetyOffset(entryAddr, rom)) return 0;
            if (!U.isSafetyOffset(entryAddr + dataSize - 1, rom)) return 0;
            return entryAddr;
        }

        void ClassId_Jump(object? sender, RoutedEventArgs e)
        {
            try
            {
                uint addr = ClassAddrFor(ClassIdBox.Value);
                if (addr == 0) return;
                if (CoreState.ROM?.RomInfo?.version == 6)
                    WindowManager.Instance.Navigate<ClassFE6View>(addr);
                else
                    WindowManager.Instance.Navigate<ClassEditorView>(addr);
            }
            catch (Exception ex) { Log.ErrorF("OPClassDemoFE7View.ClassId_Jump failed: {0}", ex.Message); }
        }

        async void ClassId_Pick(object? sender, RoutedEventArgs e)
        {
            try
            {
                uint addr = ClassAddrFor(ClassIdBox.Value);
                PickResult? result;
                if (CoreState.ROM?.RomInfo?.version == 6)
                    result = await WindowManager.Instance.PickFromEditor<ClassFE6View>(addr);
                else
                    result = await WindowManager.Instance.PickFromEditor<ClassEditorView>(addr);
                if (result != null) ClassIdBox.Value = (uint)result.Index;
            }
            catch (Exception ex) { Log.ErrorF("OPClassDemoFE7View.ClassId_Pick failed: {0}", ex.Message); }
        }

        void ClassId_ValueChanged(object? sender, IdFieldValueChangedEventArgs e)
        {
            // NameResolver returns a fallback on failure (Copilot review #638).
            ClassIdBox.NameText = NameResolver.GetClassName(e.NewValue);
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
    }
}
