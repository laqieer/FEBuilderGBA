using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    /// <summary>Vennou weapon lock editor.
    /// WinForms: VennouWeaponLockForm — B0/J_0 = Lock Type or Unit/Class ID,
    /// X_LINK = linked name display, X_LINK_ICON = icon, Explain = description.
    /// Record size: 1 byte per entry (variable-length list terminated by 0x00).
    /// </summary>
    public class VennouWeaponLockViewModel : ViewModelBase, IDataVerifiable
    {
        uint _currentAddr;
        bool _canWrite;
        uint _lockTypeOrId;
        string _linkedName = "";
        string _explanation = "";

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool CanWrite { get => _canWrite; set => SetField(ref _canWrite, value); }

        /// <summary>Lock type (first entry) or Unit/Class ID (subsequent entries).
        /// WinForms: B0 / J_0.</summary>
        public uint LockTypeOrId { get => _lockTypeOrId; set => SetField(ref _lockTypeOrId, value); }

        /// <summary>Display name resolved from LockTypeOrId. WinForms: X_LINK.</summary>
        public string LinkedName { get => _linkedName; set => SetField(ref _linkedName, value); }

        /// <summary>Explanation text. WinForms: Explain TextBox.</summary>
        public string Explanation { get => _explanation; set => SetField(ref _explanation, value); }

        public void LoadEntry(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + 1 > (uint)rom.Data.Length) return;

            CurrentAddr = addr;
            LockTypeOrId = rom.u8(addr + 0);
            CanWrite = true;
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

            rom.write_u8(CurrentAddr + 0, (byte)LockTypeOrId);
        }

        public int GetListCount() => 0;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["LockTypeOrId"] = $"0x{LockTypeOrId:X02}",
                ["LinkedName"] = LinkedName,
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
