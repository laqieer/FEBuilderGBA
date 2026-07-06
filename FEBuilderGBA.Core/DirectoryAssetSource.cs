using System;
using System.Collections.Generic;
using System.IO;

namespace FEBuilderGBA
{
    /// <summary>
    /// Pure, platform-free <see cref="AndroidConfigExtractorCore.IAssetSource"/> backed by a
    /// real on-disk directory tree (issue #1859).
    ///
    /// <para>
    /// This is the iOS counterpart of the Android <c>AssetManager</c> adapter
    /// (<c>FEBuilderGBA.Android/AndroidAssetSource.cs</c>). On iOS the bundled
    /// <c>config/</c> tree ships as <c>&lt;BundleResource&gt;</c> and lands inside the app
    /// bundle as a REAL readable directory at
    /// <c>NSBundle.MainBundle.BundlePath/config/…</c>, so — unlike Android's opaque
    /// <c>AssetManager</c> — it can be enumerated with plain <see cref="System.IO"/>. The iOS
    /// head constructs <c>new DirectoryAssetSource(bundlePath, "config")</c> and feeds it to
    /// <see cref="AndroidConfigExtractorCore.EnsureExtracted"/>, which copies the (read-only)
    /// bundle tree into an app-private writable dir on first run.
    /// </para>
    ///
    /// <para>
    /// Because it depends only on <see cref="System.IO"/> (no Foundation / no
    /// <see cref="CoreState"/> / no statics), the exact enumeration + open behaviour the iOS
    /// head relies on is fully desktop-unit-testable (<c>DirectoryAssetSourceTests</c>).
    /// Relative paths are POSIX-style (forward-slash separated) and rooted at
    /// <see cref="_rootDir"/> — i.e. they INCLUDE the <paramref name="subfolder"/> segment
    /// (e.g. <c>config/data/foo.txt</c>), matching the Android adapter's <c>config/…</c>
    /// convention so <see cref="AndroidConfigExtractorCore"/>'s top-level-root cleanup targets
    /// the same <c>config</c> root.
    /// </para>
    /// </summary>
    public sealed class DirectoryAssetSource : AndroidConfigExtractorCore.IAssetSource
    {
        readonly string _rootDir;
        readonly string _subfolder;

        /// <summary>
        /// Create a source that enumerates every file under
        /// <paramref name="rootDir"/>/<paramref name="subfolder"/>, reporting relative POSIX
        /// paths rooted at <paramref name="rootDir"/> (so the <paramref name="subfolder"/>
        /// prefix is retained).
        /// </summary>
        /// <param name="rootDir">The directory the reported relative paths are rooted at (e.g. the app bundle path).</param>
        /// <param name="subfolder">The single subtree under <paramref name="rootDir"/> to enumerate (e.g. <c>config</c>).</param>
        public DirectoryAssetSource(string rootDir, string subfolder)
        {
            if (string.IsNullOrEmpty(rootDir)) throw new ArgumentException("rootDir must be non-empty", nameof(rootDir));
            if (string.IsNullOrEmpty(subfolder)) throw new ArgumentException("subfolder must be non-empty", nameof(subfolder));
            _rootDir = rootDir;
            _subfolder = subfolder;
        }

        public IEnumerable<string> EnumerateAssetFiles()
        {
            string subtree = Path.Combine(_rootDir, _subfolder);
            if (!Directory.Exists(subtree))
            {
                yield break;
            }

            foreach (string f in Directory.EnumerateFiles(subtree, "*", SearchOption.AllDirectories))
            {
                // Report paths relative to _rootDir so they retain the "config/" prefix
                // (matching the Android adapter + the extractor's root-cleanup contract).
                yield return Path.GetRelativePath(_rootDir, f).Replace('\\', '/');
            }
        }

        public Stream OpenAsset(string relativePath)
        {
            string p = Path.Combine(_rootDir, relativePath.Replace('/', Path.DirectorySeparatorChar));
            return new FileStream(p, FileMode.Open, FileAccess.Read, FileShare.Read);
        }
    }
}
