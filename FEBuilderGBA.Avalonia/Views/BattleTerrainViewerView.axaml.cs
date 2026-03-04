using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class BattleTerrainViewerView : Window, IEditorView, IDataVerifiableView
    {
        readonly BattleTerrainViewerViewModel _vm = new();

        public string ViewTitle => "Battle Terrain Viewer";
        public bool IsLoaded => _vm.IsLoaded;
        public ViewModelBase? DataViewModel => _vm;

        public BattleTerrainViewerView()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            Opened += (_, _) => LoadList();
        }

        void LoadList()
        {
            try { var items = _vm.LoadBattleTerrainList(); EntryList.SetItems(items); }
            catch (Exception ex) { Log.Error("BattleTerrainViewerView.LoadList: {0}", ex.Message); }
        }

        void OnSelected(uint addr)
        {
            try
            {
                _vm.LoadBattleTerrain(addr);
                UpdateUI();
                LoadImage();
            }
            catch (Exception ex) { Log.Error("BattleTerrainViewerView.OnSelected: {0}", ex.Message); }
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            TerrainNameLabel.Text = _vm.TerrainName;
            ImgPtrLabel.Text = $"0x{_vm.ImagePointer:X08}";
            PalPtrLabel.Text = $"0x{_vm.PalettePointer:X08}";
        }

        void LoadImage()
        {
            try
            {
                byte[] rgba = _vm.TryLoadImage(out int w, out int h);
                if (rgba != null && w > 0 && h > 0)
                    ImageDisplay.SetRgbaData(rgba, w, h);
                else
                    ImageDisplay.SetImage(null);
            }
            catch { ImageDisplay.SetImage(null); }
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
    }
}
