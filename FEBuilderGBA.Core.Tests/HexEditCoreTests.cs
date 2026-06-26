using System;
using System.Collections.Generic;
using System.Text;
using Xunit;
using FEBuilderGBA;

namespace FEBuilderGBA.Core.Tests
{
    /// <summary>
    /// Tests for <see cref="HexEditCore"/> — the writable Hex Editor edit-commit
    /// path (#1466) ported from WinForms <c>HexEditorForm.WriteButton_Click</c>.
    /// Covers: byte edit→write→read round-trip, only-differing-cells written,
    /// strict 2-hex-digit validation (no mutation on invalid), positional address
    /// safety (gutter tampering rejected), resize-if-larger, and undo rollback.
    /// </summary>
    [Collection("SharedState")]
    public class HexEditCoreTests
    {
        static ROM MakeRom(int size = 0x200)
        {
            var rom = new ROM();
            rom.SwapNewROMDataDirect(new byte[size]);
            CoreState.ROM = rom;
            return rom;
        }

        /// <summary>Render a page exactly the way HexEditorViewModel.RefreshDisplay does.</summary>
        static string RenderPage(ROM rom, uint baseAddr, uint viewSize)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Address  | 00 01 02 03 04 05 06 07  08 09 0A 0B 0C 0D 0E 0F | ASCII");
            sb.AppendLine("---------|--------------------------------------------------|----------------");
            uint end = Math.Min(baseAddr + viewSize, (uint)rom.Data.Length);
            for (uint row = baseAddr; row < end; row += 16)
            {
                sb.Append($"{row:X08} | ");
                var ascii = new StringBuilder();
                for (uint col = 0; col < 16; col++)
                {
                    uint addr = row + col;
                    if (addr < (uint)rom.Data.Length)
                    {
                        byte b = (byte)rom.u8(addr);
                        sb.Append($"{b:X02} ");
                        ascii.Append(b >= 0x20 && b < 0x7F ? (char)b : '.');
                    }
                    else
                    {
                        sb.Append("   ");
                        ascii.Append(' ');
                    }
                    if (col == 7) sb.Append(' ');
                }
                sb.Append("| ");
                sb.AppendLine(ascii.ToString());
            }
            return sb.ToString();
        }

        // ------------------------------------------------------------------
        // Round-trip: edit one byte → write → read back
        // ------------------------------------------------------------------

        [Fact]
        public void EditOneByte_WriteRoundTrips()
        {
            var rom = MakeRom();
            rom.Data[0x10] = 0x00;
            string page = RenderPage(rom, 0, 0x100);
            // Flip the byte at 0x10 from 00 -> AB in the rendered text.
            string edited = ReplaceByteCell(page, 0x10, "AB");

            var parsed = HexEditCore.ParseDisplay(edited, 0, (uint)rom.Data.Length);
            Assert.True(parsed.Ok, parsed.Error);

            var edits = HexEditCore.BuildEdits(rom, parsed.Cells);
            Assert.Single(edits);
            Assert.Equal(0x10u, edits[0].Addr);
            Assert.Equal(0xAB, edits[0].Value);

            var wr = HexEditCore.ApplyWrite(rom, edits);
            Assert.True(wr.Success);
            Assert.Equal(1, wr.BytesWritten);
            Assert.Equal(0xABu, rom.u8(0x10));
        }

        [Fact]
        public void EditMultipleBytes_AllWritten()
        {
            var rom = MakeRom();
            string page = RenderPage(rom, 0, 0x100);
            page = ReplaceByteCell(page, 0x00, "11");
            page = ReplaceByteCell(page, 0x0F, "22");
            page = ReplaceByteCell(page, 0x10, "33");

            var parsed = HexEditCore.ParseDisplay(page, 0, (uint)rom.Data.Length);
            Assert.True(parsed.Ok, parsed.Error);
            var edits = HexEditCore.BuildEdits(rom, parsed.Cells);
            Assert.Equal(3, edits.Count);
            HexEditCore.ApplyWrite(rom, edits);
            Assert.Equal(0x11u, rom.u8(0x00));
            Assert.Equal(0x22u, rom.u8(0x0F));
            Assert.Equal(0x33u, rom.u8(0x10));
        }

