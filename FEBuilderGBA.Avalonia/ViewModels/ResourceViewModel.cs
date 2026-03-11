using System;
using System.IO;
using System.Linq;
using System.Text;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class ResourceViewModel : ViewModelBase
    {
        bool _isLoaded;
        string _romInfoText = "";
        string _configInfoText = "";
        string _romSectionText = "";

        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public string RomInfoText { get => _romInfoText; set => SetField(ref _romInfoText, value); }
        public string ConfigInfoText { get => _configInfoText; set => SetField(ref _configInfoText, value); }
        public string RomSectionText { get => _romSectionText; set => SetField(ref _romSectionText, value); }

        public void Initialize()
        {
            UpdateRomInfo();
            UpdateConfigInfo();
            UpdateRomSections();
            IsLoaded = true;
        }

        void UpdateRomInfo()
        {
            var rom = CoreState.ROM;
            if (rom == null)
            {
                RomInfoText = "No ROM loaded.";
                return;
            }

            var sb = new StringBuilder();
            sb.AppendLine($"File:    {rom.Filename ?? "(none)"}");
            sb.AppendLine($"Version: {rom.RomInfo?.VersionToFilename ?? "Unknown"}");
            sb.AppendLine($"Size:    {rom.Data.Length:N0} bytes (0x{rom.Data.Length:X})");

            // ROM title from header (bytes 0xA0-0xAB)
            if (rom.Data.Length >= 0xAC)
            {
                string title = "";
                for (int i = 0xA0; i < 0xAC; i++)
                {
                    byte b = rom.Data[i];
                    if (b >= 0x20 && b < 0x7F) title += (char)b;
                    else break;
                }
                sb.AppendLine($"Title:   {title}");
            }

            // ROM code from header (bytes 0xAC-0xAF)
            if (rom.Data.Length >= 0xB0)
            {
                string code = "";
                for (int i = 0xAC; i < 0xB0; i++)
                {
                    byte b = rom.Data[i];
                    if (b >= 0x20 && b < 0x7F) code += (char)b;
                }
                sb.AppendLine($"Code:    {code}");
            }

            // Estimate trailing free space
            long trailingFree = 0;
            for (int i = rom.Data.Length - 1; i >= 0; i--)
            {
                if (rom.Data[i] == 0x00 || rom.Data[i] == 0xFF)
                    trailingFree++;
                else
                    break;
            }
            sb.AppendLine($"Trailing Free: ~{trailingFree:N0} bytes ({(double)trailingFree / rom.Data.Length:P1})");

            // Count scattered free blocks (contiguous 0x00/0xFF runs of 16+ bytes)
            int freeBlocks = 0;
            long scatteredFree = 0;
            int runLen = 0;
            for (int i = 0; i < rom.Data.Length; i++)
            {
                if (rom.Data[i] == 0x00 || rom.Data[i] == 0xFF)
                {
                    runLen++;
                }
                else
                {
                    if (runLen >= 16)
                    {
                        freeBlocks++;
                        scatteredFree += runLen;
                    }
                    runLen = 0;
                }
            }
            if (runLen >= 16) { freeBlocks++; scatteredFree += runLen; }
            sb.AppendLine($"Free Blocks (>=16B): {freeBlocks} blocks, ~{scatteredFree:N0} bytes total");

            // Data table counts
            var info = rom.RomInfo;
            if (info != null)
            {
                int unitCount = CountTableEntries(info.unit_pointer, info.unit_datasize);
                int classCount = CountTableEntries(info.class_pointer, info.class_datasize);
                int itemCount = CountTableEntries(info.item_pointer, info.item_datasize);
                sb.AppendLine();
                sb.AppendLine($"Units:   {unitCount} entries");
                sb.AppendLine($"Classes: {classCount} entries");
                sb.AppendLine($"Items:   {itemCount} entries");
            }

            RomInfoText = sb.ToString().TrimEnd();
        }

        int CountTableEntries(uint pointer, uint dataSize)
        {
            var rom = CoreState.ROM;
            if (rom == null || pointer == 0 || dataSize == 0) return 0;
            // Count entries until we find an all-zero record or hit ROM end
            int count = 0;
            for (uint addr = pointer; addr + dataSize <= (uint)rom.Data.Length; addr += dataSize)
            {
                // Check if first 2 bytes (text ID) are zero = empty entry
                if (rom.u16(addr) == 0) break;
                count++;
                if (count > 512) break; // safety limit
            }
            return count;
        }

        void UpdateConfigInfo()
        {
            var sb = new StringBuilder();
            string configDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config");
            sb.AppendLine($"Config Directory: {configDir}");
            sb.AppendLine($"Config Exists:    {Directory.Exists(configDir)}");

            if (CoreState.ROM?.RomInfo != null)
            {
                string ver = CoreState.ROM.RomInfo.VersionToFilename;
                string patchDir = Path.Combine(configDir, "patch2", ver);
                sb.AppendLine($"Patch Directory:  {patchDir}");
                bool patchExists = Directory.Exists(patchDir);
                sb.AppendLine($"Patches Available: {(patchExists ? "Yes" : "No")}");
                if (patchExists)
                {
                    try
                    {
                        int patchCount = Directory.GetDirectories(patchDir).Length;
                        sb.AppendLine($"Patch Count:      {patchCount} patches");
                    }
                    catch { /* ignore */ }
                }
            }

            // Translation info
            string transDir = Path.Combine(configDir, "translate");
            if (Directory.Exists(transDir))
            {
                try
                {
                    var langs = Directory.GetFiles(transDir, "*.txt")
                        .Select(f => Path.GetFileNameWithoutExtension(f))
                        .ToArray();
                    sb.AppendLine($"Languages:        {string.Join(", ", langs)}");
                }
                catch { /* ignore */ }
            }

            ConfigInfoText = sb.ToString().TrimEnd();
        }

        void UpdateRomSections()
        {
            var rom = CoreState.ROM;
            if (rom?.RomInfo == null)
            {
                RomSectionText = "";
                return;
            }

            var sb = new StringBuilder();
            var info = rom.RomInfo;

            sb.AppendLine("ROM Data Section Pointers:");
            sb.AppendLine($"  Units:            0x{info.unit_pointer:X08} (size: {info.unit_datasize} bytes/entry)");
            sb.AppendLine($"  Classes:          0x{info.class_pointer:X08} (size: {info.class_datasize} bytes/entry)");
            sb.AppendLine($"  Items:            0x{info.item_pointer:X08} (size: {info.item_datasize} bytes/entry)");
            sb.AppendLine($"  Text:             0x{info.text_pointer:X08}");
            sb.AppendLine($"  Portrait:         0x{info.portrait_pointer:X08}");
            sb.AppendLine($"  Map Pointer:      0x{info.map_map_pointer_pointer:X08}");
            sb.AppendLine($"  Compress Border:  0x{info.compress_image_borderline_address:X08}");

            RomSectionText = sb.ToString().TrimEnd();
        }
    }
}
