using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class ToolWorkSupportView : Window, IEditorView
    {
        readonly ToolWorkSupportViewModel _vm = new();
        public string ViewTitle => "Work Support";
        public bool IsLoaded => _vm.IsLoaded;

        public ToolWorkSupportView()
        {
            InitializeComponent();
            DataContext = _vm;
            _vm.Initialize();
        }

        void Update_Click(object? sender, RoutedEventArgs e)
        {
            // Update check placeholder - full implementation requires network access
        }

        void Community_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                if (!string.IsNullOrEmpty(_vm.CommunityUrl))
                {
                    var psi = new System.Diagnostics.ProcessStartInfo(_vm.CommunityUrl) { UseShellExecute = true };
                    System.Diagnostics.Process.Start(psi);
                }
            }
            catch (Exception ex)
            {
                Log.Error("ToolWorkSupportView.Community", ex.ToString());
            }
        }

        void OpenInfo_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                if (!string.IsNullOrEmpty(_vm.InfoText) && System.IO.File.Exists(_vm.InfoText))
                {
                    var psi = new System.Diagnostics.ProcessStartInfo(_vm.InfoText) { UseShellExecute = true };
                    System.Diagnostics.Process.Start(psi);
                }
            }
            catch (Exception ex)
            {
                Log.Error("ToolWorkSupportView.OpenInfo", ex.ToString());
            }
        }

        void ShowAllWorks_Click(object? sender, RoutedEventArgs e)
        {
            WindowManager.Instance.Open<ToolAllWorkSupportView>();
        }

        void Reload_Click(object? sender, RoutedEventArgs e)
        {
            _vm.Initialize();
        }

        void Close_Click(object? sender, RoutedEventArgs e)
        {
            Close();
        }

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
    }
}
