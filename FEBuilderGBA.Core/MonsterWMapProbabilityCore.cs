using System;
using System.Collections.Generic;

namespace FEBuilderGBA
{
    /// <summary>
    /// GUI-free Core helper for the World Map Monster editor (FE8-only).
    /// Ports the three WinForms <c>MonsterWMapProbabilityForm</c> editing surfaces that
    /// the Avalonia <c>MonsterWMapProbabilityViewer</c> originally dropped (#1464):
    ///  - <b>Stage spread</b> — <c>monster_wmap_stage_1/2_pointer</c>, stride 1, count &lt; 0xB,
    ///    each entry a u8 mapID (Eirika = pointer 1, Ephraim = pointer 2).
    ///  - <b>Per-base probabilities</b> — <c>monster_wmap_probability_1/2_pointer</c>, stride 9,
    ///    count &lt; 0xB, each row a 9-byte spawn-weight vector (one weight per base point).
    ///  - <b>Skirmish events</b> — <c>worldmap_skirmish_startevent/endevent_pointer</c>, a single
    ///    p32 per slot.
    /// All reads guard <see cref="U.isSafetyOffset"/> / EOF and never throw. Mutators write via
    /// <c>rom.write_*</c> so they participate in whatever ambient undo scope is active.
    /// </summary>
    public static class MonsterWMapProbabilityCore
    {
        /// <summary>WinForms <c>N1_Init</c> / <c>N2_Init</c> read-max: count &lt; 0xB.</summary>
        public const int StageCount = 0xB;
        public const int ProbabilityCount = 0xB;

        /// <summary>WinForms per-row probability width (stride 9) and base-point count.</summary>
        public const int ProbabilityWidth = 9;

        /// <summary>Base point list read-max: count &lt; 9 (WinForms <c>Init</c>).</summary>
        public const int BasePointCount = 9;

        /// <summary>World map point record stride (matches the Avalonia WorldMapPoint editor).</summary>
        const uint WorldMapPointStride = 32;
        const uint WorldMapPointNameTextIdOffset = 28;

        /// <summary>
        /// FE8-only gate. The three extra surfaces only exist on FE8J/FE8U where all the
        /// relevant ROMINFO pointers are populated.
        /// </summary>
        public static bool IsSupported(ROM rom)
        {
            if (rom?.RomInfo == null) return false;
            return rom.RomInfo.monster_wmap_stage_1_pointer != 0
                && rom.RomInfo.monster_wmap_probability_1_pointer != 0
                && rom.RomInfo.worldmap_skirmish_startevent_pointer != 0;
        }

        /// <summary>
        /// Dereference a pointer slot, returning 0 (instead of throwing) when the slot's
        /// 4 bytes do not fully fit in the ROM. <c>ROM.p32</c>→<c>U.u32</c> throws on a
        /// slot within the last 3 bytes of the ROM; this keeps the "never throws" contract.
        /// </summary>
        static uint SafeP32(ROM rom, uint slot)
        {
            if (rom == null || slot == 0) return 0;
            if (!FitsInRom(rom, slot, 4)) return 0;
            return rom.p32(slot);
        }

        /// <summary>
        /// Overflow-safe range check: are <paramref name="len"/> bytes at
        /// <paramref name="addr"/> fully inside the ROM? Uses <c>ulong</c> math so a large
        /// <paramref name="addr"/> can't wrap a <c>uint</c> sum past the length test.
        /// </summary>
        static bool FitsInRom(ROM rom, uint addr, uint len)
        {
            if (rom == null) return false;
            return (ulong)addr + len <= (ulong)rom.Data.Length;
        }

        // ----------------------------------------------------------------------------
        // Surface 2 — stage spread
        // ----------------------------------------------------------------------------

        /// <summary>ROMINFO pointer slot for the stage table (Eirika = 1, Ephraim = 2).</summary>
        public static uint GetStagePointer(ROM rom, bool isEphraim)
        {
            if (rom?.RomInfo == null) return 0;
            return isEphraim
                ? rom.RomInfo.monster_wmap_stage_2_pointer
                : rom.RomInfo.monster_wmap_stage_1_pointer;
        }

