using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Dialogs;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class ToolWorkSupportView : Window, IEditorView
    {
        readonly ToolWorkSupportViewModel _vm = new();
        public string ViewTitle => "Work Support";
        public bool IsLoaded => _vm.IsLoaded;

        public ToolWorkSupportView()
        {
            InitializeComponent();
            DataContext = _vm;
            _vm.IsLoading = true;
            _vm.Initialize();
            _vm.IsLoading = false;
            _vm.MarkClean();
        }

        async void Update_Click(object? sender, RoutedEventArgs e)
        {
            _vm.AutoFeedbackStatus = "Checking for updates...";
            try
            {
                using var client = new HttpClient();
                client.DefaultRequestHeaders.Add("User-Agent", "FEBuilderGBA");
                client.Timeout = TimeSpan.FromSeconds(15);

                var response = await client.GetStringAsync(
                    "https://api.github.com/repos/laqieer/FEBuilderGBA/releases/latest");

                using var doc = JsonDocument.Parse(response);
                var root = doc.RootElement;
                string tagName = root.TryGetProperty("tag_name", out var tag) ? tag.GetString() ?? "" : "";
                string htmlUrl = root.TryGetProperty("html_url", out var url) ? url.GetString() ?? "" : "";

                string localVersion = U.getVersion();

                if (string.IsNullOrEmpty(tagName))
                {
                    _vm.AutoFeedbackStatus = "Could not determine remote version.";
                    return;
                }

                // Compare: tag_name is typically like "20260301.00" or "v20260301.00"
                string remoteVer = tagName.TrimStart('v', 'V');
                var info = new UpdateInfo();
                var updateType = info.DetermineUpdateType(remoteVer);

                if (updateType == UpdateInfo.PackageType.None)
                {
                    _vm.AutoFeedbackStatus = $"You are up to date. (Local: {localVersion}, Remote: {remoteVer})";
                }
                else
                {
                    _vm.AutoFeedbackStatus = $"Update available! Local: {localVersion}, Remote: {remoteVer}\n{htmlUrl}";
                }
            }
            catch (TaskCanceledException)
            {
                _vm.AutoFeedbackStatus = "Update check timed out. Please try again.";
            }
            catch (HttpRequestException ex)
            {
                _vm.AutoFeedbackStatus = $"Network error: {ex.Message}";
            }
            catch (Exception ex)
            {
                _vm.AutoFeedbackStatus = $"Update check failed: {ex.Message}";
                Log.Error("ToolWorkSupportView.Update", ex.ToString());
            }
        }

        void Community_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                if (!string.IsNullOrEmpty(_vm.CommunityUrl))
                {
                    var psi = new System.Diagnostics.ProcessStartInfo(_vm.CommunityUrl) { UseShellExecute = true };
                    System.Diagnostics.Process.Start(psi);
                }
            }
            catch (Exception ex)
            {
                Log.Error("ToolWorkSupportView.Community", ex.ToString());
            }
        }

        void OpenInfo_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                if (!string.IsNullOrEmpty(_vm.InfoText) && System.IO.File.Exists(_vm.InfoText))
                {
                    var psi = new System.Diagnostics.ProcessStartInfo(_vm.InfoText) { UseShellExecute = true };
                    System.Diagnostics.Process.Start(psi);
                }
            }
            catch (Exception ex)
            {
                Log.Error("ToolWorkSupportView.OpenInfo", ex.ToString());
            }
        }

        void ShowAllWorks_Click(object? sender, RoutedEventArgs e)
        {
            WindowManager.Instance.Open<ToolAllWorkSupportView>();
        }

        void Reload_Click(object? sender, RoutedEventArgs e)
        {
            _vm.IsLoading = true;
            _vm.Initialize();
            _vm.IsLoading = false;
            _vm.MarkClean();
        }

        void Close_Click(object? sender, RoutedEventArgs e)
        {
            Close();
        }

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
    }
}
