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

            // Core editors with NumericUpDown must have UIVERIFY lines showing OK
            Assert.Contains("UIVERIFY: UnitEditorView|", stdout);
            Assert.Contains("UIVERIFY: ItemEditorView|", stdout);
            Assert.Contains("UIVERIFY: ClassEditorView|", stdout);

            // None should have emptyNUDs
            Assert.DoesNotContain("UIVERIFY: UnitEditorView|emptyNUDs=", stdout);
            Assert.DoesNotContain("UIVERIFY: ItemEditorView|emptyNUDs=", stdout);
            Assert.DoesNotContain("UIVERIFY: ClassEditorView|emptyNUDs=", stdout);

            // No UI_EMPTY failures
            Assert.DoesNotContain("UI_EMPTY", stdout);
        }

        /// <summary>
        /// Verifies that CCBranch and TerrainName editors also display NumericUpDown values.
        /// These editors had FormatString="X" on ALL their NumericUpDown controls.
        /// </summary>
        [SkippableTheory]
        [MemberData(nameof(RomLocator.RepresentativeRoms), MemberType = typeof(RomLocator))]
        public void Avalonia_DataVerify_CCBranchAndTerrainDisplayValues(string romName, string? romPath)
        {
            Skip.If(ExePath == null, "Avalonia exe not found — build FEBuilderGBA.Avalonia first");
            Skip.If(romPath == null, $"{romName} ROM not available");

            var (exitCode, stdout, stderr) = AvaloniaAppRunner.Run(
                ExePath!, $"--rom \"{romPath}\" --data-verify", timeoutMs: 300_000);

            // These editors should also pass UIVERIFY
            Assert.Contains("UIVERIFY: CCBranchEditorView|", stdout);
            Assert.Contains("UIVERIFY: TerrainNameEditorView|", stdout);

            Assert.DoesNotContain("UIVERIFY: CCBranchEditorView|emptyNUDs=", stdout);
            Assert.DoesNotContain("UIVERIFY: TerrainNameEditorView|emptyNUDs=", stdout);
        }
    }
}
