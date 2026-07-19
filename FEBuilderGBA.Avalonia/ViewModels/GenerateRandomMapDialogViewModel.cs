#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows.Input;
using global::Avalonia.Threading;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public sealed class GenerateRandomMapDialogResult
    {
        public ushort[] Mars { get; init; } = Array.Empty<ushort>();
        public int Width { get; init; }
        public int Height { get; init; }
        public int EffectiveSeed { get; init; }
    }

    public sealed class GenerateRandomMapTilesetOption
    {
        public string Name { get; init; } = "";
        public string Diagnostic { get; init; } = "";
        public override string ToString() => Name;
    }

    public sealed class GenerateRandomMapDialogViewModel : ViewModelBase
    {
        internal const string DefaultAlgorithm = RandomMapGeneratorAlgorithms.Default;

        readonly ProcessRunnerDelegate _runner;
        readonly Func<string, string?, ProcessRunnerDelegate, FEMapCreatorTilesetDiscoveryResult> _discoverTilesets;
        readonly Func<RandomMapGenerationRequest, ProcessRunnerDelegate, RandomMapGenerationResult> _generateRandomMap;
        readonly Func<int> _generateSeed;
        readonly AsyncRelayCommand _browseFEMapCreatorCommand;
        readonly AsyncRelayCommand _browseAssetsDirCommand;
        readonly AsyncRelayCommand _discoverTilesetsCommand;
        readonly AsyncRelayCommand _generateCommand;
        readonly RelayCommand _cancelCommand;

        Func<Task<string?>>? _browseFEMapCreatorAsync;
        Func<Task<string?>>? _browseAssetsDirAsync;
        string _feMapCreatorPath = "";
        string _assetsDir = "";
        string _algorithm = DefaultAlgorithm;
        string _seedText = "";
        string _errorMessage = "";
        bool _isBusy;
        int _width;
        int _height;
        GenerateRandomMapTilesetOption? _selectedTileset;
        GenerateRandomMapDialogResult? _result;

        public event EventHandler? CloseRequested;

        public GenerateRandomMapDialogViewModel(
            ProcessRunnerDelegate? runner = null,
            Func<string, string?, ProcessRunnerDelegate, FEMapCreatorTilesetDiscoveryResult>? discoverTilesets = null,
            Func<RandomMapGenerationRequest, ProcessRunnerDelegate, RandomMapGenerationResult>? generateRandomMap = null,
            Func<int>? generateSeed = null)
        {
            _runner = runner ?? ProcessRunnerCore.Run;
            _discoverTilesets = discoverTilesets ?? FEMapCreatorTilesetDiscoveryCore.DiscoverTilesets;
            _generateRandomMap = generateRandomMap ?? RandomMapGeneratorCore.Generate;
            _generateSeed = generateSeed ?? (() => Random.Shared.Next());

            Tilesets = new ObservableCollection<GenerateRandomMapTilesetOption>();

            _browseFEMapCreatorCommand = new AsyncRelayCommand(BrowseFEMapCreatorAsync, () => !IsBusy);
            _browseAssetsDirCommand = new AsyncRelayCommand(BrowseAssetsDirAsync, () => !IsBusy);
            _discoverTilesetsCommand = new AsyncRelayCommand(DiscoverTilesetsAsync, () => !IsBusy);
            _generateCommand = new AsyncRelayCommand(GenerateAsync, CanGenerate);
            _cancelCommand = new RelayCommand(Cancel, () => !IsBusy);
        }

        public ObservableCollection<GenerateRandomMapTilesetOption> Tilesets { get; }
        public IReadOnlyList<string> Algorithms => RandomMapGeneratorAlgorithms.All;
        public GenerateRandomMapDialogResult? Result => _result;

        public int Width
        {
            get => _width;
            private set
            {
                if (SetField(ref _width, value))
                    OnPropertyChanged(nameof(MapSizeText));
            }
        }

        public int Height
        {
            get => _height;
            private set
            {
                if (SetField(ref _height, value))
                    OnPropertyChanged(nameof(MapSizeText));
            }
        }

        public string FEMapCreatorPath
        {
            get => _feMapCreatorPath;
            set
            {
                string normalized = value?.Trim() ?? "";
                if (!SetField(ref _feMapCreatorPath, normalized))
                    return;
                ResetDiscoveredTilesets();
                ClearErrorMessage();
                RaiseCommandStates();
            }
        }

        public string AssetsDir
        {
            get => _assetsDir;
            set
            {
                string normalized = value?.Trim() ?? "";
                if (!SetField(ref _assetsDir, normalized))
                    return;
                ResetDiscoveredTilesets();
                ClearErrorMessage();
                RaiseCommandStates();
            }
        }

        public string Algorithm
        {
            get => _algorithm;
            set
            {
                string normalized = value ?? "";
                if (!SetField(ref _algorithm, normalized))
                    return;
                ClearErrorMessage();
                RaiseCommandStates();
            }
        }

        public string SeedText
        {
            get => _seedText;
            set
            {
                string normalized = value?.Trim() ?? "";
                if (!SetField(ref _seedText, normalized))
                    return;
                UpdateSeedValidationMessage();
                RaiseCommandStates();
            }
        }

        public GenerateRandomMapTilesetOption? SelectedTileset
        {
            get => _selectedTileset;
            set
            {
                if (!SetField(ref _selectedTileset, value))
                    return;
                ClearErrorMessage();
                RaiseCommandStates();
            }
        }

        public bool IsBusy
        {
            get => _isBusy;
            private set
            {
                if (!SetField(ref _isBusy, value))
                    return;
                OnPropertyChanged(nameof(AreInputsEnabled));
                OnPropertyChanged(nameof(HasTilesets));
                RaiseCommandStates();
            }
        }

        public string ErrorMessage
        {
            get => _errorMessage;
            private set
            {
                if (SetField(ref _errorMessage, value ?? ""))
                    OnPropertyChanged(nameof(HasError));
            }
        }

        public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);
        public bool AreInputsEnabled => !IsBusy;
        public bool HasTilesets => !IsBusy && Tilesets.Count > 0;
        public string MapSizeText => string.Format(R._("Current map size: {0}x{1} tiles"), Width, Height);

        public string TitleText => R._("Generate Random Map");
        public string FEMapCreatorPathLabel => R._("FEMapCreator program");
        public string FEMapCreatorPathWatermark => R._("Absolute path to a FEMapCreator program or managed DLL");
        public string AssetsDirLabel => R._("Assets directory (optional)");
        public string AssetsDirWatermark => R._("Absolute path to the FEMapCreator assets directory");
        public string TilesetLabel => R._("Tileset");
        public string AlgorithmLabel => R._("Algorithm");
        public string SeedLabel => R._("Seed (optional)");
        public string BrowseProgramButtonText => R._("Browse Program…");
        public string BrowseFolderButtonText => R._("Browse Folder…");
        public string DiscoverTilesetsButtonText => R._("Discover Tilesets");
        public string GenerateButtonText => R._("Generate");
        public string CancelButtonText => R._("Cancel");
        public string WarningText => R._("The program you select above will run on this computer with your user account's privileges. Only point this at a FEMapCreator installation you trust.");

        public ICommand BrowseFEMapCreatorCommand => _browseFEMapCreatorCommand;
        public ICommand BrowseAssetsDirCommand => _browseAssetsDirCommand;
        public ICommand DiscoverTilesetsCommand => _discoverTilesetsCommand;
        public ICommand GenerateCommand => _generateCommand;
        public ICommand CancelCommand => _cancelCommand;

        public void Initialize(int width, int height)
        {
            Width = width;
            Height = height;
            _result = null;
            ErrorMessage = "";
            RaiseCommandStates();
        }

        internal void SetBrowseHandlers(
            Func<Task<string?>>? browseFEMapCreatorAsync,
            Func<Task<string?>>? browseAssetsDirAsync)
        {
            _browseFEMapCreatorAsync = browseFEMapCreatorAsync;
            _browseAssetsDirAsync = browseAssetsDirAsync;
        }

        internal async Task BrowseFEMapCreatorAsync()
        {
            if (IsBusy || _browseFEMapCreatorAsync == null)
                return;

            string? path = await _browseFEMapCreatorAsync();
            if (!string.IsNullOrWhiteSpace(path))
                FEMapCreatorPath = path;
        }

        internal async Task BrowseAssetsDirAsync()
        {
            if (IsBusy || _browseAssetsDirAsync == null)
                return;

            string? path = await _browseAssetsDirAsync();
            if (!string.IsNullOrWhiteSpace(path))
                AssetsDir = path;
        }

        internal async Task DiscoverTilesetsAsync()
        {
            if (IsBusy)
                return;

            if (string.IsNullOrWhiteSpace(FEMapCreatorPath))
            {
                ErrorMessage = R._("Choose a FEMapCreator program first.");
                return;
            }

            ClearErrorMessage();
            IsBusy = true;
            try
            {
                string feMapCreatorPath = FEMapCreatorPath;
                string assetsDir = string.IsNullOrWhiteSpace(AssetsDir) ? null : AssetsDir;

                FEMapCreatorTilesetDiscoveryResult result = await Task.Run(
                    () => _discoverTilesets(feMapCreatorPath, assetsDir, _runner));

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    ResetDiscoveredTilesets();
                    if (result == null)
                    {
                        ErrorMessage = R._("Tileset discovery returned no result.");
                        return;
                    }

                    if (!result.Success)
                    {
                        ErrorMessage = result.ErrorMessage ?? R._("Tileset discovery failed.");
                        return;
                    }

                    foreach (FEMapCreatorTilesetInfo tileset in result.UsableTilesets)
                    {
                        Tilesets.Add(new GenerateRandomMapTilesetOption
                        {
                            Name = tileset.Name,
                            Diagnostic = tileset.Diagnostic ?? "",
                        });
                    }

                    if (Tilesets.Count == 0)
                    {
                        ErrorMessage = R._("FEMapCreator reported no compatible tilesets.");
                        return;
                    }

                    SelectedTileset = Tilesets[0];
                    RaiseCommandStates();
                });
            }
            finally
            {
                await Dispatcher.UIThread.InvokeAsync(() => IsBusy = false);
            }
        }

        internal async Task GenerateAsync()
        {
            if (IsBusy)
                return;

            if (string.IsNullOrWhiteSpace(FEMapCreatorPath))
            {
                ErrorMessage = R._("Choose a FEMapCreator program first.");
                return;
            }
            if (SelectedTileset == null)
            {
                ErrorMessage = R._("Discover and choose a compatible tileset first.");
                return;
            }
            if (!RandomMapGeneratorAlgorithms.TryNormalize(
                Algorithm, out string normalizedAlgorithm))
            {
                ErrorMessage = string.Format(
                    R._("Algorithm must be one of: {0}."),
                    string.Join(", ", RandomMapGeneratorAlgorithms.All));
                return;
            }
            if (!TryGetSeed(out int effectiveSeed, out string seedError))
            {
                ErrorMessage = seedError;
                return;
            }

            ClearErrorMessage();
            IsBusy = true;
            try
            {
                var request = new RandomMapGenerationRequest
                {
                    Width = Width,
                    Height = Height,
                    TilesetName = SelectedTileset.Name,
                    Algorithm = normalizedAlgorithm,
                    Seed = effectiveSeed,
                    FEMapCreatorPath = FEMapCreatorPath,
                    AssetsDir = AssetsDir,
                };

                RandomMapGenerationResult result = await Task.Run(
                    () => _generateRandomMap(request, _runner));

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (result == null)
                    {
                        ErrorMessage = R._("Random map generation returned no result.");
                        return;
                    }

                    if (!result.Success)
                    {
                        ErrorMessage = result.ErrorMessage ?? R._("Random map generation failed.");
                        return;
                    }

                    _result = new GenerateRandomMapDialogResult
                    {
                        Mars = result.Mars ?? Array.Empty<ushort>(),
                        Width = Width,
                        Height = Height,
                        EffectiveSeed = effectiveSeed,
                    };
                    RequestClose();
                });
            }
            finally
            {
                await Dispatcher.UIThread.InvokeAsync(() => IsBusy = false);
            }
        }

        void Cancel()
        {
            if (IsBusy)
                return;
            _result = null;
            RequestClose();
        }

        bool CanGenerate()
        {
            return !IsBusy
                && !string.IsNullOrWhiteSpace(FEMapCreatorPath)
                && RandomMapGeneratorAlgorithms.TryNormalize(Algorithm, out _)
                && SelectedTileset != null
                && IsSeedValid();
        }

        bool TryGetSeed(out int seed, out string error)
        {
            error = "";
            if (string.IsNullOrWhiteSpace(SeedText))
            {
                seed = _generateSeed();
                return true;
            }

            if (int.TryParse(SeedText, out seed))
                return true;

            error = R._("Seed must be a valid 32-bit integer.");
            return false;
        }

        bool IsSeedValid()
            => string.IsNullOrWhiteSpace(SeedText) || int.TryParse(SeedText, out _);

        void UpdateSeedValidationMessage()
        {
            if (string.IsNullOrWhiteSpace(SeedText))
            {
                ClearErrorMessage();
                return;
            }

            if (!int.TryParse(SeedText, out _))
            {
                ErrorMessage = R._("Seed must be a valid 32-bit integer.");
                return;
            }

            ClearErrorMessage();
        }

        void ResetDiscoveredTilesets()
        {
            if (Tilesets.Count > 0)
                Tilesets.Clear();
            SelectedTileset = null;
            OnPropertyChanged(nameof(HasTilesets));
        }

        void ClearErrorMessage()
        {
            if (!IsBusy)
                ErrorMessage = "";
        }

        void RaiseCommandStates()
        {
            _browseFEMapCreatorCommand.RaiseCanExecuteChanged();
            _browseAssetsDirCommand.RaiseCanExecuteChanged();
            _discoverTilesetsCommand.RaiseCanExecuteChanged();
            _generateCommand.RaiseCanExecuteChanged();
            _cancelCommand.RaiseCanExecuteChanged();
        }

        void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);

        sealed class RelayCommand : ICommand
        {
            readonly Action _execute;
            readonly Func<bool>? _canExecute;

            internal RelayCommand(Action execute, Func<bool>? canExecute = null)
            {
                _execute = execute;
                _canExecute = canExecute;
            }

            public event EventHandler? CanExecuteChanged;

            public bool CanExecute(object? parameter) => _canExecute?.Invoke() ?? true;

            public void Execute(object? parameter) => _execute();

            internal void RaiseCanExecuteChanged()
                => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }

        sealed class AsyncRelayCommand : ICommand
        {
            readonly Func<Task> _executeAsync;
            readonly Func<bool>? _canExecute;

            internal AsyncRelayCommand(Func<Task> executeAsync, Func<bool>? canExecute = null)
            {
                _executeAsync = executeAsync;
                _canExecute = canExecute;
            }

            public event EventHandler? CanExecuteChanged;

            public bool CanExecute(object? parameter) => _canExecute?.Invoke() ?? true;

            public async void Execute(object? parameter) => await _executeAsync();

            internal void RaiseCanExecuteChanged()
                => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
