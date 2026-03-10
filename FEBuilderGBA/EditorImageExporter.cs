using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

namespace FEBuilderGBA
{
    /// <summary>
    /// Exports decoded graphics editor images as PNG files for cross-platform comparison.
    /// Unlike --screenshot-all which captures full form screenshots, this exports only
    /// the decoded image data from each graphics editor's static drawing methods.
    /// </summary>
    static class EditorImageExporter
    {
        /// <summary>
        /// Export all graphics editor images to the given directory.
        /// Returns exit code (0 = success, 1 = failures).
        /// </summary>
        public static int Export(string outputDir)
        {
            Directory.CreateDirectory(outputDir);
            string romVersion = Program.ROM?.RomInfo?.VersionToFilename ?? "Unknown";
            Console.WriteLine($"EXPORT_IMAGES: Exporting graphics editor images for {romVersion}...");

            int exported = 0;
            int failed = 0;
            var failures = new List<string>();

            var exporters = GetExporters();
            foreach (var entry in exporters)
            {
                string name = entry.Name;
                Func<uint, Bitmap> getImage = entry.GetImage;
                try
                {
                    for (int i = 0; i < 3; i++)
                    {
                        Bitmap bmp = null;
                        try
                        {
                            bmp = getImage((uint)(i + 1));
                        }
                        catch { }

                        if (bmp == null || IsBlankDummy(bmp))
                        {
                            if (i == 0)
                                Console.WriteLine($"EXPORT_IMAGES: {name}_{i:D4} ... SKIP (null/blank image)");
                            bmp?.Dispose();
                            if (i == 0) break;
                            continue;
                        }

                        string fileName = $"WinForms_{name}_{i:D4}_{romVersion}.png";
                        string filePath = Path.Combine(outputDir, fileName);
                        bmp.Save(filePath, ImageFormat.Png);
                        bmp.Dispose();
                        exported++;
                        Console.WriteLine($"EXPORT_IMAGES: {name}_{i:D4} ... OK ({filePath})");
                    }
                }
                catch (Exception ex)
                {
                    failed++;
                    failures.Add(name);
                    Console.WriteLine($"EXPORT_IMAGES: {name} ... FAIL: {ex.Message}");
                }
            }

            Console.WriteLine($"EXPORT_IMAGES: Results: {exported} exported, {failed} failed");
            if (failures.Count > 0)
                Console.WriteLine($"EXPORT_IMAGES: Failures: {string.Join(", ", failures)}");

            return failed > 0 ? 1 : 0;
        }

        static bool IsBlankDummy(Bitmap bmp)
        {
            return bmp.Width <= 1 && bmp.Height <= 1;
        }

        struct ExporterEntry
        {
            public string Name;
            public Func<uint, Bitmap> GetImage;
        }

        static List<ExporterEntry> GetExporters()
        {
            return new List<ExporterEntry>
            {
                new ExporterEntry { Name = "PortraitViewer", GetImage = (uint id) => ImagePortraitForm.DrawPortraitMap(id) },
                new ExporterEntry { Name = "BattleBGViewer", GetImage = (uint id) => ImageBattleBGForm.DrawBG(id) },
                new ExporterEntry { Name = "BattleTerrainViewer", GetImage = (uint id) => ImageBattleTerrainForm.Draw(id) },
                new ExporterEntry { Name = "BigCGViewer", GetImage = (uint id) => DrawBigCG(id) },
                new ExporterEntry { Name = "ChapterTitleViewer", GetImage = (uint id) => ImageChapterTitleForm.DrawSample(id) },
                new ExporterEntry { Name = "ChapterTitleFE7Viewer", GetImage = (uint id) => ExportChapterTitleFE7(id) },
                new ExporterEntry { Name = "ItemIconViewer", GetImage = (uint id) => ImageItemIconForm.DrawIconWhereID(id) },
                new ExporterEntry { Name = "SystemIconViewer", GetImage = (uint id) => ExportSystemIcon(id) },
                new ExporterEntry { Name = "OPClassFontViewer", GetImage = (uint id) => DrawOPClassFont(id) },
                new ExporterEntry { Name = "OPPrologueViewer", GetImage = (uint id) => OPPrologueForm.DrawImageByID(id) },
                new ExporterEntry { Name = "ImagePortraitFE6", GetImage = (uint id) => ImagePortraitFE6Form.DrawPortraitAuto(id) },
                new ExporterEntry { Name = "ImageBG", GetImage = (uint id) => ImageBGForm.DrawBG(id) },
                new ExporterEntry { Name = "ImageCG", GetImage = (uint id) => ImageCGForm.DrawImageByID(id) },
                new ExporterEntry { Name = "ImageCGFE7U", GetImage = (uint id) => ExportCGFE7U(id) },
                new ExporterEntry { Name = "ImageTSAAnime", GetImage = (uint id) => ExportTSAAnime(id) },
                new ExporterEntry { Name = "ImageBattleBG", GetImage = (uint id) => ImageBattleBGForm.DrawBG(id) },
            };
        }

