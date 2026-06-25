using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using global::Avalonia.Media;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    /// <summary>
    /// One editable AoE grid cell. <see cref="Value"/> is the 0..255 mask byte;
    /// <see cref="IsCenter"/> drives the WinForms-style center highlight
    /// (<see cref="CellBackground"/> is the highlight brush bound by the View).
    /// </summary>
    public sealed class AoeCell : ViewModelBase
    {
        static readonly IBrush CenterBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0xE0, 0x80));
        static readonly IBrush NormalBrush = Brushes.Transparent;

        uint _value;
        bool _isCenter;
        IBrush _cellBackground = NormalBrush;

        public uint Value { get => _value; set => SetField(ref _value, value); }

        public bool IsCenter
        {
            get => _isCenter;
            set
            {
                if (SetField(ref _isCenter, value))
                    CellBackground = value ? CenterBrush : NormalBrush;
            }
        }

        /// <summary>Highlight brush (center cell = amber, others = transparent).</summary>
        public IBrush CellBackground { get => _cellBackground; set => SetField(ref _cellBackground, value); }
    }

    /// <summary>
    /// Area-of-Effect Range editor ViewModel. Real port of WinForms
    /// <c>AOERANGEForm</c> (manual address input + dynamic w×h grid +
    /// repoint-on-write). Backed by <see cref="AoeRangeCore"/> for all ROM I/O.
    /// </summary>
    public class AOERANGEViewModel : ViewModelBase, IDataVerifiable
    {
        uint _currentAddr;
        uint _parentPointerSlot;
        bool _isLoaded;
        uint _width;
        uint _height;
        uint _centerX;
        uint _centerY;
        string _status = string.Empty;

        /// <summary>Address of the record currently loaded (0 = none).</summary>
        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }

        /// <summary>
        /// ROM offset of the parent pointer slot that references this record, when
        /// the editor was reached from a parent (the WinForms <c>ParentNumnic</c>).
        /// 0 in the standalone manual-address path.
        /// </summary>
        public uint ParentPointerSlot { get => _parentPointerSlot; set => SetField(ref _parentPointerSlot, value); }

        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }

        /// <summary>AoE grid width (offset 0).</summary>
        public uint Width { get => _width; set => SetField(ref _width, value); }
        /// <summary>AoE grid height (offset 1).</summary>
        public uint Height { get => _height; set => SetField(ref _height, value); }
        /// <summary>Center point X coordinate (offset 2).</summary>
        public uint CenterX { get => _centerX; set => SetField(ref _centerX, value); }
        /// <summary>Center point Y coordinate (offset 3).</summary>
        public uint CenterY { get => _centerY; set => SetField(ref _centerY, value); }

        /// <summary>Last write/status message (success or refusal reason).</summary>
        public string Status { get => _status; set => SetField(ref _status, value); }

        /// <summary>The w×h editable grid cells (row-major).</summary>
        public ObservableCollection<AoeCell> Cells { get; } = new();

        /// <summary>
        /// Read a record at <paramref name="addr"/> into the header + grid. On an
        /// unsafe/oob address the header is cleared and the grid emptied (no throw).
        /// </summary>
        public void LoadEntry(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;

            var data = AoeRangeCore.ReadAoeRange(rom, addr);
            if (data == null)
            {
                CurrentAddr = U.toOffset(addr);
                Width = Height = CenterX = CenterY = 0;
                Cells.Clear();
                IsLoaded = false;
                Status = $"No valid AOE record at 0x{U.toOffset(addr):X08}.";
                return;
            }

            CurrentAddr = U.toOffset(addr);
            Width = data.Width;
            Height = data.Height;
            CenterX = data.CenterX;
            CenterY = data.CenterY;

            Cells.Clear();
            for (int i = 0; i < data.Cells.Length; i++)
            {
                Cells.Add(new AoeCell { Value = data.Cells[i] });
            }
            UpdateCenterMark();
            IsLoaded = true;
            Status = $"Loaded 0x{CurrentAddr:X08} ({Width}×{Height}).";
        }

        /// <summary>
        /// Resize the grid to <see cref="Width"/>×<see cref="Height"/>, preserving
        /// overlapping cell values by their <c>(x,y)</c> position across a width
        /// change — the faithful WinForms <c>MapData</c> behavior (ports
        /// <c>ReloadMapData</c> + <c>UpdateControlToMapData</c>). The caller passes
        /// the PREVIOUS width/height so the overlap can be mapped correctly.
        /// </summary>
        public void ResizeGridPreserving(int oldWidth, int oldHeight)
        {
            var old = new uint[Math.Max(0, oldWidth) * Math.Max(0, oldHeight)];
            for (int i = 0; i < old.Length && i < Cells.Count; i++) old[i] = Cells[i].Value;

            int newW = (int)Width;
            int newH = (int)Height;
            Cells.Clear();
            for (int y = 0; y < newH; y++)
            {
                for (int x = 0; x < newW; x++)
                {
                    uint v = 0;
                    if (x < oldWidth && y < oldHeight)
                    {
                        int oi = x + y * oldWidth;
                        if (oi >= 0 && oi < old.Length) v = old[oi];
                    }
                    Cells.Add(new AoeCell { Value = v });
                }
            }
            UpdateCenterMark();
        }

        /// <summary>Recompute the highlighted center cell. Ports <c>UpdateCenterMark</c>.</summary>
        public void UpdateCenterMark()
        {
            int center = AoeRangeCore.CenterIndex(Width, Height, CenterX, CenterY);
            for (int i = 0; i < Cells.Count; i++)
            {
                Cells[i].IsCenter = (i == center);
            }
        }

        /// <summary>
        /// Persist the header + grid via <see cref="AoeRangeCore.WriteAoeRange"/>.
        /// On a move the returned (new) address is adopted into
        /// <see cref="CurrentAddr"/>. <see cref="Status"/> is set in every case.
        /// </summary>
        /// <returns><c>true</c> when a mutation was performed (in-place or move).</returns>
        public bool Write()
        {
            ROM rom = CoreState.ROM;
            if (rom == null)
            {
                Status = "ROM not loaded.";
                return false;
            }
            if (!IsLoaded || (CurrentAddr == 0 && ParentPointerSlot == 0))
            {
                // Standalone, no address loaded and no parent slot: a header-only or
                // fresh write here would be orphaned — refuse (matches the old
                // addr-0 no-op, but now explicit).
                Status = "No AOE record loaded. Enter an address and Reload first.";
                return false;
            }

            var cells = new byte[Cells.Count];
            for (int i = 0; i < Cells.Count; i++) cells[i] = (byte)(Cells[i].Value & 0xFF);

            AoeRangeCore.WriteResult r = AoeRangeCore.WriteAoeRange(
                rom, ParentPointerSlot, CurrentAddr, Width, Height, CenterX, CenterY, cells);

            switch (r.Status)
            {
                case AoeRangeCore.WriteStatus.InPlace:
                    Status = $"Wrote in place at 0x{r.Address:X08}.";
                    return true;
                case AoeRangeCore.WriteStatus.Moved:
                    CurrentAddr = r.Address;
                    Status = $"Moved to 0x{r.Address:X08}; repointed {r.RepointedSlots} reference(s).";
                    return true;
                default:
                    Status = r.Message;
                    return false;
            }
        }

        public int GetListCount() => IsLoaded && CurrentAddr != 0 ? 1 : 0;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["Width"] = $"0x{Width:X02}",
                ["Height"] = $"0x{Height:X02}",
                ["CenterX"] = $"0x{CenterX:X02}",
                ["CenterY"] = $"0x{CenterY:X02}",
                ["CellCount"] = $"0x{Cells.Count:X04}",
            };
        }

        public Dictionary<string, string> GetFieldOffsetMap()
        {
            return new Dictionary<string, string>
            {
                ["Width"] = "u8@0x00_Width",
                ["Height"] = "u8@0x01_Height",
                ["CenterX"] = "u8@0x02_CenterX",
                ["CenterY"] = "u8@0x03_CenterY",
            };
        }

        public Dictionary<string, string> GetRawRomReport()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return new Dictionary<string, string>();

            uint a = CurrentAddr;
            if (!U.isSafetyOffset(a + 2, rom)) return new Dictionary<string, string>();
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{a:X08}",
                ["u8@0x00_Width"] = $"0x{rom.u8(a + 0):X02}",
                ["u8@0x01_Height"] = $"0x{rom.u8(a + 1):X02}",
                ["u8@0x02_CenterX"] = $"0x{rom.u8(a + 2):X02}",
                ["u8@0x03_CenterY"] = $"0x{rom.u8(a + 3):X02}",
            };
        }
    }
}
