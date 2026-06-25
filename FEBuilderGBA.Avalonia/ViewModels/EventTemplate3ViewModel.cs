namespace FEBuilderGBA.Avalonia.ViewModels
{
    // Event Template 3 (BLANK / TalkEvent / reinforcement variants / GAMEOVER).
    // Real generator wired to EventTemplateCore. (#1434)
    // Note: the Template-3 counter-reinforcement parent-form side effect
    // (B8=1 / B9=255) is deferred — it requires the event editor host context.
    public class EventTemplate3ViewModel : EventTemplateViewModelBase
    {
        protected override int TemplateNumber => 3;
    }
}
