using System;
using System.Collections.Generic;
using System.IO;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Dialogs;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;
using FEBuilderGBA.Core;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class WelcomeView : TranslatedWindow, IEditorView, IDataVerifiableView
    {
        readonly WelcomeViewModel _vm = new();
        public string ViewTitle => "Welcome";
        public bool IsLoaded => _vm.IsLoaded;
        public ViewModelBase? DataViewModel => _vm;

        /// <summary>Stores (index, fullPath) pairs for each recent file entry.</summary>
        List<string> _recentPaths = new();

        public WelcomeView()
        {
            InitializeComponent();
            DataContext = _vm;
            _vm.Initialize();
            LoadRecentFiles();
        }

        /// <summary>
        /// Load up to 5 recent ROM files from config and display them.
        /// Uses the same key prefix and max count as MainWindowViewModel.
        /// </summary>
        void LoadRecentFiles()
        {
            try
            {
                _recentPaths.Clear();
                if (CoreState.Config != null)
                {
                    for (int i = 0; i < MainWindowViewModel.MaxRecentFiles; i++)
                    {
                        string path = CoreState.Config.at(MainWindowViewModel.RecentFileKeyPrefix + i, "");
                        if (!string.IsNullOrEmpty(path) && File.Exists(path))
                        {
                            _recentPaths.Add(path);
                            if (_recentPaths.Count >= 5) break;
                        }
                    }
                }

                if (_recentPaths.Count > 0)
                {
                    NoRecentFilesLabel.IsVisible = false;
                    // Build display items with Tag holding the index into _recentPaths
                    var buttons = new List<RecentFileDisplayItem>();
                    for (int i = 0; i < _recentPaths.Count; i++)
                    {
                        buttons.Add(new RecentFileDisplayItem
                        {
                            Display = Path.GetFileName(_recentPaths[i]) + "  (" + Path.GetDirectoryName(_recentPaths[i]) + ")",
                            Index = i
                        });
                    }
                    RecentFilesList.ItemsSource = buttons;
                }
                else
                {
                    NoRecentFilesLabel.IsVisible = true;
                    RecentFilesList.ItemsSource = null;
                }
            }
            catch (Exception ex)
            {
                Log.ErrorF("WelcomeView.LoadRecentFiles failed: {0}", ex.Message);
                NoRecentFilesLabel.IsVisible = true;
            }
        }

        void OpenLastROM_Click(object? sender, RoutedEventArgs e)
        {
            Close("OpenLastROM");
        }

        void OpenROM_Click(object? sender, RoutedEventArgs e)
        {
            Close("OpenROM");
        }

        void RecentFile_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is int idx && idx >= 0 && idx < _recentPaths.Count)
            {
                string path = _recentPaths[idx];
                if (!File.Exists(path))
                {
                    _ = MessageBoxWindow.Show(this, R._("File not found:") + $" {path}", R._("Error"), MessageBoxMode.Ok);
                    return;
                }

                // Find the MainWindow and load the ROM directly
                if (WindowManager.Instance.MainWindow is MainWindow mw)
                {
                    bool ok = mw.LoadRomFile(path);
                    if (ok)
                    {
                        Close();
                    }
                    else
                    {
                        _ = MessageBoxWindow.Show(this, R._("Failed to load ROM:") + $" {path}", R._("Error"), MessageBoxMode.Ok);
                    }
                }
            }
        }

        void UpdateCheck_Click(object? sender, RoutedEventArgs e)
        {
        }

        void Manual_Click(object? sender, RoutedEventArgs e)
        {
        }

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }

        /// <summary>Helper class for recent file list items with both display text and index.</summary>
        class RecentFileDisplayItem
        {
            public string Display { get; set; } = "";
            public int Index { get; set; }
            public override string ToString() => Display;
        }
    }
}
