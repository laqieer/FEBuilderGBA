using System;
using System.IO;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    /// <summary>
    /// Tests that Options dialog config keys match WinForms conventions
    /// and round-trip correctly through Config load/save.
    /// </summary>
    [Collection("SharedState")]
    public class OptionsConfigKeyTests : IDisposable
    {
        readonly string _configPath;
        readonly Config _config;
        readonly Config? _origConfig;

        // All WinForms-compatible tool path config keys
        static readonly string[] ToolPathKeys = new[]
        {
            "emulator", "emulator2",
            "program1", "program2", "program3",
            "sappy", "mid2agb", "gba_mus_riper", "sox", "midfix4agb",
            "event_assembler", "devkitpro_eabi", "goldroad_asm", "CFLAGS", "retdec", "python3", "FECLIB",
            "srccode_texteditor", "srccode_directory",
        };

        public OptionsConfigKeyTests()
        {
            _configPath = Path.Combine(Path.GetTempPath(), $"options_config_test_{Guid.NewGuid()}.xml");
            _config = new Config();
            _origConfig = CoreState.Config;
            CoreState.Config = _config;
        }

        public void Dispose()
        {
            CoreState.Config = _origConfig;
            try { if (File.Exists(_configPath)) File.Delete(_configPath); } catch { }
        }

        [Fact]
        public void ToolPathKeys_RoundTrip_ThroughConfig()
        {
            // Set values for all tool path keys
            for (int i = 0; i < ToolPathKeys.Length; i++)
            {
                _config[ToolPathKeys[i]] = $"/test/path/{i}";
            }

            // Save and reload
            _config.Save(_configPath);
            var loaded = new Config();
            loaded.Load(_configPath);

            // Verify all keys survived round-trip
            for (int i = 0; i < ToolPathKeys.Length; i++)
            {
                Assert.Equal($"/test/path/{i}", loaded.at(ToolPathKeys[i], ""));
            }
        }

        [Fact]
        public void GeneralSettings_RoundTrip_ThroughConfig()
        {
            _config["Language"] = "zh";
            _config["git_path"] = "/usr/bin/git";
            _config["func_auto_backup"] = "1";

            _config.Save(_configPath);
            var loaded = new Config();
            loaded.Load(_configPath);

            Assert.Equal("zh", loaded.at("Language", ""));
            Assert.Equal("/usr/bin/git", loaded.at("git_path", ""));
            Assert.Equal("1", loaded.at("func_auto_backup", ""));
        }

        [Fact]
        public void EmptyToolPaths_DefaultToEmptyString()
        {
            // Verify unset keys return empty string default
            foreach (var key in ToolPathKeys)
            {
                Assert.Equal("", _config.at(key, ""));
            }
        }

        [Fact]
        public void ConfigKeys_AreCaseSensitive()
        {
            // WinForms uses exact case: "emulator" not "Emulator", "CFLAGS" not "cflags"
            _config["emulator"] = "/path/emu";
            _config["CFLAGS"] = "-O2";

            Assert.Equal("/path/emu", _config.at("emulator", ""));
            Assert.Equal("-O2", _config.at("CFLAGS", ""));
            // Different case should not find the value
            Assert.Equal("", _config.at("Emulator", ""));
            Assert.Equal("", _config.at("cflags", ""));
        }

        [Fact]
        public void OldAvaloniaKeys_NotUsed()
        {
            // Ensure the old Avalonia-specific keys are not set by default
            // These were the broken keys: Emulator_Path, BinaryEditor_Path, Sappy_Path, CustomTool_Path
            Assert.Equal("", _config.at("Emulator_Path", ""));
            Assert.Equal("", _config.at("BinaryEditor_Path", ""));
            Assert.Equal("", _config.at("Sappy_Path", ""));
            Assert.Equal("", _config.at("CustomTool_Path", ""));
        }
    }
}
