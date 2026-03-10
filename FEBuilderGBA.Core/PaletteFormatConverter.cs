using System;
using System.Collections.Generic;
using System.Text;

namespace FEBuilderGBA
{
    /// <summary>
    /// Palette format identifiers for import/export.
    /// </summary>
    public enum PaletteFormat
    {
        /// <summary>Raw GBA BGR555 little-endian, 2 bytes per color.</summary>
        GbaRaw,
        /// <summary>JASC-PAL text format (Aseprite, GIMP, Paint Shop Pro).</summary>
        JascPal,
        /// <summary>Adobe ACT binary format (Photoshop).</summary>
        AdobeAct,
        /// <summary>GIMP GPL text palette format.</summary>
        GimpGpl,
        /// <summary>Hex text, one RRGGBB per line.</summary>
        HexText,
    }

    /// <summary>
    /// Converts GBA palettes (BGR555) to/from popular image editor palette formats.
    /// No IImageService dependency — uses inline bit-shift math.
    /// </summary>
    public static class PaletteFormatConverter
    {
        /// <summary>
        /// Export GBA raw palette bytes to the specified format.
        /// </summary>
        /// <param name="gbaPalette">Raw GBA palette (2 bytes/color, BGR555 LE).</param>
        /// <param name="format">Target format.</param>
        /// <returns>Encoded palette bytes (text formats use UTF-8).</returns>
        public static byte[] ExportToFormat(byte[] gbaPalette, PaletteFormat format)
        {
            if (gbaPalette == null) throw new ArgumentNullException(nameof(gbaPalette));

            int colorCount = gbaPalette.Length / 2;
            if (colorCount == 0) throw new ArgumentException("Palette has no colors");

            // Extract RGB triples
            var colors = new List<(byte r, byte g, byte b)>(colorCount);
            for (int i = 0; i < colorCount; i++)
            {
                ushort gba = (ushort)(gbaPalette[i * 2] | (gbaPalette[i * 2 + 1] << 8));
                GbaToRgb(gba, out byte r, out byte g, out byte b);
                colors.Add((r, g, b));
            }

            return format switch
            {
                PaletteFormat.GbaRaw => (byte[])gbaPalette.Clone(),
                PaletteFormat.JascPal => ExportJascPal(colors),
                PaletteFormat.AdobeAct => ExportAdobeAct(colors),
                PaletteFormat.GimpGpl => ExportGimpGpl(colors),
                PaletteFormat.HexText => ExportHexText(colors),
                _ => throw new ArgumentException($"Unknown format: {format}"),
            };
        }

        /// <summary>
        /// Import palette data and convert to GBA raw palette bytes.
        /// </summary>
        /// <param name="fileData">Raw file bytes.</param>
        /// <param name="format">Source format.</param>
        /// <returns>GBA palette (2 bytes/color, BGR555 LE).</returns>
        public static byte[] ImportFromFormat(byte[] fileData, PaletteFormat format)
        {
            if (fileData == null) throw new ArgumentNullException(nameof(fileData));

            var colors = format switch
            {
                PaletteFormat.GbaRaw => ImportGbaRaw(fileData),
                PaletteFormat.JascPal => ImportJascPal(fileData),
                PaletteFormat.AdobeAct => ImportAdobeAct(fileData),
                PaletteFormat.GimpGpl => ImportGimpGpl(fileData),
                PaletteFormat.HexText => ImportHexText(fileData),
                _ => throw new ArgumentException($"Unknown format: {format}"),
            };

            // Convert RGB back to GBA
            byte[] result = new byte[colors.Count * 2];
            for (int i = 0; i < colors.Count; i++)
            {
                ushort gba = RgbToGba(colors[i].r, colors[i].g, colors[i].b);
                result[i * 2] = (byte)(gba & 0xFF);
                result[i * 2 + 1] = (byte)(gba >> 8);
            }
            return result;
        }

        /// <summary>
        /// Auto-detect palette format from file content and extension.
        /// </summary>
        public static PaletteFormat DetectFormat(byte[] fileData, string extension)
        {
            if (fileData == null || fileData.Length == 0)
                return PaletteFormat.GbaRaw;

            // Content-based detection first (most reliable)
            if (fileData.Length >= 8)
            {
                string header = Encoding.ASCII.GetString(fileData, 0, Math.Min(fileData.Length, 20));
                if (header.StartsWith("JASC-PAL"))
                    return PaletteFormat.JascPal;
                if (header.StartsWith("GIMP Palette"))
                    return PaletteFormat.GimpGpl;
            }

            // Extension-based detection
            string ext = (extension ?? "").TrimStart('.').ToLowerInvariant();
            switch (ext)
            {
                case "act":
                    return PaletteFormat.AdobeAct;
                case "gpl":
                    return PaletteFormat.GimpGpl;
                case "txt":
                    // Check if it looks like hex text (lines of 6 hex chars)
                    if (LooksLikeHexText(fileData))
                        return PaletteFormat.HexText;
                    return PaletteFormat.GbaRaw;
                case "pal":
                    // Already checked JASC header above, so this is raw GBA
                    return PaletteFormat.GbaRaw;
                default:
                    return PaletteFormat.GbaRaw;
            }
        }

