using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class GraphicsToolPatchMakerViewViewModel : ViewModelBase
    {
        bool _isLoaded;
        string _statusMessage = "Graphics Patch Maker creates patches from graphics changes.\nSelect an original ROM and a modified ROM, then click Generate to compare.";
        string _patchText = string.Empty;
        string _originalRomPath = string.Empty;
        string _modifiedRomPath = string.Empty;
        int _diffRegionCount;
        int _totalChangedBytes;
        bool _canGenerate;
        bool _canSave;

        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public string StatusMessage { get => _statusMessage; set => SetField(ref _statusMessage, value); }
        /// <summary>Generated patch text content for saving.</summary>
        public string PatchText { get => _patchText; set => SetField(ref _patchText, value); }
        public string OriginalRomPath { get => _originalRomPath; set { if (SetField(ref _originalRomPath, value)) UpdateCanGenerate(); } }
        public string ModifiedRomPath { get => _modifiedRomPath; set { if (SetField(ref _modifiedRomPath, value)) UpdateCanGenerate(); } }
        public int DiffRegionCount { get => _diffRegionCount; set => SetField(ref _diffRegionCount, value); }
        public int TotalChangedBytes { get => _totalChangedBytes; set => SetField(ref _totalChangedBytes, value); }
        public bool CanGenerate { get => _canGenerate; set => SetField(ref _canGenerate, value); }
        public bool CanSave { get => _canSave; set => SetField(ref _canSave, value); }

        /// <summary>Diff regions found during comparison: (offset, data).</summary>
        public List<DiffRegion> DiffRegions { get; private set; } = new();

        public void Initialize()
        {
            IsLoaded = true;
        }

        void UpdateCanGenerate()
        {
            CanGenerate = !string.IsNullOrEmpty(_originalRomPath)
                       && !string.IsNullOrEmpty(_modifiedRomPath)
                       && File.Exists(_originalRomPath)
                       && File.Exists(_modifiedRomPath);
        }

        /// <summary>
        /// Compare two ROMs byte-by-byte, grouping consecutive differences into regions.
        /// </summary>
        public void GeneratePatch()
        {
            if (!CanGenerate) return;

            byte[] original = File.ReadAllBytes(_originalRomPath);
            byte[] modified = File.ReadAllBytes(_modifiedRomPath);

            int minLen = Math.Min(original.Length, modified.Length);
            int maxLen = Math.Max(original.Length, modified.Length);

            var regions = new List<DiffRegion>();
            int i = 0;

            while (i < maxLen)
            {
                byte origByte = i < original.Length ? original[i] : (byte)0x00;
                byte modByte = i < modified.Length ? modified[i] : (byte)0x00;

                if (origByte != modByte)
                {
                    // Start of a diff region
                    int start = i;
                    var data = new List<byte>();

                    while (i < maxLen)
                    {
                        origByte = i < original.Length ? original[i] : (byte)0x00;
                        modByte = i < modified.Length ? modified[i] : (byte)0x00;

                        if (origByte == modByte)
                        {
                            // Allow small gaps (up to 8 equal bytes) to merge nearby regions
                            int gapEnd = Math.Min(i + 8, maxLen);
                            bool moreChanges = false;
                            for (int g = i + 1; g < gapEnd; g++)
                            {
                                byte go = g < original.Length ? original[g] : (byte)0x00;
                                byte gm = g < modified.Length ? modified[g] : (byte)0x00;
                                if (go != gm) { moreChanges = true; break; }
                            }
                            if (!moreChanges) break;
                        }

                        data.Add(modByte);
                        i++;
                    }

                    regions.Add(new DiffRegion(start, data.ToArray()));
                }
                else
                {
                    i++;
                }
            }

            DiffRegions = regions;
            DiffRegionCount = regions.Count;

            int totalBytes = 0;
            foreach (var r in regions) totalBytes += r.Data.Length;
            TotalChangedBytes = totalBytes;

            // Build patch text
            var sb = new StringBuilder();
            sb.AppendLine("; FEBuilderGBA Graphics Patch");
            sb.AppendLine($"; Original: {Path.GetFileName(_originalRomPath)} ({original.Length} bytes)");
            sb.AppendLine($"; Modified: {Path.GetFileName(_modifiedRomPath)} ({modified.Length} bytes)");
            sb.AppendLine($"; Regions: {regions.Count}, Total changed bytes: {totalBytes}");
            sb.AppendLine($"; Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine();

            foreach (var region in regions)
            {
                sb.AppendLine($"@0x{region.Offset:X8} len=0x{region.Data.Length:X}");
                // Write hex data in rows of 16 bytes
                for (int row = 0; row < region.Data.Length; row += 16)
                {
                    int count = Math.Min(16, region.Data.Length - row);
                    sb.Append("  ");
                    for (int col = 0; col < count; col++)
                    {
                        sb.Append(region.Data[row + col].ToString("X2"));
                        if (col < count - 1) sb.Append(' ');
                    }
                    sb.AppendLine();
                }
                sb.AppendLine();
            }

            PatchText = sb.ToString();
            CanSave = regions.Count > 0;

            if (regions.Count == 0)
                StatusMessage = "No differences found between the two ROMs.";
            else
                StatusMessage = $"Found {regions.Count} changed region(s), {totalBytes} byte(s) total.";
        }

        /// <summary>
        /// Save the generated patch to a file. Returns true on success.
        /// </summary>
        public bool SavePatch(string outputPath)
        {
            if (string.IsNullOrEmpty(PatchText)) return false;

            File.WriteAllText(outputPath, PatchText, Encoding.UTF8);
            StatusMessage = $"Patch saved to {Path.GetFileName(outputPath)}";
            return true;
        }

        /// <summary>Represents a contiguous region of changed bytes.</summary>
        public readonly struct DiffRegion
        {
            public int Offset { get; }
            public byte[] Data { get; }

            public DiffRegion(int offset, byte[] data)
            {
                Offset = offset;
                Data = data;
            }
        }
    }
}
