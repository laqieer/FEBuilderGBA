using System;
using System.Collections.Generic;
using System.IO;

namespace FEBuilderGBA
{
    /// <summary>
    /// Cross-platform ROM diff helper extracted from WinForms ToolDiffForm.
    /// Provides 2-way (MakeDiff) and 3-way (MakeDiff3) binary patch generation,
    /// plus a parameterized DefineFreeSpace helper that takes ROM metadata as
    /// inputs rather than reading Program.ROM globally.
    ///
    /// Used by both WinForms ToolDiffForm and Avalonia ToolDiffViewModel so that
    /// the same algorithm produces identical patch files on both platforms.
    /// </summary>
    public static class DiffToolCore
    {
        /// <summary>
        /// Generate a 2-way binary patch comparing currentBin against otherBin.
        /// Mirrors WinForms ToolDiffForm.MakeDiff but takes ROM metadata
        /// (version, isMultibyte) as parameters instead of reading Program.ROM globally.
        /// Output format: text patch file (NAME=, TYPE=BIN, BINF: lines) with .bin
        /// sidecars in the same directory as outPath.
        /// </summary>
        public static void MakeDiff(string outPath, byte[] currentBin, byte[] otherBin,
            uint patchedIfMinSize, bool collectFreeSpace, int version, bool isMultibyte)
        {
            // Matches WF U.substr(filename, "PATCH_".Length): strips 6 chars from start.
            string stem = Path.GetFileNameWithoutExtension(outPath) ?? "";
            string name = stem.Length >= "PATCH_".Length
                ? stem.Substring("PATCH_".Length)
                : "";

            List<string> lines = new List<string>();
            lines.Add("NAME=" + name);
            lines.Add("TYPE=BIN");
            lines.Add("");

            bool patchedIfNotYet = true;
            int recoverMissMatch = (int)patchedIfMinSize;
            int checkpoint = -1;

            // Free area is collected as a single diff (SkillSystems pattern) when on FE8.
            collectFreeSpace = DefineFreeSpace(version, isMultibyte, collectFreeSpace,
                out uint beginFreeSpace, out uint endFreeSpace);

            int length = Math.Max(currentBin.Length, otherBin.Length);
            for (int i = 0; i < length; i++)
            {
                if (U.at(currentBin, i) == U.at(otherBin, i))
                {
                    continue;
                }

                checkpoint = i;

                i++;
                int missCount = 0;
                for (; i < length; i++)
                {
                    if (collectFreeSpace
                        && i >= beginFreeSpace && i <= endFreeSpace)
                    {
                        // Inside free area — treat misses as "still in diff" (allow more)
                        missCount = 0;
                        continue;
                    }
                    if (i >= endFreeSpace)
                    {
                        break;
                    }

                    if (U.at(currentBin, i) != U.at(otherBin, i))
                    {
                        missCount = 0;
                        continue;
                    }

                    if (missCount >= recoverMissMatch)
                    {
                        i -= missCount;
                        break;
                    }

                    missCount++;
                }

                // Record checkpoint..i as a diff range, padded to 4-byte alignment.
                checkpoint = (checkpoint / 4) * 4;
                i = U.Padding4(i);

                string splitFilename = U.ToHexString8(checkpoint) + ".bin";
                string splitFullPath = Path.Combine(Path.GetDirectoryName(outPath) ?? "", splitFilename);

                byte[] diff = U.subrange(otherBin, (uint)checkpoint, (uint)i);
                U.WriteAllBytes(splitFullPath, diff);

                if (patchedIfNotYet)
                {
                    if (diff.Length > patchedIfMinSize)
                    {
                        lines.Add("PATCHED_IF:" + U.To0xHexString((uint)checkpoint) + "=" + DumpByte(diff));
                        patchedIfNotYet = false;
                    }
                }

                lines.Add("BINF:" + U.To0xHexString((uint)checkpoint) + "=" + splitFilename);
            }

            File.WriteAllLines(outPath, lines);
        }

