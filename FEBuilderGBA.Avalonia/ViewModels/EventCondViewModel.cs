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
        uint _selectedMapId = U.NOT_FOUND; // map-id (AddrResult.tag) of the selected map; NOT_FOUND ⇒ no/invalid map (#1592)
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
        // TRAP B3 subtype (Ballista type / Vein effect / Item id) — mapped
        // to the byte at offset +3 in the 6-byte TRAP record. Distinct from
        // _subType which holds the B1 (X coordinate) byte for TRAP.
        uint _trapSubType;

        string _condTypeName = "";
        string _slotInfo = "";
        bool _isFE7Extended;
        bool _isPointerSlot; // PLAYER_UNIT, ENEMY_UNIT, etc. — just a single pointer

        // Static slot definitions loaded from config
        static List<CondSlotDef> _slotDefs = new();

        public uint MapSettingAddr { get => _mapSettingAddr; set => SetField(ref _mapSettingAddr, value); }
        /// <summary>The selected map's id (AddrResult.tag from MakeMapIDList).
        /// <see cref="U.NOT_FOUND"/> when no map (or an unrecognised addr) is
        /// selected — the Alloc-Event CALL_EndEvent path refuses on NOT_FOUND
        /// (#1592 finding #4: no wrapped/garbage map id reaches ResolveEndEvent).</summary>
        public uint SelectedMapId { get => _selectedMapId; set => SetField(ref _selectedMapId, value); }
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

        /// <summary>
        /// Effective byte stride of the currently-loaded record. For FE7 TURN
        /// type==1 rows this is 12 (vs CondRecordSize=16 for the slot); for
        /// everything else it equals CondRecordSize. Used by the View's raw
        /// hex dump and by GetRawRomReport so they don't read past the
        /// record into the next one (Copilot round 7 #2).
        /// </summary>
        public uint EffectiveRecordSize
        {
            get
            {
                if (_selectedSlotIndex >= 0 && _selectedSlotIndex < _slotDefs.Count)
                {
                    var cat = _slotDefs[_selectedSlotIndex].Category;
                    if (cat == CondCategory.TURN && _condRecordSize == 16 && _condType == 1)
                        return 12;
                }
                return _condRecordSize;
            }
        }

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
        public uint TrapSubType { get => _trapSubType; set => SetField(ref _trapSubType, value); }

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
        /// Resolve the map-id (AddrResult.tag) for a map-setting address by
        /// matching it against MakeMapIDList. Returns <see cref="U.NOT_FOUND"/>
        /// when the address isn't a recognised map (#1592 finding #4).
        /// </summary>
        static uint ResolveMapIdForAddr(uint mapSettingAddr)
        {
            foreach (var m in MapSettingCore.MakeMapIDList())
            {
                if (m.addr == mapSettingAddr) return m.tag;
            }
            return U.NOT_FOUND;
        }

        /// <summary>
        /// Map the Avalonia <see cref="CondCategory"/> of the selected slot onto
        /// the Core <see cref="MapEventUnitCore.CondType"/> so the shared
        /// <see cref="EventEditorHostContext.IsEventPointerSurface"/> gate can
        /// decide whether the record's +4 field is a genuine event pointer.
        /// </summary>
        static MapEventUnitCore.CondType ToCoreCondType(CondCategory cat)
        {
            switch (cat)
            {
                case CondCategory.TURN: return MapEventUnitCore.CondType.Turn;
                case CondCategory.TALK: return MapEventUnitCore.CondType.Talk;
                case CondCategory.OBJECT: return MapEventUnitCore.CondType.Object;
                case CondCategory.ALWAYS: return MapEventUnitCore.CondType.Always;
                case CondCategory.TUTORIAL: return MapEventUnitCore.CondType.Tutorial;
                case CondCategory.TRAP: return MapEventUnitCore.CondType.Trap;
                case CondCategory.PLAYER_UNIT: return MapEventUnitCore.CondType.PlayerUnit;
                case CondCategory.ENEMY_UNIT: return MapEventUnitCore.CondType.EnemyUnit;
                case CondCategory.FREEMAP_PLAYER_UNIT: return MapEventUnitCore.CondType.FreemapPlayerUnit;
                case CondCategory.FREEMAP_ENEMY_UNIT: return MapEventUnitCore.CondType.FreemapEnemyUnit;
                case CondCategory.START_EVENT: return MapEventUnitCore.CondType.StartEvent;
                case CondCategory.END_EVENT: return MapEventUnitCore.CondType.EndEvent;
                default: return MapEventUnitCore.CondType.Unknown;
            }
        }

        /// <summary>
        /// True when the currently-loaded record is a WinForms NEWALLOC-EVENT
        /// surface (its +4 field is a real top-level event pointer), so the
        /// Alloc-Event CALL_EndEvent / CALL_1 buttons may write it (#1592
        /// finding #3). False for pointer-only / TRAP / TUTORIAL slots and for
        /// OBJECT chest/shop subtypes that pack +4 with non-pointer data.
        /// </summary>
        public bool CanAllocCallTemplate
        {
            get
            {
                if (IsPointerSlot) return false;
                if (_selectedSlotIndex < 0 || _selectedSlotIndex >= _slotDefs.Count) return false;
                var cat = _slotDefs[_selectedSlotIndex].Category;
                return EventEditorHostContext.IsEventPointerSurface(ToCoreCondType(cat), _condType);
            }
        }

        /// <summary>
        /// True when the currently-loaded record is the TURN counter-
        /// reinforcement surface (EVENT3 / Template-3). The B8/B9 counter
        /// fields are the TURN record's TurnStart/TurnEnd bytes (#1592
        /// finding #2 — written via the category properties so
        /// ComposeCategoryFields persists them).
        /// </summary>
        public bool CanAllocCounterReinforcement
        {
            get
            {
                if (!CanAllocCallTemplate) return false;
                if (_selectedSlotIndex < 0 || _selectedSlotIndex >= _slotDefs.Count) return false;
                return _slotDefs[_selectedSlotIndex].Category == CondCategory.TURN;
            }
        }

        /// <summary>
        /// Apply a numbered CALL button's record side effects (#1592). Resolves
        /// the side effects via <see cref="EventEditorHostContext.ResolveCallTemplate"/>,
        /// applies the event-pointer + W2 (victory flag) record fields, and
        /// commits via <see cref="WriteCondRecord"/>. Requires an active undo
        /// scope (WriteCondRecord throws otherwise). Returns false WITHOUT
        /// mutating when the choice can't be resolved (no map / no end-event)
        /// or the record isn't an event-pointer surface — the caller refuses.
        /// </summary>
        public bool ApplyAllocCallTemplate(EventEditorHostContext.AllocTemplateChoice choice)
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CondRecordAddr == 0) return false;
            if (!CanAllocCallTemplate) return false;

            var eff = EventEditorHostContext.ResolveCallTemplate(rom, _selectedMapId, choice);
            if (!eff.Resolvable || !eff.HasEventPtr) return false;

            // Event-pointer field (+4 u32). WinForms writes the literal 1 for
            // CALL_1 and U.toPointer(addr) for CALL_EndEvent — ResolveCallTemplate
            // already produced the exact value.
            EventPtr = eff.EventPtr;

            // W2 (victory flag) = u16 @ +2 = FlagId. Set to 0x03 when needed,
            // else clear ONLY when it is currently exactly 0x03 (WF rule).
            if (eff.SetFlag03)
            {
                FlagId = 0x03;
            }
            else if (FlagId == 0x03)
            {
                FlagId = 0;
            }

            WriteCondRecord();
            return true;
        }

        /// <summary>
        /// Apply the counter-reinforcement record side effects (#1592): allocate
        /// a counter-reinforcement event block, point the TURN record's
        /// event-pointer field at it, and set B8=1 / B9=255 (the TURN record's
        /// TurnStart/TurnEnd). Requires an active undo scope. Returns false
        /// without mutating when the record isn't the TURN counter surface or
        /// allocation fails.
        /// </summary>
        public bool ApplyCounterReinforcement()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CondRecordAddr == 0) return false;
            if (!CanAllocCounterReinforcement) return false;

            // Allocate a fresh event block for the counter-reinforcement event
            // (the same minimal toplevel-code stub AllocateNewEvent uses).
            uint newPtr = AllocateNewEvent();
            if (newPtr == 0) return false;

            var eff = EventEditorHostContext.CounterReinforcementSideEffects();
            if (!eff.Resolvable || !eff.CounterReinforcement) return false;

            // Point the record at the new counter event.
            EventPtr = newPtr;

            // B8=1 / B9=255 are the TURN record's TurnStart/TurnEnd. Writing the
            // category properties (not the raw ExtraB8/B9) so ComposeCategoryFields
            // persists them (finding #2). TURN guarantees CondRecordSize >= 12.
            TurnStart = 1;
            TurnEnd = 255;

            WriteCondRecord();
            return true;
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

            // Resolve the map-id (AddrResult.tag) for the selected map setting
            // address so the Alloc-Event CALL_EndEvent path can resolve the
            // chapter end-event. NOT_FOUND when the addr isn't a known map
            // (#1592 finding #4 — refuse rather than feed a garbage id).
            SelectedMapId = ResolveMapIdForAddr(mapSettingAddr);

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

                // Build a display name. For TUTORIAL the row has no meaningful
                // type byte — it's a single u32 (either 1 or an event pointer),
                // so GetCondTypeName(type) would mislabel rows (Copilot bot
                // review on round-3). Show the u32 value directly instead.
                byte type = (byte)rom.u8(addrCursor);
                string name;
                if (slotDef.Category == CondCategory.TUTORIAL)
                {
                    uint v = rom.u32(addrCursor);
                    string vDesc = v == 1 ? "(blank)" : $"-> 0x{U.toOffset(v):X06}";
                    name = $"{i:D2}: TUTORIAL u32=0x{v:X08} {vDesc}";
                }
                else
                {
                    string typeName = GetCondTypeName(type);
                    name = $"{i:D2}: [{type:X02}] {typeName}";
                }

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
                    // 1-based ROM-stored unit IDs.
                    string u1Name = NameResolver.GetUnitNameByOneBasedId(unit1);
                    string u2Name = NameResolver.GetUnitNameByOneBasedId(unit2);
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
        /// WF GetDefaultEventType: the type byte to initialize a freshly
        /// allocated record with so it's visible (non-terminator) and
        /// semantically valid. Mirrors WF EventCondForm.GetDefaultEventType().
        /// TURN=2, TALK=3 (FE6=4), OBJECT=5, ALWAYS=1, TRAP=1.
        /// (Copilot round 5 review #3.)
        /// </summary>
        static uint GetDefaultEventType(CondCategory cat, ROM rom)
        {
            switch (cat)
            {
                case CondCategory.TURN: return 2;
                case CondCategory.TALK:
                    return (rom?.RomInfo?.version == 6) ? 4u : 3u;
                case CondCategory.OBJECT: return 5;
                case CondCategory.ALWAYS: return 1;
                case CondCategory.TRAP: return 1;
                default: return 1;
            }
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

                // Reset composite fields + load comment so the pointer-only
                // path doesn't show stale data from a previously-selected
                // record (Copilot bot review on round-3).
                ClearCompositeFields();
                Comment = CoreState.CommentCache?.At(addr) ?? "";
                TopAddress = EventDataAddr;
                ReadCount = (uint)_slotDefs.Count;
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

                // FE7 variable-length TURN: type==1 rows are 12 bytes, not 16,
                // so B12-B15 belong to the NEXT record. Reading them would
                // display bytes from the neighbor and tempt the user to edit
                // values that are silently discarded by the write guard.
                // Skip the B12-B15 read when in this case (Copilot bot review
                // on round-3). Note: we only have access to the per-record
                // type byte here (CondType which was just set), and the
                // selected slot category — sufficient to detect the FE7
                // type-1 case.
                bool isFe7TurnType1 = (CondRecordSize == 16) &&
                                      _selectedSlotIndex >= 0 &&
                                      _selectedSlotIndex < _slotDefs.Count &&
                                      _slotDefs[_selectedSlotIndex].Category == CondCategory.TURN &&
                                      CondType == 1;

                if (CondRecordSize >= 16 && !isFe7TurnType1)
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

                // Recompute IsFE7Extended per selected record so it doesn't
                // stick after a FE7 TURN type-1 row is selected (Copilot
                // round 7 #1). The View uses this flag to show B12-B15 and
                // the Write_Click handler only copies those controls back
                // when true — so a stale `false` from a previous type-1
                // selection would hide valid B12-B15 fields for a later
                // 16-byte FE7 TURN row.
                IsFE7Extended = (CondRecordSize >= 16) && !isFe7TurnType1;
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
        /// <summary>
        /// Zero all composite category-specific fields. Called from the
        /// pointer-only LoadCondRecord path so the previous record's TURN/
        /// TALK/OBJECT/etc values don't leak into the UI when switching to
        /// a pointer-only slot (Copilot bot review on round-3).
        /// </summary>
        void ClearCompositeFields()
        {
            TurnStart = TurnEnd = Phase = 0;
            Unit1 = Unit2 = 0;
            X1 = Y1 = X2 = Y2 = 0;
            AsmFunc = 0;
            ItemId = Gold = Durability = 0;
            TrapSubType = 0;
            TrapDirection = 0;
            InitialTimer = RepeatTimer = 0;
            ShopType = 0;
            EventType = 0;
            DamageAmount = 0;
            GasDirection = 0;
            Duration = 0;
            HatchingStart = HatchingEnd = 0;
            AdditionalDecision = 0;
            DecisionFlag = 0;
        }

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
                    // WF byte layout per InputFormRef control naming
                    // (W = u16, P = u32) — corrected from Copilot round 5:
                    //  - TALK_N03_W12 (u16 @ +12) = AdditionalDecision
                    //  - TALK_N03_W14 (u16 @ +14) = DecisionFlag
                    //  - TALK_N04_P12 (u32 @ +12) = ASM pointer (FE7/8, size 16)
                    //  - TALKFE6_N0D_P8 (u32 @ +8) = ASM pointer (FE6, size 12)
                    // In our generic byte storage:
                    //  - W12 (u16) = _extraB12 | (_extraB13 << 8)
                    //  - W14 (u16) = _extraB14 | (_extraB15 << 8)
                    //  - P12 (u32) = _extraB12 | (_extraB13 << 8) | (_extraB14 << 16) | (_extraB15 << 24)
                    //  - P8  (u32) = _extraB8  | (_extraB9 << 8)  | (_extraB10 << 16) | (_extraB11 << 24)
                    if (_condType == 0x04 && _isFE7Extended)
                    {
                        // FE7/8 TALK N04 ASM Talk: ASM pointer at +12 (u32).
                        AsmFunc = _extraB12 | (_extraB13 << 8) | (_extraB14 << 16) | (_extraB15 << 24);
                        AdditionalDecision = 0;
                        DecisionFlag = 0;
                    }
                    else if (_condType == 0x0D)
                    {
                        // FE6 TALK N0D ASM Talk (size 12): ASM pointer at +8 (u32).
                        AsmFunc = _extraB8 | (_extraB9 << 8) | (_extraB10 << 16) | (_extraB11 << 24);
                        AdditionalDecision = 0;
                        DecisionFlag = 0;
                    }
                    else if (_isFE7Extended)
                    {
                        // FE7/8 TALK N03: W12 (u16) = AdditionalDecision, W14 (u16) = DecisionFlag.
                        AdditionalDecision = _extraB12 | (_extraB13 << 8);
                        DecisionFlag = _extraB14 | (_extraB15 << 8);
                        AsmFunc = 0;
                    }
                    else
                    {
                        AdditionalDecision = 0;
                        DecisionFlag = 0;
                        AsmFunc = 0;
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
                    // WF ALWAYS layout: P4 = event pointer (at +4, _eventPtr),
                    // P8 = ASM pointer (at +8, u32 from B8-B11). For range
                    // conditions (CondType 0x0B), B8-B11 hold X1/Y1/X2/Y2.
                    // Round 5 fix: distinguish ASM (0x0D/0x0E) from range (0x0B).
                    if (_condType == 0x0D || _condType == 0x0E)
                    {
                        // ASM condition: B8-B11 = u32 ASM pointer (P8).
                        AsmFunc = _extraB8 | (_extraB9 << 8) | (_extraB10 << 16) | (_extraB11 << 24);
                        X1 = Y1 = X2 = Y2 = 0;
                    }
                    else
                    {
                        // Range condition (0x0B) or always (0x01): B8-B11 = X1/Y1/X2/Y2.
                        X1 = _extraB8;
                        Y1 = _extraB9;
                        X2 = _extraB10;
                        Y2 = _extraB11;
                        AsmFunc = 0;
                    }
                    break;
                case CondCategory.TRAP:
                    // TRAP records: 6 bytes — B0=type, B1=X, B2=Y, B3=sub, B4-5=extra.
                    // Per LoadCondRecord: _subType=B1, _flagId=B2, _eventPtr=B3,
                    // _extraB8=B4, _extraB9=B5.
                    X1 = _subType;
                    Y1 = _flagId;
                    TrapSubType = _eventPtr;    // B3 = Ballista type / Vein effect / Item id
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
                    // Round 5 fix: WF byte layout per control naming —
                    //   TALK_N04_P12 (u32 @ +12, FE7/8 size 16) = ASM pointer
                    //   TALKFE6_N0D_P8 (u32 @ +8, FE6 size 12) = ASM pointer
                    //   TALK_N03_W12 (u16 @ +12) = AdditionalDecision
                    //   TALK_N03_W14 (u16 @ +14) = DecisionFlag
                    if (_condType == 0x04 && _isFE7Extended)
                    {
                        // FE7/8 TALK N04 ASM Talk: write ASM pointer to B12-B15 (P12).
                        _extraB12 = AsmFunc & 0xFF;
                        _extraB13 = (AsmFunc >> 8) & 0xFF;
                        _extraB14 = (AsmFunc >> 16) & 0xFF;
                        _extraB15 = (AsmFunc >> 24) & 0xFF;
                    }
                    else if (_condType == 0x0D)
                    {
                        // FE6 TALK N0D ASM Talk: write ASM pointer to B8-B11 (P8).
                        // Unit1/Unit2 above already overwrote B8/B9 — that's
                        // wrong for N0D. Re-write the full u32.
                        _extraB8 = AsmFunc & 0xFF;
                        _extraB9 = (AsmFunc >> 8) & 0xFF;
                        _extraB10 = (AsmFunc >> 16) & 0xFF;
                        _extraB11 = (AsmFunc >> 24) & 0xFF;
                    }
                    else if (_isFE7Extended)
                    {
                        // FE7/8 TALK N03 (or other type): W12/W14 split.
                        _extraB12 = AdditionalDecision & 0xFF;
                        _extraB13 = (AdditionalDecision >> 8) & 0xFF;
                        _extraB14 = DecisionFlag & 0xFF;
                        _extraB15 = (DecisionFlag >> 8) & 0xFF;
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
                        // OBJECT N0A (Shop): B10 = shop type, _eventPtr = item
                        // list pointer (u32 at offset +4). Round-trip the
                        // displayed ItemList value (mirrored to EventPtr in
                        // the View) back into _eventPtr (Copilot CLI review
                        // round 3 #3). The View binds ItemListBox to
                        // _vm.EventPtr directly, so this branch sets
                        // _eventPtr explicitly to make the round-trip
                        // unambiguous even if the View later wires
                        // ItemListBox to a dedicated field.
                        _extraB10 = ShopType;
                        // _eventPtr already mirrors ItemListBox via the
                        // generic EventPtr path; nothing more needed here.
                    }
                    break;
                case CondCategory.ALWAYS:
                    // Round 5 fix: P4 is event pointer (stays in _eventPtr),
                    // P8 is ASM pointer (B8-B11 u32) for N0D/N0E. For range
                    // (N0B = 0x0B) or always (N01 = 0x01), B8-B11 = X1/Y1/X2/Y2.
                    if (_condType == 0x0D || _condType == 0x0E)
                    {
                        // ASM condition: B8-B11 = u32 ASM pointer (P8).
                        // DO NOT overwrite _eventPtr (P4 = event pointer stays).
                        _extraB8 = AsmFunc & 0xFF;
                        _extraB9 = (AsmFunc >> 8) & 0xFF;
                        _extraB10 = (AsmFunc >> 16) & 0xFF;
                        _extraB11 = (AsmFunc >> 24) & 0xFF;
                    }
                    else
                    {
                        // Range / always condition: B8-B11 = X1/Y1/X2/Y2.
                        _extraB8 = X1;
                        _extraB9 = Y1;
                        _extraB10 = X2;
                        _extraB11 = Y2;
                    }
                    break;
                case CondCategory.TRAP:
                    _subType = X1;
                    _flagId = Y1;
                    // B3 byte: Ballista type / Vein effect / Item id —
                    // TrapSubType holds B3 for 6-byte TRAP records.
                    _eventPtr = TrapSubType;
                    // Round 8 fix: pick exactly ONE canonical source for B4/B5
                    // per trap type so type-specific aliases (DamageAmount /
                    // GasDirection / Duration / Hatching*) don't clobber edits
                    // to generic Direction/Durability and vice-versa.
                    // - 0x04 (Damage Floor): B4 = DamageAmount, B5 = (durability unused).
                    // - 0x05 (Poison Gas):    B4 = GasDirection, B5 = (unused).
                    // - 0x08 (Fire):          B4 = (unused),     B5 = Duration.
                    // - 0x0C (Gorgon Egg):    B4 = HatchingStart, B5 = HatchingEnd.
                    // - 0x0B (Mine):          B3 = ItemId (overrides TrapSubType).
                    // - other (Ballista/etc): B4 = TrapDirection, B5 = Durability.
                    if (_condType == 0x04) { _extraB8 = DamageAmount; _extraB9 = Durability; }
                    else if (_condType == 0x05) { _extraB8 = GasDirection; _extraB9 = Durability; }
                    else if (_condType == 0x08) { _extraB8 = TrapDirection; _extraB9 = Duration; }
                    else if (_condType == 0x0C) { _extraB8 = HatchingStart; _extraB9 = HatchingEnd; }
                    else if (_condType == 0x0B) { _eventPtr = ItemId; _extraB8 = TrapDirection; _extraB9 = Durability; }
                    else { _extraB8 = TrapDirection; _extraB9 = Durability; }
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

            // Walk the existing list using the SAME cursor/stride logic as
            // LoadConditionRecords so FE7 TURN type-1 (12-byte) records are
            // handled correctly. Collect (sourceAddr, stride) tuples so we
            // can copy bytes faithfully when building the new buffer
            // (Copilot CLI review round 3 #1).
            var records = new List<(uint srcAddr, uint stride)>();
            uint addrCursor = baseAddr;
            for (uint i = 0; i < 256; i++)
            {
                // Must check terminator BEFORE indexing or reading type byte.
                // Use stride==recordSize as the minimum bounds-check size; if
                // FE7 type-1 sets stride=12 we still bounds-checked at least
                // that much.
                if (addrCursor + recordSize > (uint)rom.Data.Length) break;

                if (slotDef.Category == CondCategory.TUTORIAL)
                {
                    uint v = rom.u32(addrCursor);
                    if (v != 1 && !U.isPointer(v)) break;
                }
                else if (recordSize <= 6)
                {
                    if (rom.u8(addrCursor) == 0) break;
                }
                else
                {
                    if (rom.u32(addrCursor) == 0) break;
                }

                byte type = (byte)rom.u8(addrCursor);
                uint stride = GetRecordStrideAt(slotDef.Category, recordSize, type);
                records.Add((addrCursor, stride));
                addrCursor += stride;
            }

            // Sum the strides to get the total byte length of existing records.
            uint existingBytes = 0;
            foreach (var r in records) existingBytes += r.stride;

            // The new slot uses the full recordSize (16 for FE7 TURN — we
            // don't insert a type-1 row by default; user can change the type
            // after expansion).
            uint newSlotSize = recordSize;
            // Terminator at the end uses recordSize as well.
            uint terminatorSize = recordSize;
            uint totalSize = existingBytes + newSlotSize + terminatorSize;
            byte[] buffer = new byte[totalSize];

            // Copy existing records using per-record stride.
            uint dstOffset = 0;
            foreach (var r in records)
            {
                for (uint j = 0; j < r.stride; j++)
                {
                    if (r.srcAddr + j < (uint)rom.Data.Length)
                        buffer[dstOffset + j] = (byte)rom.u8(r.srcAddr + j);
                }
                dstOffset += r.stride;
            }

            // Initialize the NEW slot with WF-equivalent default type byte
            // (Copilot round 5 #3). WF GetDefaultEventType: TURN=2, TALK=3
            // (FE6=4), OBJECT=5, ALWAYS=1, TRAP=1. TUTORIAL = u32 1.
            uint newSlotOffset = dstOffset;
            if (slotDef.Category == CondCategory.TUTORIAL)
            {
                // u32 = 1 (canonical "blank" marker per WF
                // AddressListExpandsEventTutorial).
                buffer[newSlotOffset + 0] = 1;
                buffer[newSlotOffset + 1] = 0;
                buffer[newSlotOffset + 2] = 0;
                buffer[newSlotOffset + 3] = 0;
            }
            else
            {
                buffer[newSlotOffset + 0] = (byte)GetDefaultEventType(slotDef.Category, rom);
            }

            // Terminator stays zeroed at offset = newSlotOffset + newSlotSize.

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

            // Allocate a minimal stub event using the per-game default event
            // script toplevel code (mirrors WF allocation behavior). FE6: 06...
            // FE7: 0A 00 00 00... FE8: 28 02 07 00 20 01 00 00. Writing zeros
            // would create an invalid event block on some versions (Copilot
            // bot review on round-3).
            byte[] stub = rom.RomInfo?.Default_event_script_toplevel_code
                           ?? new byte[] { 0 };
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
        /// Headless equivalent of InputFormRef.AppendBinaryData. Delegates to
        /// the shared Core seam <see cref="MapEventUnitCore.AppendBinaryDataHeadless"/>
        /// (#776) so the CoreState.AppendBinaryData + ambient-undo allocation
        /// path lives in exactly one place. Routes through the registered
        /// CoreState.AppendBinaryData delegate when available (WinForms
        /// registers it via InputFormRef), else falls back to a free-space
        /// append so headless callers/tests still allocate. The callers here
        /// (ExpandRecordList / AllocateNewEvent) always run under an active
        /// undo scope (they throw otherwise), so the ambient undo is non-null.
        /// </summary>
        static uint AppendBinaryDataHeadless(ROM rom, byte[] buffer)
        {
            // The caller is required to be in an undo scope per the
            // throw-if-not-active enforcement, so the ambient UndoData is
            // available to satisfy the delegate signature.
            var ambient = ROM.GetAmbientUndoData();
            return MapEventUnitCore.AppendBinaryDataHeadless(rom, buffer, ambient);
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
            uint size = IsPointerSlot ? 4 : EffectiveRecordSize;
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
                // Use EffectiveRecordSize so FE7 TURN type-1 rows (stride 12)
                // don't report B12-B15 from the next record (Copilot round 7 #2).
                uint effSize = EffectiveRecordSize;
                report["u8@0"] = $"0x{rom.u8(a + 0):X02}";
                report["u8@1"] = $"0x{rom.u8(a + 1):X02}";
                report["u16@2"] = $"0x{rom.u16(a + 2):X04}";
                report["u32@4"] = $"0x{rom.u32(a + 4):X08}";
                if (effSize >= 12)
                {
                    report["u8@8"] = $"0x{rom.u8(a + 8):X02}";
                    report["u8@9"] = $"0x{rom.u8(a + 9):X02}";
                    report["u8@10"] = $"0x{rom.u8(a + 10):X02}";
                    report["u8@11"] = $"0x{rom.u8(a + 11):X02}";
                }
                if (effSize >= 16)
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
