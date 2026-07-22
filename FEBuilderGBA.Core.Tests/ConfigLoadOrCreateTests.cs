using System;
using System.IO;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    /// <summary>
    /// #1799: <see cref="Config.LoadOrCreate(string)"/> must yield a usable, persistable
    /// <see cref="Config"/> even when the file does not exist yet (fresh install), so
    /// <c>CoreState.Config</c> is never null and the Options dialog can save. Previously
    /// App.axaml.cs / RomLoader.cs guarded the assignment behind <c>File.Exists</c>, leaving
    /// <c>CoreState.Config</c> null on first run and silently discarding all settings.
    /// </summary>
    public class ConfigLoadOrCreateTests : IDisposable
    {
        readonly string _dir;
        readonly string _configPath;

        public ConfigLoadOrCreateTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), $"cfg_loadorcreate_{Guid.NewGuid():N}");
            _configPath = Path.Combine(_dir, "config", "config.xml");
        }

        public void Dispose()
        {
            try { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); } catch { /* best effort */ }
        }

        [Fact]
        public void LoadOrCreate_MissingFile_ReturnsUsableConfig_AndCreatesDirectory()
        {
            Assert.False(File.Exists(_configPath));
            Assert.False(Directory.Exists(Path.GetDirectoryName(_configPath)!));

            var cfg = Config.LoadOrCreate(_configPath);

            Assert.NotNull(cfg);                                              // never null on a fresh install
            Assert.Equal(_configPath, cfg.ConfigFilename);                   // Save() has a concrete target
            Assert.True(Directory.Exists(Path.GetDirectoryName(_configPath)!)); // parent dir ensured
            Assert.Equal("", cfg.at("emulator", ""));                        // empty, not crashed
        }

        [Fact]
        public void LoadOrCreate_SaveThenReload_RoundTripsToolPaths()
        {
            var cfg = Config.LoadOrCreate(_configPath);
            cfg["emulator"] = @"C:\tools\vba.exe";
            cfg["sappy"] = @"C:\tools\sappy.exe";
            cfg.Save();

            Assert.True(File.Exists(_configPath));                           // first-run config.xml created

            var reloaded = Config.LoadOrCreate(_configPath);
            Assert.Equal(@"C:\tools\vba.exe", reloaded.at("emulator", ""));
            Assert.Equal(@"C:\tools\sappy.exe", reloaded.at("sappy", ""));
        }

        [Fact]
        public void SaveOrThrow_MissingParentDirectory_PropagatesFailure()
        {
            var cfg = new Config
            {
                ["emulator"] = @"C:\tools\vba.exe",
            };
            string path = Path.Combine(_dir, "missing", "config.xml");

            Assert.Throws<DirectoryNotFoundException>(() => cfg.SaveOrThrow(path));
            Assert.False(File.Exists(path));
        }

        [Fact]
        public void SaveOrThrow_ReplaceFails_PreservesExistingFileAndCleansTemp()
        {
            var persisted = Config.LoadOrCreate(_configPath);
            persisted["emulator"] = @"C:\tools\old.exe";
            persisted.SaveOrThrow(_configPath);
            string originalXml = File.ReadAllText(_configPath);

            var proposed = new Config
            {
                ["emulator"] = @"C:\tools\new.exe",
            };

            IOException failure = Assert.Throws<IOException>(() =>
                proposed.SaveOrThrow(
                    _configPath,
                    (tempPath, targetPath) =>
                    {
                        Assert.Equal(Path.GetDirectoryName(_configPath), Path.GetDirectoryName(tempPath));
                        Assert.Equal(Path.GetFullPath(_configPath), targetPath);
                        Assert.True(File.Exists(tempPath));
                        var staged = new Config();
                        staged.Load(tempPath);
                        Assert.Equal(@"C:\tools\new.exe", staged.at("emulator", ""));
                        throw new IOException("replace failed");
                    }));

            Assert.Equal("replace failed", failure.Message);
            Assert.Equal(originalXml, File.ReadAllText(_configPath));
            Config reloaded = Config.LoadOrCreate(_configPath);
            Assert.Equal(@"C:\tools\old.exe", reloaded.at("emulator", ""));
            Assert.Empty(Directory.GetFiles(
                Path.GetDirectoryName(_configPath)!,
                ".fegba-config-*.tmp"));
        }

        [Fact]
        public void SaveOrThrow_CancelAfterFlushBeforeReplace_LeavesOriginalAndCleansTemp()
        {
            var persisted = Config.LoadOrCreate(_configPath);
            persisted["emulator"] = @"C:\tools\old.exe";
            persisted.SaveOrThrow(_configPath);
            string originalXml = File.ReadAllText(_configPath);

            var proposed = new Config
            {
                ["emulator"] = @"C:\tools\new.exe",
            };

            using var cts = new System.Threading.CancellationTokenSource();
            bool replaceCalled = false;
            bool stagedExistedAtBoundary = false;
            string? stagedEmulatorAtBoundary = null;

            Assert.Throws<OperationCanceledException>(() =>
                proposed.SaveOrThrow(
                    _configPath,
                    cts.Token,
                    replaceFile: (tempPath, targetPath) => replaceCalled = true,
                    afterTempFileFlushed: tempPath =>
                    {
                        // The fully flushed sibling temp exists and holds the new config, yet
                        // the original target has not been touched. Cancelling here must
                        // linearize immediately before replacement.
                        stagedExistedAtBoundary = File.Exists(tempPath);
                        var staged = new Config();
                        staged.Load(tempPath);
                        stagedEmulatorAtBoundary = staged.at("emulator", "");
                        cts.Cancel();
                    }));

            Assert.False(replaceCalled);                                    // replacement delegate never runs
            Assert.True(stagedExistedAtBoundary);                           // new staged config existed at the boundary
            Assert.Equal(@"C:\tools\new.exe", stagedEmulatorAtBoundary);    // ...and held the proposed value
            Assert.Equal(originalXml, File.ReadAllText(_configPath));       // original disk config untouched
            Config reloaded = Config.LoadOrCreate(_configPath);
            Assert.Equal(@"C:\tools\old.exe", reloaded.at("emulator", ""));
            Assert.Empty(Directory.GetFiles(
                Path.GetDirectoryName(_configPath)!,
                ".fegba-config-*.tmp"));                                    // temp cleaned in finally
        }
    }
}