        /// <summary>
        /// List the stage-spread entries (stride 1). Each label is
        /// <c>ToHexString(i) + " " + mapName</c> (mirrors WinForms <c>N1_Init</c>).
        /// </summary>
        public static List<AddrResult> LoadStageList(ROM rom, bool isEphraim)
        {
            var result = new List<AddrResult>();
            if (rom?.RomInfo == null) return result;

            uint ptr = GetStagePointer(rom, isEphraim);
            if (ptr == 0) return result;

            uint baseAddr = SafeP32(rom, ptr);
            if (!U.isSafetyOffset(baseAddr, rom)) return result;

            for (uint i = 0; i < StageCount; i++)
            {
                uint addr = (uint)(baseAddr + i * 1);
                if (addr >= (uint)rom.Data.Length) break;

                uint mapId = rom.u8(addr);
                string name = U.ToHexString(i) + " " + MapSettingCore.GetMapNameById(rom, mapId);
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        /// <summary>Read the single mapID byte for a stage-spread entry.</summary>
        public static uint ReadStageMapId(ROM rom, uint addr)
        {
            if (rom == null) return 0;
            if (addr == 0 || addr >= (uint)rom.Data.Length) return 0;
            return rom.u8(addr);
        }

        /// <summary>
        /// Write the mapID byte for a stage-spread entry. Uses the ambient undo scope
        /// (Avalonia wraps with <c>UndoService.Begin/Commit</c>).
        /// </summary>
        public static void WriteStageMapId(ROM rom, uint addr, uint mapId)
        {
            if (rom == null) return;
            if (addr == 0 || addr >= (uint)rom.Data.Length) return;
            rom.write_u8(addr, mapId & 0xFF);
        }

        // ----------------------------------------------------------------------------
        // Surface 3 — per-base probabilities
        // ----------------------------------------------------------------------------

        /// <summary>ROMINFO pointer slot for the probability table (Eirika = 1, Ephraim = 2).</summary>
        public static uint GetProbabilityPointer(ROM rom, bool isEphraim)
        {
            if (rom?.RomInfo == null) return 0;
            return isEphraim
                ? rom.RomInfo.monster_wmap_probability_2_pointer
                : rom.RomInfo.monster_wmap_probability_1_pointer;
        }

        /// <summary>
        /// List the probability rows (stride 9). Label is <c>ToHexString(i)</c>
        /// (mirrors WinForms <c>N2_Init</c>).
        /// </summary>
        public static List<AddrResult> LoadProbabilityList(ROM rom, bool isEphraim)
        {
            var result = new List<AddrResult>();
            if (rom?.RomInfo == null) return result;

            uint ptr = GetProbabilityPointer(rom, isEphraim);
            if (ptr == 0) return result;

            uint baseAddr = SafeP32(rom, ptr);
            if (!U.isSafetyOffset(baseAddr, rom)) return result;

            for (uint i = 0; i < ProbabilityCount; i++)
            {
                uint addr = (uint)(baseAddr + i * ProbabilityWidth);
                if (!FitsInRom(rom, addr, ProbabilityWidth)) break;

                string name = U.ToHexString(i);
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        /// <summary>Read the 9-byte probability row at <paramref name="addr"/>.</summary>
        public static byte[] ReadProbabilityRow(ROM rom, uint addr)
        {
            var row = new byte[ProbabilityWidth];
            if (rom == null) return row;
            if (addr == 0 || !FitsInRom(rom, addr, ProbabilityWidth)) return row;
            for (int k = 0; k < ProbabilityWidth; k++)
            {
                row[k] = (byte)rom.u8((uint)(addr + k));
            }
            return row;
        }

        /// <summary>
        /// Write a 9-byte probability row at <paramref name="addr"/>. Uses the ambient
        /// undo scope. <paramref name="row"/> shorter than 9 is zero-padded; longer is
        /// truncated.
        /// </summary>
        public static void WriteProbabilityRow(ROM rom, uint addr, byte[] row)
        {
            if (rom == null || row == null) return;
            if (addr == 0 || !FitsInRom(rom, addr, ProbabilityWidth)) return;
            for (int k = 0; k < ProbabilityWidth; k++)
            {
                byte v = k < row.Length ? row[k] : (byte)0;
                rom.write_u8((uint)(addr + k), v);
            }
        }

        /// <summary>Sum of a probability row (WinForms shows this as "<c>{sum}%</c>").</summary>
        public static uint Sum(byte[] row)
        {
            if (row == null) return 0;
            uint sum = 0;
            foreach (byte b in row) sum += b;
            return sum;
        }

        /// <summary>
        /// Resolve the world-map base-point name for a given base-point id. Ports
        /// WinForms <c>WorldMapPointForm.GetWorldMapPointName</c>: index the world-map
        /// point table (stride 32), read the name textid at +28, decode it.
        /// The text is decoded against the PASSED <paramref name="rom"/> (not the ambient
        /// <c>CoreState.ROM</c>) so a caller using a different ROM gets the right name;
        /// the encoder is ambient, so this is READ-ONLY but not strictly pure.
        /// </summary>
        public static string GetWorldMapPointName(ROM rom, uint baseId)
        {
            if (rom?.RomInfo == null) return "";
            uint ptr = rom.RomInfo.worldmap_point_pointer;
            if (ptr == 0) return "";

            uint baseAddr = SafeP32(rom, ptr);
            if (!U.isSafetyOffset(baseAddr, rom)) return "";

            uint addr = (uint)(baseAddr + baseId * WorldMapPointStride);
            if (!FitsInRom(rom, addr + WorldMapPointNameTextIdOffset, 2)) return "";

            uint textid = rom.u16((uint)(addr + WorldMapPointNameTextIdOffset));
            if (textid == 0) return "";

            // Decode against the PASSED rom (NameResolver.GetTextById/FETextDecode.Direct
            // read CoreState.ROM, which would mislabel when rom != the active ROM).
            var encoder = CoreState.SystemTextEncoder;
            if (encoder == null) return "";
            try
            {
                string raw = new FETextDecode(rom, encoder).Decode(textid) ?? "";
                return NameResolver.StripControlCodes(raw);
            }
            catch { return ""; }
        }

        /// <summary>
        /// The 9 base-point column labels for the probability table. WinForms reads the
        /// base-point list (<c>monster_wmap_base_point_pointer</c>) and shows
        /// <c>GetWorldMapPointName(baseId)</c> for each. Always returns exactly 9 entries
        /// (missing/short entries are blank).
        /// </summary>
        public static List<string> GetBasePointLabels(ROM rom)
        {
            var labels = new List<string>(BasePointCount);
            for (int i = 0; i < BasePointCount; i++) labels.Add("");
            if (rom?.RomInfo == null) return labels;

            uint ptr = rom.RomInfo.monster_wmap_base_point_pointer;
            if (ptr == 0) return labels;

            uint baseAddr = SafeP32(rom, ptr);
            if (!U.isSafetyOffset(baseAddr, rom)) return labels;

            for (uint i = 0; i < BasePointCount; i++)
            {
                uint addr = (uint)(baseAddr + i);
                if (addr >= (uint)rom.Data.Length) break;
                uint baseId = rom.u8(addr);
                labels[(int)i] = U.ToHexString(i) + " " + GetWorldMapPointName(rom, baseId);
            }
            return labels;
        }

        // ----------------------------------------------------------------------------
        // Surface 4 — skirmish events
        // ----------------------------------------------------------------------------

        /// <summary>Read the skirmish-start event pointer (free-map start event).</summary>
        public static uint ReadSkirmishStartEvent(ROM rom)
        {
            if (rom?.RomInfo == null) return 0;
            uint slot = rom.RomInfo.worldmap_skirmish_startevent_pointer;
            return SafeP32(rom, slot);
        }

        /// <summary>Read the skirmish-end event pointer (free-map end event).</summary>
        public static uint ReadSkirmishEndEvent(ROM rom)
        {
            if (rom?.RomInfo == null) return 0;
            uint slot = rom.RomInfo.worldmap_skirmish_endevent_pointer;
            return SafeP32(rom, slot);
        }

        /// <summary>
        /// Write the skirmish start/end event pointers. Mirrors WinForms
        /// <c>EventWriteButton_Click</c>: <c>write_p32</c> both slots. Uses the ambient
        /// undo scope.
        /// </summary>
        public static void WriteSkirmishEvents(ROM rom, uint startEvent, uint endEvent)
        {
            if (rom?.RomInfo == null) return;
            uint startSlot = rom.RomInfo.worldmap_skirmish_startevent_pointer;
            uint endSlot = rom.RomInfo.worldmap_skirmish_endevent_pointer;
            // Guard the slots fully fit in the ROM before writing — write_p32→U.write_u32
            // throws on a slot within the last 3 bytes (truncated/corrupt ROM).
            if (startSlot != 0 && FitsInRom(rom, startSlot, 4)) rom.write_p32(startSlot, startEvent);
            if (endSlot != 0 && FitsInRom(rom, endSlot, 4)) rom.write_p32(endSlot, endEvent);
        }
    }
}
