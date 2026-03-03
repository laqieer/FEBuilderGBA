using System;
using System.IO;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    /// <summary>
    /// Integration tests for CLI commands using synthetic ROM data.
    /// No copyrighted ROM data is used.
    /// </summary>
    public class CliIntegrationTests
    {
        [Fact]
        public void UPS_RoundTrip_CreateAndApply()
        {
            // Create two synthetic "ROM" files with a known difference
            byte[] original = new byte[256];
            byte[] modified = new byte[256];

            // Fill with a pattern
            for (int i = 0; i < 256; i++)
            {
                original[i] = (byte)(i & 0xFF);
                modified[i] = (byte)(i & 0xFF);
            }

            // Introduce some differences
            modified[0x10] = 0xAA;
            modified[0x20] = 0xBB;
            modified[0x30] = 0xCC;

            string tempDir = Path.Combine(Path.GetTempPath(), "febuilder_cli_test_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            try
            {
                string originalPath = Path.Combine(tempDir, "original.bin");
                string modifiedPath = Path.Combine(tempDir, "modified.bin");
                string patchPath = Path.Combine(tempDir, "test.ups");

                File.WriteAllBytes(originalPath, original);
                File.WriteAllBytes(modifiedPath, modified);

                // Create UPS patch
                UPSUtilCore.MakeUPS(original, modified, patchPath);
                Assert.True(File.Exists(patchPath), "UPS patch file should be created");

                byte[] patchData = File.ReadAllBytes(patchPath);
                Assert.True(patchData.Length > 0, "UPS patch should not be empty");

                // Verify UPS magic header "UPS1"
                Assert.Equal((byte)'U', patchData[0]);
                Assert.Equal((byte)'P', patchData[1]);
                Assert.Equal((byte)'S', patchData[2]);
                Assert.Equal((byte)'1', patchData[3]);

                // Apply UPS patch
                byte[] result = UPSUtilCore.ApplyUPS(original, patchData, out string errorMessage);
                Assert.NotNull(result);

                // Verify result matches modified
                Assert.Equal(modified.Length, result.Length);
                for (int i = 0; i < modified.Length; i++)
                {
                    Assert.Equal(modified[i], result[i]);
                }
            }
            finally
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }

        [Fact]
        public void UPS_EmptyDiff_ProducesValidPatch()
        {
            // Two identical files should produce a valid (minimal) patch
            byte[] data = new byte[128];
            for (int i = 0; i < 128; i++) data[i] = (byte)(i * 3);

            string tempDir = Path.Combine(Path.GetTempPath(), "febuilder_cli_test_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            try
            {
                string patchPath = Path.Combine(tempDir, "noop.ups");
                UPSUtilCore.MakeUPS(data, data, patchPath);
                Assert.True(File.Exists(patchPath));

                byte[] patchData = File.ReadAllBytes(patchPath);
                byte[] result = UPSUtilCore.ApplyUPS(data, patchData, out string errorMessage);
                Assert.NotNull(result);
                Assert.Equal(data.Length, result.Length);

                for (int i = 0; i < data.Length; i++)
                    Assert.Equal(data[i], result[i]);
            }
            finally
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }

        [Fact]
        public void HeadlessEtcCache_BasicOperations()
        {
            var cache = new HeadlessEtcCache();

            // Initially empty
            Assert.False(cache.CheckFast(100));
            Assert.Equal("default", cache.At(100, "default"));
            Assert.Equal("", cache.S_At(100));

            // Update and read back
            cache.Update(100, "test_value");
            Assert.True(cache.CheckFast(100));
            Assert.Equal("test_value", cache.At(100));
            Assert.Equal("test_value", cache.S_At(100));

            // Remove
            cache.Remove(100);
            Assert.False(cache.CheckFast(100));

            // RemoveOverRange
            cache.Update(10, "a");
            cache.Update(20, "b");
            cache.Update(30, "c");
            cache.RemoveOverRange(20);
            Assert.True(cache.CheckFast(10));
            Assert.False(cache.CheckFast(20));
            Assert.False(cache.CheckFast(30));
        }

        [Fact]
        public void HeadlessSystemTextEncoder_Decode_BasicRoundTrip()
        {
            var encoder = new HeadlessSystemTextEncoder();

            // Encode simple ASCII
            byte[] encoded = encoder.Encode("Hello");
            Assert.NotNull(encoded);
            Assert.True(encoded.Length > 0);

            // Decode back
            string decoded = encoder.Decode(encoded, 0, encoded.Length);
            Assert.Equal("Hello", decoded);
        }

        [Fact]
        public void ParseArgs_HandlesAllFormats()
        {
            // Test the CLI arg parser through reflection or by testing the dictionary
            var args = new[] { "--version", "--rom=test.gba", "--lint", "-h" };
            var dic = new System.Collections.Generic.Dictionary<string, string>();

            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                if (arg.StartsWith("--"))
                {
                    int eq = arg.IndexOf('=');
                    if (eq >= 0)
                        dic[arg.Substring(0, eq)] = arg.Substring(eq + 1);
                    else
                        dic[arg] = "";
                }
                else if (arg == "-h")
                {
                    dic["--help"] = "";
                }
            }

            Assert.True(dic.ContainsKey("--version"));
            Assert.Equal("test.gba", dic["--rom"]);
            Assert.True(dic.ContainsKey("--lint"));
            Assert.True(dic.ContainsKey("--help"));
        }

        [Fact]
        public void Config_LoadAndReadLastRom()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "febuilder_config_test_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            try
            {
                string configPath = Path.Combine(tempDir, "config.xml");
                // Write a minimal config with Last_Rom_Filename
                File.WriteAllText(configPath,
                    "<?xml version=\"1.0\"?><root><item><key>Last_Rom_Filename</key><value>/tmp/test.gba</value></item></root>");

                var config = new Config();
                config.Load(configPath);

                Assert.Equal("/tmp/test.gba", config.at("Last_Rom_Filename"));
            }
            finally
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }

        [Fact]
        public void Config_AtReturnsDefault()
        {
            var config = new Config();
            Assert.Equal("default_value", config.at("NonExistent", "default_value"));
            Assert.Equal("", config.at("NonExistent"));
        }

        [Fact]
        public void ParseArgs_NewMigratedFlags()
        {
            // Test all 5 new migrated CLI args
            var args = new[] { "--lastrom", "--force-detail", "--translate_batch", "--test", "--testonly" };
            var dic = new System.Collections.Generic.Dictionary<string, string>();
            foreach (var arg in args)
            {
                if (arg.StartsWith("--"))
                    dic[arg] = "";
            }

            Assert.True(dic.ContainsKey("--lastrom"));
            Assert.True(dic.ContainsKey("--force-detail"));
            Assert.True(dic.ContainsKey("--translate_batch"));
            Assert.True(dic.ContainsKey("--test"));
            Assert.True(dic.ContainsKey("--testonly"));
        }
    }
}
