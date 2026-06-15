using System;
using System.IO;
using System.IO.Compression;

namespace FEBuilderGBA
{
    /// <summary>
    /// Structural info recovered from an indexed (color-type-3) PNG by
    /// <see cref="IndexedPngReader"/> (#1150). <see cref="Ok"/> is false on any
    /// malformed input (with <see cref="Error"/> set); the reader NEVER throws.
    /// </summary>
    public sealed class IndexedPngInfo
    {
        /// <summary>True when the PNG signature + IHDR parsed cleanly.</summary>
        public bool Ok;

        /// <summary>Image width (px) from IHDR.</summary>
        public int Width;

        /// <summary>Image height (px) from IHDR.</summary>
        public int Height;

        /// <summary>Bit depth from IHDR.</summary>
        public int BitDepth;

        /// <summary>Color type from IHDR (3 == indexed/palette).</summary>
        public int ColorType;

        /// <summary>Number of palette entries (PLTE length / 3); 0 when no PLTE.</summary>
        public int PaletteColorCount;

        /// <summary>True when a tRNS chunk is present.</summary>
        public bool HasTrns;

        /// <summary>Transparent palette indices (from tRNS, value 0); empty when none.</summary>
        public int[] TransparentIndices = Array.Empty<int>();

        /// <summary>Recovered per-pixel palette indices (row-major); null when unavailable.</summary>
        public byte[] Indices;

        /// <summary>True when <see cref="Indices"/> was reconstructed.</summary>
        public bool IndicesAvailable;

        /// <summary>
        /// True when the IDAT used a PNG scanline filter other than 0 (None), so the
        /// index-level recovery was skipped — a structural check is still valid.
        /// </summary>
        public bool FiltersUnsupportedForIndexCheck;

        /// <summary>Error description when <see cref="Ok"/> is false; "" otherwise.</summary>
        public string Error = "";
    }

    /// <summary>
    /// PURE, dependency-free indexed-PNG READER (#1150) — the inverse of
    /// <see cref="IndexedPngWriter"/>. Parses the PNG signature + IHDR + PLTE + tRNS and
    /// INFLATEs IDAT to recover the per-pixel palette indices for validation.
    ///
    /// Only PNG scanline filter type 0 (None) is reconstructed — the writer always emits
    /// filter 0, so a FEBuilder-exported PNG round-trips exactly. A PNG that uses other
    /// filters parses structurally (dims/palette/tRNS) but sets
    /// <see cref="IndexedPngInfo.FiltersUnsupportedForIndexCheck"/> instead of throwing.
    ///
    /// NEVER throws: any malformed input yields <c>Ok=false</c> + an <c>Error</c>.
    /// </summary>
    public static class IndexedPngReader
    {
        static readonly byte[] PngSignature = { 137, 80, 78, 71, 13, 10, 26, 10 };

        /// <summary>
        /// Read an indexed PNG. Returns a populated <see cref="IndexedPngInfo"/>; on any
        /// fault returns <c>Ok=false</c> with <c>Error</c> set. NEVER throws.
        /// </summary>
        public static IndexedPngInfo Read(byte[] pngBytes)
        {
            var info = new IndexedPngInfo();
            try
            {
                if (pngBytes == null || pngBytes.Length < 8)
                { info.Error = "PNG too short / null."; return info; }

                for (int i = 0; i < 8; i++)
                {
                    if (pngBytes[i] != PngSignature[i])
                    { info.Error = "Bad PNG signature."; return info; }
                }

                int pos = 8;
                bool sawIhdr = false;
                bool sawPlte = false;
                bool sawIdat = false;
                bool sawIend = false;

                // Finding #5: accumulate IDAT chunk slices and concatenate ONCE after the
                // loop (O(n)), instead of growing a byte[] per chunk (the old O(n^2) path).
                var idatChunks = new System.Collections.Generic.List<byte[]>();

                while (pos + 8 <= pngBytes.Length)
                {
                    int len = ReadU32BE(pngBytes, pos);
                    if (len < 0 || pos + 12 + (long)len > pngBytes.Length)
                    { info.Error = "Truncated chunk."; return info; }
                    string type = ReadType(pngBytes, pos + 4);
                    int dataPos = pos + 8;

                    switch (type)
                    {
                        case "IHDR":
                            if (len < 13) { info.Error = "Short IHDR."; return info; }
                            info.Width = ReadU32BE(pngBytes, dataPos);
                            info.Height = ReadU32BE(pngBytes, dataPos + 4);
                            info.BitDepth = pngBytes[dataPos + 8];
                            info.ColorType = pngBytes[dataPos + 9];
                            sawIhdr = true;
                            break;

                        case "PLTE":
                            info.PaletteColorCount = len / 3;
                            sawPlte = true;
                            break;

                        case "tRNS":
                        {
                            info.HasTrns = true;
                            // An index i is fully transparent when its tRNS alpha byte == 0;
                            // indices beyond the tRNS length are implicitly opaque (255).
                            var trans = new System.Collections.Generic.List<int>();
                            for (int t = 0; t < len; t++)
                            {
                                if (pngBytes[dataPos + t] == 0)
                                    trans.Add(t);
                            }
                            info.TransparentIndices = trans.ToArray();
                            break;
                        }

                        case "IDAT":
                        {
                            var chunk = new byte[len];
                            Array.Copy(pngBytes, dataPos, chunk, 0, len);
                            idatChunks.Add(chunk);
                            sawIdat = true;
                            break;
                        }

                        case "IEND":
                            sawIend = true;
                            break;
                    }

                    pos = dataPos + len + 4; // skip data + CRC
                    if (type == "IEND") break;
                }

                if (!sawIhdr)
                { info.Error = "No IHDR chunk."; return info; }

                // Finding #1: a header-only / incomplete PNG must NOT be accepted as valid.
                // Every well-formed PNG requires an IDAT (pixel data) and an IEND terminator;
                // an indexed (colorType 3) PNG additionally requires a PLTE chunk. For
                // non-indexed color types PLTE is optional, but the file is still read enough
                // for DecompAssetValidatorCore to emit NON_INDEXED — we just require IDAT+IEND
                // so a header-only file is rejected as a bad PNG instead of "0 errors, OK".
                if (!sawIdat)
                { info.Error = "Missing IDAT chunk (incomplete PNG — no pixel data)."; return info; }
                if (!sawIend)
                { info.Error = "Missing IEND chunk (truncated PNG)."; return info; }
                if (info.ColorType == 3 && !sawPlte)
                { info.Error = "Missing PLTE chunk (an indexed PNG requires a palette)."; return info; }

                // Concatenate all IDAT chunk bytes ONCE (Finding #5).
                byte[] idat = ConcatChunks(idatChunks);

                info.Ok = true;

                // Index recovery only for indexed 8-bit (what the writer emits) with IDAT.
                if (info.ColorType == 3 && info.BitDepth == 8 && idat != null && info.Width > 0 && info.Height > 0)
                {
                    TryRecoverIndices(idat, info);
                }

                return info;
            }
            catch (Exception ex)
            {
                info.Ok = false;
                info.IndicesAvailable = false;
                info.Error = "Reader fault: " + ex.Message;
                return info;
            }
        }

