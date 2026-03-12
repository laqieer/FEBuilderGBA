using System;
using System.Threading.Tasks;
using global::Avalonia.Controls;
using global::Avalonia.Controls.Documents;
using global::Avalonia.Interactivity;
using global::Avalonia.Media;
using global::Avalonia.Platform.Storage;
using FEBuilderGBA.Avalonia.Dialogs;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class TextViewerView : Window, IEditorView, IDataVerifiableView
    {
        readonly TextViewerViewModel _vm = new();
        readonly UndoService _undoService = new();

        public string ViewTitle => "Text Editor";
        public bool IsLoaded => _vm.CanWrite;

        static readonly IBrush YellowBrush = new SolidColorBrush(Colors.Orange);
        static readonly IBrush RedBrush = new SolidColorBrush(Colors.Red);
        static readonly IBrush GreenBrush = new SolidColorBrush(Colors.Green);

        public TextViewerView()
        {
            InitializeComponent();
            TextList.SelectedAddressChanged += OnTextSelected;
            WriteTextButton.Click += OnWriteTextClick;
            EditTextBox.TextChanged += OnEditTextChanged;
            Opened += (_, _) => LoadList();
        }

        void LoadList()
        {
            try
            {
                var items = _vm.LoadTextList();
                TextList.SetItems(items);
            }
            catch (Exception ex)
            {
                Log.Error("TextViewerView.LoadList failed: {0}", ex.Message);
            }
        }

        void OnTextSelected(uint addr)
        {
            try
            {
                // The addr is the pointer table entry address, but we need the text ID (index)
                // The AddrResult.tag contains the index
                ROM rom = CoreState.ROM;
                if (rom?.RomInfo == null) return;

                uint textPtr = rom.RomInfo.text_pointer;
                if (textPtr == 0) return;
                uint baseAddr = rom.p32(textPtr);
                if (baseAddr == 0 || !U.isSafetyOffset(baseAddr)) return;

                uint id = (addr - baseAddr) / 4;
                _vm.LoadText(id);
                UpdateUI();
            }
            catch (Exception ex)
            {
                Log.Error("TextViewerView.OnTextSelected failed: {0}", ex.Message);
            }
        }

        public void NavigateTo(uint address)
        {
            TextList.SelectAddress(address);
        }

        static readonly IBrush BlueBrush = new SolidColorBrush(Colors.Blue);

        void UpdateUI()
        {
            TextIdLabel.Text = $"Text ID: 0x{_vm.CurrentId:X04}";
            ApplyHighlightedText(DecodedTextBlock, _vm.DecodedText);
            EditTextBox.Text = _vm.DecodedText;
            UpdateLengthWarning();
            UpdateCrossReferences();
        }

        void OnEditTextChanged(object? sender, TextChangedEventArgs e)
        {
            if (!_vm.CanWrite) return;
            string text = EditTextBox.Text ?? "";
            _vm.ValidateText(text);
            UpdateLengthWarning();
        }

        void UpdateLengthWarning()
        {
            string warning = _vm.LengthWarning;
            LengthWarningLabel.Text = warning;
            if (string.IsNullOrEmpty(warning))
            {
                LengthWarningLabel.IsVisible = false;
            }
            else if (_vm.OriginalLength > 0 && _vm.EncodedLength > _vm.OriginalLength)
            {
                LengthWarningLabel.Foreground = RedBrush;
                LengthWarningLabel.IsVisible = true;
            }
            else if (_vm.OriginalLength > 0 && _vm.EncodedLength == _vm.OriginalLength)
            {
                LengthWarningLabel.Foreground = YellowBrush;
                LengthWarningLabel.IsVisible = true;
            }
            else
            {
                LengthWarningLabel.Foreground = GreenBrush;
                LengthWarningLabel.IsVisible = true;
            }
        }

        void UpdateCrossReferences()
        {
            var refs = _vm.CrossReferences;
            if (refs.Count == 0)
                CrossRefList.ItemsSource = new[] { "(No references found)" };
            else
                CrossRefList.ItemsSource = refs;
        }

        /// <summary>
        /// Parse text for [...] control code sequences and render them in blue.
        /// Mirrors WinForms TextForm.KeywordHighLightFEditor() bracket scanning.
        /// </summary>
        static void ApplyHighlightedText(SelectableTextBlock block, string text)
        {
            block.Inlines?.Clear();
            if (block.Inlines == null || string.IsNullOrEmpty(text))
            {
                block.Text = text ?? "";
                return;
            }

            int i = 0;
            int normalStart = 0;
            while (i < text.Length)
            {
                if (text[i] == '[')
                {
                    // Find matching ']'
                    int close = text.IndexOf(']', i + 1);
                    if (close > i)
                    {
                        // Emit any preceding normal text
                        if (i > normalStart)
                            block.Inlines.Add(new Run(text.Substring(normalStart, i - normalStart)));
                        // Emit the bracketed control code in blue
                        block.Inlines.Add(new Run(text.Substring(i, close - i + 1))
                        {
                            Foreground = BlueBrush
                        });
                        i = close + 1;
                        normalStart = i;
                        continue;
                    }
                }
                i++;
            }
            // Emit trailing normal text
            if (normalStart < text.Length)
                block.Inlines.Add(new Run(text.Substring(normalStart)));
        }

        async void OnExportClick(object? sender, RoutedEventArgs e)
        {
            try
            {
                var tsvType = new FilePickerFileType("TSV Files") { Patterns = new[] { "*.tsv" } };
                var allType = new FilePickerFileType("All Files") { Patterns = new[] { "*" } };
                var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = "Export Texts",
                    SuggestedFileName = "texts.tsv",
                    FileTypeChoices = new[] { tsvType, allType },
                });
                string? path = file?.TryGetLocalPath();
                if (path == null) return;

                int count = _vm.ExportAllTexts(path);
                await MessageBoxWindow.Show(this, $"Exported {count} text entries to TSV.", "Export Complete", MessageBoxMode.Ok);
            }
            catch (Exception ex)
            {
                Log.Error("Export failed: {0}", ex.Message);
                await MessageBoxWindow.Show(this, $"Export failed: {ex.Message}", "Error", MessageBoxMode.Ok);
            }
        }

        async void OnImportClick(object? sender, RoutedEventArgs e)
        {
            try
            {
                string? path = await FileDialogHelper.OpenFile(this, "TSV Files", "*.tsv");
                if (path == null) return;

                int count = _vm.ImportAllTexts(path);
                if (count > 0)
                {
                    LoadList(); // Refresh the list to show updated texts
                    await MessageBoxWindow.Show(this, $"Imported {count} text entries.", "Import Complete", MessageBoxMode.Ok);
                }
                else
                {
                    await MessageBoxWindow.Show(this, "No texts were imported. Check the file format.", "Import", MessageBoxMode.Ok);
                }
            }
            catch (Exception ex)
            {
                Log.Error("Import failed: {0}", ex.Message);
                await MessageBoxWindow.Show(this, $"Import failed: {ex.Message}", "Error", MessageBoxMode.Ok);
            }
        }

        void OnWriteTextClick(object? sender, RoutedEventArgs e)
        {
            if (!_vm.CanWrite) return;
            string editedText = EditTextBox.Text ?? "";
            _undoService.Begin("Edit Text");
            try
            {
                _vm.WriteText(_vm.CurrentId, editedText);
                _undoService.Commit();
                _vm.MarkClean();
                WriteStatusLabel.Text = "Written successfully.";
                // Reload to show updated text
                _vm.LoadText(_vm.CurrentId);
                UpdateUI();
                LoadList(); // refresh list preview
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                WriteStatusLabel.Text = $"Write failed: {ex.Message}";
                Log.Error("WriteText failed: {0}", ex.Message);
            }
        }

        void OnSearchContentClick(object? sender, RoutedEventArgs e)
        {
            string query = ContentSearchBox.Text ?? "";
            if (string.IsNullOrWhiteSpace(query))
            {
                SearchResultLabel.Text = "Enter a search term.";
                return;
            }
            try
            {
                var results = _vm.SearchTexts(query);
                TextList.SetItems(results);
                SearchResultLabel.Text = $"{results.Count} results";
            }
            catch (Exception ex)
            {
                SearchResultLabel.Text = $"Error: {ex.Message}";
                Log.Error("SearchContent failed: {0}", ex.Message);
            }
        }

        void OnShowAllClick(object? sender, RoutedEventArgs e)
        {
            SearchResultLabel.Text = "";
            LoadList();
        }

        public void SelectFirstItem()
        {
            TextList.SelectFirst();
        }

        public ViewModelBase? DataViewModel => _vm;
    }
}
