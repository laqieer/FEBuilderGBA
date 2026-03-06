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
            // The deterministic method should not use DateTime in the filename
            // (the original CaptureWindow uses DateTime.UtcNow for unique filenames)
            string source = ScreenshotHelperSource;

            // Find the CaptureWindowDeterministic method body
            int methodStart = source.IndexOf("CaptureWindowDeterministic");
            Assert.True(methodStart > 0, "CaptureWindowDeterministic method not found");

            // Get ~40 lines after method start to check it doesn't use DateTime for filename
            int nextMethod = source.IndexOf("public static string? CaptureWindow(", methodStart);
            string deterministicBody = source.Substring(methodStart,
                nextMethod > methodStart ? nextMethod - methodStart : 500);

            // The deterministic method should use {safeName}.png without timestamp
            Assert.Contains("{safeName}.png", deterministicBody);
            Assert.DoesNotContain("DateTime.UtcNow", deterministicBody);
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
