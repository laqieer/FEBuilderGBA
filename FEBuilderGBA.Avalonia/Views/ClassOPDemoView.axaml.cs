using System;
using System.Collections.Generic;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;
using FEBuilderGBA.Core;

namespace FEBuilderGBA.Avalonia.Views
{
    /// <summary>
    /// Class OP Demo Editor view — Avalonia parity for the orphan WinForms
    /// `ClassOPDemoForm` (gap-sweep #405). The orphan WF form is
    /// `<Compile Remove>`'d from the WinForms build but its Designer.cs is
    /// parsed by the gap-sweep tooling, so this view rebuilds to match
    /// that surface (NOT the canonical `OPClassDemoForm`/`OPClassDemoViewerView`
    /// PR #544 built).
    ///
    /// Differences from PR #544's `OPClassDemoViewerView`:
    ///   - N1 (JP-name font glyphs) has NO 16-entry cap; the orphan
    ///     validator stops only at the 0xFF terminator.
    ///   - N2 (anime spec) is a SINGLE 6-byte tuple, NOT a (Cmd, Arg)
    ///     command stream. The orphan validator is `i &lt; 1`.
    ///   - No patch-aware UI (orphan constructor has no PatchUtil calls).
    ///   - `N1_ListExpand_Click` expands the N1 sub-block; the WF
    ///     `N1_AddressListExpandsButton` lives on the N1 panel, not the
    ///     main table.
    /// </summary>
    public partial class ClassOPDemoView : TranslatedWindow, IEditorView, IDataVerifiableView
    {
        readonly ClassOPDemoViewModel _vm = new();
        readonly UndoService _undoService = new();

        // N1 selection — JP-name font glyph row (1 byte each).
        List<ClassOPDemoViewModel.N1Row> _n1Rows = new();
        uint _n1SelectedAddr;

        // N2 selection — base address of the 6-byte anime spec tuple.
        // Stored as the dereferenced ROM offset (no 0x08000000 high bit).
        uint _n2BaseAddr;

        public string ViewTitle => "Class OP Demo Editor";
        public bool IsLoaded => _vm.IsLoaded;
        public ViewModelBase? DataViewModel => _vm;

        public ClassOPDemoView()
        {
            InitializeComponent();

            EntryList.SelectedAddressChanged += OnSelected;
            N1List.SelectedAddressChanged += OnN1Selected;
            AnimeSharedList.SelectedAddressChanged += OnAnimeSharedSelected;

            // ValueChanged previews — gated by `_vm.IsLoading` so the
            // initial population during `OnSelected` doesn't trigger
            // a cascade of redundant preview updates.
            EnglishNamePtrBox.ValueChanged += OnEnglishNamePtrChanged;
            DescTextIdBox.ValueChanged += OnDescTextIdChanged;
            JpNamePtrBox.ValueChanged += OnJpNamePtrChanged;
            PaletteIdBox.ValueChanged += OnPaletteIdChanged;
            DisplayWeaponBox.ValueChanged += OnDisplayWeaponChanged;
            AnimePtrBox.ValueChanged += OnAnimePtrChanged;
            // N2B2 is the special-spec byte that the 3-choice combo maps to
            // — NOT N2B0 which is the fixed 0x05 marker (per the orphan
            // WF designer + Copilot CLI plan-review v2 finding #2 nit).
            N2B2Box.ValueChanged += OnN2B2Changed;

            // ComboBox.Items strings are NOT scanned by ViewTranslationHelper
            // (Copilot bot review thread `PRRT_kwDOH0Mc1M6ETSJC` on PR #544),
            // so route them through R._() so ja/zh locales pick them up.
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

            // The 3-choice combo for N2_B2 (anime special spec) —
            // 01 = Normal, 02 = Critical, 03 = Ranged / Magic Sword.
            N2CmdCombo.Items.Add(R._("01 = Normal"));
            N2CmdCombo.Items.Add(R._("02 = Critical"));
            N2CmdCombo.Items.Add(R._("03 = Ranged / Magic Sword"));

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
                {
                    // Combo idx 0/1/2 maps to spec byte 1/2/3.
                    N2B2Box.Value = idx + 1;
                }
            };

