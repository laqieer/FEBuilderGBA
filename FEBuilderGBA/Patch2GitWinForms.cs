using System.Windows.Forms;

namespace FEBuilderGBA
{
    /// <summary>
    /// #1812: thin patch2 facade over <see cref="ContentRepoGitWinForms"/> (#1813). Kept so the merged
    /// OptionForm/PatchForm patch2 buttons and the reflection test keep calling
    /// <c>Patch2GitWinForms.RunInitUpdate(Form, string)</c> unchanged; it resolves the patch2 directory +
    /// remote URL (custom fork override or default) and delegates to the generic WinForms host.
    /// </summary>
    public static class Patch2GitWinForms
    {
        /// <summary>
        /// Runs the in-app patch2 Initialize/Update. <paramref name="urlOverride"/> (nullable) forces a
        /// specific remote — OptionForm passes its Patch2 URL textbox so a just-typed custom fork URL
        /// takes effect. Returns the <see cref="Patch2GitResult"/> so PatchForm can rescan on success.
        /// </summary>
        public static Patch2GitResult RunInitUpdate(Form owner, string urlOverride)
        {
            string repoDir = Patch2GitService.GetPatch2Dir(Program.BaseDirectory);
            string url = string.IsNullOrWhiteSpace(urlOverride) ? GitUtil.GetPatch2RemoteUrl() : urlOverride;
            return ContentRepoGitWinForms.RunInitUpdate(owner, repoDir, url, "Patch database");
        }
    }
}
