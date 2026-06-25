// SPDX-License-Identifier: GPL-3.0-or-later
// Regression tests for #1453: the standalone "Eff Rework" main-menu button
// opened a permanently-inert `SkillSystemsEffectivenessReworkClassTypeView`
// placeholder window (only a "requires a skill patch" warning; FieldsPanel +
// Write button hidden; CurrentAddr==0; Write() no-op).
//
// Ground truth (WinForms): `SkillSystemsEffectivenessReworkClassTypeForm` is NOT
// a standalone menu editor — it is a CLASSTYPE bitmask picker popup invoked from
// the Class editor only (InputFormRef.cs CLASSTYPE link, with U.NOT_FOUND). The
// real standalone Effectiveness-Rework editor is the FUNCTIONAL
// `ItemEffectivenessSkillSystemsReworkForm` (MainFE8Form.cs), which already exists
// in Avalonia as `ItemEffectivenessSkillSystemsReworkView` and is reachable via
// the separate "Effectiveness (Rework)" button.
//
// Fix (issue's Option B — exact WinForms parity, analogue of #1447's
// SongTrackImportWaveView removal): remove the redundant standalone main-menu
// exposure (button + click handler + GetAllEditorFactories registry entry +
// NoListEditors entry + WinForms ScreenshotFormRegistry pairing) and delete the
// inert view + its two stray ViewModels. These static-source assertions lock in
// that removal AND guard that the genuine standalone editor remains reachable
// (no functional regression).
using System.IO;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests.GapSweep
{
    public class SkillSystemsEffectivenessReworkRemovalTests
    {
        static string FindRepoRoot()
        {
            string? dir = System.AppContext.BaseDirectory;
            while (dir != null && !File.Exists(Path.Combine(dir, "FEBuilderGBA.sln")))
                dir = Path.GetDirectoryName(dir);
            Assert.NotNull(dir);
            return dir!;
        }

        static string Read(params string[] parts)
            => File.ReadAllText(Path.Combine(FindRepoRoot(), Path.Combine(parts)));

        // -----------------------------------------------------------------
        // The standalone dead-end exposure is GONE.
        // -----------------------------------------------------------------

        [Fact]
        public void MainWindow_Axaml_HasNoStandaloneEffReworkButton()
        {
            string axaml = Read("FEBuilderGBA.Avalonia", "Views", "MainWindow.axaml");
            Assert.DoesNotContain("Main_EffRework_Button", axaml);
            Assert.DoesNotContain("OpenSkillSystemsEffectivenessRework_Click", axaml);
        }

        [Fact]
        public void MainWindow_CodeBehind_HasNoEffReworkHandlerOrFactory()
        {
            string cs = Read("FEBuilderGBA.Avalonia", "Views", "MainWindow.axaml.cs");
            // No click handler and no named-editor factory for the removed view.
            Assert.DoesNotContain("OpenSkillSystemsEffectivenessRework_Click", cs);
            Assert.DoesNotContain("Open<SkillSystemsEffectivenessReworkClassTypeView>", cs);
            // The localization line for the removed button is gone too.
            Assert.DoesNotContain("EffReworkButton", cs);
        }

        [Fact]
        public void InertViewAndStrayViewModels_AreDeleted()
        {
            string root = FindRepoRoot();
            Assert.False(File.Exists(Path.Combine(root,
                "FEBuilderGBA.Avalonia", "Views",
                "SkillSystemsEffectivenessReworkClassTypeView.axaml")));
            Assert.False(File.Exists(Path.Combine(root,
                "FEBuilderGBA.Avalonia", "Views",
                "SkillSystemsEffectivenessReworkClassTypeView.axaml.cs")));
            // The view's own VM ("...ViewViewModel").
            Assert.False(File.Exists(Path.Combine(root,
                "FEBuilderGBA.Avalonia", "ViewModels",
                "SkillSystemsEffectivenessReworkClassTypeViewViewModel.cs")));
            // The unused stray VM ("...ViewModel", never wired to the view).
            Assert.False(File.Exists(Path.Combine(root,
                "FEBuilderGBA.Avalonia", "ViewModels",
                "SkillSystemsEffectivenessReworkClassTypeViewModel.cs")));
        }

        [Fact]
        public void ListParityHelper_NoListEditors_DoesNotReferenceRemovedView()
        {
            string cs = Read("FEBuilderGBA.Avalonia", "Services", "ListParityHelper.cs");
            Assert.DoesNotContain("SkillSystemsEffectivenessReworkClassTypeView", cs);
        }

        [Fact]
        public void WinFormsScreenshotRegistry_DoesNotPairRemovedView()
        {
            // The WinForms screenshot registry must stay a subset of the Avalonia
            // editor factories. Since the Avalonia factory entry is gone, the
            // WinForms pairing must be too.
            string cs = Read("FEBuilderGBA", "ScreenshotFormRegistry.cs");
            Assert.DoesNotContain("\"SkillSystemsEffectivenessReworkClassTypeView\"", cs);
        }

        [Fact]
        public void GeneratorScript_DoesNotRegenerateInertView()
        {
            // The Avalonia editor generator must not recreate the inert standalone
            // view. The only allowed occurrence is the explanatory NOTE comment.
            string sh = Read("scripts", "generate-avalonia-editors.sh");
            Assert.DoesNotContain(
                "generate_editor \"SkillSystemsEffectivenessReworkClassType\"", sh);
        }

        // -----------------------------------------------------------------
        // The REAL standalone Effectiveness-Rework editor is kept (no regression).
        // -----------------------------------------------------------------

        [Fact]
        public void FunctionalEffectivenessReworkEditor_StillWired()
        {
            string axaml = Read("FEBuilderGBA.Avalonia", "Views", "MainWindow.axaml");
            // The genuine standalone editor remains reachable via its own button.
            Assert.Contains("Main_EffectivenessRework_Button", axaml);
            Assert.Contains("OpenItemEffectivenessSkillSystemsRework_Click", axaml);

            string cs = Read("FEBuilderGBA.Avalonia", "Views", "MainWindow.axaml.cs");
            Assert.Contains("Open<ItemEffectivenessSkillSystemsReworkView>", cs);
        }

        [Fact]
        public void FunctionalEffectivenessReworkView_SourceStillPresent()
        {
            string root = FindRepoRoot();
            Assert.True(File.Exists(Path.Combine(root,
                "FEBuilderGBA.Avalonia", "Views",
                "ItemEffectivenessSkillSystemsReworkView.axaml")));
            Assert.True(File.Exists(Path.Combine(root,
                "FEBuilderGBA.Avalonia", "ViewModels",
                "ItemEffectivenessSkillSystemsReworkViewModel.cs")));
        }
    }
}
