// SPDX-License-Identifier: GPL-3.0-or-later
// #1148: decomp-mode guard for the raw map ASSET write/import paths.
//
// Chapter SETTINGS (the map_settings struct table) are source-backed in decomp mode
// (MapSettingView / MapSettingFE6View route to DecompSourceWriterCore). But the raw map
// ASSET bytes — tile layout (.mar / PLIST map data), tileset OBJ graphics, palette,
// chipset config/TSA, tile animations, and the map-change overlay — are LZ77-compressed
// binaries, NOT struct fields. In decomp mode those belong to the source tree and must be
// migrated via the #1133/#1140 asset-export pipeline, NOT silently written to the build
// preview ROM. This guard surfaces that honestly (Copilot plan-review finding 3).
using FEBuilderGBA;

namespace FEBuilderGBA.Avalonia.Services
{
    /// <summary>
    /// Blocks a raw map-asset ROM mutation while a decomp project is open and shows an
    /// export-only/manual notice. NEVER throws. Returns true when the action is blocked
    /// (the caller must NOT proceed with the ROM write); false in classic ROM mode (the
    /// caller proceeds exactly as before — byte-for-byte unchanged behavior).
    /// </summary>
    public static class DecompMapAssetGuard
    {
        /// <summary>
        /// When a decomp project is open, show an export-only/manual notice for a raw
        /// map-asset edit and return true (block). In classic mode, return false (proceed).
        /// </summary>
        /// <param name="assetName">Short human name of the asset (e.g. "map tile").</param>
        public static bool BlockIfDecomp(string assetName)
        {
            try
            {
                if (!CoreState.IsDecompMode)
                    return false;

                string what = string.IsNullOrEmpty(assetName) ? R._("map asset") : assetName;
                CoreState.Services?.ShowInfo(string.Format(
                    R._("In decomp mode, the {0} is a source-tree asset. Editing it here would write the build-preview ROM, which is overwritten on rebuild. Export it to the source tree (Decomp asset export) and edit it there instead."),
                    what));
                return true;
            }
            catch
            {
                // Never throw at a UI boundary; on any fault, do not block (preserve
                // classic behavior) — the source-backed paths already guard themselves.
                return false;
            }
        }
    }
}
