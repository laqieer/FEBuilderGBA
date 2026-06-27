using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    // WF ImagePortraitForm portrait entry layout (FE7/FE8): 28 bytes per entry
    //   +0  u32  Unit Face image pointer
    //   +4  u32  Mini portrait / map face pointer
    //   +8  u32  Palette pointer
    //   +12 u32  Mouth animation frames pointer
    //   +16 u32  Class Card image pointer (when mug_exceed patch is INACTIVE)
    //            OR  4 bytes B16/B17/B18/B19 = mug_exceed tile coords (Tile1 X/Y, Tile2 X/Y)
    //   +20 u8   Mouth coordinate X
    //   +21 u8   Mouth coordinate Y
    //   +22 u8   Eye coordinate X
    //   +23 u8   Eye coordinate Y
    //   +24 u8   Status / display mode (0=close mouth, 1=normal, 6=close eyes)
    //   +25 u8   Unused
    //   +26 u8   Unused (mug_exceed enable flag in WF — B26)
    //   +27 u8   Unused
    //
    // Made `partial` so the cross-editor navigation manifest can live in a
    // sibling .NavigationTargets.cs file. (Plan v3 / #424.)
    public partial class ImagePortraitViewModel : ViewModelBase, IDataVerifiable
    {
        const uint SIZE = 28;

        /// <summary>
        /// Sentinel returned by <see cref="GetSelectedPortraitId"/> when no valid,
        /// aligned portrait entry is selected. Distinct from any real portrait id.
        /// </summary>
        public const uint NoPortraitSelection = 0xFFFFFFFF;

        /// <summary>
        /// Resolve the portrait id (0-based row index) of the currently selected
        /// entry from <see cref="CurrentAddr"/> and the portrait table base.
        /// Returns <see cref="NoPortraitSelection"/> when nothing is selected, the
        /// portrait pointer is missing/unsafe, the address is below the table base,
        /// or the address is NOT entry-aligned (a non-aligned address yields no
        /// selection rather than a floored wrong id). Never throws.
        /// </summary>
        public uint GetSelectedPortraitId()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null || CurrentAddr == 0) return NoPortraitSelection;

            uint pointer = rom.RomInfo.portrait_pointer;
            if (pointer == 0) return NoPortraitSelection;

            uint baseAddr = rom.p32(pointer);
            if (!U.isSafetyOffset(baseAddr) || CurrentAddr < baseAddr) return NoPortraitSelection;

            uint delta = CurrentAddr - baseAddr;
            if (delta % SIZE != 0) return NoPortraitSelection;

            return delta / SIZE;
        }

        uint _currentAddr;
        bool _isLoaded;
        uint _portraitImagePtr, _miniPortraitPtr, _palettePtr, _mouthFramesPtr, _classCardPtr;
        uint _mouthX, _mouthY, _eyeX, _eyeY;
        uint _status, _unused25, _unused26, _unused27;

        // Read-only display values surfaced for the new top-of-list config
        // bar + selection bar (mirrors WF ReadStartAddress / ReadCount /
        // Size: / 選択アドレス: labels). These are updated by LoadList().
        uint _readStartAddress;
        uint _readCount;

        // Comment text — WF mirrors the Resource Cache entry for the
        // currently selected portrait. Avalonia round-trips it through the
        // VM so the View can bind a TextBox.
        string _comment = string.Empty;

        int _showFrame;
        IImage _faceImage;
        IImage _miniPortraitImage;
        IImage _mouthStripImage;
        IImage _eyeStripImage;
        IImage _classCardImage;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }

        // D0: Portrait image data pointer (Unit Face)
        public uint PortraitImagePtr { get => _portraitImagePtr; set => SetField(ref _portraitImagePtr, value); }
        // D4: Mini portrait / map sprite face pointer
        public uint MiniPortraitPtr { get => _miniPortraitPtr; set => SetField(ref _miniPortraitPtr, value); }
        // D8: Palette pointer
        public uint PalettePtr { get => _palettePtr; set => SetField(ref _palettePtr, value); }
        // D12: Mouth animation frames pointer
        public uint MouthFramesPtr { get => _mouthFramesPtr; set => SetField(ref _mouthFramesPtr, value); }
        // D16: Class card image pointer
        public uint ClassCardPtr { get => _classCardPtr; set => SetField(ref _classCardPtr, value); }
        // B20: Mouth coordinate X
        public uint MouthX { get => _mouthX; set => SetField(ref _mouthX, value); }
        // B21: Mouth coordinate Y
        public uint MouthY { get => _mouthY; set => SetField(ref _mouthY, value); }
        // B22: Eye coordinate X
        public uint EyeX { get => _eyeX; set => SetField(ref _eyeX, value); }
        // B23: Eye coordinate Y
        public uint EyeY { get => _eyeY; set => SetField(ref _eyeY, value); }
        // B24: Portrait status / display mode (0=Close mouth, 1=Normal, 6=Close eyes)
        public uint Status { get => _status; set => SetField(ref _status, value); }
        // B25: Unused / reserved
        public uint Unused25 { get => _unused25; set => SetField(ref _unused25, value); }
        // B26: Unused / reserved (mug_exceed enable flag in WF)
        public uint Unused26 { get => _unused26; set => SetField(ref _unused26, value); }
        // B27: Unused / reserved
        public uint Unused27 { get => _unused27; set => SetField(ref _unused27, value); }

        /// <summary>Block size in bytes (matches WF `BlockSize` label = 28).</summary>
        public uint BlockSize => SIZE;

        /// <summary>
        /// Start address of the portrait table (matches WF `先頭アドレス`).
        /// Populated by LoadList() so the view's top-of-list bar can display it.
        /// </summary>
        public uint ReadStartAddress { get => _readStartAddress; set => SetField(ref _readStartAddress, value); }

        /// <summary>
        /// Number of valid entries (matches WF `読込数`).
        /// Populated by LoadList().
        /// </summary>
        public uint ReadCount { get => _readCount; set => SetField(ref _readCount, value); }

        /// <summary>
        /// Per-portrait comment text (matches WF `コメント`).
        /// Round-trips through the VM; the View binds a TextBox to this.
        /// </summary>
        public string Comment { get => _comment; set => SetField(ref _comment, value ?? string.Empty); }

        // MugExceed tile-coordinate slices of D16 (ClassCardPtr).
        //
        // When the mug_exceed patch is installed, WF reinterprets the 4 bytes
        // at addr+16..addr+19 as Tile1 X (B16), Tile1 Y (B17), Tile2 X (B18),
        // Tile2 Y (B19). These properties expose those slices for one-way
        // binding into NumericUpDown inputs. The View composes the new
        // ClassCardPtr u32 from the input values BEFORE calling Write — so
        // the slices stay derived (read-only). (Plan v3 #424 / Copilot CLI
        // plan-review point on MugExceed write path.)
        public uint MugExceedB16 => (_classCardPtr >> 0) & 0xFF;
        public uint MugExceedB17 => (_classCardPtr >> 8) & 0xFF;
        public uint MugExceedB18 => (_classCardPtr >> 16) & 0xFF;
        public uint MugExceedB19 => (_classCardPtr >> 24) & 0xFF;

        /// <summary>Show frame index (0=normal, 1=half-eye, 2=closed-eye, 3-8=mouth1-6, 9=mouth7).</summary>
        public int ShowFrame
        {
            get => _showFrame;
            set
            {
                if (SetField(ref _showFrame, Math.Clamp(value, 0, PortraitRendererCore.MaxShowFrame)))
                    RefreshFaceImage();
            }
        }

        /// <summary>Main assembled face image (96x80) with current frame overlay.</summary>
        public IImage FaceImage { get => _faceImage; private set => SetField(ref _faceImage, value); }

        /// <summary>Mini portrait / map face image (32x32).</summary>
        public IImage MiniPortraitImage { get => _miniPortraitImage; private set => SetField(ref _miniPortraitImage, value); }

        /// <summary>Mouth frame strip image (32x96, 6 frames of 32x16).</summary>
        public IImage MouthStripImage { get => _mouthStripImage; private set => SetField(ref _mouthStripImage, value); }

        /// <summary>Eye frame strip image (32x32, 2 frames of 32x16: half-closed, closed).</summary>
        public IImage EyeStripImage { get => _eyeStripImage; private set => SetField(ref _eyeStripImage, value); }

        /// <summary>Class card image.</summary>
        public IImage ClassCardImage { get => _classCardImage; private set => SetField(ref _classCardImage, value); }

        /// <summary>Refresh all rendered images from current ROM data.</summary>
        public void RefreshAllImages()
        {
            RefreshFaceImage();
            RefreshMiniPortrait();
            RefreshMouthStrip();
            RefreshEyeStrip();
            RefreshClassCard();
        }

        /// <summary>Refresh the main face image with current show frame.</summary>
        public void RefreshFaceImage()
        {
            try
            {
                FaceImage = PortraitRendererCore.DrawPortraitUnitWithFrame(
                    PortraitImagePtr, PalettePtr, MouthFramesPtr,
                    (byte)MouthX, (byte)MouthY,
                    (byte)EyeX, (byte)EyeY, (byte)Status, _showFrame);
            }
            catch (Exception ex)
            {
                Log.ErrorF("RefreshFaceImage failed: {0}", ex.Message);
                FaceImage = null;
            }
        }

        void RefreshMiniPortrait()
        {
            try
            {
                MiniPortraitImage = PortraitRendererCore.DrawPortraitMap(MiniPortraitPtr, PalettePtr);
            }
            catch (Exception ex)
            {
                Log.ErrorF("RefreshMiniPortrait failed: {0}", ex.Message);
                MiniPortraitImage = null;
            }
        }

        void RefreshMouthStrip()
        {
            try
            {
                MouthStripImage = PortraitRendererCore.DrawMouthFrameStrip(MouthFramesPtr, PalettePtr);
            }
            catch (Exception ex)
            {
                Log.ErrorF("RefreshMouthStrip failed: {0}", ex.Message);
                MouthStripImage = null;
            }
        }

        void RefreshEyeStrip()
        {
            try
            {
                EyeStripImage = PortraitRendererCore.DrawEyeFrameStrip(PortraitImagePtr, PalettePtr);
            }
            catch (Exception ex)
            {
                Log.ErrorF("RefreshEyeStrip failed: {0}", ex.Message);
                EyeStripImage = null;
            }
        }

        void RefreshClassCard()
        {
            try
            {
                ClassCardImage = PortraitRendererCore.DrawPortraitClass(ClassCardPtr, PalettePtr);
            }
            catch (Exception ex)
            {
                Log.ErrorF("RefreshClassCard failed: {0}", ex.Message);
                ClassCardImage = null;
            }
        }

        public List<AddrResult> LoadList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            uint pointer = rom.RomInfo.portrait_pointer;
            uint dataSize = rom.RomInfo.portrait_datasize;
            if (pointer == 0 || dataSize == 0)
            {
                ReadStartAddress = 0;
                ReadCount = 0;
                return new List<AddrResult>();
            }

            uint baseAddr = rom.p32(pointer);
            if (!U.isSafetyOffset(baseAddr, rom))
            {
                ReadStartAddress = 0;
                ReadCount = 0;
                return new List<AddrResult>();
            }

            ReadStartAddress = baseAddr;

            var result = new List<AddrResult>();
            int nullCount = 0;
            for (int i = 0; i < 512; i++)
            {
                uint addr = baseAddr + (uint)(i * dataSize);
                if (addr + dataSize > (uint)rom.Data.Length) break;
                // Stop after several consecutive null entries
                if (rom.u32(addr) == 0) { nullCount++; if (nullCount > 3) break; }
                else nullCount = 0;

                string name = $"0x{i:X2}";
                // #656 — resolve the portrait OWNER's name (scans unit/class tables
                // for a unit/class with portrait_id == i) instead of treating the
                // portrait index as a 0-based unit-table row. Mirrors WinForms
                // ImagePortraitForm.GetPortraitNameFast.
                try
                {
                    string pname = NameResolver.GetPortraitName((uint)i);
                    if (!string.IsNullOrEmpty(pname)) name += $" {pname}";
                }
                catch (Exception ex) { Log.ErrorF("ImagePortraitViewModel.LoadList portrait name resolve: {0}", ex.Message); }
                result.Add(new AddrResult(addr, name, (uint)i));
            }
            ReadCount = (uint)result.Count;
            return result;
        }

        /// <summary>
        /// #1411 — the size in bytes of one portrait entry this generic editor reads/writes.
        /// Exposed so tests can assert the guard invariant against the real stride.
        /// </summary>
        public static uint EntrySize => SIZE;

        /// <summary>
        /// #1411 — pure guard predicate. The generic editor assumes a 28-byte portrait
        /// entry (FE7/FE8). It is supported ONLY when the ROM's portrait stride equals
        /// <see cref="SIZE"/> exactly. ANY other value — FE6's 16, a patched stride, or
        /// an unknown/zero stride — is UNSUPPORTED, so the editor never reads/writes 28
        /// bytes into a differently-sized (or invalid) table. (Copilot PR-review #1
        /// closed the prior <c>stride == 0</c> hole, which let a write proceed on an
        /// unknown layout.) The dedicated <c>ImagePortraitFE6ViewModel</c> (SIZE=16) is
        /// FE6's editor.
        /// </summary>
        public static bool IsUnsupportedPortraitStride(uint portraitDataSize)
            => portraitDataSize != SIZE;

        bool IsUnsupportedPortraitStride(ROM rom)
            => IsUnsupportedPortraitStride(rom?.RomInfo?.portrait_datasize ?? 0);

        public void LoadEntry(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (IsUnsupportedPortraitStride(rom)) return; // #1411 — only the 28-byte FE7/FE8 layout is supported
            if (addr + SIZE > (uint)rom.Data.Length) return;

            CurrentAddr = addr;

            PortraitImagePtr = rom.u32(addr + 0);
            MiniPortraitPtr = rom.u32(addr + 4);
            PalettePtr = rom.u32(addr + 8);
            MouthFramesPtr = rom.u32(addr + 12);
            ClassCardPtr = rom.u32(addr + 16);
            MouthX = rom.u8(addr + 20);
            MouthY = rom.u8(addr + 21);
            EyeX = rom.u8(addr + 22);
            EyeY = rom.u8(addr + 23);
            Status = rom.u8(addr + 24);
            Unused25 = rom.u8(addr + 25);
            Unused26 = rom.u8(addr + 26);
            Unused27 = rom.u8(addr + 27);

            // Touch slice properties so listeners refresh.
            OnPropertyChanged(nameof(MugExceedB16));
            OnPropertyChanged(nameof(MugExceedB17));
            OnPropertyChanged(nameof(MugExceedB18));
            OnPropertyChanged(nameof(MugExceedB19));

            IsLoaded = true;
        }

        /// <summary>
        /// Write the current entry to ROM. The caller passes its
        /// <see cref="UndoService"/> instance but does NOT open a Begin
        /// scope around the call — this method owns the single
        /// <c>Begin</c>/<c>Commit</c> pair (Rollback runs on exception)
        /// and every <c>rom.write_*</c> lives strictly between them.
        ///
        /// Single-owner pattern per Copilot CLI plan-review point on PR #504 —
        /// the View's <c>WriteButton_Click</c> delegates here without opening
        /// or nesting its own scope.
        /// </summary>
        public void Write(UndoService undoService)
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;
            if (IsUnsupportedPortraitStride(rom)) return; // #1411 — never write 28 bytes into a 16-byte FE6 entry
            if (CurrentAddr + SIZE > (uint)rom.Data.Length) return;
            if (undoService == null) { Write(); return; }

            uint addr = CurrentAddr;
            undoService.Begin("Write Portrait");
            try
            {
                rom.write_u32(addr + 0, PortraitImagePtr);
                rom.write_u32(addr + 4, MiniPortraitPtr);
                rom.write_u32(addr + 8, PalettePtr);
                rom.write_u32(addr + 12, MouthFramesPtr);
                rom.write_u32(addr + 16, ClassCardPtr);
                rom.write_u8(addr + 20, MouthX);
                rom.write_u8(addr + 21, MouthY);
                rom.write_u8(addr + 22, EyeX);
                rom.write_u8(addr + 23, EyeY);
                rom.write_u8(addr + 24, Status);
                rom.write_u8(addr + 25, Unused25);
                rom.write_u8(addr + 26, Unused26);
                rom.write_u8(addr + 27, Unused27);
                undoService.Commit();
            }
            catch
            {
                undoService.Rollback();
                throw;
            }
        }

        /// <summary>
        /// Parameterless legacy overload — kept for non-View callers (tests,
        /// CLI helpers). Spins up its own UndoService so the writes still
        /// register in the undo buffer.
        /// </summary>
        public void Write()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;
            if (CurrentAddr + SIZE > (uint)rom.Data.Length) return;

            var undoService = new UndoService();
            Write(undoService);
        }

        public int GetListCount() => LoadList().Count;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["PortraitImagePtr"] = $"0x{PortraitImagePtr:X08}",
                ["MiniPortraitPtr"] = $"0x{MiniPortraitPtr:X08}",
                ["PalettePtr"] = $"0x{PalettePtr:X08}",
                ["MouthFramesPtr"] = $"0x{MouthFramesPtr:X08}",
                ["ClassCardPtr"] = $"0x{ClassCardPtr:X08}",
                ["MouthX"] = $"0x{MouthX:X02}",
                ["MouthY"] = $"0x{MouthY:X02}",
                ["EyeX"] = $"0x{EyeX:X02}",
                ["EyeY"] = $"0x{EyeY:X02}",
                ["Status"] = $"0x{Status:X02}",
                ["Unused25"] = $"0x{Unused25:X02}",
                ["Unused26"] = $"0x{Unused26:X02}",
                ["Unused27"] = $"0x{Unused27:X02}",
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
                ["u32@0_PortraitImagePtr"] = $"0x{rom.u32(a + 0):X08}",
                ["u32@4_MiniPortraitPtr"] = $"0x{rom.u32(a + 4):X08}",
                ["u32@8_PalettePtr"] = $"0x{rom.u32(a + 8):X08}",
                ["u32@12_MouthFramesPtr"] = $"0x{rom.u32(a + 12):X08}",
                ["u32@16_ClassCardPtr"] = $"0x{rom.u32(a + 16):X08}",
                ["u8@20_MouthX"] = $"0x{rom.u8(a + 20):X02}",
                ["u8@21_MouthY"] = $"0x{rom.u8(a + 21):X02}",
                ["u8@22_EyeX"] = $"0x{rom.u8(a + 22):X02}",
                ["u8@23_EyeY"] = $"0x{rom.u8(a + 23):X02}",
                ["u8@24_Status"] = $"0x{rom.u8(a + 24):X02}",
                ["u8@25_Unused25"] = $"0x{rom.u8(a + 25):X02}",
                ["u8@26_Unused26"] = $"0x{rom.u8(a + 26):X02}",
                ["u8@27_Unused27"] = $"0x{rom.u8(a + 27):X02}",
            };
        }

        public Dictionary<string, string> GetFieldOffsetMap() => new()
        {
            ["PortraitImagePtr"] = "u32@0_PortraitImagePtr",
            ["MiniPortraitPtr"] = "u32@4_MiniPortraitPtr",
            ["PalettePtr"] = "u32@8_PalettePtr",
            ["MouthFramesPtr"] = "u32@12_MouthFramesPtr",
            ["ClassCardPtr"] = "u32@16_ClassCardPtr",
            ["MouthX"] = "u8@20_MouthX",
            ["MouthY"] = "u8@21_MouthY",
            ["EyeX"] = "u8@22_EyeX",
            ["EyeY"] = "u8@23_EyeY",
            ["Status"] = "u8@24_Status",
            ["Unused25"] = "u8@25_Unused25",
            ["Unused26"] = "u8@26_Unused26",
            ["Unused27"] = "u8@27_Unused27",
        };
    }
}
