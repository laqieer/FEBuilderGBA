using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class ImageBattleAnimeView : Window, IEditorView
    {
        readonly ImageBattleAnimeViewModel _vm = new();
        readonly UndoService _undoService = new();

        public string ViewTitle => "Battle Animation Editor";
        public bool IsLoaded => _vm.IsLoaded;

        public ImageBattleAnimeView()
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
                EntryList.SetItems(items);

                // Show total animation count in summary
                int count = _vm.CountAnimations();
                AnimeCountLabel.Text = $"Total animations in table: {count}";
            }
            catch (Exception ex)
            {
                Log.Error("ImageBattleAnimeView.LoadList failed: {0}", ex.Message);
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
                Log.Error("ImageBattleAnimeView.OnSelected failed: {0}", ex.Message);
            }
            finally { _vm.IsLoading = false; _vm.MarkClean(); }
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            WeaponTypeBox.Value = _vm.WeaponType;
            SpecialBox.Value = _vm.Special;
            AnimationNumberBox.Value = _vm.AnimationNumber;
            WeaponTypeNameLabel.Text = _vm.WeaponTypeName;

            // Update animation details panel
            if (_vm.HasAnimeDetails)
            {
                AnimeDetailsPanel.IsVisible = true;
                NoAnimeDetailsLabel.IsVisible = false;

                AnimeNameLabel.Text = _vm.AnimeName;
                AnimeDataAddrLabel.Text = $"0x{_vm.AnimeDataAddr:X08}";
                SectionPointerLabel.Text = _vm.SectionPointer;
                FramePointerLabel.Text = _vm.FramePointer;
                OamRtLPointerLabel.Text = _vm.OamRtLPointer;
                OamLtRPointerLabel.Text = _vm.OamLtRPointer;
                PalettePointerLabel.Text = _vm.PalettePointer;
                FrameLZ77Label.Text = _vm.FrameLZ77Info;
                OamLZ77Label.Text = _vm.OamLZ77Info;
            }
            else
            {
                AnimeDetailsPanel.IsVisible = false;
                NoAnimeDetailsLabel.IsVisible = _vm.AnimationNumber == 0
                    ? false  // ID 0 means "none", no need to show error
                    : true;
                // For ID 0, show nothing; for invalid IDs, show the "not found" message
                if (_vm.AnimationNumber == 0)
                    NoAnimeDetailsLabel.IsVisible = false;
            }
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            _undoService.Begin("Edit Battle Animation");
            try
            {
                _vm.WeaponType = (uint)(WeaponTypeBox.Value ?? 0);
                _vm.Special = (uint)(SpecialBox.Value ?? 0);
                _vm.AnimationNumber = (uint)(AnimationNumberBox.Value ?? 0);
                _vm.Write();
                _undoService.Commit();
                _vm.MarkClean();

                // Refresh animation details after write
                _vm.LoadAnimationDetails(_vm.AnimationNumber);
                _vm.WeaponTypeName = ImageBattleAnimeViewModel.ResolveSPTypeName(_vm.WeaponType, _vm.Special);
                UpdateUI();
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                CoreState.Services.ShowError($"Write failed: {ex.Message}");
            }
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
    }
}
