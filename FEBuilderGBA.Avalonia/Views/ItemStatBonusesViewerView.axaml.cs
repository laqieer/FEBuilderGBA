using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class ItemStatBonusesViewerView : Window, IEditorView
    {
        readonly ItemStatBonusesViewerViewModel _vm = new();

        public string ViewTitle => "Item Stat Bonuses";
        public bool IsLoaded => _vm.IsLoaded;

        public ItemStatBonusesViewerView()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            Opened += (_, _) => LoadList();
        }

        void LoadList()
        {
            try
            {
                var items = _vm.LoadItemStatBonusesList();
                EntryList.SetItems(items);
            }
            catch (Exception ex)
            {
                Log.Error("ItemStatBonusesViewerView.LoadList: {0}", ex.Message);
            }
        }

        void OnSelected(uint addr)
        {
            try
            {
                _vm.LoadItemStatBonuses(addr);
                UpdateUI();
            }
            catch (Exception ex)
            {
                Log.Error("ItemStatBonusesViewerView.OnSelected: {0}", ex.Message);
            }
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            HPLabel.Text = _vm.HP.ToString();
            StrLabel.Text = _vm.Str.ToString();
            SkillLabel.Text = _vm.Skill.ToString();
            SpeedLabel.Text = _vm.Speed.ToString();
            DefLabel.Text = _vm.Def.ToString();
            ResLabel.Text = _vm.Res.ToString();
            LuckLabel.Text = _vm.Luck.ToString();
            MoveLabel.Text = _vm.Move.ToString();
            ConLabel.Text = _vm.Con.ToString();
            Unknown9Label.Text = $"0x{_vm.Unknown9:X02}";
            Unknown10Label.Text = $"0x{_vm.Unknown10:X02}";
            Unknown11Label.Text = $"0x{_vm.Unknown11:X02}";
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
    }
}
