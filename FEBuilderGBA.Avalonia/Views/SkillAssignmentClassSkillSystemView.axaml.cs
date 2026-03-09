using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class SkillAssignmentClassSkillSystemView : Window, IEditorView
    {
        readonly SkillAssignmentClassSkillSystemViewModel _vm = new();

        public string ViewTitle => "Skill Assignment (Class)";
        public bool IsLoaded => _vm.IsLoaded;

        public SkillAssignmentClassSkillSystemView()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            WriteButton.Click += OnWrite;
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
                Log.Error("SkillAssignmentClassSkillSystemView.LoadList failed: {0}", ex.Message);
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
                Log.Error("SkillAssignmentClassSkillSystemView.OnSelected failed: {0}", ex.Message);
            }
        }

        void UpdateUI()
        {
            AddrLabel.Text = string.Format("0x{0:X08}", _vm.CurrentAddr);
            ClassSkillBox.Value = _vm.ClassSkill;
        }

        void OnWrite(object? sender, RoutedEventArgs e)
        {
            try
            {
                _vm.ClassSkill = (uint)(ClassSkillBox.Value ?? 0);
                _vm.Write();
            }
            catch (Exception ex)
            {
                Log.Error("SkillAssignmentClassSkillSystemView.Write failed: {0}", ex.Message);
            }
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
    }
}
