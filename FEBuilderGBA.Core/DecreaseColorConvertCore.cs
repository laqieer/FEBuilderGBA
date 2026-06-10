using System;
using System.Collections.Generic;
using System.IO;

namespace FEBuilderGBA
{
    /// <summary>
    /// Cross-platform port of the WinForms <c>DecreaseColor</c> engine
    /// (FEBuilderGBA/DecreaseColor.cs): a TSA-aware multi-palette color reducer
    /// used by the file→file "Color Reduction Tool".
    ///
    /// The reducer constrains every 8×8 tile to a single 16-color palette bank,
    /// producing up to <c>maxPalette × 16</c> colors (16 banks max). It is pure
    /// image processing — NO ROM, NO Undo, NO System.Drawing — operating on a
    /// flat RGBA byte array (4 bytes/pixel: R, G, B, A) instead of a Bitmap.
    ///
    /// The existing single-palette <see cref="DecreaseColorCore.Quantize"/> is a
    /// separate (median-cut) engine and is intentionally left untouched; this is
    /// a faithful port of the distinct WF tile-ranking + palette-assignment +
    /// k-means (<c>Convert16Color</c>) flow.
    /// </summary>
    public static class DecreaseColorConvertCore
    {
        // ---- public result / preset types -------------------------------------

        /// <summary>In-memory result of a multi-palette reduce.</summary>
        public sealed class DecreaseColorConvertResult
        {
            /// <summary>Output width = Padding8(srcW) + yohaku.</summary>
            public int Width { get; set; }
            /// <summary>Output height = Padding8(srcH).</summary>
            public int Height { get; set; }
            /// <summary>One byte per output pixel: the banked palette index (paletteNo*16+i, or flat i for ignoreTSA).</summary>
            public byte[] IndexData { get; set; }
            /// <summary>Full palette as GBA bytes (2 bytes/color, RGB555 little-endian), zero-padded to 256 colors (512 bytes) like WF.</summary>
            public byte[] GbaPalette { get; set; }
            /// <summary>Number of 16-color banks in use (=maxPalette for multi; 1 for the ignoreTSA/flat path).</summary>
            public int PaletteBankCount { get; set; }
        }

        /// <summary>A "Method" combo preset (mirrors WF DecreaseColorTSAToolForm.Method_SelectedIndexChanged).</summary>
        public struct DecreaseColorPreset
        {
            public int Width;
            public int Height;
            public int Yohaku;
            public int PaletteNo;
            public bool Scalable;
            public bool Reserve1st;
            public bool IgnoreTSA;
        }

        // Total palette entries WF zero-pads to (16 banks * 16 colors).
        private const int TOTAL_PALETTE_COLORS = 256;

        // ---- internal color/tile helpers (verbatim port) ----------------------

        private sealed class ColorRanking
        {
            public int R { get; private set; }
            public int G { get; private set; }
            public int B { get; private set; }
            public int Count;
            public int PaletteNumber;

            public ColorRanking()
            {
            }
            public ColorRanking(int r, int g, int b)
            {
                SetRGB(r, g, b);
                Count = 1;
                PaletteNumber = -1;
            }
            public ColorRanking Clone()
            {
                ColorRanking a = new ColorRanking();
                a.R = this.R;
                a.G = this.G;
                a.B = this.B;
                a.Count = this.Count;
                a.PaletteNumber = this.PaletteNumber;
                return a;
            }
            public void SetRGB(int r, int g, int b)
            {
                R = r;
                G = g;
                B = b;
            }
        }

        private sealed class TileMapping
        {
            public int Palette;
            public List<ColorRanking> Rank;
            public int X;
            public int Y;
        }

        // ---- public entry -----------------------------------------------------

        /// <summary>
        /// Reduce an RGBA image so each 8×8 tile uses a single 16-color palette bank.
        /// Faithful port of WF <c>DecreaseColor.Convert</c>.
        /// When <paramref name="maxPalette"/> == 1 or <paramref name="ignoreTSA"/> is true,
        /// routes to the flat single-palette <c>ConvertIgnoreTSA</c> path
        /// (maxColor = maxPalette*16, isUseTransparent = maxPalette &lt; 16).
        /// </summary>
        /// <param name="rgba">Source pixels, 4 bytes/pixel (R, G, B, A).</param>
        /// <param name="width">Source width.</param>
        /// <param name="height">Source height.</param>
        /// <param name="maxPalette">Number of 16-color banks (1..16).</param>
        /// <param name="yohaku">Right-margin (in pixels) added to the output width, all index 0.</param>
        /// <param name="reserve1st">Reserve palette slot 0 of each bank for the background/transparent color.</param>
        /// <param name="ignoreTSA">Produce one flat palette instead of per-tile banks.</param>
        public static DecreaseColorConvertResult Convert(byte[] rgba, int width, int height, int maxPalette, int yohaku, bool reserve1st, bool ignoreTSA)
        {
            if (rgba == null) throw new ArgumentNullException(nameof(rgba));
            if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width), "width/height must be positive");
            if (maxPalette < 1) maxPalette = 1;
            if (maxPalette > 16) maxPalette = 16;
            if (yohaku < 0) yohaku = 0;
            long needed = (long)width * height * 4;
            if (rgba.Length < needed)
                throw new ArgumentException($"rgba too small: need {needed} bytes, have {rgba.Length}", nameof(rgba));

