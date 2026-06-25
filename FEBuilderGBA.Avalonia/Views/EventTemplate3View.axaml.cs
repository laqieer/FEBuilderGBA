using global::Avalonia.Controls;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class EventTemplate3View : EventTemplateViewBase
    {
        readonly EventTemplate3ViewModel _vm = new();

        protected override EventTemplateViewModelBase Vm => _vm;
        protected override WrapPanel? Buttons => ButtonPanel;
        public override string ViewTitle => "Event Template 3";

        public EventTemplate3View()
        {
            InitializeComponent();
            InitTemplate();
        }
    }
}
