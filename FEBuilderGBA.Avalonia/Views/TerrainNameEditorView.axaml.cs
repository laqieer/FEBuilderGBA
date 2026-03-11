using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class TerrainNameEditorView : Window, IEditorView, IDataVerifiableView
    {
        readonly TerrainNameEditorViewModel _vm = new();
        readonly UndoService _undoService = new();

        public string ViewTitle => "Terrain Name Editor";
        public new bool IsLoaded => _vm.CanWrite;
        public ViewModelBase? DataViewModel => _vm;

        public TerrainNameEditorView()
        {
            InitializeComponent();
            TerrainList.SelectedAddressChanged += OnTerrainSelected;
            Opened += (_, _) => LoadList();
        }

        void LoadList()
        {
            _vm.IsLoading = true;
            try
            {
                var items = _vm.LoadTerrainNameList();
                TerrainList.SetItems(items);
            }
            catch (Exception ex)
            {
                Log.Error("TerrainNameEditorView.LoadList failed: {0}", ex.Message);
            }
            finally { _vm.IsLoading = false; _vm.MarkClean(); }
        }

        void OnTerrainSelected(uint addr)
        {
            _vm.IsLoading = true;
            try
            {
                _vm.LoadTerrainName(addr);
                UpdateUI();
            }
            catch (Exception ex)
            {
                Log.Error("TerrainNameEditorView.OnTerrainSelected failed: {0}", ex.Message);
            }
            finally { _vm.IsLoading = false; _vm.MarkClean(); }
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
            _undoService.Begin("Edit Terrain Name");
            try
            {
                _vm.TextId = (uint)(TextIdBox.Value ?? 0);
                _vm.WriteTerrainName();
                _undoService.Commit();
                _vm.MarkClean();
                CoreState.Services?.ShowInfo("Terrain name written.");
            }
            catch (Exception ex) { _undoService.Rollback(); Log.Error("TerrainNameEditorView.Write: {0}", ex.Message); }
        }

        public void SelectFirstItem()
        {
            TerrainList.SelectFirst();
        }
    }
}
