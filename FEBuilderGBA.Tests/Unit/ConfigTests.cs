using System;
using System.IO;
using Xunit;

namespace FEBuilderGBA.Tests.Unit
{
    /// <summary>
    /// Tests for Config base class (Core migration batch 12).
    /// </summary>
    public class ConfigTests : IDisposable
    {
        private readonly string _tempDir;

        public ConfigTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "FEBuilderGBA_ConfigTest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_tempDir, true); } catch { }
        }

        [Fact]
        public void Load_MissingFile_EmptyDictionary()
        {
            var config = new Config();
            config.Load(Path.Combine(_tempDir, "nonexistent.xml"));
            Assert.Empty(config);
        }

        [Fact]
        public void Load_ValidXml_PopulatesDictionary()
        {
            string path = Path.Combine(_tempDir, "config.xml");
            File.WriteAllText(path, @"<?xml version=""1.0"" encoding=""utf-8""?>
<root>
  <item><key>Language</key><value>en</value></item>
  <item><key>Theme</key><value>dark</value></item>
</root>");

            var config = new Config();
            config.Load(path);
            Assert.Equal(2, config.Count);
            Assert.Equal("en", config["Language"]);
            Assert.Equal("dark", config["Theme"]);
        }

        [Fact]
        public void Load_SetsConfigFilename()
        {
            string path = Path.Combine(_tempDir, "config.xml");
            File.WriteAllText(path, @"<?xml version=""1.0"" encoding=""utf-8""?><root></root>");

            var config = new Config();
            config.Load(path);
            Assert.Equal(path, config.ConfigFilename);
        }

        [Fact]
        public void Load_CorruptedXml_HandlesGracefully()
        {
            string path = Path.Combine(_tempDir, "bad.xml");
            File.WriteAllText(path, "not valid xml <><>");

            var config = new Config();
            var ex = Record.Exception(() => config.Load(path));
            // Should not throw — error is handled internally via R.ShowStopError
            Assert.Null(ex);
            Assert.Empty(config);
        }

        [Fact]
        public void SaveAndLoad_RoundTrip_PreservesAllPairs()
        {
            string path = Path.Combine(_tempDir, "roundtrip.xml");

            var original = new Config();
            original.Load(path); // sets ConfigFilename
            original["Key1"] = "Value1";
            original["Key2"] = "Value2";
            original["Key3"] = "Value3";
            original.Save();

            var loaded = new Config();
            loaded.Load(path);
            Assert.Equal(3, loaded.Count);
            Assert.Equal("Value1", loaded["Key1"]);
            Assert.Equal("Value2", loaded["Key2"]);
            Assert.Equal("Value3", loaded["Key3"]);
        }

        [Fact]
        public void Save_ExplicitPath_WritesToSpecifiedFile()
        {
            string path1 = Path.Combine(_tempDir, "config1.xml");
            string path2 = Path.Combine(_tempDir, "config2.xml");

            var config = new Config();
            config.Load(path1);
            config["Hello"] = "World";
            config.Save(path2);

            Assert.True(File.Exists(path2));
            var loaded = new Config();
            loaded.Load(path2);
            Assert.Equal("World", loaded["Hello"]);
        }

        [Fact]
        public void At_ExistingKey_ReturnsValue()
        {
            var config = new Config();
            config["Foo"] = "Bar";
            Assert.Equal("Bar", config.at("Foo"));
        }

        [Fact]
        public void At_MissingKey_ReturnsDefault()
        {
            var config = new Config();
            Assert.Equal("fallback", config.at("Missing", "fallback"));
        }

        [Fact]
        public void At_MissingKey_NoDefault_ReturnsEmptyString()
        {
            var config = new Config();
            Assert.Equal("", config.at("Missing"));
        }
    }
}
