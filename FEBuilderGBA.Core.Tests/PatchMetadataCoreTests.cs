using Xunit;
using FEBuilderGBA;
using System.IO;
using System.Linq;

namespace FEBuilderGBA.Core.Tests
{
    [Collection("SharedState")]
    public class PatchMetadataCoreTests : IDisposable
    {
        readonly ROM? _savedRom;
        readonly string? _savedLang;

        public PatchMetadataCoreTests()
        {
            _savedRom = CoreState.ROM;
            _savedLang = CoreState.Language;
        }

        public void Dispose()
        {
            CoreState.ROM = _savedRom;
            CoreState.Language = _savedLang;
        }

        [Fact]
        public void ParseByteArray_SimpleHex_ParsesCorrectly()
        {
            byte[] result = PatchMetadataCore.ParseByteArray("0x10 0x00 0xAB 0xFF");
            Assert.Equal(4, result.Length);
            Assert.Equal(0x10, result[0]);
            Assert.Equal(0x00, result[1]);
            Assert.Equal(0xAB, result[2]);
            Assert.Equal(0xFF, result[3]);
        }

        [Fact]
        public void ParseByteArray_EmptyString_ReturnsEmpty()
        {
            byte[] result = PatchMetadataCore.ParseByteArray("");
            Assert.Empty(result);
        }

        [Fact]
        public void ParseByteArray_StopsAtNonHex()
        {
            byte[] result = PatchMetadataCore.ParseByteArray("0x10 0x20 NOTAHEX 0x30");
            Assert.Equal(2, result.Length);
            Assert.Equal(0x10, result[0]);
            Assert.Equal(0x20, result[1]);
        }

        [Fact]
        public void CleanDescription_ReplacesEscapes()
        {
            string result = PatchMetadataCore.CleanDescription("Line1\\r\\nLine2\\nLine3  ");
            Assert.Equal("Line1\nLine2\nLine3", result);
        }

        [Fact]
        public void CheckPatchInstalled_GrepPatternPresent_Installed()
        {
            // #1919: a $GREP condition whose pattern is present in the ROM (4-aligned)
            // now resolves to Installed instead of Unknown.
            byte[] data = new byte[0x1000];
            data[0x200] = 0xAB; data[0x201] = 0xCD; data[0x202] = 0xEF; data[0x203] = 0x12;
            var rom = new ROM();
            rom.SwapNewROMDataDirect(data);

            var status = PatchMetadataCore.CheckPatchInstalled(
                "$GREP4 0xAB 0xCD 0xEF 0x12=0xAB 0xCD 0xEF 0x12", rom);
            Assert.Equal(PatchMetadataCore.PatchStatus.Installed, status);
        }

        [Fact]
        public void CheckPatchInstalled_GrepPatternAbsent_NotInstalled()
        {
            byte[] data = new byte[0x1000]; // pattern never planted
            var rom = new ROM();
            rom.SwapNewROMDataDirect(data);

            var status = PatchMetadataCore.CheckPatchInstalled(
                "$GREP4 0xAB 0xCD 0xEF 0x12=0xAB 0xCD 0xEF 0x12", rom);
            Assert.Equal(PatchMetadataCore.PatchStatus.NotInstalled, status);
        }

        [Fact]
        public void CheckPatchInstalled_GrepRespectsAlignment()
        {
            // Pattern planted at a NON-4-aligned offset: $GREP4 must NOT find it, but
            // $GREP1 (byte-aligned) must.
            byte[] data = new byte[0x1000];
            data[0x201] = 0xAB; data[0x202] = 0xCD; data[0x203] = 0xEF; data[0x204] = 0x12;
            var rom = new ROM();
            rom.SwapNewROMDataDirect(data);

            Assert.Equal(PatchMetadataCore.PatchStatus.NotInstalled,
                PatchMetadataCore.CheckPatchInstalled("$GREP4 0xAB 0xCD 0xEF 0x12=0xAB 0xCD 0xEF 0x12", rom));
            Assert.Equal(PatchMetadataCore.PatchStatus.Installed,
                PatchMetadataCore.CheckPatchInstalled("$GREP1 0xAB 0xCD 0xEF 0x12=0xAB 0xCD 0xEF 0x12", rom));
        }

        [Fact]
        public void CheckPatchInstalled_FGrep_WithPatternFile_Installed()
        {
            // #1919: FGREP reads its pattern from an external .bin relative to the patch
            // dir. Threading basedir through lets FGREP patches (the majority) resolve.
            string dir = Path.Combine(Path.GetTempPath(), "fe_fgrep_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                byte[] pattern = { 0xAB, 0xCD, 0xEF, 0x12 };
                File.WriteAllBytes(Path.Combine(dir, "sig.bin"), pattern);

                byte[] data = new byte[0x1000];
                pattern.CopyTo(data, 0x200); // planted, 4-aligned
                var rom = new ROM();
                rom.SwapNewROMDataDirect(data);

                var status = PatchMetadataCore.CheckPatchInstalled(
                    "$FGREP4 sig.bin=0xAB 0xCD 0xEF 0x12", rom, dir);
                Assert.Equal(PatchMetadataCore.PatchStatus.Installed, status);
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { /* best-effort cleanup */ }
            }
        }

        [Fact]
        public void CheckPatchInstalled_FGrep_MissingFile_NotInstalled()
        {
            // No basedir / missing pattern file -> the pattern can't be loaded -> the
            // patch is reported not installed (mirrors WinForms), never a false Installed.
            byte[] data = new byte[0x1000];
            var rom = new ROM();
            rom.SwapNewROMDataDirect(data);

            var status = PatchMetadataCore.CheckPatchInstalled("$FGREP4 missing.bin=0xAB", rom);
            Assert.Equal(PatchMetadataCore.PatchStatus.NotInstalled, status);
        }

        [Fact]
        public void CheckPatchInstalled_FGrep_LegacyTraversalPattern_Installed()
        {
            // #1936: the PUBLIC/legacy CheckPatchInstalled must PRESERVE file-backed $FGREP
            // resolution INCLUDING `..` traversal — 256 shipped patches legitimately use paths
            // like ../../FE7U/...; this path is deliberately NOT hardened. Only the exporter's
            // bounded metadata pass refuses file-backed markers.
            string root = Path.Combine(Path.GetTempPath(), "fe_fgrep_trav_" + Guid.NewGuid().ToString("N"));
            string sub = Path.Combine(root, "sub");
            Directory.CreateDirectory(sub);
            try
            {
                byte[] pattern = { 0xAB, 0xCD, 0xEF, 0x12 };
                File.WriteAllBytes(Path.Combine(root, "sig.bin"), pattern);

                byte[] data = new byte[0x1000];
                pattern.CopyTo(data, 0x200); // 4-aligned
                var rom = new ROM();
                rom.SwapNewROMDataDirect(data);

                // basedir = sub, marker references ../sig.bin -> resolves up into root.
                var status = PatchMetadataCore.CheckPatchInstalled(
                    "$FGREP4 ../sig.bin=0xAB 0xCD 0xEF 0x12", rom, sub);
                Assert.Equal(PatchMetadataCore.PatchStatus.Installed, status);
            }
            finally { try { Directory.Delete(root, true); } catch { /* best-effort */ } }
        }

        [Theory]
        [InlineData("relative")]
        [InlineData("traversal")]
        [InlineData("rooted")]
        public void TryParsePatchFileStrictBounded_FGrepMarker_ClassifiedUnknown_WhileLegacyInstalled(string form)
        {
            // #1936 bounded-exporter escape: a file-backed $FGREP install marker is classified
            // Unknown BEFORE the resolver/Path.Combine/File.Exists/File.ReadAllBytes ever run.
            // Every path form (relative, `..` traversal, rooted/absolute) is refused identically.
            // The SAME fixture resolves to Installed through the legacy unbounded parser, so the
            // Unknown result is the bounded refusal, not a vacuous non-match.
            string root = Path.Combine(Path.GetTempPath(), "fe_fgrep_bnd_" + Guid.NewGuid().ToString("N"));
            string patchDir = Path.Combine(root, "patch");
            Directory.CreateDirectory(patchDir);
            try
            {
                byte[] pattern = { 0xAB, 0xCD, 0xEF, 0x12 };
                string fileRef;
                switch (form)
                {
                    case "traversal":
                        File.WriteAllBytes(Path.Combine(root, "sig.bin"), pattern);
                        fileRef = "../sig.bin";
                        break;
                    case "rooted":
                        string abs = Path.Combine(root, "abs_sig.bin");
                        File.WriteAllBytes(abs, pattern);
                        fileRef = abs; // rooted -> Path.Combine ignores basedir
                        break;
                    default:
                        File.WriteAllBytes(Path.Combine(patchDir, "sig.bin"), pattern);
                        fileRef = "sig.bin";
                        break;
                }
                string patchFile = Path.Combine(patchDir, "PATCH_Fg.txt");
                File.WriteAllLines(patchFile, new[]
                {
                    "TYPE=ADDR",
                    "NAME=FGrep",
                    "PATCHED_IF:$FGREP4 " + fileRef + "=0xAB 0xCD 0xEF 0x12",
                });

                byte[] data = new byte[0x1000];
                pattern.CopyTo(data, 0x200);
                var rom = new ROM();
                rom.SwapNewROMDataDirect(data);

                // Non-vacuous: legacy unbounded path resolves the SAME fixture to Installed.
                var legacy = PatchMetadataCore.ParsePatchFile(patchFile, "FG", rom, "en");
                Assert.Equal(PatchMetadataCore.PatchStatus.Installed, legacy.Status);

                // Bounded exporter path refuses the file-backed marker -> Unknown.
                bool ok = PatchMetadataCore.TryParsePatchFileStrictBounded(
                    patchFile, "FG", rom, "en", 100, PatchMetadataCore.MaxPatchDefinitionBytes,
                    out var bounded, out long _);
                Assert.True(ok);
                Assert.Equal(PatchMetadataCore.PatchStatus.Unknown, bounded.Status);
            }
            finally { try { Directory.Delete(root, true); } catch { /* best-effort */ } }
        }

        [SkippableFact]
        public void TryParsePatchFileStrictBounded_FGrepMarker_FinalSymlinkTarget_ClassifiedUnknown()
        {
            // #1936: a file-backed $FGREP whose target is a SYMLINK. The legacy path follows the
            // link and matches (Installed); the bounded path never opens it (Unknown). Skipped
            // where symlink creation is unavailable.
            string root = Path.Combine(Path.GetTempPath(), "fe_fgrep_link_" + Guid.NewGuid().ToString("N"));
            string patchDir = Path.Combine(root, "patch");
            Directory.CreateDirectory(patchDir);
            try
            {
                byte[] pattern = { 0xAB, 0xCD, 0xEF, 0x12 };
                string realSig = Path.Combine(root, "real_sig.bin");
                File.WriteAllBytes(realSig, pattern);
                string linkSig = Path.Combine(patchDir, "sig.bin");
                try { File.CreateSymbolicLink(linkSig, realSig); }
                catch (Exception ex) { Skip.If(true, "Cannot create a file symlink here: " + ex.Message); return; }

                string patchFile = Path.Combine(patchDir, "PATCH_Fg.txt");
                File.WriteAllLines(patchFile, new[]
                {
                    "TYPE=ADDR",
                    "NAME=FGrep",
                    "PATCHED_IF:$FGREP4 sig.bin=0xAB 0xCD 0xEF 0x12",
                });

                byte[] data = new byte[0x1000];
                pattern.CopyTo(data, 0x200);
                var rom = new ROM();
                rom.SwapNewROMDataDirect(data);

                var legacy = PatchMetadataCore.ParsePatchFile(patchFile, "FG", rom, "en");
                Assert.Equal(PatchMetadataCore.PatchStatus.Installed, legacy.Status);

                bool ok = PatchMetadataCore.TryParsePatchFileStrictBounded(
                    patchFile, "FG", rom, "en", 100, PatchMetadataCore.MaxPatchDefinitionBytes,
                    out var bounded, out long _);
                Assert.True(ok);
                Assert.Equal(PatchMetadataCore.PatchStatus.Unknown, bounded.Status);
            }
            finally { try { Directory.Delete(root, true); } catch { /* best-effort */ } }
        }

        [Fact]
        public void TryParsePatchFileStrictBounded_FGrepMarker_OversizedTarget_ClassifiedUnknownWithoutReading()
        {
            // #1936: the file-backed $FGREP target is an OVERSIZED (16 MiB + 1) regular file.
            // The bounded parser classifies Unknown WITHOUT ever opening it, so the oversized
            // signature file is never read (a sparse SetLength keeps the fixture from
            // materializing real bytes; the legacy path would instead read the whole file).
            string root = Path.Combine(Path.GetTempPath(), "fe_fgrep_big_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                string sig = Path.Combine(root, "sig.bin");
                using (var fs = new FileStream(sig, FileMode.CreateNew, FileAccess.Write))
                    fs.SetLength(PatchMetadataCore.MaxPatchDefinitionBytes + 1); // sparse

                string patchFile = Path.Combine(root, "PATCH_Fg.txt");
                File.WriteAllLines(patchFile, new[]
                {
                    "TYPE=ADDR",
                    "NAME=FGrep",
                    "PATCHED_IF:$FGREP4 sig.bin=0xAB",
                });

                var rom = new ROM();
                rom.SwapNewROMDataDirect(new byte[0x1000]);

                bool ok = PatchMetadataCore.TryParsePatchFileStrictBounded(
                    patchFile, "FG", rom, "en", 100, PatchMetadataCore.MaxPatchDefinitionBytes,
                    out var bounded, out long _);
                Assert.True(ok);
                Assert.Equal(PatchMetadataCore.PatchStatus.Unknown, bounded.Status);
            }
            finally { try { Directory.Delete(root, true); } catch { /* best-effort */ } }
        }

