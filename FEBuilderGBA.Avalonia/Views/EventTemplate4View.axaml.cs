using global::Avalonia.Controls;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class EventTemplate4View : EventTemplateViewBase
    {
        readonly EventTemplate4ViewModel _vm = new();

        protected override EventTemplateViewModelBase Vm => _vm;
        protected override WrapPanel? Buttons => ButtonPanel;
        public override string ViewTitle => "Event Template 4";

        public EventTemplate4View()
        {
            InitializeComponent();
            InitTemplate();
        }
    }
}
