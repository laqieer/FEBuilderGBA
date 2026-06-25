#nullable enable annotations
using System;
using System.Collections.Generic;
using System.IO;
using Xunit;
using FEBuilderGBA;

namespace FEBuilderGBA.Core.Tests
{
    /// <summary>
    /// Offline tests for the work-support download/apply-UPS pipeline (#1454). All
    /// network/archive/ROM touches are injected, so these run without a network.
    /// </summary>
    public class WorkSupportUpdateDownloadCoreTests : IDisposable
    {
        readonly string _root;

        public WorkSupportUpdateDownloadCoreTests()
        {
            _root = Path.Combine(Path.GetTempPath(), "fe_wsdl_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
        }

        public void Dispose()
        {
            try { if (Directory.Exists(_root)) Directory.Delete(_root, true); } catch { }
        }

        static Dictionary<string, string> Lines(params (string, string)[] kv)
        {
            var d = new Dictionary<string, string>();
            foreach (var (k, v) in kv) d[k] = v;
            return d;
        }

        // ---- ResolveDownloadUrl ----

        [Fact]
        public void Resolve_MissingUpdateRegex_ReturnsMissing()
        {
            var r = WorkSupportUpdateDownloadCore.ResolveDownloadUrl(
                Lines(("UPDATE_URL", "http://x")), _ => "");
            Assert.Equal(WorkSupportUpdateDownloadCore.ResolveStatus.MissingUpdateRegex, r.Status);
        }

        [Fact]
        public void Resolve_NullLines_ReturnsMissing()
        {
            var r = WorkSupportUpdateDownloadCore.ResolveDownloadUrl(null, _ => "");
            Assert.Equal(WorkSupportUpdateDownloadCore.ResolveStatus.MissingUpdateRegex, r.Status);
        }

        [Fact]
        public void Resolve_DirectUrl_UsesUpdateUrlVerbatim()
        {
            var r = WorkSupportUpdateDownloadCore.ResolveDownloadUrl(
                Lines(("UPDATE_URL", "http://cdn/build.ups"), ("UPDATE_REGEX", "@DIRECT_URL")),
                _ => throw new Exception("must not GET on @DIRECT_URL"));
            Assert.Equal(WorkSupportUpdateDownloadCore.ResolveStatus.Ok, r.Status);
            Assert.Equal("http://cdn/build.ups", r.Url);
        }

        [Fact]
        public void Resolve_DirectUrl_EmptyUpdateUrl_FallsBackToCheckUrl()
        {
            // UPDATE_URL empty => @CHECK_URL placeholder; @DIRECT_URL => use CHECK_URL.
            var r = WorkSupportUpdateDownloadCore.ResolveDownloadUrl(
                Lines(("CHECK_URL", "http://cdn/check.ups"), ("UPDATE_REGEX", "@DIRECT_URL")),
                _ => "");
            Assert.Equal(WorkSupportUpdateDownloadCore.ResolveStatus.Ok, r.Status);
            Assert.Equal("http://cdn/check.ups", r.Url);
        }

        [Fact]
        public void Resolve_AtCheckUrl_ReturnsCheckUrl()
        {
            // UPDATE_URL empty => @CHECK_URL; a normal UPDATE_REGEX => use CHECK_URL listing as-is.
            var r = WorkSupportUpdateDownloadCore.ResolveDownloadUrl(
                Lines(("CHECK_URL", "http://list/page"), ("UPDATE_REGEX", @"href=(\S+)")),
                _ => throw new Exception("must not GET when url resolves to @CHECK_URL"));
            Assert.Equal(WorkSupportUpdateDownloadCore.ResolveStatus.Ok, r.Status);
            Assert.Equal("http://list/page", r.Url);
        }

        [Fact]
        public void Resolve_RegexScrape_ExtractsGroupOne()
        {
            var r = WorkSupportUpdateDownloadCore.ResolveDownloadUrl(
                Lines(("UPDATE_URL", "http://list"), ("UPDATE_REGEX", @"href=""(http://cdn[^""]+)""")),
                _ => "<a href=\"http://cdn/build.ups\">dl</a>");
            Assert.Equal(WorkSupportUpdateDownloadCore.ResolveStatus.Ok, r.Status);
            Assert.Equal("http://cdn/build.ups", r.Url);
        }

        [Fact]
        public void Resolve_RegexNoMatch_ReturnsRegexNoMatch()
        {
            var r = WorkSupportUpdateDownloadCore.ResolveDownloadUrl(
                Lines(("UPDATE_URL", "http://list"), ("UPDATE_REGEX", @"href=""(http://cdn[^""]+)""")),
                _ => "nothing here");
            Assert.Equal(WorkSupportUpdateDownloadCore.ResolveStatus.RegexNoMatch, r.Status);
        }

        [Fact]
        public void Resolve_HttpThrows_ReturnsHttpError()
        {
            var r = WorkSupportUpdateDownloadCore.ResolveDownloadUrl(
                Lines(("UPDATE_URL", "http://list"), ("UPDATE_REGEX", @"href=(\S+)")),
                _ => throw new Exception("network down"));
            Assert.Equal(WorkSupportUpdateDownloadCore.ResolveStatus.HttpError, r.Status);
        }

        [Fact]
        public void EscapeURLToDecode_UnescapesJsonSlashes()
        {
            Assert.Equal("http://x/y", WorkSupportUpdateDownloadCore.EscapeURLToDecode(@"http:\/\/x/y"));
            Assert.Equal("plain", WorkSupportUpdateDownloadCore.EscapeURLToDecode("plain"));
        }

        [Fact]
        public void RecommendUPSName_PrefersUrlFilenameWhenUps()
        {
            Assert.Equal("build.ups",
                WorkSupportUpdateDownloadCore.RecommendUPSName("http://cdn/build.ups", "myrom.gba"));
            Assert.Equal("myrom.ups",
                WorkSupportUpdateDownloadCore.RecommendUPSName("http://cdn/download?id=7", "myrom.gba"));
        }

        // ---- DownloadAndStage ----

        static byte[] MakeUpsBytes()
        {
            // A real UPS1 patch (src -> dst) big enough to clear the 256-byte floor.
            byte[] src = new byte[512];
            byte[] dst = new byte[512];
            for (int i = 0; i < dst.Length; i++) dst[i] = (byte)(i & 0xFF);
            return UPSUtilCore.MakeUPSData(src, dst);
        }

        [Fact]
        public void Stage_RawUps_CopiedIntoRomDir()
        {
            byte[] ups = MakeUpsBytes();
            string romDir = Path.Combine(_root, "romdir");
            Directory.CreateDirectory(romDir);

            var r = WorkSupportUpdateDownloadCore.DownloadAndStage(
                "http://cdn/build.ups", romDir, Path.Combine(romDir, "myrom.gba"),
                downloadFile: (url, dest) => { File.WriteAllBytes(dest, ups); return (true, ""); },
                extract: (a, d) => "must-not-extract-a-ups");

            Assert.Equal(WorkSupportUpdateDownloadCore.StageStatus.Ok, r.Status);
            Assert.Single(r.UpsFiles);
            Assert.True(File.Exists(r.UpsFiles[0]));
            Assert.Equal("build.ups", Path.GetFileName(r.UpsFiles[0]));
        }

        [Fact]
        public void Stage_Archive_ExtractsAndTrimsSingleWrapperDir()
        {
            byte[] ups = MakeUpsBytes();
            string romDir = Path.Combine(_root, "romdir2");
            Directory.CreateDirectory(romDir);

            // download writes a non-UPS blob; extractor simulates an archive that
            // expands into a single wrapper directory containing one .ups.
            var r = WorkSupportUpdateDownloadCore.DownloadAndStage(
                "http://cdn/pack.zip", romDir, Path.Combine(romDir, "myrom.gba"),
                downloadFile: (url, dest) => { File.WriteAllBytes(dest, new byte[1024]); return (true, ""); },
                extract: (archive, destDir) =>
                {
                    string wrapper = Path.Combine(destDir, "Release_1.0");
                    Directory.CreateDirectory(wrapper);
                    File.WriteAllBytes(Path.Combine(wrapper, "patch.ups"), ups);
                    return ""; // success
                });

            Assert.Equal(WorkSupportUpdateDownloadCore.StageStatus.Ok, r.Status);
            Assert.Single(r.UpsFiles);
            // CopyDirectory1Trim must have copied the INNER dir's contents (no wrapper folder).
            Assert.True(File.Exists(Path.Combine(romDir, "patch.ups")));
        }

        [Fact]
        public void Stage_DownloadFails_ReturnsDownloadFailed()
        {
            string romDir = Path.Combine(_root, "romdir3");
            Directory.CreateDirectory(romDir);
            var r = WorkSupportUpdateDownloadCore.DownloadAndStage(
                "http://x", romDir, Path.Combine(romDir, "r.gba"),
                downloadFile: (u, d) => (false, "404"),
                extract: (a, d) => "");
            Assert.Equal(WorkSupportUpdateDownloadCore.StageStatus.DownloadFailed, r.Status);
            Assert.Equal("404", r.Error);
        }

        [Fact]
        public void Stage_DownloadTooSmall_ReturnsTooSmall()
        {
            string romDir = Path.Combine(_root, "romdir4");
            Directory.CreateDirectory(romDir);
            var r = WorkSupportUpdateDownloadCore.DownloadAndStage(
                "http://x", romDir, Path.Combine(romDir, "r.gba"),
                downloadFile: (u, d) => { File.WriteAllBytes(d, new byte[10]); return (true, ""); },
                extract: (a, d) => "");
            Assert.Equal(WorkSupportUpdateDownloadCore.StageStatus.DownloadTooSmall, r.Status);
        }

        [Fact]
        public void Stage_ExtractFails_ReturnsExtractFailed()
        {
            string romDir = Path.Combine(_root, "romdir5");
            Directory.CreateDirectory(romDir);
            var r = WorkSupportUpdateDownloadCore.DownloadAndStage(
                "http://x", romDir, Path.Combine(romDir, "r.gba"),
                downloadFile: (u, d) => { File.WriteAllBytes(d, new byte[1024]); return (true, ""); },
                extract: (a, d) => "bad archive");
            Assert.Equal(WorkSupportUpdateDownloadCore.StageStatus.ExtractFailed, r.Status);
        }

        [Fact]
        public void Stage_NoUps_ReturnsNoUpsFound()
        {
            string romDir = Path.Combine(_root, "romdir6");
            Directory.CreateDirectory(romDir);
            var r = WorkSupportUpdateDownloadCore.DownloadAndStage(
                "http://x/pack.zip", romDir, Path.Combine(romDir, "r.gba"),
                downloadFile: (u, d) => { File.WriteAllBytes(d, new byte[1024]); return (true, ""); },
                extract: (a, d) => { File.WriteAllText(Path.Combine(d, "readme.txt"), "no ups"); return ""; });
            Assert.Equal(WorkSupportUpdateDownloadCore.StageStatus.NoUpsFound, r.Status);
        }

        // ---- ApplyUpsAgainstOriginal ----

        [Fact]
        public void Apply_RoundTrip_WritesPatchedGba()
        {
            // original -> modified via a real UPS; applying it back must reproduce modified.
            byte[] original = new byte[512];
            byte[] modified = new byte[512];
            for (int i = 0; i < modified.Length; i++) modified[i] = (byte)((i * 7) & 0xFF);

            string origPath = Path.Combine(_root, "vanilla.gba");
            File.WriteAllBytes(origPath, original);
            string upsPath = Path.Combine(_root, "patch.ups");
            File.WriteAllBytes(upsPath, UPSUtilCore.MakeUPSData(original, modified));

            var r = WorkSupportUpdateDownloadCore.ApplyUpsAgainstOriginal(
                new List<string> { upsPath }, origPath,
                applyOne: (orig, ups) =>
                {
                    byte[] patch = File.ReadAllBytes(ups);
                    byte[] outb = UPSUtilCore.ApplyUPS(orig, patch, out string msg);
                    return (outb, outb == null ? msg : "", outb != null ? msg : "");
                });

            Assert.Equal(WorkSupportUpdateDownloadCore.ApplyStatus.Ok, r.Status);
            Assert.Single(r.SavedRoms);
            byte[] saved = File.ReadAllBytes(r.SavedRoms[0]);
            Assert.Equal(modified, saved);
            Assert.Empty(r.Warnings); // clean CRC round-trip
        }

        [Fact]
        public void Apply_NoOriginal_ReturnsNoOriginal()
        {
            var r = WorkSupportUpdateDownloadCore.ApplyUpsAgainstOriginal(
                new List<string> { "x.ups" }, Path.Combine(_root, "missing.gba"),
                (o, u) => (new byte[1], "", ""));
            Assert.Equal(WorkSupportUpdateDownloadCore.ApplyStatus.NoOriginal, r.Status);
        }

        [Fact]
        public void Apply_HardErrorOnSecond_WritesNothing()
        {
            // validate-all-before-write: a failure on the 2nd UPS must leave NO .gba on disk.
            byte[] original = new byte[16];
            string origPath = Path.Combine(_root, "v2.gba");
            File.WriteAllBytes(origPath, original);

            string ups1 = Path.Combine(_root, "a.ups");
            string ups2 = Path.Combine(_root, "b.ups");
            File.WriteAllText(ups1, "x");
            File.WriteAllText(ups2, "x");

            var r = WorkSupportUpdateDownloadCore.ApplyUpsAgainstOriginal(
                new List<string> { ups1, ups2 }, origPath,
                applyOne: (orig, ups) => ups.EndsWith("a.ups")
                    ? (new byte[16], "", "")
                    : (null, "boom", ""));

            Assert.Equal(WorkSupportUpdateDownloadCore.ApplyStatus.ApplyFailed, r.Status);
            // NO partial output: a.gba must NOT have been written.
            Assert.False(File.Exists(Path.ChangeExtension(ups1, ".gba")));
            Assert.False(File.Exists(Path.ChangeExtension(ups2, ".gba")));
        }

        [Fact]
        public void Apply_Warning_CollectedNotFlattened()
        {
            byte[] original = new byte[16];
            string origPath = Path.Combine(_root, "v3.gba");
            File.WriteAllBytes(origPath, original);
            string ups = Path.Combine(_root, "w.ups");
            File.WriteAllText(ups, "x");

            var r = WorkSupportUpdateDownloadCore.ApplyUpsAgainstOriginal(
                new List<string> { ups }, origPath,
                applyOne: (orig, u) => (new byte[16], "", "CRC mismatch (non-fatal)"));

            Assert.Equal(WorkSupportUpdateDownloadCore.ApplyStatus.Ok, r.Status);
            Assert.Single(r.Warnings);
            Assert.Contains("CRC mismatch", r.Warnings[0]);
            Assert.True(File.Exists(r.SavedRoms[0]));
        }

        // ---- two-phase PrepareApply / CommitApply (Copilot review #2/#3) ----

        [Fact]
        public void PrepareApply_WithWarning_WritesNothing_UntilCommit()
        {
            // The whole point: warnings are surfaced BEFORE any .gba is written, so the
            // host can prompt and decline. PrepareApply must not touch the filesystem.
            byte[] original = new byte[16];
            string origPath = Path.Combine(_root, "p1_vanilla.gba");
            File.WriteAllBytes(origPath, original);
            string ups = Path.Combine(_root, "p1_patch.ups");
            File.WriteAllText(ups, "x");

            var prep = WorkSupportUpdateDownloadCore.PrepareApply(
                new List<string> { ups }, origPath,
                applyOne: (orig, u) => (new byte[16], "", "CRC mismatch"));

            Assert.Equal(WorkSupportUpdateDownloadCore.ApplyStatus.Ok, prep.Status);
            Assert.Single(prep.Warnings);
            Assert.Single(prep.Staged);
            // DECLINE: never call CommitApply -> NO .gba on disk.
            Assert.False(File.Exists(Path.ChangeExtension(ups, ".gba")));

            // ACCEPT: commit now writes it.
            var apply = WorkSupportUpdateDownloadCore.CommitApply(prep);
            Assert.Equal(WorkSupportUpdateDownloadCore.ApplyStatus.Ok, apply.Status);
            Assert.True(File.Exists(Path.ChangeExtension(ups, ".gba")));
            Assert.Single(apply.Warnings);
        }

        [Fact]
        public void PrepareApply_HardError_WritesNothing()
        {
            byte[] original = new byte[16];
            string origPath = Path.Combine(_root, "p2.gba");
            File.WriteAllBytes(origPath, original);
            string ups1 = Path.Combine(_root, "p2a.ups");
            string ups2 = Path.Combine(_root, "p2b.ups");
            File.WriteAllText(ups1, "x");
            File.WriteAllText(ups2, "x");

            var prep = WorkSupportUpdateDownloadCore.PrepareApply(
                new List<string> { ups1, ups2 }, origPath,
                applyOne: (orig, u) => u.EndsWith("p2a.ups") ? (new byte[16], "", "") : (null, "boom", ""));

            Assert.Equal(WorkSupportUpdateDownloadCore.ApplyStatus.ApplyFailed, prep.Status);
            Assert.False(File.Exists(Path.ChangeExtension(ups1, ".gba")));
            Assert.False(File.Exists(Path.ChangeExtension(ups2, ".gba")));
        }

        [Fact]
        public void CommitApply_SaveFailureOnSecond_RollsBackFirst()
        {
            // Make the SECOND target unwritable by pre-creating it as a DIRECTORY, so
            // File.Move-into-place throws. The first .gba (already written) must be
            // rolled back -> no partial output.
            byte[] original = new byte[16];
            string origPath = Path.Combine(_root, "r1.gba");
            File.WriteAllBytes(origPath, original);

            string ups1 = Path.Combine(_root, "r1a.ups");
            string ups2 = Path.Combine(_root, "r1b.ups");
            File.WriteAllText(ups1, "x");
            File.WriteAllText(ups2, "x");

            string target1 = Path.ChangeExtension(ups1, ".gba");
            string target2 = Path.ChangeExtension(ups2, ".gba");
            // Block target2 with a directory of the same name.
            Directory.CreateDirectory(target2);

            var prep = WorkSupportUpdateDownloadCore.PrepareApply(
                new List<string> { ups1, ups2 }, origPath,
                applyOne: (orig, u) => (new byte[16], "", ""));
            Assert.Equal(WorkSupportUpdateDownloadCore.ApplyStatus.Ok, prep.Status);

            var apply = WorkSupportUpdateDownloadCore.CommitApply(prep);

            Assert.Equal(WorkSupportUpdateDownloadCore.ApplyStatus.SaveFailed, apply.Status);
            // ROLLBACK: the first target must NOT remain as a written file.
            Assert.False(File.Exists(target1), "first .gba must be rolled back on a later save failure");
        }

        [Fact]
        public void CommitApply_RestoresDisplacedOriginalOnFailure()
        {
            // target1 pre-exists with sentinel bytes; target2 is blocked (directory).
            // After the failed commit, target1's ORIGINAL content must be restored.
            byte[] original = new byte[16];
            string origPath = Path.Combine(_root, "r2.gba");
            File.WriteAllBytes(origPath, original);

            string ups1 = Path.Combine(_root, "r2a.ups");
            string ups2 = Path.Combine(_root, "r2b.ups");
            File.WriteAllText(ups1, "x");
            File.WriteAllText(ups2, "x");

            string target1 = Path.ChangeExtension(ups1, ".gba");
            string target2 = Path.ChangeExtension(ups2, ".gba");
            byte[] sentinel = new byte[] { 1, 2, 3, 4, 5 };
            File.WriteAllBytes(target1, sentinel);    // pre-existing target1
            Directory.CreateDirectory(target2);        // block target2

            var prep = WorkSupportUpdateDownloadCore.PrepareApply(
                new List<string> { ups1, ups2 }, origPath,
                applyOne: (orig, u) => (new byte[16], "", ""));
            var apply = WorkSupportUpdateDownloadCore.CommitApply(prep);

            Assert.Equal(WorkSupportUpdateDownloadCore.ApplyStatus.SaveFailed, apply.Status);
            Assert.True(File.Exists(target1));
            Assert.Equal(sentinel, File.ReadAllBytes(target1)); // original restored
        }
    }
}
