#nullable enable annotations
using global::Avalonia;
using System;
using System.Collections.Generic;
using System.IO;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using global::Avalonia.Media.Imaging;
using FEBuilderGBA.Avalonia.Dialogs;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;
using AvImage = global::Avalonia.Media.IImage;

namespace FEBuilderGBA.Avalonia.Views
{
    /// <summary>
    /// All-Work-Support aggregator (#1196). READ-ONLY: renders one clickable tile
    /// per discovered work-support project (logo + name + update mark); a click
    /// opens that project's ROM in the main window. Mirrors WinForms
    /// <c>ToolAllWorkSupportForm</c>.
    /// </summary>
    public partial class ToolAllWorkSupportView : TranslatedUserControl, IEmbeddableEditor
    {
        readonly ToolAllWorkSupportViewModel _vm = new();
        bool _hasLoadedList;
        readonly List<WorkProjectTileItem> _tiles = new();

        public string ViewTitle => "All Work Support";
        public new bool IsLoaded => _vm.IsLoaded;
        public EditorDescriptor Descriptor => new("All Work Support", 1024, 700, StartupLocation: WindowStartupLocation.CenterScreen);
        public event EventHandler? CloseRequested;
        public void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);

        public ToolAllWorkSupportView()
        {
            InitializeComponent();

            HeaderLabel.Text = R._("All Work Support");
            HintLabel.Text = R._("Click a project to open its ROM.");
            UpdateCheckButton.Content = R._("Check Updates");

        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnDetachedFromVisualTree(e);
            DisposeTiles();
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            if (!_hasLoadedList)
            {
                _hasLoadedList = true;
                LoadList();
            }
        }

        void LoadList()
        {
            try
            {
                DisposeTiles();

                var projects = _vm.LoadList();
                foreach (var p in projects)
                {
                    _tiles.Add(BuildTile(p));
                }

                TilesList.ItemsSource = _tiles;

                bool empty = _tiles.Count == 0;
                EmptyLabel.IsVisible = empty;
                EmptyLabel.Text = empty
                    ? R._("No work-support projects found. Projects are user-configured under config/etc.")
                    : "";
            }
            catch (Exception ex)
            {
                Log.Error("ToolAllWorkSupportView.LoadList failed: " + ex.ToString());
            }
        }

        WorkProjectTileItem BuildTile(WorkSupportScannerCore.WorkProject p)
        {
            AvImage? logo = LoadLogo(p.LogoFilename);
            return new WorkProjectTileItem
            {
                RomFilename = p.RomFilename,
                Name = p.Name,
                Logo = logo,
                IsUpdateMark = p.IsUpdateMark,
                UpdateMarkTip = R._("Update available"),
            };
        }

        /// <summary>
        /// Load a logo image from a file path, or return <c>null</c> when the path
        /// is empty/missing/unreadable. The caller owns + disposes the result.
        /// </summary>
        static AvImage? LoadLogo(string logoFilename)
        {
            try
            {
                if (string.IsNullOrEmpty(logoFilename) || !File.Exists(logoFilename))
                {
                    return null;
                }
                using var fs = File.OpenRead(logoFilename);
                return new Bitmap(fs);
            }
            catch (Exception ex)
            {
                Log.Error("ToolAllWorkSupportView.LoadLogo failed: " + ex.ToString());
                return null;
            }
        }

        void DisposeTiles()
        {
            try
            {
                foreach (var t in _tiles)
                {
                    (t.Logo as IDisposable)?.Dispose();
                    t.Logo = null;
                }
                _tiles.Clear();
                TilesList.ItemsSource = null;
            }
            catch (Exception ex)
            {
                Log.Error("ToolAllWorkSupportView.DisposeTiles failed: " + ex.ToString());
            }
        }