        [Fact]
        public void TryParsePatchFileStrictBounded_NonFileBackedGrepMarker_StillResolvesInstalled()
        {
            // #1936 narrowness guard: the bounded escape refuses ONLY file-backed $FGREP. A
            // $GREP marker (no external file) still resolves normally through the bounded
            // parser, proving the Unknown results above are the FGREP refusal — not a blanket
            // "bounded parser can't classify install markers" regression.
            string root = Path.Combine(Path.GetTempPath(), "fe_grep_bnd_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                string patchFile = Path.Combine(root, "PATCH_Grep.txt");
                File.WriteAllLines(patchFile, new[]
                {
                    "TYPE=ADDR",
                    "NAME=Grep",
                    "PATCHED_IF:$GREP4 0xAB 0xCD 0xEF 0x12=0xAB 0xCD 0xEF 0x12",
                });

                byte[] data = new byte[0x1000];
                new byte[] { 0xAB, 0xCD, 0xEF, 0x12 }.CopyTo(data, 0x200);
                var rom = new ROM();
                rom.SwapNewROMDataDirect(data);

                bool ok = PatchMetadataCore.TryParsePatchFileStrictBounded(
                    patchFile, "Grep", rom, "en", 100, PatchMetadataCore.MaxPatchDefinitionBytes,
                    out var bounded, out long _);
                Assert.True(ok);
                Assert.Equal(PatchMetadataCore.PatchStatus.Installed, bounded.Status);
            }
            finally { try { Directory.Delete(root, true); } catch { /* best-effort */ } }
        }

        [Theory]
        [InlineData("PATCHED_IF")]
        [InlineData("PATCHED_IFNOT")]
        public void TryParsePatchFileStrictBounded_FGrepMarker_ResolverNeverInvoked(string markerKey)
        {
            // #1936 positive no-read proof: the absence of the signature body from serialized
            // output is a weak signal (even the legacy resolver only USED the bytes to classify
            // and never serialized them). This asserts the stronger property directly — for a
            // file-backed $FGREP marker the shared address resolver is NEVER invoked at all, so
            // no Path.Combine/File.Exists/File.ReadAllBytes on the external filename can occur.
            // The injected resolver THROWS if called; the marker must still classify Unknown.
            //
            // Both marker keys are covered on purpose:
            //   * PATCHED_IF    — the live path: proves the NEW pre-resolver bounded refusal
            //                     classifies file-backed $FGREP as Unknown before any resolver/I/O.
            //   * PATCHED_IFNOT — legacy-IGNORED today (PatchMetadataCore only parses lines that
            //                     start exactly "PATCHED_IF:", so this key never reaches a resolver
            //                     at all). We assert the same zero-calls/Unknown invariant to SEAL
            //                     it against a future regression that starts honoring PATCHED_IFNOT
            //                     without threading the pre-resolver $FGREP carve-out through it.
            string root = Path.Combine(Path.GetTempPath(), "fe_fgrep_noresolve_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                // A real signature file exists and WOULD match if the resolver ran. For the
                // PATCHED_IF row this makes the Unknown result a NON-VACUOUS pre-resolver refusal
                // (not a missing-file NotInstalled): the resolver, had it been reached, would have
                // matched and yielded Installed. This rationale applies ONLY to the PATCHED_IF row;
                // the PATCHED_IFNOT row is legacy-ignored (documented above) and its Unknown is the
                // default status with no marker parsed, independent of this fixture.
                byte[] pattern = { 0xAB, 0xCD, 0xEF, 0x12 };
                File.WriteAllBytes(Path.Combine(root, "sig.bin"), pattern);
                string patchFile = Path.Combine(root, "PATCH_Fg.txt");
                File.WriteAllLines(patchFile, new[]
                {
                    "TYPE=ADDR",
                    "NAME=FGrep",
                    markerKey + ":$FGREP4 sig.bin=0xAB 0xCD 0xEF 0x12",
                });

                byte[] data = new byte[0x1000];
                pattern.CopyTo(data, 0x200);
                var rom = new ROM();
                rom.SwapNewROMDataDirect(data);

                int calls = 0;
                PatchMetadataCore.MacroAddressResolver spy = (r, addr, basedir, start) =>
                {
                    calls++;
                    throw new InvalidOperationException(
                        "resolver MUST NOT be invoked for a bounded file-backed $FGREP marker: " + addr);
                };

                bool ok = PatchMetadataCore.TryParsePatchFileStrictBounded(
                    patchFile, "FG", rom, "en", 100, PatchMetadataCore.MaxPatchDefinitionBytes,
                    spy, out var bounded, out long _);

                Assert.True(ok);
                Assert.Equal(0, calls); // proven: resolver never reached
                Assert.Equal(PatchMetadataCore.PatchStatus.Unknown, bounded.Status);
            }
            finally { try { Directory.Delete(root, true); } catch { /* best-effort */ } }
        }

        [Fact]
        public void TryParsePatchFileStrictBounded_NonFileBackedGrepMarker_ResolverIsInvoked()
        {
            // #1936 non-vacuous companion to the no-read proof above: the SAME injected-resolver
            // seam is exercised with a $GREP (non-file-backed) marker and the resolver IS
            // invoked, returning a valid address so the marker classifies Installed. This proves
            // the seam/plumbing actually routes resolution through the injected delegate — i.e.
            // the "zero calls" assertion for $FGREP is a real refusal, not a dead code path that
            // never calls the resolver for anything.
            string root = Path.Combine(Path.GetTempPath(), "fe_grep_resolve_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                string patchFile = Path.Combine(root, "PATCH_Grep.txt");
                File.WriteAllLines(patchFile, new[]
                {
                    "TYPE=ADDR",
                    "NAME=Grep",
                    "PATCHED_IF:$GREP4 0xAB 0xCD 0xEF 0x12=0xAB 0xCD 0xEF 0x12",
                });

                byte[] data = new byte[0x1000];
                new byte[] { 0xAB, 0xCD, 0xEF, 0x12 }.CopyTo(data, 0x200);
                var rom = new ROM();
                rom.SwapNewROMDataDirect(data);

                int calls = 0;
                PatchMetadataCore.MacroAddressResolver spy = (r, addr, basedir, start) =>
                {
                    calls++;
                    Assert.StartsWith("$GREP", addr); // routed the non-file-backed macro here
                    return 0x200u; // valid address; expected bytes match at 0x200 -> Installed
                };

                bool ok = PatchMetadataCore.TryParsePatchFileStrictBounded(
                    patchFile, "Grep", rom, "en", 100, PatchMetadataCore.MaxPatchDefinitionBytes,
                    spy, out var bounded, out long _);

                Assert.True(ok);
                Assert.Equal(1, calls); // proven: seam routes real resolution through the delegate
                Assert.Equal(PatchMetadataCore.PatchStatus.Installed, bounded.Status);
            }
            finally { try { Directory.Delete(root, true); } catch { /* best-effort */ } }
        }

        [Fact]
        public void CheckPatchInstalled_GrepZeroAlignment_NoHang_NotInstalled()
        {
            // $GREP0 (zero alignment) must NOT infinite-loop U.Grep (blocksize step 0);
            // the resolver rejects it -> NotInstalled. Run under a 5s timeout so a
            // regressed guard fails the test bounded instead of hanging the whole run
            // (xUnit has no default per-test timeout) — #1919.
            byte[] data = new byte[0x1000];
            var rom = new ROM();
            rom.SwapNewROMDataDirect(data);

            var task = System.Threading.Tasks.Task.Run(() =>
                PatchMetadataCore.CheckPatchInstalled("$GREP0 0xAB=0xAB", rom));
            Assert.True(task.Wait(System.TimeSpan.FromSeconds(5)),
                "CheckPatchInstalled($GREP0) hung — zero-alignment guard missing");
            Assert.Equal(PatchMetadataCore.PatchStatus.NotInstalled, task.Result);
        }

        [Fact]
        public void CheckPatchInstalled_FixedAddr_Installed()
        {
            byte[] data = new byte[0x100];
            data[0x10] = 0xAB;
            data[0x11] = 0xCD;
            var rom = new ROM();
            rom.SwapNewROMDataDirect(data);

            var status = PatchMetadataCore.CheckPatchInstalled("0x10=0xAB 0xCD", rom);
            Assert.Equal(PatchMetadataCore.PatchStatus.Installed, status);
        }

        [Fact]
        public void CheckPatchInstalled_FixedAddr_NotInstalled()
        {
            byte[] data = new byte[0x100];
            data[0x10] = 0x00;
            data[0x11] = 0x00;
            var rom = new ROM();
            rom.SwapNewROMDataDirect(data);

            var status = PatchMetadataCore.CheckPatchInstalled("0x10=0xAB 0xCD", rom);
            Assert.Equal(PatchMetadataCore.PatchStatus.NotInstalled, status);
        }

        [Fact]
        public void CheckPatchInstalled_BareHexAddr_ParsedAsHex_Installed()
        {
            // Regression (#1919): patch metadata contains BARE hex addresses (no 0x),
            // e.g. real FE8J patches "PATCHED_IF:2C2F0=0x00 0x49 0x8F 0x46". These must
            // parse as HEX (0x2C2F0), not decimal (2). A decimal misparse would read
            // ROM[2] (0x00) and report NotInstalled.
            byte[] data = new byte[0x40000];
            data[0x2C2F0] = 0x00; data[0x2C2F1] = 0x49; data[0x2C2F2] = 0x8F; data[0x2C2F3] = 0x46;
            var rom = new ROM();
            rom.SwapNewROMDataDirect(data);

            Assert.Equal(PatchMetadataCore.PatchStatus.Installed,
                PatchMetadataCore.CheckPatchInstalled("2C2F0=0x00 0x49 0x8F 0x46", rom));
            // Same address, wrong expected bytes -> NotInstalled (still hex-parsed).
            Assert.Equal(PatchMetadataCore.PatchStatus.NotInstalled,
                PatchMetadataCore.CheckPatchInstalled("2C2F0=0xFF 0xFF 0xFF 0xFF", rom));
        }

        [Fact]
        public void CheckPatchInstalled_AddrBeyondRom_NotInstalled()
        {
            byte[] data = new byte[0x10];
            var rom = new ROM();
            rom.SwapNewROMDataDirect(data);

            var status = PatchMetadataCore.CheckPatchInstalled("0xFF=0xAB", rom);
            Assert.Equal(PatchMetadataCore.PatchStatus.NotInstalled, status);
        }

        [Fact]
        public void CheckPatchInstalled_NoEqualsSign_ReturnsUnknown()
        {
            byte[] data = new byte[0x100];
            var rom = new ROM();
            rom.SwapNewROMDataDirect(data);

            var status = PatchMetadataCore.CheckPatchInstalled("noequalssign", rom);
            Assert.Equal(PatchMetadataCore.PatchStatus.Unknown, status);
        }

        [Fact]
        public void GetLanguageSuffix_English_ReturnsEn()
        {
            CoreState.Language = "en";
            Assert.Equal("en", PatchMetadataCore.GetLanguageSuffix());
        }

        [Fact]
        public void GetLanguageSuffix_Chinese_ReturnsZh()
        {
            CoreState.Language = "zh";
            Assert.Equal("zh", PatchMetadataCore.GetLanguageSuffix());
        }

        [Fact]
        public void GetLanguageSuffix_Japanese_ReturnsEmpty()
        {
            CoreState.Language = "ja";
            Assert.Equal("", PatchMetadataCore.GetLanguageSuffix());
        }

        [Fact]
        public void GetLanguageSuffix_Null_DefaultsToEn()
        {
            CoreState.Language = null;
            Assert.Equal("en", PatchMetadataCore.GetLanguageSuffix());
        }

        [Fact]
        public void EnumeratePatches_NonexistentDir_ReturnsEmpty()
        {
            byte[] data = new byte[0x100];
            var rom = new ROM();
            rom.SwapNewROMDataDirect(data);

            var result = PatchMetadataCore.EnumeratePatches("/nonexistent/path", rom, "en");
            Assert.Empty(result);
        }