        /// <summary>
        /// Determine palette format from file extension (for export).
        /// .pal defaults to JASC-PAL for maximum compatibility.
        /// </summary>
        public static PaletteFormat FormatFromExtension(string extension)
        {
            string ext = (extension ?? "").TrimStart('.').ToLowerInvariant();
            return ext switch
            {
                "act" => PaletteFormat.AdobeAct,
                "gpl" => PaletteFormat.GimpGpl,
                "txt" => PaletteFormat.HexText,
                "pal" => PaletteFormat.JascPal,
                "gbapal" => PaletteFormat.GbaRaw,
                _ => PaletteFormat.JascPal,
            };
        }

        /// <summary>
        /// Get the default file extension for a format.
        /// </summary>
        public static string DefaultExtension(PaletteFormat format)
        {
            return format switch
            {
                PaletteFormat.GbaRaw => ".gbapal",
                PaletteFormat.JascPal => ".pal",
                PaletteFormat.AdobeAct => ".act",
                PaletteFormat.GimpGpl => ".gpl",
                PaletteFormat.HexText => ".txt",
                _ => ".pal",
            };
        }

        // ===================== GBA ↔ RGB bit-shift math =====================

        static void GbaToRgb(ushort gba, out byte r, out byte g, out byte b)
        {
            r = (byte)((gba & 0x1F) << 3);
            g = (byte)(((gba >> 5) & 0x1F) << 3);
            b = (byte)(((gba >> 10) & 0x1F) << 3);
        }

        static ushort RgbToGba(byte r, byte g, byte b)
        {
            return (ushort)(
                ((r >> 3) & 0x1F) |
                (((g >> 3) & 0x1F) << 5) |
                (((b >> 3) & 0x1F) << 10));
        }

        // ===================== JASC-PAL =====================

        static byte[] ExportJascPal(List<(byte r, byte g, byte b)> colors)
        {
            var sb = new StringBuilder();
            sb.AppendLine("JASC-PAL");
            sb.AppendLine("0100");
            sb.AppendLine(colors.Count.ToString());
            foreach (var (r, g, b) in colors)
                sb.AppendLine($"{r} {g} {b}");
            return Encoding.UTF8.GetBytes(sb.ToString());
        }

        static List<(byte r, byte g, byte b)> ImportJascPal(byte[] data)
        {
            string text = Encoding.UTF8.GetString(data);
            string[] lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length < 3 || !lines[0].StartsWith("JASC-PAL"))
                throw new FormatException("Invalid JASC-PAL header");

            if (!int.TryParse(lines[2].Trim(), out int count))
                throw new FormatException("Invalid color count in JASC-PAL");

