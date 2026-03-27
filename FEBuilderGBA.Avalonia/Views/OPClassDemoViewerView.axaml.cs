using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;
using FEBuilderGBA.Core;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class OPClassDemoViewerView : TranslatedWindow, IEditorView, IDataVerifiableView
    {
        readonly OPClassDemoViewerViewModel _vm = new();
        readonly UndoService _undoService = new();

        public string ViewTitle => "OP Class Demo Editor";
        public bool IsLoaded => _vm.CanWrite;
        public ViewModelBase? DataViewModel => _vm;

        public OPClassDemoViewerView()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            DescTextIdBox.ValueChanged += OnDescTextIdChanged;
            Opened += (_, _) => LoadList();
        }

        void OnDescTextIdChanged(object? sender, NumericUpDownValueChangedEventArgs e)
        {
            if (_vm.IsLoading) return;
            uint id = (uint)(DescTextIdBox.Value ?? 0);
            try { DescTextPreview.Text = id != 0 ? NameResolver.GetTextById(id) : ""; }
            catch { DescTextPreview.Text = ""; }
        }

        void LoadList()
        {
            _vm.IsLoading = true;
            try { var items = _vm.LoadOPClassDemoList(); EntryList.SetItemsWithIcons(items, i => ListIconLoaders.ClassIconLoader(items, i)); }
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
            }
            catch (Exception ex) { Log.Error($"OPClassDemoViewerView.OnSelected: {ex.Message}"); }
            finally { _vm.IsLoading = false; _vm.MarkClean(); }
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            EnglishNamePtrBox.Value = _vm.EnglishNamePointer;
            DescTextIdBox.Value = _vm.DescriptionTextId;
            try { DescTextPreview.Text = _vm.DescriptionTextId != 0 ? NameResolver.GetTextById(_vm.DescriptionTextId) : ""; }
            catch { DescTextPreview.Text = ""; }
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
            _undoService.Begin("Edit OP Class Demo");
            try
            {
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
                _undoService.Commit();
                _vm.MarkClean();
                CoreState.Services?.ShowInfo("OP Class Demo data written.");
            }
            catch (Exception ex) { _undoService.Rollback(); Log.Error($"OPClassDemoViewerView.Write: {ex.Message}"); }
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
    }
}