        // EnumeratePatches must find EVERY PATCH_*.txt recursively (matching WinForms
        // ScanPatchs), including subdirectories and multiple files per directory — not
        // just one file per top-level dir. Regression for #1376: hardcoding patches
        // live in subdirs (e.g. FE8U/SYSTEM/PATCH_*.txt) and were previously dropped,
        // so the [HardCoding] token filter could never match anything in the GUI.
        [Fact]
        public void EnumeratePatches_RecursesAndFindsAllPatchFiles()
        {
            byte[] data = new byte[0x100];
            var rom = new ROM();
            rom.SwapNewROMDataDirect(data);

            string baseDir = Path.Combine(Path.GetTempPath(), "PatchEnum_" + Guid.NewGuid().ToString("N"));
            string sub = Path.Combine(baseDir, "SYSTEM");
            Directory.CreateDirectory(sub);
            try
            {
                // Two patches in the same subdir + one at the top level.
                File.WriteAllLines(Path.Combine(sub, "PATCH_Eirika.txt"), new[] { "TYPE=ADDR", "NAME=Eirika Patch" });
                File.WriteAllLines(Path.Combine(sub, "PATCH_Ephraim.txt"), new[] { "TYPE=ADDR", "NAME=Ephraim Patch" });
                File.WriteAllLines(Path.Combine(baseDir, "PATCH_Top.txt"), new[] { "TYPE=ADDR" });

                var result = PatchMetadataCore.EnumeratePatches(baseDir, rom, "en");

                Assert.Equal(3, result.Count);
                Assert.Contains(result, p => p.Name == "Eirika Patch");
                Assert.Contains(result, p => p.Name == "Ephraim Patch");
                // Top-level file with no NAME -> default name = file minus PATCH_ prefix.
                Assert.Contains(result, p => p.Name == "Top");
                // PatchFilePath is the actual file (re-loadable by PatchFilterCore).
                Assert.All(result, p => Assert.True(File.Exists(p.PatchFilePath)));

                // DirectoryName is the patch's REAL containing folder — guards the CLI
                // --patch-name / folder filter. The two subdir patches are under SYSTEM;
                // the top-level patch is under the base dir's own leaf name.
                var eirika = result.First(p => p.Name == "Eirika Patch");
                var ephraim = result.First(p => p.Name == "Ephraim Patch");
                var top = result.First(p => p.Name == "Top");
                Assert.Equal("SYSTEM", eirika.DirectoryName);
                Assert.Equal("SYSTEM", ephraim.DirectoryName);
                Assert.Equal(Path.GetFileName(baseDir), top.DirectoryName);
            }
            finally
            {
                try { Directory.Delete(baseDir, true); } catch { }
            }
        }

        [Fact]
        public void EnumeratePatches_ReadFailureForOneFile_RetainsOtherPatches()
        {
            var rom = new ROM();
            rom.SwapNewROMDataDirect(new byte[0x100]);

            string baseDir = Path.Combine(Path.GetTempPath(), "PatchEnumTolerance");
            string good = Path.Combine(baseDir, "SYSTEM", "PATCH_Good.txt");
            string unreadable = Path.Combine(baseDir, "SYSTEM", "PATCH_Unreadable.txt");

            var result = PatchMetadataCore.EnumeratePatches(
                baseDir,
                rom,
                "en",
                file => file == unreadable
                    ? throw new IOException("simulated unreadable patch")
                    : new[] { "NAME=Good Patch", "TYPE=ADDR" },
                _ => new[] { unreadable, good });

            Assert.Equal(2, result.Count);
            Assert.Contains(result, p => p.Name == "Good Patch" && p.Type == "ADDR");
            PatchMetadataCore.PatchInfo fallback = Assert.Single(result, p => p.Name == "Unreadable");
            Assert.Equal("SYSTEM", fallback.DirectoryName);
            Assert.Equal(unreadable, fallback.PatchFilePath);
        }

        [Fact]
        public void ParsePatchFile_WithMetadata_ExtractsFields()
        {
            // Create a temp patch file
            string tempDir = Path.Combine(Path.GetTempPath(), "PatchMetadataCoreTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                string patchFile = Path.Combine(tempDir, "PATCH_Test.txt");
                File.WriteAllLines(patchFile, new[]
                {
                    "NAME=TestPatchJP",
                    "NAME.en=Test Patch English",
                    "TYPE=BIN",
                    "TAG=#ENGINE #TEST",
                    "AUTHOR=TestAuthor",
                    "INFO=Japanese description\\r\\nLine2",
                    "INFO.en=English description\\r\\nLine2",
                    "PATCHED_IF:0x10=0xAB 0xCD",
                });

                byte[] data = new byte[0x100];
                data[0x10] = 0xAB;
                data[0x11] = 0xCD;
                var rom = new ROM();
                rom.SwapNewROMDataDirect(data);

                var info = PatchMetadataCore.ParsePatchFile(patchFile, "TestDir", rom, "en");

                Assert.Equal("Test Patch English", info.Name);
                Assert.Equal("TestDir", info.DirectoryName);
                Assert.Equal("BIN", info.Type);
                Assert.Equal("#ENGINE #TEST", info.Tags);
                Assert.Equal("TestAuthor", info.Author);
                Assert.Contains("English description", info.Description);
                Assert.Equal(PatchMetadataCore.PatchStatus.Installed, info.Status);
            }
            finally
            {
                Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void ParsePatchFile_JapaneseLanguage_UsesDefaultName()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "PatchMetadataCoreTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                string patchFile = Path.Combine(tempDir, "PATCH_Test.txt");
                File.WriteAllLines(patchFile, new[]
                {
                    "NAME=TestPatchJP",
                    "NAME.en=Test Patch English",
                    "INFO=Japanese description",
                    "INFO.en=English description",
                });

                byte[] data = new byte[0x100];
                var rom = new ROM();
                rom.SwapNewROMDataDirect(data);

                // With empty lang (Japanese), should use NAME= value
                var info = PatchMetadataCore.ParsePatchFile(patchFile, "TestDir", rom, "");

                Assert.Equal("TestPatchJP", info.Name);
                Assert.Equal("Japanese description", info.Description);
            }
            finally
            {
                Directory.Delete(tempDir, true);
            }
        }

        // ===== ApplyPatch tests =====

        [Fact]
        public void ParsePatchParams_ParsesKeyValuePairs()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "PatchParamTest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                string patchFile = Path.Combine(tempDir, "PATCH_Test.txt");
                File.WriteAllLines(patchFile, new[]
                {
                    "TYPE=BIN",
                    "BIN:0x100=data.bin",
                    "JUMP:0x200:$r3=data.bin",
                    "// comment line",
                    "PATCHED_IF:0x100=0xAB",
                });

