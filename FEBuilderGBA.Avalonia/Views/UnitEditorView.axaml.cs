using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class UnitEditorView : Window, IEditorView
    {
        readonly UnitEditorViewModel _vm = new();
        readonly UndoService _undoService = new();

        public string ViewTitle => "Unit Editor";
        public bool IsLoaded => _vm.CanWrite;

        public UnitEditorView()
        {
            InitializeComponent();
            UnitList.SelectedAddressChanged += OnUnitSelected;
            Opened += (_, _) => LoadList();
        }

        void LoadList()
        {
            try
            {
                var items = _vm.LoadUnitList();
                UnitList.SetItems(items);
            }
            catch (Exception ex)
            {
                Log.Error("UnitEditorView.LoadList failed: {0}", ex.Message);
            }
        }

        void OnUnitSelected(uint addr)
        {
            try
            {
                _vm.LoadUnit(addr);
                UpdateUI();
            }
            catch (Exception ex)
            {
                Log.Error("UnitEditorView.OnUnitSelected failed: {0}", ex.Message);
            }
        }

        public void NavigateTo(uint address)
        {
            UnitList.SelectAddress(address);
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            NameLabel.Text = _vm.Name;
            NameIdBox.Value = _vm.NameId;
            ClassIdBox.Value = _vm.ClassId;
            LevelBox.Value = _vm.Level;
            HPBox.Value = _vm.HP;
            StrBox.Value = _vm.Str;
            SklBox.Value = _vm.Skl;
            SpdBox.Value = _vm.Spd;
            DefBox.Value = _vm.Def;
            ResBox.Value = _vm.Res;
            LckBox.Value = _vm.Lck;
            ConBox.Value = _vm.Con;
        }

        void ReadFromUI()
        {
            _vm.NameId = (uint)(NameIdBox.Value ?? 0);
            _vm.ClassId = (uint)(ClassIdBox.Value ?? 0);
            _vm.Level = (uint)(LevelBox.Value ?? 0);
            _vm.HP = (uint)(HPBox.Value ?? 0);
            _vm.Str = (uint)(StrBox.Value ?? 0);
            _vm.Skl = (uint)(SklBox.Value ?? 0);
            _vm.Spd = (uint)(SpdBox.Value ?? 0);
            _vm.Def = (uint)(DefBox.Value ?? 0);
            _vm.Res = (uint)(ResBox.Value ?? 0);
            _vm.Lck = (uint)(LckBox.Value ?? 0);
            _vm.Con = (uint)(ConBox.Value ?? 0);
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            ReadFromUI();
            _vm.WriteUnit();
            CoreState.Services.ShowInfo("Unit data written.");
        }

        void Undo_Click(object? sender, RoutedEventArgs e)
        {
            _undoService.Rollback();
            if (_vm.CurrentAddr != 0)
            {
                _vm.LoadUnit(_vm.CurrentAddr);
                UpdateUI();
            }
        }

        /// <summary>Select the first item in the list (for smoke testing).</summary>
        public void SelectFirstItem()
        {
            UnitList.SelectFirst();
        }
    }
}
