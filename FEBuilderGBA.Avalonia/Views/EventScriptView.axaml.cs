using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class EventScriptView : Window, IEditorView
    {
        ViewTranslationHelper _translator;

        readonly EventScriptViewModel _vm = new();

        public string ViewTitle => "Event Script Editor";
        public bool IsLoaded => _vm.IsLoaded;

        public EventScriptView()
        {
            InitializeComponent();
            // Translation support
            _translator = new ViewTranslationHelper(this);
            _translator.TranslateAll();
            CoreState.LanguageChanged += _translator.OnLanguageChanged;
            CommandsList.ItemsSource = _vm.Commands;
        }

        void Disassemble_Click(object? sender, RoutedEventArgs e)
        {
            _vm.AddressText = AddressBox.Text ?? "";
            RunDisassemble();
        }

        void RunDisassemble()
        {
            if (_vm.TryParseAddress(out uint address))
            {
                _vm.DisassembleAt(address);
                ScriptTextBox.Text = _vm.DisassembledText;
                StatusLabel.Text = _vm.StatusText;
            }
            else
            {
                StatusLabel.Text = "Invalid address. Enter a hex value like 0x08001234 or 1234.";
            }
        }

        void CommandsList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            int index = CommandsList.SelectedIndex;
            _vm.SelectedCommandIndex = index;

            // Open popup editor for the selected command
            if (index >= 0 && index < _vm.Commands.Count)
            {
                // Could open EventScriptPopupView here for detailed editing
            }
        }

        async void Category_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                var dialog = new EventScriptCategorySelectView();
                var result = await dialog.ShowDialog<string?>(this);
                if (!string.IsNullOrEmpty(result))
                {
                    StatusLabel.Text = $"Selected category: {result}";
                }
            }
            catch (Exception ex)
            {
                Log.Error("EventScriptView.Category_Click failed: {0}", ex.Message);
            }
        }

        /// <summary>Navigate to a specific address and disassemble.</summary>
        public void NavigateTo(uint address)
        {
            _vm.AddressText = $"0x{address:X08}";
            AddressBox.Text = _vm.AddressText;
            RunDisassemble();
        }

        public void SelectFirstItem()
        {
            if (CommandsList.ItemCount > 0)
                CommandsList.SelectedIndex = 0;
        }

        protected override void OnClosed(EventArgs e)
        {
            CoreState.LanguageChanged -= _translator.OnLanguageChanged;
            base.OnClosed(e);
        }
    }
}
