using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class SkillConfigSkillSystemView : Window, IEditorView
    {
        readonly SkillConfigSkillSystemViewModel _vm = new();

        public string ViewTitle => "Skill Config (SkillSystem)";
        public bool IsLoaded => _vm.IsLoaded;

        public SkillConfigSkillSystemView()
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
                Log.Error("SkillConfigSkillSystemView.LoadList failed: {0}", ex.Message);
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
                Log.Error("SkillConfigSkillSystemView.OnSelected failed: {0}", ex.Message);
            }
        }

        void UpdateUI()
        {
            AddrLabel.Text = string.Format("0x{0:X08}", _vm.CurrentAddr);
            TextDetailBox.Value = _vm.TextDetail;
        }

        void OnWrite(object? sender, RoutedEventArgs e)
        {
            try
            {
                _vm.TextDetail = (uint)(TextDetailBox.Value ?? 0);
                _vm.Write();
            }
            catch (Exception ex)
            {
                Log.Error("SkillConfigSkillSystemView.Write failed: {0}", ex.Message);
            }
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
    }
}