            Opened += (_, _) => LoadList();
        }

        // -----------------------------------------------------------------
        // Main list population.
        // -----------------------------------------------------------------

        void LoadList()
        {
            _vm.IsLoading = true;
            try
            {
                var items = _vm.LoadClassOPDemoList();
                EntryList.SetItemsWithIcons(items, i => ListIconLoaders.ClassIconLoader(items, i));
                if (CoreState.ROM?.RomInfo != null && TopBar != null)
                {
                    TopBar.StartAddressText = $"0x{CoreState.ROM.RomInfo.op_class_demo_pointer:X08}";
                    TopBar.ReadCountText = items.Count.ToString();
                }
            }
            catch (Exception ex)
            {
                Log.Error("ClassOPDemoView.LoadList failed: {0}", ex.Message);
            }
            finally
            {
                _vm.IsLoading = false;
                _vm.MarkClean();
            }
        }

        void OnSelected(uint addr)
        {
            _vm.IsLoading = true;
            try
            {
                _vm.LoadClassOPDemo(addr);
                UpdateUI();
                LoadN1Sublist();
                LoadN2Tuple();
                LoadAnimeSharedSync();
            }
            catch (Exception ex)
            {
                Log.Error("ClassOPDemoView.OnSelected failed: {0}", ex.Message);
            }
            finally
            {
                _vm.IsLoading = false;
                _vm.MarkClean();
            }
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            AddressBox.Value = _vm.CurrentAddr;
            SelectedAddressBox.Text = $"0x{_vm.CurrentAddr:X08}";

            EnglishNamePtrBox.Value = _vm.P0;
            EnglishNamePreview.Text = _vm.P0 != 0 ? $"0x{_vm.P0:X08}" : "";

            DescTextIdBox.Value = _vm.D4;
            try { DescTextPreview.Text = _vm.D4 != 0 ? NameResolver.GetTextById(_vm.D4) : ""; }
            catch { DescTextPreview.Text = ""; }

            JpNamePtrBox.Value = _vm.P8;
            JpNameLenBox.Value = _vm.B12;

            PaletteIdBox.Value = _vm.B13;
            PalettePreview.Text = _vm.B13 == 0xFF
                ? R._("Default palette") : $"Palette 0x{_vm.B13:X02}";

            DisplayWeaponBox.Value = _vm.B14;
            try { ClassNamePreview.Text = NameResolver.GetClassName(_vm.B14); }
            catch { ClassNamePreview.Text = ""; }

            AllyEnemyColorBox.Value = _vm.B15;
            {
                int v = (int)_vm.B15;
                AllyEnemyColorCombo.SelectedIndex = (v >= 0 && v < AllyEnemyColorCombo.ItemCount) ? v : -1;
            }

            BattleAnimeBox.Value = _vm.B16;

            MagicEffectBox.Value = _vm.B17;
            {
                int v = (int)_vm.B17;
                MagicEffectCombo.SelectedIndex = (v >= 0 && v < MagicEffectCombo.ItemCount) ? v : -1;
            }

            Unknown18Box.Value = _vm.D18;
            TerrainLeftBox.Value = _vm.B22;
            TerrainRightBox.Value = _vm.B23;
            AnimePtrBox.Value = _vm.P24;
        }

        // -----------------------------------------------------------------
        // ValueChanged previews — gated by `_vm.IsLoading`.
        // -----------------------------------------------------------------

        void OnEnglishNamePtrChanged(object? sender, NumericUpDownValueChangedEventArgs e)
        {
            if (_vm.IsLoading) return;
            uint ptr = (uint)(EnglishNamePtrBox.Value ?? 0);
            EnglishNamePreview.Text = ptr != 0 ? $"0x{ptr:X08}" : "";
        }

        void OnDescTextIdChanged(object? sender, NumericUpDownValueChangedEventArgs e)
        {
            if (_vm.IsLoading) return;
            uint id = (uint)(DescTextIdBox.Value ?? 0);
            try { DescTextPreview.Text = id != 0 ? NameResolver.GetTextById(id) : ""; }
            catch { DescTextPreview.Text = ""; }
        }

