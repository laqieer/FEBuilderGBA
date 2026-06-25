using global::Avalonia.Controls;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class EventTemplate1View : EventTemplateViewBase
    {
        readonly EventTemplate1ViewModel _vm = new();

        protected override EventTemplateViewModelBase Vm => _vm;
        protected override WrapPanel? Buttons => ButtonPanel;
        public override string ViewTitle => "Event Template 1";

        public EventTemplate1View()
        {
            InitializeComponent();
            InitTemplate();
        }
    }
}
