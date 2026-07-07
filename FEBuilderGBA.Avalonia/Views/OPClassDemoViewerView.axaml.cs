using System;
using global::Avalonia;
using System.Collections.Generic;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Controls;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;
using FEBuilderGBA.Core;

namespace FEBuilderGBA.Avalonia.Views
{
    /// <summary>
    /// OP Class Demo Editor — Avalonia parity for WinForms OPClassDemoForm.
    /// Rebuilt for gap-sweep #419: three-pane master-detail layout with two
    /// sub-lists (Japanese name font glyphs / animation commands) plus
    /// patch-aware affordances for OPClassReelSort and OPClassReelAnimationIDOver255.
    /// </summary>
    public partial class OPClassDemoViewerView : TranslatedUserControl, IEmbeddableEditor, IDataVerifiableView
    {
        readonly OPClassDemoViewerViewModel _vm = new();
        readonly UndoService _undoService = new();
        bool _hasLoadedList;

        // N1 sub-list state — Japanese name font glyphs (1 byte each, terminator 0xFF).
        List<OPClassDemoViewerViewModel.N1Row> _n1Rows = new();
        uint _n1SelectedAddr;

        // N2 sub-list state — animation commands (2 bytes: Cmd, Arg; terminator Cmd=0x00).
        List<OPClassDemoViewerViewModel.N2Row> _n2Rows = new();
        uint _n2SelectedAddr;

        public string ViewTitle => "OP Class Demo Editor";
        public new bool IsLoaded => _vm.CanWrite;
        public EditorDescriptor Descriptor => new("OP Class Demo Editor", 1534, 878, SizeToContent: true);
        public event EventHandler? CloseRequested;
        public ViewModelBase? DataViewModel => _vm;
        public void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);

        public OPClassDemoViewerView()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            N1List.SelectedAddressChanged += OnN1Selected;
            N2List.SelectedAddressChanged += OnN2Selected;
            DescTextIdBox.ValueChanged += OnDescTextIdChanged;
            // #950 T4: DisplayWeaponBox is now an IdFieldControl — its routed
            // ValueChanged (ClassId_ValueChanged) is wired in AXAML, not here.
            EnglishNamePtrBox.ValueChanged += OnEnglishNamePtrChanged;
            PaletteIdBox.ValueChanged += OnPaletteIdChanged;
            TerrainLeftBox.ValueChanged += OnTerrainLeftChanged;
            TerrainRightBox.ValueChanged += OnTerrainRightChanged;
            BattleAnimeBox.ValueChanged += OnBattleAnimeChanged;
            JpNamePtrBox.ValueChanged += OnJpNamePtrChanged;
            AnimePtrBox.ValueChanged += OnAnimePtrChanged;
            N2B0Box.ValueChanged += OnN2B0Changed;

            // Populate combo boxes. ComboBox.Items strings are NOT scanned by
            // ViewTranslationHelper (Copilot bot review thread
            // PRRT_kwDOH0Mc1M6ETSJC on PR #544), so route them through R._()
            // explicitly so ja/zh locales pick up the translation table entries.
            AllyEnemyColorCombo.Items.Add(R._("00 = Player"));
            AllyEnemyColorCombo.Items.Add(R._("01 = Enemy"));
            AllyEnemyColorCombo.Items.Add(R._("02 = NPC"));
            AllyEnemyColorCombo.Items.Add(R._("03 = Gray"));

            MagicEffectCombo.Items.Add(R._("00 = None"));
            MagicEffectCombo.Items.Add(R._("01 = Fire"));
            MagicEffectCombo.Items.Add(R._("02 = Thunder"));
            MagicEffectCombo.Items.Add(R._("03 = Live"));
            MagicEffectCombo.Items.Add(R._("04 = Light"));
            MagicEffectCombo.Items.Add(R._("05 = Mil"));
            MagicEffectCombo.Items.Add(R._("06 = Manakete"));
            MagicEffectCombo.Items.Add(R._("07 = Monster Magic"));
            MagicEffectCombo.Items.Add(R._("08 = Stone"));

            N2CmdCombo.Items.Add(R._("1 = Close-range attack animation"));
            N2CmdCombo.Items.Add(R._("2 = Close-range critical animation"));
            N2CmdCombo.Items.Add(R._("3 = Set anime state to 'hit effect applied'"));
            N2CmdCombo.Items.Add(R._("4 = Long-range attack animation"));
            N2CmdCombo.Items.Add(R._("5 = Wait N frames"));
            N2CmdCombo.Items.Add(R._("6 = Close-range dodge animation"));
            N2CmdCombo.Items.Add(R._("7 = (FE8 unused) Set anime state to 'hit effect applied'"));
            N2CmdCombo.Items.Add(R._("8 = Wait until anime reaches C01/C02/C18"));

