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
