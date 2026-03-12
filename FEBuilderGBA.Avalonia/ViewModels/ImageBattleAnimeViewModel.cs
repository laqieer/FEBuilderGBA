using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class ImageBattleAnimeViewModel : ViewModelBase, IDataVerifiable
    {
        const uint SIZE = 4;
        const uint ANIME_RECORD_SIZE = 32;

        uint _currentAddr;
        bool _isLoaded;
        bool _canWrite;
        uint _weaponType, _special, _animationNumber;

        // Animation detail fields
        string _weaponTypeName = "";
        string _animeName = "";
        uint _animeDataAddr;
        bool _hasAnimeDetails;
        string _sectionPointer = "";
        string _framePointer = "";
        string _oamRtLPointer = "";
        string _oamLtRPointer = "";
        string _palettePointer = "";
        string _frameLZ77Info = "";
        string _oamLZ77Info = "";
        int _animationCount;
        IImage _tileSheetImage;
        string _tileSheetInfo = "";

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public bool CanWrite { get => _canWrite; set => SetField(ref _canWrite, value); }

        // B0: Weapon type index
        public uint WeaponType { get => _weaponType; set => SetField(ref _weaponType, value); }
        // B1: Special flag
        public uint Special { get => _special; set => SetField(ref _special, value); }
        // W2: Animation number
        public uint AnimationNumber { get => _animationNumber; set => SetField(ref _animationNumber, value); }

        // Animation detail properties
        public string WeaponTypeName { get => _weaponTypeName; set => SetField(ref _weaponTypeName, value); }
        public string AnimeName { get => _animeName; set => SetField(ref _animeName, value); }
        public uint AnimeDataAddr { get => _animeDataAddr; set => SetField(ref _animeDataAddr, value); }
        public bool HasAnimeDetails { get => _hasAnimeDetails; set => SetField(ref _hasAnimeDetails, value); }
        public string SectionPointer { get => _sectionPointer; set => SetField(ref _sectionPointer, value); }
        public string FramePointer { get => _framePointer; set => SetField(ref _framePointer, value); }
        public string OamRtLPointer { get => _oamRtLPointer; set => SetField(ref _oamRtLPointer, value); }
        public string OamLtRPointer { get => _oamLtRPointer; set => SetField(ref _oamLtRPointer, value); }
        public string PalettePointer { get => _palettePointer; set => SetField(ref _palettePointer, value); }
        public string FrameLZ77Info { get => _frameLZ77Info; set => SetField(ref _frameLZ77Info, value); }
        public string OamLZ77Info { get => _oamLZ77Info; set => SetField(ref _oamLZ77Info, value); }
        public int AnimationCount { get => _animationCount; set => SetField(ref _animationCount, value); }

        /// <summary>Rendered tile sheet image for the current animation.</summary>
        public IImage TileSheetImage { get => _tileSheetImage; set => SetField(ref _tileSheetImage, value); }
        /// <summary>Description of tile sheet (size, tile count).</summary>
        public string TileSheetInfo { get => _tileSheetInfo; set => SetField(ref _tileSheetInfo, value); }

        public List<AddrResult> LoadList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            uint pointer = rom.RomInfo.image_battle_animelist_pointer;
            if (pointer == 0) return new List<AddrResult>();

            uint baseAddr = rom.p32(pointer);
            if (!U.isSafetyOffset(baseAddr, rom)) return new List<AddrResult>();

            const uint blockSize = 4;
            var result = new List<AddrResult>();
            for (int i = 0; i < 512; i++)
            {
                uint addr = baseAddr + (uint)(i * blockSize);
                if (addr + blockSize > (uint)rom.Data.Length) break;
                if (rom.u32(addr) == 0) break;

                result.Add(new AddrResult(addr, $"0x{i:X2} Anim", (uint)i));
            }
            return result;
        }

        /// <summary>
        /// Load the animation data table (32-byte records) from the ROM.
        /// Returns a list of (addr, "0xNN name") entries.
        /// </summary>
        public List<AddrResult> LoadAnimationTable()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            uint pointer = rom.RomInfo.image_battle_animelist_pointer;
            if (pointer == 0) return new List<AddrResult>();

            uint baseAddr = rom.p32(pointer);
            if (!U.isSafetyOffset(baseAddr, rom)) return new List<AddrResult>();

            var result = new List<AddrResult>();
            for (int i = 0; i < 512; i++)
            {
                uint addr = baseAddr + (uint)(i * ANIME_RECORD_SIZE);
                if (addr + ANIME_RECORD_SIZE > (uint)rom.Data.Length) break;

                // Validate: offsets 12, 20, 24 should be pointers
                if (!U.isPointer(rom.u32(addr + 12))
                    || !U.isPointer(rom.u32(addr + 20))
                    || !U.isPointer(rom.u32(addr + 24)))
                    break;

                string name = rom.getString(addr, 12);
                result.Add(new AddrResult(addr, $"0x{i + 1:X2} {name}", (uint)(i + 1)));
            }
            return result;
        }

        public void LoadEntry(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + SIZE > (uint)rom.Data.Length) return;

            CurrentAddr = addr;

            WeaponType = rom.u8(addr + 0);
            Special = rom.u8(addr + 1);
            AnimationNumber = rom.u16(addr + 2);

            // Resolve weapon/item name
            WeaponTypeName = ResolveSPTypeName(WeaponType, Special);

            // Load animation details from the 32-byte animation data table
            LoadAnimationDetails(AnimationNumber);

            IsLoaded = true;
            CanWrite = true;
        }

        /// <summary>
        /// Follow the animation ID to the 32-byte animation data record and
        /// extract section, frame, OAM, and palette pointer info.
        /// Animation IDs are 1-based (0 = none).
        /// </summary>
        public void LoadAnimationDetails(uint animeId)
        {
            ROM rom = CoreState.ROM;
            HasAnimeDetails = false;
            AnimeName = "";
            AnimeDataAddr = 0;
            SectionPointer = "";
            FramePointer = "";
            OamRtLPointer = "";
            OamLtRPointer = "";
            PalettePointer = "";
            FrameLZ77Info = "";
            OamLZ77Info = "";
            TileSheetImage = null;
            TileSheetInfo = "";

            if (rom?.RomInfo == null || animeId == 0) return;

            uint pointer = rom.RomInfo.image_battle_animelist_pointer;
            if (pointer == 0) return;

            uint tableBase = rom.p32(pointer);
            if (!U.isSafetyOffset(tableBase, rom)) return;

            // Animation ID is 1-based
            uint id = animeId - 1;
            uint addr = tableBase + id * ANIME_RECORD_SIZE;
            if (addr + ANIME_RECORD_SIZE > (uint)rom.Data.Length) return;

            // Validate the record has valid pointers
            uint sectionRaw = rom.u32(addr + 12);
            uint frameRaw = rom.u32(addr + 16);
            uint oamRtLRaw = rom.u32(addr + 20);
            uint oamLtRRaw = rom.u32(addr + 24);
            uint paletteRaw = rom.u32(addr + 28);

            if (!U.isPointer(sectionRaw) || !U.isPointer(oamRtLRaw) || !U.isPointer(oamLtRRaw))
                return;

            AnimeDataAddr = addr;
            AnimeName = rom.getString(addr, 12);
            HasAnimeDetails = true;

            SectionPointer = $"0x{sectionRaw:X08}";
            FramePointer = $"0x{frameRaw:X08}";
            OamRtLPointer = $"0x{oamRtLRaw:X08}";
            OamLtRPointer = $"0x{oamLtRRaw:X08}";
            PalettePointer = $"0x{paletteRaw:X08}";

            // Get LZ77 decompressed sizes for frame and OAM data
            uint frameOff = U.toOffset(frameRaw);
            if (U.isSafetyOffset(frameOff, rom))
            {
                uint frameSize = LZ77.getUncompressSize(rom.Data, frameOff);
                FrameLZ77Info = frameSize > 0
                    ? $"LZ77 decompressed: {frameSize} bytes"
                    : "Not LZ77 compressed";
            }

            uint oamOff = U.toOffset(oamRtLRaw);
            if (U.isSafetyOffset(oamOff, rom))
            {
                uint oamSize = LZ77.getUncompressSize(rom.Data, oamOff);
                OamLZ77Info = oamSize > 0
                    ? $"LZ77 decompressed: {oamSize} bytes"
                    : "Not LZ77 compressed";
            }

            // Render tile sheet from frame data + palette
            try
            {
                IImage sheet = BattleAnimeRendererCore.RenderAnimationTileSheet(addr, 16);
                TileSheetImage = sheet;
                if (sheet != null)
                {
                    int tileCount = 0;
                    uint fOff = U.toOffset(frameRaw);
                    if (U.isSafetyOffset(fOff, rom))
                    {
                        byte[] fd = LZ77.decompress(rom.Data, fOff);
                        if (fd != null) tileCount = fd.Length / 32;
                    }
                    TileSheetInfo = $"{sheet.Width}x{sheet.Height} px, {tileCount} tiles (4bpp)";
                }
                else
                {
                    TileSheetInfo = "Could not render tile sheet";
                }
            }
            catch
            {
                TileSheetImage = null;
                TileSheetInfo = "Rendering error";
            }
        }

        /// <summary>
        /// Count total animation entries in the animation data table.
        /// </summary>
        public int CountAnimations()
        {
            return LoadAnimationTable().Count;
        }

        /// <summary>
        /// Resolve the SP type name from weapon type and special flag.
        /// When Special=0, B0 is an item ID. When Special=1, B0 is a weapon type index.
        /// </summary>
        public static string ResolveSPTypeName(uint b0, uint b1)
        {
            if (b1 == 0)
            {
                // Item-based: b0 is item ID
                return NameResolver.GetItemName(b0);
            }
            if (b1 == 1)
            {
                // Weapon type
                string[] weaponTypes = { "Sword", "Lance", "Axe", "Bow", "Staff", "Anima", "Light", "Dark" };
                return b0 < (uint)weaponTypes.Length ? weaponTypes[b0] : $"Type 0x{b0:X02}";
            }
            return $"Special=0x{b1:X02}";
        }

        public void Write()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;

            uint addr = CurrentAddr;
            rom.write_u8(addr + 0, WeaponType);
            rom.write_u8(addr + 1, Special);
            rom.write_u16(addr + 2, AnimationNumber);
        }

        public int GetListCount() => LoadList().Count;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["WeaponType"] = $"0x{WeaponType:X02}",
                ["Special"] = $"0x{Special:X02}",
                ["AnimationNumber"] = $"0x{AnimationNumber:X04}",
            };
        }

        public Dictionary<string, string> GetRawRomReport()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return new Dictionary<string, string>();

            uint a = CurrentAddr;
            var report = new Dictionary<string, string>
            {
                ["addr"] = $"0x{a:X08}",
                ["u8@0"] = $"0x{rom.u8(a + 0):X02}",
                ["u8@1"] = $"0x{rom.u8(a + 1):X02}",
                ["u16@2"] = $"0x{rom.u16(a + 2):X04}",
            };

            // Include anime detail record if available
            if (HasAnimeDetails && AnimeDataAddr > 0
                && AnimeDataAddr + ANIME_RECORD_SIZE <= (uint)rom.Data.Length)
            {
                uint d = AnimeDataAddr;
                report["u32@12_Section"] = $"0x{rom.u32(d + 12):X08}";
                report["u32@16_Frame"] = $"0x{rom.u32(d + 16):X08}";
                report["u32@20_OamRtL"] = $"0x{rom.u32(d + 20):X08}";
                report["u32@24_OamLtR"] = $"0x{rom.u32(d + 24):X08}";
                report["u32@28_Palette"] = $"0x{rom.u32(d + 28):X08}";
            }

            return report;
        }
    }
}
