using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class MonsterProbabilityViewerView : Window, IEditorView, IDataVerifiableView
    {
        readonly MonsterProbabilityViewerViewModel _vm = new();

        public string ViewTitle => "Monster Probability";
        public bool IsLoaded => _vm.CanWrite;

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
            ClassId1Box.Value = _vm.ClassId1;
            ClassId2Box.Value = _vm.ClassId2;
            ClassId3Box.Value = _vm.ClassId3;
            ClassId4Box.Value = _vm.ClassId4;
            ClassId5Box.Value = _vm.ClassId5;
            Prob1Box.Value = _vm.Prob1;
            Prob2Box.Value = _vm.Prob2;
            Prob3Box.Value = _vm.Prob3;
            Prob4Box.Value = _vm.Prob4;
            Prob5Box.Value = _vm.Prob5;
            Unknown1Box.Value = _vm.Unknown1;
            Unknown2Box.Value = _vm.Unknown2;
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.CanWrite) return;

            _vm.ClassId1 = (uint)(ClassId1Box.Value ?? 0);
            _vm.ClassId2 = (uint)(ClassId2Box.Value ?? 0);
            _vm.ClassId3 = (uint)(ClassId3Box.Value ?? 0);
            _vm.ClassId4 = (uint)(ClassId4Box.Value ?? 0);
            _vm.ClassId5 = (uint)(ClassId5Box.Value ?? 0);
            _vm.Prob1 = (uint)(Prob1Box.Value ?? 0);
            _vm.Prob2 = (uint)(Prob2Box.Value ?? 0);
            _vm.Prob3 = (uint)(Prob3Box.Value ?? 0);
            _vm.Prob4 = (uint)(Prob4Box.Value ?? 0);
            _vm.Prob5 = (uint)(Prob5Box.Value ?? 0);
            _vm.Unknown1 = (uint)(Unknown1Box.Value ?? 0);
            _vm.Unknown2 = (uint)(Unknown2Box.Value ?? 0);
            _vm.WriteMonsterProbability();
            CoreState.Services.ShowInfo("Monster probability data written.");
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
        public ViewModelBase? DataViewModel => _vm;
    }
}
