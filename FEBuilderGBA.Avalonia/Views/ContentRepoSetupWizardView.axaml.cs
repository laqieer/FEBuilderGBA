using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class ContentRepoSetupWizardView : TranslatedUserControl, IEmbeddableEditor
    {
        public string ViewTitle => "Content Repository Setup";
        public new bool IsLoaded => true;
        public EditorDescriptor Descriptor => new("Content Repository Setup", 920, 620);
        public event EventHandler? CloseRequested;
        public object? DialogResult { get; private set; }
        public void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);
        public void NavigateTo(uint address) { }

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
            { DialogResult = true; RequestClose(); }
        }

        void Close_Click(object? sender, RoutedEventArgs e)
        {
            _vm.Skip();
            { DialogResult = false; RequestClose(); }
        }
    }
}

