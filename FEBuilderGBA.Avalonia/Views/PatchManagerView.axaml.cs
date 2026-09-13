using global::Avalonia;
using System;
using System.Threading;
using System.Threading.Tasks;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using global::Avalonia.Threading;
using global::Avalonia.VisualTree;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;
using FEBuilderGBA.Avalonia.Dialogs;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class PatchManagerView : TranslatedUserControl, IEmbeddableEditor
    {
        readonly PatchManagerViewModel _vm = new();
        readonly PatchManagerRefreshService _refresh = new();
        bool _attached;
        long _attachment;
        int _pendingSelection = -1;
        internal Task<bool> RefreshTask { get; private set; } = Task.FromResult(false);
        bool _importing;
        bool _gitRunning;
        bool _uninstalling;
        bool _patchActionRunning;
        bool _actionDialogOpen;
        long _actionGeneration;
        CancellationTokenSource? _actionCancellation;
        internal Task ActionTask { get; private set; } = Task.CompletedTask;
        bool _importDialogOpen;
        CancellationTokenSource? _importCancellation;
        readonly DispatcherTimer _operationTimer = new() { Interval = TimeSpan.FromMilliseconds(150) };

        public string ViewTitle => "Patch Manager";
        public new bool IsLoaded => _vm.IsLoaded;
        public EditorDescriptor Descriptor => new("Patch Manager", 1100, 650, SizeToContent: global::Avalonia.Controls.SizeToContent.WidthAndHeight);
        public event EventHandler? CloseRequested;
        public void RequestClose()
        {
            _attached = false;
            _attachment++;
            _refresh.Invalidate();
            _importCancellation?.Cancel();
            _actionGeneration++;
            _actionCancellation?.Cancel();
            CloseRequested?.Invoke(this, EventArgs.Empty);
        }

        public PatchManagerView()
        {
            InitializeComponent();
            PatchListBox.SelectionChanged += OnPatchSelected;
            SearchBox.TextChanged += OnSearchTextChanged;
            InstallButton.Click += OnInstallClick;
            ForceInstallButton.Click += OnForceInstallClick;
            UninstallButton.Click += OnUninstallClick;
            InitUpdatePatch2Button.Click += OnInitUpdatePatch2Click;
            ImportPatchDatabaseButton.Click += OnImportPatchDatabaseClick;
            CancelPatchDatabaseImportButton.Click += (_, _) => _importCancellation?.Cancel();
            _operationTimer.Tick += (_, _) => UpdateOperationControls();
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            _attached = true;
            _attachment++;
            _operationTimer.Start();
            if (!_importing && !_patchActionRunning) RefreshTask = LoadPatchesAsync();
            UpdateOperationControls();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            // An Android modal temporarily detaches the underlying editor without closing it.
            _attached = false;
            _attachment++;
            _refresh.Invalidate();
            if (!_importDialogOpen) _importCancellation?.Cancel();
            if (!_actionDialogOpen)
            {
                _actionGeneration++;
                _actionCancellation?.Cancel();
            }
            _operationTimer.Stop();
            base.OnDetachedFromVisualTree(e);
        }

        async Task<bool> LoadPatchesAsync(PatchDatabaseImportCore.PreparedImport? owner = null)
        {
            if (!_attached) return false;
            long attachment = _attachment;
            string filter = SearchBox.Text ?? "";
            int selection = _pendingSelection;
            _vm.SetPendingFilter(filter);
            ClearDetails();
            UpdateOperationControls();
            try
            {
                StatusMessageLabel.Text = string.IsNullOrEmpty(App.PatchDatabaseRecoveryNotice)
                    ? R._("Working…") : App.PatchDatabaseRecoveryNotice;
                if (!_attached) return false;
                if (!PatchDatabaseImportService.CanImportLoadedRom)
                {
                    StatusMessageLabel.Text = PatchDatabaseImportService.AvailabilityMessage;
                    _vm.IsLoaded = true;
                    return false;
                }
                PatchManagerRefreshService.Request Capture(long generation) =>
                    PatchManagerRefreshService.Capture(filter, selection, generation, owner != null);
                bool Current(PatchManagerRefreshService.Request request) =>
                    _attached && attachment == _attachment && request.Identity.IsCurrent &&
                    string.Equals(SearchBox.Text ?? "", request.Filter, StringComparison.Ordinal);
                void Publish(PatchManagerRefreshService.PatchListSnapshot snapshot)
                {
                    _vm.Publish(snapshot);
                    PatchListBox.ItemsSource = _vm.FilteredPatches;
                    UpdateSummary();
                    InitUpdatePatch2Button.Content = snapshot.GitButton;
                    ClearDetails();
                    if (snapshot.Request.Selection >= 0 && snapshot.Filtered.Count > 0)
                        PatchListBox.SelectedIndex = Math.Min(snapshot.Request.Selection, snapshot.Filtered.Count - 1);
                    StatusMessageLabel.Text = string.IsNullOrEmpty(App.PatchDatabaseRecoveryNotice)
                        ? R._(snapshot.Message) : App.PatchDatabaseRecoveryNotice;
                }
                Task<bool> operation = owner == null
                    ? _refresh.RefreshAsync(Capture, Current, Publish)
                    : _refresh.RefreshCommittedAsync(owner, Capture, Current, Publish, _importCancellation?.Token ?? default);
                UpdateOperationControls();
                bool refreshed = await operation;
                if (!refreshed && _attached && attachment == _attachment &&
                    string.Equals(filter, SearchBox.Text ?? "", StringComparison.Ordinal))
                {
                    _vm.SetPendingFilter(filter);
                    StatusMessageLabel.Text = WithRecoveryNotice(_refresh.Failure?.Localize() ??
                        R._("The patch database could not be refreshed: {0}", "Reopen Patch Manager."));
                }
                return refreshed;
            }
            catch (Exception ex)
            {
                Log.ErrorF("PatchManagerView.LoadPatches failed: {0}", ex.Message);
                if (_attached && attachment == _attachment)
                {
                    _vm.SetPendingFilter(filter);
                    StatusMessageLabel.Text = WithRecoveryNotice(
                        R._("The patch database could not be refreshed: {0}", ex.Message));
                }
                return false;
            }
            finally { if (_attached && attachment == _attachment) UpdateOperationControls(); }
        }

        static string WithRecoveryNotice(string message)
        {
            string notice = App.PatchDatabaseRecoveryNotice;
            return notice.Length == 0 || message.Contains(notice, StringComparison.Ordinal)
                ? message : notice + "\n" + message;
        }

        void ClearDetails()
        {
            foreach (var label in new[] { DetailName, DetailStatus, DetailAuthor, DetailType, DetailTags,
                DetailDirectory, DetailDescription, DependencyWarningText })
                label.Text = "";
            DependencyWarningBorder.IsVisible = false;
            UpdateActionButtons();
        }

        void UpdateOperationControls()
        {
            ImportPatchDatabaseButton.IsEnabled = !PatchActionsBlocked && _vm.CanImportPatchDatabase;
            InitUpdatePatch2Button.IsEnabled = !PatchActionsBlocked;
            CancelPatchDatabaseImportButton.IsVisible = _importing;
            CancelPatchDatabaseImportButton.IsEnabled = _importing && _importCancellation?.IsCancellationRequested != true;
            UpdateActionButtons();
        }

        void OnSearchTextChanged(object? sender, TextChangedEventArgs e)
        {
            _vm.SetPendingFilter(SearchBox.Text ?? "");
            if (_patchActionRunning)
            {
                _actionGeneration++;
                _actionCancellation?.Cancel();
                _refresh.Invalidate();
                return;
            }
            if (_attached && !_importing) RefreshTask = LoadPatchesAsync();
            else _refresh.Invalidate();
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
            StatusMessageLabel.Text = patch.ActionRestrictionMessage;
        }

        void UpdateActionButtons()
        {
            bool canInstall = !PatchActionsBlocked && _vm.CanInstall;
            bool hasUnmetDeps = _vm.SelectedPatch?.HasUnmetDependencies == true;

            // Disable normal Install if deps are unmet, but allow ForceInstall
            InstallButton.IsEnabled = canInstall && !hasUnmetDeps;
            ForceInstallButton.IsEnabled = canInstall && hasUnmetDeps;
            ForceInstallButton.IsVisible = canInstall && hasUnmetDeps;
            UninstallButton.IsEnabled = !PatchActionsBlocked && _vm.CanUninstall;
        }

        bool PatchActionsBlocked => _importing || _gitRunning || _uninstalling || _patchActionRunning ||
            _refresh.IsBusy || _vm.IsPatchDatabaseOperationRunning;

        bool RefuseBusyPatchAction()
        {
            if (!PatchActionsBlocked) return false;
            StatusMessageLabel.Text = PatchManagerViewModel.PatchDatabaseBusyMessage;
            UpdateOperationControls();
            return true;
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
            if (RefuseBusyPatchAction()) return;
            ActionTask = ObserveActionAsync(RunViewActionAsync((token, current) =>
                _vm.InstallPatchAsync(forceIgnoreDependencies, token, current)));
        }

        void OnUninstallClick(object? sender, RoutedEventArgs e)
        {
            if (RefuseBusyPatchAction()) return;
            _uninstalling = true;
            ActionTask = ObserveActionAsync(RunViewActionAsync((token, current) => _vm.UninstallPatchAsync(async () =>
            {
                _actionDialogOpen = true;
                try
                {
                    var dialog = await WindowManager.Instance.OpenModal<PatchFormUninstallDialogView>(
                        TopLevel.GetTopLevel(this) as Window, d => d.SeedPatchName(_vm.SelectedPatchName));
                    return dialog.UserConfirmed ? dialog.OriginalFilename : null;
                }
                finally { _actionDialogOpen = false; }
            }, token, current)));
        }

        static async Task ObserveActionAsync(Task operation)
        {
            try { await operation; }
            catch (Exception ex) { Log.Error("PatchManagerView", ex.ToString()); }
        }

        async Task RunViewActionAsync(Func<CancellationToken, Func<bool>, Task<string>> action)
        {
            _patchActionRunning = true;
            var cancellation = new CancellationTokenSource();
            _actionCancellation = cancellation;
            long generation = _actionGeneration;
            var root = TopLevel.GetTopLevel(this);
            bool Current() => _attached && generation == _actionGeneration &&
                ReferenceEquals(root, TopLevel.GetTopLevel(this));
            string? message = null;
            bool showResult = false;
            try
            {
                UpdateOperationControls();
                message = await action(cancellation.Token, Current);
                showResult = Current();
                if (showResult) StatusMessageLabel.Text = WithRecoveryNotice(message);
            }
            catch (Exception ex)
            {
                Log.Error("PatchManagerView", ex.ToString());
                if (Current())
                    StatusMessageLabel.Text = WithRecoveryNotice(R._(PatchManagerRefreshService.RefreshFailureTemplate, ex.Message));
            }
            finally
            {
                _uninstalling = false;
                _patchActionRunning = false;
                _actionCancellation = null;
                cancellation.Dispose();
            }
            if (_attached)
            {
                try
                {
                    var refresh = LoadPatchesAsync();
                    RefreshTask = refresh;
                    if (await refresh && showResult && Current() && ReferenceEquals(RefreshTask, refresh))
                        StatusMessageLabel.Text = WithRecoveryNotice(message ?? "");
                    if (_attached) UpdateOperationControls();
                }
                catch (Exception ex) { Log.Error("PatchManagerView", ex.ToString()); }
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
            if (RefuseBusyPatchAction()) return;
            _gitRunning = true;
            UpdateOperationControls();
            InitUpdatePatch2Button.IsEnabled = false;   // synchronous re-entrancy guard
            string baseDir = CoreState.BaseDirectory ?? AppDomain.CurrentDomain.BaseDirectory;
            long attachment = _attachment;

            long lastPost = 0;
            Action<string> progress = line =>
            {
                if (string.IsNullOrEmpty(line)) return;
                long now = Environment.TickCount64;
                if (now - Interlocked.Read(ref lastPost) < 150) return;   // throttle UI posts
                Interlocked.Exchange(ref lastPost, now);
                Dispatcher.UIThread.Post(() =>
                {
                    if (_attached && attachment == _attachment) StatusMessageLabel.Text = "Git: " + line;
                });
            };

            try
            {
                StatusMessageLabel.Text = "Working…";
                var result = await Task.Run(() => Patch2GitService.InitializeOrUpdate(baseDir, progress));
                if (!_attached || attachment != _attachment) return;
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
                        RefreshTask = LoadPatchesAsync();
                        bool refreshed = await RefreshTask;
                        if (_attached)
                        {
                            string message = refreshed
                                ? "Patch database updated — list refreshed. Restart recommended for all changes to take full effect."
                                : "Patch database updated, but the list was not refreshed. Reopen Patch Manager.";
                            if (!refreshed && _refresh.Failure != null) message += "\n" + _refresh.Failure.Localize();
                            StatusMessageLabel.Text = WithRecoveryNotice(message);
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                Log.Error("PatchManagerView", ex.ToString());
                if (_attached && attachment == _attachment)
                    StatusMessageLabel.Text = "Patch database operation failed: " + ex.Message;
            }
            finally
            {
                _gitRunning = false;
                if (_attached) UpdateOperationControls();
            }
        }

        async void OnImportPatchDatabaseClick(object? sender, RoutedEventArgs e)
        {
            if (RefuseBusyPatchAction()) return;
            var identity = PatchDatabaseImportService.CaptureLoadedRom();
            if (identity == null)
            {
                StatusMessageLabel.Text = PatchDatabaseImportService.AvailabilityMessage;
                return;
            }
            _importing = true;
            using var cancellation = new CancellationTokenSource();
            bool committed = false;
            _importCancellation = cancellation;
            UpdateOperationControls();
            try
            {
                _importDialogOpen = true;
                global::Avalonia.Platform.Storage.IStorageFile? selected;
                try { selected = await FileDialogHelper.OpenPatchDatabaseZipPick(TopLevel.GetTopLevel(this)); }
                finally { _importDialogOpen = false; }
                using (selected)
                {
                    if (selected == null || this.GetVisualRoot() == null)
                    {
                        StatusMessageLabel.Text = R._("Import cancelled. The previous database was not replaced.");
                        return;
                    }
                    StatusMessageLabel.Text = R._("Validating the ZIP and staging the patch database…");
                    var result = await PatchDatabaseImportService.ImportAndRefreshAsync(selected, identity,
                        CoreState.BaseDirectory, ConfirmImport, owner =>
                        {
                            RefreshTask = LoadPatchesAsync(owner);
                            return RefreshTask;
                        }, cancellation.Token);
                    committed = result.Imported;
                    if (!_attached) return;
                    if (result.Imported)
                    {
                        ShowImportedOutcome(result, identity.Version);
                    }
                    else StatusMessageLabel.Text = result.Message;
                }
            }
            catch (Exception ex)
            {
                if (_attached) StatusMessageLabel.Text = committed
                    ? R._("Imported patch database for {0}, but the list was not refreshed. Reopen Patch Manager. No patches were applied.",
                        identity.Version) + "\n" + ex.Message
                    : R._("Patch database import failed: {0}", ex.Message);
            }
            finally
            {
                _importCancellation = null;
                _importing = false;
                if (_attached) UpdateOperationControls();
            }
        }

        internal void ShowImportedOutcome(PatchDatabaseImportService.Outcome result, string version)
        {
            StatusMessageLabel.Text = result.Refreshed
                ? R._("Imported patch database for {0}; list refreshed. No patches were applied. Restart recommended for cached data.", version)
                : R._("Imported patch database for {0}, but the list was not refreshed. Reopen Patch Manager. No patches were applied.", version);
            if (!string.IsNullOrWhiteSpace(result.Message))
                StatusMessageLabel.Text += "\n" + result.Message;
            if (!result.Refreshed && _refresh.Failure != null)
                StatusMessageLabel.Text += "\n" + _refresh.Failure.Localize();
            StatusMessageLabel.Text = WithRecoveryNotice(StatusMessageLabel.Text ?? "");
        }

        async Task<bool> ConfirmImport(PatchDatabaseImportCore.PreparedImport prepared)
        {
            _importDialogOpen = true;
            try
            {
                var result = await WindowManager.Instance.OpenModal<MessageBoxContent, MessageBoxResult>(
                    TopLevel.GetTopLevel(this) as Window, content =>
                    {
                        content.Configure(PatchDatabaseImportService.ConfirmationMessage(prepared),
                            R._("Import Patch Database ZIP"), MessageBoxMode.YesNo);
                        SetDefaultImportConfirmation(content);
                    });
                if (this.GetVisualRoot() == null) _importCancellation?.Cancel();
                return result == MessageBoxResult.Yes;
            }
            finally { _importDialogOpen = false; }
        }

        internal static void SetDefaultImportConfirmation(MessageBoxContent content)
        {
            var no = content.FindControl<Button>("NoButton")!;
            var yes = content.FindControl<Button>("YesButton")!;
            no.IsDefault = true;
            no.IsCancel = true;
            no.TabIndex = 0;
            yes.IsDefault = false;
            yes.TabIndex = 1;
            content.AttachedToVisualTree += (_, _) => Dispatcher.UIThread.Post(() => no.Focus());
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
                _pendingSelection = Math.Max(0, subIndex);
                bool changed = SearchBox.Text != patchNameFilter;
                SearchBox.Text = patchNameFilter;
                if (_patchActionRunning)
                {
                    _actionGeneration++;
                    _actionCancellation?.Cancel();
                    _vm.SetPendingFilter(SearchBox.Text ?? "");
                    _refresh.Invalidate();
                }
                else if (!changed && _attached && !_importing) RefreshTask = LoadPatchesAsync();
            }
            catch (Exception ex)
            {
                Log.ErrorF("PatchManagerView.JumpTo failed: {0}", ex.Message);
            }
        }
    }
}
