// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Buffers;
using System.Buffers.Binary;
using System.IO;

namespace FEBuilderGBA.Core
{
    /// <summary>
    /// Pure-managed MD5 compatibility helper for legacy ROM/editor fingerprints.
    /// MD5 is intentionally used only for non-security identifiers and cache keys.
    /// </summary>
    public static class ManagedMd5
    {
        static readonly int[] Shift =
        {
            7, 12, 17, 22, 7, 12, 17, 22, 7, 12, 17, 22, 7, 12, 17, 22,
            5, 9, 14, 20, 5, 9, 14, 20, 5, 9, 14, 20, 5, 9, 14, 20,
            4, 11, 16, 23, 4, 11, 16, 23, 4, 11, 16, 23, 4, 11, 16, 23,
            6, 10, 15, 21, 6, 10, 15, 21, 6, 10, 15, 21, 6, 10, 15, 21,
        };

        static readonly uint[] Table =
        {
            0xd76aa478, 0xe8c7b756, 0x242070db, 0xc1bdceee,
            0xf57c0faf, 0x4787c62a, 0xa8304613, 0xfd469501,
            0x698098d8, 0x8b44f7af, 0xffff5bb1, 0x895cd7be,
            0x6b901122, 0xfd987193, 0xa679438e, 0x49b40821,
            0xf61e2562, 0xc040b340, 0x265e5a51, 0xe9b6c7aa,
            0xd62f105d, 0x02441453, 0xd8a1e681, 0xe7d3fbc8,
            0x21e1cde6, 0xc33707d6, 0xf4d50d87, 0x455a14ed,
            0xa9e3e905, 0xfcefa3f8, 0x676f02d9, 0x8d2a4c8a,
            0xfffa3942, 0x8771f681, 0x6d9d6122, 0xfde5380c,
            0xa4beea44, 0x4bdecfa9, 0xf6bb4b60, 0xbebfbc70,
            0x289b7ec6, 0xeaa127fa, 0xd4ef3085, 0x04881d05,
            0xd9d4d039, 0xe6db99e5, 0x1fa27cf8, 0xc4ac5665,
            0xf4292244, 0x432aff97, 0xab9423a7, 0xfc93a039,
            0x655b59c3, 0x8f0ccc92, 0xffeff47d, 0x85845dd1,
            0x6fa87e4f, 0xfe2ce6e0, 0xa3014314, 0x4e0811a1,
            0xf7537e82, 0xbd3af235, 0x2ad7d2bb, 0xeb86d391,
        };

        public static string ComputeHex(byte[] data)
        {
            ArgumentNullException.ThrowIfNull(data);
            return ComputeHex(data.AsSpan());
        }

        public static string ComputeHex(ReadOnlySpan<byte> data)
        {
            uint a = 0x67452301;
            uint b = 0xefcdab89;
            uint c = 0x98badcfe;
            uint d = 0x10325476;

            ReadOnlySpan<byte> remaining = data;
            while (remaining.Length >= 64)
            {
                Transform(remaining.Slice(0, 64), ref a, ref b, ref c, ref d);
                remaining = remaining.Slice(64);
            }

            Span<byte> digest = stackalloc byte[16];
            Finish(remaining, (ulong)data.Length, ref a, ref b, ref c, ref d, digest);
            return ToLowerHex(digest);
        }

