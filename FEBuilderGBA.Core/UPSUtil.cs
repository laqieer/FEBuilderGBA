using System;
using System.Collections.Generic;
using System.IO;

namespace FEBuilderGBA
{
    /// <summary>
    /// Core UPS patch creation/validation logic (cross-platform, no WinForms dependency).
    /// WinForms UPSUtil delegates to this for shared computation.
    /// </summary>
    public static class UPSUtilCore
    {
        /// <summary>
        /// CRC32 calculator (same algorithm as U.CRC32 in WinForms).
        /// </summary>
        public class CRC32
        {
            private readonly uint[] crcTable;

            public CRC32()
            {
                crcTable = new uint[256];
                for (uint i = 0; i < 256; i++)
                {
                    var x = i;
                    for (var j = 0; j < 8; j++)
                    {
                        x = (uint)((x & 1) == 0 ? x >> 1 : -306674912 ^ x >> 1);
                    }
                    crcTable[i] = x;
                }
            }

            public uint Calc(byte[] buf)
            {
                uint num = uint.MaxValue;
                for (var i = 0; i < buf.Length; i++)
                {
                    num = crcTable[(num ^ buf[i]) & 255] ^ num >> 8;
                }
                return (uint)(num ^ -1);
            }
        }

        /// <summary>
        /// Read the source CRC32 from a UPS file.
        /// </summary>
        public static uint GetUPSSrcCRC32(string upsFilename)
        {
            byte[] patch = File.ReadAllBytes(upsFilename);
            if (patch.Length < 16)
            {
                return U.NOT_FOUND;
            }
            uint src_crc32 = U.u32(patch, (uint)(patch.Length - 12));
            return src_crc32;
        }

        /// <summary>
        /// Check if a file is a valid UPS file by reading its header.
        /// </summary>
        public static bool IsUPSFile(string filename)
        {
            byte[] bin = File.ReadAllBytes(filename);
            return IsUPSData(bin);
        }

        /// <summary>
        /// Check if byte data has a valid UPS header.
        /// </summary>
        public static bool IsUPSData(byte[] data)
        {
            if (data == null || data.Length <= 4)
                return false;
            return data[0] == 'U' &&
                   data[1] == 'P' &&
                   data[2] == 'S' &&
                   data[3] == '1';
        }

        /// <summary>
        /// Create a UPS patch from source and destination byte arrays.
        /// Writes the result to the specified file.
        /// </summary>
        public static void MakeUPS(byte[] src, byte[] dst, string upsFilename)
        {
            byte[] upsData = MakeUPSData(src, dst);
            U.WriteAllBytes(upsFilename, upsData);
        }

        /// <summary>
        /// Create UPS patch data from source and destination byte arrays.
        /// Returns the raw UPS patch bytes.
        /// </summary>
        public static byte[] MakeUPSData(byte[] src, byte[] dst)
        {
            List<byte> ups = new List<byte>();
            ups.Add((byte)'U');
            ups.Add((byte)'P');
            ups.Add((byte)'S');
            ups.Add((byte)'1');
            AppendValCode(ups, (uint)src.Length);
            AppendValCode(ups, (uint)dst.Length);

            uint length = (uint)Math.Max(src.Length, dst.Length);
            uint lastPoint = 0;
            for (uint i = 0; i < length; i++)
            {
                int ss = i < src.Length ? src[i] : 0;
                int dd = i < dst.Length ? dst[i] : 0;

                if (ss == dd)
                    continue;

                AppendValCode(ups, (uint)(i - lastPoint));

                List<byte> diff = new List<byte>();
                diff.Add((byte)(ss ^ dd));

                uint n;
                for (n = i + 1; n < length; n++)
                {
                    ss = n < src.Length ? src[n] : 0;
                    dd = n < dst.Length ? dst[n] : 0;
                    if (ss == dd)
                        break;
                    diff.Add((byte)(ss ^ dd));
                }
                ups.AddRange(diff.ToArray());
                ups.Add(0);

                i = n;
                lastPoint = n + 1;
            }
            CRC32 crc32 = new CRC32();
            U.append_u32(ups, crc32.Calc(src));
            U.append_u32(ups, crc32.Calc(dst));
            U.append_u32(ups, crc32.Calc(ups.ToArray()));

            return ups.ToArray();
        }

