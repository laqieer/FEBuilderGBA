using System;
using global::Avalonia.Controls;
using global::Avalonia.Threading;

namespace FEBuilderGBA.Avalonia.Services
{
    /// <summary>Refreshes only window chrome, never translated or user-supplied child content.</summary>
    public class TitleTranslatedWindow : Window
    {
        string? _titleKey;
        bool _subscribed;

        protected void SetTitle(string title, string? titleKey)
        {
            _titleKey = titleKey;
            Title = titleKey == null ? title : ViewTranslationHelper.TranslateTitle(titleKey);
        }

        protected override void OnOpened(EventArgs e)
        {
            if (_titleKey != null)
            {
                if (!_subscribed)
                {
                    CoreState.LanguageChanged += OnLanguageChanged;
                    _subscribed = true;
                }
                Title = ViewTranslationHelper.TranslateTitle(_titleKey);
            }
            base.OnOpened(e);
        }

        void OnLanguageChanged()
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (_subscribed && _titleKey != null)
                    Title = ViewTranslationHelper.TranslateTitle(_titleKey);
            });
        }

        protected override void OnClosed(EventArgs e)
        {
            if (_subscribed)
            {
                CoreState.LanguageChanged -= OnLanguageChanged;
                _subscribed = false;
            }
            base.OnClosed(e);
        }
    }
}
