using System;
using System.Reflection;
using System.Windows.Forms;
using Xunit;

namespace FEBuilderGBA.Tests.Unit
{
    /// <summary>
    /// #1812: verifies the WinForms in-app patch2 Initialize/Update wiring is present — the shared
    /// <see cref="Patch2GitWinForms.RunInitUpdate"/> host plus the OptionForm and PatchForm buttons +
    /// click handlers. The clone/update logic itself is covered by the 12 cross-platform
    /// <c>Patch2GitServiceTests</c> (Core); a real WinForms form instantiation here would require a
    /// live ROM (PatchForm) / config + a translation-matched "Etc" tab (OptionForm), so the button
    /// creation is proven visually by the PR screenshot instead. This structural test guards against
    /// the wiring being renamed/removed (which would silently break the buttons).
    /// </summary>
    public class Patch2InitUpdateWinFormsTests
    {
        const BindingFlags NonPublicInstance = BindingFlags.NonPublic | BindingFlags.Instance;

        [Fact]
        public void Patch2GitWinForms_RunInitUpdate_HasExpectedSignature()
        {
            MethodInfo m = typeof(Patch2GitWinForms).GetMethod(
                "RunInitUpdate", new[] { typeof(Form), typeof(string) });
            Assert.NotNull(m);
            Assert.True(m.IsStatic);
            Assert.Equal(typeof(Patch2GitResult), m.ReturnType);
        }

        [Fact]
        public void OptionForm_HasPatch2InitUpdateButtonFieldAndHandler()
        {
            Assert.NotNull(typeof(OptionForm).GetField("_optionPatch2InitUpdateButton", NonPublicInstance));
            Assert.NotNull(typeof(OptionForm).GetMethod("OptionPatch2InitUpdateButton_Click", NonPublicInstance));
        }

        [Fact]
        public void PatchForm_HasPatch2InitUpdateButtonCreatorAndHandler()
        {
            Assert.NotNull(typeof(PatchForm).GetMethod("AddPatch2InitUpdateButton", NonPublicInstance));
            Assert.NotNull(typeof(PatchForm).GetMethod("Patch2InitUpdateButton_Click", NonPublicInstance));
        }
    }
}
