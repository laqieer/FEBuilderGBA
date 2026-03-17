using System;
using System.Collections.Generic;
using System.IO;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;
using FEBuilderGBA.Core;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class WelcomeView : Window, IEditorView, IDataVerifiableView
    {
        readonly WelcomeViewModel _vm = new();
        public string ViewTitle => "Welcome";
        public bool IsLoaded => _vm.IsLoaded;
        public ViewModelBase? DataViewModel => _vm;

        /// <summary>
        /// When a recent file is selected, this holds the path.
        /// The caller can check this after the dialog closes.
        /// </summary>
        public string? SelectedRecentFilePath { get; private set; }

        public WelcomeView()
        {
            InitializeComponent();
            DataContext = _vm;
            _vm.Initialize();
            LoadRecentFiles();
        }

        /// <summary>
        /// Load up to 5 recent ROM files from config and display them.
        /// </summary>
        void LoadRecentFiles()
        {
            try
            {
                var recentPaths = new List<string>();
                if (CoreState.Config != null)
                {
                    for (int i = 0; i < 10; i++)
                    {
                        string path = CoreState.Config.at("Recent_Rom_" + i, "");
                        if (!string.IsNullOrEmpty(path) && File.Exists(path))
                        {
                            recentPaths.Add(path);
                            if (recentPaths.Count >= 5) break;
                        }
                    }
                }

                if (recentPaths.Count > 0)
                {
                    NoRecentFilesLabel.IsVisible = false;
                    // Show just the filename for display, but store full path in Tag
                    var displayItems = new List<string>();
                    foreach (var p in recentPaths)
                    {
                        displayItems.Add(Path.GetFileName(p) + "  (" + Path.GetDirectoryName(p) + ")");
                    }
                    RecentFilesList.ItemsSource = displayItems;
                    // Store the actual paths for click handling
                    RecentFilesList.Tag = recentPaths;
                }
                else
                {
                    NoRecentFilesLabel.IsVisible = true;
                    RecentFilesList.ItemsSource = null;
                }
            }
            catch (Exception ex)
            {
                Log.Error("WelcomeView.LoadRecentFiles failed: {0}", ex.Message);
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
            if (sender is Button btn && btn.Content is string displayText)
            {
                // Find the matching path from the stored list
                if (RecentFilesList.Tag is List<string> paths && RecentFilesList.ItemsSource is List<string> displays)
                {
                    int idx = displays.IndexOf(displayText);
                    if (idx >= 0 && idx < paths.Count)
                    {
                        SelectedRecentFilePath = paths[idx];
                        Close("OpenRecentFile");
                        return;
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
    }
}
