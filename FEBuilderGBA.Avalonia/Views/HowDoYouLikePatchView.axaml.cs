using global::Avalonia;
using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class HowDoYouLikePatchView : TranslatedUserControl, IEmbeddableEditor
    {
        readonly HowDoYouLikePatchViewModel _vm = new();
        public string ViewTitle => "Patch Feedback";
        public new bool IsLoaded => _vm.IsLoaded;
        public EditorDescriptor Descriptor => new("Patch Review", 849, 291, SizeToContent: global::Avalonia.Controls.SizeToContent.WidthAndHeight);
        public event EventHandler? CloseRequested;
        public void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);

        public HowDoYouLikePatchView()
        {
            InitializeComponent();
            DataContext = _vm;
            _vm.Initialize();
        }

        /// <summary>
        /// Set the recommendation/explanation text shown in the dialog body
        /// (the bound <see cref="HowDoYouLikePatchViewModel.PatchInfo"/>).
        /// </summary>
        public void SetPatchInfo(string info)
        {
            _vm.PatchInfo = info ?? string.Empty;
        }

        /// <summary>True when the user clicked Apply (vs. Skip).</summary>
        public bool UserApplied => _vm.UserApplied;

        void Apply_Click(object? sender, RoutedEventArgs e)
        {
            _vm.UserApplied = true;
            RequestClose();
        }

        void Skip_Click(object? sender, RoutedEventArgs e)
        {
            _vm.UserApplied = false;
            RequestClose();
        }

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
    }
}
