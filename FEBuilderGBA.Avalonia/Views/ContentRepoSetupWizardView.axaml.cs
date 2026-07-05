using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class ContentRepoSetupWizardView : TranslatedWindow
    {
        readonly ContentRepoSetupWizardViewModel _vm;

        public ContentRepoSetupWizardView()
        {
            InitializeComponent();
            _vm = new ContentRepoSetupWizardViewModel();
            DataContext = _vm;
        }

        async void Initialize_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is Button { DataContext: ContentRepoSetupRowViewModel row })
                await _vm.InitializeAsync(row);
        }

        void DontShowAgain_Click(object? sender, RoutedEventArgs e)
        {
            _vm.DontShowAgain();
            Close(true);
        }

        void Close_Click(object? sender, RoutedEventArgs e)
        {
            _vm.Skip();
            Close(false);
        }
    }
}

