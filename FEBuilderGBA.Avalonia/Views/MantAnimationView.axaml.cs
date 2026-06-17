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
            try
            {
                var items = _vm.LoadList();
                EntryList.SetItemsWithIcons(items, i => ListIconLoaders.BattleAnimeTextLoader(items, i));
            }
            catch (Exception ex)
            {
                Log.Error("MantAnimationView.LoadList failed: " + ex.ToString());
            }
        }

        void OnSelected(uint addr)
        {
            try
            {
                _vm.LoadEntry(addr);
                UpdateUI();
            }
            catch (Exception ex)
            {
                Log.Error("MantAnimationView.OnSelected failed: " + ex.ToString());
            }
        }

        void UpdateUI()
        {
            AddrLabel.Text = string.Format("0x{0:X08}", _vm.CurrentAddr);
            PointerBox.Value = _vm.P0;
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.IsLoaded || _vm.CurrentAddr == 0) return;
            _undoService.Begin("Edit Mant Animation");
            try
            {
                _vm.P0 = (uint)(PointerBox.Value ?? 0);
                _vm.Write(_undoService.GetActiveUndoData()!);
                _undoService.Commit();
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
                if (!_vm.IsLoaded) return;
                uint animeId = _vm.GetJumpBattleAnimeId();

                // ImageBattleAnimeView.EntryList stores ROM offsets
                // (baseAddr + id*4 where baseAddr = p32(image_battle_animelist_pointer)),
                // so convert the animation id into that offset before
                // navigating — NavigateTo == EntryList.SelectAddress matches on
                // the slot offset, not the id.
                ROM rom = CoreState.ROM;
                if (rom?.RomInfo == null) return;
                uint listPtr = rom.RomInfo.image_battle_animelist_pointer;
                if (listPtr == 0) return;
                uint baseAddr = rom.p32(listPtr);
                if (!U.isSafetyOffset(baseAddr, rom)) return;
                uint slotAddr = baseAddr + animeId * 4;
                if (!U.isSafetyOffset(slotAddr, rom)) return;

                WindowManager.Instance.Navigate<ImageBattleAnimeView>(slotAddr);
            }
            catch (Exception ex)
            {
                Log.Error("MantAnimationView.JumpToBattleAnime_Click failed: " + ex.ToString());
            }
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

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
        public ViewModelBase? DataViewModel => _vm;
    }
}
