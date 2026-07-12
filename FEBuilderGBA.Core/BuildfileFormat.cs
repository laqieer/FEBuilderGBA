// SPDX-License-Identifier: GPL-3.0-or-later
// Canonical schema-v1 formatting shared by the buildfile exporter (#1935) and the
// independent buildfile consumer (#1936).
//
// The exporter EMITS these exact spellings and the consumer VALIDATES against them,
// so both sides MUST agree byte-for-byte. Keeping the hashing, hex, and payload-name
// spellings in one place removes the "divergent formatting" risk of the two sides
// re-implementing the same rules slightly differently.
using System;
using System.Security.Cryptography;
using System.Text;

namespace FEBuilderGBA
{
    /// <summary>
    /// Canonical schema-v1 hashing/hex/payload-name spellings shared by
    /// <see cref="BuildfileExportCore"/> and <see cref="BuildfileBuildCore"/>.
    /// </summary>
    internal static class BuildfileFormat
    {
        /// <summary>GBA cartridge ROM window base (offset 0 maps to 0x08000000).</summary>
        internal const uint GbaRomBase = 0x08000000u;

        /// <summary>
        /// Shared resource-safety bound on the COMBINED count of advisory buildfile.json
        /// items — <c>patches.installed</c> records, every nested <c>patches.installed[].params</c>
        /// entry, and <c>warnings</c> entries — counted together as ONE global total (#1936
        /// consumer / #1935 exporter). Exposed as public const aliases on
        /// <see cref="BuildfileExportOptions.MaxAdvisoryItems"/> and
        /// <see cref="BuildfileBuildOptions.MaxAdvisoryItems"/> so both sides can never drift.
        /// There is deliberately NO mutable/test-only override: the real 16,384 value is exact
        /// enough to prove pass-at-cap / fail-at-cap+1 deterministically without materializing
        /// gigabytes of test fixtures.
        /// </summary>
        internal const int MaxAdvisoryItems = 16384;

        /// <summary>
        /// Shared resource-safety bound on the maximum accepted/emitted <c>buildfile.json</c>
        /// size in exact UTF-8 bytes (16 MiB), INCLUDING the trailing LF
        /// <see cref="BuildfileExportCore.SerializeManifest"/> always appends (#1936 consumer /
        /// #1935 exporter follow-up: the exporter previously had no matching byte cap of its
        /// own, so it could publish a <c>buildfile.json</c> its own consumer would refuse to
        /// open). Exposed as aliases on <see cref="BuildfileBuildOptions.MaxManifestBytes"/> and
        /// <see cref="BuildfileExportOptions.MaxManifestBytes"/> so producer and consumer can
        /// never drift from each other. This is the ONLY literal spelling of the 16 MiB value —
        /// every other reference is an alias of this constant.
        /// </summary>
        internal const int MaxManifestBytes = 16 * 1024 * 1024;

        /// <summary>
        /// Overflow-safe budget consumption: adds <paramref name="additional"/> to
        /// <paramref name="total"/> and returns <c>true</c> only when the resulting combined
        /// total is still within <see cref="MaxAdvisoryItems"/>. On failure <paramref name="total"/>
        /// is left UNCHANGED (the caller should fail closed rather than partially consume the
        /// budget). Uses 64-bit arithmetic internally so a huge <paramref name="additional"/>
        /// (e.g. a maliciously large declared JSON array length) can never wrap a 32-bit
        /// accumulator into a false pass.
        /// </summary>
        internal static bool TryConsumeAdvisoryItems(ref int total, int additional)
        {
            if (additional < 0) throw new ArgumentOutOfRangeException(nameof(additional));
            long candidate = (long)total + additional;
            if (candidate > MaxAdvisoryItems) return false;
            total = (int)candidate;
            return true;
        }

        /// <summary>Lowercase 64-character hex SHA-256 of <paramref name="data"/>.</summary>
        internal static string Sha256Hex(byte[] data)
        {
            byte[] hash = SHA256.HashData(data);
            var sb = new StringBuilder(hash.Length * 2);
            foreach (byte b in hash) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }

        /// <summary>Raw CRC32 of <paramref name="data"/> (same table as the exporter).</summary>
        internal static uint Crc32(byte[] data) => new U.CRC32().Calc(data);

        /// <summary>Canonical <c>0x</c>-prefixed uppercase 8-digit CRC32 spelling.</summary>
        internal static string Crc32Hex(byte[] data) => Hex32(Crc32(data));

        /// <summary>Canonical <c>0x</c>-prefixed uppercase 8-digit hex of a 32-bit value.</summary>
        internal static string Hex32(uint v) => "0x" + v.ToString("X8");

        /// <summary>Canonical <c>0x</c>-prefixed uppercase 2-digit hex of a byte.</summary>
        internal static string Hex8(byte v) => "0x" + v.ToString("X2");

        /// <summary>Canonical mapped GBA address spelling for a ROM offset.</summary>
        internal static string GbaAddress(uint offset) => Hex32(GbaRomBase + offset);

        /// <summary>
        /// Canonical exporter-owned payload file name (relative to <c>data/</c>):
        /// <c>{index:D4}_{offset:X6}_{length}.bin</c>.
        /// </summary>
        internal static string PayloadName(int index, uint offset, uint length)
            => index.ToString("D4") + "_" + offset.ToString("X6") + "_" + length + ".bin";

        /// <summary>Canonical manifest payload path: <c>data/{index:D4}_{offset:X6}_{length}.bin</c>.</summary>
        internal static string PayloadPath(int index, uint offset, uint length)
            => "data/" + PayloadName(index, offset, length);

        /// <summary>True for a canonical <c>0x</c>-prefixed uppercase 8-digit hex string.</summary>
        internal static bool IsCanonicalHex32(string s) => IsPrefixedUpperHex(s, 8);

        /// <summary>True for a canonical <c>0x</c>-prefixed uppercase 2-digit hex string.</summary>
        internal static bool IsCanonicalHex8(string s) => IsPrefixedUpperHex(s, 2);

        /// <summary>True for a canonical lowercase 64-character hex SHA-256 string.</summary>
        internal static bool IsCanonicalSha256(string s)
        {
            if (s == null || s.Length != 64) return false;
            foreach (char c in s)
            {
                bool ok = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f');
                if (!ok) return false;
            }
            return true;
        }

        static bool IsPrefixedUpperHex(string s, int digits)
        {
            if (s == null || s.Length != digits + 2) return false;
            if (s[0] != '0' || s[1] != 'x') return false;
            for (int i = 2; i < s.Length; i++)
            {
                char c = s[i];
                bool ok = (c >= '0' && c <= '9') || (c >= 'A' && c <= 'F');
                if (!ok) return false;
            }
            return true;
        }
    }
}
