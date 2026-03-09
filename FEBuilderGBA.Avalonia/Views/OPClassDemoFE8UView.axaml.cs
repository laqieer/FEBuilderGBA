using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class OPClassDemoFE8UView : Window, IEditorView, IDataVerifiableView
    {
        readonly OPClassDemoFE8UViewModel _vm = new();

        public string ViewTitle => "OP Class Demo (FE8U) Editor";
        public bool IsLoaded => _vm.CanWrite;
        public ViewModelBase? DataViewModel => _vm;

        public OPClassDemoFE8UView()
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
                EntryList.SetItems(items);
                if (!string.IsNullOrEmpty(_vm.UnavailableMessage))
                    UnavailableLabel.Text = _vm.UnavailableMessage;
            }
            catch (Exception ex)
            {
                Log.Error("OPClassDemoFE8UView.LoadList failed: {0}", ex.Message);
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
                Log.Error("OPClassDemoFE8UView.OnSelected failed: {0}", ex.Message);
            }
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            DescTextIdBox.Value = _vm.DescriptionTextId;
            DisplayWeaponBox.Value = _vm.DisplayWeapon;
            ClassIdBox.Value = _vm.ClassId;
            AllyEnemyColorBox.Value = _vm.AllyEnemyColor;
            BattleAnimeBox.Value = _vm.BattleAnime;
            TerrainLeftBox.Value = _vm.TerrainLeft;
            TerrainRightBox.Value = _vm.TerrainRight;
            MagicEffectBox.Value = _vm.MagicEffect;
            AnimeTypeBox.Value = _vm.AnimeType;
            AnimePtrBox.Value = _vm.AnimePointer;
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.CanWrite) return;
            _vm.DescriptionTextId = (uint)(DescTextIdBox.Value ?? 0);
            _vm.DisplayWeapon = (uint)(DisplayWeaponBox.Value ?? 0);
            _vm.ClassId = (uint)(ClassIdBox.Value ?? 0);
            _vm.AllyEnemyColor = (uint)(AllyEnemyColorBox.Value ?? 0);
            _vm.BattleAnime = (uint)(BattleAnimeBox.Value ?? 0);
            _vm.TerrainLeft = (uint)(TerrainLeftBox.Value ?? 0);
            _vm.TerrainRight = (uint)(TerrainRightBox.Value ?? 0);
            _vm.MagicEffect = (uint)(MagicEffectBox.Value ?? 0);
            _vm.AnimeType = (uint)(AnimeTypeBox.Value ?? 0);
            _vm.AnimePointer = (uint)(AnimePtrBox.Value ?? 0);
            _vm.WriteEntry();
            CoreState.Services?.ShowInfo("OP Class Demo (FE8U) data written.");
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
    }
}
