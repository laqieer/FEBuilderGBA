using System;
using global::Avalonia.Controls;

namespace FEBuilderGBA.Avalonia.Services
{
    /// <summary>
    /// Base class for Avalonia Windows that automatically translates all
    /// hardcoded English text in the logical tree using R._().
    ///
    /// Subclasses inherit:
    ///   - ViewTranslationHelper creation
    ///   - Translation on Opened (when visual tree is ready)
    ///   - LanguageChanged subscription/unsubscription
    ///
    /// Usage: inherit from TranslatedWindow instead of Window.
    /// No boilerplate needed in the subclass constructor.
    /// </summary>
    public class TranslatedWindow : Window
    {
        private readonly ViewTranslationHelper _translator;
        private bool _subscribed;

        protected TranslatedWindow()
        {
            _translator = new ViewTranslationHelper(this);
            Opened += OnTranslatedWindowOpened;
        }

        private void OnTranslatedWindowOpened(object? sender, EventArgs e)
        {
            Opened -= OnTranslatedWindowOpened;
            _translator.TranslateAll();
            if (!_subscribed)
            {
                CoreState.LanguageChanged += _translator.OnLanguageChanged;
                _subscribed = true;
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            CoreState.LanguageChanged -= _translator.OnLanguageChanged;
            base.OnClosed(e);
        }
    }
}
