using global::Avalonia;
using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class EventTemplateImplView : TranslatedUserControl, IEmbeddableEditor
    {
        readonly EventTemplateImplViewModel _vm = new();
        bool _hasLoadedList;

        public string ViewTitle => "Event Template Implementation";
        public new bool IsLoaded => true;
        public EditorDescriptor Descriptor => new("Event Template Implementation", 860, 600);
        public event EventHandler? CloseRequested;
        public void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);

        public EventTemplateImplView()
        {
            InitializeComponent();
            DataContext = _vm;
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            if (!_hasLoadedList)
            {
                _hasLoadedList = true;
                LoadList();
            }
        }

        void LoadList()
        {
            try
            {
                _vm.LoadList();
            }
            catch (Exception ex)
            {
                Log.Error("EventTemplateImplView.LoadList failed: " + ex.ToString());
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
                var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
                if (clipboard != null)
                {
                    await clipboard.SetTextAsync(_vm.GeneratedHex);
                    _vm.Status = R._("Copied generated hex to clipboard.");
                }
            }
            catch (Exception ex)
            {
                Log.Error("EventTemplateImplView.CopyHex failed: " + ex.ToString());
            }
        }

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
    }
}