        /// <summary>
        /// Generate a 3-way binary patch: emit bytes where A and B agree AND
        /// differ from the current ROM (Diff3Method index 0 in WinForms).
        /// Used when you have two ROMs that share a feature absent from your base ROM.
        /// </summary>
        public static void MakeDiff3(string outPath, byte[] currentRom, byte[] a, byte[] b,
            uint patchedIfMinSize)
        {
            List<string> lines = new List<string>();
            lines.Add("TYPE=BIN");
            lines.Add("");

            bool patchedIfNotYet = true;
            int recoverMissMatch = (int)patchedIfMinSize;
            int checkpoint = -1;

            int length = Math.Max(Math.Max(currentRom.Length, a.Length), b.Length);
            for (int i = 0; i < length; i++)
            {
                uint ai = U.at(a, i);
                uint bi = U.at(b, i);
                uint ri = U.at(currentRom, i);
                if (ai != bi || ai == ri)
                {
                    continue;
                }

                checkpoint = i;

                i++;
                int missCount = 0;
                for (; i < length; i++)
                {
                    ai = U.at(a, i);
                    bi = U.at(b, i);
                    ri = U.at(currentRom, i);
                    if (!(ai != bi || ai == ri))
                    {
                        // Still in diff region
                        missCount = 0;
                        continue;
                    }

                    if (missCount >= recoverMissMatch)
                    {
                        i -= missCount;
                        break;
                    }

                    missCount++;
                }

                string splitFilename = U.ToHexString8(checkpoint) + ".bin";
                string splitFullPath = Path.Combine(Path.GetDirectoryName(outPath) ?? "", splitFilename);

                byte[] diff = U.subrange(a, (uint)checkpoint, (uint)i);
                U.WriteAllBytes(splitFullPath, diff);

                if (patchedIfNotYet)
                {
                    if (diff.Length > patchedIfMinSize)
                    {
                        lines.Add("PATCHED_IF:" + U.To0xHexString((uint)checkpoint) + "=" + DumpByte(diff));
                        patchedIfNotYet = false;
                    }
                }

                lines.Add("BINF:" + U.To0xHexString((uint)checkpoint) + "=" + splitFilename);
            }

            File.WriteAllLines(outPath, lines);
        }

        /// <summary>
        /// Calculate the free-space region for the given ROM version.
        /// Used by MakeDiff to allow misses inside the free area.
        /// Returns true if collectFreeSpace was honored (FE8 only).
        /// </summary>
        public static bool DefineFreeSpace(int version, bool isMultibyte, bool collectFreeSpace,
            out uint beginFreeSpace, out uint endFreeSpace)
        {
            beginFreeSpace = U.NOT_FOUND;
            endFreeSpace = U.NOT_FOUND;
            if (!collectFreeSpace)
            {
                return false;
            }
            if (version != 8)
            {
                return false;
            }
            if (isMultibyte)
            {
                // FE8J
                beginFreeSpace = 0xEFB2E0;
                endFreeSpace = 0xF90000 - 4;
            }
            else
            {
                // FE8U
                beginFreeSpace = 0xB2A610;
                endFreeSpace = 0xB88560 - 4;
            }
            return true;
        }

        // Local equivalent of WinForms U.DumpByte. Produces space-separated
        // hex byte tokens with variable width ("0xA" for 10, "0xAB" for 171),
        // exactly matching WinForms U.DumpByte's ToString("X") format.
        static string DumpByte(byte[] data)
        {
            if (data.Length == 0) return "";
            var sb = new System.Text.StringBuilder(data.Length * 5);
            sb.Append("0x").Append(data[0].ToString("X"));
            for (int i = 1; i < data.Length; i++)
                sb.Append(" 0x").Append(data[i].ToString("X"));
            return sb.ToString();
        }
    }
}
