using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class EventScriptTemplateView : TranslatedWindow, IEditorView
    {
        readonly EventScriptTemplateViewModel _vm = new();

        public string ViewTitle => "Script Template Browser";
        public bool IsLoaded => true;

        public EventScriptTemplateView()
        {
            InitializeComponent();
            DataContext = _vm;
            Opened += (_, _) => LoadList();
        }

        void LoadList()
        {
            try
            {
                _vm.LoadList();
            }
            catch (Exception ex)
            {
                Log.Error("EventScriptTemplateView.LoadList failed: " + ex.ToString());
            }
        }

        async void CopyHex_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                if (!_vm.HasGenerated)
                {
                    return;
                }
                var clipboard = Clipboard;
                if (clipboard != null)
                {
                    await clipboard.SetTextAsync(_vm.GeneratedHex);
                    _vm.Status = R._("Copied generated hex to clipboard.");
                }
            }
            catch (Exception ex)
            {
                Log.Error("EventScriptTemplateView.CopyHex failed: " + ex.ToString());
            }
        }

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
    }
}
