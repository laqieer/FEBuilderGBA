using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class ChangeRecord
    {
        public uint Address { get; set; }
        public byte ChangeID { get; set; }
        public byte X { get; set; }
        public byte Y { get; set; }
        public byte Width { get; set; }
        public byte Height { get; set; }
        public uint TileDataPtr { get; set; }

        public string DisplayName => $"#{ChangeID:X02} ({X},{Y}) {Width}x{Height}";
    }

    public class MapChangeViewModel : ViewModelBase, IDataVerifiable
    {
        uint _currentAddr;
        uint _changePointer;
        bool _canWrite;

        // Inner record fields
        byte _recChangeID;
        byte _recX;
        byte _recY;
        byte _recWidth;
        byte _recHeight;
        uint _recTileDataPtr;
        uint _selectedRecordAddr;
        bool _canWriteRecord;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public uint ChangePointer { get => _changePointer; set => SetField(ref _changePointer, value); }
        public bool CanWrite { get => _canWrite; set => SetField(ref _canWrite, value); }

        public byte RecChangeID { get => _recChangeID; set => SetField(ref _recChangeID, value); }
        public byte RecX { get => _recX; set => SetField(ref _recX, value); }
        public byte RecY { get => _recY; set => SetField(ref _recY, value); }
        public byte RecWidth { get => _recWidth; set => SetField(ref _recWidth, value); }
        public byte RecHeight { get => _recHeight; set => SetField(ref _recHeight, value); }
        public uint RecTileDataPtr { get => _recTileDataPtr; set => SetField(ref _recTileDataPtr, value); }
        public uint SelectedRecordAddr { get => _selectedRecordAddr; set => SetField(ref _selectedRecordAddr, value); }
        public bool CanWriteRecord { get => _canWriteRecord; set => SetField(ref _canWriteRecord, value); }

        public List<AddrResult> LoadMapChangeList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            uint ptr = rom.RomInfo.map_mapchange_pointer;
            if (ptr == 0) return new List<AddrResult>();

            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            var result = new List<AddrResult>();
            for (uint i = 0; i < 0x200; i++)
            {
                uint addr = (uint)(baseAddr + i * 4);
                if (addr + 3 >= (uint)rom.Data.Length) break;

                uint pointer = rom.u32(addr);
                // Stop if we hit clearly invalid data
                if (pointer == 0xFFFFFFFF) break;

                string ptrStr = U.isPointer(pointer)
                    ? "0x" + pointer.ToString("X08")
                    : (pointer == 0 ? "NULL" : "0x" + pointer.ToString("X08"));
                string name = U.ToHexString(i) + " Change " + ptrStr;
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        public void LoadMapChange(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;

            if (addr + 3 >= (uint)rom.Data.Length) return;

            CurrentAddr = addr;
            ChangePointer = rom.u32(addr);

            CanWrite = true;
        }

        public void WriteMapChange()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;
            rom.write_u32(CurrentAddr, ChangePointer);
        }

        /// <summary>Load change records from the pointer stored at the current outer address.</summary>
        public List<ChangeRecord> LoadChangeRecords()
        {
            ROM rom = CoreState.ROM;
            var records = new List<ChangeRecord>();
            if (rom == null) return records;

            uint pointer = ChangePointer;
            if (!U.isPointer(pointer)) return records;

            uint baseAddr = U.toOffset(pointer);
            if (!U.isSafetyOffset(baseAddr)) return records;

            for (int i = 0; i < 256; i++)
            {
                uint addr = (uint)(baseAddr + i * 12);
                if (addr + 11 >= (uint)rom.Data.Length) break;

                uint changeId = rom.u8(addr);
                if (changeId == 0xFF) break;

                records.Add(new ChangeRecord
                {
                    Address = addr,
                    ChangeID = (byte)changeId,
                    X = (byte)rom.u8(addr + 1),
                    Y = (byte)rom.u8(addr + 2),
                    Width = (byte)rom.u8(addr + 3),
                    Height = (byte)rom.u8(addr + 4),
                    TileDataPtr = rom.u32(addr + 8),
                });
            }
            return records;
        }

        /// <summary>Load a single change record into the editing fields.</summary>
        public void LoadRecord(ChangeRecord record)
        {
            SelectedRecordAddr = record.Address;
            RecChangeID = record.ChangeID;
            RecX = record.X;
            RecY = record.Y;
            RecWidth = record.Width;
            RecHeight = record.Height;
            RecTileDataPtr = record.TileDataPtr;
            CanWriteRecord = true;
        }

        /// <summary>Write the current record fields back to ROM.</summary>
        public void WriteChangeRecord()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || SelectedRecordAddr == 0) return;

            uint a = SelectedRecordAddr;
            rom.write_u8(a + 0, RecChangeID);
            rom.write_u8(a + 1, RecX);
            rom.write_u8(a + 2, RecY);
            rom.write_u8(a + 3, RecWidth);
            rom.write_u8(a + 4, RecHeight);
            rom.write_u32(a + 8, RecTileDataPtr);
        }

        public int GetListCount() => LoadMapChangeList().Count;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["ChangePointer"] = $"0x{ChangePointer:X08}",
            };
        }

        public Dictionary<string, string> GetRawRomReport()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return new Dictionary<string, string>();
            uint a = CurrentAddr;
            var report = new Dictionary<string, string>
            {
                ["addr"] = $"0x{a:X08}",
                ["u32@0x00"] = $"0x{rom.u32(a + 0):X08}",
            };

            // Include selected inner record fields if available
            if (SelectedRecordAddr > 0 && SelectedRecordAddr + 12 <= (uint)rom.Data.Length)
            {
                uint r = SelectedRecordAddr;
                report["u8@0_ChangeID"] = $"0x{rom.u8(r + 0):X02}";
                report["u8@1_X"] = $"0x{rom.u8(r + 1):X02}";
                report["u8@2_Y"] = $"0x{rom.u8(r + 2):X02}";
                report["u8@3_Width"] = $"0x{rom.u8(r + 3):X02}";
                report["u8@4_Height"] = $"0x{rom.u8(r + 4):X02}";
                report["u32@8_TileData"] = $"0x{rom.u32(r + 8):X08}";
            }

            return report;
        }
    }
}
