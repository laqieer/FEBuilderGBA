using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Dialogs;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class ToolWorkSupport_SelectUPSView : TranslatedWindow, IEditorView
    {
        readonly ToolWorkSupport_SelectUPSViewModel _vm = new();
        public string ViewTitle => "Open UPS";
        public bool IsLoaded => _vm.IsLoaded;

        public ToolWorkSupport_SelectUPSView()
        {
            InitializeComponent();
            DataContext = _vm;
            _vm.IsLoading = true;
            _vm.Initialize();
            _vm.IsLoading = false;
            _vm.MarkClean();
        }

        /// <summary>
        /// Stage a UPS and auto-find its vanilla ROM by source CRC32 (mirrors WF
        /// <c>OpenUPS</c> + <c>_Shown</c>). Call before <c>ShowDialog</c>.
        /// </summary>
        public void OpenUPS(string upsFilename) => _vm.OpenUPS(upsFilename);

        /// <summary>True when the user confirmed (Apply). Read after <c>ShowDialog</c>.</summary>
        public bool DialogConfirmed => _vm.DialogConfirmed;

        /// <summary>The chosen vanilla ROM path. Read after <c>ShowDialog</c>.</summary>
        public string SelectedOriginal => _vm.GetOriginalFilename();

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
            Close(true);
        }

        void Cancel_Click(object? sender, RoutedEventArgs e)
        {
            _vm.DialogConfirmed = false;
            Close(false);
        }

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
    }
}
