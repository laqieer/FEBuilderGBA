using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Avalonia.Threading;
using FEBuilderGBA;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public sealed class ContentRepoSetupWizardViewModel : ViewModelBase
    {
        public delegate Patch2GitResult InitRepoDelegate(string repoDir, string url, Action<string> progress);

        readonly string _baseDir;
        readonly Config _config;
        readonly InitRepoDelegate _initRepo;
        readonly Action<Action> _postToUi;
        bool _isGitAvailable;
        string _manualInstructions = "";

        public ContentRepoSetupWizardViewModel()
            : this(CoreState.BaseDirectory ?? AppDomain.CurrentDomain.BaseDirectory,
                  CoreState.Config ?? Config.LoadOrCreate(System.IO.Path.Combine(CoreState.BaseDirectory ?? AppDomain.CurrentDomain.BaseDirectory, "config", "config.xml")),
                  ContentRepoSetupCore.IsGitAvailable(),
                  ContentRepoGitService.InitializeOrUpdate,
                  action => Dispatcher.UIThread.Post(action))
        {
        }

        internal ContentRepoSetupWizardViewModel(string baseDir, Config config, bool isGitAvailable,
            InitRepoDelegate? initRepo = null, Action<Action>? postToUi = null)
        {
            _baseDir = baseDir;
            _config = config;
            _isGitAvailable = isGitAvailable;
            _initRepo = initRepo ?? ContentRepoGitService.InitializeOrUpdate;
            _postToUi = postToUi ?? (action => action());

            Rows = new ObservableCollection<ContentRepoSetupRowViewModel>();
            foreach (var d in ContentRepoSetupCore.Repos)
            {
                Rows.Add(new ContentRepoSetupRowViewModel(d, ContentRepoSetupCore.ResolveUrl(d, _config),
                    ContentRepoSetupCore.IsRepoReady(d, _baseDir), _isGitAvailable));
            }
            ManualInstructions = BuildManualInstructions();
        }

        public ObservableCollection<ContentRepoSetupRowViewModel> Rows { get; }

        public bool IsGitAvailable
        {
            get => _isGitAvailable;
            private set
            {
                if (SetField(ref _isGitAvailable, value))
                {
                    OnPropertyChanged(nameof(IsManualInstructionsVisible));
                    foreach (var row in Rows) row.SetGitAvailable(value);
                }
            }
        }

        public bool IsManualInstructionsVisible => !IsGitAvailable;

        public string ManualInstructions
        {
            get => _manualInstructions;
            private set => SetField(ref _manualInstructions, value ?? "");
        }

        public string Title => R._("Content Repository Setup");
        public string IntroText => R._("FEBuilderGBA uses separate content repositories for patches and community assets. Configure the remote URL for each repository, then initialize any repository that is not ready.");
        public string ManualHeader => R._("Git was not found. Initialize buttons are hidden; download and extract these repositories manually:");
        public string DontShowAgainText => R._("Don't show this again");
        public string CloseText => R._("Close");
        public string DisplayNameHeader => R._("Repository");
        public string UrlHeader => R._("Remote URL");
        public string StatusHeader => R._("Status");
        public string ActionHeader => R._("Action");

        public void Skip()
        {
            // Deliberately no-op: closing/skipping must not set content_repo_setup_optout.
        }

        public void DontShowAgain()
            => ContentRepoSetupCore.SetOptOut(_config);

        public async Task InitializeAsync(ContentRepoSetupRowViewModel row)
        {
            if (row == null || row.IsBusy || !IsGitAvailable)
                return;

            row.IsBusy = true;
            row.Progress = R._("Starting...");
            row.Status = R._("Running...");
            try
            {
                string url = (row.Url ?? "").Trim();
                row.Url = url;
                _config[row.Descriptor.ConfigKey] = url;
                _config.Save();

                string effectiveUrl = string.IsNullOrWhiteSpace(url) ? row.Descriptor.DefaultUrl : url;
                string repoDir = ContentRepoSetupCore.ResolveDir(row.Descriptor, _baseDir);
                var result = await Task.Run(() => _initRepo(repoDir, effectiveUrl, line =>
                {
                    if (!string.IsNullOrWhiteSpace(line))
                        _postToUi(() => row.Progress = line);
                }));

                ApplyResult(row, result);
            }
            catch (Exception ex)
            {
                Log.Error("ContentRepoSetupWizard", ex.ToString());
                row.Status = R._("Failed");
                row.Progress = ex.Message;
            }
            finally
            {
                row.IsBusy = false;
            }
        }

        void ApplyResult(ContentRepoSetupRowViewModel row, Patch2GitResult result)
        {
            switch (result.Kind)
            {
                case Patch2GitResultKind.Success:
                    row.IsReady = ContentRepoSetupCore.IsRepoReady(row.Descriptor, _baseDir);
                    row.Status = row.IsReady ? R._("Ready") : R._("Needs initialization");
                    row.Progress = result.WasClone ? R._("Initialized successfully.") : R._("Updated successfully.");
                    break;
                case Patch2GitResultKind.GitNotFound:
                    IsGitAvailable = false;
                    ManualInstructions = BuildManualInstructions();
                    row.Status = R._("Git not found");
                    row.Progress = R._("Install Git or use the manual download instructions below.");
                    break;
                case Patch2GitResultKind.AlreadyRunning:
                    row.Status = R._("Already running");
                    row.Progress = R._("Another content repository operation is already running.");
                    break;
                default:
                    row.Status = R._("Failed");
                    row.Progress = string.Format(R._("Git operation failed with exit code {0}."), result.ExitCode);
                    break;
            }
        }

        string BuildManualInstructions()
        {
            var lines = new System.Text.StringBuilder();
            lines.AppendLine(R._("Download each repository ZIP, extract it, and place the extracted contents in the matching folder:"));
            foreach (var d in ContentRepoSetupCore.Repos)
            {
                lines.Append("- ").Append(d.DisplayName).Append(": ")
                    .Append(ContentRepoSetupCore.ResolveUrl(d, _config)).Append(" -> ")
                    .AppendLine(ContentRepoSetupCore.ResolveDir(d, _baseDir));
            }
            return lines.ToString().TrimEnd();
        }
    }

    public sealed class ContentRepoSetupRowViewModel : ViewModelBase
    {
        bool _isReady;
        bool _isBusy;
        bool _gitAvailable;
        string _url;
        string _status;
        string _progress = "";

        internal ContentRepoSetupRowViewModel(ContentRepoDescriptor descriptor, string url, bool isReady, bool gitAvailable)
        {
            Descriptor = descriptor;
            _url = url;
            _isReady = isReady;
            _gitAvailable = gitAvailable;
            _status = isReady ? R._("Ready") : R._("Needs initialization");
        }

        public ContentRepoDescriptor Descriptor { get; }
        public string DisplayName => Descriptor.DisplayName;

        public string Url
        {
            get => _url;
            set => SetField(ref _url, value ?? "");
        }

        public bool IsReady
        {
            get => _isReady;
            set
            {
                if (SetField(ref _isReady, value))
                {
                    OnPropertyChanged(nameof(InitializeButtonText));
                    OnPropertyChanged(nameof(CanInitialize));
                }
            }
        }

        public bool IsBusy
        {
            get => _isBusy;
            set
            {
                if (SetField(ref _isBusy, value))
                    OnPropertyChanged(nameof(CanInitialize));
            }
        }

        public string Status
        {
            get => _status;
            set => SetField(ref _status, value ?? "");
        }

        public string Progress
        {
            get => _progress;
            set => SetField(ref _progress, value ?? "");
        }

        public bool CanInitialize => _gitAvailable && !IsBusy;
        public bool IsInitializeVisible => _gitAvailable;
        public string InitializeButtonText => IsReady ? R._("Update") : R._("Initialize");

        internal void SetGitAvailable(bool value)
        {
            if (_gitAvailable == value) return;
            _gitAvailable = value;
            OnPropertyChanged(nameof(CanInitialize));
            OnPropertyChanged(nameof(IsInitializeVisible));
        }
    }
}
