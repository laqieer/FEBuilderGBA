using System;
using global::Avalonia;
using System.Threading.Tasks;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using global::Avalonia.Platform.Storage;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class OptionsView : TranslatedUserControl, IEmbeddableEditor
    {
        public string ViewTitle => "Options";
        public new bool IsLoaded => true;
        public EditorDescriptor Descriptor => new("Options", 620, 560);
        public event EventHandler? CloseRequested;
        public object? DialogResult { get; private set; }
        public void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);
        public void NavigateTo(uint address) { }

        readonly OptionsViewModel _vm = new();
        string _originalLanguageCode = "";
        bool _loadedOnce;
        TilesetFingerprint? _pendingTilesetContext;

        /// <summary>
        /// Test-only seam exposing this view's ViewModel instance for live-status assertions.
        /// Never used by production code.
        /// </summary>
        internal OptionsViewModel ViewModelForTests => _vm;

        public OptionsView()
        {
            InitializeComponent();
            // Cancel discovery or Save Mapping when this view detaches so no late worker result
            // can publish state or persist a mapping after the owning Options surface closes.
            DetachedFromVisualTree += (_, _) => _vm.CancelTilesetMappingOperation();
        }

        /// <summary>
        /// Set which map's tileset fingerprint the mapping section below applies to. Called by a
        /// Map Editor shortcut navigating here via
        /// <c>WindowManager.Instance.OpenModal&lt;OptionsView&gt;(owner, view =&gt; view.SetTilesetContext(fingerprint))</c>.
        /// Read-only/pure — never launches a process, never touches config or the network. Safe
        /// to call before this view has attached/loaded (the context is applied once loading
        /// completes) or afterwards (applied immediately).
        /// </summary>
        public void SetTilesetContext(TilesetFingerprint fingerprint)
        {
            _pendingTilesetContext = fingerprint;
            ShowFEMapCreatorSection();
            if (_loadedOnce)
                ApplyTilesetContext(fingerprint);
        }

        /// <summary>
        /// Selects External Tools and scrolls the FEMapCreator section into view without inventing
        /// a map fingerprint. Used by the setup wizard, which has no loaded-map context.
        /// </summary>
        public void ShowFEMapCreatorSection() => NavigateToFEMapCreatorSection();

        void ApplyTilesetContext(TilesetFingerprint fingerprint)
        {
            _vm.SetTilesetContext(fingerprint);
            RefreshTilesetMappingUi();
            ShowFEMapCreatorSection();
        }

        void NavigateToFEMapCreatorSection()
        {
            OptionsTabControl.SelectedItem = ExternalToolsTabItem;
            global::Avalonia.Threading.Dispatcher.UIThread.Post(
                () => FEMapCreatorSectionPanel.BringIntoView());
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            if (_loadedOnce) return;
            _loadedOnce = true;
            LoadOptions();
        }

        void LoadOptions()
        {
            _vm.Load();
            _originalLanguageCode = OptionsViewModel.ExtractLanguageCode(_vm.Language);

            // Populate language combo
            LanguageCombo.ItemsSource = _vm.AvailableLanguages;
            int langIdx = _vm.AvailableLanguages.IndexOf(_vm.Language);
            LanguageCombo.SelectedIndex = langIdx >= 0 ? langIdx : 0;

            // General tab
            GitPathTextBox.Text = _vm.GitPath;
            AutoBackupCheckBox.IsChecked = _vm.AutoBackup;
            AutoUpdateCheckBox.IsChecked = _vm.AutoUpdateEnabled;
            AutoSaveCheckBox.IsChecked = _vm.AutoSaveEnabled;
            AutoSaveIntervalBox.Value = _vm.AutoSaveIntervalMinutes;
            Patch2UrlTextBox.Text = _vm.SubmodulePatch2Url;
            FERepoUrlTextBox.Text = _vm.SubmoduleFERepoUrl;
            FERepoMusicUrlTextBox.Text = _vm.SubmoduleFERepoMusicUrl;

            // External Tools tab
            EmulatorTextBox.Text = _vm.Emulator;
            Emulator2TextBox.Text = _vm.Emulator2;
            BinaryEditorTextBox.Text = _vm.BinaryEditor;
            Program1TextBox.Text = _vm.Program1;
            Program2TextBox.Text = _vm.Program2;
            Program3TextBox.Text = _vm.Program3;
            SappyTextBox.Text = _vm.Sappy;
            Mid2agbTextBox.Text = _vm.Mid2agb;
            GbaMusRiperTextBox.Text = _vm.GbaMusRiper;
            SoxTextBox.Text = _vm.Sox;
            Midfix4agbTextBox.Text = _vm.Midfix4agb;
            EventAssemblerTextBox.Text = _vm.EventAssembler;
            DevkitproEabiTextBox.Text = _vm.DevkitproEabi;
            GoldroadAsmTextBox.Text = _vm.GoldroadAsm;
            CflagsTextBox.Text = _vm.Cflags;
            RetdecTextBox.Text = _vm.Retdec;
            Python3TextBox.Text = _vm.Python3;
            FeclibTextBox.Text = _vm.Feclib;
            SrccodeTexteditorTextBox.Text = _vm.SrccodeTexteditor;
            SrccodeDirectoryTextBox.Text = _vm.SrccodeDirectory;

            // FEMapCreator setup (#1978 Slice 2)
            FEMapCreatorPathTextBox.Text = _vm.FEMapCreatorPath;
            FEMapCreatorAssetsRootTextBox.Text = _vm.FEMapCreatorAssetsRoot;
            RefreshFEMapCreatorStatus();

            // Random-map tileset mapping (#1978 Slice 3 review finding #5)
            if (_pendingTilesetContext.HasValue)
                _vm.SetTilesetContext(_pendingTilesetContext.Value);
            RefreshTilesetMappingUi();
            if (_pendingTilesetContext.HasValue)
                NavigateToFEMapCreatorSection();
        }

        /// <summary>
        /// Refreshes the discovered-tileset list, status/error text, and button enabled-state from
        /// the ViewModel. Read-only — never launches a process or touches the network.
        /// </summary>
        void RefreshTilesetMappingUi()
        {
            TilesetMappingContextText.Text = _vm.HasTilesetContext
                ? R._("Configuring the random-map tileset mapping for the map that opened this section.")
                : R._("Open this section from a Map Editor's random-map controls to configure a mapping for its current tileset.");
            TilesetOptionsListBox.ItemsSource = _vm.Tilesets;
            TilesetOptionsListBox.SelectedItem = _vm.SelectedTileset;
            TilesetMappingStatusText.Text = _vm.TilesetMappingStatusMessage;
            TilesetMappingErrorText.Text = _vm.TilesetMappingErrorMessage;
            bool busy = _vm.IsTilesetMappingOperationInProgress;
            TilesetOptionsListBox.IsEnabled = !busy;
            DiscoverTilesetsButton.IsEnabled = !busy;
            CancelDiscoverTilesetsButton.IsEnabled = busy;
            SaveTilesetMappingButton.IsEnabled = _vm.HasTilesetContext && _vm.SelectedTileset != null && !busy;
        }

        /// <summary>
        /// Explicit user-initiated tileset discovery (#1978 Slice 3 review finding #3): runs off
        /// the UI thread via <see cref="OptionsViewModel.DiscoverTilesetsAsync"/>, keeps the view
        /// responsive, and supports cancellation via <see cref="CancelDiscoverTilesets_Click"/> or
        /// closing this view. Never runs automatically on Options construction/open/typing.
        /// </summary>
        async void DiscoverTilesets_Click(object? sender, RoutedEventArgs e)
        {
            if (_vm.IsTilesetMappingOperationInProgress) return;
            Task operation = _vm.DiscoverTilesetsAsync();
            RefreshTilesetMappingUi();
            try
            {
                await operation;
            }
            finally
            {
                RefreshTilesetMappingUi();
            }
        }

        void CancelDiscoverTilesets_Click(object? sender, RoutedEventArgs e) =>
            _vm.CancelTilesetMappingOperation();

        void TilesetOptionsListBox_SelectionChanged(object? sender, global::Avalonia.Controls.SelectionChangedEventArgs e)
        {
            _vm.SelectedTileset = TilesetOptionsListBox.SelectedItem as FEMapCreatorTilesetOption;
            RefreshTilesetMappingUi();
        }

        /// <summary>
        /// Persists the selected discovered tileset as the mapping for the current fingerprint
        /// context via <see cref="OptionsViewModel.SaveTilesetMappingAsync"/> (which itself uses
        /// <c>TryCreateEntry</c>/<c>Upsert</c>/<c>SaveAll</c>). Never launches a process.
        /// </summary>
        async void SaveTilesetMapping_Click(object? sender, RoutedEventArgs e)
        {
            if (_vm.IsTilesetMappingOperationInProgress) return;
            Task<bool> operation = _vm.SaveTilesetMappingAsync();
            RefreshTilesetMappingUi();
            try
            {
                await operation;
            }
            finally
            {
                RefreshTilesetMappingUi();
            }
        }

        /// <summary>
        /// Re-validates the (possibly unsaved) FEMapCreator path/assets-root textbox values and
        /// updates the status line. Read-only and metadata-only — never hashes executable
        /// content, launches a process, or touches the network.
        /// </summary>
        void RefreshFEMapCreatorStatus()
        {
            _vm.FEMapCreatorPath = FEMapCreatorPathTextBox.Text ?? "";
            _vm.FEMapCreatorAssetsRoot = FEMapCreatorAssetsRootTextBox.Text ?? "";
            FEMapCreatorStatusText.Text = _vm.GetFEMapCreatorStatusText();
        }

        /// <summary>
        /// Keeps the FEMapCreator status line honest as the user types directly into either
        /// textbox, not only after Browse/Clear/initial load. Read-only re-validation only — no
        /// process launch, discovery, or network access is triggered by editing text.
        /// </summary>
        void FEMapCreatorField_TextChanged(object? sender, global::Avalonia.Controls.TextChangedEventArgs e)
        {
            RefreshFEMapCreatorStatus();
            RefreshTilesetMappingUi();
        }

        async void BrowseFile_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not string targetName)
                return;
            var target = this.FindControl<TextBox>(targetName);
            if (target == null) return;

            var allFiles = new FilePickerFileType(R._("All Files")) { Patterns = new[] { "*" } };
            var exeFiles = new FilePickerFileType(R._("Executables")) { Patterns = new[] { "*.exe", "*" } };
            var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
            if (storage == null) return;
            var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = R._("Select File"),
                AllowMultiple = false,
                FileTypeFilter = new[] { exeFiles, allFiles },
            });
            if (files.Count > 0)
            {
                // #1639: these options configure paths to EXTERNAL tool
                // executables that are launched as processes (devkitARM / EA),
                // so a real filesystem path is required. On Android (no local
                // path) leave the field and message instead of silently doing
                // nothing.
                string? path = files[0].TryGetLocalPath();
                if (path != null)
                    target.Text = path;
                else
                    CoreState.Services?.ShowInfo(R._("This setting configures an external tool path and requires desktop file-system access; it is not available on this device."));
                RefreshFEMapCreatorStatus();
            }
        }

        async void BrowseFolder_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not string targetName)
                return;
            var target = this.FindControl<TextBox>(targetName);
            if (target == null) return;

            var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
            if (storage == null) return;
            var folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = R._("Select Directory"),
                AllowMultiple = false,
            });
            if (folders.Count > 0)
            {
                // #1639: tool directory config — a real local directory is
                // required. On Android (no local path) message instead of
                // silently doing nothing.
                string? path = folders[0].TryGetLocalPath();
                if (path != null)
                    target.Text = path;
                else
                    CoreState.Services?.ShowInfo(R._("This setting configures an external tool directory and requires desktop file-system access; it is not available on this device."));
                RefreshFEMapCreatorStatus();
            }
        }

        /// <summary>Clears the optional FEMapCreator assets-root field (empty is a valid, supported state).</summary>
        void ClearFEMapCreatorAssetsRoot_Click(object? sender, RoutedEventArgs e)
        {
            FEMapCreatorAssetsRootTextBox.Text = "";
            RefreshFEMapCreatorStatus();
        }

        void OkButton_Click(object? sender, RoutedEventArgs e)
        {
            // Push UI values back to ViewModel (display string, code extracted in Save())
            if (LanguageCombo.SelectedItem is string lang)
                _vm.Language = lang;  // e.g. "ja — 日本語"
            _vm.GitPath = GitPathTextBox.Text ?? "git";
            _vm.AutoBackup = AutoBackupCheckBox.IsChecked == true;
            _vm.AutoUpdateEnabled = AutoUpdateCheckBox.IsChecked == true;
            _vm.AutoSaveEnabled = AutoSaveCheckBox.IsChecked == true;
            _vm.AutoSaveIntervalMinutes = (int)(AutoSaveIntervalBox.Value ?? 5);
            _vm.SubmodulePatch2Url = Patch2UrlTextBox.Text ?? "";
            _vm.SubmoduleFERepoUrl = FERepoUrlTextBox.Text ?? "";
            _vm.SubmoduleFERepoMusicUrl = FERepoMusicUrlTextBox.Text ?? "";

            // External Tools
            _vm.Emulator = EmulatorTextBox.Text ?? "";
            _vm.Emulator2 = Emulator2TextBox.Text ?? "";
            _vm.BinaryEditor = BinaryEditorTextBox.Text ?? "";
            _vm.Program1 = Program1TextBox.Text ?? "";
            _vm.Program2 = Program2TextBox.Text ?? "";
            _vm.Program3 = Program3TextBox.Text ?? "";
            _vm.Sappy = SappyTextBox.Text ?? "";
            _vm.Mid2agb = Mid2agbTextBox.Text ?? "";
            _vm.GbaMusRiper = GbaMusRiperTextBox.Text ?? "";
            _vm.Sox = SoxTextBox.Text ?? "";
            _vm.Midfix4agb = Midfix4agbTextBox.Text ?? "";
            _vm.EventAssembler = EventAssemblerTextBox.Text ?? "";
            _vm.DevkitproEabi = DevkitproEabiTextBox.Text ?? "";
            _vm.GoldroadAsm = GoldroadAsmTextBox.Text ?? "";
            _vm.Cflags = CflagsTextBox.Text ?? "";
            _vm.Retdec = RetdecTextBox.Text ?? "";
            _vm.Python3 = Python3TextBox.Text ?? "";
            _vm.Feclib = FeclibTextBox.Text ?? "";
            _vm.SrccodeTexteditor = SrccodeTexteditorTextBox.Text ?? "";
            _vm.SrccodeDirectory = SrccodeDirectoryTextBox.Text ?? "";

            // FEMapCreator setup (#1978 Slice 2)
            _vm.FEMapCreatorPath = FEMapCreatorPathTextBox.Text ?? "";
            _vm.FEMapCreatorAssetsRoot = FEMapCreatorAssetsRootTextBox.Text ?? "";

            string newLangCode = OptionsViewModel.ExtractLanguageCode(_vm.Language);
            bool languageChanged = !string.Equals(_originalLanguageCode, newLangCode, StringComparison.Ordinal);

            _vm.Save();

            if (languageChanged)
            {
                // Language change takes effect immediately for new UI strings,
                // but some editors may need to be re-opened to see the change.
                // The main window refreshes via CoreState.LanguageChanged event.
            }

            { DialogResult = true; RequestClose(); }
        }

        void CancelButton_Click(object? sender, RoutedEventArgs e)
        {
            { DialogResult = false; RequestClose(); }
        }

        /// <summary>
        /// #1817: initialize (clone) or update (fetch+reset) the patch2 database in-app, next to its
        /// remote-URL field. Passes the current textbox value directly to the service as an override so a
        /// just-typed custom fork URL takes effect immediately, and persists ONLY the
        /// <c>submodule_patch2_url</c> config key (not the whole Options form) so the custom URL survives
        /// later auto-updates without committing other pending edits. Button disabled synchronously and
        /// re-enabled in a finally.
        /// </summary>
        async void InitUpdatePatch2_Click(object? sender, RoutedEventArgs e)
            => await RunContentRepoInitUpdate(InitUpdatePatch2Button, Patch2UrlTextBox,
                "submodule_patch2_url", GitUtil.Patch2RemoteUrl,
                Patch2GitService.GetPatch2Dir(CoreState.BaseDirectory ?? AppDomain.CurrentDomain.BaseDirectory),
                "Patch database");

        async void InitUpdateFERepo_Click(object? sender, RoutedEventArgs e)
            => await RunContentRepoInitUpdate(InitUpdateFERepoButton, FERepoUrlTextBox,
                "submodule_fe_repo_url", GitUtil.FERepoDefaultUrl,
                GitUtil.GetFERepoDir(CoreState.BaseDirectory ?? AppDomain.CurrentDomain.BaseDirectory),
                "FE-Repo");

        async void InitUpdateFERepoMusic_Click(object? sender, RoutedEventArgs e)
            => await RunContentRepoInitUpdate(InitUpdateFERepoMusicButton, FERepoMusicUrlTextBox,
                "submodule_fe_repo_music_url", GitUtil.FERepoMusicDefaultUrl,
                GitUtil.GetFERepoMusicDir(CoreState.BaseDirectory ?? AppDomain.CurrentDomain.BaseDirectory),
                "FE-Repo-Music");

        /// <summary>
        /// #1813: shared in-app Initialize (clone) / Update (fetch+reset) of a git-delivered content repo
        /// (patch2 / FE-Repo / FE-Repo-Music), next to its remote-URL field. Trims + persists ONLY that
        /// repo's own URL config key (never the whole Options form), passes the effective URL directly
        /// (falling back to <paramref name="defaultUrl"/> when blank), and runs off the UI thread. All
        /// user-facing messages use <paramref name="displayName"/> so a failure names the correct repo.
        /// The button is disabled synchronously and re-enabled in a finally.
        /// </summary>
        async System.Threading.Tasks.Task RunContentRepoInitUpdate(
            Button button, TextBox urlTextBox, string configKey, string defaultUrl, string repoDir, string displayName)
        {
            button.IsEnabled = false;
            try
            {
                string url = (urlTextBox.Text ?? "").Trim();
                urlTextBox.Text = url; // reflect the trim so a stray space can't be re-saved

                // Persist ONLY this repo's URL key (do NOT call the full _vm.Save()).
                var cfg = CoreState.Config;
                if (cfg != null)
                {
                    cfg[configKey] = url;
                    cfg.Save();
                }

                string effUrl = string.IsNullOrWhiteSpace(url) ? defaultUrl : url;

                var result = await Task.Run(() => ContentRepoGitService.InitializeOrUpdate(repoDir, effUrl, null));
                switch (result.Kind)
                {
                    case Patch2GitResultKind.GitNotFound:
                        CoreState.Services?.ShowError($"Git was not found. Install Git and try again, or set up {displayName} manually.");
                        break;
                    case Patch2GitResultKind.AlreadyRunning:
                        CoreState.Services?.ShowInfo("A content repository operation is already running.");
                        break;
                    case Patch2GitResultKind.Failed:
                        CoreState.Services?.ShowError($"{displayName} {(result.WasClone ? "initialize" : "update")} failed (git exit {result.ExitCode}).");
                        break;
                    case Patch2GitResultKind.Success:
                        CoreState.Services?.ShowInfo($"{displayName} updated. Restart recommended for all changes to take full effect.");
                        break;
                }
            }
            catch (Exception ex)
            {
                Log.Error("OptionsView", ex.ToString());
                CoreState.Services?.ShowError($"{displayName} operation failed: " + ex.Message);
            }
            finally
            {
                button.IsEnabled = true;
            }
        }
    }
}
