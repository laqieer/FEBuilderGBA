#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    /// <summary>
    /// Backs the explicit "map this tileset" dialog (#1978 Slice 3): the only place
    /// <see cref="FEMapCreatorTilesetDiscoveryCore.DiscoverTilesets"/> may run, and it only ever
    /// runs on an intentional user click (<see cref="DiscoverTilesetsAsync"/>/<c>Discover</c>),
    /// never automatically on construction. Follows the code-behind event-handler convention
    /// used by <c>OptionsView.axaml.cs</c> rather than introducing new ICommand scaffolding.
    /// </summary>
    internal sealed class MapTilesetMappingDialogViewModel : INotifyPropertyChanged
    {
        readonly Func<string, string?, ProcessRunnerDelegate, FEMapCreatorTilesetDiscoveryResult> _discoverTilesets;
        readonly ProcessRunnerDelegate _runner;
        readonly Func<Config?> _getConfig;

        TilesetFingerprint _fingerprint;
        string _statusMessage = "";
        string _errorMessage = "";
        bool _isBusy;
        FEMapCreatorTilesetOption? _selectedTileset;

        public MapTilesetMappingDialogViewModel(
            Func<string, string?, ProcessRunnerDelegate, FEMapCreatorTilesetDiscoveryResult>? discoverTilesets = null,
            ProcessRunnerDelegate? runner = null,
            Func<Config?>? getConfig = null)
        {
            _discoverTilesets = discoverTilesets ?? FEMapCreatorTilesetDiscoveryCore.DiscoverTilesets;
            _runner = runner ?? ProcessRunnerCore.Run;
            _getConfig = getConfig ?? (() => CoreState.Config);
        }

        public ObservableCollection<FEMapCreatorTilesetOption> Tilesets { get; } = new ObservableCollection<FEMapCreatorTilesetOption>();

        public FEMapCreatorTilesetOption? SelectedTileset
        {
            get => _selectedTileset;
            set { _selectedTileset = value; OnPropertyChanged(); }
        }

        public string StatusMessage
        {
            get => _statusMessage;
            private set { _statusMessage = value; OnPropertyChanged(); }
        }

        public string ErrorMessage
        {
            get => _errorMessage;
            private set { _errorMessage = value; OnPropertyChanged(); }
        }

        public bool IsBusy
        {
            get => _isBusy;
            private set { _isBusy = value; OnPropertyChanged(); }
        }

        /// <summary>True once <see cref="SaveMapping"/> has committed a new mapping entry.</summary>
        public bool Saved { get; private set; }

        /// <summary>
        /// Initialize the dialog for the given, already-computed current-map tileset fingerprint.
        /// Read-only: never mutates config and never launches a process.
        /// </summary>
        public void Initialize(TilesetFingerprint fingerprint)
        {
            _fingerprint = fingerprint;
            Saved = false;
            ErrorMessage = "";
            StatusMessage = fingerprint.IsEmpty
                ? R._("This map's tileset could not be identified.")
                : "";
        }

        /// <summary>
        /// Run FEMapCreator tileset discovery using the currently persisted, already-validated
        /// profile. Only ever invoked by an explicit user click — never by construction/init.
        /// </summary>
        public void DiscoverTilesets()
        {
            if (IsBusy) return;
            ErrorMessage = "";
            Tilesets.Clear();
            SelectedTileset = null;

            Config? cfg = _getConfig();
            string rawExePath = cfg?.at(FEMapCreatorProfileCore.ExecutablePathConfigKey, "") ?? "";
            string rawAssetsRoot = cfg?.at(FEMapCreatorProfileCore.AssetsRootConfigKey, "") ?? "";
            FEMapCreatorSetupSnapshot profile = FEMapCreatorProfileCore.Validate(rawExePath, rawAssetsRoot);
            if (profile.Status != FEMapCreatorSetupStatus.Configured)
            {
                ErrorMessage = string.IsNullOrWhiteSpace(profile.ErrorMessage)
                    ? R._("FEMapCreator is not configured. Set it up in Options first.")
                    : profile.ErrorMessage;
                return;
            }

            IsBusy = true;
            StatusMessage = R._("Discovering tilesets...");
            try
            {
                FEMapCreatorTilesetDiscoveryResult result = _discoverTilesets(
                    profile.ExecutablePath,
                    string.IsNullOrWhiteSpace(profile.AssetsRoot) ? null : profile.AssetsRoot,
                    _runner);

                if (!result.Success)
                {
                    ErrorMessage = string.IsNullOrWhiteSpace(result.ErrorMessage)
                        ? R._("Tileset discovery failed.")
                        : result.ErrorMessage;
                    StatusMessage = "";
                    return;
                }

                List<FEMapCreatorTilesetInfo> usable = result.Tilesets.Where(t => t.IsUsable).ToList();
                foreach (FEMapCreatorTilesetInfo t in usable)
                {
                    Tilesets.Add(new FEMapCreatorTilesetOption
                    {
                        Name = t.Name,
                        Diagnostic = t.Diagnostic,
                        ImagePath = t.ResolvedImagePath,
                        GenerationDataPath = t.ResolvedGenerationDataPath,
                    });
                }

                StatusMessage = usable.Count > 0
                    ? string.Format(R._("Found {0} usable tileset(s)."), usable.Count)
                    : R._("No usable tilesets were found.");
            }
            finally
            {
                IsBusy = false;
            }
        }

        /// <summary>
        /// Persist a mapping from the current fingerprint to <see cref="SelectedTileset"/> using
        /// <see cref="FEMapCreatorTilesetMappingStoreCore.TryCreateEntry"/> +
        /// <see cref="FEMapCreatorTilesetMappingStoreCore.Upsert"/> + <c>SaveAll</c> +
        /// <see cref="Config.Save"/>. Returns false with <see cref="ErrorMessage"/> set on any
        /// validation failure; never throws.
        /// </summary>
        public bool SaveMapping()
        {
            ErrorMessage = "";
            if (_fingerprint.IsEmpty)
            {
                ErrorMessage = R._("This map's tileset could not be identified.");
                return false;
            }
            if (SelectedTileset == null)
            {
                ErrorMessage = R._("Select a tileset first.");
                return false;
            }

            Config? cfg = _getConfig();
            if (cfg == null)
            {
                ErrorMessage = R._("Configuration is not available.");
                return false;
            }

            string rawExePath = cfg.at(FEMapCreatorProfileCore.ExecutablePathConfigKey, "");
            string rawAssetsRoot = cfg.at(FEMapCreatorProfileCore.AssetsRootConfigKey, "");
            FEMapCreatorSetupSnapshot profile = FEMapCreatorProfileCore.Validate(rawExePath, rawAssetsRoot);

            if (!FEMapCreatorTilesetMappingStoreCore.TryCreateEntry(
                _fingerprint,
                SelectedTileset.Name,
                SelectedTileset.ImagePath,
                SelectedTileset.GenerationDataPath,
                profile,
                out FEMapCreatorTilesetMappingEntry entry,
                out string error))
            {
                ErrorMessage = error;
                return false;
            }

            IReadOnlyList<FEMapCreatorTilesetMappingEntry> mappings = FEMapCreatorTilesetMappingStoreCore.LoadAll(cfg);
            mappings = FEMapCreatorTilesetMappingStoreCore.Upsert(mappings, entry);
            FEMapCreatorTilesetMappingStoreCore.SaveAll(cfg, mappings);
            cfg.Save();

            Saved = true;
            StatusMessage = string.Format(R._("Mapping saved for tileset '{0}'."), SelectedTileset.Name);
            return true;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
