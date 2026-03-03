using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class MonsterProbabilityViewerView : Window, IEditorView
    {
        readonly MonsterProbabilityViewerViewModel _vm = new();

        public string ViewTitle => "Monster Probability";
        public bool IsLoaded => _vm.IsLoaded;

        public MonsterProbabilityViewerView()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            Opened += (_, _) => LoadList();
        }

        void LoadList()
        {
            try
            {
                var items = _vm.LoadMonsterProbabilityList();
                EntryList.SetItems(items);
            }
            catch (Exception ex)
            {
                Log.Error("MonsterProbabilityViewerView.LoadList failed: {0}", ex.Message);
            }
        }

        void OnSelected(uint addr)
        {
            try
            {
                _vm.LoadMonsterProbability(addr);
                UpdateUI();
            }
            catch (Exception ex)
            {
                Log.Error("MonsterProbabilityViewerView.OnSelected failed: {0}", ex.Message);
            }
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            ClassId1Label.Text = $"0x{_vm.ClassId1:X02} ({_vm.ClassId1})";
            ClassId2Label.Text = $"0x{_vm.ClassId2:X02} ({_vm.ClassId2})";
            ClassId3Label.Text = $"0x{_vm.ClassId3:X02} ({_vm.ClassId3})";
            ClassId4Label.Text = $"0x{_vm.ClassId4:X02} ({_vm.ClassId4})";
            ClassId5Label.Text = $"0x{_vm.ClassId5:X02} ({_vm.ClassId5})";
            Prob1Label.Text = $"{_vm.Prob1}%";
            Prob2Label.Text = $"{_vm.Prob2}%";
            Prob3Label.Text = $"{_vm.Prob3}%";
            Prob4Label.Text = $"{_vm.Prob4}%";
            Prob5Label.Text = $"{_vm.Prob5}%";
            Unknown1Label.Text = $"0x{_vm.Unknown1:X02}";
            Unknown2Label.Text = $"0x{_vm.Unknown2:X02}";
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
    }
}
