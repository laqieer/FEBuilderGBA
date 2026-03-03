using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class PortraitViewerView : Window, IEditorView
    {
        readonly PortraitViewerViewModel _vm = new();

        public string ViewTitle => "Portrait Viewer";
        public bool IsLoaded => _vm.IsLoaded;

        public PortraitViewerView()
        {
            InitializeComponent();
            PortraitList.SelectedAddressChanged += OnPortraitSelected;
            Opened += (_, _) => LoadList();
        }

        void LoadList()
        {
            try
            {
                var items = _vm.LoadPortraitList();
                PortraitList.SetItems(items);
            }
            catch (Exception ex)
            {
                Log.Error("PortraitViewerView.LoadList failed: {0}", ex.Message);
            }
        }

        void OnPortraitSelected(uint addr)
        {
            try
            {
                _vm.LoadPortrait(addr);
                UpdateUI();
                TryShowPortraitImage();
            }
            catch (Exception ex)
            {
                Log.Error("PortraitViewerView.OnPortraitSelected failed: {0}", ex.Message);
            }
        }

        public void NavigateTo(uint address)
        {
            PortraitList.SelectAddress(address);
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            ImgPtrLabel.Text = $"0x{_vm.ImagePointer:X08}";
            MapPtrLabel.Text = $"0x{_vm.MapPointer:X08}";
            PalPtrLabel.Text = $"0x{_vm.PalettePointer:X08}";
        }

        void TryShowPortraitImage()
        {
            try
            {
                byte[] rgba = _vm.TryLoadPortraitImage();
                if (rgba != null)
                {
                    PortraitImage.SetRgbaData(rgba, 32, 32);
                }
                else
                {
                    PortraitImage.SetImage(null);
                }
            }
            catch (Exception ex)
            {
                Log.Error("PortraitViewerView.TryShowPortraitImage failed: {0}", ex.Message);
                PortraitImage.SetImage(null);
            }
        }

        public void SelectFirstItem()
        {
            PortraitList.SelectFirst();
        }
    }
}