        [Fact]
        public void NoEdits_ProducesNoWrites()
        {
            var rom = MakeRom();
            for (int i = 0; i < 0x20; i++) rom.Data[i] = (byte)i;
            string page = RenderPage(rom, 0, 0x100);

            var parsed = HexEditCore.ParseDisplay(page, 0, (uint)rom.Data.Length);
            Assert.True(parsed.Ok);
            var edits = HexEditCore.BuildEdits(rom, parsed.Cells);
            Assert.Empty(edits); // only differing cells are edits
            var wr = HexEditCore.ApplyWrite(rom, edits);
            Assert.False(wr.Success); // nothing to write
            Assert.Equal(0, wr.BytesWritten);
        }

        [Fact]
        public void NonBasePage_DerivesAddressesFromBase()
        {
            var rom = MakeRom();
            uint baseAddr = 0x80;
            string page = RenderPage(rom, baseAddr, 0x100);
            page = ReplaceByteCell(page, 0x85, "EE");

            var parsed = HexEditCore.ParseDisplay(page, baseAddr, (uint)rom.Data.Length);
            Assert.True(parsed.Ok, parsed.Error);
            var edits = HexEditCore.BuildEdits(rom, parsed.Cells);
            Assert.Single(edits);
            Assert.Equal(0x85u, edits[0].Addr);
            HexEditCore.ApplyWrite(rom, edits);
            Assert.Equal(0xEEu, rom.u8(0x85));
        }

        // ------------------------------------------------------------------
        // Invalid input rejection (NO mutation)
        // ------------------------------------------------------------------

        [Theory]
        [InlineData("Z0")]   // non-hex
        [InlineData("0G")]   // non-hex second nibble
        [InlineData("1")]    // one digit
        [InlineData("111")]  // three digits
        public void InvalidByte_Rejected_NoMutation(string badToken)
        {
            var rom = MakeRom();
            string page = RenderPage(rom, 0, 0x100);
            page = ReplaceByteCell(page, 0x10, badToken);

            var parsed = HexEditCore.ParseDisplay(page, 0, (uint)rom.Data.Length);
            Assert.False(parsed.Ok);
            Assert.Empty(parsed.Cells);
            Assert.Equal(0x00u, rom.u8(0x10)); // unchanged
        }

        [Fact]
        public void MissingByteToken_Rejected()
        {
            var rom = MakeRom();
            string page = RenderPage(rom, 0, 0x100);
            // Delete the first byte token of row 0 entirely (shifts the row).
            page = RemoveFirstByteToken(page);

            var parsed = HexEditCore.ParseDisplay(page, 0, (uint)rom.Data.Length);
            Assert.False(parsed.Ok);
            Assert.Empty(parsed.Cells);
        }

        [Fact]
        public void ExtraByteToken_Rejected()
        {
            var rom = MakeRom();
            string page = RenderPage(rom, 0, 0x100);
            // Append an extra byte token at the end of the hex region of row 0.
            page = AppendExtraByteToken(page);

            var parsed = HexEditCore.ParseDisplay(page, 0, (uint)rom.Data.Length);
            Assert.False(parsed.Ok);
            Assert.Empty(parsed.Cells);
        }

        [Fact]
        public void EditedAddressGutter_Rejected_NoMutation()
        {
            var rom = MakeRom();
            string page = RenderPage(rom, 0, 0x100);
            // Tamper with the address gutter of the SECOND data row (00000010 -> 0000DEAD).
            page = page.Replace("00000010 |", "0000DEAD |");

            var parsed = HexEditCore.ParseDisplay(page, 0, (uint)rom.Data.Length);
            Assert.False(parsed.Ok);
            Assert.Empty(parsed.Cells);
            Assert.Contains("address", (parsed.Error ?? "").ToLowerInvariant());
        }

        [Fact]
        public void EditedAsciiGutter_Ignored()
        {
            var rom = MakeRom();
            for (int i = 0; i < 0x20; i++) rom.Data[i] = (byte)('A' + (i % 16));
            string page = RenderPage(rom, 0, 0x100);
            // Mangle the ASCII gutter content (after the 2nd '|') — must be ignored.
            page = MangleAsciiGutter(page);

            var parsed = HexEditCore.ParseDisplay(page, 0, (uint)rom.Data.Length);
            Assert.True(parsed.Ok, parsed.Error);
            var edits = HexEditCore.BuildEdits(rom, parsed.Cells);
            Assert.Empty(edits); // hex region unchanged → no writes
        }

