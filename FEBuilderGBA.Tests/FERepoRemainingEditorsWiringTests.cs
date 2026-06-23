using System;
using System.IO;
using Xunit;

namespace FEBuilderGBA.Tests
{
    /// <summary>
    /// #1397 — single-import-path + button-presence guards for the FINAL
    /// FE-Repo batch (the remaining graphics editors).
    ///
    /// Every wired editor must (a) carry exactly one FE-Repo button that routes
    /// the chosen file through the editor's EXISTING import body — no second
    /// import code path — and (b) for the dropped-as-Unsupported editors, NOT
    /// carry an FE-Repo button at all (so a generic Supported enum value such as
    /// SpellAnimation/BattleAnimation never lights a button on an editor whose
    /// import is script/palette-based).
    ///
    /// These are source-text + reflection guards (no GUI / ROM) so they run in
    /// headless CI.
    /// </summary>
    public class FERepoRemainingEditorsWiringTests
    {
        // -----------------------------------------------------------------
        // WinForms wired editors — FERepoButton_Click routes through the SAME
        // extracted ImportBitmap-style body the file-picker Import uses.
        // -----------------------------------------------------------------

        [Theory]
        [InlineData("ImageCGForm.cs", "ImportBitmap", "CGImage")]
        [InlineData("ImageCGFE7UForm.cs", "ImportBitmap", "CGImage")]
        [InlineData("ImageBattleBGForm.cs", "ImportBitmap", "BattleBackground")]
        [InlineData("ImageGenericEnemyPortraitForm.cs", "ImportBitmap", "GenericEnemyPortrait")]
        [InlineData("ImageBGForm.cs", "ImportFromFilename", "BackgroundImage")]
        [InlineData("ImagePortraitFE6Form.cs", "ImportFromFilename", "Portrait")]
        public void WinForms_FERepoButton_RoutesThroughSharedImportBody(
            string fileName, string sharedBody, string kind)
        {
            string src = ReadWinFormsSource(fileName);

            // Has a FE-Repo handler.
            int handler = src.IndexOf("void FERepoButton_Click", StringComparison.Ordinal);
            Assert.True(handler >= 0, $"{fileName}: FERepoButton_Click not found");
            string handlerBody = src.Substring(handler);

            // Routes through the established browser + the editor's verified kind.
            Assert.Contains("FERepoResourceBrowserForm", handlerBody);
            Assert.Contains("FERepoResourceBrowser.FERepoEditorKind." + kind, handlerBody);

            // Reuses the SAME import body (no second import code path).
            Assert.Contains(sharedBody + "(", handlerBody);

            // Wired onto the action row next to Import/Export.
            Assert.Contains("FERepoResourceBrowserForm.AddBrowseButton", src);
        }

        // -----------------------------------------------------------------
        // #1397 (Copilot PR #1401 review) — REJECT-not-corrupt for the wired
        // fixed-dimension editors whose seeded FE-Repo folder is mixed-size.
        // The FE-Repo path must enforce the editor's exact dimensions so a
        // wrong-size repository asset is rejected, never silently cropped.
        // -----------------------------------------------------------------

        [Fact]
        public void WinForms_BattleBG_FERepoPath_EnforcesExactWidth_NotWidthZeroCrop()
        {
            // The lenient file-picker path loads width=0 and ImportBitmap crops
            // anything wider than 240. The FE-Repo path (mixed folder) must
            // instead pass the EXACT width so ConvertDecolorUI rejects a
            // 256x160 full-screen asset rather than truncating it.
            string src = ReadWinFormsSource("ImageBattleBGForm.cs");
            int handler = src.IndexOf("void FERepoButton_Click", StringComparison.Ordinal);
            Assert.True(handler >= 0);
            // Body up to the next method (ImportBitmap) so we only inspect the
            // FE-Repo handler.
            int end = src.IndexOf("private void ImportBitmap", handler, StringComparison.Ordinal);
            string handlerBody = end > handler ? src.Substring(handler, end - handler) : src.Substring(handler);

            // Loads through the shared decolor pipeline with a NON-zero width
            // (30*8 = 240), so a non-240x160 asset is rejected before cropping.
            Assert.Contains("LoadAndConvertDecolorUILow", handlerBody);
            Assert.Contains("width = 30 * 8", handlerBody);
            // Must NOT load with the lenient width=0 that allows the crop path.
            Assert.DoesNotMatch(@"LoadAndConvertDecolorUILow\([^,]+,\s*null,\s*0\s*,", handlerBody);
        }

