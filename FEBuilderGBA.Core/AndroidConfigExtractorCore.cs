using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace FEBuilderGBA
{
    /// <summary>
    /// Pure, desktop-unit-testable first-run config extractor for the Android port
    /// (epic #1070, issue #1123).
    ///
    /// <para>
    /// On desktop the <c>config/</c> tree (game data, scripts, names, translations)
    /// ships as loose files beside the exe and Core reads it via
    /// <see cref="CoreState.BaseDirectory"/> (<c>= AppDomain.CurrentDomain.BaseDirectory</c>).
    /// Inside an APK there is no "beside the exe" loose-file layout: <c>config/**</c>
    /// is bundled as <c>AndroidAsset</c> and must be extracted once into
    /// <c>Context.FilesDir</c> on first run, after which
    /// <see cref="CoreState.BaseDirectory"/> is pointed at the extracted root.
    /// </para>
    ///
    /// <para>
    /// This helper is the <b>pure core</b> of that flow: it takes an abstract
    /// <see cref="IAssetSource"/> (so the Android <c>AssetManager</c> is the only
    /// platform-coupled piece, injected from the head) + a target dir + a version
    /// string, and performs a version-stamped, manifest-complete, crash-safe,
    /// idempotent extraction. It depends only on <see cref="System.IO"/> and the
    /// injected source — no Android, no <see cref="CoreState"/>, no statics — so the
    /// extraction logic is fully unit-testable on desktop with a synthetic
    /// directory-backed source.
    /// </para>
    ///
    /// <para>
    /// <b>Idempotency / integrity guarantee.</b> A skip happens ONLY when the stamp
    /// version matches AND every file the previous run recorded in the stamp
    /// manifest still exists on disk. This gives: (a) version-stamp idempotency —
    /// skip when up to date; (b) re-extract on app-version bump; (c) crash-before-stamp
    /// recovery — the stamp is written LAST, so a crash mid-copy leaves a
    /// stale/missing stamp and the next run re-extracts; (d) manifest-completeness —
    /// a matching stamp with any missing extracted file forces a clean re-extract.
    /// It is deliberately NOT a byte-integrity repair of a file that is present but
    /// has been mutated in place — that is out of scope (and not a realistic
    /// failure mode for read-only app-private config).
    /// </para>
    /// </summary>
    public static class AndroidConfigExtractorCore
    {
        /// <summary>
        /// Abstract read-only asset source. The Android head implements this over
        /// <c>Android.Content.Res.AssetManager</c>; tests implement it over a
        /// synthetic directory. Relative paths are POSIX-style (forward-slash
        /// separated), rooted at the asset root (e.g. <c>config/data/foo.txt</c>).
        /// </summary>
        public interface IAssetSource
        {
            /// <summary>
            /// Enumerate every asset file's relative POSIX-style path under the
            /// asset root (files only — directories are implied by the paths).
            /// </summary>
            IEnumerable<string> EnumerateAssetFiles();

            /// <summary>Open one asset for reading by its relative POSIX-style path.</summary>
            Stream OpenAsset(string relativePath);
        }

        /// <summary>Outcome of <see cref="EnsureExtracted"/>.</summary>
        public enum ExtractionResult
        {
            /// <summary>No prior stamp existed — assets were extracted for the first time.</summary>
            Extracted,

            /// <summary>Stamp matched and the manifest was complete — nothing was done.</summary>
            SkippedUpToDate,

            /// <summary>A prior stamp existed but was stale/mismatched/incomplete — assets were re-extracted.</summary>
            ReExtracted,
        }

        /// <summary>Default stamp file name written at the target root.</summary>
        public const string DefaultStampFileName = ".config_version";

        /// <summary>
        /// Ensure the bundled assets are extracted under <paramref name="targetRootDir"/>,
        /// version-stamped by <paramref name="version"/>. Idempotent: a second call
        /// with the same version and a complete on-disk manifest is a no-op.
        /// </summary>
        /// <param name="source">The asset source (Android AssetManager adapter in production; synthetic dir in tests).</param>
        /// <param name="targetRootDir">The directory to extract into (Android <c>Context.FilesDir</c>).</param>
        /// <param name="version">App version string; a change forces a clean re-extract.</param>
        /// <param name="stampFileName">Stamp file name written at the target root (defaults to <see cref="DefaultStampFileName"/>).</param>
        /// <returns>What happened — see <see cref="ExtractionResult"/>.</returns>
        public static ExtractionResult EnsureExtracted(
            IAssetSource source,
            string targetRootDir,
            string version,
            string stampFileName = DefaultStampFileName)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (string.IsNullOrEmpty(targetRootDir)) throw new ArgumentException("targetRootDir must be non-empty", nameof(targetRootDir));
            if (version == null) throw new ArgumentNullException(nameof(version));
            // Guard the public API against a path-traversal footgun: the stamp must
            // be a plain file name directly under targetRootDir, never a rooted path
            // or one containing directory separators / "..", so it can never be
            // created/deleted outside the extraction root.
            if (!IsSimpleFileName(stampFileName))
                throw new ArgumentException("stampFileName must be a simple file name (no path separators, not rooted, no '..')", nameof(stampFileName));

            string stampPath = Path.Combine(targetRootDir, stampFileName);
            bool stampExisted = File.Exists(stampPath);

            if (stampExisted && IsStampValid(stampPath, version, targetRootDir))
            {
                return ExtractionResult.SkippedUpToDate;
            }

            // Stale / mismatched / incomplete (or first run): clean re-extract.
            // Remove any previously-extracted config tree so a version bump or a
            // partial extract cannot leave orphan files behind. Only the known
            // top-level asset roots are wiped (defensive — never the whole target
            // dir, which on Android holds unrelated app-private state).
            var manifest = source.EnumerateAssetFiles()
                .Select(NormalizeRelative)
                .Where(p => p.Length > 0 && IsSafeRelativePath(p))
                .Distinct()
                .OrderBy(p => p, StringComparer.Ordinal)
                .ToList();

            foreach (string root in TopLevelRoots(manifest))
            {
                string rootDir = Path.Combine(targetRootDir, root);
                if (Directory.Exists(rootDir))
                {
                    Directory.Delete(rootDir, recursive: true);
                }
            }

            // Also drop a stale stamp up front so a crash mid-copy never leaves a
            // matching stamp pointing at a half-written tree.
            if (stampExisted)
            {
                try { File.Delete(stampPath); } catch (IOException) { /* best-effort */ }
            }

            Directory.CreateDirectory(targetRootDir);

            foreach (string rel in manifest)
            {
                string destPath = Path.Combine(targetRootDir, ToPlatformPath(rel));
                string? destDir = Path.GetDirectoryName(destPath);
                if (!string.IsNullOrEmpty(destDir))
                {
                    Directory.CreateDirectory(destDir);
                }

                using Stream input = source.OpenAsset(rel);
                using FileStream output = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None);
                input.CopyTo(output);
            }

            // Write the stamp LAST so the extraction is "committed" only after every
            // file landed. The stamp doubles as the completeness manifest.
            WriteStamp(stampPath, version, manifest);

            return stampExisted ? ExtractionResult.ReExtracted : ExtractionResult.Extracted;
        }

        // ---- internals ----

        /// <summary>
        /// A stamp is valid (skip) only when its version line matches AND every
        /// manifest-listed file still exists under the target root. A tampered or
        /// corrupt stamp must NOT be trusted to greenlight a skip: any rooted /
        /// parent-traversal / blank manifest entry makes the stamp invalid (force
        /// a clean re-extract), because <see cref="Path.Combine"/> ignores
        /// <paramref name="targetRootDir"/> for a rooted entry — so a tampered
        /// stamp could otherwise "prove" completeness against files OUTSIDE the
        /// extraction root and incorrectly skip.
        /// </summary>
        static bool IsStampValid(string stampPath, string version, string targetRootDir)
        {
            string[] lines;
            try
            {
                lines = File.ReadAllLines(stampPath);
            }
            catch (IOException)
            {
                return false;
            }

            if (lines.Length < 1) return false;
            if (!string.Equals(lines[0].Trim(), version, StringComparison.Ordinal)) return false;

            // Lines: [0]=version, [1]=count, [2..]=relative paths. Tolerate an
            // older/legacy bare-version stamp (length 1) by treating it as
            // incomplete -> re-extract (safe).
            if (lines.Length < 2) return false;
            if (!int.TryParse(lines[1].Trim(), out int expectedCount)) return false;

            int listed = lines.Length - 2;
            if (listed != expectedCount) return false;

            for (int i = 2; i < lines.Length; i++)
            {
                // Validate the RAW (only whitespace-trimmed) entry for rootedness /
                // traversal BEFORE any normalization. NormalizeRelative strips a
                // leading '/', so normalizing first would let a tampered rooted
                // entry like "/config/data/foo.txt" (or "\\server\x") pass as a safe
                // relative path and validate against an in-root file — incorrectly
                // skipping re-extraction. A blank / rooted / '..' / separator-style
                // entry means the stamp is malformed or tampered -> do NOT skip.
                string raw = lines[i].Trim();
                if (!IsSafeStampEntry(raw)) return false;
                string rel = NormalizeRelative(raw);
                string p = Path.Combine(targetRootDir, ToPlatformPath(rel));
                if (!File.Exists(p)) return false;
            }

            return expectedCount == 0 || listed > 0;
        }

        static void WriteStamp(string stampPath, string version, List<string> manifest)
        {
            var sb = new StringBuilder();
            sb.Append(version).Append('\n');
            sb.Append(manifest.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append('\n');
            foreach (string rel in manifest)
            {
                sb.Append(rel).Append('\n');
            }
            File.WriteAllText(stampPath, sb.ToString());
        }

        /// <summary>Distinct first path segments across the manifest (the asset roots, e.g. "config").</summary>
        static IEnumerable<string> TopLevelRoots(IEnumerable<string> manifest)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (string rel in manifest)
            {
                int slash = rel.IndexOf('/');
                string root = slash < 0 ? rel : rel.Substring(0, slash);
                if (root.Length > 0 && seen.Add(root))
                {
                    yield return root;
                }
            }
        }

        /// <summary>Normalize separators to POSIX and strip a leading slash.</summary>
        static string NormalizeRelative(string p)
        {
            if (string.IsNullOrEmpty(p)) return string.Empty;
            string s = p.Replace('\\', '/').Trim();
            while (s.StartsWith("/", StringComparison.Ordinal)) s = s.Substring(1);
            return s;
        }

        /// <summary>
        /// Stamp-manifest safety check on the RAW entry (NOT slash-normalized).
        /// Rejects blank, rooted ("/x", "\\x", "C:\x"), Windows-separator, or
        /// "."/".."-segment entries. Unlike <see cref="IsSafeRelativePath"/> this
        /// must run BEFORE <see cref="NormalizeRelative"/> strips a leading slash,
        /// so a tampered rooted stamp line can never masquerade as safe.
        /// </summary>
        static bool IsSafeStampEntry(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return false;
            if (raw[0] == '/' || raw[0] == '\\') return false;     // unix/UNC root
            if (raw.IndexOf('\\') >= 0) return false;              // no backslash separators in a POSIX manifest
            if (Path.IsPathRooted(raw)) return false;              // drive-rooted (e.g. C:\ or C:/)
            if (raw.IndexOf(':') >= 0) return false;               // drive/scheme marker
            foreach (string seg in raw.Split('/'))
            {
                if (seg == "." || seg == "..") return false;
            }
            return true;
        }

        /// <summary>Reject empty, rooted, or parent-traversal paths (defense-in-depth on the seam).</summary>
        static bool IsSafeRelativePath(string rel)
        {
            if (string.IsNullOrEmpty(rel)) return false;
            if (Path.IsPathRooted(rel)) return false;
            foreach (string seg in rel.Split('/'))
            {
                if (seg == "..") return false;
            }
            return true;
        }

        /// <summary>
        /// True only for a plain file name (no directory separators, not rooted,
        /// not "." / ".."), so the stamp can never escape the target root.
        /// </summary>
        static bool IsSimpleFileName(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            if (name == "." || name == "..") return false;
            if (Path.IsPathRooted(name)) return false;
            if (name.IndexOf('/') >= 0 || name.IndexOf('\\') >= 0) return false;
            // Path.GetFileName strips any directory portion; if it differs, the
            // input carried path structure we don't allow.
            return string.Equals(Path.GetFileName(name), name, StringComparison.Ordinal);
        }

        static string ToPlatformPath(string posixRel)
        {
            return posixRel.Replace('/', Path.DirectorySeparatorChar);
        }
    }
}