        void OnJpNamePtrChanged(object? sender, NumericUpDownValueChangedEventArgs e)
        {
            if (_vm.IsLoading) return;
            // Reload N1 from the in-progress spinner value (Copilot bot
            // review thread `PRRT_kwDOH0Mc1M6ETj_F` on PR #544).
            uint offset = (uint)(JpNamePtrBox.Value ?? 0);
            LoadN1SublistFromOffset(offset);
        }

        void OnPaletteIdChanged(object? sender, NumericUpDownValueChangedEventArgs e)
        {
            if (_vm.IsLoading) return;
            uint id = (uint)(PaletteIdBox.Value ?? 0);
            PalettePreview.Text = id == 0xFF
                ? R._("Default palette") : $"Palette 0x{id:X02}";
        }

        void OnDisplayWeaponChanged(object? sender, NumericUpDownValueChangedEventArgs e)
        {
            if (_vm.IsLoading) return;
            uint id = (uint)(DisplayWeaponBox.Value ?? 0);
            try { ClassNamePreview.Text = NameResolver.GetClassName(id); }
            catch { ClassNamePreview.Text = ""; }
        }

        void OnAnimePtrChanged(object? sender, NumericUpDownValueChangedEventArgs e)
        {
            if (_vm.IsLoading) return;
            uint offset = (uint)(AnimePtrBox.Value ?? 0);
            LoadN2TupleFromOffset(offset);
            LoadAnimeSharedFromOffset(offset);
        }

        void OnN2B2Changed(object? sender, NumericUpDownValueChangedEventArgs e)
        {
            if (_vm.IsLoading) return;
            int val = (int)(N2B2Box.Value ?? 0);
            // Map spec byte 1/2/3 -> combo idx 0/1/2. Anything else -> -1.
            int idx = (val >= 1 && val <= 3) ? val - 1 : -1;
            if (N2CmdCombo.SelectedIndex != idx)
                N2CmdCombo.SelectedIndex = idx;
        }

        // -----------------------------------------------------------------
        // N1 sub-list management.
        // -----------------------------------------------------------------

        void LoadN1Sublist()
        {
            // Reset _n1SelectedAddr BEFORE replacing the list (PR #544
            // stale-write fix `Copilot CLI re-review #4`).
            _n1SelectedAddr = 0;
            if (_vm.CurrentAddr == 0) { ApplyN1Rows(new List<ClassOPDemoViewModel.N1Row>()); return; }
            var rows = _vm.LoadN1FontList(_vm.CurrentAddr + 8);
            ApplyN1Rows(rows);
        }

        void LoadN1SublistFromOffset(uint baseOffset)
        {
            _n1SelectedAddr = 0;
            var rows = _vm.LoadN1FontListFromOffset(baseOffset);
            ApplyN1Rows(rows);
        }

        void ApplyN1Rows(List<ClassOPDemoViewModel.N1Row> rows)
        {
            try
            {
                _vm.IsLoading = true;
                try
                {
                    N1B0Box.Value = 0;
                    N1AddrBox.Value = 0;
                    N1SelectedAddressBox.Text = "";
                }
                finally { _vm.IsLoading = false; }

                _n1Rows = rows;
                var items = new List<AddrResult>();
                for (int i = 0; i < _n1Rows.Count; i++)
                {
                    items.Add(new AddrResult(_n1Rows[i].Addr, $"{i:X2}: 0x{_n1Rows[i].GlyphId:X2}", (uint)i));
                }
                N1List.SetItems(items);
                if (_n1Rows.Count > 0)
                {
                    N1AddrBox.Value = _n1Rows[0].Addr;
                }
            }
            catch (Exception ex)
            {
                Log.Error("ClassOPDemoView.ApplyN1Rows failed: {0}", ex.Message);
            }
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
            catch (Exception ex)
            {
                Log.Error("ClassOPDemoView.OnN1Selected failed: {0}", ex.Message);
            }
        }

        // -----------------------------------------------------------------
        // N2 single-tuple management.
        // -----------------------------------------------------------------