        /// <summary>
        /// Inflate the zlib IDAT stream and reconstruct filter-0 scanlines into a flat
        /// index array. Sets <see cref="IndexedPngInfo.FiltersUnsupportedForIndexCheck"/>
        /// (instead of throwing) when a non-zero filter byte appears. NEVER throws.
        /// </summary>
        static void TryRecoverIndices(byte[] idat, IndexedPngInfo info)
        {
            try
            {
                // zlib: 2-byte header, then a raw DEFLATE body. Skip the 2-byte header.
                if (idat.Length < 2)
                    return;
                byte[] raw;
                using (var ms = new MemoryStream(idat, 2, idat.Length - 2))
                using (var inflate = new DeflateStream(ms, CompressionMode.Decompress))
                using (var outMs = new MemoryStream())
                {
                    inflate.CopyTo(outMs);
                    raw = outMs.ToArray();
                }

                int w = info.Width, h = info.Height;
                long expected = (long)h * (1 + w);
                if (raw.Length < expected)
                    return; // not enough scanline data; leave indices unavailable

                var indices = new byte[w * h];
                for (int y = 0; y < h; y++)
                {
                    int rowBase = y * (1 + w);
                    byte filter = raw[rowBase];
                    if (filter != 0)
                    {
                        // Only filter type 0 (None) is reconstructed here.
                        info.FiltersUnsupportedForIndexCheck = true;
                        info.IndicesAvailable = false;
                        return;
                    }
                    Array.Copy(raw, rowBase + 1, indices, y * w, w);
                }

                info.Indices = indices;
                info.IndicesAvailable = true;
            }
            catch
            {
                // inflate / shape fault: indices simply unavailable (structural still ok).
                info.IndicesAvailable = false;
            }
        }

        /// <summary>
        /// Concatenate the accumulated IDAT chunk byte arrays into one buffer in a single
        /// pass (Finding #5). Returns an empty array when there are no chunks; the byte
        /// content is identical to the old per-chunk grow path.
        /// </summary>
        static byte[] ConcatChunks(System.Collections.Generic.List<byte[]> chunks)
        {
            if (chunks == null || chunks.Count == 0)
                return Array.Empty<byte>();
            int total = 0;
            for (int i = 0; i < chunks.Count; i++)
                total += chunks[i].Length;
            var result = new byte[total];
            int offset = 0;
            for (int i = 0; i < chunks.Count; i++)
            {
                Array.Copy(chunks[i], 0, result, offset, chunks[i].Length);
                offset += chunks[i].Length;
            }
            return result;
        }

        static int ReadU32BE(byte[] b, int off)
        {
            // PNG lengths/dimensions are < 2^31 in practice; cast keeps an int interface.
            long v = ((long)b[off] << 24) | ((long)b[off + 1] << 16) | ((long)b[off + 2] << 8) | b[off + 3];
            return v > int.MaxValue ? -1 : (int)v;
        }

        static string ReadType(byte[] b, int off)
        {
            return "" + (char)b[off] + (char)b[off + 1] + (char)b[off + 2] + (char)b[off + 3];
        }
    }
}
