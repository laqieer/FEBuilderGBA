using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    /// <summary>
    /// Unit Action Pointer editor — table of function pointers for unit actions.
    /// WinForms: UnitActionPointerForm.cs
    /// Struct layout: P0 = function pointer (GBA pointer, 4 bytes) = 4 bytes total.
    /// The base address comes from RomInfo.unitaction_function_pointer (p32 dereference).
    /// </summary>
    public class UnitActionPointerViewModel : ViewModelBase, IDataVerifiable
    {
        static readonly List<EditorFormRef.FieldDef> _fields =
            EditorFormRef.DetectFields(new[] { "P0" });

        const uint EntrySize = 4;

        uint _currentAddr;
        bool _isLoaded;
        uint _p0;
        uint _actionId;
        string _actionName = "";

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public uint P0 { get => _p0; set => SetField(ref _p0, value); }

        /// <summary>1-based action id of the selected entry (WinForms: non-rework ids start at 1).</summary>
        public uint ActionId { get => _actionId; set => SetField(ref _actionId, value); }

        /// <summary>Human-readable action name from the <c>unitaction_</c> resource (empty if none).</summary>
        public string ActionName { get => _actionName; set => SetField(ref _actionName, value); }

        /// <summary>
        /// Resolve the base address of the unit action pointer table.
        /// On vanilla ROMs: p32(RomInfo.unitaction_function_pointer).
        /// Returns 0 if the pointer is invalid.
        /// </summary>
        static uint ResolveBaseAddress(ROM rom)
        {
            uint pointer = rom.RomInfo.unitaction_function_pointer;
            if (pointer == 0) return 0;
            uint baseAddr = rom.p32(pointer);
            return U.isSafetyOffset(baseAddr) ? baseAddr : 0;
        }

        public List<AddrResult> LoadList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            uint baseAddr = ResolveBaseAddress(rom);
            if (baseAddr == 0) return new List<AddrResult>();

            var actionNames = LoadActionNames();

            return EditorFormRef.BuildListWithCount(rom, baseAddr, EntrySize,
                (i, addr) =>
                {
                    uint a = rom.u32(addr);
                    return U.isSafetyPointer(a);
                },
                (i, addr) =>
                {
                    // WinForms starts at id=1 for non-reworked; we use 0-based index here.
                    uint id = (uint)(i + 1);
                    return MakeLabel(id, actionNames);
                });
        }

        public void LoadEntry(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + EntrySize > (uint)rom.Data.Length) return;
            CurrentAddr = addr;
            var values = EditorFormRef.ReadFields(rom, addr, _fields);
            P0 = values["P0"];

            // Recover the 1-based action id from the offset within the table and resolve its name.
            uint baseAddr = ResolveBaseAddress(rom);
            ActionId = (baseAddr != 0 && addr >= baseAddr) ? (addr - baseAddr) / EntrySize + 1 : 0;
            ActionName = ActionId != 0 ? ResolveActionName(ActionId, LoadActionNames()) : "";

            IsLoaded = true;
        }

        /// <summary>Load the version-specific unit-action name dictionary (empty on any failure).</summary>
        static Dictionary<uint, string> LoadActionNames()
        {
            try { return U.LoadDicResource(U.ConfigDataFilename("unitaction_")); }
            catch { return new Dictionary<uint, string>(); }
        }

        /// <summary>Action name from the resource, falling back to "Action N" when unnamed —
        /// shared by the list label and the detail panel so both stay consistent.</summary>
        static string ResolveActionName(uint id, Dictionary<uint, string> actionNames)
        {
            string name = U.at(actionNames, id);
            return string.IsNullOrEmpty(name) ? "Action " + id : name;
        }

        /// <summary>List label mirroring WinForms: hex id + action name (falls back to "Action N").</summary>
        static string MakeLabel(uint id, Dictionary<uint, string> actionNames)
            => U.ToHexString(id) + " " + ResolveActionName(id, actionNames);

        public void WriteEntry()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;
            var values = new Dictionary<string, uint>
            {
                ["P0"] = P0,
            };
            EditorFormRef.WriteFields(rom, CurrentAddr, values, _fields);
        }

        public int GetListCount()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return 0;
            uint baseAddr = ResolveBaseAddress(rom);
            if (baseAddr == 0) return 0;
            return EditorFormRef.CountEntries(rom, baseAddr, EntrySize,
                (i, addr) => U.isSafetyPointer(rom.u32(addr)));
        }

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["P0_FuncPtr"] = $"0x{P0:X08}",
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
                // Raw u32 value (GBA pointer with 0x08 prefix, used for validation)
                ["u32@0x00_RawPtr"] = $"0x{rom.u32(a + 0):X08}",
                // P0 is a Pointer field (EditorFormRef reads via p32, stripping 0x08 prefix)
                ["p32@0x00_FuncPtr"] = $"0x{rom.p32(a + 0):X08}",
            };
        }

        public Dictionary<string, string> GetFieldOffsetMap() => new()
        {
            ["P0_FuncPtr"] = "p32@0x00_FuncPtr",
        };
    }
}
