using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class TerrainNameEditorView : Window, IEditorView
    {
        readonly TerrainNameEditorViewModel _vm = new();

        public string ViewTitle => "Terrain Name Editor";
        public bool IsLoaded => _vm.CanWrite;

        public TerrainNameEditorView()
        {
            InitializeComponent();
            TerrainList.SelectedAddressChanged += OnTerrainSelected;
            Opened += (_, _) => LoadList();
        }

        void LoadList()
        {
            try
            {
                var items = _vm.LoadTerrainNameList();
                TerrainList.SetItems(items);
            }
            catch (Exception ex)
            {
                Log.Error("TerrainNameEditorView.LoadList failed: {0}", ex.Message);
            }
        }

        void OnTerrainSelected(uint addr)
        {
            try
            {
                _vm.LoadTerrainName(addr);
                UpdateUI();
            }
            catch (Exception ex)
            {
                Log.Error("TerrainNameEditorView.OnTerrainSelected failed: {0}", ex.Message);
            }
        }

        public void NavigateTo(uint address)
        {
            TerrainList.SelectAddress(address);
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            TextIdBox.Value = _vm.TextId;
            NameLabel.Text = _vm.TerrainName;
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            _vm.TextId = (uint)(TextIdBox.Value ?? 0);
            _vm.WriteTerrainName();
            CoreState.Services.ShowInfo("Terrain name written.");
        }

        public void SelectFirstItem()
        {
            TerrainList.SelectFirst();
        }
    }
}
