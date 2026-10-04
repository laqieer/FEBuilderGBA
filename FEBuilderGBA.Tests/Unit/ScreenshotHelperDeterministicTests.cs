using System.IO;
using System.Text.RegularExpressions;

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
        public void ScreenshotHelper_DeterministicHasNoTimestamp() =>
            AssertDeterministicCaptureContract(ScreenshotHelperSource);

        [Theory]
        [InlineData("private static void RequireOwnedWindow")]
        [InlineData("private static CaptureProcessIdentity RequireOwnedWindow")]
        [InlineData("private static CaptureProcessIdentity RequireCaptureAdmission")]
        [InlineData("internal static string AnotherHelper")]
        [InlineData("public static string AnotherHelper")]
        public void DeterministicContract_ScopesCaptureIndependentlyOfFollowingMember(string declaration)
        {
            AssertDeterministicCaptureContract(CreateCaptureSource(declaration));
        }

        [Theory]
        [InlineData(false, true)]
        [InlineData(true, false)]
        [InlineData(false, false)]
        public void DeterministicContract_RejectsFilenameRulesOnlyInFollowingMember(
            bool hasSuffix, bool hasPath)
        {
            string source = CreateCaptureSource(
                "private static void RequireOwnedWindow", hasSuffix, hasPath);

            Assert.Throws<Xunit.Sdk.ContainsException>(() => AssertDeterministicCaptureContract(source));
        }

        [Fact]
        public void DeterministicContract_RejectsTimestampedWrapper()
        {
            string source = CreateCaptureSource("private static void RequireOwnedWindow")
                .Replace("OutputDirectory, false,", "OutputDirectory, true,", StringComparison.Ordinal);

            Assert.Throws<Xunit.Sdk.ContainsException>(() => AssertDeterministicCaptureContract(source));
        }

        [Theory]
        [InlineData("FormSmokeTests.cs")]
        [InlineData("WinFormsScreenshotAllTests.cs")]
        public void ToolbarCapture_KeepsIndependentAllHandleLifecycleBaseline(string fileName)
        {
            string source = File.ReadAllText(Path.Combine(
                SolutionDir, "FEBuilderGBA.E2ETests", "Tests", fileName));
            string loop = ToolInitWizardDownloadRouteTests.ExtractMethodBody(
                source, "foreach (var (btnHWnd, btnText) in buttons)");
            Match capture = Regex.Match(loop,
                @"WaitForNewCaptureWindows\(\s*_process\.Id,\s*(?<baseline>\w+)\s*,");
            Match cleanup = Regex.Match(loop,
                @"CloseUnexpectedWindows\(\s*_process\.Id,\s*(?<baseline>\w+)\s*\)");

            Assert.True(capture.Success, "Capture discovery baseline not found.");
            Assert.True(cleanup.Success, "Lifecycle cleanup baseline not found.");
            string captureSet = capture.Groups["baseline"].Value;
            string lifecycleSet = cleanup.Groups["baseline"].Value;
            Assert.NotEqual(captureSet, lifecycleSet);

            MatchCollection snapshots = Regex.Matches(loop,
                @"var\s+(?<name>\w+)\s*=\s*new\s+HashSet<IntPtr>\(WinAutomation\." +
                @"(?<probe>GetCaptureWindows|GetProcessWindows)\(_process\.Id\)\);");
            Match captureSnapshot = Assert.Single(
                snapshots, snapshot => snapshot.Groups["name"].Value == captureSet);
            Match lifecycleSnapshot = Assert.Single(
                snapshots, snapshot => snapshot.Groups["name"].Value == lifecycleSet);
            Assert.Equal("GetCaptureWindows", captureSnapshot.Groups["probe"].Value);
            Assert.Equal("GetProcessWindows", lifecycleSnapshot.Groups["probe"].Value);
            Assert.True(lifecycleSnapshot.Index < loop.IndexOf(
                "WinAutomation.ClickButton(", StringComparison.Ordinal));
        }

        private static void AssertDeterministicCaptureContract(string source)
        {
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
            string captureBody = ToolInitWizardDownloadRouteTests.ExtractMethodBody(
                source, "internal static string CaptureWindow(");

            Assert.Contains("string suffix = timestamp ? $\"_{DateTime.UtcNow:yyyyMMdd_HHmmss_fff}\" : \"\";",
                captureBody);
            Assert.Contains("string path = Path.Combine(outputDir, $\"{SanitizeFileName(name)}{suffix}.png\");",
                captureBody);
        }

        private static string CreateCaptureSource(
            string followingDeclaration, bool hasSuffix = true, bool hasPath = true)
        {
            const string suffix =
                "string suffix = timestamp ? $\"_{DateTime.UtcNow:yyyyMMdd_HHmmss_fff}\" : \"\";";
            const string path =
                "string path = Path.Combine(outputDir, $\"{SanitizeFileName(name)}{suffix}.png\");";

            return $$"""
                public static string CaptureWindowDeterministic(
                    Process process, IntPtr hWnd, string name, string? outputDir = null) =>
                    CaptureWindow(process, hWnd, name, outputDir ?? OutputDirectory, false,
                        probe, native);

                internal static string CaptureWindow(
                    Process process, IntPtr hWnd, string name, string outputDir, bool timestamp)
                {
                    string text = "} private static void RequireOwnedWindow(";
                    char brace = '{';
                    // } unmatched comment brace
                    /* { unmatched comment brace */
                    if (timestamp) { text = "nested"; }
                    {{(hasSuffix ? suffix : "")}}
                    {{(hasPath ? path : "")}}
                    return path;
                }

                {{followingDeclaration}}()
                {
                    {{suffix}}
                    {{path}}
                }
                """;
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
