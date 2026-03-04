using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class SystemHoverColorViewerView : Window, IEditorView, IDataVerifiableView
    {
        readonly SystemHoverColorViewerViewModel _vm = new();

        public string ViewTitle => "System Hover Color Viewer";
        public bool IsLoaded => _vm.IsLoaded;
        public ViewModelBase? DataViewModel => _vm;

        public SystemHoverColorViewerView()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            Opened += (_, _) => LoadList();
        }

        void LoadList()
        {
            try { var items = _vm.LoadHoverColorList(); EntryList.SetItems(items); }
            catch (Exception ex) { Log.Error("SystemHoverColorViewerView.LoadList: {0}", ex.Message); }
        }

        void OnSelected(uint addr)
        {
            try
            {
                _vm.LoadHoverColor(addr);
                StatusLabel.Text = _vm.StatusMessage;
            }
            catch (Exception ex) { Log.Error("SystemHoverColorViewerView.OnSelected: {0}", ex.Message); }
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
    }
}