            if (maxPalette == 1 || ignoreTSA)
            {//1パレット16色 または、 TSA無効の場合
                return ConvertIgnoreTSA(rgba, width, height, maxPalette * 16, yohaku, reserve1st, maxPalette < 16);
            }

            int padW = U.Padding8(width);
            int padH = U.Padding8(height);
            int destW = padW + yohaku;
            int destH = padH;

            List<ColorRanking> totalRank = new List<ColorRanking>();
            List<TileMapping> tileMapping = new List<TileMapping>();
            for (int y = 0; y < padH; y += 8)
            {
                if (y + 8 > padH)
                {
                    continue;
                }
                for (int x = 0; x < padW; x += 8)
                {
                    if (x + 8 > padW)
                    {
                        continue;
                    }

                    TileMapping tm = new TileMapping();
                    tm.Rank = new List<ColorRanking>();
                    tm.Palette = -1;
                    tm.X = x;
                    tm.Y = y;
                    for (int yy = 0; yy < 8; yy++)
                    {
                        for (int xx = 0; xx < 8; xx++)
                        {
                            if (!TryGetPixel(rgba, width, height, x + xx, y + yy, out int cr, out int cg, out int cb, out int ca))
                            {
                                continue; // padded area beyond the source = transparent
                            }
                            if (ca == 0)
                            {
                                continue;
                            }
                            VoteColorSnap(tm.Rank, cr, cg, cb);
                        }
                    }
                    SortColor(tm.Rank);
                    tileMapping.Add(tm);

                    //複数パレットの場合、そのタイルの中で最も人気の色
                    if (tm.Rank.Count > 0)
                    {
                        VoteColor(totalRank, tm.Rank[0]);
                    }
                }
            }
            SortColor(totalRank);

            //上位の色たちをパレットに割り当てていきます.
            List<List<ColorRanking>> countList = new List<List<ColorRanking>>();
            for (int i = 0; i < maxPalette; i++)
            {
                List<ColorRanking> pal = AssingPalette(i, totalRank, tileMapping);
                countList.Add(pal);
            }
            //まだ割り当てていないタイルがあれば、一番近い色セットを持つどれかのパレットに割り当てます.
            AssingaPaletteByUnassignedTile(countList, tileMapping, totalRank);

            //パレットの色数を16色にします.
            List<ColorRanking[]> paletteList = new List<ColorRanking[]>();
            for (int i = 0; i < maxPalette; i++)
            {
                paletteList.Add(Convert16Color(countList[i], reserve1st));
            }

            //パレットの適応 (zero-padded to 256 entries, like WF ColorPalette)
            byte[] gbaPalette = new byte[TOTAL_PALETTE_COLORS * 2];
            for (int i = 0; i < maxPalette; i++)
            {
                for (int n = 0; n < 16; n++)
                {
                    WriteGbaColor(gbaPalette, i * 16 + n, paletteList[i][n].R, paletteList[i][n].G, paletteList[i][n].B);
                }
            }
            // banks [maxPalette, 16) already zero-filled.

            //ピクセルの変換
            byte[] indexData = new byte[destW * destH];
            for (int i = 0; i < tileMapping.Count; i++)
            {
                int paletteno = tileMapping[i].Palette;
                if (paletteno < 0)
                {
                    // WF Debug.Assert(false) + continue — leave as index 0.
                    continue;
                }
                int x = tileMapping[i].X;
                int y = tileMapping[i].Y;
                for (int yy = 0; yy < 8; yy++)
                {
                    for (int xx = 0; xx < 8; xx++)
                    {
                        int index;
                        if (!TryGetPixel(rgba, width, height, x + xx, y + yy, out int cr, out int cg, out int cb, out int ca))
                        {
                            index = 0;
                        }
                        else if (ca == 0)
                        {
                            index = 0;
                        }
                        else
                        {
                            index = getPaletteIndex(cr, cg, cb, countList[paletteno]);
                        }

                        int pos = (x + xx) + ((y + yy) * destW);
                        indexData[pos] = (byte)(paletteno * 16 + index);
                    }
                }
            }

