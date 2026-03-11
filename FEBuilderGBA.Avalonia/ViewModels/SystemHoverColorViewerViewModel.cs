using System.Collections.Generic;
using System.Text;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    /// <summary>
    /// Viewer for system area gradation palette colors (move/attack/staff ranges).
    /// Uses systemarea_*_gradation_palette_pointer from Core ROMFEINFO.
    /// Each palette set has 10 u16 GBA color entries (20 bytes).
    /// </summary>
    public class SystemHoverColorViewerViewModel : ViewModelBase
    {
        bool _canWrite;
        string _statusMessage = "";
        int _selectedFilterIndex;
        readonly string[] _filterNames = { "Move Range", "Attack Range", "Staff Range" };

        public bool CanWrite { get => _canWrite; set => SetField(ref _canWrite, value); }
        public string StatusMessage { get => _statusMessage; set => SetField(ref _statusMessage, value); }
        public int SelectedFilterIndex { get => _selectedFilterIndex; set => SetField(ref _selectedFilterIndex, value); }
        public string[] FilterNames => _filterNames;

        uint GetPointerForFilter(int filterIndex)
        {
            var rom = CoreState.ROM;
            if (rom?.RomInfo == null) return 0;
            return filterIndex switch
            {
                0 => rom.RomInfo.systemarea_move_gradation_palette_pointer,
                1 => rom.RomInfo.systemarea_attack_gradation_palette_pointer,
                2 => rom.RomInfo.systemarea_staff_gradation_palette_pointer,
                _ => 0
            };
        }

        public List<AddrResult> LoadHoverColorList()
        {
            return LoadColorList(SelectedFilterIndex);
        }

        public List<AddrResult> LoadColorList(int filterIndex)
        {
            var result = new List<AddrResult>();
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return result;

            uint pointer = GetPointerForFilter(filterIndex);
            if (pointer == 0) return result;

            uint baseAddr = rom.p32(pointer);
            if (!U.isSafetyOffset(baseAddr, rom)) return result;

            // 10 u16 color entries = 20 bytes
            int colorCount = 10;
            for (int i = 0; i < colorCount; i++)
            {
                uint addr = baseAddr + (uint)(i * 2);
                if (addr + 2 > (uint)rom.Data.Length) break;
                uint color = rom.u16(addr);
                string colorStr = GbaColorToHex(color);
                result.Add(new AddrResult(addr, $"Color {i}: 0x{color:X4} ({colorStr})", color));
            }
            return result;
        }

        public void LoadHoverColor(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null)
            {
                StatusMessage = "No ROM loaded.";
                return;
            }
            if (addr + 2 > (uint)rom.Data.Length)
            {
                StatusMessage = "Address out of range.";
                return;
            }

            uint color = rom.u16(addr);
            int r = (int)(color & 0x1F) * 8;
            int g = (int)((color >> 5) & 0x1F) * 8;
            int b = (int)((color >> 10) & 0x1F) * 8;

            var sb = new StringBuilder();
            sb.AppendLine($"Address:   0x{addr:X08}");
            sb.AppendLine($"Raw Value: 0x{color:X4}");
            sb.AppendLine($"GBA RGB5:  R={color & 0x1F}, G={(color >> 5) & 0x1F}, B={(color >> 10) & 0x1F}");
            sb.AppendLine($"RGB888:    R={r}, G={g}, B={b}");
            sb.AppendLine($"Hex RGB:   #{r:X2}{g:X2}{b:X2}");

            StatusMessage = sb.ToString();
            CanWrite = true;
        }

        static string GbaColorToHex(uint color)
        {
            int r = (int)(color & 0x1F) * 8;
            int g = (int)((color >> 5) & 0x1F) * 8;
            int b = (int)((color >> 10) & 0x1F) * 8;
            return $"#{r:X2}{g:X2}{b:X2}";
        }
    }
}
