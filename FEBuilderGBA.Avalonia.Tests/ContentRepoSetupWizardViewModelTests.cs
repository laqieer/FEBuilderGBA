using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.ViewModels;
using FEBuilderGBA.Avalonia.Views;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests
{
    public class ContentRepoSetupWizardViewModelTests : IDisposable
    {
        readonly string _baseDir;
        readonly Config _cfg;

        public ContentRepoSetupWizardViewModelTests()
        {
            _baseDir = Path.Combine(Path.GetTempPath(), "FEBuilderGBA_ContentRepoSetupWizardViewModelTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_baseDir);
            _cfg = new Config();
            _cfg.Load(Path.Combine(_baseDir, "config.xml"));
        }

        public void Dispose()
        {
            try { if (Directory.Exists(_baseDir)) Directory.Delete(_baseDir, true); } catch { }
        }

        [Fact]
        public void PrefillsUrlsFromConfigAndDefaults()
        {
            _cfg["submodule_fe_repo_url"] = "https://mirror.example/FE-Repo";
            var vm = new ContentRepoSetupWizardViewModel(_baseDir, _cfg, isGitAvailable: true);

            Assert.Equal(GitUtil.Patch2RemoteUrl, vm.Rows.Single(r => r.Descriptor.Id == "patch2").Url);
            Assert.Equal("https://mirror.example/FE-Repo", vm.Rows.Single(r => r.Descriptor.Id == "fe-repo").Url);
            Assert.Equal(GitUtil.FERepoMusicDefaultUrl, vm.Rows.Single(r => r.Descriptor.Id == "fe-repo-music").Url);
        }

        [Fact]
        public async Task InitializeAsync_PersistsTrimmedUrlAndCallsInitService()
        {
            int calls = 0;
            string? calledDir = null;
            string? calledUrl = null;
            var vm = new ContentRepoSetupWizardViewModel(_baseDir, _cfg, true, (dir, url, progress) =>
            {
                calls++;
                calledDir = dir;
                calledUrl = url;
                progress("cloning");
                string readyDir = Path.Combine(ContentRepoSetupCore.ResolveDir(ContentRepoSetupCore.Repos.Single(r => r.Id == "patch2"), _baseDir), "FE8U");
                Directory.CreateDirectory(readyDir);
                File.WriteAllText(Path.Combine(readyDir, "PATCH_READY.txt"), "NAME=Ready");
                return new Patch2GitResult { Kind = Patch2GitResultKind.Success, WasClone = true };
            }, action => action());
            var row = vm.Rows.Single(r => r.Descriptor.Id == "patch2");
            row.Url = "  https://mirror.example/patch2.git  ";

            await vm.InitializeAsync(row);

            Assert.Equal(1, calls);
            Assert.Equal(ContentRepoSetupCore.ResolveDir(row.Descriptor, _baseDir), calledDir);
            Assert.Equal("https://mirror.example/patch2.git", calledUrl);
            Assert.Equal("https://mirror.example/patch2.git", _cfg.at(row.Descriptor.ConfigKey, ""));
            Assert.Equal("Initialized successfully.", row.Progress);
            Assert.True(row.IsReady);
        }

        [Fact]
        public void SkipDoesNotSetOptOut_ButDontShowAgainDoes()
        {
            var vm = new ContentRepoSetupWizardViewModel(_baseDir, _cfg, true);
            vm.Skip();
            Assert.Equal("0", _cfg.at(ContentRepoSetupCore.OptOutConfigKey, "0"));

            vm.DontShowAgain();
            Assert.Equal("1", _cfg.at(ContentRepoSetupCore.OptOutConfigKey, "0"));
        }

        [Fact]
        public void GitUnavailableShowsManualInstructionsAndDisablesInit()
        {
            var vm = new ContentRepoSetupWizardViewModel(_baseDir, _cfg, isGitAvailable: false);

            Assert.True(vm.IsManualInstructionsVisible);
            Assert.Contains("Git was not found", vm.ManualHeader);
            Assert.Contains("FE-Repo-Music", vm.ManualInstructions);
            Assert.All(vm.Rows, row => Assert.False(row.CanInitialize));
        }

        [AvaloniaFact]
        public void ContentRepoSetupWizardView_Constructs()
        {
            var savedBase = CoreState.BaseDirectory;
            var savedConfig = CoreState.Config;
            try
            {
                CoreState.BaseDirectory = _baseDir;
                CoreState.Config = _cfg;
                var view = new ContentRepoSetupWizardView();
                Assert.NotNull(view.DataContext);
                view.Close();
            }
            finally
            {
                CoreState.BaseDirectory = savedBase;
                CoreState.Config = savedConfig;
            }
        }
    }
}
