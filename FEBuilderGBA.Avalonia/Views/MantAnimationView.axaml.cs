using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Dialogs;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    /// <summary>
    /// Avalonia counterpart of WinForms <c>MantAnimationForm</c> — the Cape /
    /// Mant flutter-animation editor (#1178). A list over the
    /// <c>mant_command</c> pointer table with a D0 pointer field, a Jump to the
    /// Battle Animation editor, and a count-aware list expand (the table is
    /// count-driven via <c>mant_command_count_address</c>, so growing the list
    /// rewrites that u8 — see <see cref="MantAnimationViewModel.ExpandList"/>).
    /// </summary>
    public partial class MantAnimationView : TranslatedWindow, IEditorView, IDataVerifiableView
    {
        readonly MantAnimationViewModel _vm = new();
        readonly UndoService _undoService = new();

        public string ViewTitle => "Mant Animation";
        public bool IsLoaded => _vm.IsLoaded;

        public MantAnimationView()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            Opened += (_, _) => LoadList();
        }

        void LoadList()
        {
            _vm.IsLoading = true;
            try
            {
                var items = _vm.LoadList();
                EntryList.SetItemsWithIcons(items, i => ListIconLoaders.BattleAnimeTextLoader(items, i));
            }
            catch (Exception ex)
            {
                Log.Error("MantAnimationView.LoadList failed: " + ex.ToString());
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
                Log.Error("MantAnimationView.OnSelected failed: " + ex.ToString());
            }
            finally { _vm.IsLoading = false; _vm.MarkClean(); }
        }

        void UpdateUI()
        {
            AddrLabel.Text = string.Format("0x{0:X08}", _vm.CurrentAddr);
            PointerBox.Text = string.Format("0x{0:X08}", _vm.P0);
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.IsLoaded || _vm.CurrentAddr == 0) return;
            _undoService.Begin("Edit Mant Animation");
            try
            {
                _vm.P0 = ParseHexText(PointerBox.Text);
                _vm.Write(_undoService.GetActiveUndoData()!);
                _undoService.Commit();
                _vm.MarkClean();
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.Error("MantAnimationView.Write_Click failed: " + ex.ToString());
                CoreState.Services?.ShowError(R._("Write failed: {0}", ex.Message));
            }
        }

        void JumpToBattleAnime_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                OpenBattleAnimeJump();
            }
            catch (Exception ex)
            {
                Log.Error("MantAnimationView.JumpToBattleAnime_Click failed: " + ex.ToString());
            }
        }

        /// <summary>
        /// Open the Battle Animation editor on the anime the current Mant row
        /// points to. Shared by the Jump button click handler and the #1408
        /// regression test so the test exercises the REAL call site (not a
        /// reconstructed chain).
        ///
        /// Passes the <b>1-based</b> anime id
        /// (<see cref="MantAnimationViewModel.GetJumpBattleAnime1BasedId"/>), NOT
        /// the WF zero-based <c>GetJumpBattleAnimeId()</c> — the class-centric
        /// editor's <see cref="ImageBattleAnimeView.NavigateToAnimeId"/> expects
        /// the 1-based id (it compares against the 1-based
        /// <c>ClassFormCore.GetAnimeIDByClassID</c> and subtracts one internally
        /// for the 32-byte table index). #1408 fixes the off-by-one introduced by
        /// #1407 (zero-based value → first row no-ops, later rows show the
        /// PREVIOUS anime).
        /// </summary>
        /// <returns>
        /// The opened <see cref="ImageBattleAnimeView"/>, or <c>null</c> when
        /// nothing is loaded / the selection has no valid (non-zero) anime id.
        /// </returns>
        internal ImageBattleAnimeView? OpenBattleAnimeJump()
        {
            if (!_vm.IsLoaded) return null;
            uint animeId = _vm.GetJumpBattleAnime1BasedId();
            if (animeId == 0) return null;

            // The Battle Animation Editor's left list is now CLASS-centric
            // (#1377): rows are per-class SP-record setting pointers, NOT the
            // 32-byte ANIME-DATA-table slots (animelist base + id*4). So a
            // jump that only knows an anime id must be routed through the
            // editor's NavigateToAnimeId, which lands on a class that uses
            // that anime (or shows the anime data directly when none does) —
            // the old slot-address Navigate would never match a row.
            var view = WindowManager.Instance.Open<ImageBattleAnimeView>();
            view.NavigateToAnimeId(animeId);
            return view;
        }

        async void ListExpand_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                if (!_vm.IsLoaded)
                {
                    CoreState.Services?.ShowInfo(R._("Load a ROM first."));
                    return;
                }
                if (_vm.ReadCount == 0)
                {
                    CoreState.Services?.ShowInfo(R._("Cannot expand: list is empty."));
                    return;
                }

                uint defaultCount = _vm.ReadCount + 1;
                if (defaultCount > 255) defaultCount = 255;
                uint? chosen = await NumberInputDialog.Show(
                    this,
                    R._("Enter the new entry count for the mant animation list (current: {0}, max: 255).",
                        _vm.ReadCount),
                    R._("List Expansion"),
                    defaultCount,
                    _vm.ReadCount,
                    255);
                if (chosen == null) return; // user cancelled
                uint newCount = chosen.Value;
                if (newCount == _vm.ReadCount)
                {
                    CoreState.Services?.ShowInfo(R._("No change: new count equals current count."));
                    return;
                }

                _undoService.Begin("Expand Mant Animation List");
                try
                {
                    string err = _vm.ExpandList(newCount, _undoService.GetActiveUndoData());
                    if (!string.IsNullOrEmpty(err))
                    {
                        _undoService.Rollback();
                        CoreState.Services?.ShowError(err);
                        return;
                    }
                    _undoService.Commit();
                    LoadList();
                    CoreState.Services?.ShowInfo(
                        R._("Expanded mant animation list to {0} entries.", newCount));
                }
                catch (Exception inner)
                {
                    _undoService.Rollback();
                    Log.Error("MantAnimationView.ListExpand_Click inner failed: " + inner.ToString());
                    CoreState.Services?.ShowError(R._("List expansion failed: {0}", inner.Message));
                }
            }
            catch (Exception ex)
            {
                Log.Error("MantAnimationView.ListExpand_Click failed: " + ex.ToString());
            }
        }

        static uint ParseHexText(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return 0;
            text = text.Trim();
            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                text = text.Substring(2);
            if (uint.TryParse(text, System.Globalization.NumberStyles.HexNumber, null, out uint val))
                return val;
            return 0;
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
        public ViewModelBase? DataViewModel => _vm;
    }
}