            return new DecreaseColorConvertResult
            {
                Width = destW,
                Height = destH,
                IndexData = indexData,
                GbaPalette = gbaPalette,
                PaletteBankCount = maxPalette,
            };
        }

        private static DecreaseColorConvertResult ConvertIgnoreTSA(byte[] rgba, int width, int height, int maxColor, int yohaku, bool reserve1st, bool isUseTransparent)
        {
            int padW = U.Padding8(width);
            int padH = U.Padding8(height);
            int destW = padW + yohaku;
            int destH = padH;

            List<ColorRanking> totalRank = new List<ColorRanking>();
            List<TileMapping> tileMapping = new List<TileMapping>();
            for (int y = 0; y < padH; y += 8)
            {
                if (y + 8 > padH)
                {
                    continue;
                }
                for (int x = 0; x < padW; x += 8)
                {
                    if (x + 8 > padW)
                    {
                        continue;
                    }

                    TileMapping tm = new TileMapping();
                    tm.Rank = new List<ColorRanking>();
                    tm.Palette = -1;
                    tm.X = x;
                    tm.Y = y;
                    for (int yy = 0; yy < 8; yy++)
                    {
                        for (int xx = 0; xx < 8; xx++)
                        {
                            if (!TryGetPixel(rgba, width, height, x + xx, y + yy, out int cr, out int cg, out int cb, out int ca))
                            {
                                // padded area: WF would read a transparent pixel.
                                if (isUseTransparent) continue;
                                cr = cg = cb = 0; ca = 0;
                            }
                            if (ca == 0 && isUseTransparent)
                            {
                                continue;
                            }
                            VoteColorSnap(tm.Rank, cr, cg, cb);
                        }
                    }
                    SortColor(tm.Rank);
                    tileMapping.Add(tm);

                    for (int i = 0; i < tm.Rank.Count; i++)
                    {
                        VoteColor(totalRank, tm.Rank[i]);
                    }
                }
            }
            SortColor(totalRank);

            //パレットの色数を16色 or 256色にします.
            List<ColorRanking> paletteList = new List<ColorRanking>();
            paletteList.AddRange(Convert16Color(totalRank, reserve1st, maxColor));

            //パレットの適応 (zero-padded to 256 entries)
            byte[] gbaPalette = new byte[TOTAL_PALETTE_COLORS * 2];
            for (int i = 0; i < maxColor && i < paletteList.Count; i++)
            {
                WriteGbaColor(gbaPalette, i, paletteList[i].R, paletteList[i].G, paletteList[i].B);
            }

            int startColor = 0;
            if (reserve1st && isUseTransparent == false)
            {//最初の色が背景なのに、背景色を使わないということは、color index==0は予約されている
                startColor = 1;
            }

            //ピクセルの変換
            byte[] indexData = new byte[destW * destH];
            for (int i = 0; i < tileMapping.Count; i++)
            {
                int x = tileMapping[i].X;
                int y = tileMapping[i].Y;
                for (int yy = 0; yy < 8; yy++)
                {
                    for (int xx = 0; xx < 8; xx++)
                    {
                        int index;
                        bool inBounds = TryGetPixel(rgba, width, height, x + xx, y + yy, out int cr, out int cg, out int cb, out int ca);
                        if (!inBounds)
                        {
                            ca = 0;
                        }
                        if (ca == 0 && isUseTransparent)
                        {
                            index = 0;
                        }
                        else
                        {
                            if (!inBounds) { cr = cg = cb = 0; }
                            index = getPaletteIndex(cr, cg, cb, paletteList, startColor);
                        }

                        int pos = (x + xx) + ((y + yy) * destW);
                        indexData[pos] = (byte)index;
                    }
                }
            }

            return new DecreaseColorConvertResult
            {
                Width = destW,
                Height = destH,
                IndexData = indexData,
                GbaPalette = gbaPalette,
                PaletteBankCount = 1,
            };
        }

        //色からパレット変換
        private static int getPaletteIndex(int cr, int cg, int cb, List<ColorRanking> rank, int startColor = 0)
        {
            int r = ((cr >> 3) << 3);
            int g = ((cg >> 3) << 3);
            int b = ((cb >> 3) << 3);
            for (int i = startColor; i < rank.Count; i++)
            {
                if (rank[i].R == r
                    && rank[i].G == g
                    && rank[i].B == b
                    )
                {
                    return rank[i].PaletteNumber;
                }
            }

            //ありえないはずなのだが一応
            int best = 0;
            int min_score = Int32.MaxValue;
            for (int i = startColor; i < rank.Count; i++)
            {
                int score = CalcColorScore(rank[i], r, g, b);
                if (score < min_score)
                {
                    min_score = score;
                    best = i;
                }
            }
            return best;
        }

        //パレットを16色にします.
        private static ColorRanking[] Convert16Color(List<ColorRanking> countList, bool isReserve1StPalette, int maxColor = 16)
        {
            SortColor(countList);

            ColorRanking[] center = new ColorRanking[maxColor];
            int pal16;
            int first;
            if (isReserve1StPalette)
            {//最初のパレットが背景色で予約されている場合
                pal16 = maxColor - 1;
                first = 1;
                center[0] = new ColorRanking();
            }
            else
            {
                pal16 = maxColor;
                first = 0;
            }

            if (countList.Count < pal16)
            {//16色以下しかないなら、それで確定.
                for (int i = 0; i < countList.Count; i++)
                {
                    countList[i].PaletteNumber = i + first;
                    center[i + first] = countList[i].Clone();
                }
                for (int i = countList.Count; i < pal16; i++)
                {
                    center[i + first] = new ColorRanking();
                }
                return center;
            }

            //k-means法で16色にクラスタ化していきます。
            for (int k = 0; k < pal16; k++)
            {
                countList[k].PaletteNumber = k + first;
                center[k + first] = countList[k].Clone();
            }

            while (true)
            {
                //Assignment処理
                //一番近い中心点に所属を変えていく.
                bool isUpdate = false;
                for (int i = 0; i < countList.Count; i++)
                {
                    int best = 0;
                    int min_score = Int32.MaxValue;
                    for (int k = 0; k < pal16; k++)
                    {
                        int kk = k + first;
                        int score = CalcColorScore(center[kk], countList[i]);
                        if (score < min_score)
                        {
                            min_score = score;
                            best = kk;
                        }
                    }
                    if (countList[i].PaletteNumber != best)
                    {
                        countList[i].PaletteNumber = best;
                        isUpdate = true;
                    }
                }
                if (isUpdate == false)
                {//クラスタに変化がないので終了.
                    break;
                }

                //Update
                //クラスタの中心点の移動.
                for (int k = 0; k < pal16; k++)
                {
                    int kk = k + first;
                    UInt64 r = 0;
                    UInt64 g = 0;
                    UInt64 b = 0;
                    UInt64 count = 0;
                    for (int i = 0; i < countList.Count; i++)
                    {
                        if (countList[i].PaletteNumber != kk)
                        {
                            continue;
                        }

                        count += (UInt64)countList[i].Count;
                        r += (UInt64)(countList[i].R * countList[i].Count);
                        g += (UInt64)(countList[i].G * countList[i].Count);
                        b += (UInt64)(countList[i].B * countList[i].Count);
                    }

                    if (count == 0)
                    {
                        center[kk].SetRGB(0, 0, 0);
                    }
                    else
                    {
                        center[kk].SetRGB((int)(r / count), (int)(g / count), (int)(b / count));
                    }
                }
            }
            return center;
        }

        //まだ割り当てていないタイルがあれば、一番近い色セットを持つどれかのパレットに割り当てます.
        private static void AssingaPaletteByUnassignedTile(List<List<ColorRanking>> countList, List<TileMapping> tileMapping, List<ColorRanking> totalRank)
        {
            for (int i = 0; i < tileMapping.Count; i++)
            {
                if (tileMapping[i].Palette >= 0)
                {//既に割り当て済み
                    continue;
                }
                int pal = BestPalette(countList, tileMapping[i].Rank);
                tileMapping[i].Palette = pal;

                //タイルの色をすべてパレットに追加. ついでに、色ランキングから消去.
                InsertColorRank(countList[pal], tileMapping[i].Rank, totalRank);
            }
        }

        //一番近いパレットを探す
        private static int BestPalette(List<List<ColorRanking>> countList, List<ColorRanking> rank)
        {
            int best_pal = 0;
            int min_score = Int32.MaxValue;
            for (int i = 0; i < countList.Count; i++)
            {
                int score = CalcPaletteScore(countList[i], rank);
                if (score < min_score)
                {
                    min_score = score;
                    best_pal = i;
                }
            }
            return best_pal;
        }

        //パレット間の類似スコアを計算します. 小さい方がよい結果です
        private static int CalcPaletteScore(List<ColorRanking> palette, List<ColorRanking> rank)
        {
            uint count = 0;
            uint total_score = 0;
            for (int i = 0; i < rank.Count; i++)
            {
                int min_score = Int32.MaxValue;
                for (int n = 0; n < palette.Count; n++)
                {
                    int score = CalcColorScore(rank[i], palette[n]);
                    if (score < min_score)
                    {
                        min_score = score;
                    }
                }
                count += (uint)(rank[i].Count);
                total_score += (uint)(min_score * rank[i].Count);
            }
            if (count <= 0)
            {
                return 0;
            }
            return (int)(total_score / count);
        }

        //色がどれだけ似ていないかを求めます. 0は同一 数値が大きなればなるほど似ていません.
        private static int CalcColorScore(ColorRanking c, ColorRanking c2)
        {
            return CalcColorScore(c, c2.R, c2.G, c2.B);
        }

        //三平方の定理で、類似度を判定します.
        private static int CalcColorScore(ColorRanking c, int r2, int g2, int b2)
        {
            int r = c.R - r2;
            int g = c.G - g2;
            int b = c.B - b2;
            return (int)Math.Sqrt((r * r) + (g * g) + (b * b));
        }

        private static List<ColorRanking> AssingPalette(int palette, List<ColorRanking> totalRank, List<TileMapping> tileMapping)
        {
            List<ColorRanking> pal = new List<ColorRanking>();
            if (totalRank.Count <= 0)
            {//もう未割当の色はない.
                return pal;
            }

            //最も利用されている色を取り出す.
            ColorRanking topC = totalRank[0];
            totalRank.RemoveAt(0);

            //この色が使われているタイルの処理.
            for (int i = 0; i < tileMapping.Count; i++)
            {
                int found = FindColor(tileMapping[i].Rank, topC);
                if (found >= 0)
                {
                    //タイルの割り当て
                    tileMapping[i].Palette = palette;
                    //タイルの色をすべてパレットに追加. ついでに、色ランキングから消去.
                    InsertColorRank(pal, tileMapping[i].Rank, totalRank);
                }
            }
            return pal;
        }

        private static void InsertColorRank(List<ColorRanking> pal, List<ColorRanking> rank, List<ColorRanking> totalRank)
        {
            for (int i = 0; i < rank.Count; i++)
            {
                int found = FindColor(pal, rank[i]);
                if (found >= 0)
                {
                    pal[found].Count += rank[i].Count;
                }
                else
                {
                    ColorRanking c = rank[i].Clone();
                    pal.Add(c);
                }

                //割り当てた色は、色ランキングから消す.
                found = FindColor(totalRank, rank[i]);
                if (found >= 0)
                {
                    totalRank.RemoveAt(found);
                }
            }
        }

        private static void SortColor(List<ColorRanking> rank)
        {
            rank.Sort((a, b) => { return (b.Count) - (a.Count); });
        }

        private static int FindColor(List<ColorRanking> rank, ColorRanking c)
        {
            for (int i = 0; i < rank.Count; i++)
            {
                if (rank[i].R == c.R && rank[i].G == c.G && rank[i].B == c.B)
                {
                    return i;
                }
            }
            return -1;
        }

        private static void VoteColor(List<ColorRanking> rank, int r, int g, int b)
        {
            for (int i = 0; i < rank.Count; i++)
            {
                if (rank[i].R == r && rank[i].G == g && rank[i].B == b)
                {
                    rank[i].Count++;
                    return;
                }
            }
            ColorRanking rr = new ColorRanking(r, g, b);
            rank.Add(rr);
        }

        private static void VoteColor(List<ColorRanking> rank, ColorRanking c)
        {
            VoteColor(rank, c.R, c.G, c.B);
        }

        // WF VoteColor(rank, Color) — snaps to GBA 5-bit before voting.
        private static void VoteColorSnap(List<ColorRanking> rank, int r, int g, int b)
        {
            VoteColor(rank
                , (r >> 3) << 3
                , (g >> 3) << 3
                , (b >> 3) << 3
                );
        }

        // ---- pixel / palette byte helpers -------------------------------------

        /// <summary>Read a source RGBA pixel; returns false (transparent) for out-of-bounds (padded) coordinates.</summary>
        private static bool TryGetPixel(byte[] rgba, int width, int height, int x, int y, out int r, out int g, out int b, out int a)
        {
            r = g = b = a = 0;
            if (x < 0 || y < 0 || x >= width || y >= height)
            {
                return false;
            }
            int idx = (x + y * width) * 4;
            if (idx < 0 || idx + 3 >= rgba.Length)
            {
                return false;
            }
            r = rgba[idx + 0];
            g = rgba[idx + 1];
            b = rgba[idx + 2];
            a = rgba[idx + 3];
            return true;
        }

        /// <summary>Pack an 8-bit RGB triple to GBA RGB555 (each channel pre-snapped via &gt;&gt;3) at color slot index.</summary>
        private static void WriteGbaColor(byte[] gbaPalette, int colorIndex, int r, int g, int b)
        {
            ushort gba = (ushort)(((r >> 3) & 0x1F) | (((g >> 3) & 0x1F) << 5) | (((b >> 3) & 0x1F) << 10));
            int o = colorIndex * 2;
            if (o + 1 >= gbaPalette.Length) return;
            gbaPalette[o + 0] = (byte)(gba & 0xFF);
            gbaPalette[o + 1] = (byte)((gba >> 8) & 0xFF);
        }

        // ---- scale / resize helpers (RGBA arrays) -----------------------------

        /// <summary>
        /// Aspect-preserving bilinear scale of an RGBA image, then top-left crop/pad
        /// to <paramref name="width"/>×<paramref name="height"/>. Mirrors WF
        /// <c>ImageUtil.BitmapScale</c> (fit one dimension) + <c>BitmapSizeChange</c>.
        /// Bilinear interpolation may differ slightly from GDI+'s exact resampling —
        /// an acceptable, documented fidelity gap for a cross-platform port.
        /// </summary>
        internal static byte[] ScaleRgba(byte[] srcRgba, int srcW, int srcH, int width, int height)
        {
            if (srcRgba == null) throw new ArgumentNullException(nameof(srcRgba));
            if (srcW <= 0 || srcH <= 0 || width <= 0 || height <= 0)
                throw new ArgumentOutOfRangeException(nameof(width));

            int newWidth, newHeight;
            if ((double)srcW / srcH < (double)width / height)
            {
                newWidth = width;
                newHeight = width * srcH / srcW;
            }
            else
            {
                newHeight = height;
                newWidth = height * srcW / srcH;
            }
            if (newWidth < 1) newWidth = 1;
            if (newHeight < 1) newHeight = 1;

            // Bilinear scale src → newWidth×newHeight.
            byte[] scaled = BilinearResample(srcRgba, srcW, srcH, newWidth, newHeight);
            // Top-left crop/pad to width×height.
            return ResizeRgba(scaled, newWidth, newHeight, width, height);
        }

        /// <summary>
        /// Top-left crop/pad of an RGBA image to <paramref name="width"/>×<paramref name="height"/>,
        /// filling any new area with transparent (0,0,0,0). Mirrors WF
        /// <c>ImageUtil.BitmapSizeChange(src, 0, 0, w, h)</c>.
        /// </summary>
        internal static byte[] ResizeRgba(byte[] srcRgba, int srcW, int srcH, int width, int height)
        {
            if (srcRgba == null) throw new ArgumentNullException(nameof(srcRgba));
            if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width));

            byte[] dst = new byte[width * height * 4]; // zero = transparent
            int copyW = Math.Min(width, srcW);
            int copyH = Math.Min(height, srcH);
            for (int y = 0; y < copyH; y++)
            {
                int srcRow = y * srcW * 4;
                int dstRow = y * width * 4;
                Array.Copy(srcRgba, srcRow, dst, dstRow, copyW * 4);
            }
            return dst;
        }

        private static byte[] BilinearResample(byte[] src, int srcW, int srcH, int dstW, int dstH)
        {
            byte[] dst = new byte[dstW * dstH * 4];
            // Map dst pixel centers back into src space.
            double sx = (double)srcW / dstW;
            double sy = (double)srcH / dstH;
            for (int y = 0; y < dstH; y++)
            {
                double fy = (y + 0.5) * sy - 0.5;
                int y0 = (int)Math.Floor(fy);
                double wy = fy - y0;
                int y1 = y0 + 1;
                if (y0 < 0) { y0 = 0; }
                if (y1 < 0) { y1 = 0; }
                if (y0 > srcH - 1) y0 = srcH - 1;
                if (y1 > srcH - 1) y1 = srcH - 1;
                for (int x = 0; x < dstW; x++)
                {
                    double fx = (x + 0.5) * sx - 0.5;
                    int x0 = (int)Math.Floor(fx);
                    double wx = fx - x0;
                    int x1 = x0 + 1;
                    if (x0 < 0) { x0 = 0; }
                    if (x1 < 0) { x1 = 0; }
                    if (x0 > srcW - 1) x0 = srcW - 1;
                    if (x1 > srcW - 1) x1 = srcW - 1;

                    int p00 = (x0 + y0 * srcW) * 4;
                    int p10 = (x1 + y0 * srcW) * 4;
                    int p01 = (x0 + y1 * srcW) * 4;
                    int p11 = (x1 + y1 * srcW) * 4;
                    int o = (x + y * dstW) * 4;
                    for (int c = 0; c < 4; c++)
                    {
                        double top = src[p00 + c] * (1 - wx) + src[p10 + c] * wx;
                        double bot = src[p01 + c] * (1 - wx) + src[p11 + c] * wx;
                        double val = top * (1 - wy) + bot * wy;
                        int iv = (int)Math.Round(val);
                        if (iv < 0) iv = 0; else if (iv > 255) iv = 255;
                        dst[o + c] = (byte)iv;
                    }
                }
            }
            return dst;
        }

        // ---- method preset table ----------------------------------------------

        /// <summary>
        /// The "Method" combo preset for the Color Reduction Tool, ported from WF
        /// <c>DecreaseColorTSAToolForm.Method_SelectedIndexChanged</c>. Methods 1..0xA
        /// are supported; methods 3 &amp; 4 vary by ROM version (6/7/8). Method 0 and any
        /// unhandled index return the WF ctor's initial state (Method=1, scalable).
        /// </summary>
        /// <param name="method">Method combo index (0..0xA).</param>
        /// <param name="romVersion">ROM version (6, 7, or 8) — only used by methods 3 &amp; 4.</param>
        public static DecreaseColorPreset GetMethodPreset(int method, int romVersion)
        {
            switch (method)
            {
                case 1: // 背景とCG (BG / CG)
                    return new DecreaseColorPreset { Width = 30 * 8, Height = 20 * 8, Yohaku = 2 * 8, PaletteNo = 8, Reserve1st = true, Scalable = true, IgnoreTSA = false };
                case 2: // 戦闘背景 (battle BG)
                    return new DecreaseColorPreset { Width = 30 * 8, Height = 20 * 8, Yohaku = 0, PaletteNo = 8, Reserve1st = true, Scalable = true, IgnoreTSA = false };
                case 3: // ワールドマップ(でかい) (world map, large)
                    if (romVersion == 8)
                        return new DecreaseColorPreset { Width = 480, Height = 320, Yohaku = 0, PaletteNo = 4, Reserve1st = true, Scalable = true, IgnoreTSA = false };
                    if (romVersion == 7)
                        return new DecreaseColorPreset { Width = 1024, Height = 688, Yohaku = 0, PaletteNo = 4, Reserve1st = true, Scalable = true, IgnoreTSA = false };
                    // version 6 (256-color, reserve off)
                    return new DecreaseColorPreset { Width = 240, Height = 160, Yohaku = 0, PaletteNo = 16, Reserve1st = false, Scalable = true, IgnoreTSA = false };
                case 4: // ワールドマップ(イベント用) (world map, event)
                    if (romVersion == 8)
                        return new DecreaseColorPreset { Width = 30 * 8, Height = 20 * 8, Yohaku = 2 * 8, PaletteNo = 4, Reserve1st = true, Scalable = true, IgnoreTSA = false };
                    if (romVersion == 7)
                        return new DecreaseColorPreset { Width = 30 * 8, Height = 20 * 8, Yohaku = 2 * 8, PaletteNo = 4, Reserve1st = true, Scalable = true, IgnoreTSA = false };
                    // version 6 (256-color, reserve off)
                    return new DecreaseColorPreset { Width = 240, Height = 160, Yohaku = 0, PaletteNo = 16, Reserve1st = false, Scalable = true, IgnoreTSA = false };
                case 5: // TSAを利用しない256色 (256-color, ignore TSA)
                    return new DecreaseColorPreset { Width = 30 * 8, Height = 20 * 8, Yohaku = 0, PaletteNo = 16, Reserve1st = true, Scalable = true, IgnoreTSA = true };
                case 6: // ステータス画面背景(FE8) (status screen BG, FE8)
                    return new DecreaseColorPreset { Width = 30 * 8, Height = 20 * 8, Yohaku = 0, PaletteNo = 4, Reserve1st = true, Scalable = true, IgnoreTSA = false };
                case 7: // 一枚絵マップチップ (single-image map chips)
                    return new DecreaseColorPreset { Width = 512, Height = 512, Yohaku = 0, PaletteNo = 5, Reserve1st = true, Scalable = false, IgnoreTSA = false };
                case 8: // 一枚絵マップチップ 10色 (single-image map chips, 10-color)
                    return new DecreaseColorPreset { Width = 512, Height = 512, Yohaku = 0, PaletteNo = 10, Reserve1st = true, Scalable = false, IgnoreTSA = false };
                case 9: // TSAを利用しないBG256色(カットシーン) (cutscene BG, ignore TSA, 256-color)
                    return new DecreaseColorPreset { Width = 30 * 8, Height = 20 * 8, Yohaku = 2 * 8, PaletteNo = 16, Reserve1st = true, Scalable = true, IgnoreTSA = true };
                case 0xA: // TSAを利用しないBG224色(会話用) (talk BG, ignore TSA, 224-color)
                    return new DecreaseColorPreset { Width = 30 * 8, Height = 20 * 8, Yohaku = 2 * 8, PaletteNo = 14, Reserve1st = true, Scalable = true, IgnoreTSA = true };
                default: // method 0 / unhandled → WF ctor initial state (Method=1, ConvertSizeMethod=1)
                    return new DecreaseColorPreset { Width = 30 * 8, Height = 20 * 8, Yohaku = 2 * 8, PaletteNo = 8, Reserve1st = true, Scalable = true, IgnoreTSA = false };
            }
        }

        // ---- file entry -------------------------------------------------------

        /// <summary>
        /// File→file color reduction, mirroring WF static
        /// <c>DecreaseColorTSAToolForm.DecreaseColor</c>. Loads the input image,
        /// optionally scales (bilinear, aspect-preserving) or resizes (top-left
        /// crop/pad) to <paramref name="width"/>×<paramref name="height"/> when both
        /// are positive, runs <see cref="Convert"/>, and saves the result.
        ///
        /// NOTE on the saved file format: the output is written as an <b>RGBA</b>
        /// PNG (SkiaSharp's <c>SkiaImage.Save</c> encodes the RGBA bitmap, not an
        /// 8bpp-indexed image). This stays functionally re-importable because every
        /// 8×8 tile holds ≤16 GBA-5bit-snapped colors and ≤<paramref name="paletteNo"/>
        /// banks total, and the importer (<c>ImageUtil.ImageToPalette</c>) re-derives
        /// the banks from pixel colors. The faithful <i>banked</i> structure lives in
        /// the in-memory <see cref="Convert"/> result (<c>IndexData</c>/<c>GbaPalette</c>).
        /// The saved RGBA is fully <b>opaque</b>, exactly as WF's <c>Format8bppIndexed</c>
        /// save (its <c>ColorPalette.Entries</c> have no alpha): each pixel renders as its
        /// palette color, with bank slot 0 = (0,0,0) black when <c>reserve1st=true</c> and a
        /// real image color when <c>reserve1st=false</c> (so the FE6 no-reserve world-map
        /// presets round-trip correctly).
        /// </summary>
        /// <returns>0 = ok; -2 = bad arguments / missing input; -1 = image/IO/save error.</returns>
        public static int ReduceColorFile(string inPath, string outPath, int width, int height, int yohaku, int paletteNo, bool isScalable, bool reserve1st, bool ignoreTSA)
        {
            if (string.IsNullOrEmpty(inPath) || !File.Exists(inPath))
            {
                return -2;
            }
            if (string.IsNullOrEmpty(outPath))
            {
                return -2;
            }
            if (CoreState.ImageService == null)
            {
                return -1;
            }
            if (paletteNo <= 0) paletteNo = 1;

            bool wroteOutput = false;
            try
            {
                IImage src = CoreState.ImageService.LoadImage(inPath);
                if (src == null)
                {
                    return -1;
                }

                byte[] rgba;
                int rw, rh;
                using (src)
                {
                    byte[] srcRgba = src.GetPixelData();
                    int srcW = src.Width;
                    int srcH = src.Height;

                    if (width > 0 && height > 0)
                    {
                        rgba = isScalable
                            ? ScaleRgba(srcRgba, srcW, srcH, width, height)
                            : ResizeRgba(srcRgba, srcW, srcH, width, height);
                        rw = width;
                        rh = height;
                    }
                    else
                    {
                        rgba = srcRgba;
                        rw = srcW;
                        rh = srcH;
                    }
                }

                DecreaseColorConvertResult result = Convert(rgba, rw, rh, paletteNo, yohaku, reserve1st, ignoreTSA);

                // Expand the banked indexed result to RGBA for saving. WF saves a
                // Format8bppIndexed bitmap whose ColorPalette.Entries are all opaque
                // Color.FromArgb(r,g,b) — so the saved image is fully OPAQUE, with index 0
                // of each bank rendering as its palette color (black (0,0,0) when reserve1st
                // is true, a real image color when false). We mirror that exactly: every
                // pixel becomes its opaque palette color. This is faithful for both reserve
                // modes (incl. the FE6 no-reserve world-map presets) and re-importable, since
                // ImageUtil.ImageToPalette re-derives banks from the ≤16-colors-per-tile pixels.
                byte[] outRgba = ExpandToRgba(result);

                using IImage outImg = CoreState.ImageService.CreateImage(result.Width, result.Height);
                outImg.SetPixelData(outRgba);
                outImg.Save(outPath);
                wroteOutput = true;
            }
            catch (Exception ex) when (ex is IOException
                                       || ex is UnauthorizedAccessException
                                       || ex is ArgumentException
                                       || ex is NotSupportedException
                                       || ex is OutOfMemoryException)
            {
                Log.Error("DecreaseColorConvertCore.ReduceColorFile: " + ex.Message);
                if (!wroteOutput)
                {
                    TryDeletePartialOutput(outPath);
                }
                return -1;
            }

            return 0;
        }

        /// <summary>
        /// Expand a banked indexed result to a fully-opaque RGBA buffer, exactly as the
        /// WF <c>Format8bppIndexed</c> save would render it: every pixel maps to its GBA
        /// palette color with alpha 255 (WF's <c>ColorPalette.Entries</c> are opaque
        /// <c>Color.FromArgb(r,g,b)</c>). No special index-0 alpha handling — when
        /// <c>reserve1st=true</c>, slot 0 is (0,0,0) black; when false, slot 0 is a real
        /// image color; both render opaque, matching WF for every reserve mode.
        /// </summary>
        private static byte[] ExpandToRgba(DecreaseColorConvertResult result)
        {
            int n = result.Width * result.Height;
            byte[] outRgba = new byte[n * 4];
            byte[] pal = result.GbaPalette;
            int colorCount = pal.Length / 2;
            for (int i = 0; i < n; i++)
            {
                int idx = result.IndexData[i];
                if (idx >= 0 && idx < colorCount)
                {
                    ushort gba = (ushort)(pal[idx * 2] | (pal[idx * 2 + 1] << 8));
                    outRgba[i * 4 + 0] = (byte)((gba & 0x1F) << 3);
                    outRgba[i * 4 + 1] = (byte)(((gba >> 5) & 0x1F) << 3);
                    outRgba[i * 4 + 2] = (byte)(((gba >> 10) & 0x1F) << 3);
                }
                // colors are opaque (WF indexed-palette entries have no alpha).
                outRgba[i * 4 + 3] = 255;
            }
            return outRgba;
        }

        private static void TryDeletePartialOutput(string outPath)
        {
            try
            {
                if (!string.IsNullOrEmpty(outPath) && File.Exists(outPath))
                {
                    File.Delete(outPath);
                }
            }
            catch
            {
                // best-effort cleanup; ignore.
            }
        }
    }
}
