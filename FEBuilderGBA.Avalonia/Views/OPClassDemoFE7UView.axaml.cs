using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class OPClassDemoFE7UView : Window, IEditorView, IDataVerifiableView
    {
        readonly OPClassDemoFE7UViewModel _vm = new();

        public string ViewTitle => "OP Class Demo (FE7U) Editor";
        public bool IsLoaded => _vm.CanWrite;
        public ViewModelBase? DataViewModel => _vm;

        public OPClassDemoFE7UView()
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
                Log.Error("OPClassDemoFE7UView.LoadList failed: {0}", ex.Message);
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
                Log.Error("OPClassDemoFE7UView.OnSelected failed: {0}", ex.Message);
            }
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            EnglishNamePtrBox.Value = _vm.EnglishNamePointer;
            DescTextIdBox.Value = _vm.DescriptionTextId;
            JpNameLenBox.Value = _vm.JapaneseNameLength;
            ClassIdBox.Value = _vm.ClassId;
            AllyEnemyColorBox.Value = _vm.AllyEnemyColor;
            BattleAnimeBox.Value = _vm.BattleAnime;
            MagicEffectBox.Value = _vm.MagicEffect;
            TerrainLeftBox.Value = _vm.TerrainLeft;
            TerrainRightBox.Value = _vm.TerrainRight;
            AnimePtrBox.Value = _vm.AnimePointer;
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.CanWrite) return;
            _vm.EnglishNamePointer = (uint)(EnglishNamePtrBox.Value ?? 0);
            _vm.DescriptionTextId = (uint)(DescTextIdBox.Value ?? 0);
            _vm.JapaneseNameLength = (uint)(JpNameLenBox.Value ?? 0);
            _vm.ClassId = (uint)(ClassIdBox.Value ?? 0);
            _vm.AllyEnemyColor = (uint)(AllyEnemyColorBox.Value ?? 0);
            _vm.BattleAnime = (uint)(BattleAnimeBox.Value ?? 0);
            _vm.MagicEffect = (uint)(MagicEffectBox.Value ?? 0);
            _vm.TerrainLeft = (uint)(TerrainLeftBox.Value ?? 0);
            _vm.TerrainRight = (uint)(TerrainRightBox.Value ?? 0);
            _vm.AnimePointer = (uint)(AnimePtrBox.Value ?? 0);
            _vm.WriteEntry();
            CoreState.Services?.ShowInfo("OP Class Demo (FE7U) data written.");
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
    }
}
