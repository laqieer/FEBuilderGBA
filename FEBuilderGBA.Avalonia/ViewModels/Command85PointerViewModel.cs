using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    /// <summary>
    /// Command-0x85 Pointer Table editor — port of WinForms <c>Command85PointerForm</c>.
    /// The table at <c>p32(RomInfo.command_85_pointer_table_pointer)</c> holds 4-byte function
    /// pointers, one per battle-animation command code starting at <c>0x19</c> (so list row
    /// <c>i</c> = command id <c>i + 0x19</c>). Each pointer (D0) is editable; the command name
    /// comes from the version-specific <c>battleanime_85command_</c> resource.
    /// </summary>
    public class Command85PointerViewModel : ViewModelBase, IDataVerifiable
    {
        static readonly List<EditorFormRef.FieldDef> _fields =
            EditorFormRef.DetectFields(new[] { "D0" });

        public const uint FirstCommandId = 0x19;
        public const uint EntrySize = 4;

        uint _currentAddr;
        bool _isLoaded;
        uint _pointerValue;
        uint _commandId;
        string _commandName = string.Empty;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        /// <summary>Pointer value at the command 0x85 address.</summary>
        public uint PointerValue { get => _pointerValue; set => SetField(ref _pointerValue, value); }
        /// <summary>Command id of the selected entry (0x19-based, WinForms convention).</summary>
        public uint CommandId { get => _commandId; set => SetField(ref _commandId, value); }
        /// <summary>Human-readable command name from the battleanime_85command_ resource (empty if none).</summary>
        public string CommandName { get => _commandName; set => SetField(ref _commandName, value); }

        public List<AddrResult> LoadList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            uint pointer = rom.RomInfo.command_85_pointer_table_pointer;
            if (pointer == 0) return new List<AddrResult>();
            uint baseAddr = rom.p32(pointer);
            if (!U.isSafetyOffset(baseAddr, rom)) return new List<AddrResult>();

            var commandNames = LoadCommandNames();

            var result = new List<AddrResult>();
            for (int i = 0; i < 256; i++)
            {
                uint addr = baseAddr + (uint)i * EntrySize;
                if (addr + EntrySize > (uint)rom.Data.Length) break;
                uint ptr = rom.u32(addr);
                // Mirror WinForms cond: pointer-or-NULL, NULL allowed, reject sub-0x08000100.
                if (!U.isPointerOrNULL(ptr)) break;
                if (ptr != 0 && ptr <= 0x08000100) break;

                uint id = (uint)i + FirstCommandId;
                result.Add(new AddrResult(addr, MakeLabel(id, commandNames), id));
            }
            return result;
        }

        public void LoadEntry(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + EntrySize > (uint)rom.Data.Length) return;
            CurrentAddr = addr;
            var values = EditorFormRef.ReadFields(rom, addr, _fields);
            PointerValue = values["D0"];

            // Recover the 0x19-based command id from the table offset and resolve its name.
            uint pointer = rom.RomInfo.command_85_pointer_table_pointer;
            uint baseAddr = pointer != 0 ? rom.p32(pointer) : 0;
            CommandId = (baseAddr != 0 && addr >= baseAddr) ? (addr - baseAddr) / EntrySize + FirstCommandId : 0;
            CommandName = CommandId != 0 ? U.at(LoadCommandNames(), CommandId) : "";

            IsLoaded = true;
        }

        public void Write()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;
            if (CurrentAddr + EntrySize > (uint)rom.Data.Length) return;
            var values = new Dictionary<string, uint> { ["D0"] = PointerValue };
            EditorFormRef.WriteFields(rom, CurrentAddr, values, _fields);
        }

        public int GetListCount() => LoadList().Count;

        /// <summary>Load the version-specific command-name dictionary (empty on any failure).</summary>
        static Dictionary<uint, string> LoadCommandNames()
        {
            try { return U.LoadDicResource(U.ConfigDataFilename("battleanime_85command_")); }
            catch { return new Dictionary<uint, string>(); }
        }

        /// <summary>List label mirroring WinForms: hex id + "C{id:X02}", plus the command name when known.</summary>
        static string MakeLabel(uint id, Dictionary<uint, string> commandNames)
        {
            string label = U.ToHexString(id) + " C" + id.ToString("X02");
            string name = U.at(commandNames, id);
            return string.IsNullOrEmpty(name) ? label : label + " " + name;
        }

        public Dictionary<string, string> GetDataReport()
        {
            // Note: CommandName is a display-only field (not ROM-backed), so it is
            // excluded from the report.  Including it as "" would cause the
            // data-verify cross-check to skip the entire record.
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["Pointer Value"] = $"0x{PointerValue:X08}",
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
                ["u32@0x00"] = $"0x{rom.u32(a + 0):X08}",
            };
        }

        public Dictionary<string, string> GetFieldOffsetMap() => new()
        {
            ["Pointer Value"] = "u32@0x00",
        };
    }
}