        [Fact]
        public void Avalonia_BattleBG_FERepoPath_UsesStrictSize()
        {
            // The Avalonia FE-Repo handler funnels through ImportImageFromFile
            // with strictSize:true, and that body enforces 240x160 strictly.
            string src = ReadAvaloniaSource("BattleBGViewerView.axaml.cs");
            int handler = src.IndexOf("void FERepo_Click", StringComparison.Ordinal);
            Assert.True(handler >= 0);
            string handlerBody = src.Substring(handler);
            Assert.Contains("ImportImageFromFile(path, strictSize: true)", handlerBody);
            // And the shared body forwards strictSize into the dimension check.
            Assert.Contains("LoadAndQuantizeFromFile(filePath, 240, 160, 16, strictSize: strictSize)", src);
        }

        // -----------------------------------------------------------------
        // Avalonia wired editors — FERepo_Click routes the picked path through
        // the SAME *FromFile / path-taking import body.
        // -----------------------------------------------------------------

        [Theory]
        [InlineData("BattleBGViewerView.axaml.cs", "ImportImageFromFile", "BattleBackground")]
        [InlineData("PortraitViewerView.axaml.cs", "ImportImageFromFile", "Portrait")]
        [InlineData("ImagePortraitFE6View.axaml.cs", "ImportImageFromFile", "Portrait")]
        [InlineData("SkillConfigSkillSystemView.axaml.cs", "ImportIconFromPath", "SkillIcon")]
        [InlineData("SkillConfigFE8NVer2SkillView.axaml.cs", "ImportIconFromPath", "SkillIcon")]
        [InlineData("SkillConfigFE8NVer3SkillView.axaml.cs", "ImportIconFromPath", "SkillIcon")]
        [InlineData("SkillConfigFE8UCSkillSys09xView.axaml.cs", "ImportIconFromPath", "SkillIcon")]
        public void Avalonia_FERepoButton_RoutesThroughSharedImportBody(
            string fileName, string sharedBody, string kind)
        {
            string src = ReadAvaloniaSource(fileName);

            int handler = src.IndexOf("void FERepo_Click", StringComparison.Ordinal);
            Assert.True(handler >= 0, $"{fileName}: FERepo_Click not found");
            string handlerBody = src.Substring(handler);

            Assert.Contains("FERepoPickHelper.PickForEditor", handlerBody);
            Assert.Contains("FERepoResourceBrowser.FERepoEditorKind." + kind, handlerBody);
            Assert.Contains(sharedBody + "(", handlerBody);
        }

        [Theory]
        [InlineData("BattleBGViewerView.axaml")]
        [InlineData("PortraitViewerView.axaml")]
        [InlineData("ImagePortraitFE6View.axaml")]
        [InlineData("SkillConfigSkillSystemView.axaml")]
        [InlineData("SkillConfigFE8NVer2SkillView.axaml")]
        [InlineData("SkillConfigFE8NVer3SkillView.axaml")]
        [InlineData("SkillConfigFE8UCSkillSys09xView.axaml")]
        public void Avalonia_WiredEditor_AxamlHasFERepoButton(string axamlName)
        {
            string src = ReadAvaloniaSource(axamlName);
            Assert.Contains("Click=\"FERepo_Click\"", src);
            Assert.Contains("_FERepo_Button", src);
        }

