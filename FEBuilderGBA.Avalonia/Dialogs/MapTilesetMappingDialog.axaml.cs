#nullable enable

using global::Avalonia.Controls;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Dialogs
{
    public partial class MapTilesetMappingDialog : TranslatedWindow
    {
        MapTilesetMappingDialogContent? _content;
        bool _allowClose;

        public bool Saved { get; private set; }

        public MapTilesetMappingDialog()
            : this(new MapTilesetMappingDialogContent())
        {
        }

        internal MapTilesetMappingDialog(MapTilesetMappingDialogContent content)
        {
            InitializeComponent();
            _content = content ?? throw new System.ArgumentNullException(nameof(content));
            Content = _content;
            Closing += MapTilesetMappingDialog_Closing;
        }

        public MapTilesetMappingDialog(TilesetFingerprint fingerprint)
            : this()
        {
            _content ??= new MapTilesetMappingDialogContent();
            _content.Configure(fingerprint);
            _content.CloseRequested += (_, _) =>
            {
                Saved = _content.Saved;
                _allowClose = true;
                Close();
            };
        }

        void MapTilesetMappingDialog_Closing(object? sender, WindowClosingEventArgs e)
        {
            if (!_allowClose && _content?.CanClose == false)
                e.Cancel = true;
        }

        /// <summary>
        /// Show the explicit "map this tileset" action for <paramref name="fingerprint"/>.
        /// Returns true when a mapping was saved. Never launches discovery or mutates
        /// config on its own — the dialog only acts on the user's explicit clicks.
        /// </summary>
        public static async System.Threading.Tasks.Task<bool> Show(Window? owner, TilesetFingerprint fingerprint)
        {
            if (WindowManager.Instance.Service is AndroidNavigationService)
            {
                bool? result = await WindowManager.Instance.OpenModal<MapTilesetMappingDialogContent, bool>(
                    owner,
                    content => content.Configure(fingerprint));
                return result == true;
            }

            var dlg = new MapTilesetMappingDialog(fingerprint);
            if (owner != null)
                await dlg.ShowDialog(owner);
            else
                dlg.Show();
            return dlg.Saved;
        }
    }
}
