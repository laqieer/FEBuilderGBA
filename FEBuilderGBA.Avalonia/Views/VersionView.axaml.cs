using System;
using System.Text;
using global::Avalonia.Controls;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class VersionView : Window, IEditorView, IDataVerifiableView
    {
        readonly VersionViewModel _vm = new();
        public string ViewTitle => "Version Information";
        public bool IsLoaded => _vm.IsLoaded;

        public VersionView()
        {
            InitializeComponent();
            Opened += (_, _) => { _vm.Initialize(); UpdateUI(); };
        }

        void UpdateUI()
        {
            VersionTextBox.Text = _vm.VersionMessage;
        }

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
        public ViewModelBase? DataViewModel => _vm;
    }
}
