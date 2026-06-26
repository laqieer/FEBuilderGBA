using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class MonsterWMapProbabilityViewerViewModel : ViewModelBase, IDataVerifiable
    {
        static readonly List<EditorFormRef.FieldDef> _fields =
            EditorFormRef.DetectFields(new[] { "B0" });

        uint _currentAddr;
        bool _canWrite;
        uint _basePointId;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool CanWrite { get => _canWrite; set => SetField(ref _canWrite, value); }
        public uint BasePointId { get => _basePointId; set => SetField(ref _basePointId, value); }

        // --------------------------------------------------------------------
        // FE8-only gate for the three extra surfaces (#1464).
        // --------------------------------------------------------------------
        public bool IsSupported
        {
            get
            {
                ROM rom = CoreState.ROM;
                return rom != null && MonsterWMapProbabilityCore.IsSupported(rom);
            }
        }

        // --------------------------------------------------------------------
        // Surface 1 — base point list (already ported)
        // --------------------------------------------------------------------
        public List<AddrResult> LoadMonsterWMapProbabilityList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            uint ptr = rom.RomInfo.monster_wmap_base_point_pointer;
            if (ptr == 0) return new List<AddrResult>();

            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            var result = new List<AddrResult>();
            for (uint i = 0; i < MonsterWMapProbabilityCore.BasePointCount; i++)
            {
                uint addr = (uint)(baseAddr + i * 1);
                if (addr >= (uint)rom.Data.Length) break;

                uint basePointId = rom.u8(addr);
                string ptName = MonsterWMapProbabilityCore.GetWorldMapPointName(rom, basePointId);
                string name = U.ToHexString(i) + " 0x" + basePointId.ToString("X02")
                    + (string.IsNullOrEmpty(ptName) ? "" : " " + ptName);
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        public void LoadMonsterWMapProbability(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr >= (uint)rom.Data.Length) return;

            CurrentAddr = addr;
            var values = EditorFormRef.ReadFields(rom, addr, _fields);
            BasePointId = values["B0"];
            CanWrite = true;
        }

        public void WriteMonsterWMapProbability()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;
            if (CurrentAddr >= (uint)rom.Data.Length) return;

            var values = new Dictionary<string, uint> { ["B0"] = BasePointId };
            EditorFormRef.WriteFields(rom, CurrentAddr, values, _fields);
        }

        public int GetListCount() => LoadMonsterWMapProbabilityList().Count;

        // --------------------------------------------------------------------
        // Surface 2 — stage spread (monster_wmap_stage_1/2)
        // --------------------------------------------------------------------
        uint _stageAddr;
        uint _stageMapId;
        bool _stageIsEphraim;
        string _stageMapName = "";

        public uint StageAddr { get => _stageAddr; set => SetField(ref _stageAddr, value); }
        public uint StageMapId { get => _stageMapId; set => SetField(ref _stageMapId, value); }
        public bool StageIsEphraim { get => _stageIsEphraim; set => SetField(ref _stageIsEphraim, value); }
        public string StageMapName { get => _stageMapName; set => SetField(ref _stageMapName, value); }

        public List<AddrResult> LoadStageList()
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return new List<AddrResult>();
            return MonsterWMapProbabilityCore.LoadStageList(rom, StageIsEphraim);
        }

        public void LoadStage(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            StageAddr = addr;
            StageMapId = MonsterWMapProbabilityCore.ReadStageMapId(rom, addr);
            StageMapName = MapSettingCore.GetMapNameById(rom, StageMapId);
        }

        public void WriteStage()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || StageAddr == 0) return;
            MonsterWMapProbabilityCore.WriteStageMapId(rom, StageAddr, StageMapId);
        }

        // --------------------------------------------------------------------
        // Surface 3 — per-base probabilities (monster_wmap_probability_1/2)
        // --------------------------------------------------------------------
        uint _probAddr;
        bool _probIsEphraim;
        readonly uint[] _prob = new uint[MonsterWMapProbabilityCore.ProbabilityWidth];

        public uint ProbAddr { get => _probAddr; set => SetField(ref _probAddr, value); }
        public bool ProbIsEphraim { get => _probIsEphraim; set => SetField(ref _probIsEphraim, value); }

        public uint Prob0 { get => _prob[0]; set { _prob[0] = value; OnPropertyChanged(); OnPropertyChanged(nameof(ProbSum)); } }
        public uint Prob1 { get => _prob[1]; set { _prob[1] = value; OnPropertyChanged(); OnPropertyChanged(nameof(ProbSum)); } }
        public uint Prob2 { get => _prob[2]; set { _prob[2] = value; OnPropertyChanged(); OnPropertyChanged(nameof(ProbSum)); } }
        public uint Prob3 { get => _prob[3]; set { _prob[3] = value; OnPropertyChanged(); OnPropertyChanged(nameof(ProbSum)); } }
        public uint Prob4 { get => _prob[4]; set { _prob[4] = value; OnPropertyChanged(); OnPropertyChanged(nameof(ProbSum)); } }
        public uint Prob5 { get => _prob[5]; set { _prob[5] = value; OnPropertyChanged(); OnPropertyChanged(nameof(ProbSum)); } }
        public uint Prob6 { get => _prob[6]; set { _prob[6] = value; OnPropertyChanged(); OnPropertyChanged(nameof(ProbSum)); } }
        public uint Prob7 { get => _prob[7]; set { _prob[7] = value; OnPropertyChanged(); OnPropertyChanged(nameof(ProbSum)); } }
        public uint Prob8 { get => _prob[8]; set { _prob[8] = value; OnPropertyChanged(); OnPropertyChanged(nameof(ProbSum)); } }

        public string ProbSum
        {
            get
            {
                uint sum = 0;
                foreach (uint v in _prob) sum += v;
                return sum + "%";
            }
        }

        public List<AddrResult> LoadProbabilityList()
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return new List<AddrResult>();
            return MonsterWMapProbabilityCore.LoadProbabilityList(rom, ProbIsEphraim);
        }

        public List<string> GetBasePointLabels()
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return new List<string>();
            return MonsterWMapProbabilityCore.GetBasePointLabels(rom);
        }

        public void LoadProbability(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            ProbAddr = addr;
            byte[] row = MonsterWMapProbabilityCore.ReadProbabilityRow(rom, addr);
            for (int k = 0; k < _prob.Length; k++) _prob[k] = row[k];
            OnPropertyChanged(nameof(Prob0)); OnPropertyChanged(nameof(Prob1));
            OnPropertyChanged(nameof(Prob2)); OnPropertyChanged(nameof(Prob3));
            OnPropertyChanged(nameof(Prob4)); OnPropertyChanged(nameof(Prob5));
            OnPropertyChanged(nameof(Prob6)); OnPropertyChanged(nameof(Prob7));
            OnPropertyChanged(nameof(Prob8)); OnPropertyChanged(nameof(ProbSum));
        }

        public void WriteProbability()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || ProbAddr == 0) return;
            var row = new byte[MonsterWMapProbabilityCore.ProbabilityWidth];
            for (int k = 0; k < row.Length; k++) row[k] = (byte)(_prob[k] & 0xFF);
            MonsterWMapProbabilityCore.WriteProbabilityRow(rom, ProbAddr, row);
        }

        // --------------------------------------------------------------------
        // Surface 4 — skirmish events
        // --------------------------------------------------------------------
        uint _skirmishStartEvent;
        uint _skirmishEndEvent;

        public uint SkirmishStartEvent { get => _skirmishStartEvent; set => SetField(ref _skirmishStartEvent, value); }
        public uint SkirmishEndEvent { get => _skirmishEndEvent; set => SetField(ref _skirmishEndEvent, value); }

        public void LoadSkirmishEvents()
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            SkirmishStartEvent = MonsterWMapProbabilityCore.ReadSkirmishStartEvent(rom);
            SkirmishEndEvent = MonsterWMapProbabilityCore.ReadSkirmishEndEvent(rom);
        }

        public void WriteSkirmishEvents()
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            MonsterWMapProbabilityCore.WriteSkirmishEvents(rom, SkirmishStartEvent, SkirmishEndEvent);
        }

        // --------------------------------------------------------------------
        // Data verification
        // --------------------------------------------------------------------
        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["BasePointId"] = $"0x{BasePointId:X02}",
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
                ["u8@0x00"] = $"0x{rom.u8(a):X02}",
            };
        }

        public Dictionary<string, string> GetFieldOffsetMap() => new()
        {
            ["BasePointId"] = "u8@0x00",
        };
    }
}