        /// <summary>
        /// Apply a UPS patch to source data. Returns patched data or null on failure.
        /// errorMessage is set on failure.
        /// </summary>
        public static byte[] ApplyUPS(byte[] sourceData, byte[] patchData, out string errorMessage)
        {
            errorMessage = null;

            if (patchData.Length < 16)
            {
                errorMessage = "UPS file is corrupted: below minimum size.";
                return null;
            }

            if (!IsUPSData(patchData))
            {
                errorMessage = "UPS file is corrupted: missing UPS1 header.";
                return null;
            }

            CRC32 crc32 = new CRC32();

            // Verify patch CRC
            uint patchCalcCrc32 = crc32.Calc(U.subrange(patchData, 0, (uint)(patchData.Length - 4)));
            uint patchCrc32 = U.u32(patchData, (uint)(patchData.Length - 4));
            if (patchCalcCrc32 != patchCrc32)
            {
                errorMessage = "UPS file CRC mismatch (patch may be corrupted).";
                // Continue anyway — some tools produce bad CRCs
            }

            // Verify source CRC
            uint srcCalcCrc32 = crc32.Calc(sourceData);
            uint srcCrc32 = U.u32(patchData, (uint)(patchData.Length - 12));
            if (srcCalcCrc32 != srcCrc32)
            {
                errorMessage = $"Source ROM CRC mismatch. Expected: 0x{srcCrc32:X8}, Got: 0x{srcCalcCrc32:X8}";
                return null;
            }

            uint i = 4; // skip UPS1 header
            uint sourceSize = ReadValCode(patchData, i, out i);
            uint destSize = ReadValCode(patchData, i, out i);

            byte[] result = new byte[destSize];
            Array.Copy(sourceData, 0, result, 0, Math.Min(sourceData.Length, (int)destSize));

            uint romI = 0;
            uint end = (uint)(patchData.Length - 4 * 3);
            for (; i < end; i++)
            {
                uint skipSize = ReadValCode(patchData, i, out i);
                romI += skipSize;
                for (; i < end; i++)
                {
                    if (romI >= destSize)
                        break;
                    result[romI] = (byte)(result[romI] ^ patchData[i]);
                    romI++;
                    if (patchData[i] == 0x00)
                        break;
                }
            }

            // Verify dest CRC
            uint destCalcCrc32 = crc32.Calc(result);
            uint destCrc32 = U.u32(patchData, (uint)(patchData.Length - 8));
            if (destCalcCrc32 != destCrc32)
            {
                errorMessage = "Result CRC mismatch after applying UPS patch.";
                // Return the result anyway — caller can decide
            }

            return result;
        }

        /// <summary>
        /// Encode a variable-length integer in UPS format and append to list.
        /// </summary>
        internal static void AppendValCode(List<byte> data, uint v)
        {
            byte x = (byte)(v & 0x7f);
            v = v >> 7;

            while (v != 0x0)
            {
                data.Add(x);
                v--;
                x = (byte)(v & 0x7f);
                v = v >> 7;
            }
            data.Add((byte)(x | 0x80));
        }

        /// <summary>
        /// Decode a variable-length integer from UPS format.
        /// </summary>
        internal static uint ReadValCode(byte[] data, uint i, out uint nextI)
        {
            uint shift = 1;
            uint value = 0;
            while (true)
            {
                byte b = data[i];
                i++;
                value += ((uint)(b & 0x7f)) * shift;
                if ((b & 0x80) == 0x80)
                    break;
                shift <<= 7;
                value += shift;
            }
            nextI = i;
            return value;
        }
    }
}