            AllyEnemyColorCombo.SelectionChanged += (_, _) =>
            {
                if (_vm.IsLoading) return;
                if (AllyEnemyColorCombo.SelectedIndex >= 0)
                    AllyEnemyColorBox.Value = AllyEnemyColorCombo.SelectedIndex;
            };
            AllyEnemyColorBox.ValueChanged += (_, _) =>
            {
                if (_vm.IsLoading) return;
                int v = (int)(AllyEnemyColorBox.Value ?? 0);
                AllyEnemyColorCombo.SelectedIndex = (v >= 0 && v < AllyEnemyColorCombo.ItemCount) ? v : -1;
            };

            MagicEffectCombo.SelectionChanged += (_, _) =>
            {
                if (_vm.IsLoading) return;
                if (MagicEffectCombo.SelectedIndex >= 0)
                    MagicEffectBox.Value = MagicEffectCombo.SelectedIndex;
            };
            MagicEffectBox.ValueChanged += (_, _) =>
            {
                if (_vm.IsLoading) return;
                int v = (int)(MagicEffectBox.Value ?? 0);
                MagicEffectCombo.SelectedIndex = (v >= 0 && v < MagicEffectCombo.ItemCount) ? v : -1;
            };

            N2CmdCombo.SelectionChanged += (_, _) =>
            {
                if (_vm.IsLoading) return;
                int idx = N2CmdCombo.SelectedIndex;
                if (idx >= 0)
                    N2B0Box.Value = idx + 1; // combo entry "1=..." → value 1
            };

        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            if (!_hasLoadedList)
            {
                _hasLoadedList = true;
                ApplyPatchAwareUI();
                LoadList();
            }
        }

        // -----------------------------------------------------------------
        // Patch-aware UI: show ListExpand button only when OPClassReelSort
        // is installed; show the BattleAnime+1 label only when Over255 is
        // active (mirrors WF OPClassDemoForm constructor logic).
        // -----------------------------------------------------------------
        void ApplyPatchAwareUI()
        {
            bool listExpand = _vm.IsReelSortPatchActive;
            bool over255 = _vm.IsOver255PatchActive;
            ListExpandButton.IsVisible = listExpand;
            BattleAnimePlus1Label.IsVisible = over255;
            // Show the vanilla label only when the Over255 patch is NOT
            // installed so the slot always has a label. (Copilot bot
            // review thread PRRT_kwDOH0Mc1M6ETj-6 on PR #544.)
            Unknown18VanillaLabel.IsVisible = !over255;
            // Runtime sentences must be routed through R._() because
            // ViewTranslationHelper only translates static XAML attributes
            // (Copilot bot review thread PRRT_kwDOH0Mc1M6ETSJG on PR #544).
            if (over255)
            {
                PatchNoticeText.Text = R._("Patch: OPClassReelAnimationIDOver255 active. Battle Anime ID is read from D18.");
            }
            else if (listExpand)
            {
                PatchNoticeText.Text = R._("Patch: OPClassReelSort active. Data Expansion enabled.");
            }
            else
            {
                PatchNoticeText.Text = "";
            }
        }

        void OnDescTextIdChanged(object? sender, NumericUpDownValueChangedEventArgs e)
        {
            if (_vm.IsLoading) return;
            uint id = (uint)(DescTextIdBox.Value ?? 0);
            try { DescTextPreview.Text = id != 0 ? NameResolver.GetTextById(id) : ""; }
            catch { DescTextPreview.Text = ""; }
        }

        // #950 T4: DisplayWeaponBox is the B14 class field (WF J_14_CLASS).
        // Migrated to a class IdFieldControl — routed ValueChanged refreshes
        // the ClassNamePreview readout AND the IdFieldControl's own inline name
        // preview; Jump/Pick open the Class editor (ClassFE6View for FE6).
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
                uint addr = ClassAddrFor(DisplayWeaponBox.Value);
                if (addr == 0) return;
                if (CoreState.ROM?.RomInfo?.version == 6)
                    WindowManager.Instance.Navigate<ClassFE6View>(addr);
                else
                    WindowManager.Instance.Navigate<ClassEditorView>(addr);
            }
            catch (Exception ex) { Log.Error($"OPClassDemoViewerView.ClassId_Jump: {ex.Message}"); }
        }

        async void ClassId_Pick(object? sender, RoutedEventArgs e)
        {
            try
            {
                uint addr = ClassAddrFor(DisplayWeaponBox.Value);
                PickResult? result;
                if (CoreState.ROM?.RomInfo?.version == 6)
                    result = await WindowManager.Instance.PickFromEditor<ClassFE6View>(addr);
                else
                    result = await WindowManager.Instance.PickFromEditor<ClassEditorView>(addr);
                if (result != null) DisplayWeaponBox.Value = (uint)result.Index;
            }
            catch (Exception ex) { Log.Error($"OPClassDemoViewerView.ClassId_Pick: {ex.Message}"); }
        }

        void ClassId_ValueChanged(object? sender, IdFieldValueChangedEventArgs e)
        {
            if (_vm.IsLoading) return;
            try
            {
                string name = NameResolver.GetClassName(e.NewValue);
                ClassNamePreview.Text = name;
                DisplayWeaponBox.NameText = name;
            }
            catch { ClassNamePreview.Text = ""; }
        }

        void OnEnglishNamePtrChanged(object? sender, NumericUpDownValueChangedEventArgs e)
        {
            if (_vm.IsLoading) return;
            uint ptr = (uint)(EnglishNamePtrBox.Value ?? 0);
            EnglishNamePreview.Text = ptr != 0 ? $"0x{ptr:X08}" : "";
        }

        void OnPaletteIdChanged(object? sender, NumericUpDownValueChangedEventArgs e)
        {
            if (_vm.IsLoading) return;
            uint id = (uint)(PaletteIdBox.Value ?? 0);
            // Route through R._() so the preview stays localized when the
            // user edits the value interactively. (Copilot bot review
            // thread PRRT_kwDOH0Mc1M6ETZ4a on PR #544.)
            PalettePreview.Text = id == 0xFF ? R._("Default palette") : $"Palette 0x{id:X02}";
        }

        void OnTerrainLeftChanged(object? sender, NumericUpDownValueChangedEventArgs e)
        {
            if (_vm.IsLoading) return;
            uint id = (uint)(TerrainLeftBox.Value ?? 0);
            TerrainLeftPreview.Text = $"Terrain 0x{id:X02}";
        }

        void OnTerrainRightChanged(object? sender, NumericUpDownValueChangedEventArgs e)
        {
            if (_vm.IsLoading) return;
            uint id = (uint)(TerrainRightBox.Value ?? 0);
            TerrainRightPreview.Text = $"Terrain 0x{id:X02}";
        }

        void OnBattleAnimeChanged(object? sender, NumericUpDownValueChangedEventArgs e)
        {
            if (_vm.IsLoading) return;
            uint id = (uint)(BattleAnimeBox.Value ?? 0);
            BattleAnimePreview.Text = $"Anime 0x{id:X02}";
        }

        void OnJpNamePtrChanged(object? sender, NumericUpDownValueChangedEventArgs e)
        {
            if (_vm.IsLoading) return;
            // Reload from the *unsaved* spinner value so the preview reflects
            // the in-progress edit. (Copilot bot review thread
            // PRRT_kwDOH0Mc1M6ETj_F on PR #544.)
            uint offset = (uint)(JpNamePtrBox.Value ?? 0);
            LoadN1SublistFromOffset(offset);
        }

        void OnAnimePtrChanged(object? sender, NumericUpDownValueChangedEventArgs e)
        {
            if (_vm.IsLoading) return;
            uint offset = (uint)(AnimePtrBox.Value ?? 0);
            LoadN2SublistFromOffset(offset);
        }

        void OnN2B0Changed(object? sender, NumericUpDownValueChangedEventArgs e)
        {
            if (_vm.IsLoading) return;
            int val = (int)(N2B0Box.Value ?? 0);
            // Map command byte → combo index (1..8 → 0..7; anything else → -1).
            int idx = (val >= 1 && val <= 8) ? val - 1 : -1;
            if (N2CmdCombo.SelectedIndex != idx)
                N2CmdCombo.SelectedIndex = idx;
        }

        void LoadList()
        {
            _vm.IsLoading = true;
            try
            {
                var items = _vm.LoadOPClassDemoList();
                // #939: the row prefix is the row INDEX, not the class id. Key
                // the icon off the real Display Weapon class id at entry+14
                // (the value the row's class name is resolved from in the VM).
                EntryList.SetItemsWithIcons(items, i => ListIconLoaders.ClassIconLoader(items, i, ClassIdOf));
                if (CoreState.ROM?.RomInfo != null)
                {
                    // #649: display via the unified EditorTopBar read-only slots.
                    TopBar.StartAddressText = CoreState.ROM.RomInfo.op_class_demo_pointer.ToString();
                    TopBar.ReadCountText = items.Count.ToString();
                }
            }
            catch (Exception ex) { Log.Error($"OPClassDemoViewerView.LoadList: {ex.Message}"); }
            finally { _vm.IsLoading = false; _vm.MarkClean(); }
        }

        void OnSelected(uint addr)
        {
            _vm.IsLoading = true;
            try
            {
                _vm.LoadOPClassDemo(addr);
                UpdateUI();
                LoadN1Sublist();
                LoadN2Sublist();
            }
            catch (Exception ex) { Log.Error($"OPClassDemoViewerView.OnSelected: {ex.Message}"); }
            finally { _vm.IsLoading = false; _vm.MarkClean(); }
        }

        void LoadN1Sublist()
        {
            // Default: walk from the pointer-slot at _vm.CurrentAddr + 8.
            // Called on entry change.
            if (_vm.CurrentAddr == 0) { ResetN1ListState(clearOnly: true); return; }
            var rows = _vm.LoadN1FontList(_vm.CurrentAddr + 8);
            ApplyN1Rows(rows);
        }

        void LoadN1SublistFromOffset(uint baseOffset)
        {
            // Walk from an explicit ROM offset (the unsaved spinner value).
            // (Copilot bot review thread PRRT_kwDOH0Mc1M6ETj_F on PR #544.)
            var rows = _vm.LoadN1FontListFromOffset(baseOffset);
            ApplyN1Rows(rows);
        }

        void LoadN2Sublist()
        {
            if (_vm.CurrentAddr == 0) { ResetN2ListState(clearOnly: true); return; }
            var rows = _vm.LoadN2CommandList(_vm.CurrentAddr + 24);
            ApplyN2Rows(rows);
        }

        void LoadN2SublistFromOffset(uint baseOffset)
        {
            var rows = _vm.LoadN2CommandListFromOffset(baseOffset);
            ApplyN2Rows(rows);
        }

        void ResetN1ListState(bool clearOnly)
        {
            // Reset the selected-address gate BEFORE replacing the
            // list so a follow-up Write button click cannot land on
            // a stale address from the previous row. (Copilot CLI
            // re-review on PR #544 #4.)
            _n1SelectedAddr = 0;
            _vm.IsLoading = true;
            try { N1B0Box.Value = 0; }
            finally { _vm.IsLoading = false; }
            N1SelectedAddressBox.Text = "";
            if (clearOnly) { _n1Rows = new(); N1List.SetItems(new List<AddrResult>()); }
        }

        void ResetN2ListState(bool clearOnly)
        {
            _n2SelectedAddr = 0;
            _vm.IsLoading = true;
            try { N2B0Box.Value = 0; N2B1Box.Value = 0; N2CmdCombo.SelectedIndex = -1; }
            finally { _vm.IsLoading = false; }
            if (clearOnly) { _n2Rows = new(); N2List.SetItems(new List<AddrResult>()); }
        }

        void ApplyN1Rows(List<OPClassDemoViewerViewModel.N1Row> rows)
        {
            try
            {
                ResetN1ListState(clearOnly: false);
                _n1Rows = rows;
                var items = new List<AddrResult>();
                for (int i = 0; i < _n1Rows.Count; i++)
                {
                    items.Add(new AddrResult(_n1Rows[i].Addr, $"{i:X2}: 0x{_n1Rows[i].GlyphId:X2}", (uint)i));
                }
                N1List.SetItems(items);
            }
            catch (Exception ex) { Log.Error($"OPClassDemoViewerView.ApplyN1Rows: {ex.Message}"); }
        }

        void ApplyN2Rows(List<OPClassDemoViewerViewModel.N2Row> rows)
        {
            try
            {
                ResetN2ListState(clearOnly: false);
                _n2Rows = rows;
                var items = new List<AddrResult>();
                for (int i = 0; i < _n2Rows.Count; i++)
                {
                    items.Add(new AddrResult(_n2Rows[i].Addr, $"{i:X2}: Cmd={_n2Rows[i].Command:X2} Arg={_n2Rows[i].Argument}", (uint)i));
                }
                N2List.SetItems(items);
            }
            catch (Exception ex) { Log.Error($"OPClassDemoViewerView.ApplyN2Rows: {ex.Message}"); }
        }

        void OnN1Selected(uint addr)
        {
            _n1SelectedAddr = addr;
            N1SelectedAddressBox.Text = $"0x{addr:X08}";
            try
            {
                var rom = CoreState.ROM;
                if (rom != null && addr != 0 && addr < (uint)rom.Data.Length)
                {
                    _vm.IsLoading = true;
                    try { N1B0Box.Value = rom.u8(addr); }
                    finally { _vm.IsLoading = false; }
                }
            }
            catch (Exception ex) { Log.Error($"OPClassDemoViewerView.OnN1Selected: {ex.Message}"); }
        }

        void OnN2Selected(uint addr)
        {
            _n2SelectedAddr = addr;
            try
            {
                var rom = CoreState.ROM;
                if (rom != null && addr != 0 && addr + 2 <= (uint)rom.Data.Length)
                {
                    _vm.IsLoading = true;
                    try
                    {
                        N2B0Box.Value = rom.u8(addr);
                        N2B1Box.Value = rom.u8(addr + 1);
                        // NumericUpDown.Value is decimal? — use the
                        // null-safe form (Copilot bot review thread
                        // PRRT_kwDOH0Mc1M6ETZ4X on PR #544).
                        int val = (int)(N2B0Box.Value ?? 0);
                        N2CmdCombo.SelectedIndex = (val >= 1 && val <= 8) ? val - 1 : -1;
                    }
                    finally { _vm.IsLoading = false; }
                }
            }
            catch (Exception ex) { Log.Error($"OPClassDemoViewerView.OnN2Selected: {ex.Message}"); }
        }

        void UpdateUI()
        {
            // Every derived preview / combo / inline label must be
            // populated here because the ValueChanged handlers are
            // gated by `_vm.IsLoading` during load. (Copilot CLI
            // re-review on PR #544.)
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            AddressBox.Value = _vm.CurrentAddr;
            SelectedAddressBox.Text = $"0x{_vm.CurrentAddr:X08}";

            EnglishNamePtrBox.Value = _vm.EnglishNamePointer;
            EnglishNamePreview.Text = _vm.EnglishNamePointer != 0
                ? $"0x{_vm.EnglishNamePointer:X08}" : "";

            DescTextIdBox.Value = _vm.DescriptionTextId;
            try { DescTextPreview.Text = _vm.DescriptionTextId != 0 ? NameResolver.GetTextById(_vm.DescriptionTextId) : ""; }
            catch { DescTextPreview.Text = ""; }

            JpNamePtrBox.Value = _vm.JapaneseNamePointer;
            JpNameLenBox.Value = _vm.JapaneseNameLength;

            PaletteIdBox.Value = _vm.PaletteId;
            PalettePreview.Text = _vm.PaletteId == 0xFF
                ? R._("Default palette") : $"Palette 0x{_vm.PaletteId:X02}";

            DisplayWeaponBox.Value = _vm.DisplayWeapon;
            try
            {
                string className = NameResolver.GetClassName(_vm.DisplayWeapon);
                ClassNamePreview.Text = className;
                DisplayWeaponBox.NameText = className;
            }
            catch { ClassNamePreview.Text = ""; }

            AllyEnemyColorBox.Value = _vm.AllyEnemyColor;
            // The ValueChanged handler is gated by IsLoading, so sync
            // the combo SelectedIndex explicitly here. (Copilot bot
            // review thread PRRT_kwDOH0Mc1M6ETSI- on PR #544.)
            {
                int v = (int)_vm.AllyEnemyColor;
                AllyEnemyColorCombo.SelectedIndex = (v >= 0 && v < AllyEnemyColorCombo.ItemCount) ? v : -1;
            }

            BattleAnimeBox.Value = _vm.BattleAnime;
            BattleAnimePreview.Text = $"Anime 0x{_vm.BattleAnime:X02}";

            MagicEffectBox.Value = _vm.MagicEffect;
            {
                int v = (int)_vm.MagicEffect;
                MagicEffectCombo.SelectedIndex = (v >= 0 && v < MagicEffectCombo.ItemCount) ? v : -1;
            }

            Unknown18Box.Value = _vm.Unknown18;

            TerrainLeftBox.Value = _vm.TerrainLeft;
            TerrainLeftPreview.Text = $"Terrain 0x{_vm.TerrainLeft:X02}";

            TerrainRightBox.Value = _vm.TerrainRight;
            TerrainRightPreview.Text = $"Terrain 0x{_vm.TerrainRight:X02}";

            AnimePtrBox.Value = _vm.AnimePointer;
        }

        void ReloadList_Click(object? sender, RoutedEventArgs e)
        {
            ApplyPatchAwareUI();
            LoadList();
        }

        // #649: routed event from the unified EditorTopBar Reload button.
        void OnTopBarReloadRequested(object? sender, RoutedEventArgs e)
        {
            ApplyPatchAwareUI();
            LoadList();
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.CanWrite) return;
            _undoService.Begin("Edit OP Class Demo");
            try
            {
                _vm.EnglishNamePointer = (uint)(EnglishNamePtrBox.Value ?? 0);
                _vm.DescriptionTextId = (uint)(DescTextIdBox.Value ?? 0);
                _vm.JapaneseNamePointer = (uint)(JpNamePtrBox.Value ?? 0);
                _vm.JapaneseNameLength = (uint)(JpNameLenBox.Value ?? 0);
                _vm.PaletteId = (uint)(PaletteIdBox.Value ?? 0);
                // #950 T4: IdFieldControl.Value is a non-nullable uint.
                _vm.DisplayWeapon = DisplayWeaponBox.Value;
                _vm.AllyEnemyColor = (uint)(AllyEnemyColorBox.Value ?? 0);
                _vm.BattleAnime = (uint)(BattleAnimeBox.Value ?? 0);
                _vm.MagicEffect = (uint)(MagicEffectBox.Value ?? 0);
                _vm.Unknown18 = (uint)(Unknown18Box.Value ?? 0);
                _vm.TerrainLeft = (uint)(TerrainLeftBox.Value ?? 0);
                _vm.TerrainRight = (uint)(TerrainRightBox.Value ?? 0);
                _vm.AnimePointer = (uint)(AnimePtrBox.Value ?? 0);
                _vm.WriteOPClassDemo();
                _undoService.Commit();
                _vm.MarkClean();
                CoreState.Services?.ShowInfo("OP Class Demo data written.");
            }
            catch (Exception ex) { _undoService.Rollback(); Log.Error($"OPClassDemoViewerView.Write: {ex.Message}"); }
        }

        void N1_Write_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.CanWrite || _n1SelectedAddr == 0) return;
            _undoService.Begin("Edit OP Class Demo (JP Name Font Glyph)");
            try
            {
                uint glyph = (uint)(N1B0Box.Value ?? 0);
                _vm.WriteN1Entry(_n1SelectedAddr, glyph);
                _undoService.Commit();
                LoadN1Sublist();
                CoreState.Services?.ShowInfo("JP name font glyph written.");
            }
            catch (Exception ex) { _undoService.Rollback(); Log.Error($"OPClassDemoViewerView.N1_Write: {ex.Message}"); }
        }

        void N2_Write_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.CanWrite || _n2SelectedAddr == 0) return;
            _undoService.Begin("Edit OP Class Demo (Animation Command)");
            try
            {
                uint cmd = (uint)(N2B0Box.Value ?? 0);
                uint arg = (uint)(N2B1Box.Value ?? 0);
                _vm.WriteN2Entry(_n2SelectedAddr, cmd, arg);
                _undoService.Commit();
                LoadN2Sublist();
                CoreState.Services?.ShowInfo("Animation command written.");
            }
            catch (Exception ex) { _undoService.Rollback(); Log.Error($"OPClassDemoViewerView.N2_Write: {ex.Message}"); }
        }

        void ListExpand_Click(object? sender, RoutedEventArgs e)
        {
            _undoService.Begin("Expand OP Class Demo Table");
            try
            {
                var result = _vm.ExpandList();
                if (!result.Success)
                {
                    _undoService.Rollback();
                    CoreState.Services?.ShowError(result.Error ?? "Expansion failed.");
                    return;
                }
                _undoService.Commit();
                LoadList();
                CoreState.Services?.ShowInfo($"Table expanded. New base: 0x{result.NewBaseAddress:X08}, new count: {result.NewCount}.");
            }
            catch (Exception ex) { _undoService.Rollback(); Log.Error($"OPClassDemoViewerView.ListExpand: {ex.Message}"); }
        }

        // #939: resolve the real class id (Display Weapon, u8 at entry+14) for
        // the list icon. Guards a null ROM by returning 0 → the loader returns
        // null (no icon), never throws.
        static uint ClassIdOf(AddrResult r)
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return 0;
            if (!U.isSafetyOffset(r.addr + 14, rom)) return 0;
            return rom.u8(r.addr + 14);
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
    }
}
