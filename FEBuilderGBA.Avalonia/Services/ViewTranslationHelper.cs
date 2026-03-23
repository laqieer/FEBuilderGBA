using System;
using System.Collections.Generic;
using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.LogicalTree;
using global::Avalonia.Threading;

namespace FEBuilderGBA.Avalonia.Services
{
    /// <summary>
    /// Walks the logical tree of a window or control and translates
    /// all hardcoded English text using R._().
    ///
    /// This helper stores the original (English) text for every translatable
    /// control so it can re-translate when the language changes at runtime.
    ///
    /// Prefer using TranslatedWindow / TranslatedUserControl base classes
    /// which wire this helper automatically. For manual usage:
    ///   _translator = new ViewTranslationHelper(this);
    ///   // On Opened / AttachedToVisualTree:
    ///   _translator.TranslateAll();
    ///   CoreState.LanguageChanged += _translator.OnLanguageChanged;
    ///   // On Closed / DetachedFromVisualTree:
    ///   CoreState.LanguageChanged -= _translator.OnLanguageChanged;
    /// </summary>
    public sealed class ViewTranslationHelper
    {
        readonly Control _root;

        /// <summary>
        /// Maps each translatable control to its original English key text.
        /// We store (control, property-kind) -> original text.
        /// </summary>
        readonly List<TranslationEntry> _entries = new();

        enum PropKind { Text, Header, Content, Title, Watermark }

        readonly record struct TranslationEntry(Control Control, PropKind Kind, string OriginalText);

        /// <summary>
        /// Strings that should NOT be translated (technical field names, hex labels, etc.).
        /// </summary>
        static bool ShouldSkip(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return true;

            // Skip single-char or very short technical labels
            if (text.Length <= 1)
                return true;

            // Skip binding expressions
            if (text.StartsWith("{"))
                return true;

            // Skip hex addresses
            if (text.StartsWith("0x") || text.StartsWith("0X"))
                return true;

            // Skip pure numeric
            if (double.TryParse(text, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out _))
                return true;

            // Skip field codes like "B0", "W2", "D0" etc (but allow words like "HP", "AI")
            // Technical field IDs: single letter + digits pattern at start, inside parens
            // We don't skip these — they appear as part of longer labels like "Ability 1 (B40):"

            return false;
        }

        /// <summary>
        /// Check whether a string looks like translatable UI text.
        /// Must contain at least one ASCII letter.
        /// </summary>
        static bool IsTranslatable(string text)
        {
            if (ShouldSkip(text))
                return false;

            // Must contain at least one letter
            foreach (char c in text)
            {
                if ((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z'))
                    return true;
            }
            return false;
        }

        public ViewTranslationHelper(Control root)
        {
            _root = root;
        }

        /// <summary>
        /// Scan all controls and translate their text.
        /// Call this after InitializeComponent() or on Opened.
        /// </summary>
        public void TranslateAll()
        {
            _entries.Clear();
            ScanControl(_root);
            ApplyTranslations();
        }

        /// <summary>
        /// Re-apply translations (call from LanguageChanged handler).
        /// </summary>
        public void OnLanguageChanged()
        {
            Dispatcher.UIThread.Post(ApplyTranslations);
        }

        void ScanControl(Control control)
        {
            // Check TextBlock.Text
            if (control is TextBlock tb && IsTranslatable(tb.Text ?? ""))
            {
                _entries.Add(new TranslationEntry(control, PropKind.Text, tb.Text!));
            }

            // Check Expander.Header (string)
            if (control is Expander exp && exp.Header is string expHeader && IsTranslatable(expHeader))
            {
                _entries.Add(new TranslationEntry(control, PropKind.Header, expHeader));
            }

            // Check TabItem.Header (string)
            if (control is TabItem tab && tab.Header is string tabHeader && IsTranslatable(tabHeader))
            {
                _entries.Add(new TranslationEntry(control, PropKind.Header, tabHeader));
            }

            // Check Button.Content (string)
            if (control is Button btn && btn.Content is string btnContent && IsTranslatable(btnContent))
            {
                _entries.Add(new TranslationEntry(control, PropKind.Content, btnContent));
            }

            // Check CheckBox.Content (string) — but skip "WidthAndHeight" special marker
            if (control is CheckBox cb && cb.Content is string cbContent
                && cbContent != "WidthAndHeight" && IsTranslatable(cbContent))
            {
                _entries.Add(new TranslationEntry(control, PropKind.Content, cbContent));
            }

            // Check RadioButton.Content (string)
            if (control is RadioButton rb && rb.Content is string rbContent && IsTranslatable(rbContent))
            {
                _entries.Add(new TranslationEntry(control, PropKind.Content, rbContent));
            }

            // Check MenuItem.Header (string)
            if (control is MenuItem mi && mi.Header is string miHeader && IsTranslatable(miHeader))
            {
                _entries.Add(new TranslationEntry(control, PropKind.Header, miHeader));
            }

            // Check Window.Title
            if (control is Window win && IsTranslatable(win.Title ?? ""))
            {
                _entries.Add(new TranslationEntry(control, PropKind.Title, win.Title!));
            }

            // Check Label.Content (string)
            if (control is Label lbl && lbl.Content is string lblContent && IsTranslatable(lblContent))
            {
                _entries.Add(new TranslationEntry(control, PropKind.Content, lblContent));
            }

            // Check TextBox.Watermark (placeholder text)
            if (control is TextBox textBox && IsTranslatable(textBox.Watermark ?? ""))
            {
                _entries.Add(new TranslationEntry(control, PropKind.Watermark, textBox.Watermark!));
            }

            // Recurse into logical children
            foreach (var child in control.GetLogicalChildren())
            {
                if (child is Control childControl)
                    ScanControl(childControl);
            }
        }

        void ApplyTranslations()
        {
            foreach (var entry in _entries)
            {
                string translated = R._(entry.OriginalText);
                switch (entry.Kind)
                {
                    case PropKind.Text:
                        if (entry.Control is TextBlock tb)
                            tb.Text = translated;
                        break;
                    case PropKind.Header:
                        if (entry.Control is Expander exp)
                            exp.Header = translated;
                        else if (entry.Control is TabItem tab)
                            tab.Header = translated;
                        else if (entry.Control is MenuItem mi)
                            mi.Header = translated;
                        break;
                    case PropKind.Content:
                        if (entry.Control is Button btn)
                            btn.Content = translated;
                        else if (entry.Control is CheckBox cb)
                            cb.Content = translated;
                        else if (entry.Control is RadioButton rb)
                            rb.Content = translated;
                        else if (entry.Control is Label lbl)
                            lbl.Content = translated;
                        break;
                    case PropKind.Title:
                        if (entry.Control is Window win)
                            win.Title = translated;
                        break;
                    case PropKind.Watermark:
                        if (entry.Control is TextBox textBox)
                            textBox.Watermark = translated;
                        break;
                }
            }
        }
    }
}