                var parms = PatchMetadataCore.ParsePatchParams(patchFile);
                Assert.Equal(4, parms.Count); // TYPE, BIN, JUMP, PATCHED_IF (comment skipped)
                Assert.Equal("BIN", parms[1].Keyword);
                Assert.Equal("0x100", parms[1].KeyParts[1]);
                Assert.Equal("data.bin", parms[1].Value);
                Assert.Equal("JUMP", parms[2].Keyword);
            }
            finally
            {
                Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void ApplyPatch_NullRom_Fails()
        {
            var result = PatchMetadataCore.ApplyPatch(null, "nonexistent.txt");
            Assert.False(result.Success);
            Assert.Contains("No ROM", result.Message);
        }

        [Fact]
        public void ApplyPatch_NonexistentFile_Fails()
        {
            var rom = new ROM();
            rom.SwapNewROMDataDirect(new byte[0x100]);
            var result = PatchMetadataCore.ApplyPatch(rom, "/nonexistent/PATCH_test.txt");
            Assert.False(result.Success);
            Assert.Contains("not found", result.Message);
        }

        [Fact]
        public void ApplyPatch_EAType_Fails()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "PatchEATest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                string patchFile = Path.Combine(tempDir, "PATCH_Test.txt");
                File.WriteAllLines(patchFile, new[]
                {
                    "TYPE=EA",
                    "EA=Installer.event",
                });

                var rom = new ROM();
                rom.SwapNewROMDataDirect(new byte[0x1000]);
                var result = PatchMetadataCore.ApplyPatch(rom, patchFile);
                Assert.False(result.Success);
                Assert.Contains("EA-type", result.Message);
            }
            finally
            {
                Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void ApplyPatch_FixedAddress_WritesData()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "PatchBinTest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                // Create binary data file
                byte[] binData = new byte[] { 0xAA, 0xBB, 0xCC, 0xDD };
                File.WriteAllBytes(Path.Combine(tempDir, "test.bin"), binData);

                string patchFile = Path.Combine(tempDir, "PATCH_Test.txt");
                File.WriteAllLines(patchFile, new[]
                {
                    "TYPE=BIN",
                    "BIN:0x200=test.bin",
                    "PATCHED_IF:0x200=0xAA 0xBB 0xCC 0xDD",
                });

                var rom = new ROM();
                rom.SwapNewROMDataDirect(new byte[0x1000]);

                var result = PatchMetadataCore.ApplyPatch(rom, patchFile);
                Assert.True(result.Success);
                Assert.Equal(4, result.BytesWritten);

                // Verify data was written
                Assert.Equal(0xAAu, rom.u8(0x200));
                Assert.Equal(0xBBu, rom.u8(0x201));
                Assert.Equal(0xCCu, rom.u8(0x202));
                Assert.Equal(0xDDu, rom.u8(0x203));
            }
            finally
            {
                Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void ApplyPatch_WithUndo_TracksChanges()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "PatchUndoTest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                byte[] binData = new byte[] { 0x11, 0x22 };
                File.WriteAllBytes(Path.Combine(tempDir, "test.bin"), binData);

                string patchFile = Path.Combine(tempDir, "PATCH_Test.txt");
                File.WriteAllLines(patchFile, new[]
                {
                    "TYPE=BIN",
                    "BIN:0x100=test.bin",
                });

                var rom = new ROM();
                rom.SwapNewROMDataDirect(new byte[0x1000]);
                CoreState.ROM = rom;

                var undo = new Undo();
                var undoData = undo.NewUndoData("test");

                var result = PatchMetadataCore.ApplyPatch(rom, patchFile, undoData);
                Assert.True(result.Success);
                // Undo data should have recorded write positions
                Assert.True(undoData.list.Count > 0);
            }
            finally
            {
                Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void ApplyPatch_MultipleBins_WritesAll()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "PatchMultiBinTest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                File.WriteAllBytes(Path.Combine(tempDir, "a.bin"), new byte[] { 0xAA });
                File.WriteAllBytes(Path.Combine(tempDir, "b.bin"), new byte[] { 0xBB, 0xCC });

                string patchFile = Path.Combine(tempDir, "PATCH_Test.txt");
                File.WriteAllLines(patchFile, new[]
                {
                    "TYPE=BIN",
                    "BIN:0x100=a.bin",
                    "BIN:0x200=b.bin",
                });

                var rom = new ROM();
                rom.SwapNewROMDataDirect(new byte[0x1000]);

                var result = PatchMetadataCore.ApplyPatch(rom, patchFile);
                Assert.True(result.Success);
                Assert.Equal(3, result.BytesWritten);
                Assert.Equal(0xAAu, rom.u8(0x100));
                Assert.Equal(0xBBu, rom.u8(0x200));
                Assert.Equal(0xCCu, rom.u8(0x201));
            }
            finally
            {
                Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void ApplyPatch_MissingBinFile_Fails()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "PatchMissingBinTest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                string patchFile = Path.Combine(tempDir, "PATCH_Test.txt");
                File.WriteAllLines(patchFile, new[]
                {
                    "TYPE=BIN",
                    "BIN:0x100=nonexistent.bin",
                });

                var rom = new ROM();
                rom.SwapNewROMDataDirect(new byte[0x1000]);

                var result = PatchMetadataCore.ApplyPatch(rom, patchFile);
                Assert.False(result.Success);
                Assert.Contains("not found", result.Message);
            }
            finally
            {
                Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void ApplyPatch_FreeArea_FindsSpace()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "PatchFreeAreaTest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                byte[] binData = new byte[] { 0x01, 0x02, 0x03, 0x04 };
                File.WriteAllBytes(Path.Combine(tempDir, "hook.dmp"), binData);

                string patchFile = Path.Combine(tempDir, "PATCH_Test.txt");
                File.WriteAllLines(patchFile, new[]
                {
                    "TYPE=BIN",
                    "BIN:$FREEAREA=hook.dmp",
                });

                // Create ROM with free space (0x00 bytes at end)
                var rom = new ROM();
                byte[] romData = new byte[0x10000]; // 64KB
                // Fill first 0x200 bytes with non-zero to simulate used space
                for (int i = 0; i < 0x200; i++) romData[i] = 0x42;
                rom.SwapNewROMDataDirect(romData);

                var result = PatchMetadataCore.ApplyPatch(rom, patchFile);
                Assert.True(result.Success);
                Assert.Equal(4, result.BytesWritten);
            }
            finally
            {
                Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void ApplyPatch_JumpWithBin_WritesJumpCode()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "PatchJumpTest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                // Small routine to place in free area
                byte[] binData = new byte[] { 0x00, 0x4B, 0x9F, 0x46 };
                File.WriteAllBytes(Path.Combine(tempDir, "routine.dmp"), binData);

                string patchFile = Path.Combine(tempDir, "PATCH_Test.txt");
                File.WriteAllLines(patchFile, new[]
                {
                    "TYPE=BIN",
                    "BIN:0x1000=routine.dmp",
                    "JUMP:0x200:$r3=routine.dmp",
                });

                var rom = new ROM();
                rom.SwapNewROMDataDirect(new byte[0x10000]);

                var result = PatchMetadataCore.ApplyPatch(rom, patchFile);
                Assert.True(result.Success);
                // BIN wrote 4 bytes, JUMP wrote some bytes for jump trampoline
                Assert.True(result.BytesWritten > 4);
            }
            finally
            {
                Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void UninstallPatch_NoBackup_Fails()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "PatchUninstallNoBackup_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                string patchFile = Path.Combine(tempDir, "PATCH_Test.txt");
                File.WriteAllLines(patchFile, new[] { "TYPE=BIN" });

                var rom = new ROM();
                rom.SwapNewROMDataDirect(new byte[0x100]);
                var result = PatchMetadataCore.UninstallPatch(rom, patchFile);
                Assert.False(result.Success);
                Assert.Contains("No backup file", result.Message);
            }
            finally
            {
                Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void UninstallPatch_NullRom_Fails()
        {
            var result = PatchMetadataCore.UninstallPatch(null, "anything.txt");
            Assert.False(result.Success);
            Assert.Contains("No ROM", result.Message);
        }

        [Fact]
        public void SaveBackup_And_ParseBackupFile_RoundTrips()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "PatchBackupRoundTrip_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                string patchFile = Path.Combine(tempDir, "PATCH_Test.txt");
                File.WriteAllText(patchFile, "TYPE=BIN");

                byte[] romData = new byte[0x1000];
                romData[0x100] = 0xAA;
                romData[0x101] = 0xBB;
                romData[0x200] = 0xCC;
                var rom = new ROM();
                rom.SwapNewROMDataDirect(romData);

                var regions = new List<(uint address, int length)>
                {
                    (0x100, 2),
                    (0x200, 1),
                };

                PatchMetadataCore.SaveBackup(rom, patchFile, regions);

                string backupPath = PatchMetadataCore.GetBackupFilePath(patchFile);
                Assert.True(File.Exists(backupPath));

                var records = PatchMetadataCore.ParseBackupFile(backupPath);
                Assert.NotNull(records);
                Assert.Equal(2, records.Count);

                Assert.Equal(0x100u, records[0].address);
                Assert.Equal(new byte[] { 0xAA, 0xBB }, records[0].data);

                Assert.Equal(0x200u, records[1].address);
                Assert.Equal(new byte[] { 0xCC }, records[1].data);
            }
            finally
            {
                Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void HasBackup_ReturnsFalse_WhenNoFile()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "PatchHasBackup_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                string patchFile = Path.Combine(tempDir, "PATCH_Test.txt");
                File.WriteAllText(patchFile, "TYPE=BIN");
                Assert.False(PatchMetadataCore.HasBackup(patchFile));
            }
            finally
            {
                Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void ApplyPatch_CreatesBackup_Then_UninstallRestores()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "PatchInstallUninstall_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                // Set up ROM with known data at 0x200
                byte[] romData = new byte[0x1000];
                romData[0x200] = 0x11;
                romData[0x201] = 0x22;
                romData[0x202] = 0x33;
                romData[0x203] = 0x44;
                var rom = new ROM();
                rom.SwapNewROMDataDirect(romData);

                // Create patch that overwrites 0x200 with different data
                byte[] binData = new byte[] { 0xAA, 0xBB, 0xCC, 0xDD };
                File.WriteAllBytes(Path.Combine(tempDir, "test.bin"), binData);

                string patchFile = Path.Combine(tempDir, "PATCH_Test.txt");
                File.WriteAllLines(patchFile, new[]
                {
                    "TYPE=BIN",
                    "BIN:0x200=test.bin",
                    "PATCHED_IF:0x200=0xAA 0xBB 0xCC 0xDD",
                });

                // Install patch
                var installResult = PatchMetadataCore.ApplyPatch(rom, patchFile);
                Assert.True(installResult.Success);
                Assert.Equal(0xAAu, rom.u8(0x200));
                Assert.Equal(0xBBu, rom.u8(0x201));
                Assert.Equal(0xCCu, rom.u8(0x202));
                Assert.Equal(0xDDu, rom.u8(0x203));

                // Verify backup was created
                Assert.True(PatchMetadataCore.HasBackup(patchFile));

                // Uninstall patch
                var uninstallResult = PatchMetadataCore.UninstallPatch(rom, patchFile);
                Assert.True(uninstallResult.Success);
                Assert.Contains("restored", uninstallResult.Message);

                // Verify original bytes restored
                Assert.Equal(0x11u, rom.u8(0x200));
                Assert.Equal(0x22u, rom.u8(0x201));
                Assert.Equal(0x33u, rom.u8(0x202));
                Assert.Equal(0x44u, rom.u8(0x203));

                // Backup is now PRESERVED across uninstall (closes #1429 undo-of-uninstall dead-end)
                Assert.True(PatchMetadataCore.HasBackup(patchFile));
            }
            finally
            {
                Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void ParseBackupFile_MalformedFile_ReturnsNull()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "PatchBadBackup_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                string backupPath = Path.Combine(tempDir, ".backup_PATCH_Test.txt");
                File.WriteAllText(backupPath, "not a valid backup");

                var records = PatchMetadataCore.ParseBackupFile(backupPath);
                Assert.Null(records);
            }
            finally
            {
                Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void ParseBackupFile_NonexistentFile_ReturnsNull()
        {
            var records = PatchMetadataCore.ParseBackupFile("/nonexistent/.backup_test.txt");
            Assert.Null(records);
        }

        [Fact]
        public void UninstallPatch_MalformedBackup_Fails()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "PatchUninstallBadBackup_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                string patchFile = Path.Combine(tempDir, "PATCH_Test.txt");
                File.WriteAllText(patchFile, "TYPE=BIN");

                // Create a malformed backup file
                string backupPath = PatchMetadataCore.GetBackupFilePath(patchFile);
                File.WriteAllText(backupPath, "garbage data");

                var rom = new ROM();
                rom.SwapNewROMDataDirect(new byte[0x100]);
                var result = PatchMetadataCore.UninstallPatch(rom, patchFile);
                Assert.False(result.Success);
                Assert.Contains("malformed", result.Message);
            }
            finally
            {
                Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void GetBackupFilePath_CorrectFormat()
        {
            string dir = Path.Combine(Path.GetTempPath(), "someDir");
            string patchFile = Path.Combine(dir, "PATCH_MyPatch.txt");
            string expected = Path.Combine(dir, ".backup_PATCH_MyPatch.txt");
            Assert.Equal(expected, PatchMetadataCore.GetBackupFilePath(patchFile));
        }

        [Fact]
        public void FindFreeSpace_FindsSpaceInRom()
        {
            var rom = new ROM();
            byte[] data = new byte[0x10000];
            // Fill first 0x200 bytes
            for (int i = 0; i < 0x200; i++) data[i] = 0x42;
            rom.SwapNewROMDataDirect(data);

            uint addr = PatchMetadataCore.FindFreeSpace(rom, 100);
            Assert.NotEqual(U.NOT_FOUND, addr);
            Assert.True(addr >= 0x200);
        }

        [Fact]
        public void PatchApplyResult_OkAndFail_WorkCorrectly()
        {
            var ok = PatchMetadataCore.PatchApplyResult.Ok("Success", 42);
            Assert.True(ok.Success);
            Assert.Equal("Success", ok.Message);
            Assert.Equal(42, ok.BytesWritten);

            var fail = PatchMetadataCore.PatchApplyResult.Fail("Error");
            Assert.False(fail.Success);
            Assert.Equal("Error", fail.Message);
        }

        // ===== Dependency checking tests =====

        [Fact]
        public void GetPatchDependencies_NoDeps_ReturnsEmpty()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "PatchDepTest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                string patchFile = Path.Combine(tempDir, "PATCH_Test.txt");
                File.WriteAllLines(patchFile, new[]
                {
                    "NAME=NoDeps",
                    "TYPE=BIN",
                    "PATCHED_IF:0x100=0xAB",
                });

                var deps = PatchMetadataCore.GetPatchDependencies(patchFile);
                Assert.Empty(deps);
            }
            finally
            {
                Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void GetPatchDependencies_WithIfLines_ExtractsConditions()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "PatchDepTest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                string patchFile = Path.Combine(tempDir, "PATCH_Test.txt");
                File.WriteAllLines(patchFile, new[]
                {
                    "NAME=WithDeps",
                    "IF:0x02BA4=0x00 0xB5 0xC2 0x0F //need Anti-Huffman",
                    "IF:0x100=0xAB 0xCD",
                    "TYPE=BIN",
                    "PATCHED_IF:0x200=0xFF",
                });

                var deps = PatchMetadataCore.GetPatchDependencies(patchFile);
                Assert.Equal(2, deps.Count);
                Assert.Equal("0x02BA4=0x00 0xB5 0xC2 0x0F", deps[0].Condition);
                Assert.Equal("need Anti-Huffman", deps[0].Comment); // from inline comment
                Assert.Equal("0x100=0xAB 0xCD", deps[1].Condition);
            }
            finally
            {
                Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void GetPatchDependencies_WithIfComment_UsesComment()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "PatchDepTest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                string patchFile = Path.Combine(tempDir, "PATCH_Test.txt");
                File.WriteAllLines(patchFile, new[]
                {
                    "NAME=WithComment",
                    "IF:0x100=0xAB 0xCD",
                    "IF_COMMENT=Please install Patch X first.",
                    "IF_COMMENT.en=Please install Patch X first (English).",
                    "TYPE=BIN",
                });

                // With English lang
                var deps = PatchMetadataCore.GetPatchDependencies(patchFile, "en");
                Assert.Single(deps);
                Assert.Equal("Please install Patch X first (English).", deps[0].Comment);

                // With empty lang (Japanese)
                var depsJp = PatchMetadataCore.GetPatchDependencies(patchFile, "");
                Assert.Single(depsJp);
                Assert.Equal("Please install Patch X first.", depsJp[0].Comment);
            }
            finally
            {
                Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void GetPatchDependencies_NonexistentFile_ReturnsEmpty()
        {
            var deps = PatchMetadataCore.GetPatchDependencies("/nonexistent/PATCH_test.txt");
            Assert.Empty(deps);
        }

        [Fact]
        public void EvaluateIfCondition_Satisfied_ReturnsTrue()
        {
            byte[] data = new byte[0x1000];
            data[0x100] = 0xAB;
            data[0x101] = 0xCD;
            var rom = new ROM();
            rom.SwapNewROMDataDirect(data);

            Assert.True(PatchMetadataCore.EvaluateIfCondition("0x100=0xAB 0xCD", rom));
        }

        [Fact]
        public void EvaluateIfCondition_NotSatisfied_ReturnsFalse()
        {
            byte[] data = new byte[0x1000];
            data[0x100] = 0x00;
            data[0x101] = 0x00;
            var rom = new ROM();
            rom.SwapNewROMDataDirect(data);

            Assert.False(PatchMetadataCore.EvaluateIfCondition("0x100=0xAB 0xCD", rom));
        }

        [Fact]
        public void EvaluateIfCondition_GrepCondition_ReturnsTrue()
        {
            var rom = new ROM();
            rom.SwapNewROMDataDirect(new byte[0x100]);

            // GREP conditions are treated as satisfied (can't check simply)
            Assert.True(PatchMetadataCore.EvaluateIfCondition("$GREP4 0xAB=0xAB", rom));
            Assert.True(PatchMetadataCore.EvaluateIfCondition("$FGREP4 test.dmp=0xAB", rom));
        }

        [Fact]
        public void EvaluateIfCondition_NullRom_ReturnsFalse()
        {
            Assert.False(PatchMetadataCore.EvaluateIfCondition("0x100=0xAB", null));
        }

        [Fact]
        public void EvaluateIfCondition_AddrBeyondRom_ReturnsFalse()
        {
            var rom = new ROM();
            rom.SwapNewROMDataDirect(new byte[0x10]);

            Assert.False(PatchMetadataCore.EvaluateIfCondition("0xFF=0xAB", rom));
        }

        [Fact]
        public void CheckDependencies_AllMet_ReturnsEmpty()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "PatchCheckDeps_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                string patchFile = Path.Combine(tempDir, "PATCH_Test.txt");
                File.WriteAllLines(patchFile, new[]
                {
                    "IF:0x100=0xAB 0xCD",
                    "TYPE=BIN",
                });

                byte[] data = new byte[0x1000];
                data[0x100] = 0xAB;
                data[0x101] = 0xCD;
                var rom = new ROM();
                rom.SwapNewROMDataDirect(data);

                var missing = PatchMetadataCore.CheckDependencies(rom, patchFile);
                Assert.Empty(missing);
            }
            finally
            {
                Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void CheckDependencies_SomeUnmet_ReturnsMissing()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "PatchCheckDeps_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                string patchFile = Path.Combine(tempDir, "PATCH_Test.txt");
                File.WriteAllLines(patchFile, new[]
                {
                    "IF:0x100=0xAB 0xCD",
                    "IF:0x200=0xEE 0xFF",
                    "TYPE=BIN",
                });

                byte[] data = new byte[0x1000];
                data[0x100] = 0xAB;
                data[0x101] = 0xCD;
                // 0x200 is zeroed = second dep not met
                var rom = new ROM();
                rom.SwapNewROMDataDirect(data);

                var missing = PatchMetadataCore.CheckDependencies(rom, patchFile);
                Assert.Single(missing);
                Assert.Equal("0x200=0xEE 0xFF", missing[0].Condition);
                Assert.False(missing[0].IsSatisfied);
            }
            finally
            {
                Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void ParsePatchFile_PopulatesDependencyFields()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "PatchDepFields_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                string patchFile = Path.Combine(tempDir, "PATCH_Test.txt");
                File.WriteAllLines(patchFile, new[]
                {
                    "NAME=DepPatch",
                    "IF:0x100=0xAB 0xCD",
                    "TYPE=BIN",
                    "PATCHED_IF:0x200=0xFF",
                });

                byte[] data = new byte[0x1000];
                // IF condition NOT met (0x100 is zeroed)
                var rom = new ROM();
                rom.SwapNewROMDataDirect(data);

                var info = PatchMetadataCore.ParsePatchFile(patchFile, "TestDir", rom, "en");
                Assert.Equal(1, info.DependencyCount);
                Assert.Equal(1, info.UnsatisfiedDependencyCount);
                Assert.Single(info.UnsatisfiedDependencies);
            }
            finally
            {
                Directory.Delete(tempDir, true);
            }
        }

        // ===== Undo-recording uninstall tests (#1429) =====

        // Test A: the recording overload records every restored region into the
        // supplied UndoData (before each write captures the PATCHED bytes), and a
        // subsequent Rollback restores the ROM byte-for-byte to its PATCHED state.
        [Fact]
        public void UninstallPatch_WithUndoData_RecordsAndRollbackRestoresPatchedBytes()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "PatchUninstallUndo_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                // ROM with known ORIGINAL data at 0x200.
                byte[] romData = new byte[0x1000];
                romData[0x200] = 0x11; romData[0x201] = 0x22; romData[0x202] = 0x33; romData[0x203] = 0x44;
                var rom = new ROM();
                rom.SwapNewROMDataDirect(romData);
                CoreState.ROM = rom; // UndoData/UndoPostion read CoreState.ROM.

                byte[] binData = new byte[] { 0xAA, 0xBB, 0xCC, 0xDD };
                File.WriteAllBytes(Path.Combine(tempDir, "test.bin"), binData);

                string patchFile = Path.Combine(tempDir, "PATCH_Test.txt");
                File.WriteAllLines(patchFile, new[]
                {
                    "TYPE=BIN",
                    "BIN:0x200=test.bin",
                    "PATCHED_IF:0x200=0xAA 0xBB 0xCC 0xDD",
                });

                // Install -> creates the backup + patches the ROM.
                var installResult = PatchMetadataCore.ApplyPatch(rom, patchFile);
                Assert.True(installResult.Success);
                Assert.True(PatchMetadataCore.HasBackup(patchFile));

                // Capture the PATCHED ROM bytes (what undo should restore us TO).
                byte[] patchedSnapshot = (byte[])rom.Data.Clone();
                Assert.Equal(0xAAu, rom.u8(0x200));

                var undo = new Undo();
                var undoData = undo.NewUndoData("PatchUninstall", "Test");

                var result = PatchMetadataCore.UninstallPatch(rom, patchFile, undoData);
                Assert.True(result.Success);

                // The restore region was recorded into undoData.
                Assert.NotEmpty(undoData.list);
                Assert.Contains(undoData.list, p => p.addr == 0x200);
                // The recorded bytes are the PATCHED bytes (snapshot before the restore write).
                var rec = undoData.list.First(p => p.addr == 0x200);
                Assert.Equal(new byte[] { 0xAA, 0xBB, 0xCC, 0xDD }, rec.data);

                // ROM is now restored to ORIGINAL.
                Assert.Equal(0x11u, rom.u8(0x200));
                Assert.Equal(0x44u, rom.u8(0x203));

                // Rolling the recorded record forward re-applies the PATCHED bytes:
                // ROM becomes byte-identical to the captured patched snapshot.
                undo.Push(undoData);
                undo.RunUndo();
                Assert.Equal(patchedSnapshot, rom.Data);
            }
            finally
            {
                CoreState.ROM = null;
                Directory.Delete(tempDir, true);
            }
        }

        // Test B: a 2-record backup where record1 is valid and record2 EXCEEDS the
        // ROM size. Uninstall fails (message mentions "exceeds ROM size"), but
        // record1's restore was already written AND recorded into undoData so a
        // Rollback restores byte-identity to the pre-uninstall (patched) state. The
        // backup file is NOT deleted on failure.
        [Fact]
        public void UninstallPatch_PartialFailure_RecordsRestoredRegionAndKeepsBackup()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "PatchUninstallPartial_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                // PATCHED ROM (small): 0x10 bytes. record1 restores 0x00..0x01;
                // record2 names addr 0x100 (way beyond the 0x10-byte ROM) -> fails.
                byte[] romData = new byte[0x10];
                romData[0x00] = 0xAA; romData[0x01] = 0xBB; // current (patched) bytes
                var rom = new ROM();
                rom.SwapNewROMDataDirect(romData);
                CoreState.ROM = rom;

                byte[] patchedSnapshot = (byte[])rom.Data.Clone();

                string patchFile = Path.Combine(tempDir, "PATCH_Test.txt");
                File.WriteAllText(patchFile, "TYPE=BIN");

                // Hand-craft a 2-record backup file (format: 0xADDRESS:LENGTH:HH HH ...).
                // record1 -> restore 0x00 to ORIGINAL bytes 11 22 (valid).
                // record2 -> addr 0x100, 2 bytes -> exceeds 0x10-byte ROM (fails validation).
                string backupPath = PatchMetadataCore.GetBackupFilePath(patchFile);
                File.WriteAllLines(backupPath, new[]
                {
                    "0x0:2:11 22",
                    "0x100:2:33 44",
                });

                var undo = new Undo();
                var undoData = undo.NewUndoData("PatchUninstall", "Partial");

                var result = PatchMetadataCore.UninstallPatch(rom, patchFile, undoData);

                // Fails on the over-size record2.
                Assert.False(result.Success);
                Assert.Contains("exceeds ROM size", result.Message);

                // record1 WAS written (ROM partly mutated) ...
                Assert.Equal(0x11u, rom.u8(0x0));
                Assert.Equal(0x22u, rom.u8(0x1));
                // ... and recorded into undoData (capturing the patched bytes 0xAA 0xBB).
                Assert.NotEmpty(undoData.list);
                Assert.Contains(undoData.list, p => p.addr == 0x0);
                var rec = undoData.list.First(p => p.addr == 0x0);
                Assert.Equal(new byte[] { 0xAA, 0xBB }, rec.data);

                // Backup file is NOT deleted on failure.
                Assert.True(File.Exists(backupPath));

                // Rolling the recorded partial mutation forward restores byte-identity
                // to the pre-uninstall (patched) state.
                undo.Push(undoData);
                undo.RunUndo();
                Assert.Equal(patchedSnapshot, rom.Data);
            }
            finally
            {
                CoreState.ROM = null;
                Directory.Delete(tempDir, true);
            }
        }

        // Test C: the parameterless overload still succeeds, restores bytes, and does
        // not throw (back-compat) — it delegates to the undoData=null path.
        [Fact]
        public void UninstallPatch_ParameterlessOverload_StillRestoresBytes()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "PatchUninstallNoUndo_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                byte[] romData = new byte[0x1000];
                romData[0x200] = 0x11; romData[0x201] = 0x22; romData[0x202] = 0x33; romData[0x203] = 0x44;
                var rom = new ROM();
                rom.SwapNewROMDataDirect(romData);

                byte[] binData = new byte[] { 0xAA, 0xBB, 0xCC, 0xDD };
                File.WriteAllBytes(Path.Combine(tempDir, "test.bin"), binData);

                string patchFile = Path.Combine(tempDir, "PATCH_Test.txt");
                File.WriteAllLines(patchFile, new[]
                {
                    "TYPE=BIN",
                    "BIN:0x200=test.bin",
                    "PATCHED_IF:0x200=0xAA 0xBB 0xCC 0xDD",
                });

                Assert.True(PatchMetadataCore.ApplyPatch(rom, patchFile).Success);
                Assert.Equal(0xAAu, rom.u8(0x200));

                var result = PatchMetadataCore.UninstallPatch(rom, patchFile);
                Assert.True(result.Success);
                Assert.Contains("restored", result.Message);

                // Original bytes restored.
                Assert.Equal(0x11u, rom.u8(0x200));
                Assert.Equal(0x22u, rom.u8(0x201));
                Assert.Equal(0x33u, rom.u8(0x202));
                Assert.Equal(0x44u, rom.u8(0x203));

                // Backup is PRESERVED across uninstall (closes #1429 undo-of-uninstall dead-end);
                // the parameterless overload delegates to the same restore path.
                Assert.True(PatchMetadataCore.HasBackup(patchFile));
            }
            finally
            {
                Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void ParsePatchFile_NoDeps_ZeroCounts()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "PatchNoDepFields_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                string patchFile = Path.Combine(tempDir, "PATCH_Test.txt");
                File.WriteAllLines(patchFile, new[]
                {
                    "NAME=NoDeps",
                    "TYPE=BIN",
                });

                var rom = new ROM();
                rom.SwapNewROMDataDirect(new byte[0x100]);

                var info = PatchMetadataCore.ParsePatchFile(patchFile, "TestDir", rom, "en");
                Assert.Equal(0, info.DependencyCount);
                Assert.Equal(0, info.UnsatisfiedDependencyCount);
                Assert.Empty(info.UnsatisfiedDependencies);
            }
            finally
            {
                Directory.Delete(tempDir, true);
            }
        }

        // ---- #1811: empty/uninitialized patch2 detection ----

        [Fact]
        public void IsPatchLibraryEmpty_MissingOrNullDirectory_ReturnsTrue()
        {
            string missing = Path.Combine(Path.GetTempPath(), "fe_no_patch2_" + System.Guid.NewGuid().ToString("N"));
            Assert.True(PatchMetadataCore.IsPatchLibraryEmpty(missing));
            Assert.True(PatchMetadataCore.IsPatchLibraryEmpty(null));
            Assert.True(PatchMetadataCore.IsPatchLibraryEmpty(""));
        }

        [Fact]
        public void IsPatchLibraryEmpty_ExistingEmptyDirectory_ReturnsTrue()
        {
            string dir = Path.Combine(Path.GetTempPath(), "fe_empty_patch2_" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try { Assert.True(PatchMetadataCore.IsPatchLibraryEmpty(dir)); }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void IsPatchLibraryEmpty_DirectoryWithOnlyNonPatchFiles_ReturnsTrue()
        {
            string dir = Path.Combine(Path.GetTempPath(), "fe_nonpatch_" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "README.txt"), "x");
            try { Assert.True(PatchMetadataCore.IsPatchLibraryEmpty(dir)); }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void IsPatchLibraryEmpty_DirectoryWithNestedPatch_ReturnsFalse()
        {
            // Matches EnumeratePatches' recursive PATCH_*.txt scan (e.g. FE8U/SYSTEM/PATCH_*.txt).
            string dir = Path.Combine(Path.GetTempPath(), "fe_patch2_" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(dir, "SYSTEM"));
            File.WriteAllText(Path.Combine(dir, "SYSTEM", "PATCH_Test.txt"), "NAME:Test");
            try { Assert.False(PatchMetadataCore.IsPatchLibraryEmpty(dir)); }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void NotInitializedMessage_IsNonEmpty_MentionsDownload_AndDistinctFromAndroidNotice()
        {
            Assert.False(string.IsNullOrWhiteSpace(PatchMetadataCore.NotInitializedMessage));
            Assert.Contains("download", PatchMetadataCore.NotInitializedMessage, System.StringComparison.OrdinalIgnoreCase);
            Assert.NotEqual(AndroidResourceNoticeCore.PatchLibraryUnavailableMessage, PatchMetadataCore.NotInitializedMessage);
        }

        // ---- #1965 PR feedback remediation: byte-first bounded metadata/params scan ----
        // Companion to TryEnumeratePatchesBounded's exporter-only metadata-scan fix: the
        // exporter must never fully materialize a discovered patch file via an eager
        // File.ReadAllLines/File.ReadLines just to extract NAME/TYPE/PATCHED_IF metadata or
        // key=value params. Every bounded read now rejects on BYTES (16 MiB per file, 64 MiB
        // aggregate per pass) BEFORE a single line is ever decoded.

        [Fact]
        public void TryParsePatchFileStrictBounded_MetadataWithinBound_ParsesIdenticallyToUnbounded()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "PatchBoundedMeta_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                string patchFile = Path.Combine(tempDir, "PATCH_Test.txt");
                File.WriteAllLines(patchFile, new[]
                {
                    "NAME=TestPatchJP",
                    "NAME.en=Test Patch English",
                    "TYPE=BIN",
                    "TAG=#ENGINE #TEST",
                    "AUTHOR=TestAuthor",
                    "PATCHED_IF:0x10=0xAB 0xCD",
                });

                byte[] data = new byte[0x100];
                data[0x10] = 0xAB;
                data[0x11] = 0xCD;
                var rom = new ROM();
                rom.SwapNewROMDataDirect(data);

                var unbounded = PatchMetadataCore.ParsePatchFile(patchFile, "TestDir", rom, "en");
                bool withinBound = PatchMetadataCore.TryParsePatchFileStrictBounded(
                    patchFile, "TestDir", rom, "en", 100, PatchMetadataCore.MaxPatchDefinitionBytes,
                    out var bounded, out long bytesRead);

                Assert.True(withinBound);
                Assert.True(bytesRead > 0);
                Assert.Equal(unbounded.Name, bounded.Name);
                Assert.Equal(unbounded.Type, bounded.Type);
                Assert.Equal(unbounded.Tags, bounded.Tags);
                Assert.Equal(unbounded.Author, bounded.Author);
                Assert.Equal(unbounded.Status, bounded.Status);
            }
            finally { Directory.Delete(tempDir, true); }
        }

        [Fact]
        public void TryParsePatchFileStrictBounded_MetadataBeyondLineBound_FailsWithoutPartialInfo()
        {
            // A bounded scan must never turn an oversized patch into a plausible partial record.
            // It reports the breach and returns no PatchInfo so the exporter can degrade the
            // entire advisory inventory.
            string tempDir = Path.Combine(Path.GetTempPath(), "PatchBoundedMetaCut_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                string patchFile = Path.Combine(tempDir, "PATCH_Test.txt");
                var lines = new List<string> { "TYPE=BIN" };
                for (int i = 0; i < 10; i++)
                    lines.Add("// filler " + i);
                lines.Add("NAME=TooLate"); // placed at line index 11 (0-based)
                File.WriteAllLines(patchFile, lines);

                var rom = new ROM();
                rom.SwapNewROMDataDirect(new byte[0x100]);

                bool withinBound = PatchMetadataCore.TryParsePatchFileStrictBounded(
                    patchFile, "DefaultName", rom, "en", 5, PatchMetadataCore.MaxPatchDefinitionBytes,
                    out var bounded, out long bytesRead);

                Assert.False(withinBound);
                Assert.Null(bounded);
            }
            finally { Directory.Delete(tempDir, true); }
        }

        [Theory]
        [InlineData(BuildfileFormat.MaxAdvisoryItems, true)]
        [InlineData(BuildfileFormat.MaxAdvisoryItems + 1, false)]
        public void TryParsePatchFileStrictBounded_SharedLimitBoundary_IsExact(
            int lineCount,
            bool expectedWithinBound)
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "PatchBoundedMetaBoundary_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                string patchFile = Path.Combine(tempDir, "PATCH_Test.txt");
                var lines = new List<string>(lineCount);
                for (int i = 0; i < lineCount; i++)
                    lines.Add("// bounded metadata line " + i);
                File.WriteAllLines(patchFile, lines);

                var rom = new ROM();
                rom.SwapNewROMDataDirect(new byte[0x100]);

                bool withinBound = PatchMetadataCore.TryParsePatchFileStrictBounded(
                    patchFile,
                    "DefaultName",
                    rom,
                    "en",
                    BuildfileFormat.MaxAdvisoryItems,
                    PatchMetadataCore.MaxPatchDefinitionBytes,
                    out var info,
                    out long bytesRead);

                Assert.Equal(expectedWithinBound, withinBound);
                Assert.Equal(expectedWithinBound, info != null);
            }
            finally { Directory.Delete(tempDir, true); }
        }

        [Fact]
        public void TryEnumeratePatchesBounded_MetadataLineBreach_ClearsPartialInventoryAndSignalsLimit()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "PatchBoundedMetaInventory_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                string first = Path.Combine(tempDir, "PATCH_A.txt");
                string oversized = Path.Combine(tempDir, "PATCH_B.txt");
                File.WriteAllLines(first, new[] { "NAME=AcceptedFirst" });
                File.WriteAllLines(oversized, new[]
                {
                    "NAME=Oversized",
                    "// filler 1",
                    "// filler 2",
                    "// filler 3",
                    "// filler 4",
                    "// cap plus one",
                });

                var rom = new ROM();
                rom.SwapNewROMDataDirect(new byte[0x100]);

                bool success = PatchMetadataCore.TryEnumeratePatchesBounded(
                    tempDir,
                    rom,
                    "en",
                    _ => new[] { first, oversized },
                    5,
                    PatchMetadataCore.MaxMetadataAggregateBytes,
                    out var patches,
                    out string error,
                    out bool limitExceeded);

                Assert.False(success);
                Assert.True(limitExceeded);
                Assert.Empty(patches);
                Assert.Contains("line-scan bound", error, StringComparison.Ordinal);
                Assert.DoesNotContain(tempDir, error, StringComparison.Ordinal);
            }
            finally { Directory.Delete(tempDir, true); }
        }

        [Fact]
        public void TryEnumeratePatchesBounded_ProductionLazyPath_MissingPatchRoot_SucceedsEmpty()
        {
            // #1936 Finding A: the PRODUCTION discovery path (listPatchFiles: null) uses LAZY
            // Directory.EnumerateFiles, which does not touch the filesystem — and so cannot
            // throw — at the point it is called; a missing patchBaseDir only raises
            // DirectoryNotFoundException once the internal foreach actually begins iterating.
            // Before the fix, that exception fell through to the broad
            // `catch (Exception ex) when (IsExpectedFileSystemException(ex))` clause (since
            // DirectoryNotFoundException IS an IOException) and was reported as a real failure.
            // A definitely nonexistent patch root must instead resolve to the SAME
            // successful-empty contract the eager/injected-lister branch and the unbounded
            // TryEnumeratePatches path already honor: true, empty patches, empty error,
            // limitExceeded false — proven here with listPatchFiles left null so PRODUCTION
            // lazy discovery (not an injected test double) is what actually runs.
            string missingRoot = Path.Combine(
                Path.GetTempPath(), "PatchBoundedMissingRoot_" + Guid.NewGuid().ToString("N"));
            // Deliberately do NOT create missingRoot.
            Assert.False(Directory.Exists(missingRoot));

            var rom = new ROM();
            rom.SwapNewROMDataDirect(new byte[0x100]);

            bool success = PatchMetadataCore.TryEnumeratePatchesBounded(
                missingRoot,
                rom,
                "en",
                listPatchFiles: null,
                maxFiles: 16384,
                maxAggregateBytes: PatchMetadataCore.MaxMetadataAggregateBytes,
                out var patches,
                out string error,
                out bool limitExceeded);

            Assert.True(success);
            Assert.Empty(patches);
            Assert.Equal("", error);
            Assert.False(limitExceeded);
        }

        [Fact]
        public void TryEnumeratePatchesBounded_DiscoveredPathMissingDuringMetadata_FailsNonLimitFault()
        {
            // #1936 missing-file contract (b): a path already RETURNED by discovery but MISSING
            // when the bounded METADATA pass opens it is a whole-inventory FILESYSTEM fault —
            // false, empty patches, limitExceeded FALSE. It is NEVER the successful-empty
            // missing-ROOT case (a) and NEVER a resource-budget breach. The injected listing
            // names a definition file that never exists on disk.
            string tempDir = Path.Combine(
                Path.GetTempPath(), "PatchBoundedMissingMeta_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                string missing = Path.Combine(tempDir, "PATCH_Missing.txt"); // deliberately NOT created

                var rom = new ROM();
                rom.SwapNewROMDataDirect(new byte[0x100]);

                bool success = PatchMetadataCore.TryEnumeratePatchesBounded(
                    tempDir,
                    rom,
                    "en",
                    _ => new[] { missing },
                    5,
                    PatchMetadataCore.MaxMetadataAggregateBytes,
                    out var patches,
                    out string error,
                    out bool limitExceeded);

                Assert.False(success);
                Assert.False(limitExceeded); // filesystem fault, NOT a resource-budget breach
                Assert.Empty(patches);
                Assert.NotEqual("", error);
            }
            finally { Directory.Delete(tempDir, true); }
        }

        // ---- #1965: shared byte-first helper (TryReadBoundedFileLines) unit coverage ----

        static string WriteBytes(string dir, string name, byte[] data)
        {
            string path = Path.Combine(dir, name);
            File.WriteAllBytes(path, data);
            return path;
        }

        [Fact]
        public void TryReadBoundedFileLines_SingleLineOverCapPlusOne_RejectsWithoutPartialLines()
        {
            // A single line with NO newline, one byte over the cap: File.ReadLines would
            // materialize the whole oversized line before any count-based guard ever ran. The
            // byte-first helper must reject on bytes alone, before decoding a single line.
            string tempDir = Path.Combine(Path.GetTempPath(), "BoundedHelperSingleLine_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                const int cap = 10;
                byte[] data = System.Text.Encoding.ASCII.GetBytes(new string('a', cap + 1));
                string path = WriteBytes(tempDir, "PATCH_Big.txt", data);

                bool ok = PatchMetadataCore.TryReadBoundedFileLines(
                    path, cap, maxLines: int.MaxValue, lines: out var lines, bytesRead: out long bytesRead);

                Assert.False(ok);
                Assert.Null(lines);
            }
            finally { Directory.Delete(tempDir, true); }
        }

        [Fact]
        public void TryReadBoundedFileLines_MaxBytesAboveImmutableCeiling_Throws()
        {
            // #1965 L3 correction ("opposite hypothesis" check): a caller invoking the shared
            // helper directly with a maxBytes wider than the immutable MaxPatchDefinitionBytes
            // per-file ceiling must be rejected outright — this is the seam that actually
            // allocates the read buffer, so it is the correct place to enforce the immutable
            // bound regardless of what any upstream caller intended to pass.
            string tempDir = Path.Combine(Path.GetTempPath(), "BoundedHelperCeiling_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                string path = WriteBytes(tempDir, "PATCH_Small.txt", System.Text.Encoding.ASCII.GetBytes("NAME=X\n"));

                Assert.Throws<ArgumentOutOfRangeException>(() =>
                    PatchMetadataCore.TryReadBoundedFileLines(
                        path,
                        maxBytes: PatchMetadataCore.MaxPatchDefinitionBytes + 1,
                        maxLines: int.MaxValue,
                        lines: out _,
                        bytesRead: out _));
            }
            finally { Directory.Delete(tempDir, true); }
        }

        [Fact]
        public void TryReadBoundedFileLines_ExactlyAtCap_SucceedsAndDecodesIdentically()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "BoundedHelperExactCap_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                const int cap = 12; // "NAME=TESTAB" + LF == 12 bytes exactly
                byte[] data = System.Text.Encoding.ASCII.GetBytes("NAME=TESTAB\n");
                Assert.Equal(cap, data.Length);
                string path = WriteBytes(tempDir, "PATCH_Exact.txt", data);

                bool ok = PatchMetadataCore.TryReadBoundedFileLines(
                    path, cap, maxLines: int.MaxValue, lines: out var lines, bytesRead: out long bytesRead);

                Assert.True(ok);
                Assert.Equal(cap, bytesRead);
                Assert.Single(lines);
                Assert.Equal("NAME=TESTAB", lines[0]);
            }
            finally { Directory.Delete(tempDir, true); }
        }

        [Fact]
        public void TryReadBoundedFileLines_ZeroBudget_EmptyFileSucceeds_NonEmptyFileFails()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "BoundedHelperZeroBudget_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                string emptyPath = WriteBytes(tempDir, "PATCH_Empty.txt", Array.Empty<byte>());
                string nonEmptyPath = WriteBytes(tempDir, "PATCH_NonEmpty.txt", new byte[] { 0x41 });

                bool emptyOk = PatchMetadataCore.TryReadBoundedFileLines(
                    emptyPath, 0, maxLines: int.MaxValue, lines: out var emptyLines, bytesRead: out long emptyBytesRead);
                bool nonEmptyOk = PatchMetadataCore.TryReadBoundedFileLines(
                    nonEmptyPath, 0, maxLines: int.MaxValue, lines: out var nonEmptyLines, bytesRead: out long nonEmptyBytesRead);

                Assert.True(emptyOk);
                Assert.Empty(emptyLines);
                Assert.Equal(0, emptyBytesRead);
                Assert.False(nonEmptyOk);
                Assert.Null(nonEmptyLines);
            }
            finally { Directory.Delete(tempDir, true); }
        }

        // Fake FileStream double that reports a huge Length without any backing data actually
        // existing on disk — proves the sparse/huge-Length case is rejected on the Length
        // comparison alone, never by attempting to allocate/read that many bytes.
        sealed class SparseHugeLengthFileStream : FileStream
        {
            readonly long _fakeLength;
            public SparseHugeLengthFileStream(string path, long fakeLength)
                : base(path, FileMode.Open, FileAccess.Read, FileShare.Read)
            {
                _fakeLength = fakeLength;
            }
            public override long Length => _fakeLength;
        }

        [Fact]
        public void TryReadBoundedFileLines_SparseHugeLength_RejectsWithoutReadingBuffer()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "BoundedHelperSparse_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                string path = WriteBytes(tempDir, "PATCH_Sparse.txt", new byte[] { 0x41 });

                bool ok = PatchMetadataCore.TryReadBoundedFileLines(
                    path,
                    maxBytes: 1024,
                    maxLines: int.MaxValue,
                    openFileStreamForTest: p => new SparseHugeLengthFileStream(p, long.MaxValue / 2),
                    lines: out var lines,
                    bytesRead: out long bytesRead);

                Assert.False(ok);
                Assert.Null(lines);
                Assert.Equal(0, bytesRead);
            }
            finally { Directory.Delete(tempDir, true); }
        }

        // Fake FileStream double that under-reports its Length (within the cap) but actually
        // yields MORE bytes than the cap when read — simulates the file growing between the
        // Length check and the read (or a Length that simply lied).
        sealed class GrowthAfterLengthFileStream : FileStream
        {
            readonly long _reportedLength;
            public GrowthAfterLengthFileStream(string path, long reportedLength)
                : base(path, FileMode.Open, FileAccess.Read, FileShare.Read)
            {
                _reportedLength = reportedLength;
            }
            public override long Length => _reportedLength;
        }

        [Fact]
        public void TryReadBoundedFileLines_GrowthAfterLengthCheck_RejectsAtCapPlusOne()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "BoundedHelperGrowth_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                const int cap = 8;
                byte[] realData = System.Text.Encoding.ASCII.GetBytes(new string('b', cap + 5)); // actually bigger than cap
                string path = WriteBytes(tempDir, "PATCH_Growth.txt", realData);

                bool ok = PatchMetadataCore.TryReadBoundedFileLines(
                    path,
                    maxBytes: cap,
                    maxLines: int.MaxValue,
                    // Length under-reports as exactly the cap (within bound), but the handle's
                    // real underlying data is larger — the read loop must catch the growth.
                    openFileStreamForTest: p => new GrowthAfterLengthFileStream(p, cap),
                    lines: out var lines,
                    bytesRead: out long bytesRead);

                Assert.False(ok);
                Assert.Null(lines);
            }
            finally { Directory.Delete(tempDir, true); }
        }

        // #1965 length-drift correction: reuses the SAME GrowthAfterLengthFileStream double
        // above (it only overrides Length — it never changes what is actually read), but with a
        // reported Length that under-reports the real backing data while BOTH the reported
        // Length and the real data stay comfortably under maxBytes. Before this correction, the
        // shared reader only rejected growth that breached maxBytes — growth that merely passed
        // the handle's OWN captured Length, while staying within the caller's byte budget, was
        // silently accepted and decoded, publishing a longer/changed advisory text than the
        // Length actually validated at open time.
        [Fact]
        public void TryReadBoundedFileLines_WithinCapGrowthAfterLengthCheck_RejectsWithSurplusBytesRead()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "BoundedHelperWithinCapGrowth_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                const int reportedLength = 8;
                const int maxBytes = 1024; // comfortably above both the reported length AND the real data below
                byte[] realData = System.Text.Encoding.ASCII.GetBytes(new string('d', reportedLength + 5));
                string path = WriteBytes(tempDir, "PATCH_WithinCapGrowth.txt", realData);

                bool ok = PatchMetadataCore.TryReadBoundedFileLines(
                    path,
                    maxBytes: maxBytes,
                    maxLines: int.MaxValue,
                    // Length under-reports (well within maxBytes), but the handle's real
                    // underlying data is larger than that reported Length — still well within
                    // maxBytes overall. Must still be rejected: the accepted-buffer contract is
                    // exact-length-through-EOF, not "anything up to maxBytes".
                    openFileStreamForTest: p => new GrowthAfterLengthFileStream(p, reportedLength),
                    lines: out var lines,
                    bytesRead: out long bytesRead);

                Assert.False(ok);
                Assert.Null(lines);
                // Deliberately not asserting an exact chunk-size-specific surplus count — only
                // that genuinely-consumed bytes exceeded the (under-reported) captured Length.
                Assert.True(bytesRead > reportedLength,
                    $"Expected bytesRead ({bytesRead}) to exceed the reported length ({reportedLength}).");
            }
            finally { Directory.Delete(tempDir, true); }
        }

        // #1965 length-drift correction: the OPPOSITE direction from the growth case above — the
        // handle reports a Length that OVER-reports what it can actually deliver (the file was
        // truncated/shrunk between the Length read and EOF). Before this correction, the shared
        // reader only checked `total > maxBytes` and `read <= 0` (genuine EOF) — a shorter file
        // than the validated Length decoded successfully as long as it stayed under maxBytes,
        // publishing a shorter/changed advisory text than the Length actually validated.
        [Fact]
        public void TryReadBoundedFileLines_PrematureEofBelowReportedLength_RejectsAndPreservesBytesRead()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "BoundedHelperPrematureEof_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                const int actualLength = 8;
                byte[] realData = System.Text.Encoding.ASCII.GetBytes(new string('c', actualLength));
                string path = WriteBytes(tempDir, "PATCH_PrematureEof.txt", realData);

                bool ok = PatchMetadataCore.TryReadBoundedFileLines(
                    path,
                    maxBytes: 1024,
                    maxLines: int.MaxValue,
                    // Length over-reports by one byte beyond what the handle can actually
                    // deliver. maxBytes sits comfortably above the (over-)reported Length, so
                    // the early Length>maxBytes reject never fires — only the post-loop
                    // total==length equality check can catch this premature EOF.
                    openFileStreamForTest: p => new GrowthAfterLengthFileStream(p, actualLength + 1),
                    lines: out var lines,
                    bytesRead: out long bytesRead);

                Assert.False(ok);
                Assert.Null(lines);
                Assert.Equal(actualLength, bytesRead); // bytes genuinely consumed before the
                                                        // rejection remain visible.
            }
            finally { Directory.Delete(tempDir, true); }
        }

        // ---- #1965: TryParsePatchParamsBounded byte-first coverage ----

        [Fact]
        public void TryParsePatchParamsBounded_SingleLineOverCapPlusOne_FailsWithoutPartialParams()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "ParamsBoundedSingleLine_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                const int cap = 10;
                string path = WriteBytes(tempDir, "PATCH_Big.txt",
                    System.Text.Encoding.ASCII.GetBytes("KEY=" + new string('v', cap)));

                bool ok = PatchMetadataCore.TryParsePatchParamsBounded(
                    path, maxEntries: 100, maxBytes: cap, out var result, out long bytesRead);

                Assert.False(ok);
                Assert.Empty(result);
            }
            finally { Directory.Delete(tempDir, true); }
        }

        [Fact]
        public void TryParsePatchParamsBounded_ParsedEntryCapExact_SucceedsFull_CapPlusOne_ReturnsEmptyNotPartial()
        {
            // #1965 L3 correction: the blocking regression this guards against — `result` was
            // populated directly during parsing and left PARTIALLY filled (up to maxEntries
            // items) on a maxEntries breach, contradicting both the XML doc contract and the
            // caller's whole-record-degradation expectation. `result` must be the EMPTY list
            // constructed at method entry on every false path, never a partial parse.
            string tempDir = Path.Combine(Path.GetTempPath(), "ParamsBoundedEntryCap_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                const int maxEntries = 3;
                string exactContent = string.Concat(Enumerable.Range(0, maxEntries).Select(i => $"KEY{i}=value{i}\n"));
                string exactPath = WriteBytes(tempDir, "PATCH_ExactEntries.txt", System.Text.Encoding.ASCII.GetBytes(exactContent));

                bool okExact = PatchMetadataCore.TryParsePatchParamsBounded(
                    exactPath, maxEntries: maxEntries, maxBytes: PatchMetadataCore.MaxPatchDefinitionBytes,
                    out var resultExact, out _);
                Assert.True(okExact);
                Assert.Equal(maxEntries, resultExact.Count);

                string overContent = string.Concat(Enumerable.Range(0, maxEntries + 1).Select(i => $"KEY{i}=value{i}\n"));
                string overPath = WriteBytes(tempDir, "PATCH_OverEntries.txt", System.Text.Encoding.ASCII.GetBytes(overContent));

                bool okOver = PatchMetadataCore.TryParsePatchParamsBounded(
                    overPath, maxEntries: maxEntries, maxBytes: PatchMetadataCore.MaxPatchDefinitionBytes,
                    out var resultOver, out _);
                Assert.False(okOver);
                Assert.Empty(resultOver); // NOT partially populated with the first `maxEntries` items
            }
            finally { Directory.Delete(tempDir, true); }
        }

        // #1965 length-drift correction: reuses the same GrowthAfterLengthFileStream double
        // used by TryReadBoundedFileLines' own within-cap-growth coverage above, proving the
        // shared reader's length-drift rejection is visible all the way through
        // TryParsePatchParamsBounded — no partial/changed params list is ever produced, and
        // `result` stays the empty list constructed at method entry rather than any params
        // parsed from the (rejected) surplus bytes.
        [Fact]
        public void TryParsePatchParamsBounded_WithinCapGrowthAfterLengthCheck_FailsWithEmptyResult()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "ParamsBoundedWithinCapGrowth_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                const int reportedLength = 8;
                const int maxBytes = 1024;
                byte[] realData = System.Text.Encoding.ASCII.GetBytes("A=1\nB=2\nC=3\n"); // > reportedLength, well within maxBytes
                Assert.True(realData.Length > reportedLength);
                string path = WriteBytes(tempDir, "PATCH_ParamsWithinCapGrowth.txt", realData);

                bool ok = PatchMetadataCore.TryParsePatchParamsBounded(
                    path,
                    maxEntries: int.MaxValue,
                    maxBytes: maxBytes,
                    openFileStreamForTest: p => new GrowthAfterLengthFileStream(p, reportedLength),
                    result: out var result,
                    bytesRead: out long bytesRead);

                Assert.False(ok);
                Assert.Empty(result); // no partial params — never populated from rejected bytes
                Assert.True(bytesRead > reportedLength,
                    $"Expected bytesRead ({bytesRead}) to exceed the reported length ({reportedLength}).");
            }
            finally { Directory.Delete(tempDir, true); }
        }

        [Fact]
        public void TryParsePatchParamsBounded_MissingFile_SucceedsEmpty()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "ParamsBoundedMissing_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                string missing = Path.Combine(tempDir, "PATCH_DoesNotExist.txt");

                bool ok = PatchMetadataCore.TryParsePatchParamsBounded(
                    missing, maxEntries: 100, maxBytes: PatchMetadataCore.MaxPatchDefinitionBytes,
                    out var result, out long bytesRead);

                Assert.True(ok);
                Assert.Empty(result);
                Assert.Equal(0, bytesRead);
            }
            finally { Directory.Delete(tempDir, true); }
        }

        [Fact]
        public void TryParsePatchParamsBounded_MissingParentDirectory_SucceedsEmpty()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "ParamsBoundedMissingDir_" + Guid.NewGuid().ToString("N"));
            // Deliberately do NOT create tempDir — the parent directory itself is missing.
            string missing = Path.Combine(tempDir, "sub", "PATCH_DoesNotExist.txt");

            bool ok = PatchMetadataCore.TryParsePatchParamsBounded(
                missing, maxEntries: 100, maxBytes: PatchMetadataCore.MaxPatchDefinitionBytes,
                out var result, out long bytesRead);

            Assert.True(ok);
            Assert.Empty(result);
        }

        [Theory]
        [InlineData(typeof(UnauthorizedAccessException))]
        [InlineData(typeof(IOException))]
        [InlineData(typeof(System.Security.SecurityException))]
        [InlineData(typeof(PlatformNotSupportedException))]
        public void TryParsePatchParamsBounded_ExpectedFileSystemFault_PropagatesExactExceptionType(Type exceptionType)
        {
            // Deterministic opener-injection (#1965 L3 correction) replaces the prior
            // platform-dependent `Assert.ThrowsAny<Exception>` directory probe (which only
            // reliably throws UnauthorizedAccessException on Windows) — every expected
            // filesystem/access fault class must propagate as its EXACT type from
            // TryParsePatchParamsBounded, on every platform, deterministically.
            // PlatformNotSupportedException (#1965/#1936 correction) covers the new default
            // opener's own Browser classification (ProjectionFileSystemSafety.OpenRegularFileForRead
            // throws PlatformNotSupportedException on Browser instead of an unsafe fallback);
            // this seam-injected double proves that exact type propagates here too, without
            // needing an actual Browser runtime.
            string tempDir = Path.Combine(Path.GetTempPath(), "ParamsBoundedExactFault_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                string path = Path.Combine(tempDir, "PATCH_Fault.txt");
                Func<string, FileStream> throwingOpener = p =>
                    throw (Exception)Activator.CreateInstance(exceptionType, "simulated fault (test double)");

                Assert.Throws(exceptionType, () =>
                    PatchMetadataCore.TryParsePatchParamsBounded(
                        path, maxEntries: 100, maxBytes: PatchMetadataCore.MaxPatchDefinitionBytes,
                        throwingOpener, out _, out _));
            }
            finally { Directory.Delete(tempDir, true); }
        }

        [Fact]
        public void TryReadBoundedFileLines_Utf8Bom_And_Utf16Bom_And_NewlineVarieties_MatchFileReadLines()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "BoundedHelperEncoding_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                // UTF-8 BOM + CRLF/LF/CR mixture + unterminated final line.
                byte[] utf8Bom = { 0xEF, 0xBB, 0xBF };
                string content = "line1\r\nline2\nline3\rline4"; // no trailing newline on line4
                byte[] utf8Data = utf8Bom.Concat(System.Text.Encoding.UTF8.GetBytes(content)).ToArray();
                string utf8Path = WriteBytes(tempDir, "PATCH_Utf8Bom.txt", utf8Data);

                var expected = File.ReadLines(utf8Path).ToList();
                bool ok = PatchMetadataCore.TryReadBoundedFileLines(
                    utf8Path, PatchMetadataCore.MaxPatchDefinitionBytes, maxLines: int.MaxValue, out var actual, out _);

                Assert.True(ok);
                Assert.Equal(expected, actual);

                // UTF-16 LE BOM.
                byte[] utf16Data = System.Text.Encoding.Unicode.GetBytes("\uFEFFhello\r\nworld");
                string utf16Path = WriteBytes(tempDir, "PATCH_Utf16Bom.txt", utf16Data);
                var expected16 = File.ReadLines(utf16Path).ToList();
                bool ok16 = PatchMetadataCore.TryReadBoundedFileLines(
                    utf16Path, PatchMetadataCore.MaxPatchDefinitionBytes, maxLines: int.MaxValue, out var actual16, out _);
                Assert.True(ok16);
                Assert.Equal(expected16, actual16);

                // Invalid UTF-8 byte sequence — StreamReader default (non-strict) replacement
                // fallback must match File.ReadLines exactly (U+FFFD), never throw.
                byte[] invalidUtf8 = { 0x4E, 0x41, 0x4D, 0x45, 0x3D, 0xFF, 0xFE, 0x0A }; // "NAME=" + invalid bytes + LF
                string invalidPath = WriteBytes(tempDir, "PATCH_InvalidUtf8.txt", invalidUtf8);
                var expectedInvalid = File.ReadLines(invalidPath).ToList();
                bool okInvalid = PatchMetadataCore.TryReadBoundedFileLines(
                    invalidPath, PatchMetadataCore.MaxPatchDefinitionBytes, maxLines: int.MaxValue, out var actualInvalid, out _);
                Assert.True(okInvalid);
                Assert.Equal(expectedInvalid, actualInvalid);
            }
            finally { Directory.Delete(tempDir, true); }
        }

        // ---- #1965: aggregate byte-budget breaches (metadata pass + params pass) ----

        [Fact]
        public void TryEnumeratePatchesBounded_AggregateBytesCapPlusOne_ClearsPriorAcceptedRecords()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "PatchAggregateMeta_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                // Two small files whose COMBINED bytes exceed a tiny deterministic aggregate cap,
                // even though neither breaches the (generous) per-file/line bound alone.
                string first = Path.Combine(tempDir, "PATCH_A.txt");
                string second = Path.Combine(tempDir, "PATCH_B.txt");
                File.WriteAllText(first, "NAME=First\n");
                File.WriteAllText(second, "NAME=Second\n");
                long firstLen = new FileInfo(first).Length;
                long secondLen = new FileInfo(second).Length;
                long tinyAggregate = firstLen + secondLen - 1; // one byte short of fitting both

                var rom = new ROM();
                rom.SwapNewROMDataDirect(new byte[0x100]);

                bool success = PatchMetadataCore.TryEnumeratePatchesBounded(
                    tempDir,
                    rom,
                    "en",
                    _ => new[] { first, second },
                    maxFiles: 16384,
                    maxAggregateBytes: tinyAggregate,
                    out var patches,
                    out string error,
                    out bool limitExceeded);

                Assert.False(success);
                Assert.True(limitExceeded);
                Assert.Empty(patches); // the accepted "First" record must NOT survive
                Assert.Contains("line-scan bound", error, StringComparison.Ordinal);
                Assert.DoesNotContain(tempDir, error, StringComparison.Ordinal);
            }
            finally { Directory.Delete(tempDir, true); }
        }

        [Fact]
        public void TryEnumeratePatchesBounded_MaxAggregateBytesAboveImmutableCeiling_Throws()
        {
            // #1965 L3 correction: the parameterized aggregate budget exists ONLY so
            // deterministic tests can exercise a breach with small fixtures — it must never be
            // usable to WIDEN the immutable production ceiling. Proves the "opposite hypothesis"
            // (a caller invoking the bounded helper directly with too-large a limit) is rejected,
            // not silently honored.
            var rom = new ROM();
            rom.SwapNewROMDataDirect(new byte[0x100]);

            Assert.Throws<ArgumentOutOfRangeException>(() =>
                PatchMetadataCore.TryEnumeratePatchesBounded(
                    Path.GetTempPath(),
                    rom,
                    "en",
                    _ => Array.Empty<string>(),
                    maxFiles: 16384,
                    maxAggregateBytes: PatchMetadataCore.MaxMetadataAggregateBytes + 1,
                    out _, out _, out _));
        }

        // ---- #1965 L2 correction: chunked/pooled ingestion, in-loop raw-line cap, and
        // exception-safe incremental byte accounting ----

        // Fake FileStream double that records the largest single `count` ever requested from
        // Read(byte[],int,int) — proves the chunked helper never sizes a single read request to
        // the (16 MiB) production per-file cap, only to the small fixed chunk size.
        sealed class RecordingMaxRequestFileStream : FileStream
        {
            public int MaxRequestedCount { get; private set; }
            public RecordingMaxRequestFileStream(string path)
                : base(path, FileMode.Open, FileAccess.Read, FileShare.Read) { }
            public override int Read(byte[] buffer, int offset, int count)
            {
                MaxRequestedCount = Math.Max(MaxRequestedCount, count);
                return base.Read(buffer, offset, count);
            }
        }

        [Fact]
        public void TryReadBoundedFileLines_TinyFileWithProductionCap_NeverRequestsMoreThanChunkSize()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "BoundedHelperChunkSize_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                string path = WriteBytes(tempDir, "PATCH_Tiny.txt", System.Text.Encoding.ASCII.GetBytes("NAME=Tiny\n"));
                RecordingMaxRequestFileStream captured = null;

                bool ok = PatchMetadataCore.TryReadBoundedFileLines(
                    path,
                    maxBytes: PatchMetadataCore.MaxPatchDefinitionBytes, // production 16 MiB cap
                    maxLines: int.MaxValue,
                    openFileStreamForTest: p => captured = new RecordingMaxRequestFileStream(p),
                    lines: out var lines,
                    bytesRead: out long bytesRead);

                Assert.True(ok);
                Assert.NotNull(captured);
                // The regression this guards against: a naive implementation allocates/requests
                // a buffer sized to the FULL per-file cap for every file, even a 10-byte one.
                Assert.True(captured.MaxRequestedCount <= PatchMetadataCore.ReadChunkBytes);
                Assert.True(captured.MaxRequestedCount < PatchMetadataCore.MaxPatchDefinitionBytes);
            }
            finally { Directory.Delete(tempDir, true); }
        }

        [Fact]
        public void TryReadBoundedFileLines_RawLineCapExact_SucceedsFull_CapPlusOne_RejectsWithoutPartial()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "BoundedHelperRawLineCap_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                const int maxLines = 5;
                string exactContent = string.Concat(Enumerable.Repeat("x\n", maxLines));
                string exactPath = WriteBytes(tempDir, "PATCH_ExactLines.txt", System.Text.Encoding.ASCII.GetBytes(exactContent));

                bool okExact = PatchMetadataCore.TryReadBoundedFileLines(
                    exactPath, maxBytes: 1024, maxLines: maxLines, out var linesExact, out _);
                Assert.True(okExact);
                Assert.Equal(maxLines, linesExact.Count);

                string overContent = string.Concat(Enumerable.Repeat("x\n", maxLines + 1));
                string overPath = WriteBytes(tempDir, "PATCH_OverLines.txt", System.Text.Encoding.ASCII.GetBytes(overContent));

                bool okOver = PatchMetadataCore.TryReadBoundedFileLines(
                    overPath, maxBytes: 1024, maxLines: maxLines, out var linesOver, out _);
                Assert.False(okOver);
                Assert.Null(linesOver); // no partial line list is ever produced on a raw-line breach
            }
            finally { Directory.Delete(tempDir, true); }
        }

        [Fact]
        public void TryParsePatchParamsBounded_NonEntryLinesExceedRawLineCap_RejectsEmptyNotPartial()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "ParamsRawLineCap_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                // None of these lines are ever a parsed KEY=VALUE entry, so the pre-existing
                // maxEntries (parsed-entry) bound never trips — only the raw-line cap can
                // reject this file, proving it protects against an unbounded raw List<string>
                // even when every line is a blank/comment/non-entry line.
                int lineCount = PatchMetadataCore.MaxRawParamLines + 1;
                var sb = new System.Text.StringBuilder();
                for (int i = 0; i < lineCount; i++) sb.Append("// comment\n");
                string path = WriteBytes(tempDir, "PATCH_ManyComments.txt", System.Text.Encoding.ASCII.GetBytes(sb.ToString()));

                bool ok = PatchMetadataCore.TryParsePatchParamsBounded(
                    path, maxEntries: 1_000_000, maxBytes: PatchMetadataCore.MaxPatchDefinitionBytes,
                    out var result, out long bytesRead);

                Assert.False(ok);
            }
            finally { Directory.Delete(tempDir, true); }
        }

        // Fault-injecting FileStream double (see the equivalent duplicated type in
        // BuildfileExportCoreTests — these test fakes are intentionally file-local, no shared
        // test-helper file exists in this repo) that yields exactly N genuine bytes read from a
        // REAL small backing file, then throws IOException on any subsequent read.
        sealed class FaultAfterNBytesFileStream : FileStream
        {
            readonly long _faultAfter;
            long _totalRead;
            public FaultAfterNBytesFileStream(string path, long faultAfter)
                : base(path, FileMode.Open, FileAccess.Read, FileShare.Read)
            {
                _faultAfter = faultAfter;
            }
            public override int Read(byte[] buffer, int offset, int count)
            {
                if (_totalRead >= _faultAfter)
                    throw new IOException("Simulated I/O fault after N bytes (test double).");
                int allowed = (int)Math.Min(count, _faultAfter - _totalRead);
                int read = base.Read(buffer, offset, allowed);
                _totalRead += read;
                return read;
            }
        }

        [Fact]
        public void TryReadBoundedFileLines_FaultAfterNBytes_PreservesBytesReadThroughException()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "BoundedHelperFault_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                const int n = 10;
                byte[] data = System.Text.Encoding.ASCII.GetBytes(new string('a', 50));
                string path = WriteBytes(tempDir, "PATCH_Fault.txt", data);

                IOException caught = null;
                long bytesRead = 0;
                try
                {
                    PatchMetadataCore.TryReadBoundedFileLines(
                        path,
                        maxBytes: 1024,
                        maxLines: int.MaxValue,
                        openFileStreamForTest: p => new FaultAfterNBytesFileStream(p, n),
                        lines: out var lines,
                        bytesRead: out bytesRead);
                }
                catch (IOException ex)
                {
                    caught = ex;
                }

                Assert.NotNull(caught);
                // The bytes genuinely read before the fault must survive via the `out` alias —
                // never reset to 0 — so an aggregate byte budget downstream still accounts for
                // them (#1965 L2 correction).
                Assert.Equal(n, bytesRead);
            }
            finally { Directory.Delete(tempDir, true); }
        }

        // ---- #1965/#1936: default (production) opener rejects a final PATCH_*.txt symlink
        // without ever reading/returning its target's content ----

        [SkippableFact]
        public void TryReadBoundedFileLines_DefaultOpener_FinalSymlink_RejectsWithoutFollowing()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "BoundedSymlink_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            string externalDir = Path.Combine(Path.GetTempPath(), "BoundedSymlinkExternal_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(externalDir);
            try
            {
                const string SentinelValue = "super-secret-target-value-1936";
                string externalTarget = Path.Combine(externalDir, "outside.txt");
                File.WriteAllText(externalTarget, "SECRET_KEY=" + SentinelValue + "\n");

                string linkPath = Path.Combine(tempDir, "PATCH_Link.txt");
                try
                {
                    File.CreateSymbolicLink(linkPath, externalTarget);
                }
                catch (Exception ex)
                {
                    Skip.If(true, "Cannot create a file symlink here: " + ex.Message);
                    return;
                }

                List<string> lines = null;
                long bytesRead = 0;
                Exception caught = Record.Exception(() =>
                    PatchMetadataCore.TryReadBoundedFileLines(
                        linkPath, PatchMetadataCore.MaxPatchDefinitionBytes, maxLines: int.MaxValue,
                        out lines, out bytesRead));

                // The DEFAULT (no injected opener) production path must refuse a final symlink
                // through ProjectionFileSystemSafety.OpenRegularFileForRead — never transparently
                // follow it via a plain FileStream, which would read the external target's bytes.
                Assert.NotNull(caught);
                Assert.IsAssignableFrom<IOException>(caught);
                Assert.DoesNotContain(SentinelValue, caught.Message);
                Assert.DoesNotContain(externalTarget, caught.Message);
                Assert.Null(lines);
            }
            finally
            {
                Directory.Delete(tempDir, true);
                Directory.Delete(externalDir, true);
            }
        }

        [SkippableFact]
        public void TryParsePatchParamsBounded_DefaultOpener_FinalSymlink_RejectsWithoutReadingTarget()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "ParamsBoundedSymlink_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            string externalDir = Path.Combine(Path.GetTempPath(), "ParamsBoundedSymlinkExternal_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(externalDir);
            try
            {
                const string SentinelValue = "super-secret-target-value-1936";
                string externalTarget = Path.Combine(externalDir, "outside.txt");
                File.WriteAllText(externalTarget, "SECRET_KEY=" + SentinelValue + "\n");

                string linkPath = Path.Combine(tempDir, "PATCH_Link.txt");
                try
                {
                    File.CreateSymbolicLink(linkPath, externalTarget);
                }
                catch (Exception ex)
                {
                    Skip.If(true, "Cannot create a file symlink here: " + ex.Message);
                    return;
                }

                List<PatchMetadataCore.PatchParam> result = null;
                long bytesRead = 0;
                Exception caught = Record.Exception(() =>
                    PatchMetadataCore.TryParsePatchParamsBounded(
                        linkPath, maxEntries: 100, maxBytes: PatchMetadataCore.MaxPatchDefinitionBytes,
                        out result, out bytesRead));

                // Per the documented contract, only FileNotFoundException/DirectoryNotFoundException
                // (a genuinely missing final file/parent) resolve to a successful empty result;
                // a rejected symlink is a non-missing IOException that PROPAGATES — it must never
                // resolve to a silent empty/"no params" success that could mask a read that
                // actually happened.
                Assert.NotNull(caught);
                Assert.IsAssignableFrom<IOException>(caught);
                Assert.DoesNotContain(SentinelValue, caught.Message);
                Assert.DoesNotContain(externalTarget, caught.Message);
            }
            finally
            {
                Directory.Delete(tempDir, true);
                Directory.Delete(externalDir, true);
            }
        }
    }
}
