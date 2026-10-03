using System;
using global::Avalonia.Controls;
using global::Avalonia.Threading;

namespace FEBuilderGBA.Avalonia.Services
{
    /// <summary>Desktop top-level wrapper for embeddable editor content.</summary>
    public sealed class EditorHostWindow : Window
    {
        readonly string _titleKey;
        bool _subscribed;

        public EditorHostWindow(IEmbeddableEditor editor)
        {
            var descriptor = editor.Descriptor;
            _titleKey = descriptor.Title;
            Title = ViewTranslationHelper.TranslateTitle(_titleKey);
            Width = descriptor.PreferredWidth;
            Height = descriptor.PreferredHeight;
            MinWidth = descriptor.MinWidth;
            MinHeight = descriptor.MinHeight;
            CanResize = descriptor.CanResize;
            WindowStartupLocation = descriptor.StartupLocation;
            SizeToContent = descriptor.SizeToContent;
            Content = editor;
        }

        protected override void OnOpened(EventArgs e)
        {
            Title = ViewTranslationHelper.TranslateTitle(_titleKey);
            if (!_subscribed)
            {
                CoreState.LanguageChanged += OnLanguageChanged;
                _subscribed = true;
            }
            base.OnOpened(e);
        }

        void OnLanguageChanged()
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (_subscribed)
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
