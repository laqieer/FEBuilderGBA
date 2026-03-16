using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    /// <summary>
    /// Tests for recent files list management logic used by MainWindowViewModel.
    /// Reproduces the core algorithm standalone to avoid Avalonia dependency.
    /// </summary>
    [Collection("SharedState")]
    public class RecentFilesTests : IDisposable
    {
        const int MaxRecentFiles = 10;
        const string KeyPrefix = "Recent_Rom_";

        readonly Config _config;
        readonly string _configPath;

        public RecentFilesTests()
        {
            _configPath = Path.Combine(Path.GetTempPath(), $"recent_files_test_{Guid.NewGuid()}.xml");
            _config = new Config();
            _config.Load(_configPath);
            CoreState.Config = _config;
        }

        public void Dispose()
        {
            try { File.Delete(_configPath); } catch { }
        }

        /// <summary>Reproduce the AddRecentFile algorithm from MainWindowViewModel.</summary>
        static List<string> AddRecentFile(Config config, string path, List<string> existing)
        {
            var paths = new List<string> { path };
            foreach (var p in existing)
            {
                if (!string.Equals(p, path, StringComparison.OrdinalIgnoreCase)
                    && paths.Count < MaxRecentFiles)
                {
                    paths.Add(p);
                }
            }

            // Persist
            for (int i = 0; i < MaxRecentFiles; i++)
            {
                string key = KeyPrefix + i;
                if (i < paths.Count)
                    config[key] = paths[i];
                else if (config.ContainsKey(key))
                    config.Remove(key);
            }
            return paths;
        }

        /// <summary>Read recent files from config.</summary>
        static List<string> LoadRecentFiles(Config config)
        {
            var result = new List<string>();
            for (int i = 0; i < MaxRecentFiles; i++)
            {
                string val = config.at(KeyPrefix + i, "");
                if (!string.IsNullOrEmpty(val))
                    result.Add(val);
            }
            return result;
        }

        [Fact]
        public void AddRecentFile_AddsToFront()
        {
            var current = new List<string>();
            var result = AddRecentFile(_config, "/path/to/rom1.gba", current);
            Assert.Single(result);
            Assert.Equal("/path/to/rom1.gba", result[0]);
        }

        [Fact]
        public void AddRecentFile_MovesDuplicateToFront()
        {
            var current = new List<string> { "/path/a.gba", "/path/b.gba", "/path/c.gba" };
            var result = AddRecentFile(_config, "/path/b.gba", current);
            Assert.Equal(3, result.Count);
            Assert.Equal("/path/b.gba", result[0]);
            Assert.Equal("/path/a.gba", result[1]);
            Assert.Equal("/path/c.gba", result[2]);
        }

        [Fact]
        public void AddRecentFile_CapsAtMax()
        {
            var current = new List<string>();
            for (int i = 0; i < MaxRecentFiles; i++)
                current.Add($"/path/rom{i}.gba");

            // Add an 11th file
            var result = AddRecentFile(_config, "/path/new.gba", current);
            Assert.Equal(MaxRecentFiles, result.Count);
            Assert.Equal("/path/new.gba", result[0]);
            // The last old entry should be dropped
            Assert.DoesNotContain($"/path/rom{MaxRecentFiles - 1}.gba", result);
        }

        [Fact]
        public void AddRecentFile_PersistsToConfig()
        {
            var current = new List<string>();
            AddRecentFile(_config, "/path/first.gba", current);
            current = LoadRecentFiles(_config);
            AddRecentFile(_config, "/path/second.gba", current);

            var loaded = LoadRecentFiles(_config);
            Assert.Equal(2, loaded.Count);
            Assert.Equal("/path/second.gba", loaded[0]);
            Assert.Equal("/path/first.gba", loaded[1]);
        }

        [Fact]
        public void AddRecentFile_DuplicateIsCaseInsensitive()
        {
            var current = new List<string> { "/Path/Rom.GBA" };
            var result = AddRecentFile(_config, "/path/rom.gba", current);
            Assert.Single(result);
            Assert.Equal("/path/rom.gba", result[0]);
        }

        [Fact]
        public void LoadRecentFiles_EmptyConfigReturnsEmpty()
        {
            var loaded = LoadRecentFiles(_config);
            Assert.Empty(loaded);
        }

        [Fact]
        public void AddRecentFile_CleansUpOldKeys()
        {
            // Start with 3 entries
            var current = new List<string> { "/a.gba", "/b.gba", "/c.gba" };
            AddRecentFile(_config, "/a.gba", current);

            // Verify only 3 keys exist
            Assert.Equal("/a.gba", _config.at(KeyPrefix + "0"));
            Assert.Equal("/b.gba", _config.at(KeyPrefix + "1"));
            Assert.Equal("/c.gba", _config.at(KeyPrefix + "2"));
            Assert.Equal("", _config.at(KeyPrefix + "3", ""));
        }

        [Fact]
        public void MissingFilesDetected()
        {
            // This tests the concept: File.Exists returns false for non-existent paths
            Assert.False(File.Exists("/nonexistent/path/rom.gba"));
        }

        [Fact]
        public void ExistingFileDetected()
        {
            // Create a temp file and verify it exists
            string tempFile = Path.GetTempFileName();
            try
            {
                Assert.True(File.Exists(tempFile));
            }
            finally
            {
                File.Delete(tempFile);
            }
        }
    }
}
