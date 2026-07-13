using System;

namespace FEBuilderGBA.E2ETests.Helpers
{
    internal static class BuildfileRomFixture
    {
        // Keep this black-box test contract aligned with the CLI's documented buildfile limit.
        internal const int MaxRomSize = 32 * 1024 * 1024;
        internal const int PreferredExtensionSize = 0x40000;
        internal const int MinimumExtensionSize = 0x13;
        private const int MinimumCleanSize = 0x200001;

        internal static int GetSparseExtensionSize(long cleanLength)
        {
            if (cleanLength < 0 || cleanLength > MaxRomSize)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(cleanLength),
                    $"Clean fixture length must be between 0 and {MaxRomSize} bytes.");
            }

            long remaining = MaxRomSize - cleanLength;
            if (remaining < MinimumExtensionSize)
                return 0;

            return (int)Math.Min(PreferredExtensionSize, remaining);
        }

        internal static byte[] CreateSparseExtendedCopy(byte[] clean)
        {
            ArgumentNullException.ThrowIfNull(clean);

            int extensionSize = GetSparseExtensionSize(clean.LongLength);
            if (extensionSize == 0)
            {
                throw new ArgumentException(
                    $"Clean fixture ({clean.Length} bytes) leaves fewer than "
                    + $"{MinimumExtensionSize} bytes for distinct extension markers.",
                    nameof(clean));
            }

            var extended = new byte[checked(clean.Length + extensionSize)];
            Buffer.BlockCopy(clean, 0, extended, 0, clean.Length);
            extended.AsSpan(clean.Length).Fill(0xFF);
            return extended;
        }

        internal static byte[] CreateModdedCopy(byte[] clean)
        {
            ArgumentNullException.ThrowIfNull(clean);
            if (clean.Length < MinimumCleanSize)
            {
                throw new ArgumentException(
                    $"Clean fixture must contain at least {MinimumCleanSize} bytes for disjoint edits.",
                    nameof(clean));
            }

            byte[] modded = CreateSparseExtendedCopy(clean);
            modded[1] ^= 0xFF;
            modded[0x100000] = 0xA1;
            modded[0x100001] = 0xA2;
            modded[0x200000] = 0xB7;
            modded[clean.Length + 0x10] = 0x01;
            modded[clean.Length + 0x11] = 0x02;
            modded[modded.Length - 1] = 0x03;
            return modded;
        }
    }
}
