using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Core;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class ImageUnitWaitIconViewModel : ViewModelBase, IDataVerifiable
    {
        const uint SIZE = 8;

        uint _currentAddr;
        int _currentIndex;
        bool _isLoaded;
        ushort _w0, _w2;
        uint _p4;
        int _paletteType;
        int _step;
        string _comment = string.Empty;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public int CurrentIndex { get => _currentIndex; set => SetField(ref _currentIndex, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public ushort W0 { get => _w0; set => SetField(ref _w0, value); }
        public ushort W2 { get => _w2; set => SetField(ref _w2, value); }
        public uint P4 { get => _p4; set => SetField(ref _p4, value); }

        /// <summary>0=self,1=npc,2=enemy,3=gray,4=four (the 5 selectable types).</summary>
        public int PaletteType { get => _paletteType; set => SetField(ref _paletteType, value); }
        /// <summary>Frame step 0..2 for the single-frame preview.</summary>
        public int Step { get => _step; set => SetField(ref _step, value); }
        public string Comment { get => _comment; set => SetField(ref _comment, value); }

        /// <summary>The 8-byte table entry address of the loaded wait icon.</summary>
        public uint EntryAddress => _currentAddr;

        public List<AddrResult> LoadList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            uint ptr = rom.RomInfo.unit_wait_icon_pointer;
            if (ptr == 0) return new List<AddrResult>();

            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            var result = new List<AddrResult>();
            for (uint i = 0; i < 0x100; i++)
            {
                uint addr = (uint)(baseAddr + i * SIZE);
                if (addr + SIZE > (uint)rom.Data.Length) break;

                uint imgPtr = rom.u32(addr + 4);
                if (!U.isPointer(imgPtr)) break;

                // #991: append the owning class name (lockstep with
                // ListParityHelper.BuildImageUnitWaitIconList — golden test
                // gated). U.SA prefixes a single space iff the name is non-empty.
                string className = ClassFormCore.GetClassNameWhereWaitIconId(rom, i);
                string name = U.ToHexString(i) + U.SA(className) + " WaitIcon";
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        public void LoadEntry(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + SIZE > (uint)rom.Data.Length) return;

            CurrentAddr = addr;
            W0 = (ushort)rom.u16(addr + 0);
            W2 = (ushort)rom.u16(addr + 2);
            P4 = rom.u32(addr + 4);

            // Row index relative to the wait-icon table base (= waitIconId).
            uint ptr = rom.RomInfo.unit_wait_icon_pointer;
            uint baseAddr = ptr != 0 ? rom.p32(ptr) : 0;
            CurrentIndex = (baseAddr != 0 && addr >= baseAddr)
                ? (int)((addr - baseAddr) / SIZE)
                : 0;

            LoadComment();
            IsLoaded = true;
        }

        /// <summary>Re-read the entry fields + comment from ROM (post-import refresh).</summary>
        public void ReloadEntry()
        {
            if (CurrentAddr != 0) LoadEntry(CurrentAddr);
        }

        // ----------------------------------------------------------------
        // Comment (mirror ImageBattleBGViewModel)
        // ----------------------------------------------------------------
        public void LoadComment()
        {
            var cache = CoreState.CommentCache;
            Comment = (cache != null) ? (cache.S_At(CurrentAddr) ?? string.Empty) : string.Empty;
        }

        public void SaveComment(string text)
        {
            Comment = text ?? string.Empty;
            var cache = CoreState.CommentCache;
            if (cache == null || CurrentAddr == 0) return;
            cache.Update(CurrentAddr, Comment);
        }

        // ----------------------------------------------------------------
        // Write fields back (W0/W2 as u16, P4 as a RAW GBA pointer)
        // ----------------------------------------------------------------
        public void Write()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;

            uint addr = CurrentAddr;
            rom.write_u16(addr + 0, W0);
            rom.write_u16(addr + 2, W2);
            // P4 is a RAW GBA pointer field — write the value verbatim
            // (write_u32, NOT write_p32 which would re-apply the 0x08000000
            // offset).
            rom.write_u32(addr + 4, P4);
            SaveComment(Comment);
        }

        // ----------------------------------------------------------------
        // Previews
        // ----------------------------------------------------------------
        /// <summary>Render the full decoded sprite sheet (no crop) for X_PIC.</summary>
        public IImage RenderFullSheet()
        {
            ROM rom = CoreState.ROM;
            var svc = CoreState.ImageService;
            if (rom == null || svc == null) return null;
            return WaitIconRenderCore.RenderFullSheet(rom, (uint)CurrentIndex, svc, PaletteType);
        }

        /// <summary>Render the single <see cref="Step"/> frame for X_ONE_PIC.</summary>
        public IImage RenderFrame()
        {
            ROM rom = CoreState.ROM;
            var svc = CoreState.ImageService;
            if (rom == null || svc == null) return null;
            return WaitIconRenderCore.RenderFrame(rom, (uint)CurrentIndex, Step, svc, PaletteType);
        }

        // ----------------------------------------------------------------
        // Export (#991 WU2)
        // ----------------------------------------------------------------
        /// <summary>Export the full decoded sheet as an indexed PNG.</summary>
        public bool ExportPng(string path)
        {
            using IImage img = RenderFullSheet();
            if (img == null) return false;
            img.Save(path);
            return true;
        }

        /// <summary>
        /// Export the 3 animation frames (step 0..2) as an animated GIF.
        /// Mirrors WF <c>ImageUnitWaitIconFrom.SaveAnimeGif</c> (wait=10 game
        /// frames per frame). Mirrors the SkillConfig export's GifEncoder usage.
        /// </summary>
        public bool ExportGif(string path)
        {
            ROM rom = CoreState.ROM;
            var svc = CoreState.ImageService;
            if (rom == null || svc == null) return false;

            var gifFrames = new List<GifEncoderCore.GifFrame>();
            var rendered = new List<IImage>();
            try
            {
                for (int step = 0; step < 3; step++)
                {
                    IImage img = WaitIconRenderCore.RenderFrame(rom, (uint)CurrentIndex, step, svc, PaletteType);
                    if (img == null) return false;
                    rendered.Add(img);

                    byte[] rgba;
                    if (img.IsIndexed)
                    {
                        byte[] indexed = img.GetPixelData();
                        byte[] palette = img.GetPaletteRGBA();
                        rgba = GifEncoderCore.IndexedToRgba(indexed, palette, img.Width, img.Height);
                    }
                    else
                    {
                        rgba = img.GetPixelData();
                    }

                    gifFrames.Add(new GifEncoderCore.GifFrame
                    {
                        Width = img.Width,
                        Height = img.Height,
                        RgbaPixels = rgba,
                        DelayCs = U.GameFrameSecToGifFrameSec(10),
                    });
                }

                GifEncoderCore.Encode(gifFrames, path);
                return true;
            }
            finally
            {
                // Each RenderFrame produces a fresh image — dispose all 3.
                foreach (var img in rendered) img.Dispose();
            }
        }

        // ----------------------------------------------------------------
        // Jump-to-Move-Icon (#991 WU4)
        // ----------------------------------------------------------------
        /// <summary>
        /// Resolve the 1-BASED move-icon id for the current wait icon:
        /// waitIconId → owning class id → that class's move-icon id (`u8 @
        /// class+4`). Returns null when the wait icon has no owning class, the
        /// lookups fail, OR the move-icon id is 0 ("no move icon" — the WF
        /// sentinel). Returning null for 0 (rather than the raw 0) means a
        /// caller can safely subtract 1 to get the 0-based table index without
        /// underflowing (Copilot review on PR #993).
        /// </summary>
        public uint? ResolveMoveIconForSelection()
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return null;
            uint cid = ClassFormCore.GetClassIdWhereWaitIconId(rom, (uint)CurrentIndex);
            if (cid == U.NOT_FOUND) return null;
            uint moveIcon = ClassFormCore.GetClassMoveIcon(rom, cid);
            if (moveIcon == U.NOT_FOUND) return null;
            if (moveIcon == 0) return null; // 0 = "no move icon" (WF sentinel)
            return moveIcon;
        }

        /// <summary>
        /// Resolve the ROM ADDRESS of the Move Icon list entry the Jump button
        /// should navigate to, or null when there is no owning class / move icon.
        /// The class move-icon field (u8 @ class+4) is a 1-BASED id (WF
        /// PreviewIconHelper.LoadMoveIcon uses `id - 1`; ImageUnitMoveIconViewModel
        /// .LoadList is 0-based by table index), so the target entry is at
        /// `baseAddr + (id - 1) * 8`. id 0 ("no move icon") → null. Guards every
        /// pointer/address — never throws.
        /// </summary>
        public uint? ResolveMoveIconEntryAddress()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return null;
            uint? moveIcon = ResolveMoveIconForSelection();
            if (moveIcon == null || moveIcon.Value == 0) return null;

            uint ptr = rom.RomInfo.unit_move_icon_pointer;
            if (ptr == 0) return null;
            uint baseAddr = rom.p32(ptr);
            // rom-consistent guard (#993 Copilot review): validate against the
            // local `rom` instance, not the ambient CoreState.ROM.
            if (!U.isSafetyOffset(baseAddr, rom)) return null;

            uint entryAddr = baseAddr + (moveIcon.Value - 1) * 8;
            if (entryAddr + 8 > (uint)rom.Data.Length) return null;
            return entryAddr;
        }

        public int GetListCount() => LoadList().Count;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["W0"] = $"0x{W0:X04}",
                ["W2"] = $"0x{W2:X04}",
                ["P4"] = $"0x{P4:X08}",
            };
        }

        public Dictionary<string, string> GetRawRomReport()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return new Dictionary<string, string>();

            uint a = CurrentAddr;
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{a:X08}",
                ["u16@0x00"] = $"0x{rom.u16(a + 0):X04}",
                ["u16@0x02"] = $"0x{rom.u16(a + 2):X04}",
                ["u32@0x04"] = $"0x{rom.u32(a + 4):X08}",
            };
        }

        public Dictionary<string, string> GetFieldOffsetMap() => new()
        {
            ["W0"] = "u16@0x00",
            ["W2"] = "u16@0x02",
            ["P4"] = "u32@0x04",
        };
    }
}
