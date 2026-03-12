using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    /// <summary>Vennou weapon lock editor.
    /// WinForms: VennouWeaponLockForm — variable-length null-terminated list.
    /// First byte = lock type (0=Soft char, 1=Hard char, 2=Soft class, 3=Hard class).
    /// Subsequent bytes = Unit or Class IDs depending on type.
    /// Record size: 1 byte per entry, terminated by 0x00.
    /// </summary>
    public class VennouWeaponLockViewModel : ViewModelBase, IDataVerifiable
    {
        static readonly List<EditorFormRef.FieldDef> _fields =
            EditorFormRef.DetectFields(new[] { "B0" });

        uint _currentAddr;
        uint _baseAddr;
        bool _canWrite;
        uint _lockTypeOrId;
        string _linkedName = "";
        string _explanation = "";
        string _fieldLabel = "Lock Type / ID";
        uint _typeId; // cached first-entry type

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool CanWrite { get => _canWrite; set => SetField(ref _canWrite, value); }

        /// <summary>Lock type (first entry) or Unit/Class ID (subsequent entries).</summary>
        public uint LockTypeOrId { get => _lockTypeOrId; set => SetField(ref _lockTypeOrId, value); }

        /// <summary>Display name resolved from LockTypeOrId.</summary>
        public string LinkedName { get => _linkedName; set => SetField(ref _linkedName, value); }

        /// <summary>Explanation text.</summary>
        public string Explanation { get => _explanation; set => SetField(ref _explanation, value); }

        /// <summary>Dynamic label: "Type" for first entry, "Unit" or "Class" for subsequent.</summary>
        public string FieldLabel { get => _fieldLabel; set => SetField(ref _fieldLabel, value); }

        /// <summary>Build the full null-terminated lock list starting at the given base address.</summary>
        public List<AddrResult> BuildList(uint baseAddr)
        {
            _baseAddr = baseAddr;
            var result = new List<AddrResult>();
            ROM rom = CoreState.ROM;
            if (rom == null || baseAddr == 0) return result;

            uint typeId = 0;
            for (int i = 0; baseAddr + (uint)i < (uint)rom.Data.Length; i++)
            {
                uint addr = baseAddr + (uint)i;
                uint id = rom.u8(addr);

                // First entry is always present (type header)
                if (i == 0)
                {
                    typeId = id;
                    string typeName = TypeIDToString(id);
                    result.Add(new AddrResult(addr, typeName, id));
                    continue;
                }

                // Subsequent entries: null terminator ends the list
                if (id == 0x00) break;

                string name;
                if (typeId <= 1)
                    name = $"0x{id:X2} {NameResolver.GetUnitName(id)}";
                else
                    name = $"0x{id:X2} {NameResolver.GetClassName(id)}";
                result.Add(new AddrResult(addr, name, id));
            }
            return result;
        }

        /// <summary>Convert type ID to human-readable string.</summary>
        public static string TypeIDToString(uint id)
        {
            return id switch
            {
                0 => "Soft character lock",
                1 => "Hard character lock",
                2 => "Soft class lock",
                3 => "Hard class lock",
                _ => $"Unknown type ({id})",
            };
        }

        public void LoadEntry(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + 1 > (uint)rom.Data.Length) return;

            IsLoading = true;
            CurrentAddr = addr;
            var values = EditorFormRef.ReadFields(rom, addr, _fields);
            LockTypeOrId = values["B0"];

            // Determine if this is the type entry (first in list) or a value entry
            bool isTypeEntry = (addr == _baseAddr);
            if (isTypeEntry)
            {
                _typeId = LockTypeOrId;
                FieldLabel = "Type";
                LinkedName = TypeIDToString(LockTypeOrId);
                Explanation = "Lock type determines how subsequent IDs are interpreted:\n"
                    + "0/1 = Character lock (Unit IDs)\n2/3 = Class lock (Class IDs)\n"
                    + "Soft = AI ignores lock; Hard = AI respects lock.";
            }
            else
            {
                if (_typeId <= 1)
                {
                    FieldLabel = "Unit";
                    LinkedName = NameResolver.GetUnitName(LockTypeOrId);
                    Explanation = $"Unit ID 0x{LockTypeOrId:X2} in a {TypeIDToString(_typeId)} list.";
                }
                else
                {
                    FieldLabel = "Class";
                    LinkedName = NameResolver.GetClassName(LockTypeOrId);
                    Explanation = $"Class ID 0x{LockTypeOrId:X2} in a {TypeIDToString(_typeId)} list.";
                }
            }

            CanWrite = true;
            IsLoading = false;
            MarkClean();
        }

        public void Write()
        {
            WriteEntry();
        }

        public void WriteEntry()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;
            if (CurrentAddr + 1 > (uint)rom.Data.Length) return;

            var values = new Dictionary<string, uint> { ["B0"] = LockTypeOrId };
            EditorFormRef.WriteFields(rom, CurrentAddr, values, _fields);
        }

        public int GetListCount()
        {
            if (_baseAddr == 0) return 0;
            return BuildList(_baseAddr).Count;
        }

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["LockTypeOrId"] = $"0x{LockTypeOrId:X02}",
                ["LinkedName"] = LinkedName,
                ["FieldLabel"] = FieldLabel,
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
                ["u8@0x00"] = $"0x{rom.u8(a + 0):X02}",
            };
        }
    }
}
