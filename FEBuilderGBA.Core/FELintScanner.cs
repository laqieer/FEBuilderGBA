using System;
using System.Collections.Generic;

namespace FEBuilderGBA
{
    /// <summary>
    /// Cross-platform lint scanner for structural/pointer validation.
    /// Best-effort coverage — validates pointer tables, data integrity, map settings.
    /// Does not include Form-specific MakeCheckError() methods.
    /// </summary>
    public class FELintScanner
    {
        /// <summary>
        /// Run all available lint checks on the loaded ROM.
        /// </summary>
        public List<FELintCore.ErrorSt> Scan()
        {
            ROM rom = CoreState.ROM;
            if (rom == null)
                return new List<FELintCore.ErrorSt>();

            var errors = new List<FELintCore.ErrorSt>();

            ScanRomHeader(rom, errors);
            ScanUnitTable(rom, errors);
            ScanClassTable(rom, errors);
            ScanItemTable(rom, errors);
            ScanMapSettings(rom, errors);
            ScanTextTable(rom, errors);

            return errors;
        }

        void ScanRomHeader(ROM rom, List<FELintCore.ErrorSt> errors)
        {
            // Validate ROM header magic
            if (rom.Data.Length < 0xC0)
            {
                errors.Add(new FELintCore.ErrorSt(
                    FELintCore.Type.ROM_HEADER, 0, "ROM too small (< 192 bytes)"));
                return;
            }

            // Check fixed GBA header values
            uint fixedByte = rom.u8(0xB2);
            if (fixedByte != 0x96)
            {
                errors.Add(new FELintCore.ErrorSt(
                    FELintCore.Type.ROM_HEADER, 0xB2,
                    $"GBA header fixed byte should be 0x96, got 0x{fixedByte:X2}"));
            }
        }

        void ScanUnitTable(ROM rom, List<FELintCore.ErrorSt> errors)
        {
            if (rom.RomInfo.unit_pointer == 0) return;

            var err = FELintCore.CheckPointerAligned4(rom, FELintCore.Type.UNIT,
                rom.RomInfo.unit_pointer, "Unit table pointer");
            if (err != null)
            {
                errors.Add(err);
                return;
            }

            uint baseAddr = rom.p32(rom.RomInfo.unit_pointer);
            if (!U.isSafetyOffset(baseAddr)) return;

            uint dataSize = rom.RomInfo.unit_datasize;
            if (dataSize == 0) return;

            // Walk until end of ROM or reasonable limit
            uint maxCount = rom.RomInfo.unit_maxcount;
            if (maxCount == 0) maxCount = 0x200;
            for (uint i = 0; i < maxCount; i++)
            {
                uint addr = (uint)(baseAddr + i * dataSize);
                if (addr + dataSize > (uint)rom.Data.Length) break;

                uint nameId = rom.u16(addr + 0);
                ValidateTextId(rom, errors, FELintCore.Type.UNIT, addr, nameId, $"Unit {i} name");
            }
        }

        void ScanClassTable(ROM rom, List<FELintCore.ErrorSt> errors)
        {
            if (rom.RomInfo.class_pointer == 0) return;

            var err = FELintCore.CheckPointerAligned4(rom, FELintCore.Type.CLASS,
                rom.RomInfo.class_pointer, "Class table pointer");
            if (err != null)
            {
                errors.Add(err);
                return;
            }

            uint baseAddr = rom.p32(rom.RomInfo.class_pointer);
            if (!U.isSafetyOffset(baseAddr)) return;

            uint dataSize = rom.RomInfo.class_datasize;
            if (dataSize == 0) return;

            // Walk up to a reasonable limit
            for (uint i = 0; i < 0x200; i++)
            {
                uint addr = (uint)(baseAddr + i * dataSize);
                if (addr + dataSize > (uint)rom.Data.Length) break;

                uint nameId = rom.u16(addr + 0);
                ValidateTextId(rom, errors, FELintCore.Type.CLASS, addr, nameId, $"Class {i} name");
            }
        }

        void ScanItemTable(ROM rom, List<FELintCore.ErrorSt> errors)
        {
            if (rom.RomInfo.item_pointer == 0) return;

            var err = FELintCore.CheckPointerAligned4(rom, FELintCore.Type.ITEM,
                rom.RomInfo.item_pointer, "Item table pointer");
            if (err != null)
            {
                errors.Add(err);
                return;
            }

            uint baseAddr = rom.p32(rom.RomInfo.item_pointer);
            if (!U.isSafetyOffset(baseAddr)) return;

            uint dataSize = rom.RomInfo.item_datasize;
            if (dataSize == 0) return;

            // Walk up to a reasonable limit
            for (uint i = 0; i < 0x200; i++)
            {
                uint addr = (uint)(baseAddr + i * dataSize);
                if (addr + dataSize > (uint)rom.Data.Length) break;

                uint nameId = rom.u16(addr + 0);
                ValidateTextId(rom, errors, FELintCore.Type.ITEM, addr, nameId, $"Item {i} name");
            }
        }

        void ScanMapSettings(ROM rom, List<FELintCore.ErrorSt> errors)
        {
            if (rom.RomInfo.map_setting_pointer == 0) return;

            var err = FELintCore.CheckPointerAligned4(rom, FELintCore.Type.MAPSETTING,
                rom.RomInfo.map_setting_pointer, "Map settings pointer");
            if (err != null)
            {
                errors.Add(err);
                return;
            }

            var maps = MapSettingCore.MakeMapIDList();
            uint eventPlistPos = rom.RomInfo.map_setting_event_plist_pos;

            for (int i = 0; i < maps.Count; i++)
            {
                uint addr = maps[i].addr;

                // Validate event PLIST
                if (eventPlistPos > 0 && eventPlistPos + 1 < rom.RomInfo.map_setting_datasize)
                {
                    uint eventPlist = rom.u8(addr + eventPlistPos);
                    if (eventPlist > 0)
                    {
                        // Event PLIST should point to valid data
                        uint eventPointer = rom.RomInfo.map_setting_pointer;
                        // Basic validation: plist value shouldn't be absurdly large
                        if (eventPlist > 0xFF)
                        {
                            errors.Add(new FELintCore.ErrorSt(
                                FELintCore.Type.MAPSETTING_PLIST_EVENT, addr,
                                $"Map {i} event PLIST value too large: 0x{eventPlist:X}"));
                        }
                    }
                }
            }
        }

        void ScanTextTable(ROM rom, List<FELintCore.ErrorSt> errors)
        {
            if (rom.RomInfo.text_pointer == 0) return;

            var err = FELintCore.CheckPointerAligned4(rom, FELintCore.Type.TEXTID_FOR_SYSTEM,
                rom.RomInfo.text_pointer, "Text table pointer");
            if (err != null)
                errors.Add(err);
        }

        void ValidateTextId(ROM rom, List<FELintCore.ErrorSt> errors,
            FELintCore.Type dataType, uint addr, uint textId, string context)
        {
            if (textId == 0) return; // 0 is valid (empty/unused)

            // Simple bounds check
            if (rom.RomInfo.text_pointer == 0) return;
            uint textBase = rom.p32(rom.RomInfo.text_pointer);
            if (!U.isSafetyOffset(textBase)) return;

            // Check if textId * 4 would be within ROM
            uint textEntryAddr = (uint)(textBase + textId * 4);
            if (textEntryAddr + 4 > (uint)rom.Data.Length)
            {
                errors.Add(new FELintCore.ErrorSt(dataType, addr,
                    $"Text ID 0x{textId:X} out of range: {context}"));
            }
        }
    }
}
