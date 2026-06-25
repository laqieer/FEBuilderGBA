using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class SoundFootStepsViewerViewModel : ViewModelBase, IDataVerifiable
    {
        static readonly List<EditorFormRef.FieldDef> _fields =
            EditorFormRef.DetectFields(new[] { "D0" });

        uint _currentAddr;
        bool _canWrite;
        uint _dataPointer;
        bool _isSwitch2Enabled;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool CanWrite { get => _canWrite; set => SetField(ref _canWrite, value); }
        public uint DataPointer { get => _dataPointer; set => SetField(ref _dataPointer, value); }

        /// <summary>
        /// True when the SoundFootSteps Switch2 jump-table signature is present.
        /// Gates the Write + List Expansion buttons — mirrors WinForms
        /// <c>SoundFootStepsForm_Load</c> (hides Write + shows the not-found
        /// error when the Switch2 is absent). Refreshed by
        /// <see cref="RefreshEnableState"/>.
        /// </summary>
        public bool IsSwitch2Enabled
        {
            get => _isSwitch2Enabled;
            set => SetField(ref _isSwitch2Enabled, value);
        }

        /// <summary>
        /// Recompute <see cref="IsSwitch2Enabled"/> from the current ROM —
        /// thin wrapper over <see cref="SoundFootStepsExpandCore.IsEnabled"/>.
        /// </summary>
        public void RefreshEnableState()
        {
            IsSwitch2Enabled = SoundFootStepsExpandCore.IsEnabled(CoreState.ROM);
        }

        public List<AddrResult> LoadSoundFootStepsList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            uint ptr = rom.RomInfo.sound_foot_steps_pointer;
            if (ptr == 0) return new List<AddrResult>();

            // Guard the FULL 4-byte pointer slot before dereferencing: ROM.p32
            // only checks `addr >= Data.Length` (not addr+3), so a truncated/
            // corrupt ROM could throw IndexOutOfRangeException. (#1449 review.)
            if (!U.isSafetyOffset(ptr + 3, rom)) return new List<AddrResult>();

            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            // Switch2 jump-table: the entry count comes from the Switch2
            // metadata (`u8(switch2 + 2) + 1`), NOT a first-NULL/non-pointer
            // stop. Mirrors WinForms ReInit (`ifr.ReInit(addr, count + 1)`) so a
            // NULL pointer between two valid entries stays in the list — and so
            // the rows immediately reflect a freshly-expanded table. Falls back
            // to the legacy first-non-pointer scan when no Switch2 is present
            // (e.g. an unpatched ROM where the editor is read-only anyway).
            var s2 = SoundFootStepsExpandCore.ReadSwitch2(rom);
            uint startClassId = s2?.Start ?? 0;
            uint totalCount = s2?.TotalCount ?? 0;
            bool useSwitch2Count = s2 != null;

            var result = new List<AddrResult>();
            for (uint i = 0; useSwitch2Count ? i < totalCount : i < 0x200; i++)
            {
                uint addr = (uint)(baseAddr + i * 4);
                if (addr + 4 > (uint)rom.Data.Length) break;

                uint value = rom.u32(addr);
                if (!useSwitch2Count && !U.isPointer(value)) break;

                uint classId = startClassId + i;
                string className = NameResolver.GetClassName(classId);
                string name = $"{U.ToHexString(classId)} {className}";
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        /// <summary>
        /// Number of classes the footstep-sound table should cover — the
        /// <c>newCount</c> WinForms <c>SwitchListExpandsButton_Click</c> passes
        /// to <c>Switch2Expands</c> (<c>ClassForm.DataCount()</c>). Delegates to
        /// the Core port <see cref="RebuildProducerCore.ClassDataCount"/>.
        /// </summary>
        public uint GetNewCount()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return 0;
            return RebuildProducerCore.ClassDataCount(rom);
        }

        /// <summary>
        /// Expand the per-class footstep-sound table to cover all classes and
        /// apply the FE8 <c>PlaySoundStepByClass</c> hardcode fix under the same
        /// undo scope. Delegates to <see cref="SoundFootStepsExpandCore.Expand"/>
        /// (the shared WinForms-parity mutation). The View owns the undo scope.
        /// Returns the new table address, or <see cref="U.NOT_FOUND"/> on
        /// failure (caller rolls back).
        /// </summary>
        public uint ExpandList(uint newCount, uint defaultJumpAddr, Undo.UndoData undo)
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return U.NOT_FOUND;
            return SoundFootStepsExpandCore.Expand(rom, newCount, defaultJumpAddr, undo);
        }

        public void LoadSoundFootSteps(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + 4 > (uint)rom.Data.Length) return;

            CurrentAddr = addr;
            var values = EditorFormRef.ReadFields(rom, addr, _fields);
            DataPointer = values["D0"];
            CanWrite = true;
        }

        public void WriteSoundFootSteps()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;
            if (CurrentAddr + 4 > (uint)rom.Data.Length) return;

            var values = new Dictionary<string, uint> { ["D0"] = DataPointer };
            EditorFormRef.WriteFields(rom, CurrentAddr, values, _fields);
        }

        public int GetListCount() => LoadSoundFootStepsList().Count;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["DataPointer"] = $"0x{DataPointer:X08}",
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
            ["DataPointer"] = "u32@0x00",
        };
    }
}
