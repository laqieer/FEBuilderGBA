using global::Avalonia.Controls;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class EventTemplate5View : EventTemplateViewBase
    {
        readonly EventTemplate5ViewModel _vm = new();

        protected override EventTemplateViewModelBase Vm => _vm;
        protected override WrapPanel? Buttons => ButtonPanel;
        public override string ViewTitle => "Event Template 5";

        public EventTemplate5View()
        {
            InitializeComponent();
            InitTemplate();
        }
    }
}
