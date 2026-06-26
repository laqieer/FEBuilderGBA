using System;
using System.Collections.Generic;
using System.Text;

namespace FEBuilderGBA
{
    /// <summary>
    /// Core (cross-platform) logic for the writable Hex Editor (#1466). Ports the
    /// edit-commit path of the WinForms <c>HexEditorForm.WriteButton_Click</c> /
    /// <c>HexBox</c> into a pure, unit-testable helper so the Avalonia Hex Editor
    /// can edit + write ROM bytes (previously read-only).
    ///
    /// <para>The Avalonia hex "grid" is a multiline TextBox whose rows render as
    /// <c>AAAAAAAA | hh hh .. hh | ascii</c> (see <c>HexEditorViewModel.RefreshDisplay</c>).
    /// The user types over hex digits in the visible page; on Write the displayed
    /// page is re-parsed and only the bytes that DIFFER from the current ROM are
    /// committed — the identical net effect to WinForms <c>HexBox.getWriteData()</c>
    /// (which tracks only edited cells).</para>
    ///
    /// <para><b>Address safety (Copilot review #1):</b> write targets are derived
    /// POSITIONALLY from the page base (<c>baseAddress + row*16 + col</c>); the typed
    /// 8-hex address gutter is parsed only to VALIDATE that it matches the expected
    /// aligned row address. A typo in the gutter rejects the whole write — it can
    /// never redirect a write to an unintended address.</para>
    ///
    /// <para><b>Positional byte slots (Copilot review #2):</b> the hex region is read
    /// as fixed two-character slots; every committed slot must be exactly two hex
    /// digits. A deleted/short/extra token shifts the row geometry and is rejected
    /// before any mutation — bytes can never silently shift into earlier addresses.</para>
    /// </summary>
    public static class HexEditCore
    {
        /// <summary>16 bytes per displayed row (matches the ViewModel renderer).</summary>
        public const int BytesPerRow = 16;

        /// <summary>One parsed, to-be-written byte edit (offset + new value).</summary>
        public readonly struct ByteEdit
        {
            public readonly uint Addr;
            public readonly byte Value;
            public ByteEdit(uint addr, byte value) { Addr = addr; Value = value; }
        }

        /// <summary>Result of parsing the edited hex display.</summary>
        public sealed class ParseResult
        {
            /// <summary>All byte slots parsed from the page (every in-ROM-display slot).</summary>
            public List<ByteEdit> Cells { get; } = new List<ByteEdit>();
            /// <summary>Non-null when parsing/validation failed — caller must NOT mutate.</summary>
            public string? Error { get; set; }
            public bool Ok => Error == null;
        }

        /// <summary>Result of an <see cref="ApplyWrite"/> attempt.</summary>
        public sealed class WriteResult
        {
            public bool Success { get; set; }
            /// <summary>Number of bytes actually written (only differing cells).</summary>
            public int BytesWritten { get; set; }
            /// <summary>Human-readable status / refusal reason.</summary>
            public string Message { get; set; } = string.Empty;
        }

        /// <summary>
        /// Parse the edited multiline hex display back into byte values, deriving each
        /// byte's address positionally from <paramref name="baseAddress"/>. Pure (no
        /// ROM mutation). Header/separator/blank lines are tolerated and skipped.
        /// On ANY malformed row the result carries an <see cref="ParseResult.Error"/>
        /// and an empty cell list (caller must not write).
        /// </summary>
        /// <param name="editedText">The full HexGrid TextBox contents.</param>
        /// <param name="baseAddress">The page base address (offset) the display started at.</param>
        /// <param name="romLength">Current ROM byte length (slots at/after this may be blank padding).</param>
        public static ParseResult ParseDisplay(string editedText, uint baseAddress, uint romLength)
        {
            var result = new ParseResult();
            if (editedText == null)
            {
                result.Error = "No hex text to parse.";
                return result;
            }

            string[] lines = editedText.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            int dataRow = 0; // index among DATA rows (not counting header/separator/blank)

            foreach (string raw in lines)
            {
                string line = raw;
                if (string.IsNullOrWhiteSpace(line)) continue;

                // Header / separator lines have no leading 8-hex address followed by " | ".
                // Identify a data row by a leading 8-hex token followed by a '|'.
                int bar1 = line.IndexOf('|');
                if (bar1 < 0) continue; // not a data row
                string addrToken = line.Substring(0, bar1).Trim();
                if (addrToken.Length != 8 || !IsHex(addrToken))
                {
                    // header ("Address  | 00 01 ...") or separator ("---------|---")
                    continue;
                }

                uint rowBase = baseAddress + (uint)(dataRow * BytesPerRow);

                // (Copilot #1) Gutter must match the expected positional address.
                if (!TryParseHex8(addrToken, out uint gutterAddr) || gutterAddr != rowBase)
                {
                    result.Cells.Clear();
                    result.Error = $"Address column '{addrToken}' on row {dataRow} does not match the expected page address 0x{rowBase:X08}. Editing the address column is not allowed.";
                    return result;
                }

                // Hex region lies between the first and second '|'.
                int bar2 = line.IndexOf('|', bar1 + 1);
                string hexRegion = bar2 >= 0
                    ? line.Substring(bar1 + 1, bar2 - bar1 - 1)
                    : line.Substring(bar1 + 1);

                if (!ParseHexRegion(hexRegion, rowBase, romLength, result))
                {
                    // Error already set inside ParseHexRegion.
                    return result;
                }

                dataRow++;
            }

            return result;
        }

