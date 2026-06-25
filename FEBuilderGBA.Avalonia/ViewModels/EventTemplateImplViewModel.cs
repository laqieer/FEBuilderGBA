namespace FEBuilderGBA.Avalonia.ViewModels
{
    // EventTemplateImpl is the same template browser as EventScriptTemplate
    // (ports WinForms EventTemplateImpl). Reuse the real browser engine so this
    // registry-reachable window is not a dead shell. (#1434)
    public class EventTemplateImplViewModel : EventScriptTemplateViewModel
    {
    }
}