        [Fact]
        public void LowercaseHex_Accepted()
        {
            var rom = MakeRom();
            string page = RenderPage(rom, 0, 0x100);
            page = ReplaceByteCell(page, 0x05, "ab");

            var parsed = HexEditCore.ParseDisplay(page, 0, (uint)rom.Data.Length);
            Assert.True(parsed.Ok, parsed.Error);
            var edits = HexEditCore.BuildEdits(rom, parsed.Cells);
            Assert.Single(edits);
            Assert.Equal(0xABu, edits[0].Value);
        }

        [Fact]
        public void DuplicateAddressRewrite_LastValueWins()
        {
            // Not expressible by tampering the renderer (each addr appears once),
            // but ApplyWrite must apply edits in order so a later edit overwrites.
            var rom = MakeRom();
            var edits = new List<HexEditCore.ByteEdit>
            {
                new HexEditCore.ByteEdit(0x10, 0x11),
                new HexEditCore.ByteEdit(0x10, 0x22),
            };
            HexEditCore.ApplyWrite(rom, edits);
            Assert.Equal(0x22u, rom.u8(0x10));
        }

        // ------------------------------------------------------------------
        // Resize-if-larger (parity with WinForms write_resize_data)
        // ------------------------------------------------------------------

        [Fact]
        public void EditBeyondEof_ResizesRom()
        {
            var rom = MakeRom(0x100);
            int originalLen = rom.Data.Length;
            var edits = new List<HexEditCore.ByteEdit>
            {
                new HexEditCore.ByteEdit(0x150, 0x99),
            };
            var wr = HexEditCore.ApplyWrite(rom, edits);
            Assert.True(wr.Success);
            Assert.True(rom.Data.Length > originalLen);
            Assert.Equal(0x99u, rom.u8(0x150));
        }

        // ------------------------------------------------------------------
        // Undo rollback restores original bytes AND length
        // ------------------------------------------------------------------

        [Fact]
        public void UndoRollback_RestoresBytes()
        {
            var rom = MakeRom();
            rom.Data[0x10] = 0x42;
            var undo = new Undo();
            CoreState.Undo = undo;

            var ud = undo.NewUndoData("hex");
            using (ROM.BeginUndoScope(ud))
            {
                var edits = new List<HexEditCore.ByteEdit> { new HexEditCore.ByteEdit(0x10, 0xFF) };
                HexEditCore.ApplyWrite(rom, edits);
            }
            Assert.Equal(0xFFu, rom.u8(0x10));

            undo.Push(ud);
            undo.RunUndo();
            Assert.Equal(0x42u, rom.u8(0x10)); // original restored
        }

        [Fact]
        public void UndoRollback_RestoresResizedLength()
        {
            var rom = MakeRom(0x100);
            int originalLen = rom.Data.Length;
            var undo = new Undo();
            CoreState.Undo = undo;

            var ud = undo.NewUndoData("hex-grow");
            using (ROM.BeginUndoScope(ud))
            {
                var edits = new List<HexEditCore.ByteEdit> { new HexEditCore.ByteEdit(0x150, 0x99) };
                HexEditCore.ApplyWrite(rom, edits);
            }
            Assert.True(rom.Data.Length > originalLen);

            undo.Push(ud);
            undo.RunUndo();
            // filesize was captured at NewUndoData time → length restored.
            Assert.Equal(originalLen, rom.Data.Length);
        }

        // ------------------------------------------------------------------
        // Defensive null/empty handling
        // ------------------------------------------------------------------

        [Fact]
        public void NullText_ReturnsError()
        {
            var rom = MakeRom();
            var parsed = HexEditCore.ParseDisplay(null!, 0, (uint)rom.Data.Length);
            Assert.False(parsed.Ok);
        }

        [Fact]
        public void ApplyWrite_NullRom_Fails()
        {
            var wr = HexEditCore.ApplyWrite(null!, new List<HexEditCore.ByteEdit> { new HexEditCore.ByteEdit(0, 1) });
            Assert.False(wr.Success);
        }

        // ------------------------------------------------------------------
        // Helpers — surgical edits to the rendered page text
        // ------------------------------------------------------------------

