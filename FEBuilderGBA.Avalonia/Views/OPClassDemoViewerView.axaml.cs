using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class OPClassDemoViewerView : Window, IEditorView, IDataVerifiableView
    {
        readonly OPClassDemoViewerViewModel _vm = new();

        public string ViewTitle => "OP Class Demo Editor";
        public bool IsLoaded => _vm.CanWrite;
        public ViewModelBase? DataViewModel => _vm;

        public OPClassDemoViewerView()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            Opened += (_, _) => LoadList();
        }

        void LoadList()
        {
            try { var items = _vm.LoadOPClassDemoList(); EntryList.SetItems(items); }
            catch (Exception ex) { Log.Error("OPClassDemoViewerView.LoadList: {0}", ex.Message); }
        }

        void OnSelected(uint addr)
        {
            try
            {
                _vm.LoadOPClassDemo(addr);
                UpdateUI();
            }
            catch (Exception ex) { Log.Error("OPClassDemoViewerView.OnSelected: {0}", ex.Message); }
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            EnglishNamePtrBox.Value = _vm.EnglishNamePointer;
            DescTextIdBox.Value = _vm.DescriptionTextId;
            JpNamePtrBox.Value = _vm.JapaneseNamePointer;
            JpNameLenBox.Value = _vm.JapaneseNameLength;
            PaletteIdBox.Value = _vm.PaletteId;
            DisplayWeaponBox.Value = _vm.DisplayWeapon;
            AllyEnemyColorBox.Value = _vm.AllyEnemyColor;
            BattleAnimeBox.Value = _vm.BattleAnime;
            MagicEffectBox.Value = _vm.MagicEffect;
            Unknown18Box.Value = _vm.Unknown18;
            TerrainLeftBox.Value = _vm.TerrainLeft;
            TerrainRightBox.Value = _vm.TerrainRight;
            AnimePtrBox.Value = _vm.AnimePointer;
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.CanWrite) return;
            _vm.EnglishNamePointer = (uint)(EnglishNamePtrBox.Value ?? 0);
            _vm.DescriptionTextId = (uint)(DescTextIdBox.Value ?? 0);
            _vm.JapaneseNamePointer = (uint)(JpNamePtrBox.Value ?? 0);
            _vm.JapaneseNameLength = (uint)(JpNameLenBox.Value ?? 0);
            _vm.PaletteId = (uint)(PaletteIdBox.Value ?? 0);
            _vm.DisplayWeapon = (uint)(DisplayWeaponBox.Value ?? 0);
            _vm.AllyEnemyColor = (uint)(AllyEnemyColorBox.Value ?? 0);
            _vm.BattleAnime = (uint)(BattleAnimeBox.Value ?? 0);
            _vm.MagicEffect = (uint)(MagicEffectBox.Value ?? 0);
            _vm.Unknown18 = (uint)(Unknown18Box.Value ?? 0);
            _vm.TerrainLeft = (uint)(TerrainLeftBox.Value ?? 0);
            _vm.TerrainRight = (uint)(TerrainRightBox.Value ?? 0);
            _vm.AnimePointer = (uint)(AnimePtrBox.Value ?? 0);
            _vm.WriteOPClassDemo();
            CoreState.Services?.ShowInfo("OP Class Demo data written.");
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
    }
}
