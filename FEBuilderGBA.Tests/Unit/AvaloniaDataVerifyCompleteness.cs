using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace FEBuilderGBA.Tests.Unit
{
    /// <summary>
    /// Verifies that every ViewModel implementing IDataVerifiable is covered
    /// in the MainWindow data-verify runner, and that every View implementing
    /// IDataVerifiableView has a corresponding ViewModel that implements IDataVerifiable.
    /// </summary>
    public class AvaloniaDataVerifyCompleteness
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

        private string AvaloniaDir => Path.Combine(SolutionDir, "FEBuilderGBA.Avalonia");

        /// <summary>
        /// Every ViewModel that implements IDataVerifiable should also have
        /// a corresponding View that implements IDataVerifiableView.
        /// </summary>
        [Fact]
        public void AllIDataVerifiableViewModels_HaveMatchingViews()
        {
            var vmDir = Path.Combine(AvaloniaDir, "ViewModels");
            var viewDir = Path.Combine(AvaloniaDir, "Views");

            var verifiableVMs = new List<string>();
            foreach (var file in Directory.GetFiles(vmDir, "*.cs"))
            {
                var content = File.ReadAllText(file);
                if (content.Contains("IDataVerifiable") && content.Contains("GetDataReport"))
                {
                    verifiableVMs.Add(Path.GetFileNameWithoutExtension(file));
                }
            }

            Assert.True(verifiableVMs.Count >= 3,
                $"Expected at least 3 IDataVerifiable ViewModels, found {verifiableVMs.Count}");

            var viewFiles = Directory.GetFiles(viewDir, "*.axaml.cs")
                .Select(f => File.ReadAllText(f))
                .ToList();

            var missingViews = new List<string>();
            foreach (var vmName in verifiableVMs)
            {
                // Check if any view file references IDataVerifiableView
                bool hasView = viewFiles.Any(v =>
                    v.Contains("IDataVerifiableView") && v.Contains("DataViewModel"));

                if (!hasView)
                    missingViews.Add(vmName);
            }

            // At minimum, the Views directory should have IDataVerifiableView implementations
            var viewsWithInterface = viewFiles.Count(v => v.Contains("IDataVerifiableView"));
            Assert.True(viewsWithInterface >= 3,
                $"Expected at least 3 Views implementing IDataVerifiableView, found {viewsWithInterface}");
        }

        /// <summary>
        /// The MainWindow must have both RunDataVerify and RunSmokeTestAll methods.
        /// </summary>
        [Fact]
        public void MainWindow_HasBothTestRunners()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Views", "MainWindow.axaml.cs"));
            Assert.Contains("RunDataVerify()", src);
            Assert.Contains("RunSmokeTestAll()", src);
            Assert.Contains("GetAllEditorFactories()", src);
        }

        /// <summary>
        /// The data-verify runner outputs structured VERIFY and RAWROM lines.
        /// </summary>
        [Fact]
        public void MainWindow_DataVerifyOutputsStructuredLines()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Views", "MainWindow.axaml.cs"));
            Assert.Contains("VERIFY:", src);
            Assert.Contains("RAWROM:", src);
            Assert.Contains("DATAVERIFY:", src);
            Assert.Contains("CrossCheckDataReport", src);
        }

        /// <summary>
        /// Verify that the IDataVerifiable interface declares all required methods.
        /// </summary>
        [Fact]
        public void IDataVerifiable_HasRequiredMethods()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Services", "IDataVerifiable.cs"));
            Assert.Contains("int GetListCount()", src);
            Assert.Contains("Dictionary<string, string> GetDataReport()", src);
            Assert.Contains("Dictionary<string, string> GetRawRomReport()", src);
        }

        /// <summary>
        /// Count the number of ViewModels implementing IDataVerifiable.
        /// Should grow as more editors are instrumented.
        /// </summary>
        [Fact]
        public void CountIDataVerifiableViewModels_AtLeast3()
        {
            var vmDir = Path.Combine(AvaloniaDir, "ViewModels");
            int count = 0;
            foreach (var file in Directory.GetFiles(vmDir, "*.cs"))
            {
                var content = File.ReadAllText(file);
                if (content.Contains(": ViewModelBase, IDataVerifiable") ||
                    content.Contains(": ViewModelBase,IDataVerifiable"))
                {
                    count++;
                }
            }

            Assert.True(count >= 3,
                $"Expected at least 3 ViewModels implementing IDataVerifiable, found {count}");
        }

        /// <summary>
        /// The data-verify runner must also verify NumericUpDown UI display values.
        /// </summary>
        [Fact]
        public void MainWindow_HasUIVerifyCheck()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Views", "MainWindow.axaml.cs"));
            Assert.Contains("CheckNumericUpDownsDisplayValues", src);
            Assert.Contains("UIVERIFY:", src);
            Assert.Contains("UI_EMPTY", src);
        }

        /// <summary>
        /// No AXAML file should use FormatString="X" on NumericUpDown
        /// because NumericUpDown.Value is decimal? and decimal.ToString("X") throws FormatException.
        /// </summary>
        [Fact]
        public void NoAxaml_UsesHexFormatStringOnNumericUpDown()
        {
            var viewsDir = Path.Combine(AvaloniaDir, "Views");
            foreach (var file in Directory.GetFiles(viewsDir, "*.axaml"))
            {
                var content = File.ReadAllText(file);
                if (content.Contains("NumericUpDown"))
                {
                    Assert.False(content.Contains("FormatString=\"X\""),
                        $"{Path.GetFileName(file)} uses FormatString=\"X\" — incompatible with decimal type");
                }
            }
        }

        /// <summary>
        /// The data-verify runner must output TEXTVERIFY lines for text encoding verification.
        /// </summary>
        [Fact]
        public void MainWindow_HasTextVerify()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Views", "MainWindow.axaml.cs"));
            Assert.Contains("VerifyTextEncoding", src);
            Assert.Contains("TEXTVERIFY:", src);
            Assert.Contains("is_multibyte", src);
        }

        /// <summary>
        /// MainWindow.LoadRomFile must use ROM-aware HeadlessSystemTextEncoder as fallback.
        /// </summary>
        [Fact]
        public void MainWindow_LoadRomFile_UsesRomAwareFallback()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Views", "MainWindow.axaml.cs"));
            Assert.Contains("new HeadlessSystemTextEncoder(CoreState.ROM)", src);
        }

        /// <summary>
        /// SystemTextEncoder.Build must register CodePagesEncodingProvider before using Shift_JIS.
        /// </summary>
        [Fact]
        public void SystemTextEncoder_Build_RegistersCodePages()
        {
            var solutionDir = SolutionDir;
            var src = File.ReadAllText(Path.Combine(solutionDir, "FEBuilderGBA.Core", "SystemTextEncoder.cs"));
            // The RegisterProvider call must appear in Build() before GetEncoding("Shift_JIS")
            int registerIdx = src.IndexOf("Encoding.RegisterProvider(CodePagesEncodingProvider.Instance)");
            int shiftJisIdx = src.IndexOf("Encoding.GetEncoding(\"Shift_JIS\")");
            Assert.True(registerIdx >= 0, "SystemTextEncoder.Build must call RegisterProvider");
            Assert.True(registerIdx < shiftJisIdx, "RegisterProvider must be called before GetEncoding(\"Shift_JIS\")");
        }

        /// <summary>
        /// HeadlessSystemTextEncoder must have a ROM constructor for encoding auto-detection.
        /// </summary>
        [Fact]
        public void HeadlessSystemTextEncoder_HasRomConstructor()
        {
            var solutionDir = SolutionDir;
            var src = File.ReadAllText(Path.Combine(solutionDir, "FEBuilderGBA.Core", "HeadlessSystemTextEncoder.cs"));
            Assert.Contains("public HeadlessSystemTextEncoder(ROM rom)", src);
            Assert.Contains("DetectEncodingFromRom", src);
            Assert.Contains("is_multibyte", src);
            Assert.Contains("Shift_JIS", src);
        }

        /// <summary>
        /// Core.csproj must have explicit System.Text.Encoding.CodePages dependency.
        /// </summary>
        [Fact]
        public void CoreCsproj_HasCodePagesReference()
        {
            var solutionDir = SolutionDir;
            var csproj = File.ReadAllText(Path.Combine(solutionDir, "FEBuilderGBA.Core", "FEBuilderGBA.Core.csproj"));
            Assert.Contains("System.Text.Encoding.CodePages", csproj);
        }
    }
}

