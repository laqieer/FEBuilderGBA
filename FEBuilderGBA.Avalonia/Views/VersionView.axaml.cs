using System;
using global::Avalonia;
using System.Text;
using global::Avalonia.Controls;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class VersionView : TranslatedUserControl, IEmbeddableEditor, IDataVerifiableView
    {
        readonly VersionViewModel _vm = new();

        bool _hasLoadedList;
        public string ViewTitle => "Version Information";
        public new bool IsLoaded => _vm.IsLoaded;


        public EditorDescriptor Descriptor => new("Version Information", 720, 420, SizeToContent: true);

        public event EventHandler? CloseRequested;
        public VersionView()
        {
            InitializeComponent();
        }


        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)

        {

            base.OnAttachedToVisualTree(e);

            if (!_hasLoadedList)

            {

                _hasLoadedList = true;

                _vm.Initialize(); UpdateUI();

            }

        }

        void UpdateUI()
        {
            VersionTextBox.Text = _vm.VersionMessage;
        }

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
        public ViewModelBase? DataViewModel => _vm;

        public void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);
    }
}
