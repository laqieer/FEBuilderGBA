using System;
using global::Avalonia;
using global::Avalonia.Controls;

namespace FEBuilderGBA.Avalonia.Services
{
    /// <summary>
    /// Base class for Avalonia UserControls that automatically translates all
    /// hardcoded English text in the logical tree using R._().
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
        private bool _subscribed;

        protected TranslatedUserControl()
        {
            _translator = new ViewTranslationHelper(this);
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            _translator.TranslateAll();
            if (!_subscribed)
            {
                CoreState.LanguageChanged += _translator.OnLanguageChanged;
                _subscribed = true;
            }
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            if (_subscribed)
            {
                CoreState.LanguageChanged -= _translator.OnLanguageChanged;
                _subscribed = false;
            }
            base.OnDetachedFromVisualTree(e);
        }
    }
}