        public static string ComputeHex(Stream stream)
        {
            ArgumentNullException.ThrowIfNull(stream);

            uint a = 0x67452301;
            uint b = 0xefcdab89;
            uint c = 0x98badcfe;
            uint d = 0x10325476;

            byte[] rented = ArrayPool<byte>.Shared.Rent(8192);
            Span<byte> pending = stackalloc byte[64];
            int pendingLength = 0;
            ulong totalLength = 0;

            try
            {
                int read;
                while ((read = stream.Read(rented, 0, rented.Length)) > 0)
                {
                    unchecked { totalLength += (ulong)read; }
                    ReadOnlySpan<byte> input = rented.AsSpan(0, read);

                    if (pendingLength > 0)
                    {
                        int fill = Math.Min(64 - pendingLength, input.Length);
                        input.Slice(0, fill).CopyTo(pending.Slice(pendingLength));
                        pendingLength += fill;
                        input = input.Slice(fill);

                        if (pendingLength == 64)
                        {
                            Transform(pending, ref a, ref b, ref c, ref d);
                            pendingLength = 0;
                        }
                    }

                    while (input.Length >= 64)
                    {
                        Transform(input.Slice(0, 64), ref a, ref b, ref c, ref d);
                        input = input.Slice(64);
                    }

                    if (!input.IsEmpty)
                    {
                        input.CopyTo(pending);
                        pendingLength = input.Length;
                    }
                }

                Span<byte> digest = stackalloc byte[16];
                Finish(pending.Slice(0, pendingLength), totalLength, ref a, ref b, ref c, ref d, digest);
                return ToLowerHex(digest);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(rented);
            }
        }

        public static string ComputeFileHex(string path)
        {
            ArgumentNullException.ThrowIfNull(path);
            using FileStream stream = File.OpenRead(path);
            return ComputeHex(stream);
        }

        static void Finish(
            ReadOnlySpan<byte> remaining,
            ulong totalLength,
            ref uint a,
            ref uint b,
            ref uint c,
            ref uint d,
            Span<byte> digest)
        {
            Span<byte> block = stackalloc byte[64];
            block.Clear();
            remaining.CopyTo(block);
            block[remaining.Length] = 0x80;

            if (remaining.Length >= 56)
            {
                Transform(block, ref a, ref b, ref c, ref d);
                block.Clear();
            }

            ulong bitLength = unchecked(totalLength * 8);
            BinaryPrimitives.WriteUInt64LittleEndian(block.Slice(56, 8), bitLength);
            Transform(block, ref a, ref b, ref c, ref d);

            BinaryPrimitives.WriteUInt32LittleEndian(digest.Slice(0, 4), a);
            BinaryPrimitives.WriteUInt32LittleEndian(digest.Slice(4, 4), b);
            BinaryPrimitives.WriteUInt32LittleEndian(digest.Slice(8, 4), c);
            BinaryPrimitives.WriteUInt32LittleEndian(digest.Slice(12, 4), d);
        }

        static void Transform(ReadOnlySpan<byte> block, ref uint a, ref uint b, ref uint c, ref uint d)
        {
            Span<uint> words = stackalloc uint[16];
            for (int i = 0; i < words.Length; i++)
                words[i] = BinaryPrimitives.ReadUInt32LittleEndian(block.Slice(i * 4, 4));

            uint aa = a;
            uint bb = b;
            uint cc = c;
            uint dd = d;

            for (int i = 0; i < 64; i++)
            {
                uint f;
                int g;

                if (i < 16)
                {
                    f = (bb & cc) | (~bb & dd);
                    g = i;
                }
                else if (i < 32)
                {
                    f = (dd & bb) | (~dd & cc);
                    g = (5 * i + 1) & 0x0F;
                }
                else if (i < 48)
                {
                    f = bb ^ cc ^ dd;
                    g = (3 * i + 5) & 0x0F;
                }
                else
                {
                    f = cc ^ (bb | ~dd);
                    g = (7 * i) & 0x0F;
                }

                uint temp = dd;
                dd = cc;
                cc = bb;
                bb = unchecked(bb + RotateLeft(unchecked(aa + f + Table[i] + words[g]), Shift[i]));
                aa = temp;
            }

            a = unchecked(a + aa);
            b = unchecked(b + bb);
            c = unchecked(c + cc);
            d = unchecked(d + dd);
        }

        static uint RotateLeft(uint value, int bits)
        {
            return (value << bits) | (value >> (32 - bits));
        }

        static string ToLowerHex(ReadOnlySpan<byte> bytes)
        {
            const string hex = "0123456789abcdef";
            Span<char> chars = stackalloc char[32];
            int pos = 0;
            foreach (byte value in bytes)
            {
                chars[pos++] = hex[value >> 4];
                chars[pos++] = hex[value & 0x0F];
            }
            return new string(chars);
        }
    }
}
