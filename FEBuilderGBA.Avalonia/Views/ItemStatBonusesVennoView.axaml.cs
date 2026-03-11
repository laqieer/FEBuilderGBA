using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class ItemStatBonusesVennoView : Window, IEditorView, IDataVerifiableView
    {
        public ViewModelBase? DataViewModel => _vm;
        readonly ItemStatBonusesVennoViewModel _vm = new();
        readonly UndoService _undoService = new();

        public string ViewTitle => "Stat Bonuses (Venno)";
        public bool IsLoaded => _vm.IsLoaded;

        public ItemStatBonusesVennoView()
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
            }
            catch (Exception ex)
            {
                Log.Error("ItemStatBonusesVennoView.LoadList failed: {0}", ex.Message);
            }
        }

        void OnSelected(uint addr)
        {
            try
            {
                _vm.IsLoading = true;
                _vm.LoadEntry(addr);
                UpdateUI();
                _vm.IsLoading = false;
                _vm.MarkClean();
            }
            catch (Exception ex)
            {
                _vm.IsLoading = false;
                Log.Error("ItemStatBonusesVennoView.OnSelected failed: {0}", ex.Message);
            }
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";

            // Stat bonuses
            HPBox.Value = _vm.HP;
            StrBox.Value = _vm.Str;
            SkillBox.Value = _vm.Skill;
            SpeedBox.Value = _vm.Speed;
            DefBox.Value = _vm.Def;
            ResBox.Value = _vm.Res;
            LuckBox.Value = _vm.Luck;
            MoveBox.Value = _vm.Move;
            ConBox.Value = _vm.Con;

            // Growth rate bonuses
            GrowHPBox.Value = _vm.GrowHP;
            GrowStrBox.Value = _vm.GrowStr;
            GrowSkillBox.Value = _vm.GrowSkill;
            GrowSpeedBox.Value = _vm.GrowSpeed;
            GrowDefBox.Value = _vm.GrowDef;
            GrowResBox.Value = _vm.GrowRes;
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            _vm.HP = (int)(HPBox.Value ?? 0);
            _vm.Str = (int)(StrBox.Value ?? 0);
            _vm.Skill = (int)(SkillBox.Value ?? 0);
            _vm.Speed = (int)(SpeedBox.Value ?? 0);
            _vm.Def = (int)(DefBox.Value ?? 0);
            _vm.Res = (int)(ResBox.Value ?? 0);
            _vm.Luck = (int)(LuckBox.Value ?? 0);
            _vm.Move = (int)(MoveBox.Value ?? 0);
            _vm.Con = (int)(ConBox.Value ?? 0);

            _vm.GrowHP = (int)(GrowHPBox.Value ?? 0);
            _vm.GrowStr = (int)(GrowStrBox.Value ?? 0);
            _vm.GrowSkill = (int)(GrowSkillBox.Value ?? 0);
            _vm.GrowSpeed = (int)(GrowSpeedBox.Value ?? 0);
            _vm.GrowDef = (int)(GrowDefBox.Value ?? 0);
            _vm.GrowRes = (int)(GrowResBox.Value ?? 0);

            _undoService.Begin("Edit Stat Bonuses (Venno)");
            try
            {
                _vm.Write();
                _undoService.Commit();
                _vm.MarkClean();
                CoreState.Services.ShowInfo("Stat Bonuses (Venno) data written.");
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.Error("Write failed: {0}", ex.Message);
            }
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
    }
}