        /// <summary>
        /// Tokenize the hex region of one row into byte slots. (Copilot #2) Each
        /// non-blank token must be EXACTLY two hex digits at its slot position; a
        /// blank slot is only allowed when it falls at/after <paramref name="romLength"/>
        /// (out-of-ROM display padding near EOF). Otherwise the whole parse is rejected.
        /// </summary>
        static bool ParseHexRegion(string hexRegion, uint rowBase, uint romLength, ParseResult result)
        {
            // Split on whitespace; the renderer inserts an extra space between the two
            // 8-byte halves, so consecutive whitespace is collapsed.
            string[] tokens = hexRegion.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);

            // The number of present tokens must equal the number of in-ROM slots for this
            // row (so a deleted/extra token can NEVER shift bytes into wrong addresses).
            int inRomSlots = 0;
            for (int k = 0; k < BytesPerRow; k++)
            {
                if (rowBase + (uint)k < romLength) inRomSlots++;
            }

            if (tokens.Length != inRomSlots)
            {
                result.Cells.Clear();
                result.Error = $"Row at 0x{rowBase:X08} has {tokens.Length} byte value(s) but expected {inRomSlots}. Do not add or remove byte columns.";
                return false;
            }

            for (int k = 0; k < tokens.Length; k++)
            {
                string tok = tokens[k];
                if (tok.Length != 2 || !IsHex(tok))
                {
                    result.Cells.Clear();
                    result.Error = $"Invalid byte '{tok}' at 0x{(rowBase + (uint)k):X08}. Each byte must be exactly two hex digits (00-FF).";
                    return false;
                }
                byte value = (byte)((HexVal(tok[0]) << 4) | HexVal(tok[1]));
                result.Cells.Add(new ByteEdit(rowBase + (uint)k, value));
            }
            return true;
        }

        /// <summary>
        /// Diff the parsed cells against the current ROM and return ONLY the cells
        /// whose value differs (mirrors WinForms <c>getWriteData()</c>: edited cells
        /// only). Pure (read-only). Out-of-ROM cells (addr ≥ length) are kept as edits
        /// so the resize-if-larger path can grow the ROM (parity with WinForms).
        /// </summary>
        public static List<ByteEdit> BuildEdits(ROM rom, IReadOnlyList<ByteEdit> cells)
        {
            var edits = new List<ByteEdit>();
            if (rom == null || rom.Data == null || cells == null) return edits;

            int len = rom.Data.Length;
            foreach (var c in cells)
            {
                if (c.Addr < (uint)len)
                {
                    if (rom.Data[c.Addr] != c.Value) edits.Add(c);
                }
                else
                {
                    edits.Add(c); // beyond EOF — always an edit (forces resize)
                }
            }
            return edits;
        }

        /// <summary>
        /// Apply <paramref name="edits"/> to <paramref name="rom"/> via
        /// <c>rom.write_u8</c> under the AMBIENT undo scope the caller has already
        /// opened (Avalonia <c>UndoService.Begin</c>). Resizes the ROM first when any
        /// edit lands beyond the current EOF (parity with WinForms
        /// <c>WriteButton_Click</c>'s <c>write_resize_data</c>), aborting the whole
        /// write if the resize fails. Does NOT swallow ROM/write exceptions
        /// (Copilot #5) — those propagate so the caller can Rollback.
        /// </summary>
        public static WriteResult ApplyWrite(ROM rom, IReadOnlyList<ByteEdit> edits)
        {
            var wr = new WriteResult();
            if (rom == null || rom.Data == null)
            {
                wr.Message = "ROM not loaded.";
                return wr;
            }
            if (edits == null || edits.Count == 0)
            {
                wr.Success = false; // nothing to do — caller rolls back the empty scope
                wr.Message = "No changes to write.";
                return wr;
            }

            // Resize-if-larger (mirrors HexEditorForm.WriteButton_Click 218-227).
            uint maxAddr = 0;
            foreach (var e in edits) if (e.Addr > maxAddr) maxAddr = e.Addr;
            uint required = maxAddr + 1;
            if (required > (uint)rom.Data.Length)
            {
                bool ok = rom.write_resize_data(required);
                if (!ok)
                {
                    wr.Message = $"Failed to resize ROM to 0x{required:X08}.";
                    return wr;
                }
            }

            foreach (var e in edits)
            {
                rom.write_u8(e.Addr, e.Value);
            }

            wr.Success = true;
            wr.BytesWritten = edits.Count;
            wr.Message = $"Wrote {edits.Count} byte(s).";
            return wr;
        }

        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------

        static bool IsHex(string s)
        {
            for (int i = 0; i < s.Length; i++)
            {
                if (!IsHexChar(s[i])) return false;
            }
            return s.Length > 0;
        }

        static bool IsHexChar(char c) =>
            (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');

        static int HexVal(char c)
        {
            if (c >= '0' && c <= '9') return c - '0';
            if (c >= 'a' && c <= 'f') return c - 'a' + 10;
            return c - 'A' + 10;
        }

        static bool TryParseHex8(string s, out uint value)
        {
            value = 0;
            if (s.Length != 8 || !IsHex(s)) return false;
            for (int i = 0; i < 8; i++) value = (value << 4) | (uint)HexVal(s[i]);
            return true;
        }
    }
}
