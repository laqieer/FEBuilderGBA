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
            // Translate tab (#947 bug #12): populate the from/to language combos
            // from the shared ToolTranslateROM arrays + wire the Translate button.
            PopulateTranslateCombos();
            TranslateButton.Click += OnTranslateClick;
            // Bind the conversation viewer tab's card collection ONCE. The
            // VM mutates the same ObservableCollection in place so we never
            // need to re-wire ItemsSource again.
            ConversationCardsList.ItemsSource = _convVm.Cards;
            // Lazy-load the Conversation Viewer tab: defer the parse + portrait
            // decode work until the user actually activates that tab.
            EditorTabs.SelectionChanged += OnEditorTabChanged;
            // Populate the Export Filter combo in the ctor (deterministic — no
            // dependency on the Opened event) so SelectedIndex="0" resolves.
            PopulateExportFilterCombo();
            // Keep the Export Limit label in sync with the selected filter
            // (finding 4): wire SelectionChanged + seed the initial label.
            ExportFilterCombo.SelectionChanged += OnExportFilterChanged;
            UpdateExportLimitLabel();
            Opened += (_, _) =>
            {
                LoadList();
                PopulateAddressBar();
                // Export Limit descriptor — describes the export filter that
                // will be applied. WF `TextForm.textBox1` shows the same kind
                // of read-only stub label. Re-sync in case the combo changed
                // before the window opened.
                UpdateExportLimitLabel();
            };
        }

        /// <summary>
        /// Populate the Export Filter combo with the 11 WF categories in WF order
        /// (#1028 Slice B). Index 0 = All (no filter); 1..10 map 1:1 to the WF
        /// <c>ToolTranslateROM.InitExportFilter</c> filter values.
        /// </summary>
        void PopulateExportFilterCombo()
        {
            if (ExportFilterCombo.ItemCount > 0) return; // already populated
            foreach (string key in ExportFilterCore.FilterLabelKeys)
            {
                ExportFilterCombo.Items.Add(new ComboBoxItem { Content = R._(key) });
            }
            ExportFilterCombo.SelectedIndex = 0;
        }

        void OnExportFilterChanged(object? sender, SelectionChangedEventArgs e)
        {
            UpdateExportLimitLabel();
        }

        /// <summary>
        /// Sync the read-only Export Limit textbox with the selected Export Filter
        /// category so it never misleads (finding 4): a filtered export shows the
        /// category name; index 0 / no selection shows "All".
        /// </summary>
        void UpdateExportLimitLabel()
        {
            int idx = ExportFilterCombo.SelectedIndex;
            string key = (idx >= 0 && idx < ExportFilterCore.FilterLabelKeys.Length)
                ? ExportFilterCore.FilterLabelKeys[idx]
                : ExportFilterCore.FilterLabelKeys[0]; // "All"
            ExportLimitBox.Text = R._(key);
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
            // Use the already-populated list count (TextList.ItemCount) instead
            // of calling `_vm.GetListCount()` which would re-decode every text
            // entry (LoadList already ran moments before PopulateAddressBar is
            // called from Opened / OnReloadClick).
            ReadCountBox.Text = TextList.ItemCount.ToString();
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
                // `addr` is the pointer-table entry address that was selected
                // in the AddressListControl. We back-compute the text ID from
                // `(addr - baseAddr) / 4` to stay consistent with the way
                // `LoadTextList` builds the entries (addr = baseAddr + id*4).
                // Issue #404: use `_vm.ResolveTextTableBase()` so the recovery
                // fallback (to `text_recover_address`) applies uniformly here
                // and in `LoadTextList` / `FindApproximatelyUnreferencedTexts`.
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
                    // Read-only computation of the current row's pointer-table
                    // entry address (not a write target; the VM's WriteText
                    // path handles actual ROM writes).
                    uint entryAddr = textBase + _vm.CurrentId * 4u;
                    if (entryAddr + 4 <= (uint)rom.Data.Length && U.isSafetyOffset(entryAddr, rom))
                    {
                        uint pointer = rom.u32(entryAddr);
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
        /// References tab → "Add Reference" (#1028 Slice A). Open the
        /// <see cref="TextRefAddDialogView"/> pre-filled with the currently-selected
        /// text id + its existing reference comment, then persist the result through
        /// the <see cref="ITextIDCache"/> seam (Update + Save) and refresh the
        /// cross-reference display. Mirrors WinForms <c>TextForm.ShowRefAddDialog</c>.
        /// </summary>
        async void OnAddReferenceClick(object? sender, RoutedEventArgs e)
        {
            try
            {
                if (!_vm.CanWrite) return;

                var cache = CoreState.UseTextIDCache;
                ROM rom = CoreState.ROM;
                if (cache == null || rom == null)
                {
                    ReferencesTabStatusLabel.Text = R._("(No ROM loaded)");
                    return;
                }

                uint textid = _vm.CurrentId;
                string existing = cache.GetName(textid) ?? "";

                var dlg = new TextRefAddDialogView();
                dlg.Init(textid, existing);
                var result = await dlg.ShowDialog<TextRefAddDialogViewModel?>(this);
                if (result == null) return; // Cancelled.

                // Persist via the cache seam. GetComment() applies the WF blank-entry
                // convention: a blank comment on a NEW entry is stored as a single
                // space (kept-but-blank); clearing an EXISTING entry removes it.
                cache.Update(textid, result.GetComment());
                cache.Save(rom.Filename);

                // Refresh the cross-reference display for the current text. LoadText
                // re-runs FindCrossReferences, which UpdateCrossReferences renders.
                _vm.LoadText(textid);
                UpdateUI();
            }
            catch (Exception ex)
            {
                Log.Error("TextViewerView.OnAddReferenceClick failed: {0}", ex.Message);
                ReferencesTabStatusLabel.Text = R._("Error: {0}", ex.Message);
            }
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

                // #1028 Slice C: thread the "Include AI Hints" checkbox state
                // into the export so per-entry face translate-info lines are
                // appended when requested.
                bool includeAIHints = IncludeAIHintsCheck.IsChecked == true;
                // #1028 Slice B: thread the Export Filter category. SelectedIndex
                // maps 1:1 to the WF filter value (0 = All).
                int filterIndex = ExportFilterCombo.SelectedIndex;
                int count = _vm.ExportAllTexts(path, includeAIHints, filterIndex);
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

        // #1028 Slice D — injectable interaction seams so the bad-char routing can
        // be unit-tested without a real Window (Copilot PR #1107 findings 1 & 2).
        // Defaults call the real Avalonia modal dialogs; tests substitute fakes.

        /// <summary>
        /// Shows the bad-character popup modally and AWAITS the chosen action
        /// ("GiveUp" / "AntiHuffman" / "EncodingTable" / null on close). Injectable.
        /// </summary>
        internal Func<string, Task<string?>> ShowBadCharPopupAsync;

        /// <summary>
        /// Opens the Patch Manager MODALLY and AWAITS it, so the user can install
        /// the AntiHuffman patch before the caller re-checks. Injectable.
        /// </summary>
        internal Func<Task> OpenPatchManagerModalAsync;

        Task<string?> ShowBadCharPopupDefaultAsync(string error)
        {
            string dialogText = R.Error("文字:{0}はシステムに登録されていません。", error);
            var popup = new TextBadCharPopupView(dialogText);
            return popup.ShowDialog<string?>(this);
        }

        Task OpenPatchManagerModalDefaultAsync()
        {
            // Open the Patch Manager as a MODAL dialog and AWAIT it — WriteText's
            // subsequent re-check only sees a freshly-installed patch if we block
            // here until the user closes the Patch Manager (Copilot finding 1).
            return WindowManager.Instance.OpenModal<PatchManagerView>(this);
        }

        async void OnWriteTextClick(object? sender, RoutedEventArgs e)
        {
            if (!_vm.CanWrite) return;
            string editedText = EditTextBox.Text ?? "";

            // #1028 Slice D: WF TextForm.WriteText flow. Pre-flight the Huffman
            // encode (no mutation). On a bad-character failure, if the AntiHuffman
            // patch is missing, run the WF NeedAntiHuffman interaction (popup for
            // ja/zh/ko, Patch Manager for other langs) AWAITING the modal so a
            // freshly-installed patch is visible to the re-check. Then VM.WriteText
            // re-checks the patch and either UnHuffman-encodes (patch now present)
            // or aborts with no mutation. The async dialogs are orchestrated HERE
            // (the VM stays synchronous + UI-free).
            ROM rom = CoreState.ROM;
            string? encodeError = (rom != null) ? _vm.PeekEncodeError(editedText) : null;
            if (encodeError != null && rom != null
                && !PatchDetection.SearchAntiHuffmanPatch(rom))
            {
                await ResolveAntiHuffmanInteractionAsync(encodeError);
            }

            // The async prompt (if any) has already run — including AWAITING any
            // modal Patch Manager so an install is committed before this point.
            // Give WriteText a synchronous re-check callback that simply reports the
            // CURRENT patch state — never re-prompts on the UI thread from inside
            // the sync VM.
            _vm.AntiHuffmanPromptCallback = _ => CoreState.ROM != null
                && PatchDetection.SearchAntiHuffmanPatch(CoreState.ROM);

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
            catch (TextViewerViewModel.EncodeAbortedException ex)
            {
                // WF-faithful abort: no ROM mutation, no undo commit.
                _undoService.Rollback();
                WriteStatusLabel.Text = ex.Message;
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                WriteStatusLabel.Text = $"Write failed: {ex.Message}";
                Log.Error("WriteText failed: {0}", ex.Message);
            }
            finally
            {
                _vm.AntiHuffmanPromptCallback = null;
            }
        }

        /// <summary>
        /// Language gate for the bad-character popup, mirroring WF
        /// <c>TextForm.NeedAntiHuffman</c> which opens <c>TextBadCharPopupForm</c>
        /// only for ja/zh/ko (other languages route to the AntiHuffman patch
        /// recommendation instead — here, the Patch Manager).
        /// </summary>
        internal static bool IsBadCharPopupLanguage()
        {
            string lang = CoreState.Language ?? "";
            return lang == "ja" || lang == "zh" || lang == "ko";
        }

        /// <summary>
        /// Run the WF <c>NeedAntiHuffman</c> bad-character interaction (#1028 Slice D),
        /// AWAITING every modal so the subsequent <c>PatchDetection.SearchAntiHuffmanPatch</c>
        /// re-check (in <see cref="OnWriteTextClick"/>) sees a freshly-installed patch.
        /// Routing (Copilot PR #1107 findings 1 &amp; 2):
        /// <list type="bullet">
        ///   <item><b>ja/zh/ko</b>: show <c>TextBadCharPopupView</c>; on the
        ///   <c>"AntiHuffman"</c> choice, open the Patch Manager MODALLY and AWAIT it
        ///   (so install→re-check→proceed works); <c>GiveUp</c>/<c>EncodingTable</c>/
        ///   close are no-ops (WriteText then re-checks and aborts if unresolved —
        ///   the Encoding-Table char-code editor is not yet ported to Avalonia).</item>
        ///   <item><b>other languages</b>: route directly to the Patch Manager
        ///   (MODAL + AWAIT) so the user gets actionable guidance to install
        ///   AntiHuffman instead of a silent abort with no UI.</item>
        /// </list>
        /// Both injectable seams default to the real Avalonia dialogs; tests
        /// substitute fakes to assert the routing without a real Window.
        /// </summary>
        internal async Task ResolveAntiHuffmanInteractionAsync(string error)
        {
            var showPopup = ShowBadCharPopupAsync ?? ShowBadCharPopupDefaultAsync;
            var openPatchManager = OpenPatchManagerModalAsync ?? OpenPatchManagerModalDefaultAsync;

            if (IsBadCharPopupLanguage())
            {
                string? action = await showPopup(error);
                if (action == "AntiHuffman")
                {
                    // Open the Patch Manager MODALLY + AWAIT so the install is
                    // committed before WriteText re-checks (Copilot finding 1).
                    await openPatchManager();
                }
                // GiveUp / EncodingTable / null (closed): no navigation.
            }
            else
            {
                // Non-ja/zh/ko: WF routes English to the patch recommendation flow.
                // Open the Patch Manager (modal + await) so the user has actionable
                // guidance to install AntiHuffman, instead of a silent abort with no
                // UI (Copilot finding 2).
                await openPatchManager();
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

        // ============================================================
        // Translate tab (#947 bug #12)
        // ============================================================

        /// <summary>
        /// Populate the from/to language combos from the SHARED
        /// <see cref="ToolTranslateROMViewModel"/> language arrays (single
        /// source of truth — never duplicated here). Display values are routed
        /// through <c>R._(...)</c> so the visible labels follow the active UI
        /// language while the underlying <c>code=label</c> items keep their
        /// <c>ParseLanguageKey</c>-parseable prefix. Default indexes come from
        /// the same <see cref="ToolTranslateROMViewModel.CalcDefaultLanguageIndexes"/>
        /// logic as the ROM↔ROM translate tool.
        /// </summary>
        void PopulateTranslateCombos()
        {
            try
            {
                var fromRaw = ToolTranslateROMViewModel.FromLanguageItemsRaw;
                var toRaw = ToolTranslateROMViewModel.ToLanguageItemsRaw;

                var fromItems = new string[fromRaw.Length];
                for (int i = 0; i < fromRaw.Length; i++) fromItems[i] = R._(fromRaw[i]);
                var toItems = new string[toRaw.Length];
                for (int i = 0; i < toRaw.Length; i++) toItems[i] = R._(toRaw[i]);

                TranslateFromCombo.ItemsSource = fromItems;
                TranslateToCombo.ItemsSource = toItems;

                // Default selection mirrors the ROM↔ROM translate tool: derive
                // from the current ROM multibyte flag, text encoding + UI lang.
                var rom = CoreState.ROM;
                bool isMultibyte = rom?.RomInfo?.is_multibyte ?? false;
                var (from, to) = ToolTranslateROMViewModel.CalcDefaultLanguageIndexes(
                    isMultibyte, CoreState.TextEncoding, CoreState.Language ?? "en");

                if (from >= 0 && from < fromItems.Length) TranslateFromCombo.SelectedIndex = from;
                else if (fromItems.Length > 0) TranslateFromCombo.SelectedIndex = 0;

                if (to >= 0 && to < toItems.Length) TranslateToCombo.SelectedIndex = to;
                else if (toItems.Length > 0) TranslateToCombo.SelectedIndex = 0;
            }
            catch (Exception ex)
            {
                Log.Error("TextViewerView.PopulateTranslateCombos failed: {0}", ex.Message);
            }
        }

        /// <summary>
        /// Resolve the language code (e.g. "ja", "en", "zh-CN") for the given
        /// combo by re-parsing the RAW array entry at the selected index via
        /// <see cref="ToolTranslateROMCore.ParseLanguageKey"/>. We parse the raw
        /// (untranslated) item rather than the displayed text so the
        /// <c>code=label</c> prefix is always present regardless of UI language.
        /// </summary>
        static string ResolveLanguageCode(ComboBox combo, string[] raw)
        {
            int idx = combo.SelectedIndex;
            if (idx < 0 || idx >= raw.Length) return string.Empty;
            return ToolTranslateROMCore.ParseLanguageKey(raw[idx]);
        }

        /// <summary>
        /// Translate the currently-selected text from the source to the target
        /// language and drop the result into the Edit box for review (the user
        /// then writes it back via the existing Edit-tab Write flow). Mirrors WF
        /// <c>TextForm.TranslateButton_Click</c>.
        ///
        /// Routes through <see cref="TranslateTextUtilCore.TranslateText"/>
        /// (#967, follow-up to the #949 MVP): FE control codes (<c>@0001</c>,
        /// <c>@0003</c>, …) are split out and re-inserted verbatim so they survive
        /// translation, and a literal segment that matches the shipped fixed
        /// glossary (<c>config/translate/dic_*.txt</c>) is taken from the
        /// dictionary instead of calling Google.
        /// </summary>
        async void OnTranslateClick(object? sender, RoutedEventArgs e)
        {
            string fromCode = ResolveLanguageCode(TranslateFromCombo, ToolTranslateROMViewModel.FromLanguageItemsRaw);
            string toCode = ResolveLanguageCode(TranslateToCombo, ToolTranslateROMViewModel.ToLanguageItemsRaw);

            string text = EditTextBox.Text ?? "";
            // No-op guards (each with a DISTINCT status so the user can tell
            // which precondition failed):
            //   1. nothing to translate,
            //   2. missing/invalid language selection (e.g. a combo never got
            //      populated or has no selection — empty parsed code),
            //   3. source language == target language (both non-empty + equal).
            if (string.IsNullOrWhiteSpace(text))
            {
                TranslateStatusLabel.Text = R._("(No text to translate)");
                return;
            }
            if (string.IsNullOrEmpty(fromCode) || string.IsNullOrEmpty(toCode))
            {
                TranslateStatusLabel.Text = R._("(Select a source and target language)");
                return;
            }
            if (fromCode == toCode)
            {
                TranslateStatusLabel.Text = R._("(Source and target language are the same)");
                return;
            }

            // Load (and cache, per from/to) the fixed glossary so dictionary
            // hits skip the network. LoadFixedDic never throws — a missing file
            // or unset BaseDirectory simply yields an empty glossary.
            var dic = TranslateTextUtilCore.LoadFixedDic(fromCode, toCode);

            TranslateButton.IsEnabled = false;
            TranslateStatusLabel.Text = R._("Translating...");
            try
            {
                // TranslateTextUtilCore.TranslateText protects @control-codes and
                // prefers the glossary; for non-glossary segments it makes the
                // SYNCHRONOUS online (Google) TranslateManage.Trans call — so run
                // the whole thing off the UI thread to keep the window responsive.
                string result = await Task.Run(() =>
                    TranslateTextUtilCore.TranslateText(text, fromCode, toCode, dic, useGoogle: true));

                // Reject empty/garbage results — never overwrite EditTextBox with
                // an empty or error-shaped string.
                if (string.IsNullOrWhiteSpace(result))
                {
                    Log.Error("TextViewerView.OnTranslateClick: translation returned empty for {0}->{1}", fromCode, toCode);
                    CoreState.Services?.ShowError(R._("Translation failed: the service returned no result."));
                    return;
                }

                // Success: put the translated text in the Edit box for review.
                // The existing OnEditTextChanged handler re-validates length.
                EditTextBox.Text = result;
            }
            catch (System.Net.WebException wex)
            {
                Log.Error("TextViewerView.OnTranslateClick WebException: {0}", wex.Message);
                CoreState.Services?.ShowError(R._("Google translation returned an error. You may have sent too many requests.\r\n\r\n{0}", wex.Message));
            }
            catch (Exception ex)
            {
                Log.Error("TextViewerView.OnTranslateClick failed: {0}", ex.Message);
                CoreState.Services?.ShowError(R._("Translation failed: {0}", ex.Message));
            }
            finally
            {
                TranslateButton.IsEnabled = true;
                TranslateStatusLabel.Text = "";
            }
        }

        public void SelectFirstItem()
        {
            TextList.SelectFirst();
        }

        public ViewModelBase? DataViewModel => _vm;
    }
}
