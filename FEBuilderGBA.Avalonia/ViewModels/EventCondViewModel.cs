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

    public partial class EventCondViewModel : ViewModelBase, IDataVerifiable
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

        // Top read-config bar
        uint _topAddress;
        uint _readCount;

        // Comment (round-trip via CoreState.CommentCache)
        string _comment = "";

        // Category-specific composite views (derived from B8/B9/B10/B11 etc.,
        // exposed as named properties for direct binding in category panels).
        // These are computed on demand from the generic ExtraB* properties via
        // GetCategoryFields() / SetCategoryFields(), so storing them as
        // independent properties keeps the binding layer clean and matches
        // the EventUnitFE7 pattern for sub-field decomposition.
        uint _turnStart, _turnEnd, _phase;
        uint _unit1, _unit2;
        uint _x1, _y1, _x2, _y2;
        uint _asmFunc;
        uint _itemId, _gold, _durability;
        uint _trapDirection;
        uint _initialTimer, _repeatTimer;
        uint _shopType;
        uint _eventType;
        uint _damageAmount;
        uint _gasDirection;
        uint _duration;
        uint _hatchingStart, _hatchingEnd;
        uint _additionalDecision;
        uint _decisionFlag;

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

        // Top read-config bar properties
        public uint TopAddress { get => _topAddress; set => SetField(ref _topAddress, value); }
        public uint ReadCount { get => _readCount; set => SetField(ref _readCount, value); }

        // Comment property (round-trip via CommentCache)
        public string Comment { get => _comment; set => SetField(ref _comment, value); }

        // Category-specific properties (derived/composite views of the record bytes)
        public uint TurnStart { get => _turnStart; set => SetField(ref _turnStart, value); }
        public uint TurnEnd { get => _turnEnd; set => SetField(ref _turnEnd, value); }
        public uint Phase { get => _phase; set => SetField(ref _phase, value); }
        public uint Unit1 { get => _unit1; set => SetField(ref _unit1, value); }
        public uint Unit2 { get => _unit2; set => SetField(ref _unit2, value); }
        public uint X1 { get => _x1; set => SetField(ref _x1, value); }
        public uint Y1 { get => _y1; set => SetField(ref _y1, value); }
        public uint X2 { get => _x2; set => SetField(ref _x2, value); }
        public uint Y2 { get => _y2; set => SetField(ref _y2, value); }
        public uint AsmFunc { get => _asmFunc; set => SetField(ref _asmFunc, value); }
        public uint ItemId { get => _itemId; set => SetField(ref _itemId, value); }
        public uint Gold { get => _gold; set => SetField(ref _gold, value); }
        public uint Durability { get => _durability; set => SetField(ref _durability, value); }
        public uint TrapDirection { get => _trapDirection; set => SetField(ref _trapDirection, value); }
        public uint InitialTimer { get => _initialTimer; set => SetField(ref _initialTimer, value); }
        public uint RepeatTimer { get => _repeatTimer; set => SetField(ref _repeatTimer, value); }
        public uint ShopType { get => _shopType; set => SetField(ref _shopType, value); }
        public uint EventType { get => _eventType; set => SetField(ref _eventType, value); }
        public uint DamageAmount { get => _damageAmount; set => SetField(ref _damageAmount, value); }
        public uint GasDirection { get => _gasDirection; set => SetField(ref _gasDirection, value); }
        public uint Duration { get => _duration; set => SetField(ref _duration, value); }
        public uint HatchingStart { get => _hatchingStart; set => SetField(ref _hatchingStart, value); }
        public uint HatchingEnd { get => _hatchingEnd; set => SetField(ref _hatchingEnd, value); }
        public uint AdditionalDecision { get => _additionalDecision; set => SetField(ref _additionalDecision, value); }
        public uint DecisionFlag { get => _decisionFlag; set => SetField(ref _decisionFlag, value); }

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

            // Enumerate records. For FE7 TURN records the stride is variable:
            // type==1 advances 12 bytes (FE6/8-shape), other types advance
            // eventcond_tern_size (which is 16 on FE7). Mirror the WinForms
            // rule by computing per-record stride. For other categories the
            // recordSize is uniform.
            uint addrCursor = baseAddr;
            for (uint i = 0; i < 256; i++)
            {
                if (addrCursor + recordSize > (uint)rom.Data.Length)
                    break;

                // Terminator check (must come FIRST so we don't read past list).
                // Order: TUTORIAL u32-based stop > standard u32 stop > byte-only.
                if (slotDef.Category == CondCategory.TUTORIAL)
                {
                    // TUTORIAL: 4-byte u32. Stop on u32 != 1 AND NOT isPointer
                    // (per WF InitTutorial). MUST check this BEFORE the
                    // byte-only branch — a valid pointer like 0x08000100 has
                    // low byte 0x00 which would falsely terminate a byte scan.
                    uint v = rom.u32(addrCursor);
                    if (v != 1 && !U.isPointer(v)) break;
                }
                else if (recordSize <= 6)
                {
                    // TRAP-style 6-byte records: stop on first-byte == 0.
                    if (rom.u8(addrCursor) == 0) break;
                }
                else
                {
                    // Standard records: stop on u32 == 0 (covers 12-byte +
                    // 16-byte FE7 records uniformly because the first u32 is
                    // type/sub/flag composite).
                    if (rom.u32(addrCursor) == 0) break;
                }

                // Build a display name
                byte type = (byte)rom.u8(addrCursor);
                string typeName = GetCondTypeName(type);
                string name = $"{i:D2}: [{type:X02}] {typeName}";

                // Add extra info based on category
                if (slotDef.Category == CondCategory.TURN)
                {
                    uint turnStart = rom.u8(addrCursor + 8);
                    uint turnEnd = rom.u8(addrCursor + 9);
                    uint phase = rom.u8(addrCursor + 10);
                    string phaseName = phase == 0 ? "Player" : phase == 0x40 ? "Ally" : phase == 0x80 ? "Enemy" : $"0x{phase:X02}";
                    name += $" Turn {turnStart}-{turnEnd} ({phaseName})";
                }
                else if (slotDef.Category == CondCategory.TALK)
                {
                    uint unit1 = rom.u8(addrCursor + 8);
                    uint unit2 = rom.u8(addrCursor + 9);
                    string u1Name = NameResolver.GetUnitName(unit1);
                    string u2Name = NameResolver.GetUnitName(unit2);
                    name += $" {u1Name} <-> {u2Name}";
                }
                else if (slotDef.Category == CondCategory.OBJECT)
                {
                    uint x = rom.u8(addrCursor + 8);
                    uint y = rom.u8(addrCursor + 9);
                    name += $" ({x},{y})";
                }
                else if (slotDef.Category == CondCategory.TRAP)
                {
                    uint x = rom.u8(addrCursor + 1);
                    uint y = rom.u8(addrCursor + 2);
                    name += $" ({x},{y})";
                }

                result.Add(new AddrResult(addrCursor, name, i));

                // Advance cursor by per-record stride (handles FE7 variable-
                // length TURN records: type==1 -> 12 bytes; else recordSize).
                uint stride = GetRecordStrideAt(slotDef.Category, recordSize, type);
                addrCursor += stride;
            }

            return result;
        }

        /// <summary>
        /// Per-record stride for variable-length record types. For FE7 TURN
        /// records, type==1 advances 12 bytes (FE6/8-shape) while other
        /// types advance the full recordSize (16). For other categories,
        /// stride equals recordSize uniformly.
        /// </summary>
        static uint GetRecordStrideAt(CondCategory cat, uint recordSize, byte type)
        {
            // FE7-extended TURN records: when recordSize is 16 and the type
            // byte is 1, the row is the smaller 12-byte shape (mirrors WF
            // EventCondInnerControl per-type stride). All other rows in an
            // FE7 TURN list use the full 16-byte stride.
            if (cat == CondCategory.TURN && recordSize == 16 && type == 1)
                return 12;
            return recordSize;
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

            if (CondRecordSize == 4)
            {
                // TUTORIAL records: 4 bytes — single u32. WinForms reads this
                // as `rom.u32(addr + 0)` (TUTORIAL_P0). Stop condition is u32 != 1
                // and NOT isPointer. We surface the u32 as EventPtr so the user
                // can edit either the special-value 1 or a pointer.
                CondType = 0;
                SubType = 0;
                FlagId = 0;
                EventPtr = rom.u32(addr + 0);
                ExtraB8 = ExtraB9 = ExtraB10 = ExtraB11 = 0;
                ExtraB12 = ExtraB13 = ExtraB14 = ExtraB15 = 0;
            }
            else if (CondRecordSize <= 6)
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

            // Decompose category-specific composite views from the generic bytes.
            DecomposeCategoryFields();

            // Top read-config bar reflects the current event-data context.
            TopAddress = EventDataAddr;
            ReadCount = (uint)_slotDefs.Count;

            // Pull the comment from the cache (round-trip via HeadlessEtcCache).
            Comment = CoreState.CommentCache?.At(addr) ?? "";
        }

        /// <summary>
        /// Decompose the generic ExtraB* bytes into category-specific composite
        /// views (TurnStart, Unit1, X/Y, etc.) so the per-category sub-panels
        /// can bind directly.
        /// </summary>
        void DecomposeCategoryFields()
        {
            if (_selectedSlotIndex < 0 || _selectedSlotIndex >= _slotDefs.Count)
                return;

            var cat = _slotDefs[_selectedSlotIndex].Category;
            switch (cat)
            {
                case CondCategory.TURN:
                    TurnStart = _extraB8;
                    TurnEnd = _extraB9;
                    Phase = _extraB10;
                    break;
                case CondCategory.TALK:
                    Unit1 = _extraB8;
                    Unit2 = _extraB9;
                    if (_isFE7Extended)
                    {
                        AdditionalDecision = _extraB12;
                        DecisionFlag = _extraB13;
                        AsmFunc = _eventPtr; // for N04 ASM talk
                    }
                    break;
                case CondCategory.OBJECT:
                    X1 = _extraB8;
                    Y1 = _extraB9;
                    EventType = _extraB10;
                    // For N07 (chest): WinForms layout is B4=item, B5=durability,
                    // W6=gold (u16). The full u32 at offset +4 (which we
                    // store as _eventPtr) packs them as:
                    //   item    (B4) = _eventPtr & 0xFF
                    //   durability (B5) = (_eventPtr >> 8) & 0xFF
                    //   gold    (W6) = (_eventPtr >> 16) & 0xFFFF
                    ItemId = _condType == 0x07 ? _eventPtr & 0xFF : 0;
                    Durability = _condType == 0x07 ? (_eventPtr >> 8) & 0xFF : 0;
                    Gold = _condType == 0x07 ? (_eventPtr >> 16) & 0xFFFF : 0;
                    ShopType = _condType == 0x0A ? _extraB10 : 0;
                    break;
                case CondCategory.ALWAYS:
                    X1 = _extraB8;
                    Y1 = _extraB9;
                    X2 = _extraB10;
                    Y2 = _extraB11;
                    if (_condType == 0x0D || _condType == 0x0E)
                    {
                        AsmFunc = _eventPtr;
                    }
                    break;
                case CondCategory.TRAP:
                    // TRAP records: 6 bytes — B0=type, B1=X, B2=Y, B3=sub, B4-5=extra
                    X1 = _subType;
                    Y1 = _flagId;
                    TrapDirection = _extraB8;
                    Durability = _extraB9;
                    // Trap-type-specific: damage/gas/duration/hatching share the
                    // B4-B5 bytes; mapped per-category for binding clarity.
                    DamageAmount = _condType == 0x04 ? _extraB8 : 0;
                    GasDirection = _condType == 0x05 ? _extraB8 : 0;
                    Duration = _condType == 0x08 ? _extraB9 : 0;
                    HatchingStart = _condType == 0x0C ? _extraB8 : 0;
                    HatchingEnd = _condType == 0x0C ? _extraB9 : 0;
                    ItemId = (_condType == 0x0B) ? _eventPtr & 0xFF : 0;
                    break;
                case CondCategory.TUTORIAL:
                    // TUTORIAL: single u32 (TUTORIAL_P0) = either 1 or an event pointer.
                    // No InitialTimer / RepeatTimer fields — these are WF
                    // EventCondInnerControl artifacts that don't exist in the
                    // 4-byte record. We expose the raw u32 via EventPtr; the
                    // tutorial sub-panel labels InitialTimer/RepeatTimer are
                    // out-of-scope visual hints (mirroring the WF tab title
                    // structure, not the byte layout).
                    InitialTimer = 0;
                    RepeatTimer = 0;
                    break;
            }
        }

        /// <summary>
        /// Compose category-specific values back into generic bytes before write.
        /// </summary>
        void ComposeCategoryFields()
        {
            if (_selectedSlotIndex < 0 || _selectedSlotIndex >= _slotDefs.Count)
                return;

            var cat = _slotDefs[_selectedSlotIndex].Category;
            switch (cat)
            {
                case CondCategory.TURN:
                    _extraB8 = TurnStart;
                    _extraB9 = TurnEnd;
                    _extraB10 = Phase;
                    break;
                case CondCategory.TALK:
                    _extraB8 = Unit1;
                    _extraB9 = Unit2;
                    if (_isFE7Extended)
                    {
                        _extraB12 = AdditionalDecision;
                        _extraB13 = DecisionFlag;
                    }
                    break;
                case CondCategory.OBJECT:
                    _extraB8 = X1;
                    _extraB9 = Y1;
                    _extraB10 = EventType;
                    if (_condType == 0x07)
                    {
                        // Chest: B4=item, B5=durability, W6=gold (u16).
                        _eventPtr = (ItemId & 0xFF) | ((Durability & 0xFF) << 8) | ((Gold & 0xFFFF) << 16);
                    }
                    else if (_condType == 0x0A)
                    {
                        _extraB10 = ShopType;
                    }
                    break;
                case CondCategory.ALWAYS:
                    _extraB8 = X1;
                    _extraB9 = Y1;
                    _extraB10 = X2;
                    _extraB11 = Y2;
                    if (_condType == 0x0D || _condType == 0x0E)
                    {
                        _eventPtr = AsmFunc;
                    }
                    break;
                case CondCategory.TRAP:
                    _subType = X1;
                    _flagId = Y1;
                    _extraB8 = TrapDirection;
                    _extraB9 = Durability;
                    if (_condType == 0x04) _extraB8 = DamageAmount;
                    else if (_condType == 0x05) _extraB8 = GasDirection;
                    else if (_condType == 0x08) _extraB9 = Duration;
                    else if (_condType == 0x0C) { _extraB8 = HatchingStart; _extraB9 = HatchingEnd; }
                    else if (_condType == 0x0B) _eventPtr = ItemId;
                    break;
                case CondCategory.TUTORIAL:
                    // TUTORIAL: single u32 (TUTORIAL_P0). The raw u32 is in
                    // _eventPtr; nothing else to compose (CondType/SubType
                    // are not stored for TUTORIAL records).
                    break;
            }
        }

        /// <summary>
        /// Write the current condition record back to ROM. Requires an active
        /// undo scope (via UndoService.Begin or ROM.BeginUndoScope); throws
        /// InvalidOperationException otherwise. The fail-fast enforcement
        /// guarantees no ROM mutation slips through without undo tracking.
        /// </summary>
        public void WriteCondRecord()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CondRecordAddr == 0) return;

            // Undo enforcement: throw if no ambient scope is active.
            if (!ROM.IsAmbientUndoScopeActive)
            {
                throw new InvalidOperationException(
                    "EventCondViewModel.WriteCondRecord requires an active undo scope " +
                    "(call UndoService.Begin or ROM.BeginUndoScope before invoking).");
            }

            // Compose category-specific composite views back into generic bytes.
            ComposeCategoryFields();

            if (IsPointerSlot)
            {
                rom.write_u32(CondRecordAddr, EventPtr);
                return;
            }

            if (CondRecordAddr + CondRecordSize > (uint)rom.Data.Length) return;

            if (CondRecordSize == 4)
            {
                // TUTORIAL records: 4 bytes — single u32 (TUTORIAL_P0). Mirrors
                // WinForms `rom.write_u32(addr, value)` where value is either
                // the special-value 1 or an event pointer. Must NOT write bytes
                // 4-5 because that would corrupt the next record.
                rom.write_u32(CondRecordAddr + 0, EventPtr);
            }
            else if (CondRecordSize <= 6)
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

                // FE7 variable-length TURN records: type==1 advances 12 bytes,
                // other types advance the full 16. Writing B12-B15 for a
                // type==1 row would clobber the next record's first 4 bytes
                // (Copilot CLI review round 2 #3).
                bool isFe7TurnType1 = (CondRecordSize == 16) &&
                                       _selectedSlotIndex >= 0 &&
                                       _selectedSlotIndex < _slotDefs.Count &&
                                       _slotDefs[_selectedSlotIndex].Category == CondCategory.TURN &&
                                       CondType == 1;

                if (CondRecordSize >= 16 && !isFe7TurnType1)
                {
                    rom.write_u8(CondRecordAddr + 12, (byte)ExtraB12);
                    rom.write_u8(CondRecordAddr + 13, (byte)ExtraB13);
                    rom.write_u8(CondRecordAddr + 14, (byte)ExtraB14);
                    rom.write_u8(CondRecordAddr + 15, (byte)ExtraB15);
                }
            }
        }

        /// <summary>
        /// Expand the record list for the currently-selected slot. Returns
        /// the new base pointer of the expanded list (in GBA-pointer form).
        /// Requires an active undo scope; throws otherwise.
        /// </summary>
        public uint ExpandRecordList()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || _eventDataAddr == 0 || _selectedSlotIndex < 0 ||
                _selectedSlotIndex >= _slotDefs.Count) return 0;

            if (!ROM.IsAmbientUndoScopeActive)
            {
                throw new InvalidOperationException(
                    "EventCondViewModel.ExpandRecordList requires an active undo scope.");
            }

            var slotDef = _slotDefs[_selectedSlotIndex];
            uint recordSize = GetRecordSize(slotDef.Category);

            // Pointer-only slots can't be expanded — they're a single pointer.
            if (IsCategoryPointerOnly(slotDef.Category))
                return 0;

            // Read the current list pointer.
            uint slotPointerAddr = _eventDataAddr + (uint)(_selectedSlotIndex * 4);
            uint rawPtr = rom.u32(slotPointerAddr);
            if (!U.isPointer(rawPtr)) return 0;

            uint baseAddr = U.toOffset(rawPtr);

            // Count current records (terminator detection — must match
            // LoadConditionRecords stop conditions exactly so Expand creates
            // a buffer the same scanner will iterate).
            uint count = 0;
            for (uint i = 0; i < 256; i++)
            {
                uint addr = baseAddr + i * recordSize;
                if (addr + recordSize > (uint)rom.Data.Length) break;

                if (slotDef.Category == CondCategory.TUTORIAL)
                {
                    // TUTORIAL stop: u32 != 1 and NOT isPointer (per WF InitTutorial).
                    uint v = rom.u32(addr);
                    if (v != 1 && !U.isPointer(v)) break;
                }
                else if (recordSize <= 6)
                {
                    if (rom.u8(addr) == 0) break;
                }
                else
                {
                    if (rom.u32(addr) == 0) break;
                }
                count++;
            }

            // Build a new buffer with one extra slot + terminator.
            uint newCount = count + 1;
            uint totalSize = (newCount + 1) * recordSize; // +1 for terminator
            byte[] buffer = new byte[totalSize];

            // Copy existing records.
            for (uint i = 0; i < count; i++)
            {
                uint srcAddr = baseAddr + i * recordSize;
                for (uint j = 0; j < recordSize; j++)
                {
                    if (srcAddr + j < (uint)rom.Data.Length)
                        buffer[i * recordSize + j] = (byte)rom.u8(srcAddr + j);
                }
            }

            // Initialize the NEW slot with category-appropriate non-terminator
            // data so the scanner sees it as a real row (Copilot CLI review #3).
            // - TUTORIAL: write u32 = 1 (the special "blank" value per WF
            //   AddressListExpandsEventTutorial; isPointer would also work but
            //   1 is the canonical "no event yet" marker).
            // - Other categories: write a placeholder type byte = 1 so the
            //   scanner doesn't see byte 0 as terminator (TURN/TALK/OBJECT/etc.
            //   all stop on first-byte == 0 OR u32 == 0). The user can change
            //   the type after the row is visible.
            uint newSlotOffset = count * recordSize;
            if (slotDef.Category == CondCategory.TUTORIAL)
            {
                // u32 = 1 in little-endian.
                buffer[newSlotOffset + 0] = 1;
                buffer[newSlotOffset + 1] = 0;
                buffer[newSlotOffset + 2] = 0;
                buffer[newSlotOffset + 3] = 0;
            }
            else
            {
                // Placeholder type byte at offset 0 so the row is non-terminator.
                buffer[newSlotOffset + 0] = 1;
            }

            // Terminator stays zeroed at offset = newCount * recordSize.

            // Append the buffer to ROM end (mirrors WF InputFormRef.AppendBinaryData).
            uint newAddr = AppendBinaryDataHeadless(rom, buffer);
            if (newAddr == U.NOT_FOUND) return 0;

            // Update the slot pointer to point at the new list.
            rom.write_u32(slotPointerAddr, U.toPointer(newAddr));

            return U.toPointer(newAddr);
        }

        /// <summary>
        /// Allocate a new event block (stub: just an END byte at the freshly
        /// appended address). Returns the new event pointer (in GBA-pointer
        /// form) so the caller can write it into the relevant slot.
        /// Requires an active undo scope; throws otherwise.
        /// </summary>
        public uint AllocateNewEvent()
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return 0;

            if (!ROM.IsAmbientUndoScopeActive)
            {
                throw new InvalidOperationException(
                    "EventCondViewModel.AllocateNewEvent requires an active undo scope.");
            }

            // Allocate a minimal stub event: 4 bytes of END (0x00 terminator on FE6/8,
            // 0x0A 0x00 0x00 0x00 = NOP+END for FE7). Just 4 zero bytes work as a
            // safe default — same shape WF uses for "blank event allocation".
            byte[] stub = new byte[4];
            uint newAddr = AppendBinaryDataHeadless(rom, stub);
            if (newAddr == U.NOT_FOUND) return 0;

            return U.toPointer(newAddr);
        }

        /// <summary>
        /// Update the comment for the currently-selected record via
        /// CoreState.CommentCache. Requires an active undo scope so any
        /// downstream ROM commit (e.g. comment cache persistence side-effects)
        /// is tracked. The comment cache itself is in-memory and persisted
        /// separately, but we enforce the scope to keep all VM mutations
        /// fail-fast against the same undo discipline.
        /// </summary>
        public void UpdateComment(string value)
        {
            if (!ROM.IsAmbientUndoScopeActive)
            {
                throw new InvalidOperationException(
                    "EventCondViewModel.UpdateComment requires an active undo scope.");
            }

            Comment = value ?? "";
            if (CondRecordAddr == 0) return;

            CoreState.CommentCache?.Update(CondRecordAddr, Comment);
        }

        /// <summary>
        /// Headless equivalent of InputFormRef.AppendBinaryData. Routes through
        /// the registered CoreState.AppendBinaryData delegate when available
        /// (WinForms registers it via InputFormRef); returns U.NOT_FOUND when
        /// no allocator is wired (e.g. in headless tests).
        /// </summary>
        static uint AppendBinaryDataHeadless(ROM rom, byte[] buffer)
        {
            // WinForms wires the real allocator via CoreState.AppendBinaryData;
            // the Avalonia editor relies on the same delegate (resolved through
            // ROM-end appending or freespace search depending on what's
            // registered). When the delegate isn't wired (headless tests),
            // return U.NOT_FOUND so callers handle the gracefully.
            var allocator = CoreState.AppendBinaryData;
            if (allocator == null) return U.NOT_FOUND;

            // We need an UndoData to satisfy the signature; we recover the
            // ambient one (the caller is required to be in an undo scope per
            // the throw-if-not-active enforcement).
            var ambient = ROM.GetAmbientUndoData();
            if (ambient == null) return U.NOT_FOUND;

            return allocator(buffer, ambient);
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
                ["addr"] = $"0x{CondRecordAddr:X08}",
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
                ["addr"] = $"0x{a:X08}",
            };

            if (IsPointerSlot)
            {
                report["u32@0"] = $"0x{rom.u32(a):X08}";
            }
            else if (CondRecordSize == 4)
            {
                // TUTORIAL: 4-byte u32 record (single TUTORIAL_P0 field).
                // Reporting bytes 0-5 would read past the record (Copilot CLI
                // review round 2 #2).
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
