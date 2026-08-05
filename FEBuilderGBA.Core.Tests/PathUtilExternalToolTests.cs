// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.IO;

namespace FEBuilderGBA.Core.Tests
{
    public sealed class PathUtilExternalToolTests : IDisposable
    {
        readonly string _root = Path.Combine(
            Path.GetTempPath(),
            "external_tool_bundle_" + Guid.NewGuid().ToString("N"));

        public PathUtilExternalToolTests()
        {
            Directory.CreateDirectory(_root);
        }

        public void Dispose()
        {
            try { Directory.Delete(_root, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        [Fact]
        public void BundleNameExecutable_ResolvesDirectFile()
        {
            string bundle = CreateBundle("mGBA.app", "mGBA");

            bool ok = PathUtil.TryResolveExternalToolExecutable(
                bundle, isMacOS: true, _ => true, out string resolved);

            Assert.True(ok);
            Assert.Equal(Path.Combine(bundle, "Contents", "MacOS", "mGBA"), resolved);
        }

        [Fact]
        public void SoleSafeExecutable_ResolvesWhenNameDiffers()
        {
            string bundle = CreateBundle("VisualBoyAdvance.app", "vbam");

            bool ok = PathUtil.TryResolveExternalToolExecutable(
                bundle, isMacOS: true, _ => true, out string resolved);

            Assert.True(ok);
            Assert.EndsWith(Path.Combine("Contents", "MacOS", "vbam"), resolved);
        }

        [Fact]
        public void AmbiguousBundle_IsRejected()
        {
            string bundle = CreateBundle("Tools.app", "first", "second");

            bool ok = PathUtil.TryResolveExternalToolExecutable(
                bundle, isMacOS: true, _ => true, out string resolved);

            Assert.False(ok);
            Assert.Equal(bundle, resolved);
        }

        [Fact]
        public void NonExecutableCandidate_IsRejected()
        {
            string bundle = CreateBundle("mGBA.app", "mGBA");

            bool ok = PathUtil.TryResolveExternalToolExecutable(
                bundle, isMacOS: true, _ => false, out _);

            Assert.False(ok);
        }

        [Fact]
        public void TrailingSeparatorAndUppercaseExtension_AreAccepted()
        {
            string bundle = CreateBundle("mGBA.APP", "mGBA");

            bool ok = PathUtil.TryResolveExternalToolExecutable(
                bundle + Path.DirectorySeparatorChar,
                isMacOS: true,
                _ => true,
                out string resolved);

            Assert.True(ok);
            Assert.EndsWith(Path.Combine("Contents", "MacOS", "mGBA"), resolved);
        }

        [Fact]
        public void AlreadyResolvedFile_IsUnchanged()
        {
            string bundle = CreateBundle("mGBA.app", "mGBA");
            string executable = Path.Combine(bundle, "Contents", "MacOS", "mGBA");

            bool ok = PathUtil.TryResolveExternalToolExecutable(
                executable, isMacOS: true, _ => false, out string resolved);

            Assert.True(ok);
            Assert.Equal(executable, resolved);
        }

        [Fact]
        public void NonMacBundle_RemainsInvalidDirectory()
        {
            string bundle = CreateBundle("mGBA.app", "mGBA");

            bool ok = PathUtil.TryResolveExternalToolExecutable(
                bundle, isMacOS: false, _ => true, out string resolved);

            Assert.False(ok);
            Assert.Equal(bundle, resolved);
        }

        [SkippableFact]
        public void EscapingExecutableSymlink_IsRejected()
        {
            string outside = Path.Combine(_root, "outside");
            File.WriteAllText(outside, "not executed");
            string bundle = CreateEmptyBundle("mGBA.app");
            string link = Path.Combine(bundle, "Contents", "MacOS", "mGBA");
            try
            {
                File.CreateSymbolicLink(link, outside);
            }
            catch (Exception ex) when (
                ex is PlatformNotSupportedException
                or UnauthorizedAccessException
                or IOException)
            {
                Skip.If(true, "Symbolic links are unavailable: " + ex.Message);
            }

            bool ok = PathUtil.TryResolveExternalToolExecutable(
                bundle, isMacOS: true, _ => true, out _);

            Assert.False(ok);
        }

        [SkippableFact]
        public void EscapingMacOSDirectorySymlink_IsRejected()
        {
            string outside = Path.Combine(_root, "outside-macos");
            Directory.CreateDirectory(outside);
            File.WriteAllText(Path.Combine(outside, "mGBA"), "not executed");
            string bundle = Path.Combine(_root, "mGBA.app");
            Directory.CreateDirectory(Path.Combine(bundle, "Contents"));
            CreateDirectorySymlinkOrSkip(
                Path.Combine(bundle, "Contents", "MacOS"),
                outside);

            bool ok = PathUtil.TryResolveExternalToolExecutable(
                bundle, isMacOS: true, _ => true, out _);

            Assert.False(ok);
        }

        [SkippableFact]
        public void EscapingContentsDirectorySymlink_IsRejected()
        {
            string outside = Path.Combine(_root, "outside-contents");
            Directory.CreateDirectory(Path.Combine(outside, "MacOS"));
            File.WriteAllText(Path.Combine(outside, "MacOS", "mGBA"), "not executed");
            string bundle = Path.Combine(_root, "mGBA.app");
            Directory.CreateDirectory(bundle);
            CreateDirectorySymlinkOrSkip(
                Path.Combine(bundle, "Contents"),
                outside);

            bool ok = PathUtil.TryResolveExternalToolExecutable(
                bundle, isMacOS: true, _ => true, out _);

            Assert.False(ok);
        }

        [SkippableFact]
        public void PublicResolver_OnMac_RequiresAndAcceptsExecutableBit()
        {
            Skip.IfNot(PathUtil.IsMacOS, "macOS-only public resolver proof");
            string bundle = CreateBundle("mGBA.app", "mGBA");
            string executable = Path.Combine(bundle, "Contents", "MacOS", "mGBA");
            File.SetUnixFileMode(
                executable,
                UnixFileMode.UserRead
                | UnixFileMode.UserWrite
                | UnixFileMode.UserExecute);

            Assert.True(PathUtil.TryResolveExternalToolExecutable(
                bundle,
                out string resolved));
            Assert.Equal(executable, resolved);
        }

        string CreateBundle(string bundleName, params string[] files)
        {
            string bundle = CreateEmptyBundle(bundleName);
            string macOS = Path.Combine(bundle, "Contents", "MacOS");
            foreach (string file in files)
                File.WriteAllText(Path.Combine(macOS, file), "not executed");
            return bundle;
        }

        string CreateEmptyBundle(string bundleName)
        {
            string bundle = Path.Combine(_root, bundleName);
            Directory.CreateDirectory(Path.Combine(bundle, "Contents", "MacOS"));
            return bundle;
        }

        static void CreateDirectorySymlinkOrSkip(string link, string target)
        {
            try
            {
                Directory.CreateSymbolicLink(link, target);
            }
            catch (Exception ex) when (
                ex is PlatformNotSupportedException
                or UnauthorizedAccessException
                or IOException)
            {
                Skip.If(true, "Directory symbolic links are unavailable: " + ex.Message);
            }
        }
    }
}
