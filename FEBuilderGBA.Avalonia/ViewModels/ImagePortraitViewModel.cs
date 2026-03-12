using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class ImagePortraitViewModel : ViewModelBase, IDataVerifiable
    {
        const uint SIZE = 28;

        uint _currentAddr;
        bool _isLoaded;
        uint _portraitImagePtr, _miniPortraitPtr, _palettePtr, _mouthFramesPtr, _classCardPtr;
        uint _mouthX, _mouthY, _eyeX, _eyeY;
        uint _status, _unused25, _unused26, _unused27;

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
        // B26: Unused / reserved
        public uint Unused26 { get => _unused26; set => SetField(ref _unused26, value); }
        // B27: Unused / reserved
        public uint Unused27 { get => _unused27; set => SetField(ref _unused27, value); }

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
                Log.Error("RefreshFaceImage failed: {0}", ex.Message);
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
                Log.Error("RefreshMiniPortrait failed: {0}", ex.Message);
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
                Log.Error("RefreshMouthStrip failed: {0}", ex.Message);
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
                Log.Error("RefreshEyeStrip failed: {0}", ex.Message);
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
                Log.Error("RefreshClassCard failed: {0}", ex.Message);
                ClassCardImage = null;
            }
        }

        public List<AddrResult> LoadList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            uint pointer = rom.RomInfo.portrait_pointer;
            uint dataSize = rom.RomInfo.portrait_datasize;
            if (pointer == 0 || dataSize == 0) return new List<AddrResult>();

            uint baseAddr = rom.p32(pointer);
            if (!U.isSafetyOffset(baseAddr, rom)) return new List<AddrResult>();

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
                // Try to get unit name if this portrait index corresponds to a unit
                try { string uname = NameResolver.GetUnitName((uint)i); if (uname != "???" && uname != $"#{i}") name += $" {uname}"; }
                catch { }
                result.Add(new AddrResult(addr, name, (uint)i));
            }
            return result;
        }

        public void LoadEntry(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
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

            IsLoaded = true;
        }

        public void Write()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;
            if (CurrentAddr + SIZE > (uint)rom.Data.Length) return;

            uint addr = CurrentAddr;
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
    }
}
