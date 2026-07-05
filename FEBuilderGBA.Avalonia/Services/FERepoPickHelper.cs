using System;
using System.Threading.Tasks;
using global::Avalonia.Controls;
using FEBuilderGBA.Avalonia.Views;

namespace FEBuilderGBA.Avalonia.Services
{
    /// <summary>
    /// #1380 Part B — shared glue for the per-editor "FE-Repo" button.
    /// Opens the generic <see cref="FERepoResourceBrowserWindow"/> seeded to the
    /// FE-Repo folder for the given editor kind (resolved once in Core via
    /// <see cref="FERepoResourceBrowser.GetFERepoFolderForEditor"/>) and returns
    /// the chosen file path (or null if cancelled / unsupported). The caller then
    /// feeds the path through the SAME import pipeline as its file-picker Import
    /// button — no second import code path.
    /// </summary>
    public static class FERepoPickHelper
    {
        /// <summary>
        /// True when the editor kind has a real FE-Repo source folder (used to
        /// decide whether to show the FE-Repo button at all).
        /// </summary>
        public static bool IsSupported(FERepoResourceBrowser.FERepoEditorKind kind)
            => FERepoResourceBrowser.GetFERepoFolderForEditor(kind).Supported;

        /// <summary>
        /// Open the FE-Repo browser seeded for <paramref name="kind"/> and await
        /// the user's chosen file path. Returns null when unsupported, cancelled,
        /// or nothing was selected.
        /// </summary>
        public static async Task<string?> PickForEditor(Window owner,
            FERepoResourceBrowser.FERepoEditorKind kind)
        {
            var folder = FERepoResourceBrowser.GetFERepoFolderForEditor(kind);
            if (!folder.Supported) return null;

            var browser = new FERepoResourceBrowserWindow(folder.Category, folder.SubCategory);
            string result = await browser.ShowDialog<string>(owner);
            return string.IsNullOrEmpty(result) ? null : result;
        }

        /// <summary>
        /// Open the FE-Repo-Music browser (music mode) and await the user's chosen
        /// music file path. Optionally pre-navigate to a seed category/subcategory.
        /// Returns null when the dialog was cancelled or nothing was selected. The
        /// browser is ALWAYS openable — when the music submodule is absent it shows
        /// an actionable "not found / clone" empty-state (#1815), so this no longer
        /// short-circuits on availability. The caller feeds the returned path
        /// through the SAME music-import dispatcher as its file-picker Import —
        /// no second import code path (#1383).
        /// </summary>
        public static async Task<string?> PickMusic(Window owner,
            string seedCategory = null, string seedSubCategory = null)
        {
            var browser = new FERepoResourceBrowserWindow(true, seedCategory, seedSubCategory);
            string result = await browser.ShowDialog<string>(owner);
            return string.IsNullOrEmpty(result) ? null : result;
        }
    }
}
