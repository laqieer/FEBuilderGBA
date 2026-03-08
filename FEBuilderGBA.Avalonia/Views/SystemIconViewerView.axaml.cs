using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class SystemIconViewerView : Window, IEditorView, IDataVerifiableView
    {
        readonly SystemIconViewerViewModel _vm = new();
        uint _selectedIndex;

        public string ViewTitle => "System Icon Viewer";
        public bool IsLoaded => _vm.CanWrite;
        public ViewModelBase? DataViewModel => _vm;

        public SystemIconViewerView()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            Opened += (_, _) => LoadList();
        }

        void LoadList()
        {
            try { var items = _vm.LoadSystemIconList(); EntryList.SetItems(items); }
            catch (Exception ex) { Log.Error("SystemIconViewerView.LoadList: {0}", ex.Message); }
        }

        void OnSelected(uint addr)
        {
            try
            {
                // Calculate the selected index from the address list
                var items = _vm.LoadSystemIconList();
                _selectedIndex = 0;
                for (int i = 0; i < items.Count; i++)
                {
                    if (items[i].addr == addr) { _selectedIndex = items[i].tag; break; }
                }

                _vm.LoadSystemIcon(addr);
                UpdateUI();
                LoadImage();
            }
            catch (Exception ex) { Log.Error("SystemIconViewerView.OnSelected: {0}", ex.Message); }
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            ImgPtrLabel.Text = $"0x{_vm.ImagePointer:X08}";
            PalPtrLabel.Text = $"0x{_vm.PalettePointer:X08}";
        }

        void LoadImage()
        {
            try
            {
                byte[] rgba = _vm.TryLoadImage(_selectedIndex, out int w, out int h);
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
