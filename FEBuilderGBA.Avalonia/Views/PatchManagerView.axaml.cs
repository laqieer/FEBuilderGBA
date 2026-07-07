using global::Avalonia;
using System;
using System.Threading;
using System.Threading.Tasks;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using global::Avalonia.Threading;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class PatchManagerView : TranslatedUserControl, IEmbeddableEditor
    {
        readonly PatchManagerViewModel _vm = new();
        bool _hasLoadedList;

        public string ViewTitle => "Patch Manager";
        public new bool IsLoaded => _vm.IsLoaded;
        public EditorDescriptor Descriptor => new("Patch Manager", 1100, 650, SizeToContent: global::Avalonia.Controls.SizeToContent.WidthAndHeight);
        public event EventHandler? CloseRequested;
        public void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);

        public PatchManagerView()
        {
            InitializeComponent();
            PatchListBox.SelectionChanged += OnPatchSelected;
            SearchBox.TextChanged += OnSearchTextChanged;
            InstallButton.Click += OnInstallClick;
            ForceInstallButton.Click += OnForceInstallClick;
            UninstallButton.Click += OnUninstallClick;
            InitUpdatePatch2Button.Click += OnInitUpdatePatch2Click;
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            if (!_hasLoadedList)
            {
                _hasLoadedList = true;
                LoadPatches();
            }
        }

        void LoadPatches()
        {
            try
            {
                _vm.LoadPatchList();
                PatchListBox.ItemsSource = _vm.FilteredPatches;
                UpdateSummary();
                InitUpdatePatch2Button.Content = _vm.Patch2ButtonText;
                // Surface the VM's load-time status (e.g. the Android patch2-unavailable
                // empty-state notice, #1641) into the status label. Always assign so a
                // cleared StatusMessage ("") also resets the label — never leaves a stale notice.
                StatusMessageLabel.Text = _vm.StatusMessage;
            }
            catch (Exception ex)
            {
                Log.ErrorF("PatchManagerView.LoadPatches failed: {0}", ex.Message);
            }
        }

        void OnSearchTextChanged(object? sender, TextChangedEventArgs e)
        {
            _vm.FilterText = SearchBox.Text ?? "";
            UpdateSummary();
        }

        void OnPatchSelected(object? sender, SelectionChangedEventArgs e)
        {
            if (PatchListBox.SelectedItem is PatchEntry patch)
            {
                _vm.SelectedPatch = patch;
                UpdateDetails(patch);
            }
        }

        void UpdateSummary()
        {
            string filter = string.IsNullOrWhiteSpace(_vm.FilterText) ? "" : $" (filtered: {_vm.FilteredPatches.Count})";
            SummaryLabel.Text = $"Total: {_vm.TotalCount} patches | Installed: {_vm.InstalledCount}{filter}";
        }

        void UpdateDetails(PatchEntry patch)
        {
            DetailName.Text = patch.Name;
            DetailStatus.Text = patch.StatusText;
            DetailAuthor.Text = string.IsNullOrEmpty(patch.Author) ? "(unknown)" : patch.Author;
            DetailType.Text = string.IsNullOrEmpty(patch.Type) ? "(not specified)" : patch.Type;
            DetailTags.Text = string.IsNullOrEmpty(patch.Tags) ? "(none)" : patch.Tags;
            DetailDirectory.Text = patch.DirectoryPath;
            DetailDescription.Text = string.IsNullOrEmpty(patch.Description)
                ? "(no description available)"
                : patch.Description;

            // Show dependency warnings
            if (patch.HasUnmetDependencies)
            {
                DependencyWarningBorder.IsVisible = true;
                DependencyWarningText.Text = patch.DependencyWarning;
                ForceInstallButton.IsVisible = true;
            }
            else
            {
                DependencyWarningBorder.IsVisible = false;
                DependencyWarningText.Text = "";
                ForceInstallButton.IsVisible = false;
            }

            UpdateActionButtons();
            StatusMessageLabel.Text = "";
        }

        void UpdateActionButtons()
        {
            bool canInstall = _vm.CanInstall;
            bool hasUnmetDeps = _vm.SelectedPatch?.HasUnmetDependencies == true;

            // Disable normal Install if deps are unmet, but allow ForceInstall
            InstallButton.IsEnabled = canInstall && !hasUnmetDeps;
            ForceInstallButton.IsEnabled = canInstall && hasUnmetDeps;
            ForceInstallButton.IsVisible = hasUnmetDeps;
            UninstallButton.IsEnabled = _vm.CanUninstall;
        }

        void OnInstallClick(object? sender, RoutedEventArgs e)
        {
            DoInstall(forceIgnoreDependencies: false);
        }

        void OnForceInstallClick(object? sender, RoutedEventArgs e)
        {
            DoInstall(forceIgnoreDependencies: true);
        }

        void DoInstall(bool forceIgnoreDependencies)
        {
            string msg = _vm.InstallPatch(forceIgnoreDependencies);
            StatusMessageLabel.Text = msg;

            // Refresh the detail display
            if (_vm.SelectedPatch != null)
            {
                DetailStatus.Text = _vm.SelectedPatch.StatusText;
                UpdateActionButtons();
            }
            UpdateSummary();

            // Refresh the list to show updated status
            PatchListBox.ItemsSource = null;
            PatchListBox.ItemsSource = _vm.FilteredPatches;
        }

        async void OnUninstallClick(object? sender, RoutedEventArgs e)
        {
            // Fast path: a per-patch backup written by this/Avalonia session's install.
            if (!_vm.SelectedPatchNeedsCleanRom)
            {
                StatusMessageLabel.Text = _vm.UninstallPatch();
                return;
            }

            // #1462: no backup file (patch installed in a prior/WinForms session or already
            // present in the loaded ROM) — open the clean-ROM-diff dialog to obtain a
            // patch-free ROM, then diff-restore the patched regions.
            try
            {
                var dialog = await WindowManager.Instance.OpenModal<PatchFormUninstallDialogView>(
                    TopLevel.GetTopLevel(this) as Window,
                    d => d.SeedPatchName(_vm.SelectedPatchName));

                if (!dialog.UserConfirmed)
                {
                    StatusMessageLabel.Text = "Uninstall cancelled.";
                    return;
                }
                if (string.IsNullOrEmpty(dialog.OriginalFilename))
                {
                    StatusMessageLabel.Text = "Uninstall failed: no clean ROM selected.";
                    return;
                }

                StatusMessageLabel.Text = _vm.UninstallPatchWithCleanRom(dialog.OriginalFilename);
            }
            catch (Exception ex)
            {
                Log.Error("PatchManagerView", ex.ToString());
                StatusMessageLabel.Text = "Uninstall failed: " + ex.Message;
            }
        }

        public void NavigateTo(uint address) { }
        public void SelectFirstItem()
        {
            if (PatchListBox.ItemCount > 0)
                PatchListBox.SelectedIndex = 0;
        }

        /// <summary>
        /// #1817: in-app patch2 Initialize (clone) / Update (fetch+reset), the Avalonia half of #1812.
        /// Runs <see cref="Patch2GitService.InitializeOrUpdate"/> off the UI thread; the button is
        /// disabled synchronously on click and re-enabled in a finally so a mid-run exception can't leave
        /// it stuck. git progress lines are throttled to ~150 ms to avoid saturating the UI thread
        /// (a single clone emits hundreds of progress lines).
        /// </summary>
        async void OnInitUpdatePatch2Click(object? sender, RoutedEventArgs e)
        {
            InitUpdatePatch2Button.IsEnabled = false;   // synchronous re-entrancy guard
            string baseDir = CoreState.BaseDirectory ?? AppDomain.CurrentDomain.BaseDirectory;

            long lastPost = 0;
            Action<string> progress = line =>
            {
                if (string.IsNullOrEmpty(line)) return;
                long now = Environment.TickCount64;
                if (now - Interlocked.Read(ref lastPost) < 150) return;   // throttle UI posts
                Interlocked.Exchange(ref lastPost, now);
                Dispatcher.UIThread.Post(() => StatusMessageLabel.Text = "Git: " + line);
            };

            try
            {
                StatusMessageLabel.Text = "Working…";
                var result = await Task.Run(() => Patch2GitService.InitializeOrUpdate(baseDir, progress));
                switch (result.Kind)
                {
                    case Patch2GitResultKind.GitNotFound:
                        StatusMessageLabel.Text = "Git was not found. Install Git and try again, or set up config/patch2 manually — see the Patch Database Setup wiki page.";
                        break;
                    case Patch2GitResultKind.AlreadyRunning:
                        StatusMessageLabel.Text = "A patch database operation is already running.";
                        break;
                    case Patch2GitResultKind.Failed:
                        StatusMessageLabel.Text = string.Format("Patch database {0} failed (git exit {1}). {2}",
                            result.WasClone ? "initialize" : "update", result.ExitCode, LastLogLine(result.Log));
                        break;
                    case Patch2GitResultKind.Success:
                        LoadPatches();   // re-scan config/patch2 from disk so new patches appear immediately
                        StatusMessageLabel.Text = "Patch database updated — list refreshed. Restart recommended for all changes to take full effect.";
                        break;
                }
            }
            catch (Exception ex)
            {
                Log.Error("PatchManagerView", ex.ToString());
                StatusMessageLabel.Text = "Patch database operation failed: " + ex.Message;
            }
            finally
            {
                InitUpdatePatch2Button.Content = _vm.Patch2ButtonText;
                InitUpdatePatch2Button.IsEnabled = true;
            }
        }

        static string LastLogLine(string log)
        {
            if (string.IsNullOrEmpty(log)) return "";
            var lines = log.Replace("\r", "").Split('\n');
            for (int i = lines.Length - 1; i >= 0; i--)
                if (!string.IsNullOrWhiteSpace(lines[i]))
                    return lines[i].Trim();
            return "";
        }

        /// <summary>
        /// #428: filter the patch list by <paramref name="patchNameFilter"/>
        /// and select the entry at <paramref name="subIndex"/> in the result.
        /// Mirrors WF <c>PatchForm.JumpTo("FILTERNAME", subIndex)</c>. When
        /// the filtered list is empty, the search box is still seeded so the
        /// user can clear it and see why nothing matched.
        /// </summary>
        public void JumpTo(string patchNameFilter, int subIndex = 0)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(patchNameFilter)) return;
                if (!IsLoaded) LoadPatches();
                SearchBox.Text = patchNameFilter;
                _vm.FilterText = patchNameFilter;
                PatchListBox.ItemsSource = null;
                PatchListBox.ItemsSource = _vm.FilteredPatches;
                UpdateSummary();
                if (_vm.FilteredPatches.Count > subIndex && subIndex >= 0)
                {
                    PatchListBox.SelectedIndex = subIndex;
                }
                else if (_vm.FilteredPatches.Count > 0)
                {
                    PatchListBox.SelectedIndex = 0;
                }
            }
            catch (Exception ex)
            {
                Log.ErrorF("PatchManagerView.JumpTo failed: {0}", ex.Message);
            }
        }
    }
}
