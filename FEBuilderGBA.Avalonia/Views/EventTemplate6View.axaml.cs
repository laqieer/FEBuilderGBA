using global::Avalonia.Controls;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class EventTemplate6View : EventTemplateViewBase
    {
        readonly EventTemplate6ViewModel _vm = new();

        protected override EventTemplateViewModelBase Vm => _vm;
        protected override WrapPanel? Buttons => ButtonPanel;
        public override string ViewTitle => "Event Template 6";

        public EventTemplate6View()
        {
            InitializeComponent();
            InitTemplate();
        }
    }
}