        /// <summary>
        /// Draw Big CG by ID. Inlined because BigCGForm.cs is excluded from WinForms build.
        /// </summary>
        static Bitmap DrawBigCG(uint id)
        {
            uint ptr = Program.ROM.RomInfo.bigcg_pointer;
            if (ptr == 0) return ImageUtil.BlankDummy();

            uint baseAddr = Program.ROM.p32(ptr);
            if (!U.isSafetyOffset(baseAddr)) return ImageUtil.BlankDummy();

            // Each entry is 12 bytes: table pointer, TSA pointer, palette pointer
            uint addr = (uint)(baseAddr + (id - 1) * 12);
            if (addr + 12 > (uint)Program.ROM.Data.Length) return ImageUtil.BlankDummy();

            uint table = Program.ROM.u32(addr);
            uint tsa = Program.ROM.u32(addr + 4);
            uint palette = Program.ROM.u32(addr + 8);

            if (!U.isPointer(table) || !U.isPointer(tsa) || !U.isPointer(palette))
                return ImageUtil.BlankDummy();

            uint tableOff = U.toOffset(table);
            var imageUZList = new List<byte>();
            for (int i = 0; i < 10; i++)
            {
                uint image = Program.ROM.u32((uint)(tableOff + (i * 4)));
                if (!U.isPointer(image)) return ImageUtil.BlankDummy();
                byte[] imageUZ = LZ77.decompress(Program.ROM.Data, U.toOffset(image));
                imageUZList.AddRange(imageUZ);
            }

            return ImageUtil.ByteToImage16TileHeaderTSA(32 * 8, 20 * 8
                , imageUZList.ToArray(), 0
                , Program.ROM.Data, (int)U.toOffset(palette)
                , Program.ROM.Data, (int)U.toOffset(tsa)
                );
        }

        /// <summary>
        /// Draw OP Class Font by ID. Inlined because ClassOPFontForm.cs is excluded from WinForms build.
        /// </summary>
        static Bitmap DrawOPClassFont(uint id)
        {
            uint ptr = Program.ROM.RomInfo.op_class_font_pointer;
            if (ptr == 0) return ImageUtil.BlankDummy();

            uint baseAddr = Program.ROM.p32(ptr);
            if (!U.isSafetyOffset(baseAddr)) return ImageUtil.BlankDummy();

            // Each entry is 4 bytes: image pointer
            uint addr = (uint)(baseAddr + (id - 1) * 4);
            if (addr + 4 > (uint)Program.ROM.Data.Length) return ImageUtil.BlankDummy();

            uint image = Program.ROM.u32(addr);
            if (!U.isPointer(image)) return ImageUtil.BlankDummy();

            uint palette = Program.ROM.p32(Program.ROM.RomInfo.op_class_font_palette_pointer);
            byte[] imageUZ = LZ77.decompress(Program.ROM.Data, U.toOffset(image));

            return ImageUtil.ByteToImage16Tile(4 * 8, 4 * 8
                , imageUZ, 0
                , Program.ROM.Data, (int)U.toOffset(palette)
                );
        }

        /// <summary>
        /// Export Chapter Title FE7 image by ID.
        /// </summary>
        static Bitmap ExportChapterTitleFE7(uint id)
        {
            if (Program.ROM.RomInfo.version != 7)
                return ImageUtil.BlankDummy();

            uint ptr = Program.ROM.RomInfo.image_chapter_title_pointer;
            if (ptr == 0) return ImageUtil.BlankDummy();

            uint baseAddr = Program.ROM.p32(ptr);
            if (!U.isSafetyOffset(baseAddr)) return ImageUtil.BlankDummy();

            // FE7 chapter title: each entry is 4 bytes (one pointer)
            uint addr = (uint)(baseAddr + (id - 1) * 4);
            if (addr + 4 > (uint)Program.ROM.Data.Length) return ImageUtil.BlankDummy();

            uint imagePtr = Program.ROM.u32(addr);
            if (!U.isPointer(imagePtr)) return ImageUtil.BlankDummy();

            return ImageChapterTitleFE7Form.DrawPic(U.toOffset(imagePtr), 30 * 8);
        }

        /// <summary>
        /// Export system icon sheet. Only ID=1 is meaningful (it's a single sheet).
        /// </summary>
        static Bitmap ExportSystemIcon(uint id)
        {
            if (id != 1) return ImageUtil.BlankDummy();
            return ImageSystemIconForm.BaseImage();
        }

        /// <summary>
        /// Export FE7U CG image. Only available for FE7U ROMs.
        /// </summary>
        static Bitmap ExportCGFE7U(uint id)
        {
            if (Program.ROM.RomInfo.version != 7 || Program.ROM.RomInfo.is_multibyte)
                return ImageUtil.BlankDummy();
            return ImageCGFE7UForm.DrawImageByID(id);
        }

        /// <summary>
        /// Export TSA anime image by index. Loads TSA anime resource to find known pointers.
        /// </summary>
        static Bitmap ExportTSAAnime(uint id)
        {
            var tsaAnime = U.LoadTSVResource(U.ConfigDataFilename("tsaanime_"));
            if (tsaAnime == null || tsaAnime.Count == 0) return ImageUtil.BlankDummy();

            // Get the id-th category's first entry
            int index = 0;
            foreach (var pair in tsaAnime)
            {
                if (index == (int)(id - 1))
                {
                    uint pointer = pair.Key;
                    uint ptrOff = U.toOffset(pointer);
                    if (!U.isSafetyOffset(ptrOff)) return ImageUtil.BlankDummy();

                    uint baseAddr = Program.ROM.p32(ptrOff);
                    if (!U.isSafetyOffset(baseAddr)) return ImageUtil.BlankDummy();

                    uint image = Program.ROM.u32(baseAddr);
                    uint palette = Program.ROM.u32(baseAddr + 4);
                    uint tsa = Program.ROM.u32(baseAddr + 8);
                    return ImageTSAAnimeForm.DrawTSAAnime(image, palette, tsa);
                }
                index++;
            }
            return ImageUtil.BlankDummy();
        }
    }
}
