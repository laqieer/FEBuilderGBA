using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class BigCGViewerView : Window, IEditorView, IDataVerifiableView
    {
        readonly BigCGViewerViewModel _vm = new();

        public string ViewTitle => "Big CG Editor";
        public bool IsLoaded => _vm.CanWrite;
        public ViewModelBase? DataViewModel => _vm;

        public BigCGViewerView()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            Opened += (_, _) => LoadList();
        }

        void LoadList()
        {
            try { var items = _vm.LoadBigCGList(); EntryList.SetItems(items); }
            catch (Exception ex) { Log.Error("BigCGViewerView.LoadList: {0}", ex.Message); }
        }

        void OnSelected(uint addr)
        {
            try
            {
                _vm.LoadBigCG(addr);
                UpdateUI();
                LoadImage();
            }
            catch (Exception ex) { Log.Error("BigCGViewerView.OnSelected: {0}", ex.Message); }
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            TablePtrBox.Value = _vm.TablePointer;
            TsaPtrBox.Value = _vm.TSAPointer;
            PalPtrBox.Value = _vm.PalettePointer;
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            _vm.TablePointer = (uint)(TablePtrBox.Value ?? 0);
            _vm.TSAPointer = (uint)(TsaPtrBox.Value ?? 0);
            _vm.PalettePointer = (uint)(PalPtrBox.Value ?? 0);
            _vm.WriteBigCG();
            CoreState.Services.ShowInfo("Big CG data written.");
        }

        void LoadImage()
        {
            try
            {
                ImageDisplay.SetImage(_vm.TryLoadImage());
            }
            catch { ImageDisplay.SetImage(null); }
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
    }
}
