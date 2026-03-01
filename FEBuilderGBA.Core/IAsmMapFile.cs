using System.Collections.Generic;

namespace FEBuilderGBA
{
    /// <summary>
    /// Data class for ASM map entries (function/symbol information).
    /// Shared between Core (DisassemblerTrumb) and WinForms (AsmMapFile).
    /// </summary>
    public class AsmMapSt
    {
        public string Name = "";
        public string ResultAndArgs = "";
        public string TypeName = "";
        public uint Length = 0;
        public bool IsPointer = false;
        public bool IsFreeArea = false;

        public string ToStringInfo()
        {
            if (ResultAndArgs != "")
            {
                return Name + " " + ResultAndArgs;
            }
            return Name;
        }
    }

    /// <summary>
    /// Interface for ASM map lookup, used by DisassemblerTrumb.
    /// WinForms' AsmMapFile implements this.
    /// </summary>
    public interface IAsmMapFile
    {
        bool TryGetValue(uint pointer, out AsmMapSt out_p);
    }

    /// <summary>
    /// GBA BIOS SWI call name lookup table.
    /// Extracted from AsmMapFile for use in Core's DisassemblerTrumb.
    /// </summary>
    public static class GbaBiosCall
    {
        public static string GetSWI_GBA_BIOS_CALL(uint swicode)
        {
            switch (swicode)
            {
                case 0x00: return "SoftReset";
                case 0x01: return "RegisterRamReset";
                case 0x02: return "Halt";
                case 0x03: return "Stop";
                case 0x04: return "IntrWait";
                case 0x05: return "VBlankIntrWait";
                case 0x06: return "Div";
                case 0x07: return "DivArm";
                case 0x08: return "Sqrt";
                case 0x09: return "ArcTan";
                case 0x0A: return "ArcTan2";
                case 0x0B: return "CpuSet";
                case 0x0C: return "CpuFastSet";
                case 0x0D: return "GetBiosCheckSum";
                case 0x0E: return "BgAffineSet";
                case 0x0F: return "ObjAffineSet";
                case 0x10: return "BitUnPack";
                case 0x11: return "LZ77UnCompNormalWrite8bit";
                case 0x12: return "LZ77UnCompNormalWrite8bit";
                case 0x13: return "HuffUnCompReadNormal";
                case 0x14: return "RLUnCompReadNormalWrite8bit";
                case 0x15: return "RLUnCompReadNormalWrite16bit";
                case 0x16: return "Diff8bitUnFilterNormalWrite8bit";
                case 0x17: return "Diff8bitUnFilterNormalWrite8bit";
                case 0x18: return "Diff16bitUnFilter";
                case 0x19: return "SoundBias";
                case 0x1A: return "SoundDriverInit";
                case 0x1B: return "SoundDriverMode";
                case 0x1C: return "SoundDriverMain";
                case 0x1D: return "SoundDriverVSync";
                case 0x1E: return "SoundChannelClear";
                case 0x1F: return "MidiKey2Freq";
                case 0x20: return "SoundWHatever0";
                case 0x21: return "SoundWHatever1";
                case 0x22: return "SoundWHatever2";
                case 0x23: return "SoundWHatever3";
                case 0x24: return "SoundWHatever4";
                case 0x25: return "MultiBoot";
                case 0x26: return "HardReset";
                case 0x27: return "CustomHalt";
                case 0x28: return "SoundDriverVSyncOff";
                case 0x29: return "SoundDriverVSyncOn";
                case 0x2A: return "SoundGetJumpList";
            }
            return "";
        }
    }
}