        void LoadN2Tuple()
        {
            // Reset _n2BaseAddr BEFORE refreshing the tuple display so a
            // follow-up Write_Click cannot land on a stale base address.
            _n2BaseAddr = 0;
            if (_vm.CurrentAddr == 0) { ApplyN2Tuple(null); return; }
            var tuple = _vm.LoadN2Tuple(_vm.CurrentAddr + 24);
            ApplyN2Tuple(tuple);
        }

        void LoadN2TupleFromOffset(uint baseOffset)
        {
            _n2BaseAddr = 0;
            var tuple = _vm.LoadN2TupleFromOffset(baseOffset);
            ApplyN2Tuple(tuple);
        }

        void ApplyN2Tuple(ClassOPDemoViewModel.N2Tuple? tuple)
        {
            _vm.IsLoading = true;
            try
            {
                if (tuple.HasValue)
                {
                    _n2BaseAddr = tuple.Value.Addr;
                    N2AddrBox.Value = tuple.Value.Addr;
                    N2SelectedAddressBox.Text = $"0x{tuple.Value.Addr:X08}";
                    N2B0Box.Value = tuple.Value.B0;
                    N2B1Box.Value = tuple.Value.B1;
                    N2B2Box.Value = tuple.Value.B2;
                    N2B3Box.Value = tuple.Value.B3;
                    N2B4Box.Value = tuple.Value.B4;
                    N2B5Box.Value = tuple.Value.B5;
                    int v = (int)tuple.Value.B2;
                    N2CmdCombo.SelectedIndex = (v >= 1 && v <= 3) ? v - 1 : -1;
                }
                else
                {
                    N2AddrBox.Value = 0;
                    N2SelectedAddressBox.Text = "";
                    N2B0Box.Value = 0;
                    N2B1Box.Value = 0;
                    N2B2Box.Value = 0;
                    N2B3Box.Value = 0;
                    N2B4Box.Value = 0;
                    N2B5Box.Value = 0;
                    N2CmdCombo.SelectedIndex = -1;
                }
            }
            finally { _vm.IsLoading = false; }
        }

        // -----------------------------------------------------------------
        // Anime Spec Shared list (read-only mirror).
        // -----------------------------------------------------------------

        void LoadAnimeSharedSync()
        {
            uint p24Offset = _vm.P24;
            LoadAnimeSharedFromOffset(p24Offset);
        }

        void LoadAnimeSharedFromOffset(uint p24Offset)
        {
            try
            {
                // AddressListControl.SetItems calls SelectFirst() which fires
                // SelectedAddressChanged → OnAnimeSharedSelected. We gate the
                // jump on the population pass so the auto-select on row 0
                // does NOT jump the main list back to the first sibling
                // (Copilot bot review thread PRRT_kwDOH0Mc1M6EWIzW).
                _suppressAnimeSharedJump = true;
                try
                {
                    if (p24Offset == 0 || _vm.CurrentAddr == 0)
                    {
                        AnimeSharedList.SetItems(new List<AddrResult>());
                        return;
                    }
                    var siblings = _vm.LoadAnimeSharedList(p24Offset, _vm.CurrentAddr);
                    AnimeSharedList.SetItems(siblings);
                }
                finally { _suppressAnimeSharedJump = false; }
            }
            catch (Exception ex)
            {
                Log.Error("ClassOPDemoView.LoadAnimeShared failed: {0}", ex.Message);
            }
        }

        // True while LoadAnimeSharedFromOffset is populating the list — used
        // to suppress the auto-jump triggered by SetItems' SelectFirst() call.
        bool _suppressAnimeSharedJump;

        void OnAnimeSharedSelected(uint addr)
        {
            // Selecting an anime-shared sibling jumps the main list to
            // that entry. Read-only mirror — no ROM write here. Skip the
            // jump when the selection came from SetItems' auto-select on
            // population (gated by _suppressAnimeSharedJump).
            if (_suppressAnimeSharedJump) return;
            if (addr == 0) return;
            EntryList.SelectAddress(addr);
        }

