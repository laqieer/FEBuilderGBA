// SPDX-License-Identifier: GPL-3.0-or-later
//
// #2032: portrait-only safe-relocation helpers on ImageImportCore.
//
// A canonical 16 MiB FE8U (real-repro shape) portrait-sheet import placed
// D4/D8 bytes at 0x00802178/0x00802238, corrupting Battle Screen Layout
// data, because the portrait-specific wrappers around FindAndWriteData
// reused whatever ROM.FindFreeSpace's mid-ROM 0x00/0xFF scan happened to
// select -- including a legitimate vanilla data run.
//
// These tests cover ONLY the additive portrait helpers
// (TryGetPortraitReuseFloor / WriteCompressedPortraitToROM /
// WriteRawPortraitAppendAndRepoint / WritePortraitPaletteToROM /
// WriteHalfbodyPortraitPaletteToROM). The generic
// FindAndWriteData / ROM.FindFreeSpace / WriteCompressedToROM /
// WriteRawToROM / WritePaletteToROM / WriteCompressedInPlaceOrRelocate
// behavior for non-portrait callers is intentionally untouched and is
// NOT re-tested here (see ImageImportCoreWriteCompressedTests.cs).
using System;
using System.Linq;
using FEBuilderGBA;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    [Collection("SharedState")]
    public class ImageImportCorePortraitWriteTests : IDisposable
    {
        readonly ROM? _savedRom;

        public ImageImportCorePortraitWriteTests()
        {
            _savedRom = CoreState.ROM;
        }

        public void Dispose()
        {
            CoreState.ROM = _savedRom;
        }

        const uint Fe8Floor = 0x01000000u;
        const uint Fe6Floor = 0x00800000u;

        // Default length gives 8 KiB of room ABOVE the floor so at/above-floor
        // reuse/growth/shared-detach tests have real, in-bounds expansion-area
        // space to seed a blob into. Tests that specifically need the exact
        // floor-length boundary pass Fe8Floor explicitly.
        static ROM CreateRecognizedFe8Rom(uint length = Fe8Floor + 0x2000, byte fill = 0xFF)
        {
            byte[] data = new byte[length];
            Array.Fill(data, fill);
            var rom = new ROM();
            Assert.True(rom.LoadForceVersionFromBytes("synthetic-fe8u.gba", data, "FE8U"));
            CoreState.ROM = rom;
            return rom;
        }

        static ROM CreateRecognizedFe6Rom(uint length = Fe6Floor, byte fill = 0xFF)
        {
            byte[] data = new byte[length];
            Array.Fill(data, fill);
            var rom = new ROM();
            Assert.True(rom.LoadForceVersionFromBytes("synthetic-fe6.gba", data, "FE6"));
            CoreState.ROM = rom;
            return rom;
        }

        static ROM CreateRecognizedFe7Rom(uint length = Fe8Floor, byte fill = 0xFF)
        {
            byte[] data = new byte[length];
            Array.Fill(data, fill);
            var rom = new ROM();
            Assert.True(rom.LoadForceVersionFromBytes("synthetic-fe7u.gba", data, "FE7U"));
            CoreState.ROM = rom;
            return rom;
        }

        static void SetExtendsAddress(ROM rom, uint value)
        {
            var property = typeof(ROMFEINFO).GetProperty(nameof(ROMFEINFO.extends_address));
            Assert.NotNull(property);
            var setter = property.GetSetMethod(nonPublic: true);
            Assert.NotNull(setter);
            setter.Invoke(rom.RomInfo, new object[] { value });
        }

        static uint AssertRecognizedFloor(ROM rom, uint expected)
        {
            Assert.True(ImageImportCore.TryGetPortraitReuseFloor(rom, out uint floor));
            Assert.Equal(expected, floor);
            return floor;
        }

        static ROM CreateUnknownRom(uint length = 0x8000)
        {
            byte[] data = new byte[length];
            Array.Fill(data, (byte)0xAA);
            var rom = new ROM();
            Assert.True(rom.LoadLow("synthetic-unknown.gba", data, "NAZO"));
            CoreState.ROM = rom;
            return rom;
        }

        static byte[] LiteralLz77(byte seed, int uncompressedSize)
        {
            Assert.True(uncompressedSize >= 3);
            byte[] raw = Enumerable.Range(0, uncompressedSize)
                .Select(i => (byte)(seed + i)).ToArray();
            int flagCount = (uncompressedSize + 7) / 8;
            byte[] compressed = new byte[4 + flagCount + uncompressedSize];
            compressed[0] = 0x10;
            compressed[1] = (byte)(uncompressedSize & 0xFF);
            compressed[2] = (byte)((uncompressedSize >> 8) & 0xFF);
            compressed[3] = (byte)((uncompressedSize >> 16) & 0xFF);
            int src = 0, dst = 4;
            while (src < raw.Length)
            {
                compressed[dst++] = 0x00;
                int count = Math.Min(8, raw.Length - src);
                Array.Copy(raw, src, compressed, dst, count);
                src += count;
                dst += count;
            }
            return compressed;
        }

        static void SeedCompressedPointer(ROM rom, uint pointerEntryAddr, uint dataAddr, byte[] compressed)
        {
            rom.write_p32(pointerEntryAddr, dataAddr);
            rom.write_range(dataAddr, compressed);
        }

        static void AssertZeroFilled(ROM rom, uint addr, uint length)
        {
            for (uint i = 0; i < length; i++)
                Assert.Equal(0x00, rom.Data[addr + i]);
        }

        // ------------------------------------------------------------
        // TryGetPortraitReuseFloor
        // ------------------------------------------------------------

        [Fact]
        public void ReuseFloor_RecognizedFe8Profile_ReturnsExpectedFloor()
        {
            var rom = CreateRecognizedFe8Rom();
            Assert.Equal(8, rom.RomInfo.version);
            Assert.Equal(Fe8Floor, U.toOffset(rom.RomInfo.extends_address));

            Assert.True(ImageImportCore.TryGetPortraitReuseFloor(rom, out uint floor));
            Assert.Equal(Fe8Floor, floor);
        }

        [Fact]
        public void ReuseFloor_RecognizedFe6Profile_ReturnsExpectedFloor()
        {
            var rom = CreateRecognizedFe6Rom();
            Assert.Equal(6, rom.RomInfo.version);
            Assert.Equal(Fe6Floor, U.toOffset(rom.RomInfo.extends_address));

            Assert.True(ImageImportCore.TryGetPortraitReuseFloor(rom, out uint floor));
            Assert.Equal(Fe6Floor, floor);
        }

        [Fact]
        public void ReuseFloor_RecognizedFe7Profile_ReturnsExpectedFloor()
        {
            var rom = CreateRecognizedFe7Rom();
            Assert.Equal(7, rom.RomInfo.version);

            Assert.Equal(Fe8Floor, AssertRecognizedFloor(rom, Fe8Floor));
        }

        [Fact]
        public void ReuseFloor_UnknownRomfe0_FailsClosed()
        {
            var rom = CreateUnknownRom();
            Assert.Equal(0, rom.RomInfo.version); // ROMFE0: no recognized version constant
            Assert.False(ImageImportCore.TryGetPortraitReuseFloor(rom, out uint floor));
            Assert.Equal(0u, floor);
        }

        [Fact]
        public void ReuseFloor_NullRom_FailsClosed()
        {
            Assert.False(ImageImportCore.TryGetPortraitReuseFloor(null, out uint floor));
            Assert.Equal(0u, floor);
        }

        [Fact]
        public void ReuseFloor_CanonicalEofEqualsFloor_IsValid()
        {
            // Revision 9/10: "equal to canonical EOF is valid" -- exact
            // floor-length ROM must still resolve successfully.
            var rom = CreateRecognizedFe8Rom(Fe8Floor);
            Assert.Equal(Fe8Floor, (uint)rom.Data.Length);
            Assert.True(ImageImportCore.TryGetPortraitReuseFloor(rom, out uint floor));
            Assert.Equal(Fe8Floor, floor);
        }

        [Fact]
        public void ReuseFloor_ZeroOrMismatchedMetadata_FailsClosed()
        {
            var zero = CreateRecognizedFe8Rom();
            SetExtendsAddress(zero, 0);
            Assert.False(ImageImportCore.TryGetPortraitReuseFloor(zero, out _));

            var mismatched = CreateRecognizedFe8Rom();
            SetExtendsAddress(mismatched, Fe6Floor);
            Assert.False(ImageImportCore.TryGetPortraitReuseFloor(mismatched, out _));
        }

        [Fact]
        public void ReuseFloor_RecognizedButTruncatedRom_FailsClosed()
        {
            var rom = CreateRecognizedFe8Rom(Fe8Floor);
            rom.SwapNewROMDataDirect(
                rom.Data.Take(checked((int)Fe8Floor - 4)).ToArray());
            Assert.Equal(8, rom.RomInfo.version);
            Assert.False(ImageImportCore.TryGetPortraitReuseFloor(rom, out _));
        }

        [Fact]
        public void ReusableTarget_RequiresCompleteTargetInRecognizedExpansionSpace()
        {
            var rom = CreateRecognizedFe8Rom();
            const uint pointerAddr = 0x2F0;

            rom.write_p32(pointerAddr, Fe8Floor + 0x100);
            Assert.True(ImageImportCore.TryGetReusablePortraitTarget(
                rom, pointerAddr, out uint reusable));
            Assert.Equal(Fe8Floor + 0x100, reusable);

            rom.write_p32(pointerAddr, 0x00800000);
            Assert.False(ImageImportCore.TryGetReusablePortraitTarget(
                rom, pointerAddr, out _));

            rom.write_p32(pointerAddr, (uint)rom.Data.Length - 3);
            Assert.False(ImageImportCore.TryGetReusablePortraitTarget(
                rom, pointerAddr, out _));
        }

        // ------------------------------------------------------------
        // WriteCompressedPortraitToROM -- below-floor / unknown-metadata
        // append-only branch (never reuses, never scans, never clears).
        // ------------------------------------------------------------

        [Fact]
        public void WriteCompressedPortraitToROM_LegacyBelowFloor_NeverReusesEvenWithValidLz77Stream()
        {
            var rom = CreateRecognizedFe8Rom();
            const uint pointerAddr = 0x300;
            const uint legacyAddr = 0x00500000; // below the 0x01000000 floor
            byte[] legacyBlob = LiteralLz77(0x10, 64);
            SeedCompressedPointer(rom, pointerAddr, legacyAddr, legacyBlob);
            byte[] legacyBefore = rom.getBinaryData(legacyAddr, (uint)legacyBlob.Length);
            uint expectedAppendAddr = U.Padding4((uint)rom.Data.Length);

            byte[] newTile = Enumerable.Range(0, 32).Select(i => (byte)(0x40 + i)).ToArray();
            uint written = ImageImportCore.WriteCompressedPortraitToROM(rom, newTile, pointerAddr);

            Assert.NotEqual(U.NOT_FOUND, written);
            Assert.Equal(expectedAppendAddr, written); // appended, not reused in place
            Assert.Equal(written, rom.p32(pointerAddr));
            Assert.Equal(legacyBefore, rom.getBinaryData(legacyAddr, (uint)legacyBlob.Length)); // untouched
        }

        // Reproduces the reported #2032 shape: a legitimate vanilla data run
        // seeded below the floor at the exact reported collision offset must
        // survive a portrait import byte-for-byte, and the safe writer must
        // land the new payload somewhere else entirely (the aligned ROM
        // end), never hard-coding or depending on the real first-party
        // 0x00802178 address beyond using it as the seeded location.
        [Fact]
        public void WriteCompressedPortraitToROM_SeededLiveRegionBelowFloor_SurvivesImportUntouched()
        {
            var rom = CreateRecognizedFe8Rom();
            const uint pointerAddr = 0x304;
            const uint liveRegionAddr = 0x00802178; // matches the reported collision offset
            byte[] liveRegionBefore = Enumerable.Repeat((byte)0x53, 0x100).ToArray();
            rom.write_range(liveRegionAddr, liveRegionBefore);

            Assert.True(ImageImportCore.TryGetPortraitReuseFloor(rom, out uint floor));
            Assert.True(liveRegionAddr < floor, "the seeded live region must sit below the reuse floor");

            // Old pointer legitimately targets a valid LZ77 stream INSIDE the
            // seeded live region -- the exact shape that corrupted Battle
            // Screen Layout pre-fix.
            byte[] legacyBlob = LiteralLz77(0x20, 32);
            rom.write_p32(pointerAddr, liveRegionAddr);
            rom.write_range(liveRegionAddr, legacyBlob);
            byte[] liveRegionAfterSeed = rom.getBinaryData(liveRegionAddr, 0x100);

            byte[] newTile = Enumerable.Range(0, 64).Select(i => (byte)(0x60 + i)).ToArray();
            uint written = ImageImportCore.WriteCompressedPortraitToROM(rom, newTile, pointerAddr);

            Assert.NotEqual(U.NOT_FOUND, written);
            Assert.True(written >= floor || written >= (uint)liveRegionAddr + 0x100,
                "the new payload must never land inside the seeded live region");
            Assert.Equal(written, rom.p32(pointerAddr));
            Assert.Equal(liveRegionAfterSeed, rom.getBinaryData(liveRegionAddr, 0x100));
        }

        [Fact]
        public void WriteCompressedPortraitToROM_UnrecognizedRom_AlwaysAppendOnly()
        {
            var rom = CreateUnknownRom(0x8000);
            const uint pointerAddr = 0x300;
            const uint oldAddr = 0x1000;
            byte[] oldBlob = LiteralLz77(0x30, 16);
            SeedCompressedPointer(rom, pointerAddr, oldAddr, oldBlob);
            byte[] oldBefore = rom.getBinaryData(oldAddr, (uint)oldBlob.Length);
            uint expectedAppendAddr = U.Padding4((uint)rom.Data.Length);

            byte[] newTile = Enumerable.Range(0, 16).Select(i => (byte)i).ToArray();
            uint written = ImageImportCore.WriteCompressedPortraitToROM(rom, newTile, pointerAddr);

            Assert.False(ImageImportCore.TryGetPortraitReuseFloor(rom, out _));
            Assert.NotEqual(U.NOT_FOUND, written);
            Assert.Equal(expectedAppendAddr, written);
            Assert.Equal(oldBefore, rom.getBinaryData(oldAddr, (uint)oldBlob.Length));
        }

        [Fact]
        public void WriteCompressedPortraitToROM_OutOfBoundsExpansionPointer_AppendsWithoutInspectingTarget()
        {
            var rom = CreateRecognizedFe8Rom();
            const uint pointerAddr = 0x304;
            const uint invalidTarget = Fe8Floor + 0x4000;
            rom.write_p32(pointerAddr, invalidTarget);
            uint expectedAppendAddr = U.Padding4((uint)rom.Data.Length);

            byte[] newTile = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();
            uint written = ImageImportCore.WriteCompressedPortraitToROM(rom, newTile, pointerAddr);

            Assert.Equal(expectedAppendAddr, written);
            Assert.Equal(written, rom.p32(pointerAddr));
        }

        [Fact]
        public void WriteCompressedPortraitToROM_BareExpansionOffset_AppendsAndPreservesOldBlob()
        {
            var rom = CreateRecognizedFe8Rom();
            const uint pointerAddr = 0x306;
            uint bareOffset = Fe8Floor + 0x100;
            byte[] oldBlob = LiteralLz77(0x25, 64);
            rom.write_u32(pointerAddr, bareOffset);
            rom.write_range(bareOffset, oldBlob);
            byte[] oldBefore = rom.getBinaryData(
                bareOffset, (uint)oldBlob.Length);
            uint expectedAppendAddr = U.Padding4((uint)rom.Data.Length);

            uint written = ImageImportCore.WriteCompressedPortraitToROM(
                rom,
                Enumerable.Range(0, 16).Select(i => (byte)i).ToArray(),
                pointerAddr);

            Assert.Equal(expectedAppendAddr, written);
            Assert.True(U.isPointer(rom.u32(pointerAddr)));
            Assert.Equal(written, rom.p32(pointerAddr));
            Assert.Equal(oldBefore, rom.getBinaryData(
                bareOffset, (uint)oldBlob.Length));
        }

        [Fact]
        public void WriteCompressedPortraitToROM_MalformedExpansionStream_AppendsAndPreservesOldBlob()
        {
            var rom = CreateRecognizedFe8Rom();
            const uint pointerAddr = 0x307;
            uint oldAddr = Fe8Floor + 0x180;
            byte[] malformed =
            {
                0x10, 0x03, 0x00, 0x00, 0x80,
                0x00, 0x00, 0x11, 0x22, 0x33,
            };
            rom.write_p32(pointerAddr, oldAddr);
            rom.write_range(oldAddr, malformed);
            Assert.NotEqual(0u, LZ77.getCompressedSize(
                rom.Data, oldAddr));
            Assert.Equal(0u, LZ77.getCompressedSizeStrict(
                rom.Data, oldAddr));
            byte[] oldBefore = rom.getBinaryData(
                oldAddr, (uint)malformed.Length);
            uint expectedAppendAddr = U.Padding4((uint)rom.Data.Length);

            uint written = ImageImportCore.WriteCompressedPortraitToROM(
                rom,
                Enumerable.Range(0, 16).Select(i => (byte)i).ToArray(),
                pointerAddr);

            Assert.Equal(expectedAppendAddr, written);
            Assert.Equal(written, rom.p32(pointerAddr));
            Assert.Equal(oldBefore, rom.getBinaryData(
                oldAddr, (uint)malformed.Length));
        }

        // ------------------------------------------------------------
        // WriteCompressedPortraitToROM -- at/above-floor delegation to the
        // existing, unchanged WriteCompressedInPlaceOrRelocate.
        // ------------------------------------------------------------

        [Fact]
        public void WriteCompressedPortraitToROM_AtFloor_PrivateFitReusesInPlace()
        {
            var rom = CreateRecognizedFe8Rom();
            uint floor = AssertRecognizedFloor(rom, Fe8Floor);
            const uint pointerAddr = 0x308;
            uint oldAddr = floor; // exactly at the floor
            byte[] oldBlob = LiteralLz77(0x10, 64);
            uint oldSize = U.Padding4(LZ77.getCompressedSize(oldBlob, 0));
            SeedCompressedPointer(rom, pointerAddr, oldAddr, oldBlob);

            byte[] newTile = Enumerable.Range(0, 16).Select(i => (byte)(0x50 + i)).ToArray();
            uint written = ImageImportCore.WriteCompressedPortraitToROM(rom, newTile, pointerAddr);

            Assert.Equal(oldAddr, written); // reused in place
            Assert.Equal(oldAddr, rom.p32(pointerAddr));
        }

        [Fact]
        public void WriteCompressedPortraitToROM_AboveFloor_GrowthRelocatesAndFreesOldPrivateBlob()
        {
            var rom = CreateRecognizedFe8Rom();
            uint floor = AssertRecognizedFloor(rom, Fe8Floor);
            const uint pointerAddr = 0x30C;
            uint oldAddr = floor + 0x100;
            byte[] oldBlob = LiteralLz77(0x20, 8);
            uint oldSize = U.Padding4(LZ77.getCompressedSize(oldBlob, 0));
            SeedCompressedPointer(rom, pointerAddr, oldAddr, oldBlob);

            byte[] newTile = Enumerable.Range(0, 256).Select(i => (byte)i).ToArray();
            uint written = ImageImportCore.WriteCompressedPortraitToROM(rom, newTile, pointerAddr);

            Assert.NotEqual(U.NOT_FOUND, written);
            Assert.NotEqual(oldAddr, written);
            Assert.Equal(written, rom.p32(pointerAddr));
            AssertZeroFilled(rom, oldAddr, oldSize);
        }

        [Fact]
        public void WriteCompressedPortraitToROM_AboveFloor_SharedBlobDetachesWithoutMutatingSibling()
        {
            var rom = CreateRecognizedFe8Rom();
            const uint pointerA = 0x310;
            const uint pointerB = 0x314;
            uint sharedAddr = Fe8Floor + 0x200;
            byte[] oldBlob = LiteralLz77(0x30, 8);
            SeedCompressedPointer(rom, pointerA, sharedAddr, oldBlob);
            rom.write_p32(pointerB, sharedAddr);
            byte[] siblingBefore = rom.getBinaryData(sharedAddr, (uint)oldBlob.Length);
            uint floor = AssertRecognizedFloor(rom, Fe8Floor);
            Assert.True(sharedAddr >= floor);
            Assert.True(
                (ulong)sharedAddr + (ulong)oldBlob.Length
                <= (ulong)rom.Data.Length);

            byte[] newTile = Enumerable.Range(0, 256).Select(i => (byte)(0x70 + i)).ToArray();
            uint written = ImageImportCore.WriteCompressedPortraitToROM(rom, newTile, pointerA);

            Assert.NotEqual(U.NOT_FOUND, written);
            Assert.NotEqual(sharedAddr, written);
            Assert.Equal(written, rom.p32(pointerA));
            Assert.Equal(sharedAddr, rom.p32(pointerB)); // Slot B untouched
            Assert.Equal(siblingBefore, rom.getBinaryData(sharedAddr, (uint)oldBlob.Length));
        }

        // ------------------------------------------------------------
        // WriteRawPortraitAppendAndRepoint -- exact append contract.
        // ------------------------------------------------------------

        [Fact]
        public void WriteRawPortraitAppendAndRepoint_AlignedStart_WritesExactBytesAndRepoints()
        {
            var rom = CreateUnknownRom(0x8000); // already 4-byte aligned length
            const uint pointerAddr = 0x200;
            byte[] payload = Enumerable.Range(0, 40).Select(i => (byte)i).ToArray();
            uint expectedAddr = U.Padding4((uint)rom.Data.Length);

            uint written = ImageImportCore.WriteRawPortraitAppendAndRepoint(rom, pointerAddr, payload);

            Assert.Equal(expectedAddr, written);
            Assert.Equal(written, rom.p32(pointerAddr));
            Assert.Equal(payload, rom.getBinaryData(written, (uint)payload.Length));
        }

        [Fact]
        public void WriteRawPortraitAppendAndRepoint_UnalignedPayload_ZeroFillsTailAndRepointsAfterWrite()
        {
            var rom = CreateUnknownRom(0x8001); // forces an unaligned starting length
            const uint pointerAddr = 0x204;
            byte[] payload = Enumerable.Range(0, 13).Select(i => (byte)(0x80 + i)).ToArray(); // unaligned length
            uint expectedAddr = U.Padding4((uint)rom.Data.Length);
            uint expectedPadded = U.Padding4((uint)payload.Length);

            uint written = ImageImportCore.WriteRawPortraitAppendAndRepoint(rom, pointerAddr, payload);

            Assert.Equal(expectedAddr, written);
            Assert.Equal(written, rom.p32(pointerAddr));
            Assert.Equal(payload, rom.getBinaryData(written, (uint)payload.Length));
            AssertZeroFilled(rom, written + (uint)payload.Length, expectedPadded - (uint)payload.Length);
        }

        [Fact]
        public void WriteRawPortraitAppendAndRepoint_NeverInspectsOrClearsOldTarget()
        {
            var rom = CreateUnknownRom(0x8000);
            const uint pointerAddr = 0x208;
            const uint oldAddr = 0x1000;
            byte[] oldData = Enumerable.Repeat((byte)0x77, 64).ToArray();
            rom.write_p32(pointerAddr, oldAddr);
            rom.write_range(oldAddr, oldData);

            byte[] payload = Enumerable.Range(0, 20).Select(i => (byte)i).ToArray();
            uint written = ImageImportCore.WriteRawPortraitAppendAndRepoint(rom, pointerAddr, payload);

            Assert.NotEqual(oldAddr, written);
            Assert.Equal(oldData, rom.getBinaryData(oldAddr, (uint)oldData.Length)); // never touched
        }

        [Fact]
        public void WriteRawPortraitAppendAndRepoint_ExactCap_SucceedsAtThirtyTwoMebibyteBoundary()
        {
            byte[] payload = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();
            uint needSize = U.Padding4((uint)payload.Length);
            uint romLength = 0x02000000 - needSize;
            byte[] data = new byte[romLength];
            Array.Fill(data, (byte)0xAA);
            var rom = new ROM();
            Assert.True(rom.LoadLow("synthetic-cap.gba", data, "NAZO"));
            CoreState.ROM = rom;
            const uint pointerAddr = 0x20C;

            uint written = ImageImportCore.WriteRawPortraitAppendAndRepoint(rom, pointerAddr, payload);

            Assert.Equal(romLength, written);
            Assert.Equal(0x02000000u, (uint)rom.Data.Length);
            Assert.Equal(written, rom.p32(pointerAddr));
        }

        [Fact]
        public void WriteRawPortraitAppendAndRepoint_OverflowsCap_FailsAtomicallyWithoutMutation()
        {
            uint romLength = 0x01FFFFF8;
            byte[] data = new byte[romLength];
            Array.Fill(data, (byte)0xAA);
            var rom = new ROM();
            Assert.True(rom.LoadLow("synthetic-overflow.gba", data, "NAZO"));
            CoreState.ROM = rom;
            const uint pointerAddr = 0x210;
            const uint oldAddr = 0x300;
            byte[] oldData = Enumerable.Repeat((byte)0x99, 16).ToArray();
            rom.write_p32(pointerAddr, oldAddr);
            rom.write_range(oldAddr, oldData);
            byte[] payload = Enumerable.Range(0, 64).Select(i => (byte)i).ToArray(); // pushes past the cap

            uint written = ImageImportCore.WriteRawPortraitAppendAndRepoint(rom, pointerAddr, payload);

            Assert.Equal(U.NOT_FOUND, written);
            Assert.Equal(romLength, (uint)rom.Data.Length);
            Assert.Equal(oldAddr, rom.p32(pointerAddr)); // pointer never mutated
            Assert.Equal(oldData, rom.getBinaryData(oldAddr, (uint)oldData.Length));
        }

        [Fact]
        public void PortraitWriters_OutOfBoundsPointerSlots_FailWithoutMutation()
        {
            var rom = CreateUnknownRom(0x8000);
            byte[] before = rom.Data.ToArray();
            uint invalidPointerAddr = (uint)rom.Data.Length - 3;
            uint invalidEntryAddr = (uint)rom.Data.Length - 11;
            byte[] payload = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();

            Assert.Equal(U.NOT_FOUND,
                ImageImportCore.WriteRawPortraitAppendAndRepoint(rom, invalidPointerAddr, payload));
            Assert.Equal(U.NOT_FOUND,
                ImageImportCore.WriteCompressedPortraitToROM(rom, payload, invalidPointerAddr));
            Assert.Equal(U.NOT_FOUND,
                ImageImportCore.WritePortraitPaletteToROM(rom, payload, invalidPointerAddr));
            Assert.Equal(U.NOT_FOUND,
                ImageImportCore.WritePortraitEntryPaletteToROM(rom, payload, invalidEntryAddr));
            Assert.Equal(before, rom.Data);
        }

        [Fact]
        public void WriteRawPortraitAppendAndRepoint_AmbientUndoRestoresPointerBytesAndLength()
        {
            var rom = CreateUnknownRom(0x8000);
            const uint pointerAddr = 0x214;
            byte[] beforeRom = rom.Data.ToArray();
            uint beforeLength = (uint)rom.Data.Length;

            var undo = new Undo();
            var ud = new Undo.UndoData
            {
                time = DateTime.Now,
                name = "raw-portrait-append",
                list = new System.Collections.Generic.List<Undo.UndoPostion>(),
                filesize = beforeLength
            };

            byte[] payload = Enumerable.Range(0, 40).Select(i => (byte)i).ToArray();
            uint written;
            using (ROM.BeginUndoScope(ud))
            {
                written = ImageImportCore.WriteRawPortraitAppendAndRepoint(rom, pointerAddr, payload);
            }
            Assert.NotEqual(U.NOT_FOUND, written);
            Assert.True(rom.Data.Length > beforeLength);

            int postionBeforeRollback = undo.Postion; // 0: nothing pushed onto this fresh Undo yet.
            undo.Rollback(ud); // Push(ud) then RunUndo() -- the real two-call CLI/Avalonia contract.

            Assert.Equal(beforeLength, (uint)rom.Data.Length);
            Assert.Equal(beforeRom, rom.Data);
            // #2032: assert the UNDO CURSOR itself is restored -- not merely that
            // UndoBuffer is non-empty (Push always appends a record; a broken Patch
            // that no-ops would still leave the buffer non-empty while corrupting
            // the ROM). Rollback(ud) = Push(ud) [Postion -> 1] then RunUndo()
            // [Rollback(Postion-1)=Rollback(0) -> Postion -> 0], so Postion must land
            // back exactly where it started (0), proving RunUndo() actually replayed
            // the patch rather than merely recording it.
            Assert.Equal(postionBeforeRollback, undo.Postion);
            Assert.Equal(0, undo.Postion);
        }

        [Fact]
        public void PortraitRollback_UnalignedInput_RestoresExactOriginalLength()
        {
            var rom = CreateUnknownRom(0x8001);
            const uint pointerAddr = 0x216;
            byte[] beforeRom = rom.Data.ToArray();
            uint beforeLength = (uint)rom.Data.Length;
            var undo = new Undo();
            Undo.UndoData ud = undo.NewUndoData("unaligned-portrait-rollback");

            using (ROM.BeginUndoScope(ud))
            {
                Assert.NotEqual(U.NOT_FOUND,
                    ImageImportCore.WriteRawPortraitAppendAndRepoint(
                        rom, pointerAddr, Enumerable.Repeat((byte)0x5A, 32).ToArray()));
            }

            undo.Push(ud);
            undo.RunUndo();
            Assert.Equal(U.Padding4(beforeLength), (uint)rom.Data.Length);

            ImageImportCore.RestorePortraitRomLengthAfterUndo(rom, beforeLength);

            Assert.Equal(beforeLength, (uint)rom.Data.Length);
            Assert.Equal(beforeRom, rom.Data);
            Assert.Equal(0, undo.Postion);
        }

        [Fact]
        public void CliTransactionPattern_TileSucceedsPaletteOverflows_FullRollbackRestoresRomAndUndoPostion()
        {
            // Mirrors the ACTUAL two-write CLI/Avalonia portrait transaction
            // (FEBuilderGBA.CLI.Program.ImportPortraitFromFile: a tile write via
            // WriteRawPortraitAppendAndRepoint/WriteCompressedPortraitToROM, THEN a
            // WritePortraitPaletteToROM call) inside one BeginUndoScope, followed by
            // the EXACT failure-path contract (undo.Push(ud); undo.RunUndo();) used
            // by that method's finally block when the second write fails. This proves
            // a mid-transaction failure (tile ok, palette hits the 32 MiB cap) rolls
            // back BOTH writes -- never leaves a half-written portrait -- and that the
            // undo cursor itself (not just a non-empty UndoBuffer) is restored.
            // See PortraitSourceRoutingGuardTests for the source-level guard proving
            // the production CLI method actually contains this exact control flow.
            byte[] tilePayload = Enumerable.Range(0, 100).Select(i => (byte)i).ToArray();
            const uint palNeed = 32u; // WritePortraitPaletteToROM always normalizes to 32 bytes.
            uint tileNeed = U.Padding4((uint)tilePayload.Length);
            uint headroomAfterTile = palNeed - 16; // deliberately 16 bytes short of the palette's need.
            uint romLength = 0x02000000 - headroomAfterTile - tileNeed;

            byte[] data = new byte[romLength];
            Array.Fill(data, (byte)0xAA);
            var rom = new ROM();
            Assert.True(rom.LoadLow("synthetic-cli-txn.gba", data, "NAZO"));
            CoreState.ROM = rom;

            const uint tilePointerAddr = 0x220;
            const uint palPointerAddr = 0x228;
            byte[] beforeRom = rom.Data.ToArray();
            uint beforeLength = (uint)rom.Data.Length;

            var undo = new Undo();
            var ud = new Undo.UndoData
            {
                time = DateTime.Now,
                name = "cli-portrait-import",
                list = new System.Collections.Generic.List<Undo.UndoPostion>(),
                filesize = beforeLength
            };

            int postionBeforeTxn = undo.Postion;
            uint tileWritten, palWritten;
            using (ROM.BeginUndoScope(ud))
            {
                tileWritten = ImageImportCore.WriteRawPortraitAppendAndRepoint(rom, tilePointerAddr, tilePayload);
                palWritten = ImageImportCore.WritePortraitPaletteToROM(
                    rom, Enumerable.Repeat((byte)0x77, 32).ToArray(), palPointerAddr);
            }

            Assert.NotEqual(U.NOT_FOUND, tileWritten); // first write succeeded -- ROM was mutated mid-transaction.
            Assert.Equal(U.NOT_FOUND, palWritten);     // second write failed -- this is the rollback trigger.
            Assert.True(rom.Data.Length > beforeLength); // confirms the mutation actually happened before rollback.

            // #2032 CLI failure-path contract: Push(ud) then RunUndo() (NOT Rollback(ud) --
            // pinning the exact two-call sequence Program.cs uses).
            undo.Push(ud);
            undo.RunUndo();

            Assert.Equal(beforeLength, (uint)rom.Data.Length);
            Assert.Equal(beforeRom, rom.Data); // both the appended tile bytes AND the pointer writes are undone.
            Assert.Equal(postionBeforeTxn, undo.Postion); // undo cursor restored, not just a non-empty buffer.
            Assert.Equal(0, undo.Postion);
        }

        // ------------------------------------------------------------
        // Portrait palette normalization (32 bytes standard / 64 halfbody).
        // ------------------------------------------------------------

        [Fact]
        public void WritePortraitPaletteToROM_ShortPalette_PadsToThirtyTwoBytes()
        {
            var rom = CreateUnknownRom(0x8000);
            const uint pointerAddr = 0x218;
            byte[] shortPalette = Enumerable.Range(0, 10).Select(i => (byte)(0x10 + i)).ToArray();

            uint written = ImageImportCore.WritePortraitPaletteToROM(rom, shortPalette, pointerAddr);

            byte[] stored = rom.getBinaryData(written, 32);
            Assert.Equal(shortPalette, stored.Take(10).ToArray());
            AssertZeroFilled(rom, written + 10, 32 - 10);
        }

        [Fact]
        public void WritePortraitPaletteToROM_OverlongPalette_TruncatesToThirtyTwoBytes()
        {
            var rom = CreateUnknownRom(0x8000);
            const uint pointerAddr = 0x21C;
            byte[] longPalette = Enumerable.Range(0, 64).Select(i => (byte)i).ToArray();

            uint written = ImageImportCore.WritePortraitPaletteToROM(rom, longPalette, pointerAddr);

            byte[] stored = rom.getBinaryData(written, 32);
            Assert.Equal(longPalette.Take(32).ToArray(), stored);
        }

        [Fact]
        public void WriteHalfbodyPortraitPaletteToROM_ExactSixtyFourBytes_NeverNormalizedToThirtyTwo()
        {
            var rom = CreateUnknownRom(0x8000);
            const uint pointerAddr = 0x220;
            byte[] palette64 = Enumerable.Range(0, 64).Select(i => (byte)(0x20 + i)).ToArray();

            uint written = ImageImportCore.WriteHalfbodyPortraitPaletteToROM(rom, palette64, pointerAddr);

            Assert.Equal(palette64, rom.getBinaryData(written, 64));
        }

        [Fact]
        public void WriteHalfbodyPortraitPaletteToROM_OneBank_DuplicatesItIntoBothBanks()
        {
            var rom = CreateUnknownRom(0x8000);
            const uint pointerAddr = 0x224;
            byte[] palette32 = Enumerable.Range(0, 32)
                .Select(i => (byte)(0x30 + i)).ToArray();

            uint written = ImageImportCore.WriteHalfbodyPortraitPaletteToROM(
                rom, palette32, pointerAddr);

            Assert.Equal(palette32, rom.getBinaryData(written, 32));
            Assert.Equal(palette32, rom.getBinaryData(written + 32, 32));
        }

        [Fact]
        public void WritePortraitEntryPaletteToROM_StandardEntry_WritesThirtyTwoBytes()
        {
            var rom = CreateRecognizedFe8Rom();
            const uint entryAddr = 0x240;
            const uint faceAddr = 0x2000;
            rom.write_p32(entryAddr, faceAddr);
            rom.write_u32(faceAddr, 0x00000010); // compressed, not halfbody raw header
            byte[] palette64 = Enumerable.Range(0, 64).Select(i => (byte)i).ToArray();

            uint written = ImageImportCore.WritePortraitEntryPaletteToROM(
                rom, palette64, entryAddr);

            Assert.NotEqual(U.NOT_FOUND, written);
            Assert.Equal(palette64.Take(32).ToArray(), rom.getBinaryData(written, 32));
            Assert.Equal(written, rom.p32(entryAddr + 8));
        }

        [Fact]
        public void WritePortraitEntryPaletteToROM_HalfbodyEntry_PreservesSixtyFourBytes()
        {
            var rom = CreateRecognizedFe8Rom();
            const uint entryAddr = 0x260;
            const uint faceAddr = Fe8Floor + 0x300;
            rom.write_p32(entryAddr, faceAddr);
            rom.write_u32(faceAddr, 0x00200400);
            byte[] palette64 = Enumerable.Range(0, 64)
                .Select(i => (byte)(0x40 + i)).ToArray();

            uint written = ImageImportCore.WritePortraitEntryPaletteToROM(
                rom, palette64, entryAddr);

            Assert.NotEqual(U.NOT_FOUND, written);
            Assert.True(ImageImportCore.IsHalfbodyPortraitEntry(rom, entryAddr));
            Assert.Equal(palette64, rom.getBinaryData(written, 64));
            Assert.Equal(written, rom.p32(entryAddr + 8));
        }

        [Fact]
        public void WritePortraitEntryPaletteToROM_BelowFloorRawHalfbodyHeader_UsesThirtyTwoBytesWithoutInspectingTarget()
        {
            var rom = CreateRecognizedFe8Rom();
            const uint entryAddr = 0x270;
            const uint faceAddr = 0x3000;
            rom.write_p32(entryAddr, faceAddr);
            rom.write_u32(faceAddr, 0x00200400);
            byte[] faceBefore = rom.getBinaryData(faceAddr, 4);
            byte[] palette64 = Enumerable.Range(0, 64).Select(i => (byte)i).ToArray();

            uint written = ImageImportCore.WritePortraitEntryPaletteToROM(
                rom, palette64, entryAddr);

            Assert.False(ImageImportCore.IsHalfbodyPortraitEntry(rom, entryAddr));
            Assert.Equal(palette64.Take(32).ToArray(), rom.getBinaryData(written, 32));
            Assert.Equal(faceBefore, rom.getBinaryData(faceAddr, 4));
        }

        [Fact]
        public void IsHalfbodyPortraitEntry_BareExpansionOffset_FailsClosed()
        {
            var rom = CreateRecognizedFe8Rom();
            const uint entryAddr = 0x278;
            uint bareOffset = Fe8Floor + 0x500;
            rom.write_u32(entryAddr, bareOffset);
            rom.write_u32(bareOffset, 0x00200400);

            Assert.False(ImageImportCore.IsHalfbodyPortraitEntry(
                rom, entryAddr));
        }

        [Theory]
        [InlineData(0u)]
        [InlineData(0x00001000u)]
        [InlineData(0x02002000u)]
        public void WritePortraitEntryPaletteToROM_InvalidOrGarbageFace_UsesThirtyTwoBytes(
            uint faceAddr)
        {
            var rom = CreateRecognizedFe8Rom();
            const uint entryAddr = 0x280;
            if (faceAddr != 0 && faceAddr < (uint)rom.Data.Length)
            {
                rom.write_p32(entryAddr, faceAddr);
                rom.write_u32(faceAddr, 0xDEADBEEFu);
            }
            else
            {
                rom.write_p32(entryAddr, faceAddr);
            }
            byte[] palette64 = Enumerable.Range(0, 64).Select(i => (byte)i).ToArray();

            uint written = ImageImportCore.WritePortraitEntryPaletteToROM(
                rom, palette64, entryAddr);

            Assert.Equal(palette64.Take(32).ToArray(), rom.getBinaryData(written, 32));
        }

        [Fact]
        public void WritePortraitEntryPaletteToROM_CompressedFace_UsesThirtyTwoBytes()
        {
            var rom = CreateRecognizedFe8Rom();
            const uint entryAddr = 0x2A0;
            const uint faceAddr = 0x4000;
            byte[] compressed = LiteralLz77(0x40, 64);
            rom.write_p32(entryAddr, faceAddr);
            rom.write_range(faceAddr, compressed);
            byte[] palette64 = Enumerable.Range(0, 64).Select(i => (byte)i).ToArray();

            uint written = ImageImportCore.WritePortraitEntryPaletteToROM(
                rom, palette64, entryAddr);

            Assert.Equal(palette64.Take(32).ToArray(), rom.getBinaryData(written, 32));
        }

        [Fact]
        public void WritePortraitEntryPaletteToROM_NonFe8RawHeader_UsesThirtyTwoBytes()
        {
            var rom = CreateRecognizedFe6Rom(Fe6Floor + 0x2000);
            const uint entryAddr = 0x2C0;
            const uint faceAddr = 0x4000;
            rom.write_p32(entryAddr, faceAddr);
            rom.write_u32(faceAddr, 0x00200400);
            byte[] palette64 = Enumerable.Range(0, 64).Select(i => (byte)i).ToArray();

            uint written = ImageImportCore.WritePortraitEntryPaletteToROM(
                rom, palette64, entryAddr);

            Assert.Equal(palette64.Take(32).ToArray(), rom.getBinaryData(written, 32));
        }
    }
}
