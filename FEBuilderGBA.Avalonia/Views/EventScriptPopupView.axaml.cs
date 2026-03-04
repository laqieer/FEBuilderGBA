using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class EventScriptPopupView : Window, IEditorView, IDataVerifiableView
    {
        readonly EventScriptPopupViewModel _vm = new();

        public string ViewTitle => "Event Script Command Reference";
        public bool IsLoaded => _vm.IsLoaded;
        public ViewModelBase? DataViewModel => _vm;

        public EventScriptPopupView()
        {
            InitializeComponent();
            _vm.Load();
            InfoBox.Text = _vm.InfoText;
        }

        void Close_Click(object? sender, RoutedEventArgs e)
        {
            Close();
        }

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
    }
}
