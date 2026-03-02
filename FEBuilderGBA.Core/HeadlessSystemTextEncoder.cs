using System;
using System.Collections.Generic;
using System.Text;

namespace FEBuilderGBA
{
    /// <summary>
    /// Minimal ISystemTextEncoder for headless (CLI/Avalonia) use.
    /// Uses ISO 8859-1 for US ROMs and Shift-JIS for JP ROMs as a basic fallback.
    /// For full TBL support, use SystemTextEncoder with a loaded ROM.
    /// </summary>
    public class HeadlessSystemTextEncoder : ISystemTextEncoder
    {
        readonly Encoding _encoding;

        public HeadlessSystemTextEncoder()
        {
            // Default to Latin-1 which handles most EN text
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            _encoding = Encoding.GetEncoding("iso-8859-1");
        }

        public HeadlessSystemTextEncoder(string encodingName)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            _encoding = Encoding.GetEncoding(encodingName);
        }

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