        void Tile_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is not Button btn || btn.Tag is not WorkProjectTileItem tile)
                {
                    return;
                }

                string path = tile.RomFilename;
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                {
                    _ = MessageBoxWindow.Show(TopLevel.GetTopLevel(this) as Window,
                        R._("File not found:") + $" {path}", R._("Error"), MessageBoxMode.Ok);
                    return;
                }

                if (WindowManager.Instance.MainWindow is MainWindow mw)
                {
                    bool ok = mw.LoadRomFile(path);
                    if (ok)
                    {
                        RequestClose();
                    }
                    else
                    {
                        _ = MessageBoxWindow.Show(TopLevel.GetTopLevel(this) as Window,
                            R._("Failed to load ROM:") + $" {path}", R._("Error"), MessageBoxMode.Ok);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error("ToolAllWorkSupportView.Tile_Click failed: " + ex.ToString());
            }
        }

        async void UpdateCheck_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                UpdateCheckButton.IsEnabled = false;
                StatusLabel.Text = R._("Checking for updates...");

                // The check is network-bound; run it off the UI thread.
                int updateable = await System.Threading.Tasks.Task.Run(() =>
                    _vm.UpdateCheckAll(url => U.HttpGet(url), HttpHeadLastModified, GetRomDateTime));

                // Re-render so the marks reflect the refreshed IsUpdateMark state.
                foreach (var p in _vm.Projects)
                {
                    foreach (var t in _tiles)
                    {
                        if (t.RomFilename == p.RomFilename)
                        {
                            t.IsUpdateMark = p.IsUpdateMark;
                        }
                    }
                }
                TilesList.ItemsSource = null;
                TilesList.ItemsSource = _tiles;

                StatusLabel.Text = string.Format(R._("Updates available: {0}"), updateable);
            }
            catch (Exception ex)
            {
                Log.Error("ToolAllWorkSupportView.UpdateCheck_Click failed: " + ex.ToString());
                StatusLabel.Text = R._("Update check failed.");
            }
            finally
            {
                UpdateCheckButton.IsEnabled = true;
            }
        }

        // One shared HttpClient for all HEAD probes (per .NET guidance — do NOT
        // new-up an HttpClient per request) with a bounded timeout so a slow or
        // unreachable host cannot hang the update check.
        static readonly System.Net.Http.HttpClient s_httpClient = new System.Net.Http.HttpClient
        {
            Timeout = TimeSpan.FromSeconds(8),
        };

        /// <summary>HTTP HEAD probe for a URL's Last-Modified header (null when absent/unreachable/timed out).</summary>
        static string? HttpHeadLastModified(string url)
        {
            try
            {
                using var req = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Head, url);
                using var resp = s_httpClient.Send(req);
                if (resp.Content.Headers.LastModified.HasValue)
                {
                    return resp.Content.Headers.LastModified.Value.ToString();
                }
                return null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Local ROM/UPS timestamp (the newer of the ROM file and its sibling
        /// <c>.ups</c>). Ports WF <c>ToolWorkSupportForm.GetROMDateTime</c>.
        /// </summary>
        static DateTime GetRomDateTime(string romFilename)
        {
            DateTime dt = File.GetLastWriteTime(romFilename);
            string ups = Path.ChangeExtension(romFilename, ".ups");
            if (File.Exists(ups))
            {
                DateTime upsDt = File.GetLastWriteTime(ups);
                if (upsDt > dt) dt = upsDt;
            }
            return dt;
        }

        public void NavigateTo(uint address) { }

        public void SelectFirstItem()
        {
            // Tiles are not a selectable list; nothing to select. LoadList (on
            // Opened) already populates the tiles for rendering/screenshot.
        }
    }

    /// <summary>One rendered project tile (logo + name + update mark).</summary>
    public sealed class WorkProjectTileItem
    {
        public string RomFilename { get; set; } = "";
        public string Name { get; set; } = "";
        public AvImage? Logo { get; set; }
        public bool IsUpdateMark { get; set; }
        public string UpdateMarkTip { get; set; } = "";
    }
}
