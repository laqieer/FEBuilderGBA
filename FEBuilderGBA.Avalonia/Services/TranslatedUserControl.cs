using System;
using global::Avalonia;
using global::Avalonia.Controls;

namespace FEBuilderGBA.Avalonia.Services
{
    /// <summary>
    /// Base class for Avalonia UserControls that automatically translates all
    /// hardcoded English text in the visual tree using R._().
    ///
    /// Uses AttachedToVisualTree / DetachedFromVisualTree for proper lifecycle
    /// management (UserControls do not have Opened/Closed like Windows).
    ///
    /// Usage: inherit from TranslatedUserControl instead of UserControl.
    /// No boilerplate needed in the subclass constructor.
    /// </summary>
    public class TranslatedUserControl : UserControl
    {
        private readonly ViewTranslationHelper _translator;

        protected TranslatedUserControl()
        {
            _translator = new ViewTranslationHelper(this);
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            _translator.TranslateAll();
            CoreState.LanguageChanged += _translator.OnLanguageChanged;
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            CoreState.LanguageChanged -= _translator.OnLanguageChanged;
            base.OnDetachedFromVisualTree(e);
        }
    }
}
