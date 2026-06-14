#if ANDROID
using System.Collections.Generic;
using System.IO;
using Android.Content.Res;
using FEBuilderGBA;

namespace FEBuilderGBA.Android
{
    /// <summary>
    /// Android <see cref="AssetManager"/> adapter for the pure
    /// <see cref="AndroidConfigExtractorCore.IAssetSource"/> seam (#1123). This is
    /// the ONLY Android-coupled piece of the config-extraction flow — the copy /
    /// version-stamp / idempotency logic lives in the platform-free
    /// <see cref="AndroidConfigExtractorCore"/> in Core (and is desktop-unit-tested).
    ///
    /// <para>
    /// Enumerates every bundled asset file under <see cref="AssetRoot"/> (the
    /// <c>config/</c> tree shipped as <c>&lt;AndroidAsset&gt;</c>) by recursively
    /// walking <see cref="AssetManager.List(string)"/>, and opens individual assets
    /// via <see cref="AssetManager.Open(string)"/>. Paths are POSIX-style
    /// (forward-slash separated), rooted at the asset root.
    /// </para>
    /// </summary>
    public sealed class AndroidAssetSource : AndroidConfigExtractorCore.IAssetSource
    {
        /// <summary>The asset subtree to extract (config only; patch2 is never bundled — see docs/ANDROID.md §5).</summary>
        public const string AssetRoot = "config";

        readonly AssetManager _assets;

        public AndroidAssetSource(AssetManager assets)
        {
            _assets = assets;
        }

        public IEnumerable<string> EnumerateAssetFiles()
        {
            var files = new List<string>();
            Walk(AssetRoot, files);
            return files;
        }

        public Stream OpenAsset(string relativePath)
        {
            // AssetManager uses forward-slash paths; relativePath is already POSIX.
            return _assets.Open(relativePath);
        }

        /// <summary>
        /// Recursively collect file (leaf) paths. <see cref="AssetManager.List(string)"/>
        /// returns the immediate children of a directory; a child with no further
        /// children is treated as a file. (AssetManager has no "is-directory" probe,
        /// so an empty <c>List</c> result is the directory/leaf discriminator — empty
        /// directories are not representable as assets anyway.)
        /// </summary>
        void Walk(string dir, List<string> outFiles)
        {
            string[]? children = _assets.List(dir);
            if (children == null || children.Length == 0)
            {
                // Leaf node = a file.
                outFiles.Add(dir);
                return;
            }

            foreach (string child in children)
            {
                string childPath = dir.Length == 0 ? child : dir + "/" + child;
                Walk(childPath, outFiles);
            }
        }
    }
}
#endif
