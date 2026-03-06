using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Dialogs;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class ToolWorkSupport_SelectUPSView : Window, IEditorView
    {
        readonly ToolWorkSupport_SelectUPSViewModel _vm = new();
        public string ViewTitle => "Open UPS";
        public bool IsLoaded => _vm.IsLoaded;

        public ToolWorkSupport_SelectUPSView()
        {
            InitializeComponent();
            DataContext = _vm;
            _vm.Initialize();
        }

        async void Browse_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                var path = await FileDialogHelper.OpenRomFile(this);
                if (!string.IsNullOrEmpty(path))
                {
                    _vm.OriginalFilename = path;
                }
            }
            catch (Exception ex)
            {
                Log.Error("ToolWorkSupport_SelectUPSView", ex.ToString());
            }
        }

        void ApplyUPS_Click(object? sender, RoutedEventArgs e)
        {
            _vm.DialogConfirmed = true;
            Close();
        }

        void Cancel_Click(object? sender, RoutedEventArgs e)
        {
            _vm.DialogConfirmed = false;
            Close();
        }

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
    }
}
