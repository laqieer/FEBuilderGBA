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
        /// Back-compat overload that infers the displayed page size from the ROM
        /// length and base (used by tests that render a single sub-0x100 page). The
        /// view always passes the real page size via the 4-arg overload.
        /// </summary>
        public static ParseResult ParseDisplay(string editedText, uint baseAddress, uint romLength)
            => ParseDisplay(editedText, baseAddress, romLength, DefaultViewSize);

        /// <summary>Default display page size (matches HexEditorViewModel.ViewSize).</summary>
        public const uint DefaultViewSize = 0x100;

        /// <summary>
        /// Parse the edited multiline hex display back into byte values, deriving each
        /// byte's address positionally from <paramref name="baseAddress"/>. Pure (no
        /// ROM mutation). Header/separator/blank lines are tolerated and skipped.
        /// On ANY malformed row the result carries an <see cref="ParseResult.Error"/>
        /// and an empty cell list (caller must not write).
        ///
        /// <para>Validation is geometric, not token-shift based (Copilot PR review):
        /// the byte values are read from FIXED column positions matching the renderer
        /// (each slot is "HH " plus one extra space after slot 7), so a within-row
        /// delete+append that keeps the token count at 16 still fails because the
        /// columns no longer line up. The displayed page is bounded to EXACTLY the
        /// rows the renderer would have produced for <paramref name="viewSize"/>, so a
        /// deleted trailing row (count short) or an appended sequential row beyond the
        /// page (count long, even if within ROM) is rejected before any mutation.</para>
        /// </summary>
        /// <param name="editedText">The full HexGrid TextBox contents.</param>
        /// <param name="baseAddress">The page base address (offset) the display started at.</param>
        /// <param name="romLength">Current ROM byte length (slots at/after this are blank padding).</param>
        /// <param name="viewSize">The displayed page size in bytes (HexEditorViewModel.ViewSize).</param>
        public static ParseResult ParseDisplay(string editedText, uint baseAddress, uint romLength, uint viewSize)
        {
            var result = new ParseResult();
            if (editedText == null)
            {
                result.Error = "No hex text to parse.";
                return result;
            }

            // EXACT set of data rows the renderer would have produced: from baseAddress
            // up to min(base+viewSize, romLength), stepping 16. Any other data-row count
            // (deleted/appended row) is rejected.
            uint pageEnd = romLength;
            ulong wanted = (ulong)baseAddress + viewSize;
            if (wanted < pageEnd) pageEnd = (uint)wanted;
            int expectedRows = 0;
            if (pageEnd > baseAddress)
            {
                expectedRows = (int)(((pageEnd - baseAddress) + (BytesPerRow - 1)) / BytesPerRow);
            }

            string[] lines = editedText.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            int dataRow = 0; // index among DATA rows (not counting header/separator/blank)

            foreach (string raw in lines)
            {
                string line = raw;
                if (string.IsNullOrWhiteSpace(line)) continue;

                // Header / separator lines have no leading 8-hex address followed by '|'.
                int bar1 = line.IndexOf('|');
                if (bar1 < 0) continue; // not a data row
                string addrToken = line.Substring(0, bar1).Trim();
                if (addrToken.Length != 8 || !IsHex(addrToken))
                {
                    // header ("Address  | 00 01 ...") or separator ("---------|---")
                    continue;
                }

                // More data rows than the page should have ⇒ an appended row was added.
                if (dataRow >= expectedRows)
                {
                    result.Cells.Clear();
                    result.Error = $"More data rows than the displayed page ({expectedRows}). Do not add or remove rows.";
                    return result;
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
                if (bar2 < 0)
                {
                    result.Cells.Clear();
                    result.Error = $"Row at 0x{rowBase:X08} is missing the ASCII column separator. Do not delete the '|' columns.";
                    return result;
                }
                string hexRegion = line.Substring(bar1 + 1, bar2 - bar1 - 1);

                if (!ParseHexRegion(hexRegion, rowBase, romLength, result))
                {
                    // Error already set inside ParseHexRegion.
                    return result;
                }

                dataRow++;
            }

            // Fewer data rows than the page should have ⇒ a row was deleted.
            if (dataRow != expectedRows)
            {
                result.Cells.Clear();
                result.Error = $"Expected {expectedRows} data row(s) but found {dataRow}. Do not add or remove rows.";
                return result;
            }

            return result;
        }

        /// <summary>
        /// Parse the hex region of one row by FIXED column geometry (Copilot PR review).
        /// The renderer emits, for each of the 16 columns, two hex chars + one space,
        /// with ONE EXTRA space after column 7 ("HH HH HH HH HH HH HH HH  HH ..."). We
        /// reproduce that exact layout and read each in-ROM slot at its expected column,
        /// requiring exactly two hex digits there. A within-row delete+append (which
        /// keeps the whitespace-token count at 16 but shifts the columns) therefore
        /// fails, and out-of-ROM slots near EOF must be blank.
        /// </summary>
        static bool ParseHexRegion(string hexRegion, uint rowBase, uint romLength, ParseResult result)
        {
            // The renderer prefixes the hex region with a single space (from "| ") and
            // suffixes one (the trailing "HH " space before " |"). Work on the raw
            // region but index columns from the first hex position. The deterministic
            // layout from column 0 is: pos(k) = k*3 + (k >= 8 ? 1 : 0), measured from
            // the first byte char. We locate that first byte char as the offset after
            // the single leading separator space.
            if (hexRegion.Length == 0 || hexRegion[0] != ' ')
            {
                result.Cells.Clear();
                result.Error = $"Row at 0x{rowBase:X08} has a malformed byte column layout.";
                return false;
            }
            // Strip exactly ONE leading space (the "| " gap). Everything else is fixed.
            string body = hexRegion.Substring(1);

            for (int k = 0; k < BytesPerRow; k++)
            {
                int pos = k * 3 + (k >= 8 ? 1 : 0);
                uint addr = rowBase + (uint)k;
                bool inRom = addr < romLength;

                if (!inRom)
                {
                    // Out-of-ROM slot: the renderer wrote "   " (3 spaces). Require the
                    // 2-char slot to be blank so a user can't smuggle bytes past EOF here.
                    if (pos + 2 <= body.Length)
                    {
                        string slot = body.Substring(pos, 2);
                        if (slot.Trim().Length != 0)
                        {
                            result.Cells.Clear();
                            result.Error = $"Byte at 0x{addr:X08} is past end-of-ROM and must stay blank.";
                            return false;
                        }
                    }
                    continue;
                }

                if (pos + 2 > body.Length)
                {
                    result.Cells.Clear();
                    result.Error = $"Row at 0x{rowBase:X08} is truncated at byte 0x{addr:X08}. Do not delete byte columns.";
                    return false;
                }

                // The character immediately after the slot must be the column separator
                // space (or, after slot 7, the extra space) — enforces the fixed grid so
                // a shifted token can't masquerade as a valid byte.
                char c0 = body[pos], c1 = body[pos + 1];
                if (!IsHexChar(c0) || !IsHexChar(c1))
                {
                    result.Cells.Clear();
                    result.Error = $"Invalid byte '{c0}{c1}' at 0x{addr:X08}. Each byte must be exactly two hex digits (00-FF).";
                    return false;
                }
                int sepPos = pos + 2;
                if (sepPos < body.Length && body[sepPos] != ' ')
                {
                    result.Cells.Clear();
                    result.Error = $"Byte at 0x{addr:X08} is not aligned to its column. Do not add or remove characters between byte columns.";
                    return false;
                }

                byte value = (byte)((HexVal(c0) << 4) | HexVal(c1));
                result.Cells.Add(new ByteEdit(addr, value));
            }

            // Nothing but whitespace may follow the last column (slot 15 ends at
            // pos = 15*3+1 = 46, i.e. body[48..]). Trailing non-space content means an
            // extra byte token was appended — reject it.
            int afterLast = (BytesPerRow - 1) * 3 + 1 + 2; // end of slot 15's two hex chars
            if (afterLast < body.Length && body.Substring(afterLast).Trim().Length != 0)
            {
                result.Cells.Clear();
                result.Error = $"Row at 0x{rowBase:X08} has extra content after the last byte column. Do not add byte columns.";
                return false;
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
