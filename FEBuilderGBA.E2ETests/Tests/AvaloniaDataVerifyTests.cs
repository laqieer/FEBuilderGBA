using FEBuilderGBA.E2ETests.Helpers;
using Xunit;

namespace FEBuilderGBA.E2ETests.Tests
{
    /// <summary>
    /// E2E tests that verify Avalonia GUI editors load and display correct ROM data.
    /// Uses --data-verify mode to open each editor, select the first item, read
    /// ViewModel data, cross-check against raw ROM bytes, and print structured results.
    /// </summary>
    public class AvaloniaDataVerifyTests
    {
        private static readonly string? ExePath = AvaloniaAppRunner.FindExePath();

        /// <summary>
        /// Runs --data-verify mode and verifies all IDataVerifiable editors
        /// report data consistent with raw ROM bytes.
        /// </summary>
        [SkippableTheory]
        [MemberData(nameof(RomLocator.AllRoms), MemberType = typeof(RomLocator))]
        public void Avalonia_DataVerify_AllEditorsPassVerification(string romName, string? romPath)
        {
            Skip.If(ExePath == null, "Avalonia exe not found — build FEBuilderGBA.Avalonia first");
            Skip.If(romPath == null, $"{romName} ROM not available");

            var (exitCode, stdout, stderr) = AvaloniaAppRunner.Run(
                ExePath!, $"--rom \"{romPath}\" --data-verify", timeoutMs: 300_000);

            // Verify the data verify mode ran
            Assert.Contains("DATAVERIFY: Testing", stdout);
            Assert.Contains("DATAVERIFY: Results:", stdout);

            // No failures
            Assert.True(exitCode == 0,
                $"{romName}: Data verify failed with exit code {exitCode}.\n" +
                $"Stdout: {stdout}\nStderr: {stderr}");
        }

        /// <summary>
        /// Verifies that core editors (Unit, Item, Class) produce VERIFY lines.
        /// </summary>
        [SkippableTheory]
        [MemberData(nameof(RomLocator.RepresentativeRoms), MemberType = typeof(RomLocator))]
        public void Avalonia_DataVerify_CoreEditorsProduceVerifyLines(string romName, string? romPath)
        {
            Skip.If(ExePath == null, "Avalonia exe not found — build FEBuilderGBA.Avalonia first");
            Skip.If(romPath == null, $"{romName} ROM not available");

            var (exitCode, stdout, stderr) = AvaloniaAppRunner.Run(
                ExePath!, $"--rom \"{romPath}\" --data-verify", timeoutMs: 300_000);

            // Core editors should produce VERIFY lines
            Assert.Contains("VERIFY: UnitEditorView|", stdout);
            Assert.Contains("VERIFY: ItemEditorView|", stdout);
            Assert.Contains("VERIFY: ClassEditorView|", stdout);

            // And corresponding RAWROM lines
            Assert.Contains("RAWROM: UnitEditorView|", stdout);
            Assert.Contains("RAWROM: ItemEditorView|", stdout);
            Assert.Contains("RAWROM: ClassEditorView|", stdout);
        }

        /// <summary>
        /// Verifies UnitEditorView VERIFY line contains expected fields.
        /// </summary>
        [SkippableTheory]
        [MemberData(nameof(RomLocator.RepresentativeRoms), MemberType = typeof(RomLocator))]
        public void Avalonia_DataVerify_UnitEditorHasExpectedFields(string romName, string? romPath)
        {
            Skip.If(ExePath == null, "Avalonia exe not found — build FEBuilderGBA.Avalonia first");
            Skip.If(romPath == null, $"{romName} ROM not available");

            var (exitCode, stdout, stderr) = AvaloniaAppRunner.Run(
                ExePath!, $"--rom \"{romPath}\" --data-verify", timeoutMs: 300_000);

            // Find the VERIFY line for UnitEditorView
            var lines = stdout.Split('\n');
            var verifyLine = System.Array.Find(lines, l => l.StartsWith("VERIFY: UnitEditorView|"));
            Assert.NotNull(verifyLine);

            // Must contain key fields
            Assert.Contains("listCount=", verifyLine);
            Assert.Contains("NameId=", verifyLine);
            Assert.Contains("ClassId=", verifyLine);
            Assert.Contains("Level=", verifyLine);
            Assert.Contains("HP=", verifyLine);
        }