        // -----------------------------------------------------------------
        // Click handlers (all ROM writes wrapped in `_undoService`).
        // -----------------------------------------------------------------

        // #668: routed event from the unified EditorTopBar control.
        void OnTopBarReloadRequested(object? sender, RoutedEventArgs e) => LoadList();

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.IsLoaded) return;
            _undoService.Begin("Edit Class OP Demo (Main)");
            try
            {
                _vm.P0 = (uint)(EnglishNamePtrBox.Value ?? 0);
                _vm.D4 = (uint)(DescTextIdBox.Value ?? 0);
                _vm.P8 = (uint)(JpNamePtrBox.Value ?? 0);
                _vm.B12 = (uint)(JpNameLenBox.Value ?? 0);
                _vm.B13 = (uint)(PaletteIdBox.Value ?? 0);
                _vm.B14 = (uint)(DisplayWeaponBox.Value ?? 0);
                _vm.B15 = (uint)(AllyEnemyColorBox.Value ?? 0);
                _vm.B16 = (uint)(BattleAnimeBox.Value ?? 0);
                _vm.B17 = (uint)(MagicEffectBox.Value ?? 0);
                _vm.D18 = (uint)(Unknown18Box.Value ?? 0);
                _vm.B22 = (uint)(TerrainLeftBox.Value ?? 0);
                _vm.B23 = (uint)(TerrainRightBox.Value ?? 0);
                _vm.P24 = (uint)(AnimePtrBox.Value ?? 0);
                _vm.WriteClassOPDemo();
                _undoService.Commit();
                _vm.MarkClean();
                // Refresh sub-lists in case pointers changed.
                LoadN1Sublist();
                LoadN2Tuple();
                LoadAnimeSharedSync();
                CoreState.Services?.ShowInfo(R._("Class OP Demo data written."));
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.Error("ClassOPDemoView.Write failed: {0}", ex.Message);
                CoreState.Services?.ShowError(string.Format(R._("Failed to write Class OP Demo entry: {0}"), ex.Message));
            }
        }

        void N1_Write_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.IsLoaded || _n1SelectedAddr == 0)
            {
                CoreState.Services?.ShowError(R._("Select a JP name font row first."));
                return;
            }
            _undoService.Begin("Edit Class OP Demo (JP Name Font)");
            try
            {
                uint glyph = (uint)(N1B0Box.Value ?? 0);
                _vm.WriteN1Entry(_n1SelectedAddr, glyph);
                _undoService.Commit();
                LoadN1Sublist();
                CoreState.Services?.ShowInfo(R._("JP name font glyph written."));
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.Error("ClassOPDemoView.N1_Write failed: {0}", ex.Message);
                CoreState.Services?.ShowError(string.Format(R._("Failed to write JP name font glyph: {0}"), ex.Message));
            }
        }

        void N2_Write_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.IsLoaded || _n2BaseAddr == 0)
            {
                CoreState.Services?.ShowError(R._("Select an entry with a valid anime pointer first."));
                return;
            }
            _undoService.Begin("Edit Class OP Demo (Anime Spec Tuple)");
            try
            {
                uint b0 = (uint)(N2B0Box.Value ?? 0);
                uint b1 = (uint)(N2B1Box.Value ?? 0);
                uint b2 = (uint)(N2B2Box.Value ?? 0);
                uint b3 = (uint)(N2B3Box.Value ?? 0);
                uint b4 = (uint)(N2B4Box.Value ?? 0);
                uint b5 = (uint)(N2B5Box.Value ?? 0);
                _vm.WriteN2Tuple(_n2BaseAddr, b0, b1, b2, b3, b4, b5);
                _undoService.Commit();
                LoadN2Tuple();
                CoreState.Services?.ShowInfo(R._("Anime spec tuple written."));
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.Error("ClassOPDemoView.N2_Write failed: {0}", ex.Message);
                CoreState.Services?.ShowError(string.Format(R._("Failed to write anime spec tuple: {0}"), ex.Message));
            }
        }

        /// <summary>
        /// Write the currently-selected N1 row address back into the parent
        /// entry's P8 (Japanese Name Pointer) slot. Mirrors the WF orphan
        /// `N1_WriteButton` text "日本語名 / ポインタ書き込み" — the WF Write
        /// Pointer button repoints the parent slot at the selected sub-row.
        /// Wraps the ROM write in its own undo scope per Copilot bot review
        /// thread `PRRT_kwDOH0Mc1M6EWIza` (button must be wired, not inert).
        /// </summary>
        void N1_WritePtr_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.IsLoaded || _n1SelectedAddr == 0)
            {
                CoreState.Services?.ShowError(R._("Select a JP name font row first."));
                return;
            }
            _undoService.Begin("Edit Class OP Demo (JP Name Pointer)");
            try
            {
                ROM rom = CoreState.ROM!;
                rom.write_p32(_vm.CurrentAddr + 8, _n1SelectedAddr);
                _undoService.Commit();

                // Reload to reflect the new pointer in the main detail panel
                // (the spinner and the N1 sub-list).
                _vm.IsLoading = true;
                try
                {
                    _vm.LoadClassOPDemo(_vm.CurrentAddr);
                    UpdateUI();
                }
                finally { _vm.IsLoading = false; }
                LoadN1Sublist();
                CoreState.Services?.ShowInfo(R._("JP name pointer written."));
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.Error("ClassOPDemoView.N1_WritePtr failed: {0}", ex.Message);
                CoreState.Services?.ShowError(string.Format(R._("Failed to write JP name pointer: {0}"), ex.Message));
            }
        }

        /// <summary>
        /// Write the current N2 tuple base address back into the parent
        /// entry's P24 (Anime Spec Pointer) slot. Mirrors the WF orphan
        /// `N2_WriteButton` text "アニメ指定 / ポインタ書き込み".
        /// </summary>
        void N2_WritePtr_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.IsLoaded || _n2BaseAddr == 0)
            {
                CoreState.Services?.ShowError(R._("Select an entry with a valid anime pointer first."));
                return;
            }
            _undoService.Begin("Edit Class OP Demo (Anime Spec Pointer)");
            try
            {
                ROM rom = CoreState.ROM!;
                rom.write_p32(_vm.CurrentAddr + 24, _n2BaseAddr);
                _undoService.Commit();

                _vm.IsLoading = true;
                try
                {
                    _vm.LoadClassOPDemo(_vm.CurrentAddr);
                    UpdateUI();
                }
                finally { _vm.IsLoading = false; }
                LoadN2Tuple();
                LoadAnimeSharedSync();
                CoreState.Services?.ShowInfo(R._("Anime spec pointer written."));
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.Error("ClassOPDemoView.N2_WritePtr failed: {0}", ex.Message);
                CoreState.Services?.ShowError(string.Format(R._("Failed to write anime spec pointer: {0}"), ex.Message));
            }
        }

        void N1_ListExpand_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.IsLoaded)
            {
                CoreState.Services?.ShowError(R._("Select a Class OP Demo entry first."));
                return;
            }
            _undoService.Begin("Expand Class OP Demo JP Name Font Block");
            try
            {
                // The orphan's `N1_AddressListExpandsButton` lives on the
                // N1 panel and expands the N1 sub-block (NOT the main
                // table). Use the per-row JP-name pointer slot at
                // CurrentAddr + 8.
                uint slot = _vm.CurrentAddr + 8;
                uint currentCount = (uint)_n1Rows.Count;
                var result = _vm.ExpandN1Block(slot, currentCount);
                if (!result.Success)
                {
                    _undoService.Rollback();
                    CoreState.Services?.ShowError(result.Error ?? R._("Expansion failed."));
                    return;
                }
                _undoService.Commit();
                LoadN1Sublist();
                CoreState.Services?.ShowInfo(string.Format(
                    R._("JP name font block expanded. New base: 0x{0:X08}, new count: {1}."),
                    result.NewBaseAddress, result.NewCount));
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.Error("ClassOPDemoView.N1_ListExpand failed: {0}", ex.Message);
                CoreState.Services?.ShowError(string.Format(R._("Failed to expand JP name font block: {0}"), ex.Message));
            }
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
    }
}
