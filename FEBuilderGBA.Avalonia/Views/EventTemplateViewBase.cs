using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using global::Avalonia.Layout;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    // Shared code-behind for the numbered Event Template windows (1..6). Each
    // window dynamically builds a button per EventTemplateCore.TemplateButton,
    // wires it to the VM generator, and shows a disassembled hex preview the
    // user copies into the event editor. (#1434)
    public abstract class EventTemplateViewBase : TranslatedWindow, IEditorView
    {
        protected abstract EventTemplateViewModelBase Vm { get; }
        protected abstract WrapPanel? Buttons { get; }

        public abstract string ViewTitle { get; }
        public bool IsLoaded => true;

        protected void InitTemplate()
        {
            DataContext = Vm;
            Opened += (_, _) => BuildButtons();
        }

        void BuildButtons()
        {
            WrapPanel? panel = Buttons;
            if (panel == null)
            {
                return;
            }
            panel.Children.Clear();
            foreach (EventTemplateCore.TemplateButton btn in Vm.GetButtons())
            {
                var b = new Button
                {
                    Content = ButtonLabel(btn),
                    Margin = new global::Avalonia.Thickness(4),
                    MinWidth = 120,
                    Tag = btn,
                };
                b.Click += OnTemplateButtonClick;
                panel.Children.Add(b);
            }
        }

        static string ButtonLabel(EventTemplateCore.TemplateButton btn)
        {
            if (btn.IsBlank)
            {
                return R._("BLANK");
            }
            // Route the internal key through R._ so a translation can localize it
            // (falls back to the key verbatim when no translation exists).
            return R._(btn.Key);
        }

        void OnTemplateButtonClick(object? sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is Button b && b.Tag is EventTemplateCore.TemplateButton btn)
                {
                    Vm.GenerateButton(btn);
                }
            }
            catch (Exception ex)
            {
                Log.Error("EventTemplateView.OnTemplateButtonClick failed: " + ex.ToString());
            }
        }

        protected async void CopyHex_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                if (!Vm.HasGenerated)
                {
                    return;
                }
                var clipboard = Clipboard;
                if (clipboard != null)
                {
                    await clipboard.SetTextAsync(Vm.GeneratedHex);
                    Vm.Status = R._("Copied generated hex to clipboard.");
                }
            }
            catch (Exception ex)
            {
                Log.Error("EventTemplateView.CopyHex failed: " + ex.ToString());
            }
        }

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
    }
}