        /// <summary>
        /// Verifies the data-verify mode reports at least 3 verified editors.
        /// </summary>
        [SkippableTheory]
        [MemberData(nameof(RomLocator.RepresentativeRoms), MemberType = typeof(RomLocator))]
        public void Avalonia_DataVerify_ReportsVerifiedCount(string romName, string? romPath)
        {
            Skip.If(ExePath == null, "Avalonia exe not found — build FEBuilderGBA.Avalonia first");
            Skip.If(romPath == null, $"{romName} ROM not available");

            var (exitCode, stdout, stderr) = AvaloniaAppRunner.Run(
                ExePath!, $"--rom \"{romPath}\" --data-verify", timeoutMs: 300_000);

            // Must have at least 3 verified (Unit, Item, Class)
            Assert.Contains("DATAVERIFY: UnitEditorView ... VERIFIED", stdout);
            Assert.Contains("DATAVERIFY: ItemEditorView ... VERIFIED", stdout);
            Assert.Contains("DATAVERIFY: ClassEditorView ... VERIFIED", stdout);
        }

        /// <summary>
        /// Verifies NumericUpDown controls in core editors display actual values (not empty).
        /// This catches the FormatString="X" bug where decimal.ToString("X") throws
        /// FormatException, causing all NumericUpDown controls to show empty text.
        /// </summary>
        [SkippableTheory]
        [MemberData(nameof(RomLocator.RepresentativeRoms), MemberType = typeof(RomLocator))]
        public void Avalonia_DataVerify_NumericUpDownsDisplayValues(string romName, string? romPath)
        {
            Skip.If(ExePath == null, "Avalonia exe not found — build FEBuilderGBA.Avalonia first");
            Skip.If(romPath == null, $"{romName} ROM not available");

            var (exitCode, stdout, stderr) = AvaloniaAppRunner.Run(
                ExePath!, $"--rom \"{romPath}\" --data-verify", timeoutMs: 300_000);

            // Core editors (Unit/Item/Class) exist in all ROM versions
            Assert.Contains("UIVERIFY: UnitEditorView|", stdout);
            Assert.Contains("UIVERIFY: ItemEditorView|", stdout);
            Assert.Contains("UIVERIFY: ClassEditorView|", stdout);

            // Core editors must NOT have emptyNUDs
            Assert.DoesNotContain("UIVERIFY: UnitEditorView|emptyNUDs=", stdout);
            Assert.DoesNotContain("UIVERIFY: ItemEditorView|emptyNUDs=", stdout);
            Assert.DoesNotContain("UIVERIFY: ClassEditorView|emptyNUDs=", stdout);

            // No UI_EMPTY failures for any editor
            // (editors with no data for the current ROM skip the UI check)
            Assert.DoesNotContain("UI_EMPTY", stdout);
        }

        /// <summary>
        /// Verifies that CCBranch and TerrainName editors display NumericUpDown values
        /// on FE8U (where both editors have data). Some ROM versions (FE6) may not
        /// have CC Branch data, so this test only runs against FE8U.
        /// </summary>
        [SkippableFact]
        public void Avalonia_DataVerify_CCBranchAndTerrainDisplayValues_FE8U()
        {
            Skip.If(ExePath == null, "Avalonia exe not found — build FEBuilderGBA.Avalonia first");
            var fe8uPath = RomLocator.FE8U;
            Skip.If(fe8uPath == null, "FE8U ROM not available");

            var (exitCode, stdout, stderr) = AvaloniaAppRunner.Run(
                ExePath!, $"--rom \"{fe8uPath}\" --data-verify", timeoutMs: 300_000);

            // On FE8U, both editors have data
            Assert.Contains("UIVERIFY: CCBranchEditorView|", stdout);
            Assert.Contains("UIVERIFY: TerrainNameEditorView|", stdout);

            Assert.DoesNotContain("UIVERIFY: CCBranchEditorView|emptyNUDs=", stdout);
            Assert.DoesNotContain("UIVERIFY: TerrainNameEditorView|emptyNUDs=", stdout);
        }

