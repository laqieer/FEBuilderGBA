namespace FEBuilderGBA
{
    /// <summary>
    /// Validation utilities for ROM write operations.
    /// Each method returns (isValid, error) where error is null when valid.
    /// </summary>
    public static class WriteValidator
    {
        public static (bool isValid, string? error) ValidateU8(uint value, string fieldName = "value")
        {
            if (value > 0xFF)
                return (false, $"{fieldName} must be 0x00-0xFF (got 0x{value:X})");
            return (true, null);
        }

        public static (bool isValid, string? error) ValidateU16(uint value, string fieldName = "value")
        {
            if (value > 0xFFFF)
                return (false, $"{fieldName} must be 0x0000-0xFFFF (got 0x{value:X})");
            return (true, null);
        }

        public static (bool isValid, string? error) ValidateU32(uint value, string fieldName = "value")
        {
            // All uint values are valid for u32
            return (true, null);
        }

        /// <summary>Validate that an address + size fits within the ROM.</summary>
        public static (bool isValid, string? error) ValidateAddress(uint addr, uint size)
        {
            var rom = CoreState.ROM;
            if (rom == null)
                return (false, "No ROM loaded");
            if (addr + size > rom.Data.Length)
                return (false, $"Address 0x{addr:X08} + size {size} exceeds ROM length ({rom.Data.Length})");
            return (true, null);
        }

        /// <summary>Validate that a value looks like a valid GBA pointer.</summary>
        public static (bool isValid, string? error) ValidatePointer(uint value)
        {
            if (value == 0)
                return (true, null); // null pointer is allowed
            if (value < 0x08000000 || value >= 0x0A000000)
                return (false, $"Invalid GBA pointer: 0x{value:X08} (expected 0x08000000-0x09FFFFFF range)");
            var rom = CoreState.ROM;
            if (rom != null)
            {
                uint offset = value - 0x08000000;
                if (offset >= rom.Data.Length)
                    return (false, $"Pointer 0x{value:X08} points beyond ROM (offset 0x{offset:X} >= 0x{rom.Data.Length:X})");
            }
            return (true, null);
        }

        /// <summary>Validate that an ID is not the protected zero ID (often means "none").</summary>
        public static (bool isValid, string? error) ValidateNotProtectedId(uint id, string entityName = "entry")
        {
            if (id == 0)
                return (false, $"Cannot overwrite {entityName} ID 0x00 (reserved/none)");
            return (true, null);
        }
    }
}
