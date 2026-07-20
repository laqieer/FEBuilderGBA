#nullable enable

using System;
using global::Avalonia.Controls;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Dialogs
{
    /// <summary>
    /// Explicit per-current-fingerprint "map this tileset" action (#1978 Slice 3). Discovery
    /// only ever runs from <see cref="Discover_Click"/> — never from construction/<see cref="Configure"/>.
    /// Follows the code-behind event-handler convention used throughout <c>OptionsView.axaml.cs</c>
    /// rather than ICommand-bound MVVM, since <see cref="MapTilesetMappingDialogViewModel"/> exposes
    /// plain methods/properties, not commands.
    /// </summary>
    public partial class MapTilesetMappingDialogContent : TranslatedUserControl, IEmbeddableEditor
    {
        readonly MapTilesetMappingDialogViewModel _vm;

        public string ViewTitle => R._("Map Tileset for External FEMapCreator");
        public new bool IsLoaded => true;
        public EditorDescriptor Descriptor => new(ViewTitle, 480, 360, CanResize: false);
        public object? DialogResult => _vm.Saved;
        public bool Saved => _vm.Saved;
        public bool CanClose => true;
        public event EventHandler? CloseRequested;

        public MapTilesetMappingDialogContent()
            : this(new MapTilesetMappingDialogViewModel())
        {
        }

        internal MapTilesetMappingDialogContent(MapTilesetMappingDialogViewModel vm)
        {
            _vm = vm ?? throw new ArgumentNullException(nameof(vm));
            InitializeComponent();
        }

        /// <summary>Initialize for the given, already-computed current-map tileset fingerprint. Never launches a process.</summary>
        public void Configure(TilesetFingerprint fingerprint)
        {
            _vm.Initialize(fingerprint);
            RefreshFromViewModel();
        }

        public void NavigateTo(uint address) { }

        void Discover_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
        {
            _vm.DiscoverTilesets();
            RefreshFromViewModel();
        }

        void TilesetComboBox_SelectionChanged(object? sender, global::Avalonia.Controls.SelectionChangedEventArgs e)
        {
            _vm.SelectedTileset = TilesetComboBox?.SelectedItem as FEMapCreatorTilesetOption;
        }

        void Save_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (_vm.SaveMapping())
            {
                RefreshFromViewModel();
                CloseRequested?.Invoke(this, EventArgs.Empty);
            }
            else
            {
                RefreshFromViewModel();
            }
        }

        void Cancel_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
        {
            CloseRequested?.Invoke(this, EventArgs.Empty);
        }

        void RefreshFromViewModel()
        {
            if (TilesetComboBox != null)
            {
                object? previouslySelected = TilesetComboBox.SelectedItem;
                TilesetComboBox.ItemsSource = _vm.Tilesets;
                TilesetComboBox.SelectedItem = _vm.SelectedTileset ?? previouslySelected;
            }
            if (StatusText != null) StatusText.Text = _vm.StatusMessage;
            if (ErrorText != null)
            {
                ErrorText.Text = _vm.ErrorMessage;
                ErrorText.IsVisible = !string.IsNullOrWhiteSpace(_vm.ErrorMessage);
            }
            if (DiscoverButton != null) DiscoverButton.IsEnabled = !_vm.IsBusy;
            if (SaveButton != null) SaveButton.IsEnabled = !_vm.IsBusy;
        }
    }
}
