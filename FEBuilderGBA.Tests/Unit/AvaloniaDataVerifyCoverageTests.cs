using System.IO;
using System.Linq;
using Xunit;

namespace FEBuilderGBA.Tests.Unit
{
    /// <summary>
    /// Source-code verification tests that ensure IDataVerifiable is properly
    /// implemented across Avalonia ViewModels and exposed via IDataVerifiableView.
    /// </summary>
    public class AvaloniaDataVerifyCoverageTests
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

        // ------------------------------------------------------------------ IDataVerifiable interface

        [Fact]
        public void IDataVerifiable_InterfaceExists()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Services", "IDataVerifiable.cs"));
            Assert.Contains("interface IDataVerifiable", src);
            Assert.Contains("GetListCount()", src);
            Assert.Contains("GetDataReport()", src);
            Assert.Contains("GetRawRomReport()", src);
        }

        [Fact]
        public void IDataVerifiableView_InterfaceExists()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Services", "IDataVerifiable.cs"));
            Assert.Contains("interface IDataVerifiableView", src);
            Assert.Contains("DataViewModel", src);
        }

        // ------------------------------------------------------------------ Core editors implement IDataVerifiable

        [Fact]
        public void UnitEditorViewModel_ImplementsIDataVerifiable()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "ViewModels", "UnitEditorViewModel.cs"));
            Assert.Contains("IDataVerifiable", src);
            Assert.Contains("GetListCount()", src);
            Assert.Contains("GetDataReport()", src);
            Assert.Contains("GetRawRomReport()", src);
        }

        [Fact]
        public void ItemEditorViewModel_ImplementsIDataVerifiable()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "ViewModels", "ItemEditorViewModel.cs"));
            Assert.Contains("IDataVerifiable", src);
            Assert.Contains("GetListCount()", src);
            Assert.Contains("GetDataReport()", src);
            Assert.Contains("GetRawRomReport()", src);
        }

        [Fact]
        public void ClassEditorViewModel_ImplementsIDataVerifiable()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "ViewModels", "ClassEditorViewModel.cs"));
            Assert.Contains("IDataVerifiable", src);
            Assert.Contains("GetListCount()", src);
            Assert.Contains("GetDataReport()", src);
            Assert.Contains("GetRawRomReport()", src);
        }

        // ------------------------------------------------------------------ Views implement IDataVerifiableView

        [Fact]
        public void UnitEditorView_ImplementsIDataVerifiableView()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Views", "UnitEditorView.axaml.cs"));
            Assert.Contains("IDataVerifiableView", src);
            Assert.Contains("DataViewModel", src);
        }

        [Fact]
        public void ItemEditorView_ImplementsIDataVerifiableView()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Views", "ItemEditorView.axaml.cs"));
            Assert.Contains("IDataVerifiableView", src);
            Assert.Contains("DataViewModel", src);
        }

        [Fact]
        public void ClassEditorView_ImplementsIDataVerifiableView()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Views", "ClassEditorView.axaml.cs"));
            Assert.Contains("IDataVerifiableView", src);
            Assert.Contains("DataViewModel", src);
        }

        // ------------------------------------------------------------------ App supports --data-verify

        [Fact]
        public void App_SupportsDataVerifyArg()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "App.axaml.cs"));
            Assert.Contains("--data-verify", src);
            Assert.Contains("DataVerifyMode", src);
        }

        // ------------------------------------------------------------------ MainWindow has RunDataVerify

        [Fact]
        public void MainWindow_HasRunDataVerifyMethod()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Views", "MainWindow.axaml.cs"));
            Assert.Contains("RunDataVerify()", src);
            Assert.Contains("DATAVERIFY:", src);
            Assert.Contains("VERIFY:", src);
            Assert.Contains("RAWROM:", src);
        }

        [Fact]
        public void MainWindow_DataVerifyBranchesFromSmokeTest()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Views", "MainWindow.axaml.cs"));
            Assert.Contains("App.DataVerifyMode", src);
        }

        // ------------------------------------------------------------------ Data report field coverage

        [Fact]
        public void UnitEditorViewModel_DataReportIncludesKeyFields()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "ViewModels", "UnitEditorViewModel.cs"));
            // GetDataReport must include these field names
            Assert.Contains("[\"NameId\"]", src);
            Assert.Contains("[\"ClassId\"]", src);
            Assert.Contains("[\"Level\"]", src);
            Assert.Contains("[\"HP\"]", src);
            Assert.Contains("[\"Str\"]", src);
            Assert.Contains("[\"Skl\"]", src);
            Assert.Contains("[\"Spd\"]", src);
            Assert.Contains("[\"Def\"]", src);
            Assert.Contains("[\"Res\"]", src);
            Assert.Contains("[\"Lck\"]", src);
            Assert.Contains("[\"Con\"]", src);
        }

        [Fact]
        public void ItemEditorViewModel_DataReportIncludesKeyFields()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "ViewModels", "ItemEditorViewModel.cs"));
            Assert.Contains("[\"W0_NameId\"]", src);
            Assert.Contains("[\"B7_WeaponType\"]", src);
            Assert.Contains("[\"B21_Might\"]", src);
            Assert.Contains("[\"B22_Hit\"]", src);
            Assert.Contains("[\"W26_Price\"]", src);
        }

        [Fact]
        public void ClassEditorViewModel_DataReportIncludesKeyFields()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "ViewModels", "ClassEditorViewModel.cs"));
            Assert.Contains("[\"W0_NameId\"]", src);
            Assert.Contains("[\"B4_ClassNumber\"]", src);
            Assert.Contains("[\"B11_BaseHp\"]", src);
            Assert.Contains("[\"B27_GrowHp\"]", src);
            Assert.Contains("[\"B17_Mov\"]", src);
        }

        // ------------------------------------------------------------------ Raw ROM report covers correct offsets

        [Fact]
        public void UnitEditorViewModel_RawRomReportMatchesReadOffsets()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "ViewModels", "UnitEditorViewModel.cs"));
            // GetRawRomReport uses the same offsets as LoadUnit
            Assert.Contains("[\"u16@0x00\"]", src);
            Assert.Contains("[\"u8@0x04\"]", src);
            Assert.Contains("[\"u8@0x08\"]", src);
            Assert.Contains("[\"u8@0x0C\"]", src);
        }

        [Fact]
        public void ItemEditorViewModel_RawRomReportMatchesReadOffsets()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "ViewModels", "ItemEditorViewModel.cs"));
            Assert.Contains("[\"u16@0\"]", src);
            Assert.Contains("[\"u8@7\"]", src);
            Assert.Contains("[\"u8@21\"]", src);
            Assert.Contains("[\"u16@26\"]", src);
        }

        [Fact]
        public void ClassEditorViewModel_RawRomReportMatchesReadOffsets()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "ViewModels", "ClassEditorViewModel.cs"));
            Assert.Contains("[\"u16@0\"]", src);
            Assert.Contains("[\"u8@4\"]", src);
            Assert.Contains("[\"u8@11\"]", src);
            Assert.Contains("[\"u8@27\"]", src);
        }
    }
}
