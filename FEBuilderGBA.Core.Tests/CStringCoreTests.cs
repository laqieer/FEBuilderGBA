using System;
using System.Collections.Generic;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    /// <summary>
    /// Pure / read-only CStringCore tests (ReadCString + OldRegionLength). These set
    /// CoreState.SystemTextEncoder (needed by the decode/length path) so they live in
    /// the shared collection and restore it on dispose.
    /// </summary>
    [Collection("SharedState")]
    public class CStringCorePureTests : IDisposable
    {
        readonly ROM? _savedRom;
        readonly ISystemTextEncoder? _savedEncoder;

        public CStringCorePureTests()
        {
            _savedRom = CoreState.ROM;
            _savedEncoder = CoreState.SystemTextEncoder;
            CoreState.SystemTextEncoder = new HeadlessSystemTextEncoder();
        }

        public void Dispose()
        {
            CoreState.ROM = _savedRom;
            CoreState.SystemTextEncoder = _savedEncoder;
            PatchDetection.ClearAllCaches();
        }

        static ROM MakeRom(int size = 0x200000)
        {
            var rom = new ROM();
            rom.LoadLow("test.gba", new byte[size], "NAZO"); // ROMFE0 — valid RomInfo.
            return rom;
        }

        static void WriteAscii(ROM rom, uint addr, string s)
        {
            byte[] enc = new HeadlessSystemTextEncoder().Encode(s);
            for (int i = 0; i < enc.Length; i++) rom.Data[addr + i] = enc[i];
            rom.Data[addr + enc.Length] = 0x00;
        }

        // ---- ReadCString -------------------------------------------------

        [Fact]
        public void ReadCString_NullRom_ReturnsEmpty()
        {
            Assert.Equal(string.Empty, CStringCore.ReadCString(null, 0x08001000));
        }

        [Fact]
        public void ReadCString_UnsafeAddress_ReturnsEmpty()
        {
            var rom = MakeRom();
            // Null (0) and a genuinely unsafe offset (< 0x200 danger zone) are
            // neither a safety pointer NOR a safety offset => "" (matches
            // CStringForm.Init's early return).
            Assert.Equal(string.Empty, CStringCore.ReadCString(rom, 0));
            Assert.Equal(string.Empty, CStringCore.ReadCString(rom, 0x10));
        }

        [Fact]
        public void ReadCString_AcceptsRawOffset()
        {
            // The Avalonia manual-address box supplies a raw OFFSET (e.g. 0x1000);
            // ReadCString must promote it to its GBA pointer form and decode, NOT
            // treat it as a text ID and return empty (Copilot finding #1).
            var rom = MakeRom();
            CoreState.ROM = rom;
            uint addr = 0x1000;
            WriteAscii(rom, addr, "Offset Path");

            Assert.Equal("Offset Path", CStringCore.ReadCString(rom, addr));            // raw offset
            Assert.Equal("Offset Path", CStringCore.ReadCString(rom, addr + 0x08000000)); // GBA pointer
        }

        [Fact]
        public void ReadCString_RoundTrips_Ascii()
        {
            var rom = MakeRom();
            CoreState.ROM = rom;
            uint addr = 0x1000;
            WriteAscii(rom, addr, "Hello World");

            string s = CStringCore.ReadCString(rom, addr + 0x08000000);
            Assert.Equal("Hello World", s);
        }

        [Fact]
        public void ReadCString_StripsAt001F()
        {
            // TextForm.Direct strips "@001F"; with the headless encoder the literal
            // characters "@001F" appear in the decoded string and must be removed.
            var rom = MakeRom();
            CoreState.ROM = rom;
            uint addr = 0x1000;
            WriteAscii(rom, addr, "AB@001FCD");

            string s = CStringCore.ReadCString(rom, addr + 0x08000000);
            Assert.Equal("ABCD", s);
        }

        [Fact]
        public void ReadCString_DoesNotThrow_OnAnyState()
        {
            var rom = MakeRom(0x300);
            CoreState.ROM = rom;
            // Pointer just inside the ROM with no terminator before EOF — must not throw.
            var ex = Record.Exception(() => CStringCore.ReadCString(rom, 0x080002F0));
            Assert.Null(ex);
        }

        // ---- OldRegionLength ---------------------------------------------

        [Fact]
        public void OldRegionLength_NullRom_ReturnsZero()
        {
            Assert.Equal(0u, CStringCore.OldRegionLength(null, 0x1000));
        }

        [Fact]
        public void OldRegionLength_PadsTo2_IncludingNul()
        {
            var rom = MakeRom();
            uint addr = 0x1000;
            WriteAscii(rom, addr, "Hi"); // 2 chars + NUL = 3 => Padding2 => 4.
            Assert.Equal(4u, CStringCore.OldRegionLength(rom, addr));

            WriteAscii(rom, addr + 0x10, "abc"); // 3 + NUL = 4 => already even => 4.
            Assert.Equal(4u, CStringCore.OldRegionLength(rom, addr + 0x10));
        }

        [Fact]
        public void OldRegionLength_RunsPastEof_ClampsToTail()
        {
            // A string whose NUL never appears before EOF: getString stops at EOF and
            // Padding2(len+1) would run past it; WF clamps to Data.Length - addr.
            var rom = MakeRom(0x1010);
            uint addr = 0x1000;
            for (uint i = addr; i < (uint)rom.Data.Length; i++) rom.Data[i] = (byte)'A';
            uint len = CStringCore.OldRegionLength(rom, addr);
            Assert.Equal((uint)rom.Data.Length - addr, len);
        }
    }

    /// <summary>
    /// ROM-mutating CStringCore.WriteCString tests. These set CoreState.ROM (the
    /// append path routes through RecycleAddress, which writes via CoreState.ROM)
    /// under a BeginUndoScope, so they live in the shared collection and restore
    /// CoreState.ROM + encoder on dispose.
    /// </summary>
    [Collection("SharedState")]
    public class CStringCoreWriteTests : IDisposable
    {
        readonly ROM? _savedRom;
        readonly ISystemTextEncoder? _savedEncoder;

        public CStringCoreWriteTests()
        {
            _savedRom = CoreState.ROM;
            _savedEncoder = CoreState.SystemTextEncoder;
            CoreState.SystemTextEncoder = new HeadlessSystemTextEncoder();
        }

        public void Dispose()
        {
            CoreState.ROM = _savedRom;
            CoreState.SystemTextEncoder = _savedEncoder;
            PatchDetection.ClearAllCaches();
        }

        static ROM MakeRom(int size = 0x200000)
        {
            var rom = new ROM();
            rom.LoadLow("test.gba", new byte[size], "NAZO");
            return rom;
        }

        static void WritePointer(ROM rom, uint slot, uint offset)
        {
            uint gba = offset + 0x08000000;
            rom.Data[slot + 0] = (byte)(gba & 0xFF);
            rom.Data[slot + 1] = (byte)((gba >> 8) & 0xFF);
            rom.Data[slot + 2] = (byte)((gba >> 16) & 0xFF);
            rom.Data[slot + 3] = (byte)((gba >> 24) & 0xFF);
        }

        static void WriteAscii(ROM rom, uint addr, string s)
        {
            byte[] enc = new HeadlessSystemTextEncoder().Encode(s);
            for (int i = 0; i < enc.Length; i++) rom.Data[addr + i] = enc[i];
            rom.Data[addr + enc.Length] = 0x00;
        }

        static Undo.UndoData NewUndo(ROM rom) => new Undo.UndoData
        {
            time = DateTime.Now,
            name = "cstring test",
            list = new List<Undo.UndoPostion>(),
            filesize = (uint)rom.Data.Length,
        };

        [Fact]
        public void WriteCString_NullRom_Refused()
        {
            var r = CStringCore.WriteCString(null, 0, 0x1000, "Hi");
            Assert.Equal(CStringCore.WriteStatus.Refused, r.Status);
        }

        [Fact]
        public void WriteCString_InPlace_SameLength_NoMove()
        {
            var rom = MakeRom();
            CoreState.ROM = rom;
            uint addr = 0x1000;
            WriteAscii(rom, addr, "AAAA"); // 4 + NUL = 5 => Padding2 => 6 bytes region.

            CStringCore.WriteResult r;
            using (ROM.BeginUndoScope(NewUndo(rom)))
            {
                r = CStringCore.WriteCString(rom, 0, addr, "BBBB"); // 4 + NUL = 5 <= 6.
            }
            Assert.Equal(CStringCore.WriteStatus.InPlace, r.Status);
            Assert.Equal(addr, r.Address);
            Assert.Equal((byte)'B', rom.Data[addr + 0]);
            Assert.Equal(0, rom.Data[addr + 4]); // NUL
        }

        [Fact]
        public void WriteCString_InPlace_Shorter_ZeroFillsSurplus()
        {
            var rom = MakeRom();
            CoreState.ROM = rom;
            uint addr = 0x1000;
            WriteAscii(rom, addr, "HELLOWORLD"); // 10 + NUL = 11 => Padding2 => 12.

            CStringCore.WriteResult r;
            using (ROM.BeginUndoScope(NewUndo(rom)))
            {
                r = CStringCore.WriteCString(rom, 0, addr, "Hi"); // 2 + NUL = 3 <= 12.
            }
            Assert.Equal(CStringCore.WriteStatus.InPlace, r.Status);
            Assert.Equal((byte)'H', rom.Data[addr + 0]);
            Assert.Equal((byte)'i', rom.Data[addr + 1]);
            Assert.Equal(0, rom.Data[addr + 2]); // NUL
            // Surplus (offsets 3..11) zero-filled.
            for (uint i = 3; i < 12; i++) Assert.Equal(0, rom.Data[addr + i]);
        }

        [Fact]
        public void WriteCString_Grow_Appends_RepointsParentSlot_ZerosOld()
        {
            var rom = MakeRom();
            CoreState.ROM = rom;
            uint slot = 0x400;     // >= 0x200 — outside the header danger zone.
            uint oldAddr = 0x1000;
            WriteAscii(rom, oldAddr, "Hi"); // small region (4 bytes).
            WritePointer(rom, slot, oldAddr);

            CStringCore.WriteResult r;
            using (ROM.BeginUndoScope(NewUndo(rom)))
            {
                // A much longer string forces a move.
                r = CStringCore.WriteCString(rom, slot, oldAddr, "A much longer string than before");
            }
            Assert.Equal(CStringCore.WriteStatus.Moved, r.Status);
            Assert.NotEqual(oldAddr, r.Address);
            Assert.True(r.RepointedSlots >= 1);
            // Parent slot now points at the new offset (p32 returns an offset).
            Assert.Equal(r.Address, rom.p32(slot));
            // Old region zeroed.
            Assert.Equal(0, rom.Data[oldAddr + 0]);
            // New region holds the new string (read it back).
            Assert.Equal("A much longer string than before",
                CStringCore.ReadCString(rom, r.Address + 0x08000000));
        }

        [Fact]
        public void WriteCString_Grow_RepointsScannerFoundReference_NoExplicitSlot()
        {
            // No explicit parentPointerSlot, but a raw pointer reference to the old
            // data exists elsewhere — the scanner finds + repoints it (covers the
            // scanner-repointed-slot case, Copilot finding #4).
            var rom = MakeRom();
            CoreState.ROM = rom;
            uint refSlot = 0x800;
            uint oldAddr = 0x1000;
            WriteAscii(rom, oldAddr, "Hi");
            WritePointer(rom, refSlot, oldAddr); // a discoverable raw reference.

            CStringCore.WriteResult r;
            using (ROM.BeginUndoScope(NewUndo(rom)))
            {
                r = CStringCore.WriteCString(rom, 0, oldAddr, "Now a far longer replacement string");
            }
            Assert.Equal(CStringCore.WriteStatus.Moved, r.Status);
            Assert.True(r.RepointedSlots >= 1);
            Assert.Equal(r.Address, rom.p32(refSlot));
        }

        [Fact]
        public void WriteCString_Grow_NoParentNoReference_RefusedNoMutation()
        {
            // Standalone grow with no parent slot and no discoverable reference =>
            // refuse (no orphan), Copilot finding #1.
            var rom = MakeRom();
            CoreState.ROM = rom;
            uint oldAddr = 0x1000;
            WriteAscii(rom, oldAddr, "Hi"); // small region, no references anywhere.
            byte before = rom.Data[oldAddr];

            CStringCore.WriteResult r;
            using (ROM.BeginUndoScope(NewUndo(rom)))
            {
                r = CStringCore.WriteCString(rom, 0, oldAddr, "Way too long to fit in place here");
            }
            Assert.Equal(CStringCore.WriteStatus.Refused, r.Status);
            Assert.Equal(before, rom.Data[oldAddr]); // unchanged.
        }

        [Fact]
        public void WriteCString_FreshAppend_RequiresParentSlot()
        {
            var rom = MakeRom();
            CoreState.ROM = rom;
            CStringCore.WriteResult r;
            using (ROM.BeginUndoScope(NewUndo(rom)))
            {
                r = CStringCore.WriteCString(rom, 0, 0, "New string"); // no addr, no slot.
            }
            Assert.Equal(CStringCore.WriteStatus.Refused, r.Status);
        }

        [Fact]
        public void WriteCString_FreshAppend_WithParentSlot_AppendsAndRepoints()
        {
            var rom = MakeRom();
            CoreState.ROM = rom;
            uint slot = 0x400;
            // Slot currently NULL (no old string); a fresh append wires it up.
            CStringCore.WriteResult r;
            using (ROM.BeginUndoScope(NewUndo(rom)))
            {
                r = CStringCore.WriteCString(rom, slot, 0, "Brand new");
            }
            Assert.Equal(CStringCore.WriteStatus.Moved, r.Status);
            Assert.Equal(r.Address, rom.p32(slot));
            Assert.Equal("Brand new", CStringCore.ReadCString(rom, r.Address + 0x08000000));
        }

        [Fact]
        public void WriteCString_Grow_DangerZoneSlot_NotUsable_RefusedNoMutation()
        {
            // A danger-zone slot (< 0x200) is NOT usable. With no discoverable
            // reference either, the grow is refused — and the invalid slot is never
            // fed to p32/write_p32 (Copilot findings #2/#3).
            var rom = MakeRom();
            CoreState.ROM = rom;
            uint badSlot = 0x100; // < 0x200 — danger zone, not a safety offset.
            uint oldAddr = 0x1000;
            WriteAscii(rom, oldAddr, "Hi"); // no references anywhere.
            byte[] snap = (byte[])rom.Data.Clone();

            CStringCore.WriteResult r;
            using (ROM.BeginUndoScope(NewUndo(rom)))
            {
                r = CStringCore.WriteCString(rom, badSlot, oldAddr, "Way too long to fit in place here");
            }
            Assert.Equal(CStringCore.WriteStatus.Refused, r.Status);
            Assert.Equal(snap, rom.Data); // no mutation, no header corruption.
        }

        [Fact]
        public void WriteCString_UnsafeAddr_RefusedNoMutation()
        {
            var rom = MakeRom();
            CoreState.ROM = rom;
            CStringCore.WriteResult r;
            using (ROM.BeginUndoScope(NewUndo(rom)))
            {
                r = CStringCore.WriteCString(rom, 0, 0x10 /* < 0x200 danger zone */, "x");
            }
            Assert.Equal(CStringCore.WriteStatus.Refused, r.Status);
        }
    }
}
