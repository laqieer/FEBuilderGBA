using System;
using System.Collections.Generic;
using System.Text;

namespace FEBuilderGBA
{
    /// <summary>
    /// Minimal ISystemTextEncoder for headless (CLI/Avalonia) use.
    /// Auto-detects encoding from the current ROM: Shift-JIS for Japanese ROMs,
    /// ISO 8859-1 for US ROMs. For full TBL support, use SystemTextEncoder.
    /// </summary>
    public class HeadlessSystemTextEncoder : ISystemTextEncoder
    {
        readonly Encoding _encoding;

        public HeadlessSystemTextEncoder()
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            // Auto-detect from current ROM if available
            _encoding = DetectEncodingFromRom();
        }

        public HeadlessSystemTextEncoder(string encodingName)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            _encoding = Encoding.GetEncoding(encodingName);
        }

        /// <summary>
        /// Creates a HeadlessSystemTextEncoder that auto-detects encoding from the given ROM.
        /// </summary>
        public HeadlessSystemTextEncoder(ROM rom)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            _encoding = DetectEncodingFromRom(rom);
        }

        /// <summary>
        /// Detects the appropriate encoding from CoreState.ROM.
        /// Returns Shift_JIS for Japanese ROMs, ISO-8859-1 otherwise.
        /// </summary>
        static Encoding DetectEncodingFromRom(ROM rom = null)
        {
            rom ??= CoreState.ROM;
            if (rom?.RomInfo != null && rom.RomInfo.is_multibyte)
            {
                try
                {
                    return Encoding.GetEncoding("Shift_JIS");
                }
                catch
                {
                    // Fallback if Shift_JIS not available
                }
            }
            return Encoding.GetEncoding("iso-8859-1");
        }

        /// <summary>
        /// The encoding name used by this instance (for diagnostics).
        /// </summary>
        public string EncodingName => _encoding.WebName;

        public string Decode(byte[] str)
        {
            if (str == null || str.Length == 0) return "";
            return _encoding.GetString(str);
        }

        public string Decode(byte[] str, int start, int len)
        {
            if (str == null || str.Length == 0 || len <= 0) return "";
            if (start + len > str.Length) len = str.Length - start;
            return _encoding.GetString(str, start, len);
        }

        public byte[] Encode(string str)
        {
            if (string.IsNullOrEmpty(str)) return Array.Empty<byte>();
            return _encoding.GetBytes(str);
        }

        public Dictionary<string, uint> GetTBLEncodeDicLow()
        {
            return new Dictionary<string, uint>();
        }
    }
}