        // -----------------------------------------------------------------
        // Dropped-as-Unsupported editors — MUST NOT carry an FE-Repo button
        // (the footgun: a generic Supported enum value lighting a bad button on
        // a script/palette-based editor).
        // -----------------------------------------------------------------

        [Theory]
        // CG Avalonia: 240x160 import vs 256x160-only folder → dropped.
        [InlineData("ImageCGView.axaml.cs")]
        [InlineData("ImageCGFE7UView.axaml.cs")]
        // Magic = script + per-frame package.
        [InlineData("ImageMagicFEditorView.axaml.cs")]
        [InlineData("ImageMagicCSACreatorView.axaml.cs")]
        // Palette editors = palette, not image.
        [InlineData("ImageUnitPaletteView.axaml.cs")]
        [InlineData("ImageBattleAnimePalletView.axaml.cs")]
        // FE8N v1 SkillConfig = render-only (no writable icon I/O).
        [InlineData("SkillConfigFE8NSkillView.axaml.cs")]
        public void Avalonia_DroppedEditor_HasNoFERepoButton(string fileName)
        {
            string src = ReadAvaloniaSource(fileName);
            Assert.DoesNotContain("FERepoPickHelper.PickForEditor", src);
            Assert.DoesNotContain("void FERepo_Click", src);
        }

        [Theory]
        [InlineData("ClassForm.cs")]            // CSV stats import, not a graphic.
        [InlineData("ImageBattleAnimeForm.cs")] // .bin/.txt/.gif anime script.
        [InlineData("ImageMagicFEditorForm.cs")]
        [InlineData("ImageMagicCSACreatorForm.cs")]
        [InlineData("ImageUnitPaletteForm.cs")] // palette, not image.
        public void WinForms_DroppedEditor_HasNoFERepoButton(string fileName)
        {
            string src = ReadWinFormsSource(fileName);
            Assert.DoesNotContain("FERepoResourceBrowserForm.AddBrowseButton", src);
            Assert.DoesNotContain("void FERepoButton_Click", src);
        }

        // -----------------------------------------------------------------
        // The skill-icon path-taking core that BOTH the file-dialog import and
        // the FE-Repo button reuse must exist as a single shared method (no
        // second import body, no second file picker). Source-text guard so this
        // project needs no Avalonia assembly reference (the public-static
        // reflection check lives in FEBuilderGBA.Avalonia.Tests).
        // -----------------------------------------------------------------

        [Fact]
        public void SkillConfigIconIoHelper_HasSharedImportIconFromPath_AndDialogReusesIt()
        {
            string src = ReadAvaloniaSource("SkillConfigIconIoHelper.cs");
            // The shared path-taking core exists and is public+static.
            Assert.Contains("public static string? ImportIconFromPath(", src);
            // The dialog import funnels into it (no duplicate import body).
            int dlg = src.IndexOf("ImportIconAsync(", StringComparison.Ordinal);
            Assert.True(dlg >= 0);
            Assert.Contains("return ImportIconFromPath(", src);
        }

        // -----------------------------------------------------------------
        // helpers
        // -----------------------------------------------------------------

        static string ReadWinFormsSource(string fileName)
        {
            string root = FindRepoRoot();
            Assert.NotNull(root);
            string path = Path.Combine(root, "FEBuilderGBA", fileName);
            Assert.True(File.Exists(path), $"{fileName} not found at {path}");
            return File.ReadAllText(path);
        }

        static string ReadAvaloniaSource(string fileName)
        {
            string root = FindRepoRoot();
            Assert.NotNull(root);
            string path = Path.Combine(root, "FEBuilderGBA.Avalonia", "Views", fileName);
            if (!File.Exists(path))
                path = Path.Combine(root, "FEBuilderGBA.Avalonia", "Services", fileName);
            Assert.True(File.Exists(path), $"{fileName} not found under FEBuilderGBA.Avalonia");
            return File.ReadAllText(path);
        }

        static string FindRepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "FEBuilderGBA.sln")))
                    return dir.FullName;
                dir = dir.Parent;
            }
            return null;
        }
    }
}
