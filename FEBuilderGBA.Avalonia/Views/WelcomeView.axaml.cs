using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class WelcomeView : Window, IEditorView, IDataVerifiableView
    {
        readonly WelcomeViewModel _vm = new();
        public string ViewTitle => "Welcome";
        public bool IsLoaded => _vm.IsLoaded;
        public ViewModelBase? DataViewModel => _vm;

        public WelcomeView()
        {
            InitializeComponent();
            DataContext = _vm;
            _vm.Initialize();

            // Show version info
            try
            {
#if DEBUG
                VersionText.Text = "Version: Debug Build";
#else
                VersionText.Text = $"Version: {U.getVersion()}";
#endif
            }
            catch { VersionText.Text = ""; }
        }

        void OpenLastROM_Click(object? sender, RoutedEventArgs e)
        {
            Close("OpenLastROM");
        }

        void OpenROM_Click(object? sender, RoutedEventArgs e)
        {
            Close("OpenROM");
        }

        void UpdateCheck_Click(object? sender, RoutedEventArgs e)
        {
        }

        void Manual_Click(object? sender, RoutedEventArgs e)
        {
        }

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
    }
}
