using global::Avalonia.Controls;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class EventTemplate2View : EventTemplateViewBase
    {
        readonly EventTemplate2ViewModel _vm = new();

        protected override EventTemplateViewModelBase Vm => _vm;
        protected override WrapPanel? Buttons => ButtonPanel;
        public override string ViewTitle => "Event Template 2";

        public EventTemplate2View()
        {
            InitializeComponent();
            InitTemplate();
        }
    }
}
