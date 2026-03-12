using System;
using System.Collections.Generic;
using System.IO;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    /// <summary>
    /// Condition type categories matching WinForms EventCondForm.CONDTYPE.
    /// </summary>
    public enum CondCategory
    {
        TURN, TALK, OBJECT, ALWAYS, TUTORIAL, TRAP,
        PLAYER_UNIT, ENEMY_UNIT, FREEMAP_PLAYER_UNIT, FREEMAP_ENEMY_UNIT,
        START_EVENT, END_EVENT, UNKNOWN
    }

    /// <summary>
    /// One entry from the eventcond config file (e.g. eventcond_FE8.en.txt).
    /// Each entry corresponds to one pointer slot in the event data block.
    /// </summary>
    public class CondSlotDef
    {
        public CondCategory Category { get; set; }
        public string Name { get; set; } = "";
    }

    public class EventCondViewModel : ViewModelBase, IDataVerifiable
    {
        uint _mapSettingAddr;
        uint _eventDataAddr;     // base of the event data block (array of pointers)
        int _selectedSlotIndex = -1;
        uint _condRecordAddr;    // address of the currently-selected condition record
        uint _condRecordSize;    // size of one record for the current slot type
        bool _canWrite;

        // Condition record fields (generic layout)
        uint _condType;
        uint _subType;
        uint _flagId;
        uint _eventPtr;
        uint _extraB8, _extraB9, _extraB10, _extraB11;
        // FE7 extended (bytes 12-15)
        uint _extraB12, _extraB13, _extraB14, _extraB15;

        string _condTypeName = "";
        string _slotInfo = "";
        bool _isFE7Extended;
        bool _isPointerSlot; // PLAYER_UNIT, ENEMY_UNIT, etc. — just a single pointer

        // Static slot definitions loaded from config
        static List<CondSlotDef> _slotDefs = new();

        public uint MapSettingAddr { get => _mapSettingAddr; set => SetField(ref _mapSettingAddr, value); }
        public uint EventDataAddr { get => _eventDataAddr; set => SetField(ref _eventDataAddr, value); }
        public int SelectedSlotIndex { get => _selectedSlotIndex; set => SetField(ref _selectedSlotIndex, value); }
        public uint CondRecordAddr { get => _condRecordAddr; set => SetField(ref _condRecordAddr, value); }
        public uint CondRecordSize { get => _condRecordSize; set => SetField(ref _condRecordSize, value); }
        public bool CanWrite { get => _canWrite; set => SetField(ref _canWrite, value); }

        public uint CondType { get => _condType; set => SetField(ref _condType, value); }
        public uint SubType { get => _subType; set => SetField(ref _subType, value); }
        public uint FlagId { get => _flagId; set => SetField(ref _flagId, value); }
        public uint EventPtr { get => _eventPtr; set => SetField(ref _eventPtr, value); }
        public uint ExtraB8 { get => _extraB8; set => SetField(ref _extraB8, value); }
        public uint ExtraB9 { get => _extraB9; set => SetField(ref _extraB9, value); }
        public uint ExtraB10 { get => _extraB10; set => SetField(ref _extraB10, value); }
        public uint ExtraB11 { get => _extraB11; set => SetField(ref _extraB11, value); }
        public uint ExtraB12 { get => _extraB12; set => SetField(ref _extraB12, value); }
        public uint ExtraB13 { get => _extraB13; set => SetField(ref _extraB13, value); }
        public uint ExtraB14 { get => _extraB14; set => SetField(ref _extraB14, value); }
        public uint ExtraB15 { get => _extraB15; set => SetField(ref _extraB15, value); }

        public string CondTypeName { get => _condTypeName; set => SetField(ref _condTypeName, value); }
        public string SlotInfo { get => _slotInfo; set => SetField(ref _slotInfo, value); }
        public bool IsFE7Extended { get => _isFE7Extended; set => SetField(ref _isFE7Extended, value); }
        public bool IsPointerSlot { get => _isPointerSlot; set => SetField(ref _isPointerSlot, value); }

        public static IReadOnlyList<CondSlotDef> SlotDefs => _slotDefs;

        /// <summary>
        /// Load the condition slot definitions from the config file.
        /// Called once at startup.
        /// </summary>
        public static void LoadSlotDefs()
        {
            _slotDefs.Clear();
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return;

            int ver = rom.RomInfo.version;
            string lang = CoreState.Language ?? "en";

            // Try language-specific file first, then English, then default (Japanese)
            string baseName = $"eventcond_FE{ver}";
            string configDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config", "data");

            string[] candidates = {
                Path.Combine(configDir, baseName + "." + lang + ".txt"),
                Path.Combine(configDir, baseName + ".en.txt"),
                Path.Combine(configDir, baseName + ".txt"),
            };

            string? filePath = null;
            foreach (var c in candidates)
            {
                if (File.Exists(c)) { filePath = c; break; }
            }
            if (filePath == null) return;

            foreach (var line in File.ReadAllLines(filePath))
            {
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith("//") || line.StartsWith("#"))
                    continue;

                string[] parts = line.Split('\t');
                if (parts.Length < 2) continue;

                var def = new CondSlotDef();
                def.Name = parts[1];
                def.Category = parts[0] switch
                {
                    "TURN" => CondCategory.TURN,
                    "TALK" => CondCategory.TALK,
                    "OBJECT" => CondCategory.OBJECT,
                    "ALWAYS" => CondCategory.ALWAYS,
                    "TUTORIAL" => CondCategory.TUTORIAL,
                    "TRAP" => CondCategory.TRAP,
                    "PLAYER_UNIT" => CondCategory.PLAYER_UNIT,
                    "ENEMY_UNIT" => CondCategory.ENEMY_UNIT,
                    "FREEMAP_PLAYER_UNIT" => CondCategory.FREEMAP_PLAYER_UNIT,
                    "FREEMAP_ENEMY_UNIT" => CondCategory.FREEMAP_ENEMY_UNIT,
                    "START_EVENT" => CondCategory.START_EVENT,
                    "END_EVENT" => CondCategory.END_EVENT,
                    _ => CondCategory.UNKNOWN,
                };
                _slotDefs.Add(def);
            }
        }

        /// <summary>
        /// Return the map list for the left-panel navigation.
        /// </summary>
        public List<AddrResult> LoadMapList()
        {
            return MapSettingCore.MakeMapIDList();
        }

        /// <summary>
        /// Resolve the event data block address for a given map setting address.
        /// The map setting contains an event PLIST at a known offset.
        /// </summary>
        public bool ResolveEventDataAddr(uint mapSettingAddr)
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return false;

            MapSettingAddr = mapSettingAddr;
            EventDataAddr = 0;
            CanWrite = false;

            uint eventPlistPos = rom.RomInfo.map_setting_event_plist_pos;
            uint eventPlist = rom.u8(mapSettingAddr + eventPlistPos);
            if (eventPlist == 0) return false;

            // Resolve PLIST -> ROM address via the event pointer table
            uint eventPointerTable = rom.RomInfo.map_event_pointer;
            if (eventPointerTable == 0) return false;

            uint tableBase = rom.p32(eventPointerTable);
            if (!U.isSafetyOffset(tableBase)) return false;

            uint entryAddr = (uint)(tableBase + eventPlist * 4);
            if (entryAddr + 4 > (uint)rom.Data.Length) return false;

            uint eventAddr = rom.p32(entryAddr);
            if (!U.isSafetyOffset(eventAddr)) return false;

            EventDataAddr = eventAddr;
            return true;
        }

        /// <summary>
        /// Get the record size for a given condition slot category.
        /// </summary>
        uint GetRecordSize(CondCategory cat)
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return 12;

            switch (cat)
            {
                case CondCategory.TURN:
                    return rom.RomInfo.eventcond_tern_size;
                case CondCategory.TALK:
                    return rom.RomInfo.eventcond_talk_size;
                case CondCategory.OBJECT:
                case CondCategory.ALWAYS:
                    return 12;
                case CondCategory.TRAP:
                    return 6;
                case CondCategory.TUTORIAL:
                    return 4;
                case CondCategory.PLAYER_UNIT:
                case CondCategory.ENEMY_UNIT:
                case CondCategory.FREEMAP_PLAYER_UNIT:
                case CondCategory.FREEMAP_ENEMY_UNIT:
                case CondCategory.START_EVENT:
                case CondCategory.END_EVENT:
                    return 4; // pointer-only slots
                default:
                    return 12;
            }
        }

        /// <summary>
        /// Determine if a slot category is a "pointer-only" slot
        /// (unit placement, start/end events — just a single p32 pointer, no record array).
        /// </summary>
        static bool IsCategoryPointerOnly(CondCategory cat)
        {
            return cat == CondCategory.PLAYER_UNIT
                || cat == CondCategory.ENEMY_UNIT
                || cat == CondCategory.FREEMAP_PLAYER_UNIT
                || cat == CondCategory.FREEMAP_ENEMY_UNIT
                || cat == CondCategory.START_EVENT
                || cat == CondCategory.END_EVENT;
        }

        /// <summary>
        /// Load condition records for a specific slot index.
        /// Returns a list of AddrResult for the secondary list.
        /// </summary>
        public List<AddrResult> LoadConditionRecords(int slotIndex)
        {
            SelectedSlotIndex = slotIndex;
            var result = new List<AddrResult>();

            ROM rom = CoreState.ROM;
            if (rom == null || EventDataAddr == 0 || slotIndex < 0 || slotIndex >= _slotDefs.Count)
                return result;

            var slotDef = _slotDefs[slotIndex];
            bool pointerOnly = IsCategoryPointerOnly(slotDef.Category);
            IsPointerSlot = pointerOnly;

            // The event data block has one 4-byte pointer per slot
            uint slotPointerAddr = EventDataAddr + (uint)(slotIndex * 4);
            if (slotPointerAddr + 4 > (uint)rom.Data.Length)
                return result;

            uint rawPtr = rom.u32(slotPointerAddr);
            uint recordSize = GetRecordSize(slotDef.Category);
            CondRecordSize = recordSize;
            IsFE7Extended = (recordSize > 12);

            SlotInfo = $"Slot {slotIndex}: {slotDef.Name} ({slotDef.Category}) Ptr=0x{rawPtr:X08}";

            if (pointerOnly)
            {
                // Single pointer value — show as one "record"
                string name = $"0x{rawPtr:X08}";
                if (U.isPointer(rawPtr))
                    name = $"-> 0x{U.toOffset(rawPtr):X06}";
                else if (rawPtr == 0)
                    name = "(null)";
                result.Add(new AddrResult(slotPointerAddr, name, 0));
                return result;
            }

            // Resolve the pointer to a ROM offset
            if (!U.isPointer(rawPtr))
            {
                if (rawPtr != 0)
                    result.Add(new AddrResult(slotPointerAddr, $"(invalid ptr: 0x{rawPtr:X08})", 0));
                return result;
            }

            uint baseAddr = U.toOffset(rawPtr);
            if (!U.isSafetyOffset(baseAddr))
                return result;

            // Enumerate records until terminator (first byte == 0)
            for (uint i = 0; i < 256; i++)
            {
                uint addr = baseAddr + i * recordSize;
                if (addr + recordSize > (uint)rom.Data.Length)
                    break;

                // Terminator check: first byte == 0 for most types, u32 == 0 for standard
                if (recordSize <= 6)
                {
                    if (rom.u8(addr) == 0) break;
                }
                else if (slotDef.Category == CondCategory.TUTORIAL)
                {
                    uint v = rom.u32(addr);
                    if (v != 1 && !U.isPointer(v)) break;
                }
                else
                {
                    if (rom.u32(addr) == 0) break;
                }

                // Build a display name
                byte type = (byte)rom.u8(addr);
                string typeName = GetCondTypeName(type);
                string name = $"{i:D2}: [{type:X02}] {typeName}";

                // Add extra info based on category
                if (slotDef.Category == CondCategory.TURN)
                {
                    uint turnStart = rom.u8(addr + 8);
                    uint turnEnd = rom.u8(addr + 9);
                    uint phase = rom.u8(addr + 10);
                    string phaseName = phase == 0 ? "Player" : phase == 0x40 ? "Ally" : phase == 0x80 ? "Enemy" : $"0x{phase:X02}";
                    name += $" Turn {turnStart}-{turnEnd} ({phaseName})";
                }
                else if (slotDef.Category == CondCategory.TALK)
                {
                    uint unit1 = rom.u8(addr + 8);
                    uint unit2 = rom.u8(addr + 9);
                    string u1Name = NameResolver.GetUnitName(unit1);
                    string u2Name = NameResolver.GetUnitName(unit2);
                    name += $" {u1Name} <-> {u2Name}";
                }
                else if (slotDef.Category == CondCategory.OBJECT)
                {
                    uint x = rom.u8(addr + 8);
                    uint y = rom.u8(addr + 9);
                    name += $" ({x},{y})";
                }
                else if (slotDef.Category == CondCategory.TRAP)
                {
                    uint x = rom.u8(addr + 1);
                    uint y = rom.u8(addr + 2);
                    name += $" ({x},{y})";
                }

                result.Add(new AddrResult(addr, name, i));
            }

            return result;
        }

        /// <summary>
        /// Load fields from a specific condition record address.
        /// </summary>
        public void LoadCondRecord(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;

            CondRecordAddr = addr;

            if (IsPointerSlot)
            {
                // For pointer-only slots, addr points to the slot in the event data block
                EventPtr = rom.u32(addr);
                CondType = 0;
                SubType = 0;
                FlagId = 0;
                ExtraB8 = ExtraB9 = ExtraB10 = ExtraB11 = 0;
                ExtraB12 = ExtraB13 = ExtraB14 = ExtraB15 = 0;
                CondTypeName = "Event Pointer";
                CanWrite = true;
                return;
            }

            if (addr + CondRecordSize > (uint)rom.Data.Length)
                return;

            if (CondRecordSize <= 6)
            {
                // TRAP records: 6 bytes — B0=type, B1=X, B2=Y, B3=subtype, B4-B5=extra
                CondType = rom.u8(addr + 0);
                SubType = rom.u8(addr + 1);  // X for traps
                FlagId = rom.u8(addr + 2);   // Y for traps (stored in FlagId field)
                EventPtr = rom.u8(addr + 3); // sub-type for traps (stored in EventPtr field)
                ExtraB8 = rom.u8(addr + 4);
                ExtraB9 = rom.u8(addr + 5);
                ExtraB10 = ExtraB11 = 0;
                ExtraB12 = ExtraB13 = ExtraB14 = ExtraB15 = 0;
            }
            else
            {
                // Standard records: B0=type, B1=sub, B2-B3=flag(u16), B4-B7=eventptr(u32), B8+=extra
                CondType = rom.u8(addr + 0);
                SubType = rom.u8(addr + 1);
                FlagId = rom.u16(addr + 2);
                EventPtr = rom.u32(addr + 4);

                if (CondRecordSize >= 12)
                {
                    ExtraB8 = rom.u8(addr + 8);
                    ExtraB9 = rom.u8(addr + 9);
                    ExtraB10 = rom.u8(addr + 10);
                    ExtraB11 = rom.u8(addr + 11);
                }
                else
                {
                    ExtraB8 = ExtraB9 = ExtraB10 = ExtraB11 = 0;
                }

                if (CondRecordSize >= 16)
                {
                    ExtraB12 = rom.u8(addr + 12);
                    ExtraB13 = rom.u8(addr + 13);
                    ExtraB14 = rom.u8(addr + 14);
                    ExtraB15 = rom.u8(addr + 15);
                }
                else
                {
                    ExtraB12 = ExtraB13 = ExtraB14 = ExtraB15 = 0;
                }
            }

            CondTypeName = GetCondTypeName((byte)CondType);
            CanWrite = true;
        }

        /// <summary>
        /// Write the current condition record back to ROM.
        /// </summary>
        public void WriteCondRecord()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CondRecordAddr == 0) return;

            if (IsPointerSlot)
            {
                rom.write_u32(CondRecordAddr, EventPtr);
                return;
            }

            if (CondRecordAddr + CondRecordSize > (uint)rom.Data.Length) return;

            if (CondRecordSize <= 6)
            {
                // TRAP layout: B0=type, B1=X, B2=Y, B3=subtype, B4-B5=extra
                rom.write_u8(CondRecordAddr + 0, (byte)CondType);
                rom.write_u8(CondRecordAddr + 1, (byte)SubType);
                rom.write_u8(CondRecordAddr + 2, (byte)FlagId);
                rom.write_u8(CondRecordAddr + 3, (byte)EventPtr);
                rom.write_u8(CondRecordAddr + 4, (byte)ExtraB8);
                rom.write_u8(CondRecordAddr + 5, (byte)ExtraB9);
            }
            else
            {
                // Standard layout
                rom.write_u8(CondRecordAddr + 0, (byte)CondType);
                rom.write_u8(CondRecordAddr + 1, (byte)SubType);
                rom.write_u16(CondRecordAddr + 2, (ushort)FlagId);
                rom.write_u32(CondRecordAddr + 4, EventPtr);

                if (CondRecordSize >= 12)
                {
                    rom.write_u8(CondRecordAddr + 8, (byte)ExtraB8);
                    rom.write_u8(CondRecordAddr + 9, (byte)ExtraB9);
                    rom.write_u8(CondRecordAddr + 10, (byte)ExtraB10);
                    rom.write_u8(CondRecordAddr + 11, (byte)ExtraB11);
                }

                if (CondRecordSize >= 16)
                {
                    rom.write_u8(CondRecordAddr + 12, (byte)ExtraB12);
                    rom.write_u8(CondRecordAddr + 13, (byte)ExtraB13);
                    rom.write_u8(CondRecordAddr + 14, (byte)ExtraB14);
                    rom.write_u8(CondRecordAddr + 15, (byte)ExtraB15);
                }
            }
        }

        /// <summary>
        /// Get a human-readable name for a condition type code.
        /// </summary>
        public static string GetCondTypeName(byte code)
        {
            return code switch
            {
                0x00 => "END (terminator)",
                0x01 => "ALWAYS / Ballista",
                0x02 => "TURN",
                0x03 => "TALK",
                0x04 => "TALK (ASM) / Damage Floor",
                0x05 => "Seize / House / Poison Gas",
                0x06 => "Visit Village",
                0x07 => "Chest / Arrow of God",
                0x08 => "Door / Fire",
                0x09 => "unknown (09)",
                0x0A => "Shop",
                0x0B => "Range Condition / Mine",
                0x0C => "Gorgon Egg",
                0x0D => "ASM Condition (FE6)",
                0x0E => "ASM Condition (FE7/8)",
                _ => $"unknown (0x{code:X02})",
            };
        }

        /// <summary>
        /// Get contextual field labels based on current slot category.
        /// </summary>
        public (string B0, string B1, string B2B3, string B4B7, string B8, string B9, string B10, string B11) GetFieldLabels()
        {
            if (IsPointerSlot)
                return ("", "", "", "Event Pointer:", "", "", "", "");

            if (_selectedSlotIndex < 0 || _selectedSlotIndex >= _slotDefs.Count)
                return ("Type:", "Sub-type:", "Flag ID:", "Event Pointer:", "B8:", "B9:", "B10:", "B11:");

            var cat = _slotDefs[_selectedSlotIndex].Category;
            return cat switch
            {
                CondCategory.TURN =>
                    ("Type:", "Sub-type:", "Flag ID:", "Event Pointer:", "Turn Start:", "Turn End:", "Phase:", "Unused:"),
                CondCategory.TALK =>
                    ("Type:", "Sub-type:", "Flag ID:", "Event Pointer:", "Unit 1:", "Unit 2:", "Unused:", "Unused:"),
                CondCategory.OBJECT =>
                    ("Type:", "Sub-type:", "Flag ID:", "Event Pointer:", "X:", "Y:", "Obj Type:", "Unused:"),
                CondCategory.ALWAYS =>
                    ("Type:", "Sub-type:", "Flag ID:", "Event Pointer:", "X1 / Param:", "Y1 / Param:", "X2 / Param:", "Y2 / Param:"),
                CondCategory.TRAP =>
                    ("Type:", "X:", "Y:", "Sub-type/Item:", "B4:", "B5:", "", ""),
                _ =>
                    ("Type:", "Sub-type:", "Flag ID:", "Event Pointer:", "B8:", "B9:", "B10:", "B11:"),
            };
        }

        // ------- IDataVerifiable -------

        public int GetListCount() => LoadMapList().Count;

        public Dictionary<string, string> GetDataReport()
        {
            var report = new Dictionary<string, string>
            {
                ["MapSettingAddr"] = $"0x{MapSettingAddr:X08}",
                ["EventDataAddr"] = $"0x{EventDataAddr:X08}",
                ["CondRecordAddr"] = $"0x{CondRecordAddr:X08}",
                ["CondType"] = $"0x{CondType:X02}",
                ["SubType"] = $"0x{SubType:X02}",
                ["FlagId"] = $"0x{FlagId:X04}",
                ["EventPtr"] = $"0x{EventPtr:X08}",
            };
            if (!IsPointerSlot && CondRecordSize >= 12)
            {
                report["ExtraB8"] = $"0x{ExtraB8:X02}";
                report["ExtraB9"] = $"0x{ExtraB9:X02}";
                report["ExtraB10"] = $"0x{ExtraB10:X02}";
                report["ExtraB11"] = $"0x{ExtraB11:X02}";
            }
            return report;
        }

        public Dictionary<string, string> GetRawRomReport()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CondRecordAddr == 0) return new Dictionary<string, string>();

            uint a = CondRecordAddr;
            uint size = IsPointerSlot ? 4 : CondRecordSize;
            if (a + size > (uint)rom.Data.Length)
                return new Dictionary<string, string>();

            var report = new Dictionary<string, string>
            {
                ["Address"] = $"0x{a:X08}",
            };

            if (IsPointerSlot)
            {
                report["u32@0"] = $"0x{rom.u32(a):X08}";
            }
            else if (CondRecordSize <= 6)
            {
                report["u8@0"] = $"0x{rom.u8(a + 0):X02}";
                report["u8@1"] = $"0x{rom.u8(a + 1):X02}";
                report["u8@2"] = $"0x{rom.u8(a + 2):X02}";
                report["u8@3"] = $"0x{rom.u8(a + 3):X02}";
                report["u8@4"] = $"0x{rom.u8(a + 4):X02}";
                report["u8@5"] = $"0x{rom.u8(a + 5):X02}";
            }
            else
            {
                report["u8@0"] = $"0x{rom.u8(a + 0):X02}";
                report["u8@1"] = $"0x{rom.u8(a + 1):X02}";
                report["u16@2"] = $"0x{rom.u16(a + 2):X04}";
                report["u32@4"] = $"0x{rom.u32(a + 4):X08}";
                if (CondRecordSize >= 12)
                {
                    report["u8@8"] = $"0x{rom.u8(a + 8):X02}";
                    report["u8@9"] = $"0x{rom.u8(a + 9):X02}";
                    report["u8@10"] = $"0x{rom.u8(a + 10):X02}";
                    report["u8@11"] = $"0x{rom.u8(a + 11):X02}";
                }
                if (CondRecordSize >= 16)
                {
                    report["u8@12"] = $"0x{rom.u8(a + 12):X02}";
                    report["u8@13"] = $"0x{rom.u8(a + 13):X02}";
                    report["u8@14"] = $"0x{rom.u8(a + 14):X02}";
                    report["u8@15"] = $"0x{rom.u8(a + 15):X02}";
                }
            }

            return report;
        }
    }
}
