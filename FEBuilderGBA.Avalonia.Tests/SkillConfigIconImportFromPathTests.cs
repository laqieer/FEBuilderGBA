using System.Reflection;
using FEBuilderGBA.Avalonia.Services;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests
{
    /// <summary>
    /// #1397 — the SkillConfig FE-Repo button and the file-dialog Image Import
    /// must share ONE import body. <see cref="SkillConfigIconIoHelper"/> exposes
    /// the path-taking core <c>ImportIconFromPath</c> as a public static method
    /// so the FE-Repo handler can reuse it without a second file picker or a
    /// duplicate import path.
    /// </summary>
    public class SkillConfigIconImportFromPathTests
    {
        [Fact]
        public void ImportIconFromPath_IsPublicStatic()
        {
            MethodInfo m = typeof(SkillConfigIconIoHelper).GetMethod(
                "ImportIconFromPath",
                BindingFlags.Public | BindingFlags.Static);
            Assert.NotNull(m);
            // Contract: returns string? (null = cancel, "" = success, else error).
            Assert.Equal(typeof(string), m.ReturnType);
        }
    }
}