            var colors = new List<(byte r, byte g, byte b)>(count);
            for (int i = 3; i < lines.Length && colors.Count < count; i++)
            {
                string[] parts = lines[i].Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 3) continue;
                byte r = byte.Parse(parts[0]);
                byte g = byte.Parse(parts[1]);
                byte b = byte.Parse(parts[2]);
                colors.Add((r, g, b));
            }
            return colors;
        }

        // ===================== Adobe ACT =====================

        static byte[] ExportAdobeAct(List<(byte r, byte g, byte b)> colors)
        {
            // ACT: 256 * 3 bytes RGB, optionally + 2 byte count + 2 byte transparency index
            // Always write 772 bytes for maximum compat
            byte[] result = new byte[772];
            for (int i = 0; i < Math.Min(colors.Count, 256); i++)
            {
                result[i * 3] = colors[i].r;
                result[i * 3 + 1] = colors[i].g;
                result[i * 3 + 2] = colors[i].b;
            }
            // Color count at offset 768 (big-endian 16-bit)
            int cnt = Math.Min(colors.Count, 256);
            result[768] = (byte)(cnt >> 8);
            result[769] = (byte)(cnt & 0xFF);
            // Transparency index at offset 770 (big-endian 16-bit), 0xFFFF = none
            result[770] = 0xFF;
            result[771] = 0xFF;
            return result;
        }

        static List<(byte r, byte g, byte b)> ImportAdobeAct(byte[] data)
        {
            if (data.Length < 768)
                throw new FormatException("Adobe ACT file too small (need >= 768 bytes)");

            int count;
            if (data.Length >= 770)
            {
                // Read color count from footer
                count = (data[768] << 8) | data[769];
                if (count == 0 || count > 256) count = 256;
            }
            else
            {
                count = 256;
            }

            var colors = new List<(byte r, byte g, byte b)>(count);
            for (int i = 0; i < count; i++)
            {
                colors.Add((data[i * 3], data[i * 3 + 1], data[i * 3 + 2]));
            }
            return colors;
        }

        // ===================== GIMP GPL =====================

        static byte[] ExportGimpGpl(List<(byte r, byte g, byte b)> colors)
        {
            var sb = new StringBuilder();
            sb.AppendLine("GIMP Palette");
            sb.AppendLine("Name: GBA Palette");
            sb.AppendLine($"Columns: {Math.Min(colors.Count, 16)}");
            sb.AppendLine("#");
            for (int i = 0; i < colors.Count; i++)
            {
                var (r, g, b) = colors[i];
                sb.AppendLine($"{r,3} {g,3} {b,3}\tColor {i}");
            }
            return Encoding.UTF8.GetBytes(sb.ToString());
        }

        static List<(byte r, byte g, byte b)> ImportGimpGpl(byte[] data)
        {
            string text = Encoding.UTF8.GetString(data);
            string[] lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length < 1 || !lines[0].StartsWith("GIMP Palette"))
                throw new FormatException("Invalid GIMP GPL header");

            var colors = new List<(byte r, byte g, byte b)>();
            for (int i = 1; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.StartsWith("#") || line.StartsWith("Name:") || line.StartsWith("Columns:"))
                    continue;

                // Split on tab first to separate color name, then parse RGB
                string colorPart = line.Contains('\t') ? line.Substring(0, line.IndexOf('\t')) : line;
                string[] parts = colorPart.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 3) continue;

                if (byte.TryParse(parts[0], out byte r) &&
                    byte.TryParse(parts[1], out byte g) &&
                    byte.TryParse(parts[2], out byte b))
                {
                    colors.Add((r, g, b));
                }
            }
            return colors;
        }

        // ===================== Hex Text =====================

        static byte[] ExportHexText(List<(byte r, byte g, byte b)> colors)
        {
            var sb = new StringBuilder();
            foreach (var (r, g, b) in colors)
                sb.AppendLine($"{r:X2}{g:X2}{b:X2}");
            return Encoding.UTF8.GetBytes(sb.ToString());
        }

        static List<(byte r, byte g, byte b)> ImportHexText(byte[] data)
        {
            string text = Encoding.UTF8.GetString(data);
            string[] lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            var colors = new List<(byte r, byte g, byte b)>();
            foreach (string rawLine in lines)
            {
                string line = rawLine.Trim();
                if (line.StartsWith("#")) line = line.Substring(1);
                if (line.Length < 6) continue;
                // Take first 6 hex chars
                string hex = line.Substring(0, 6);
                if (uint.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out uint val))
                {
                    byte r = (byte)((val >> 16) & 0xFF);
                    byte g = (byte)((val >> 8) & 0xFF);
                    byte b = (byte)(val & 0xFF);
                    colors.Add((r, g, b));
                }
            }
            return colors;
        }

        // ===================== GBA Raw import helper =====================

        static List<(byte r, byte g, byte b)> ImportGbaRaw(byte[] data)
        {
            int count = data.Length / 2;
            var colors = new List<(byte r, byte g, byte b)>(count);
            for (int i = 0; i < count; i++)
            {
                ushort gba = (ushort)(data[i * 2] | (data[i * 2 + 1] << 8));
                GbaToRgb(gba, out byte r, out byte g, out byte b);
                colors.Add((r, g, b));
            }
            return colors;
        }

        // ===================== Detection helpers =====================

        static bool LooksLikeHexText(byte[] data)
        {
            try
            {
                string text = Encoding.UTF8.GetString(data);
                string[] lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                if (lines.Length == 0) return false;
                int hexLines = 0;
                foreach (string line in lines)
                {
                    string trimmed = line.Trim();
                    if (trimmed.StartsWith("#")) trimmed = trimmed.Substring(1);
                    if (trimmed.Length >= 6 &&
                        uint.TryParse(trimmed.Substring(0, 6), System.Globalization.NumberStyles.HexNumber, null, out _))
                        hexLines++;
                }
                return hexLines >= lines.Length / 2 && hexLines > 0;
            }
            catch
            {
                return false;
            }
        }
    }
}