        /// <summary>
        /// Verifies that text encoding is correctly initialized for all ROM types.
        /// The TEXTVERIFY line should report the encoder type and status=OK.
        /// </summary>
        [SkippableTheory]
        [MemberData(nameof(RomLocator.AllRoms), MemberType = typeof(RomLocator))]
        public void Avalonia_DataVerify_TextEncodingInitialized(string romName, string? romPath)
        {
            Skip.If(ExePath == null, "Avalonia exe not found — build FEBuilderGBA.Avalonia first");
            Skip.If(romPath == null, $"{romName} ROM not available");

            var (exitCode, stdout, stderr) = AvaloniaAppRunner.Run(
                ExePath!, $"--rom \"{romPath}\" --data-verify", timeoutMs: 300_000);

            // Must have TEXTVERIFY output
            Assert.Contains("TEXTVERIFY: encoder=", stdout);

            // Text decode must not produce replacement characters
            Assert.DoesNotContain("status=REPLACEMENT_CHARS", stdout);

            // Text decode must not be empty (text ID 1 always exists)
            Assert.DoesNotContain("status=EMPTY", stdout);
        }

        /// <summary>
        /// Verifies that Japanese ROMs (FE8J) decode text with actual CJK characters,
        /// not garbled Latin characters from wrong encoding.
        /// </summary>
        [SkippableFact]
        public void Avalonia_DataVerify_JapaneseTextHasCJK_FE8J()
        {
            Skip.If(ExePath == null, "Avalonia exe not found — build FEBuilderGBA.Avalonia first");
            var fe8jPath = RomLocator.FE8J;
            Skip.If(fe8jPath == null, "FE8J ROM not available");

            var (exitCode, stdout, stderr) = AvaloniaAppRunner.Run(
                ExePath!, $"--rom \"{fe8jPath}\" --data-verify", timeoutMs: 300_000);

            // Must have TEXTVERIFY lines
            Assert.Contains("TEXTVERIFY: encoder=", stdout);
            Assert.Contains("is_multibyte=True", stdout);

            // Japanese ROM must decode to CJK characters, not garbled Latin
            Assert.DoesNotContain("status=NO_CJK", stdout);
            Assert.DoesNotContain("status=REPLACEMENT_CHARS", stdout);
            Assert.Contains("hasCJK=True", stdout);
        }

        /// <summary>
        /// Verifies that FE6 (Japanese) ROM text decoding works correctly.
        /// </summary>
        [SkippableFact]
        public void Avalonia_DataVerify_JapaneseTextHasCJK_FE6()
        {
            Skip.If(ExePath == null, "Avalonia exe not found — build FEBuilderGBA.Avalonia first");
            var fe6Path = RomLocator.FE6;
            Skip.If(fe6Path == null, "FE6 ROM not available");

            var (exitCode, stdout, stderr) = AvaloniaAppRunner.Run(
                ExePath!, $"--rom \"{fe6Path}\" --data-verify", timeoutMs: 300_000);

            Assert.Contains("TEXTVERIFY: encoder=", stdout);
            Assert.Contains("is_multibyte=True", stdout);
            Assert.DoesNotContain("status=REPLACEMENT_CHARS", stdout);
            Assert.Contains("hasCJK=True", stdout);
        }

        /// <summary>
        /// Verifies that FE8U (English) ROM text decoding works correctly.
        /// English ROMs should not have CJK but should decode OK.
        /// </summary>
        [SkippableFact]
        public void Avalonia_DataVerify_EnglishTextDecodes_FE8U()
        {
            Skip.If(ExePath == null, "Avalonia exe not found — build FEBuilderGBA.Avalonia first");
            var fe8uPath = RomLocator.FE8U;
            Skip.If(fe8uPath == null, "FE8U ROM not available");

            var (exitCode, stdout, stderr) = AvaloniaAppRunner.Run(
                ExePath!, $"--rom \"{fe8uPath}\" --data-verify", timeoutMs: 300_000);

            Assert.Contains("TEXTVERIFY: encoder=", stdout);
            Assert.Contains("is_multibyte=False", stdout);
            Assert.Contains("status=OK", stdout);
            Assert.DoesNotContain("status=REPLACEMENT_CHARS", stdout);
        }
    }
}

