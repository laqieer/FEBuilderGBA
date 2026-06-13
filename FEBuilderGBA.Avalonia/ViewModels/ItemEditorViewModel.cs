using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public partial class ItemEditorViewModel : ViewModelBase, IDataVerifiable
    {
        uint _currentAddr;
        string _name = "";
        // W0, W2, W4 (u16 text IDs)
        uint _nameId, _descId, _useDescId;
        // B6 (item number), B7 (weapon type)
        uint _itemNumber, _weaponType;
        // B8-B11 (traits/flags)
        uint _trait1, _trait2, _trait3, _trait4;
        // P12 (stat bonuses ptr), P16 (effectiveness ptr)
        uint _statBonusesPtr, _effectivenessPtr;
        // B20-B25 (combat stats)
        uint _uses, _might, _hit, _weight, _crit, _range;
        // W26 (price)
        uint _price;
        // B28-B35 (weapon rank, icon, effects, etc.)
        uint _weaponRank, _icon, _usageEffect, _damageEffect;
        uint _weaponExp, _unk33, _unk34, _unk35;
        bool _canWrite;

        // Description text previews
        string _descText = "";
        string _useDescText = "";

        // Skill system extension fields
        bool _showSkillField;
        string _skillName = "";
        string _b35Label = "Unknown";

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public string Name { get => _name; set => SetField(ref _name, value); }

        // W0: Name text ID
        public uint NameId { get => _nameId; set => SetField(ref _nameId, value); }
        // W2: Description text ID
        public uint DescId { get => _descId; set => SetField(ref _descId, value); }
        // W4: Usage description text ID
        public uint UseDescId { get => _useDescId; set => SetField(ref _useDescId, value); }
        // Description text preview
        public string DescText { get => _descText; set => SetField(ref _descText, value); }
        // Usage description text preview
        public string UseDescText { get => _useDescText; set => SetField(ref _useDescText, value); }
        // B6: Item number / ID
        public uint ItemNumber { get => _itemNumber; set => SetField(ref _itemNumber, value); }
        // B7: Weapon type (0=sword,1=lance,2=axe,etc.)
        public uint WeaponType { get => _weaponType; set => SetField(ref _weaponType, value); }
        // B8: Trait flags 1
        public uint Trait1 { get => _trait1; set => SetField(ref _trait1, value); }
        // B9: Trait flags 2
        public uint Trait2 { get => _trait2; set => SetField(ref _trait2, value); }
        // B10: Trait flags 3
        public uint Trait3 { get => _trait3; set => SetField(ref _trait3, value); }
        // B11: Trait flags 4
        public uint Trait4 { get => _trait4; set => SetField(ref _trait4, value); }
        // P12: Pointer to stat bonuses data
        public uint StatBonusesPtr { get => _statBonusesPtr; set => SetField(ref _statBonusesPtr, value); }
        // P16: Pointer to effectiveness data
        public uint EffectivenessPtr { get => _effectivenessPtr; set => SetField(ref _effectivenessPtr, value); }
        // B20: Durability / uses
        public uint Uses { get => _uses; set => SetField(ref _uses, value); }
        // B21: Might / attack power
        public uint Might { get => _might; set => SetField(ref _might, value); }
        // B22: Hit rate
        public uint Hit { get => _hit; set => SetField(ref _hit, value); }
        // B23: Weight
        public uint Weight { get => _weight; set => SetField(ref _weight, value); }
        // B24: Critical rate
        public uint Crit { get => _crit; set => SetField(ref _crit, value); }
        // B25: Range
        public uint Range { get => _range; set => SetField(ref _range, value); }
        // W26: Unit price
        public uint Price { get => _price; set => SetField(ref _price, value); }
        // B28: Weapon rank / level
        public uint WeaponRank { get => _weaponRank; set => SetField(ref _weaponRank, value); }
        // B29: Icon ID
        public uint Icon { get => _icon; set => SetField(ref _icon, value); }
        // B30: Usage effect (staff effect)
        public uint UsageEffect { get => _usageEffect; set => SetField(ref _usageEffect, value); }
        // B31: Damage additional effect
        public uint DamageEffect { get => _damageEffect; set => SetField(ref _damageEffect, value); }
        // B32: Weapon experience gain
        public uint WeaponExp { get => _weaponExp; set => SetField(ref _weaponExp, value); }
        // B33: Unknown / patch-dependent
        public uint Unk33 { get => _unk33; set => SetField(ref _unk33, value); }
        // B34: Unknown / patch-dependent
        public uint Unk34 { get => _unk34; set => SetField(ref _unk34, value); }
        // B35: Unknown / patch-dependent
        public uint Unk35 { get => _unk35; set => SetField(ref _unk35, value); }
        public bool CanWrite { get => _canWrite; set => SetField(ref _canWrite, value); }

        // Skill system extension properties
        /// <summary>True when a skill system patch is installed and B35 should be labeled "Skill".</summary>
        public bool ShowSkillField { get => _showSkillField; private set => SetField(ref _showSkillField, value); }
        /// <summary>Resolved skill name for B35 value, or empty if no skill system.</summary>
        public string SkillName { get => _skillName; private set => SetField(ref _skillName, value); }
        /// <summary>Label for the B35 field: "Skill" when skill system is detected, "Unknown" otherwise.</summary>
        public string B35Label { get => _b35Label; private set => SetField(ref _b35Label, value); }

        // --- Computed: Shop prices ---
        uint _shopBuyPrice, _shopSellPrice, _shopForgePrice;
        public uint ShopBuyPrice { get => _shopBuyPrice; private set => SetField(ref _shopBuyPrice, value); }
        public uint ShopSellPrice { get => _shopSellPrice; private set => SetField(ref _shopSellPrice, value); }
        public uint ShopForgePrice { get => _shopForgePrice; private set => SetField(ref _shopForgePrice, value); }

        // --- Computed: Stat bonus preview ---
        int _bonusHP, _bonusStr, _bonusSkl, _bonusSpd, _bonusDef, _bonusRes, _bonusLck, _bonusMove, _bonusCon, _bonusMag;
        bool _hasBonusPreview, _hasMagicBonus;
        public int BonusHP { get => _bonusHP; private set => SetField(ref _bonusHP, value); }
        public int BonusStr { get => _bonusStr; private set => SetField(ref _bonusStr, value); }
        public int BonusSkl { get => _bonusSkl; private set => SetField(ref _bonusSkl, value); }
        public int BonusSpd { get => _bonusSpd; private set => SetField(ref _bonusSpd, value); }
        public int BonusDef { get => _bonusDef; private set => SetField(ref _bonusDef, value); }
        public int BonusRes { get => _bonusRes; private set => SetField(ref _bonusRes, value); }
        public int BonusLck { get => _bonusLck; private set => SetField(ref _bonusLck, value); }
        public int BonusMove { get => _bonusMove; private set => SetField(ref _bonusMove, value); }
        public int BonusCon { get => _bonusCon; private set => SetField(ref _bonusCon, value); }
        public bool HasBonusPreview { get => _hasBonusPreview; private set => SetField(ref _hasBonusPreview, value); }
        /// <summary>
        /// MagicSplit magic stat-bonus value (offset +9 in the stat-bonus block).
        /// Mirrors WF <c>MagicExtUnitBase</c>. Read bounds-guarded; 0 when the
        /// 10th byte is unavailable. Only meaningful when <see cref="HasMagicBonus"/>.
        /// </summary>
        public int BonusMag { get => _bonusMag; private set => SetField(ref _bonusMag, value); }
        /// <summary>
        /// True only on FE7U/FE8U MagicSplit ROMs, where the stat-bonus block
        /// carries a 10th Magic value (mirrors WF <c>MagicExtUnitBase</c>
        /// visibility). FE8N/vanilla/FE6 keep this false so the Magic row stays
        /// hidden.
        /// </summary>
        public bool HasMagicBonus { get => _hasMagicBonus; private set => SetField(ref _hasMagicBonus, value); }

        // --- Computed: Effective class list ---
        List<string> _effectiveClassList = new();
        bool _hasEffectiveClasses;
        public List<string> EffectiveClassList { get => _effectiveClassList; private set => SetField(ref _effectiveClassList, value); }
        public bool HasEffectiveClasses { get => _hasEffectiveClasses; private set => SetField(ref _hasEffectiveClasses, value); }

        // --- Computed: Null pointer warnings ---
        bool _showAllocStatBonuses, _showAllocEffectiveness;
        uint _currentItemIndex;
        public bool ShowAllocStatBonuses { get => _showAllocStatBonuses; private set => SetField(ref _showAllocStatBonuses, value); }
        public bool ShowAllocEffectiveness { get => _showAllocEffectiveness; private set => SetField(ref _showAllocEffectiveness, value); }

        /// <summary>
        /// Current entry index (0 = the null/unused item 0). Exposed for the
        /// source-backed writer save-gate (#1132) which keys the edit by entry id.
        /// </summary>
        public int CurrentItemIndex => (int)_currentItemIndex;

        /// <summary>
        /// Build the candidate C-field → value map for the decomp source-backed
        /// writer (#1132). Keys use the conventional decomp C field names; the View
        /// intersects this with the manifest owner's declared <c>fields</c> before
        /// calling the writer, so a field the manifest does not declare is silently
        /// dropped (never an UnsupportedField error). Only plain integer item-stat
        /// fields are included — pointers and text-id fields are intentionally
        /// omitted (they are not safe to rewrite as integer literals here).
        /// </summary>
        public IReadOnlyDictionary<string, uint> BuildSourceFieldDict()
        {
            return new Dictionary<string, uint>(StringComparer.Ordinal)
            {
                { "maxUses",       Uses },
                { "might",         Might },
                { "hitRate",       Hit },
                { "weight",        Weight },
                { "critRate",      Crit },
                { "maxRange",      Range },
                { "minRange",      Range },
                { "cost",          Price },
                { "price",         Price },
                { "weaponLevel",   WeaponRank },
                { "iconId",        Icon },
                { "weaponType",    WeaponType },
            };
        }

        /// <summary>Recalculate all computed fields. Call after loading or UI changes.</summary>
        public void RecalcComputed()
        {
            // Shop prices
            uint total = Uses * Price;
            ShopBuyPrice = total;
            ShopSellPrice = total / 2;
            ShopForgePrice = (uint)(total * 1.5);

            // Null pointer warnings
            ShowAllocStatBonuses = StatBonusesPtr == 0 && _currentItemIndex > 0;
            ShowAllocEffectiveness = EffectivenessPtr == 0 && _currentItemIndex > 0;

            // Skill name resolution
            if (ShowSkillField)
                SkillName = ResolveSkillName(Unk35);

            // Stat bonus preview
            RecalcStatBonuses();

            // Effective class list
            UpdateEffectiveClassList();
        }

        void RecalcStatBonuses()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || !U.isPointer(StatBonusesPtr))
            {
                HasBonusPreview = false;
                BonusHP = BonusStr = BonusSkl = BonusSpd = BonusDef = BonusRes = BonusLck = BonusMove = BonusCon = 0;
                BonusMag = 0;
                HasMagicBonus = false;
                return;
            }

            uint addr = StatBonusesPtr - 0x08000000;
            if (!U.isSafetyOffset(addr) || addr + 9 > (uint)rom.Data.Length)
            {
                HasBonusPreview = false;
                BonusHP = BonusStr = BonusSkl = BonusSpd = BonusDef = BonusRes = BonusLck = BonusMove = BonusCon = 0;
                BonusMag = 0;
                HasMagicBonus = false;
                return;
            }

            HasBonusPreview = true;
            BonusHP   = (sbyte)rom.u8(addr + 0);
            BonusStr  = (sbyte)rom.u8(addr + 1);
            BonusSkl  = (sbyte)rom.u8(addr + 2);
            BonusSpd  = (sbyte)rom.u8(addr + 3);
            BonusDef  = (sbyte)rom.u8(addr + 4);
            BonusRes  = (sbyte)rom.u8(addr + 5);
            BonusLck  = (sbyte)rom.u8(addr + 6);
            BonusMove = (sbyte)rom.u8(addr + 7);
            BonusCon  = (sbyte)rom.u8(addr + 8);

            // MagicSplit magic stat-bonus (offset +9) — mirrors WF MagicExtUnitBase.
            // Bounds-guard the 10th byte so the existing 9-byte vanilla preview
            // stays intact (a vanilla block has no +9 byte → Mag = 0).
            BonusMag = (addr + 10 <= (uint)rom.Data.Length) ? (sbyte)rom.u8(addr + 9) : 0;
            // Visibility gate: FE7U/FE8U MagicSplit only (NOT FE8N) for WF parity.
            var ms = PatchDetectionService.Instance.MagicSplit;
            HasMagicBonus = (ms == PatchDetectionService.MagicSplitType.FE7U
                          || ms == PatchDetectionService.MagicSplitType.FE8U);
        }

        void UpdateEffectiveClassList()
        {
            ROM rom = CoreState.ROM;
            var list = new List<string>();
            if (rom != null && U.isPointer(EffectivenessPtr))
            {
                uint addr = U.toOffset(EffectivenessPtr);
                if (U.isSafetyOffset(addr))
                {
                    for (int i = 0; i < 64; i++)
                    {
                        if (addr + (uint)i >= (uint)rom.Data.Length) break;
                        uint classId = rom.u8((uint)(addr + i));
                        if (classId == 0) break;
                        string name = NameResolver.GetClassName(classId);
                        list.Add($"0x{classId:X02} {name}");
                    }
                }
            }
            EffectiveClassList = list;
            HasEffectiveClasses = list.Count > 0;
        }

        // Keep old property names as aliases for backward compatibility with Views
        [Obsolete("Use WeaponRank instead")] public uint Rank { get => WeaponRank; set => WeaponRank = value; }

        public List<AddrResult> LoadItemList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            uint ptr = rom.RomInfo.item_pointer;
            if (ptr == 0) return new List<AddrResult>();

            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            uint dataSize = rom.RomInfo.item_datasize;
            bool fe8SingleByte = ItemListPredicate.IsFE8SingleByte(rom);

            var result = new List<AddrResult>();
            // Issue #364: mirror the WinForms ItemForm stop predicate so the list
            // ends at the last real item (e.g. 206 for FE8U) instead of running
            // unconditionally to 256 and producing dummy/garbage tail entries.
            for (uint i = 0; i <= 0xFF; i++)
            {
                uint addr = (uint)(baseAddr + i * dataSize);
                if (addr + dataSize > (uint)rom.Data.Length) break;
                if (!ItemListPredicate.IsValidEntry(rom, (int)i, addr, fe8SingleByte)) break;

                uint nameId = rom.u16(addr + 0);
                string decoded;
                try { decoded = NameResolver.GetTextById(nameId); }
                catch { decoded = "???"; }
                string name = U.ToHexString(i) + " " + decoded;
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        public void LoadItem(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;

            uint dataSize = rom.RomInfo?.item_datasize ?? 36;
            if (addr + dataSize > (uint)rom.Data.Length) return;

            CurrentAddr = addr;

            // Text IDs
            NameId = rom.u16(addr + 0);      // W0
            DescId = rom.u16(addr + 2);      // W2
            UseDescId = rom.u16(addr + 4);   // W4

            try { Name = NameResolver.GetTextById(NameId); }
            catch { Name = "???"; }

            try { DescText = NameResolver.GetTextById(DescId); }
            catch { DescText = ""; }

            try { UseDescText = NameResolver.GetTextById(UseDescId); }
            catch { UseDescText = ""; }

            // Identity
            ItemNumber = rom.u8(addr + 6);   // B6
            WeaponType = rom.u8(addr + 7);   // B7

            // Trait flags
            Trait1 = rom.u8(addr + 8);       // B8
            Trait2 = rom.u8(addr + 9);       // B9
            Trait3 = rom.u8(addr + 10);      // B10
            Trait4 = rom.u8(addr + 11);      // B11

            // Pointers
            StatBonusesPtr = rom.u32(addr + 12);  // P12
            EffectivenessPtr = rom.u32(addr + 16); // P16

            // Combat stats
            Uses = rom.u8(addr + 20);        // B20
            Might = rom.u8(addr + 21);       // B21
            Hit = rom.u8(addr + 22);         // B22
            Weight = rom.u8(addr + 23);      // B23
            Crit = rom.u8(addr + 24);        // B24
            Range = rom.u8(addr + 25);       // B25
            Price = rom.u16(addr + 26);      // W26

            // Weapon rank and effects
            WeaponRank = rom.u8(addr + 28);  // B28
            Icon = rom.u8(addr + 29);        // B29
            UsageEffect = rom.u8(addr + 30); // B30
            DamageEffect = rom.u8(addr + 31); // B31
            WeaponExp = rom.u8(addr + 32);   // B32

            // Extension bytes (FE7/FE8 only, datasize >= 36)
            if (dataSize >= 36)
            {
                Unk33 = rom.u8(addr + 33);   // B33
                Unk34 = rom.u8(addr + 34);   // B34
                Unk35 = rom.u8(addr + 35);   // B35
            }

            CanWrite = true;

            // Skill system detection for B35
            ShowSkillField = PatchDetectionService.Instance.HasSkillSystem;
            B35Label = ShowSkillField ? "Skill" : "Unknown";
            if (ShowSkillField && dataSize >= 36)
            {
                SkillName = ResolveSkillName(Unk35);
            }
            else
            {
                SkillName = "";
            }

            // Determine item index from address
            uint ptr = rom.RomInfo.item_pointer;
            uint baseAddr = rom.p32(ptr);
            if (baseAddr > 0 && addr >= baseAddr)
                _currentItemIndex = (addr - baseAddr) / dataSize;
            else
                _currentItemIndex = 0;

            RecalcComputed();
        }

        public int GetListCount() => LoadItemList().Count;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["W0_NameId"] = $"0x{NameId:X04}",
                ["W2_DescId"] = $"0x{DescId:X04}",
                ["W4_UseDescId"] = $"0x{UseDescId:X04}",
                ["B6_ItemNumber"] = $"0x{ItemNumber:X02}",
                ["B7_WeaponType"] = $"0x{WeaponType:X02}",
                ["B8_Trait1"] = $"0x{Trait1:X02}",
                ["B9_Trait2"] = $"0x{Trait2:X02}",
                ["B10_Trait3"] = $"0x{Trait3:X02}",
                ["B11_Trait4"] = $"0x{Trait4:X02}",
                ["P12_StatBonuses"] = $"0x{StatBonusesPtr:X08}",
                ["P16_Effectiveness"] = $"0x{EffectivenessPtr:X08}",
                ["B20_Uses"] = $"0x{Uses:X02}",
                ["B21_Might"] = $"0x{Might:X02}",
                ["B22_Hit"] = $"0x{Hit:X02}",
                ["B23_Weight"] = $"0x{Weight:X02}",
                ["B24_Crit"] = $"0x{Crit:X02}",
                ["B25_Range"] = $"0x{Range:X02}",
                ["W26_Price"] = $"0x{Price:X04}",
                ["B28_WeaponRank"] = $"0x{WeaponRank:X02}",
                ["B29_Icon"] = $"0x{Icon:X02}",
                ["B30_UsageEffect"] = $"0x{UsageEffect:X02}",
                ["B31_DamageEffect"] = $"0x{DamageEffect:X02}",
                ["B32_WeaponExp"] = $"0x{WeaponExp:X02}",
                ["B33_Unk"] = $"0x{Unk33:X02}",
                ["B34_Unk"] = $"0x{Unk34:X02}",
                ["B35_Unk"] = $"0x{Unk35:X02}",
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
                ["u16@0x04"] = $"0x{rom.u16(a + 4):X04}",
                ["u8@0x06"] = $"0x{rom.u8(a + 6):X02}",
                ["u8@0x07"] = $"0x{rom.u8(a + 7):X02}",
                ["u8@0x08"] = $"0x{rom.u8(a + 8):X02}",
                ["u8@0x09"] = $"0x{rom.u8(a + 9):X02}",
                ["u8@0x0A"] = $"0x{rom.u8(a + 10):X02}",
                ["u8@0x0B"] = $"0x{rom.u8(a + 11):X02}",
                ["u32@0x0C"] = $"0x{rom.u32(a + 12):X08}",
                ["u32@0x10"] = $"0x{rom.u32(a + 16):X08}",
                ["u8@0x14"] = $"0x{rom.u8(a + 20):X02}",
                ["u8@0x15"] = $"0x{rom.u8(a + 21):X02}",
                ["u8@0x16"] = $"0x{rom.u8(a + 22):X02}",
                ["u8@0x17"] = $"0x{rom.u8(a + 23):X02}",
                ["u8@0x18"] = $"0x{rom.u8(a + 24):X02}",
                ["u8@0x19"] = $"0x{rom.u8(a + 25):X02}",
                ["u16@0x1A"] = $"0x{rom.u16(a + 26):X04}",
                ["u8@0x1C"] = $"0x{rom.u8(a + 28):X02}",
                ["u8@0x1D"] = $"0x{rom.u8(a + 29):X02}",
                ["u8@0x1E"] = $"0x{rom.u8(a + 30):X02}",
                ["u8@0x1F"] = $"0x{rom.u8(a + 31):X02}",
                ["u8@0x20"] = $"0x{rom.u8(a + 32):X02}",
                ["u8@0x21"] = $"0x{rom.u8(a + 33):X02}",
                ["u8@0x22"] = $"0x{rom.u8(a + 34):X02}",
                ["u8@0x23"] = $"0x{rom.u8(a + 35):X02}",
            };
        }

        public Dictionary<string, string> GetFieldOffsetMap() => new()
        {
            ["W0_NameId"] = "u16@0x00",
            ["W2_DescId"] = "u16@0x02",
            ["W4_UseDescId"] = "u16@0x04",
            ["B6_ItemNumber"] = "u8@0x06",
            ["B7_WeaponType"] = "u8@0x07",
            ["B8_Trait1"] = "u8@0x08",
            ["B9_Trait2"] = "u8@0x09",
            ["B10_Trait3"] = "u8@0x0A",
            ["B11_Trait4"] = "u8@0x0B",
            ["P12_StatBonuses"] = "u32@0x0C",
            ["P16_Effectiveness"] = "u32@0x10",
            ["B20_Uses"] = "u8@0x14",
            ["B21_Might"] = "u8@0x15",
            ["B22_Hit"] = "u8@0x16",
            ["B23_Weight"] = "u8@0x17",
            ["B24_Crit"] = "u8@0x18",
            ["B25_Range"] = "u8@0x19",
            ["W26_Price"] = "u16@0x1A",
            ["B28_WeaponRank"] = "u8@0x1C",
            ["B29_Icon"] = "u8@0x1D",
            ["B30_UsageEffect"] = "u8@0x1E",
            ["B31_DamageEffect"] = "u8@0x1F",
            ["B32_WeaponExp"] = "u8@0x20",
            ["B33_Unk"] = "u8@0x21",
            ["B34_Unk"] = "u8@0x22",
            ["B35_Unk"] = "u8@0x23",
        };

        // --- Validation ---
        List<string> _validationWarnings = new();
        public List<string> ValidationWarnings { get => _validationWarnings; set => SetField(ref _validationWarnings, value); }
        public bool HasWarnings => _validationWarnings?.Count > 0;

        /// <summary>
        /// Validate item data and return a list of warnings.
        /// These are advisory only and do not block writes.
        /// </summary>
        public List<string> ValidateItem()
        {
            var warnings = new List<string>();

            // Only validate non-zero item indices (index 0 is null/unused)
            if (_currentItemIndex > 0)
            {
                if (NameId == 0)
                    warnings.Add("No name assigned (NameId is 0)");
            }

            // Check unbreakable flag: bit 3 of Trait1
            bool isUnbreakable = (Trait1 & 0x08) != 0;

            if (Uses == 0 && !isUnbreakable && _currentItemIndex > 0)
                warnings.Add("Uses is 0 (item is not unbreakable)");

            if (Price > 0 && Uses == 0 && !isUnbreakable)
                warnings.Add("Has price but no uses");

            // Weapon type set (0-7 are weapon types; 9+ are typically non-weapon items)
            // Check if item has a weapon type but no weapon stats (might, hit both 0)
            if (WeaponType <= 7 && _currentItemIndex > 0 && Might == 0 && Hit == 0 && Range == 0)
                warnings.Add("Weapon type set but no weapon stats (might, hit, range all 0)");

            ValidationWarnings = warnings;
            OnPropertyChanged(nameof(HasWarnings));
            return warnings;
        }

        public void WriteItem()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;

            uint addr = CurrentAddr;
            uint dataSize = rom.RomInfo?.item_datasize ?? 36;

            rom.write_u16(addr + 0, NameId);
            rom.write_u16(addr + 2, DescId);
            rom.write_u16(addr + 4, UseDescId);
            rom.write_u8(addr + 6, ItemNumber);
            rom.write_u8(addr + 7, WeaponType);
            rom.write_u8(addr + 8, Trait1);
            rom.write_u8(addr + 9, Trait2);
            rom.write_u8(addr + 10, Trait3);
            rom.write_u8(addr + 11, Trait4);
            rom.write_u32(addr + 12, StatBonusesPtr);
            rom.write_u32(addr + 16, EffectivenessPtr);
            rom.write_u8(addr + 20, Uses);
            rom.write_u8(addr + 21, Might);
            rom.write_u8(addr + 22, Hit);
            rom.write_u8(addr + 23, Weight);
            rom.write_u8(addr + 24, Crit);
            rom.write_u8(addr + 25, Range);
            rom.write_u16(addr + 26, Price);
            rom.write_u8(addr + 28, WeaponRank);
            rom.write_u8(addr + 29, Icon);
            rom.write_u8(addr + 30, UsageEffect);
            rom.write_u8(addr + 31, DamageEffect);
            rom.write_u8(addr + 32, WeaponExp);

            if (dataSize >= 36)
            {
                rom.write_u8(addr + 33, Unk33);
                rom.write_u8(addr + 34, Unk34);
                rom.write_u8(addr + 35, Unk35);
            }
        }

        /// <summary>
        /// New-alloc the StatBooster (P12) block for the current item — mirrors
        /// WF <c>L_12_NEWALLOC_ITEMSTATBOOSTER</c> /
        /// <c>AllocEvent("ITEMSTATBOOSTER")</c>. Delegates to
        /// <see cref="ItemAllocCore.AllocStatBonuses"/> (template <c>byte[20]</c>
        /// <c>[1]=5</c>), which writes the block + repoints the slot under the
        /// ambient undo scope opened by the View's <c>UndoService.Begin</c>.
        /// No-clobber: a non-zero P12 is left untouched. On success refreshes
        /// <see cref="StatBonusesPtr"/> + the computed warnings/preview.
        /// </summary>
        /// <returns>true when a block was allocated and the pointer set.</returns>
        public bool AllocStatBonuses(Undo.UndoData? undoData)
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return false;
            // Gate identical to the warning visibility (Ptr==0 && index>0).
            if (StatBonusesPtr != 0 || _currentItemIndex == 0) return false;

            uint addr = ItemAllocCore.AllocStatBonuses(rom, CurrentAddr, undoData);
            if (addr == U.NOT_FOUND) return false;

            // Re-read the freshly written pointer slot so the UI + computed
            // fields reflect the new block (mirrors WF writeButton.PerformClick).
            StatBonusesPtr = rom.u32(CurrentAddr + 12);
            RecalcComputed();
            return true;
        }

        /// <summary>
        /// New-alloc the Effectiveness (P16) block for the current item —
        /// mirrors WF <c>L_16_NEWALLOC_EFFECTIVENESS</c> /
        /// <c>AllocEvent("EFFECTIVENESS")</c>. <paramref name="skillSystemsRework"/>
        /// selects the WF patch-conditional template
        /// (<see cref="ItemAllocCore.BuildEffectivenessTemplate"/>); the View
        /// supplies <c>PatchDetectionService.SkillSystemsClassTypeRework</c>.
        /// No-clobber + ambient-undo, exactly like
        /// <see cref="AllocStatBonuses"/>.
        /// </summary>
        public bool AllocEffectiveness(bool skillSystemsRework, Undo.UndoData? undoData)
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return false;
            if (EffectivenessPtr != 0 || _currentItemIndex == 0) return false;

            uint addr = ItemAllocCore.AllocEffectiveness(rom, CurrentAddr, skillSystemsRework, undoData);
            if (addr == U.NOT_FOUND) return false;

            EffectivenessPtr = rom.u32(CurrentAddr + 16);
            RecalcComputed();
            return true;
        }

        /// <summary>
        /// Resolve a skill ID to a display name via NameResolver.
        /// </summary>
        static string ResolveSkillName(uint skillId)
        {
            if (skillId == 0) return "(None)";
            return NameResolver.GetSkillName(skillId);
        }
    }
}
