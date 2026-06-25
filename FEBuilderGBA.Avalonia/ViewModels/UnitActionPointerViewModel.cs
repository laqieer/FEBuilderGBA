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
    /// The base address, entry validity and action-id origin are resolved through
    /// <see cref="UnitActionPointerCore"/> so the editor honors the UnitActionRework
    /// patch (relocated table base, 0-based ids, <c>&amp; 0x0FFFFFFF</c> masking) exactly
    /// like WinForms <c>UnitActionPointerForm.SearchActionPointer</c>/<c>Init</c> (#1415).
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

        /// <summary>Action id of the selected entry. Non-rework ids start at 1; UnitActionRework ids are 0-based.</summary>
        public uint ActionId { get => _actionId; set => SetField(ref _actionId, value); }

        /// <summary>Human-readable action name from the <c>unitaction_</c> resource (empty if none).</summary>
        public string ActionName { get => _actionName; set => SetField(ref _actionName, value); }

        /// <summary>
        /// Resolve the base address of the unit action pointer table, honoring the
        /// UnitActionRework patch (relocated base) via <see cref="UnitActionPointerCore"/>.
        /// Returns 0 if the pointer is invalid.
        /// </summary>
        static uint ResolveBaseAddress(ROM rom) => UnitActionPointerCore.ResolveBaseAddress(rom);

        public List<AddrResult> LoadList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            uint baseAddr = ResolveBaseAddress(rom);
            if (baseAddr == 0) return new List<AddrResult>();

            bool isRework = UnitActionPointerCore.IsRework(rom);
            var actionNames = LoadActionNames();

            return EditorFormRef.BuildListWithCount(rom, baseAddr, EntrySize,
                (i, addr) => UnitActionPointerCore.IsDataExists(rom, addr, isRework),
                (i, addr) =>
                {
                    // Non-rework ids start at 1; rework ids are 0-based (mirrors WinForms Init).
                    uint id = UnitActionPointerCore.ResolveActionId(i, isRework);
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

            // Recover the action id from the offset within the table (rework-aware origin) and resolve its name.
            bool isRework = UnitActionPointerCore.IsRework(rom);
            uint baseAddr = ResolveBaseAddress(rom);
            ActionId = UnitActionPointerCore.ResolveActionIdFromAddr(addr, baseAddr, isRework);
            // Non-rework id 0 signals "below base / out of range"; rework id 0 is the valid first entry.
            bool hasName = isRework || ActionId != 0;
            ActionName = hasName ? ResolveActionName(ActionId, LoadActionNames()) : "";

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
            bool isRework = UnitActionPointerCore.IsRework(rom);
            return EditorFormRef.CountEntries(rom, baseAddr, EntrySize,
                (i, addr) => UnitActionPointerCore.IsDataExists(rom, addr, isRework));
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
