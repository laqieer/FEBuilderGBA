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
    public partial class TextViewerView : TranslatedWindow, IEditorView, IDataVerifiableView
    {
        readonly TextViewerViewModel _vm = new();
        readonly ConversationViewerTabViewModel _convVm = new();
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
            // Bind the conversation viewer tab's card collection ONCE. The
            // VM mutates the same ObservableCollection in place so we never
            // need to re-wire ItemsSource again.
            ConversationCardsList.ItemsSource = _convVm.Cards;
            // Lazy-load the Conversation Viewer tab: defer the parse + portrait
            // decode work until the user actually activates that tab.
            EditorTabs.SelectionChanged += OnEditorTabChanged;
            Opened += (_, _) =>
            {
                LoadList();
                PopulateAddressBar();
                // Export Limit descriptor — describes the export filter that
                // will be applied. WF `TextForm.textBox1` shows the same kind
                // of read-only stub label. Both depend on `ToolTranslateROM`
                // Core extraction (same blocker as `ExportFilterCombo`).
                ExportLimitBox.Text = R._("All (out-of-scope)");
            };
        }

        /// <summary>
        /// Populate the read-only Address Bar widgets on the Search Tools tab.
        /// Issue #404: WF `TextForm.ReadStartAddress` mirrors `InputFormRef.BaseAddress`
        /// which equals `rom.p32(rom.RomInfo.text_pointer)` (with the
        /// recovery fallback to `text_recover_address` for unsafe pointers).
        /// We delegate to `_vm.ResolveTextTableBase()` so the recovery path
        /// stays consistent across the view and VM.
        /// </summary>
        void PopulateAddressBar()
        {
            uint baseAddr = _vm.ResolveTextTableBase();
            if (baseAddr == 0)
            {
                ReadStartAddressBox.Text = R._("(invalid)");
                ReadCountBox.Text = "0";
                SizeValueBox.Text = "0x4";
                FilterValueBox.Text = "";
                return;
            }
            ReadStartAddressBox.Text = "0x" + baseAddr.ToString("X08");
            ReadCountBox.Text = _vm.GetListCount().ToString();
            SizeValueBox.Text = "0x4";
            FilterValueBox.Text = "";
        }

        void OnEditorTabChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (EditorTabs.SelectedItem == ConversationTab)
            {
                _convVm.EnsureCurrent();
            }
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
                // The AddrResult.tag contains the index.
                // Issue #404: use `_vm.ResolveTextTableBase()` so the recovery
                // fallback (to `text_recover_address`) applies uniformly here
                // and in `LoadTextList()` / `FindApproximatelyUnreferencedTexts()`.
                ROM rom = CoreState.ROM;
                if (rom?.RomInfo == null) return;

                uint baseAddr = _vm.ResolveTextTableBase();
                if (baseAddr == 0) return;

                uint id = (addr - baseAddr) / 4;
                _vm.LoadText(id);
                // Just mark the conversation viewer's pending id; the actual
                // decode + portrait work only runs when the user activates
                // the Conversation Viewer tab (see OnEditorTabChanged).
                _convVm.SetPendingTextId(id);
                if (EditorTabs.SelectedItem == ConversationTab)
                {
                    _convVm.EnsureCurrent();
                }
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
            UpdatePointerDisplay();
        }

        /// <summary>
        /// Update the inline Pointer + Refs displays on the Edit tab.
        /// Pointer = u32 read from textBase + id*4 (mirrors WF AddressPointer).
        /// Refs count = current cross-reference list count.
        /// </summary>
        void UpdatePointerDisplay()
        {
            try
            {
                ROM rom = CoreState.ROM;
                if (rom?.RomInfo == null || rom.Data == null)
                {
                    PointerValueBox.Text = R._("(invalid)");
                    RefsCountBox.Text = "0";
                    return;
                }
                uint textBase = _vm.ResolveTextTableBase();
                if (textBase == 0)
                {
                    PointerValueBox.Text = R._("(invalid)");
                }
                else
                {
                    uint writePointer = textBase + _vm.CurrentId * 4u;
                    if (writePointer + 4 <= (uint)rom.Data.Length && U.isSafetyOffset(writePointer, rom))
                    {
                        uint pointer = rom.u32(writePointer);
                        PointerValueBox.Text = "0x" + pointer.ToString("X08");
                    }
                    else
                    {
                        PointerValueBox.Text = R._("(invalid)");
                    }
                }
                RefsCountBox.Text = _vm.CrossReferences.Count.ToString();
            }
            catch (Exception ex)
            {
                Log.Error("TextViewerView.UpdatePointerDisplay: {0}", ex.Message);
                PointerValueBox.Text = R._("(invalid)");
                RefsCountBox.Text = "0";
            }
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
            {
                CrossRefList.ItemsSource = new[] { R._("(No references found)") };
                ReferencesTabList.ItemsSource = new[] { R._("(No references found)") };
            }
            else
            {
                CrossRefList.ItemsSource = refs;
                ReferencesTabList.ItemsSource = refs;
            }
            ReferencesTabStatusLabel.Text = R._("References found: {0}", refs.Count);
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
                var tsvType = new FilePickerFileType(R._("TSV Files")) { Patterns = new[] { "*.tsv" } };
                var allType = new FilePickerFileType(R._("All Files")) { Patterns = new[] { "*" } };
                var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = R._("Export Texts"),
                    SuggestedFileName = "texts.tsv",
                    FileTypeChoices = new[] { tsvType, allType },
                });
                string? path = file?.TryGetLocalPath();
                if (path == null) return;

                int count = _vm.ExportAllTexts(path);
                await MessageBoxWindow.Show(this, $"Exported {count} text entries to TSV.", R._("Export Complete"), MessageBoxMode.Ok);
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
                    await MessageBoxWindow.Show(this, $"Imported {count} text entries.", R._("Import Complete"), MessageBoxMode.Ok);
                }
                else
                {
                    await MessageBoxWindow.Show(this, R._("No texts were imported. Check the file format."), "Import", MessageBoxMode.Ok);
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

        /// <summary>
        /// Reload the text list from ROM and refresh the address-bar widgets.
        /// Wired to the Reload button on the Search Tools tab.
        /// </summary>
        void OnReloadClick(object? sender, RoutedEventArgs e)
        {
            LoadList();
            PopulateAddressBar();
            FreeAreaStatusLabel.Text = "";
        }

        /// <summary>
        /// Issue #404: scan all text IDs for unreferenced "free area" slots
        /// (mirror WF `SearcFreeArea_Click`, approximate scope per VM docstring).
        /// Replaces the `TextList` items with the free-area results; user can
        /// then select any to inspect it.
        /// </summary>
        void OnSearchFreeAreaClick(object? sender, RoutedEventArgs e)
        {
            try
            {
                var results = _vm.FindApproximatelyUnreferencedTexts();
                TextList.SetItems(results);
                FreeAreaStatusLabel.Text = R._("Free area: {0} results", results.Count);
            }
            catch (Exception ex)
            {
                FreeAreaStatusLabel.Text = R._("Error: {0}", ex.Message);
                Log.Error("SearchFreeArea failed: {0}", ex.Message);
            }
        }

        public void SelectFirstItem()
        {
            TextList.SelectFirst();
        }

        public ViewModelBase? DataViewModel => _vm;
    }
}
