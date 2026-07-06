using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FEBuilderGBA;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    /// <summary>
    /// Desktop unit tests for the pure <see cref="DirectoryAssetSource"/> (#1859) — the iOS
    /// bundle-directory counterpart of the Android <c>AssetManager</c> adapter. These verify
    /// the exact enumeration/open contract the iOS head relies on, and that it drives
    /// <see cref="AndroidConfigExtractorCore.EnsureExtracted"/> to a correct extraction,
    /// entirely on a desktop host with a synthetic directory (no Foundation dependency).
    /// </summary>
    public class DirectoryAssetSourceTests
    {
        static string NewTempDir()
        {
            string d = Path.Combine(Path.GetTempPath(), "feb_diras_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(d);
            return d;
        }

        static void Write(string root, string rel, string content)
        {
            string p = Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(p)!);
            File.WriteAllText(p, content);
        }

        [Fact]
        public void Enumerate_ReturnsPosixPathsRootedAtRoot_WithSubfolderPrefix()
        {
            string root = NewTempDir();
            Write(root, "config/data/foo.txt", "FOO");
            Write(root, "config/translate/ja.txt", "JA");
            // A sibling that is NOT under the subfolder must be excluded.
            Write(root, "other/ignore.txt", "IGNORE");

            var src = new DirectoryAssetSource(root, "config");
            List<string> files = src.EnumerateAssetFiles().OrderBy(p => p, StringComparer.Ordinal).ToList();

            Assert.Equal(new[] { "config/data/foo.txt", "config/translate/ja.txt" }, files);
            // POSIX separators only — never a backslash, even on Windows.
            Assert.All(files, f => Assert.DoesNotContain('\\', f));
        }

        [Fact]
        public void OpenAsset_ReturnsFileContent()
        {
            string root = NewTempDir();
            Write(root, "config/data/foo.txt", "HELLO-WORLD");
            var src = new DirectoryAssetSource(root, "config");

            using Stream s = src.OpenAsset("config/data/foo.txt");
            using var reader = new StreamReader(s);
            Assert.Equal("HELLO-WORLD", reader.ReadToEnd());
        }

        [Fact]
        public void Enumerate_MissingSubfolder_ReturnsEmpty()
        {
            string root = NewTempDir();
            var src = new DirectoryAssetSource(root, "config");
            Assert.Empty(src.EnumerateAssetFiles());
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void Ctor_RejectsEmptyArgs(string? bad)
        {
            Assert.Throws<ArgumentException>(() => new DirectoryAssetSource(bad!, "config"));
            Assert.Throws<ArgumentException>(() => new DirectoryAssetSource("root", bad!));
        }

        [Theory]
        [InlineData("")]
        [InlineData("../escape.txt")]
        [InlineData("config/../../escape.txt")]
        [InlineData("config/../../../etc/passwd")]
        public void OpenAsset_RejectsEmptyOrTraversal(string evil)
        {
            string root = NewTempDir();
            var src = new DirectoryAssetSource(root, "config");
            Assert.Throws<ArgumentException>(() => src.OpenAsset(evil));
        }

        [Fact]
        public void OpenAsset_RejectsRootedPath()
        {
            string root = NewTempDir();
            var src = new DirectoryAssetSource(root, "config");
            string rooted = Path.Combine(Path.GetTempPath(), "outside.txt");
            Assert.Throws<ArgumentException>(() => src.OpenAsset(rooted));
        }

        [Fact]
        public void OpenAsset_RejectsPathInsideRootButOutsideSubfolder()
        {
            // A path that stays under _rootDir but escapes the configured subtree must still
            // be rejected — the source is scoped to "config", not the whole root (#1860 review).
            string root = NewTempDir();
            Write(root, "other/secret.txt", "SECRET");
            var src = new DirectoryAssetSource(root, "config");
            Assert.Throws<ArgumentException>(() => src.OpenAsset("other/secret.txt"));
        }

        [Fact]
        public void DrivesEnsureExtracted_EndToEnd()
        {
            // Simulate the iOS flow: a read-only "bundle" dir with config/ under it,
            // extracted via the shared extractor into a separate app-private target.
            string bundle = NewTempDir();
            Write(bundle, "config/data/foo.txt", "FOO");
            Write(bundle, "config/data/bar.txt", "BAR");
            Write(bundle, "config/translate/ja.txt", "JA");

            string target = NewTempDir();
            var src = new DirectoryAssetSource(bundle, "config");

            AndroidConfigExtractorCore.ExtractionResult r1 =
                AndroidConfigExtractorCore.EnsureExtracted(src, target, "1.0");
            Assert.Equal(AndroidConfigExtractorCore.ExtractionResult.Extracted, r1);

            Assert.Equal("FOO", File.ReadAllText(Path.Combine(target, "config", "data", "foo.txt")));
            Assert.Equal("BAR", File.ReadAllText(Path.Combine(target, "config", "data", "bar.txt")));
            Assert.Equal("JA", File.ReadAllText(Path.Combine(target, "config", "translate", "ja.txt")));

            // Second call with the same version is a no-op skip.
            AndroidConfigExtractorCore.ExtractionResult r2 =
                AndroidConfigExtractorCore.EnsureExtracted(src, target, "1.0");
            Assert.Equal(AndroidConfigExtractorCore.ExtractionResult.SkippedUpToDate, r2);
        }
    }
}
