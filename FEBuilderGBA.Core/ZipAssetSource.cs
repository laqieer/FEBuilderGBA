using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

namespace FEBuilderGBA
{
    /// <summary>
    /// Pure, platform-free <see cref="AndroidConfigExtractorCore.IAssetSource"/> backed by a
    /// <see cref="ZipArchive"/> (issue #1864, the WebAssembly/Browser head).
    ///
    /// <para>
    /// On the browser there is no app bundle and no real filesystem to read <c>config/</c> from:
    /// the head fetches a single <c>config.zip</c> static asset over HTTP and extracts it, once,
    /// into a writable in-memory (MEMFS) directory, after which <c>App.BaseDirectoryOverride</c>
    /// points Core there. This is the browser analog of the Android <c>AssetManager</c> adapter
    /// (<c>FEBuilderGBA.Android/AndroidAssetSource.cs</c>) and the iOS
    /// <see cref="DirectoryAssetSource"/> — the ONLY difference is the backing store.
    /// </para>
    ///
    /// <para>
    /// Because it depends only on <see cref="System.IO.Compression"/> (no browser interop), the
    /// exact enumeration + open behaviour the browser head relies on is fully desktop-unit-testable
    /// (<c>ZipAssetSourceTests</c>). Reported paths are the zip entry full names, which are
    /// POSIX-style (forward-slash) and — per the config.zip build contract — retain the
    /// <c>config/</c> prefix (e.g. <c>config/data/foo.txt</c>), so
    /// <see cref="AndroidConfigExtractorCore"/>'s top-level-root cleanup + skip logic target the
    /// same <c>config</c> root as the other heads.
    /// </para>
    ///
    /// <para>
    /// The wrapped <see cref="ZipArchive"/> must remain open for the lifetime of this source
    /// (until <see cref="AndroidConfigExtractorCore.EnsureExtracted"/> has consumed every entry).
    /// The caller owns the archive's disposal.
    /// </para>
    /// </summary>
    public sealed class ZipAssetSource : AndroidConfigExtractorCore.IAssetSource
    {
        readonly ZipArchive _archive;

        public ZipAssetSource(ZipArchive archive)
        {
            _archive = archive ?? throw new ArgumentNullException(nameof(archive));
        }

        public IEnumerable<string> EnumerateAssetFiles()
        {
            foreach (ZipArchiveEntry entry in _archive.Entries)
            {
                // Directory entries have an empty Name (their FullName ends with '/'); skip them —
                // the extractor recreates directories from the file paths.
                if (string.IsNullOrEmpty(entry.Name))
                {
                    continue;
                }
                // FullName is already POSIX (forward-slash) per the zip spec.
                yield return entry.FullName;
            }
        }

        public Stream OpenAsset(string relativePath)
        {
            if (string.IsNullOrEmpty(relativePath))
                throw new ArgumentException("relativePath must be non-empty", nameof(relativePath));

            // Zip entry names are POSIX; normalize any platform separators the caller may pass so
            // GetEntry matches the FullName we reported from EnumerateAssetFiles.
            string key = relativePath.Replace('\\', '/');
            ZipArchiveEntry entry = _archive.GetEntry(key)
                ?? throw new FileNotFoundException("Zip entry not found: " + key);
            return entry.Open();
        }
    }
}
