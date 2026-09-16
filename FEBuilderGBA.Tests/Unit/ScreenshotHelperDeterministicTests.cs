using System.IO;

namespace FEBuilderGBA.Tests.Unit
{
    /// <summary>
    /// Tests for ScreenshotHelper filename sanitization and deterministic filename patterns.
    /// Verifies the E2E screenshot helpers via source-code inspection.
    /// </summary>
    public class ScreenshotHelperDeterministicTests
    {
        private static string SolutionDir
        {
            get
            {
                var dir = AppContext.BaseDirectory;
                while (dir != null && !File.Exists(Path.Combine(dir, "FEBuilderGBA.sln")))
                    dir = Path.GetDirectoryName(dir);
                return dir ?? throw new InvalidOperationException("Cannot find solution root");
            }
        }

        private string ScreenshotHelperSource => File.ReadAllText(
            Path.Combine(SolutionDir, "FEBuilderGBA.E2ETests", "Helpers", "ScreenshotHelper.cs"));

        [Fact]
        public void ScreenshotHelper_HasSanitizeFileNameMethod()
        {
            Assert.Contains("public static string SanitizeFileName", ScreenshotHelperSource);
        }

        [Fact]
        public void ScreenshotHelper_HasCaptureWindowDeterministicMethod()
        {
            Assert.Contains("CaptureWindowDeterministic", ScreenshotHelperSource);
        }

        [Fact]
        public void ScreenshotHelper_DeterministicHasNoTimestamp()
        {
            string source = ScreenshotHelperSource;

            int methodStart = source.IndexOf("public static string CaptureWindowDeterministic(",
                StringComparison.Ordinal);
            Assert.True(methodStart >= 0, "CaptureWindowDeterministic method not found");
            int methodEnd = source.IndexOf(";", methodStart, StringComparison.Ordinal);
            Assert.True(methodEnd > methodStart, "CaptureWindowDeterministic expression terminator not found");
            string deterministicBody = source.Substring(methodStart, methodEnd - methodStart + 1);

            Assert.Contains("CaptureWindow(process, hWnd, name, outputDir ?? OutputDirectory, false,",
                deterministicBody);
            Assert.DoesNotContain("DateTime.UtcNow", deterministicBody);

            int captureStart = source.IndexOf("internal static string CaptureWindow(", StringComparison.Ordinal);
            Assert.True(captureStart >= 0, "Shared CaptureWindow implementation not found");
            int captureEnd = source.IndexOf("private static void RequireOwnedWindow(",
                captureStart, StringComparison.Ordinal);
            Assert.True(captureEnd > captureStart, "Shared CaptureWindow implementation boundary not found");
            string captureBody = source.Substring(captureStart, captureEnd - captureStart);

            Assert.Contains("string suffix = timestamp ? $\"_{DateTime.UtcNow:yyyyMMdd_HHmmss_fff}\" : \"\";",
                captureBody);
            Assert.Contains("string path = Path.Combine(outputDir, $\"{SanitizeFileName(name)}{suffix}.png\");",
                captureBody);
        }

        [Fact]
        public void ScreenshotHelper_DeterministicAcceptsOutputDir()
        {
            Assert.Contains("string? outputDir", ScreenshotHelperSource);
        }

        [Fact]
        public void ScreenshotHelper_SanitizeUsesInvalidFileNameChars()
        {
            Assert.Contains("GetInvalidFileNameChars", ScreenshotHelperSource);
        }

        [Fact]
        public void ScreenshotHelper_HasOutputDirectoryProperty()
        {
            Assert.Contains("FEBUILDERGBA_SCREENSHOT_DIR", ScreenshotHelperSource);
            Assert.Contains("public static string OutputDirectory", ScreenshotHelperSource);
        }
    }
}