        /// <summary>Replace the 2-char hex cell for an absolute address in the rendered page.</summary>
        static string ReplaceByteCell(string page, uint addr, string newToken)
        {
            string[] lines = page.Replace("\r\n", "\n").Split('\n');
            uint rowBase = addr & 0xFFFFFFF0;
            int col = (int)(addr - rowBase);
            for (int li = 0; li < lines.Length; li++)
            {
                string line = lines[li];
                int bar1 = line.IndexOf('|');
                if (bar1 < 0) continue;
                string addrTok = line.Substring(0, bar1).Trim();
                if (addrTok.Length != 8) continue;
                if (!uint.TryParse(addrTok, System.Globalization.NumberStyles.HexNumber, null, out uint rb)) continue;
                if (rb != rowBase) continue;

                int bar2 = line.IndexOf('|', bar1 + 1);
                string hexRegion = line.Substring(bar1 + 1, bar2 - bar1 - 1);
                // Find the col-th token in the hex region and replace it.
                var tokenStarts = TokenStarts(hexRegion);
                int start = bar1 + 1 + tokenStarts[col];
                lines[li] = line.Substring(0, start) + newToken + line.Substring(start + 2);
                return string.Join("\n", lines);
            }
            throw new InvalidOperationException($"Row for addr 0x{addr:X} not found in page.");
        }

        static List<int> TokenStarts(string hexRegion)
        {
            var starts = new List<int>();
            int i = 0;
            while (i < hexRegion.Length)
            {
                while (i < hexRegion.Length && hexRegion[i] == ' ') i++;
                if (i >= hexRegion.Length) break;
                starts.Add(i);
                while (i < hexRegion.Length && hexRegion[i] != ' ') i++;
            }
            return starts;
        }

        static string RemoveFirstByteToken(string page)
        {
            string[] lines = page.Replace("\r\n", "\n").Split('\n');
            for (int li = 0; li < lines.Length; li++)
            {
                string line = lines[li];
                int bar1 = line.IndexOf('|');
                if (bar1 < 0) continue;
                string addrTok = line.Substring(0, bar1).Trim();
                if (addrTok.Length != 8 || !uint.TryParse(addrTok, System.Globalization.NumberStyles.HexNumber, null, out _)) continue;
                int bar2 = line.IndexOf('|', bar1 + 1);
                string hexRegion = line.Substring(bar1 + 1, bar2 - bar1 - 1);
                // remove the first 2-char token (and its trailing space)
                var starts = TokenStarts(hexRegion);
                string newHex = hexRegion.Remove(starts[0], 3); // "XX "
                lines[li] = line.Substring(0, bar1 + 1) + newHex + line.Substring(bar2);
                return string.Join("\n", lines);
            }
            return page;
        }

        static string AppendExtraByteToken(string page)
        {
            string[] lines = page.Replace("\r\n", "\n").Split('\n');
            for (int li = 0; li < lines.Length; li++)
            {
                string line = lines[li];
                int bar1 = line.IndexOf('|');
                if (bar1 < 0) continue;
                string addrTok = line.Substring(0, bar1).Trim();
                if (addrTok.Length != 8 || !uint.TryParse(addrTok, System.Globalization.NumberStyles.HexNumber, null, out _)) continue;
                int bar2 = line.IndexOf('|', bar1 + 1);
                string hexRegion = line.Substring(bar1 + 1, bar2 - bar1 - 1).TrimEnd();
                lines[li] = line.Substring(0, bar1 + 1) + hexRegion + " AA " + line.Substring(bar2);
                return string.Join("\n", lines);
            }
            return page;
        }

        static string MangleAsciiGutter(string page)
        {
            string[] lines = page.Replace("\r\n", "\n").Split('\n');
            for (int li = 0; li < lines.Length; li++)
            {
                string line = lines[li];
                int bar1 = line.IndexOf('|');
                if (bar1 < 0) continue;
                string addrTok = line.Substring(0, bar1).Trim();
                if (addrTok.Length != 8 || !uint.TryParse(addrTok, System.Globalization.NumberStyles.HexNumber, null, out _)) continue;
                int bar2 = line.IndexOf('|', bar1 + 1);
                if (bar2 < 0) continue;
                lines[li] = line.Substring(0, bar2 + 1) + " !!!MANGLED-ASCII!!!";
            }
            return string.Join("\n", lines);
        }
    }
}
