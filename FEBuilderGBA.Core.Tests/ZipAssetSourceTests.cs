using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using FEBuilderGBA;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    /// <summary>
    /// Desktop unit tests for the pure <see cref="ZipAssetSource"/> (#1864) — the browser head's
    /// config source (a fetched <c>config.zip</c>). Verifies the enumeration/open contract and
    /// that it drives <see cref="AndroidConfigExtractorCore.EnsureExtracted"/> to a correct
    /// extraction, including the critical guarantee that entries retain the <c>config/</c> prefix
    /// so the app resolves <c>{BaseDirectory}/config/config.xml</c> after extraction.
    /// </summary>
    public class ZipAssetSourceTests
    {
        static ZipArchive BuildZip(params (string path, string content)[] entries)
        {
            var ms = new MemoryStream();
            using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            {
                foreach (var (path, content) in entries)
                {
                    ZipArchiveEntry e = zip.CreateEntry(path);
                    using Stream s = e.Open();
                    byte[] bytes = Encoding.UTF8.GetBytes(content);
                    s.Write(bytes, 0, bytes.Length);
                }
            }
            ms.Position = 0;
            return new ZipArchive(ms, ZipArchiveMode.Read);
        }

        static string NewTempDir()
        {
            string d = Path.Combine(Path.GetTempPath(), "feb_zipas_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(d);
            return d;
        }

        [Fact]
        public void Enumerate_ReturnsConfigPrefixedFilePaths_SkipsDirectories()
        {
            using ZipArchive zip = BuildZip(
                ("config/", ""),                       // explicit directory entry — must be skipped
                ("config/data/foo.txt", "FOO"),
                ("config/config.xml", "<config/>"));
            var src = new ZipAssetSource(zip);

            string[] files = src.EnumerateAssetFiles().OrderBy(p => p, StringComparer.Ordinal).ToArray();
            Assert.Equal(new[] { "config/config.xml", "config/data/foo.txt" }, files);
            Assert.All(files, f => Assert.StartsWith("config/", f));
        }

        [Fact]
        public void OpenAsset_ReturnsEntryContent()
        {
            using ZipArchive zip = BuildZip(("config/data/foo.txt", "HELLO-ZIP"));
            var src = new ZipAssetSource(zip);
            using Stream s = src.OpenAsset("config/data/foo.txt");
            using var reader = new StreamReader(s);
            Assert.Equal("HELLO-ZIP", reader.ReadToEnd());
        }

        [Fact]
        public void OpenAsset_Missing_Throws()
        {
            using ZipArchive zip = BuildZip(("config/a.txt", "A"));
            var src = new ZipAssetSource(zip);
            Assert.Throws<FileNotFoundException>(() => src.OpenAsset("config/missing.txt"));
        }

        [Fact]
        public void Ctor_RejectsNull()
        {
            Assert.Throws<ArgumentNullException>(() => new ZipAssetSource(null!));
        }

        [Fact]
        public void DrivesEnsureExtracted_ResolvesConfigConfigXml_EndToEnd()
        {
            // The exact browser flow: a config.zip with config/-prefixed entries, extracted via
            // the shared extractor into a writable target, then the app resolves config/config.xml
            // under BaseDirectory=targetRoot (opus #1864 N1 round-trip).
            using ZipArchive zip = BuildZip(
                ("config/config.xml", "<config/>"),
                ("config/data/foo.txt", "FOO"),
                ("config/translate/ja.txt", "JA"));
            string target = NewTempDir();
            var src = new ZipAssetSource(zip);

            AndroidConfigExtractorCore.ExtractionResult r =
                AndroidConfigExtractorCore.EnsureExtracted(src, target, "1.0");
            Assert.Equal(AndroidConfigExtractorCore.ExtractionResult.Extracted, r);

            // Resolve exactly as Core does: {BaseDirectory}/config/config.xml.
            Assert.True(File.Exists(Path.Combine(target, "config", "config.xml")));
            Assert.Equal("FOO", File.ReadAllText(Path.Combine(target, "config", "data", "foo.txt")));
            Assert.Equal("JA", File.ReadAllText(Path.Combine(target, "config", "translate", "ja.txt")));

            // Idempotent second call = skip.
            using ZipArchive zip2 = BuildZip(("config/config.xml", "<config/>"), ("config/data/foo.txt", "FOO"), ("config/translate/ja.txt", "JA"));
            Assert.Equal(AndroidConfigExtractorCore.ExtractionResult.SkippedUpToDate,
                AndroidConfigExtractorCore.EnsureExtracted(new ZipAssetSource(zip2), target, "1.0"));
        }
    }
}
