using System;
using System.Globalization;

namespace FEBuilderGBA.Avalonia.Services
{
    public static class NumericInputParser
    {
        public static bool TryParseUInt32(
            string? text,
            uint minimum,
            uint maximum,
            out uint value)
        {
            value = 0;
            if (minimum > maximum || string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            string trimmed = text.Trim();
            uint parsed;
            if (trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                string digits = trimmed.Substring(2);
                if (digits.Length == 0
                    || !uint.TryParse(
                        digits,
                        NumberStyles.AllowHexSpecifier,
                        CultureInfo.InvariantCulture,
                        out parsed))
                {
                    return false;
                }
            }
            else if (!uint.TryParse(
                trimmed,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out parsed))
            {
                return false;
            }

            if (parsed < minimum || parsed > maximum)
            {
                return false;
            }

            value = parsed;
            return true;
        }

        public static string FormatHexByte(uint value)
        {
            if (value > byte.MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            return $"0x{value:X2}";
        }
    }
}
