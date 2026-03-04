using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class ArenaClassViewerView : Window, IEditorView, IDataVerifiableView
    {
        readonly ArenaClassViewerViewModel _vm = new();

        public string ViewTitle => "Arena Class";
        public bool IsLoaded => _vm.IsLoaded;

        public ArenaClassViewerView()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            Opened += (_, _) => LoadList();
        }

        void LoadList()
        {
            try
            {
                var items = _vm.LoadArenaClassList();
                EntryList.SetItems(items);
            }
            catch (Exception ex)
            {
                Log.Error("ArenaClassViewerView.LoadList failed: {0}", ex.Message);
            }
        }

        void OnSelected(uint addr)
        {
            try
            {
                _vm.LoadArenaClass(addr);
                UpdateUI();
            }
            catch (Exception ex)
            {
                Log.Error("ArenaClassViewerView.OnSelected failed: {0}", ex.Message);
            }
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            ClassIdLabel.Text = $"0x{_vm.ClassId:X02} ({_vm.ClassId})";
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
        public ViewModelBase? DataViewModel => _vm;
    }
}
