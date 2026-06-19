using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    public class RebuildCoreTests
    {
        [Fact]
        public void FindPointers_DetectsGBAPointers()
        {
            byte[] rom = new byte[256];
            uint ptr = 0x08000080;
            rom[0] = (byte)(ptr & 0xFF);
            rom[1] = (byte)((ptr >> 8) & 0xFF);
            rom[2] = (byte)((ptr >> 16) & 0xFF);
            rom[3] = (byte)((ptr >> 24) & 0xFF);

            var ptrs = RebuildCore.FindPointers(rom);
            Assert.True(ptrs.ContainsKey(0));
            Assert.Equal(ptr, ptrs[0]);
        }

        [Fact]
        public void FindPointers_NullReturnsEmpty()
        {
            var ptrs = RebuildCore.FindPointers(null);
            Assert.Empty(ptrs);
        }

        [Fact]
        public void FindModifiedRegions_DetectsChanges()
        {
            byte[] vanilla = new byte[64];
            byte[] modified = new byte[64];
            // Modify bytes at offset 10-14
            for (int i = 10; i < 15; i++)
                modified[i] = 0xFF;

            var regions = RebuildCore.FindModifiedRegions(vanilla, modified);
            Assert.NotEmpty(regions);
            Assert.Contains(regions, r => r.offset == 10);
        }

        [Fact]
        public void FindModifiedRegions_IdenticalReturnsEmpty()
        {
            byte[] data = new byte[64];
            var regions = RebuildCore.FindModifiedRegions(data, (byte[])data.Clone());
            Assert.Empty(regions);
        }

        [Fact]
        public void FindModifiedRegions_NullReturnsEmpty()
        {
            var regions = RebuildCore.FindModifiedRegions(null, new byte[64]);
            Assert.Empty(regions);
        }

        [Fact]
        public void FindModifiedRegions_ExtendedDataReportsAdditional()
        {
            byte[] vanilla = new byte[32];
            byte[] modified = new byte[64];
            // Even if first 32 bytes are same, the extended part is reported
            var regions = RebuildCore.FindModifiedRegions(vanilla, modified);
            Assert.Contains(regions, r => r.offset == 32);
        }

        [Fact]
        public void FindFreeSpace_FindsFreeRegions()
        {
            byte[] rom = new byte[128];
            // Fill with data
            for (int i = 0; i < rom.Length; i++)
                rom[i] = 0xAA;
            // Create a free region at offset 32-63 (0xFF)
            for (int i = 32; i < 64; i++)
                rom[i] = 0xFF;

            var free = RebuildCore.FindFreeSpace(rom, 16);
            Assert.NotEmpty(free);
            Assert.Contains(free, f => f.offset == 32 && f.length == 32);
        }

        [Fact]
        public void FindFreeSpace_IgnoresSmallRegions()
        {
            byte[] rom = new byte[128];
            for (int i = 0; i < rom.Length; i++)
                rom[i] = 0xAA;
            // Small free region (4 bytes)
            rom[10] = 0xFF; rom[11] = 0xFF; rom[12] = 0xFF; rom[13] = 0xFF;

            var free = RebuildCore.FindFreeSpace(rom, 16);
            Assert.Empty(free);
        }

        [Fact]
        public void Rebuild_ReturnsSuccess()
        {
            byte[] vanilla = new byte[128];
            byte[] modified = new byte[128];
            modified[10] = 0xFF;

            var result = RebuildCore.Rebuild(vanilla, modified);
            Assert.True(result.Success);
            Assert.True(result.BlocksMoved > 0);
        }

        [Fact]
        public void Rebuild_NullReturnsFailure()
        {
            var result = RebuildCore.Rebuild(null, new byte[64]);
            Assert.False(result.Success);
        }

        [Fact]
        public void BuildRebuildReport_ContainsHeaderAndRegions()
        {
            byte[] vanilla = new byte[128];
            byte[] modified = new byte[128];
            for (int i = 10; i < 15; i++) modified[i] = 0xAA;   // a modified region

            string report = RebuildCore.BuildRebuildReport(vanilla, modified, 0x09000000);
            Assert.Contains("@_CRC32 ", report);
            Assert.Contains("@_REBUILDADDRESS 09000000", report);   // ToHexString(uint) pads to X08
            Assert.Contains("@MOD ", report);
            Assert.Contains("MODIFIED REGIONS:", report);
            Assert.Contains("FREE SPACE:", report);
        }

        [Fact]
        public void WriteRebuildReport_WritesFile_AndReportsSuccess()
        {
            byte[] vanilla = new byte[256];
            byte[] modified = new byte[256];
            modified[20] = 0xFF; modified[21] = 0xFF;

            string outPath = Path.Combine(Path.GetTempPath(), "feb_rebuild_" + Guid.NewGuid().ToString("N") + ".rebuild");
            try
            {
                var result = RebuildCore.WriteRebuildReport(vanilla, modified, 0x09000000, outPath);
                Assert.True(result.Success);
                Assert.True(File.Exists(outPath));
                string text = File.ReadAllText(outPath);
                Assert.Contains("@_CRC32 ", text);
                Assert.Contains(outPath, result.Message);   // message reports the output path
                // No leftover temp artifact.
                Assert.False(File.Exists(outPath + ".tmp"));
            }
            finally { try { File.Delete(outPath); } catch { } }
        }

        [Fact]
        public void WriteRebuildReport_OverwritesExistingReport_AtomicReplace()
        {
            // Writing over an existing report must succeed (atomic File.Move overwrite)
            // and leave the file holding the NEW content — not lose it to a
            // delete-then-failed-move window.
            byte[] vanilla = new byte[256];
            byte[] modified1 = new byte[256]; modified1[20] = 0xFF;
            byte[] modified2 = new byte[256]; modified2[20] = 0xFF; modified2[21] = 0xFF; modified2[100] = 0xAB;

            string outPath = Path.Combine(Path.GetTempPath(), "feb_rebuild_ovr_" + Guid.NewGuid().ToString("N") + ".rebuild");
            try
            {
                var r1 = RebuildCore.WriteRebuildReport(vanilla, modified1, 0x09000000, outPath);
                Assert.True(r1.Success);
                Assert.True(File.Exists(outPath));

                // Second write replaces the first; file still present + parseable, no temp left.
                var r2 = RebuildCore.WriteRebuildReport(vanilla, modified2, 0x09100000, outPath);
                Assert.True(r2.Success);
                Assert.True(File.Exists(outPath));
                Assert.False(File.Exists(outPath + ".tmp"));
                string text = File.ReadAllText(outPath);
                Assert.Contains("@_CRC32 ", text);
                Assert.Contains("@_REBUILDADDRESS ", text);
            }
            finally { try { File.Delete(outPath); } catch { } }
        }

        [Fact]
        public void WriteRebuildReport_NullData_ReturnsFailure_NoFile()
        {
            string outPath = Path.Combine(Path.GetTempPath(), "feb_rebuild_null_" + Guid.NewGuid().ToString("N") + ".rebuild");
            var result = RebuildCore.WriteRebuildReport(null, new byte[64], 0, outPath);
            Assert.False(result.Success);
            Assert.False(File.Exists(outPath));
        }

        [Fact]
        public void WriteRebuildReport_EmptyOutputPath_ReturnsFailure()
        {
            var result = RebuildCore.WriteRebuildReport(new byte[64], new byte[64], 0, "");
            Assert.False(result.Success);
        }
    }

    /// <summary>
    /// #1261 slice 1 — full Make/Apply round-trip on a SYNTHETIC fragmented ROM with a
    /// hand-built <see cref="Address"/> struct list (no real ROM, no WinForms keystone).
    /// Proves: pointers stay intact through relocation, the free run is reclaimed, a second
    /// Make on the rebuilt output is stable, and a forward (missing-then-resolved) pointer is
    /// back-patched correctly.
    /// </summary>
    [Collection("SharedState")]
    public class RebuildMakeApplyRoundTripTests
    {
        // Synthetic layout (all values little-endian):
        //   0x000..0x0FF  header (arbitrary, identical vanilla vs modified)
        //   0x200         pointer -> 0x08004000 (struct B)   [non-rebuild region]
        //   0x204         pointer -> 0x08008000 (struct C)   [non-rebuild region]
        //   0x1000        struct A  (0x20 bytes)  [rebuild region: addr == rebuildAddr, stays]
        //   0x1800        0x2000-byte 0x00 FREE RUN
        //   0x4000        struct B  (0x30 bytes)  [rebuild region, relocated/compacted]
        //   0x8000        struct C  (0x40 bytes)  [rebuild region, relocated/compacted]
        const uint HEADER_LEN = 0x100;
        const uint PTR_B_OFF = 0x200;
        const uint PTR_C_OFF = 0x204;
        const uint STRUCT_A_OFF = 0x1000;
        const uint STRUCT_A_LEN = 0x20;
        const uint FREE_RUN_OFF = 0x1800;
        const uint FREE_RUN_LEN = 0x2000;
        const uint STRUCT_B_OFF = 0x4000;
        const uint STRUCT_B_LEN = 0x30;
        const uint STRUCT_C_OFF = 0x8000;
        const uint STRUCT_C_LEN = 0x40;
        const uint ROM_LEN = 0x10000;
        const uint VANILLA_LEN = 0x1000;     // the non-rebuild base region
        const uint REBUILD_ADDR = 0x1000;    // offset; everything strictly above this relocates

        static void Fill(byte[] rom, uint off, uint len, byte seed)
        {
            for (uint i = 0; i < len; i++) rom[off + i] = (byte)(seed + i);
        }
        static void WriteU32(byte[] rom, uint off, uint val)
        {
            rom[off + 0] = (byte)(val & 0xFF);
            rom[off + 1] = (byte)((val >> 8) & 0xFF);
            rom[off + 2] = (byte)((val >> 16) & 0xFF);
            rom[off + 3] = (byte)((val >> 24) & 0xFF);
        }
        static uint ReadU32(byte[] rom, uint off)
        {
            return (uint)(rom[off] | (rom[off + 1] << 8) | (rom[off + 2] << 16) | (rom[off + 3] << 24));
        }

        // The modified (fragmented) ROM.
        static byte[] BuildModified()
        {
            byte[] rom = new byte[ROM_LEN];
            Fill(rom, 0, HEADER_LEN, 0x11);
            WriteU32(rom, PTR_B_OFF, 0x08000000 + STRUCT_B_OFF);
            WriteU32(rom, PTR_C_OFF, 0x08000000 + STRUCT_C_OFF);
            Fill(rom, STRUCT_A_OFF, STRUCT_A_LEN, 0xA0);
            // FREE_RUN stays all-zero
            Fill(rom, STRUCT_B_OFF, STRUCT_B_LEN, 0xB0);
            Fill(rom, STRUCT_C_OFF, STRUCT_C_LEN, 0xC0);
            return rom;
        }

        // The vanilla base: just the non-rebuild region (0..0x1000), zero elsewhere conceptually.
        // It only needs to cover the non-rebuild region; Apply seeds the rebuilt ROM from it.
        // The two pointer fields are LEFT NULL in vanilla — the mod is what points them at
        // structs B/C. This makes the pointer fields DIFFER from vanilla, so the emitter
        // records them as @-token MIX rows (not @DEF), which is what lets Apply back-patch
        // them to the relocated targets. (Identical-to-vanilla pointer fields would emit @DEF
        // and keep the stale literal address — the realistic case is always a repoint.)
        static ROM BuildVanilla()
        {
            byte[] data = new byte[VANILLA_LEN];
            Fill(data, 0, HEADER_LEN, 0x11);
            // PTR_B_OFF / PTR_C_OFF stay 0x00000000 (null) in vanilla.
            var rom = new ROM();
            rom.SwapNewROMDataDirect(data);
            return rom;
        }

        // Hand-built struct list marking the 3 structs + 2 pointer fields. No Forms — this IS
        // the parameter boundary the slice exists to prove.
        static List<Address> BuildStructList()
        {
            var list = new List<Address>();
            // Pointer fields (in the non-rebuild region) — DataType POINTER, length 4.
            list.Add(new Address(PTR_B_OFF, 4, U.NOT_FOUND, "PTR_B", Address.DataTypeEnum.POINTER));
            list.Add(new Address(PTR_C_OFF, 4, U.NOT_FOUND, "PTR_C", Address.DataTypeEnum.POINTER));
            // Struct A: stays at rebuildAddr (no pointer, fixed BIN).
            list.Add(new Address(STRUCT_A_OFF, STRUCT_A_LEN, U.NOT_FOUND, "STRUCT_A", Address.DataTypeEnum.BIN));
            // Structs B and C: reachable via the pointers; BIN data that must relocate.
            list.Add(new Address(STRUCT_B_OFF, STRUCT_B_LEN, 0x08000000 + PTR_B_OFF, "STRUCT_B", Address.DataTypeEnum.BIN));
            list.Add(new Address(STRUCT_C_OFF, STRUCT_C_LEN, 0x08000000 + PTR_C_OFF, "STRUCT_C", Address.DataTypeEnum.BIN));
            return list;
        }

        sealed class TempDir : IDisposable
        {
            public string Path { get; }
            public TempDir()
            {
                Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "feb_rebuild_rt_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(Path);
            }
            public void Dispose() { try { Directory.Delete(Path, true); } catch { } }
        }

        [Fact]
        public void RoundTrip_PointersIntact_FreeReclaimed_Idempotent()
        {
            byte[] modified = BuildModified();
            ROM vanilla = BuildVanilla();

            // The emitter's pointer-safety checks read CoreState.ROM (the modified ROM).
            // The Address ctor itself validates against CoreState.ROM, so set it FIRST,
            // then hand-build the struct list.
            var prevRom = CoreState.ROM;
            try
            {
                var modifiedRom = new ROM();
                modifiedRom.SwapNewROMDataDirect((byte[])modified.Clone());
                CoreState.ROM = modifiedRom;

                var structList = BuildStructList();

                using (var tmp = new TempDir())
                {
                    string manifestPath = Path.Combine(tmp.Path, "test.rebuild");

                    // (3) Make -> manifest with the expected @-token columns.
                    RebuildMakeCore.Make(modified, vanilla, REBUILD_ADDR, structList, manifestPath);
                    Assert.True(File.Exists(manifestPath));
                    string manifest = File.ReadAllText(manifestPath);

                    Assert.Contains("@_CRC32 ", manifest);
                    Assert.Contains("@_REBUILDADDRESS ", manifest);
                    Assert.Contains("@BIN ", manifest);     // structs A/B/C emit as fixed BIN
                    // The two pointer fields are MIX rows pointing at sidecar files; the
                    // 4-byte pointer column lives in the sidecar as an @-token (the manifest
                    // line only references the file). Verify both: the @MIX manifest line AND
                    // the @-token inside the sidecar (proving pointer columns emit).
                    Assert.Contains("@MIX ", manifest);
                    string allSidecars = ReadAllSidecars(Path.Combine(tmp.Path, "rebuild_mix"));
                    Assert.Contains("@" + U.ToHexString(0x08000000 + STRUCT_B_OFF), allSidecars);
                    Assert.Contains("@" + U.ToHexString(0x08000000 + STRUCT_C_OFF), allSidecars);

                    // (4) Apply -> rebuilt byte[].
                    var result = RebuildApplyCore.Apply(vanilla, manifestPath, 0x09000000);
                    Assert.True(result.Success, "Apply log:\n" + result.Log);
                    Assert.NotNull(result.Rebuilt);
                    byte[] rebuilt = result.Rebuilt;

                    // (5a) DATA INTACT: every pointer still resolves to bytes equal to the
                    // ORIGINAL struct content.
                    uint newB = U.toOffset(ReadU32(rebuilt, PTR_B_OFF));
                    uint newC = U.toOffset(ReadU32(rebuilt, PTR_C_OFF));
                    AssertBytesEqual(modified, STRUCT_B_OFF, rebuilt, newB, STRUCT_B_LEN);
                    AssertBytesEqual(modified, STRUCT_C_OFF, rebuilt, newC, STRUCT_C_LEN);
                    // Struct A stays at its address (== rebuildAddr) and keeps its bytes.
                    AssertBytesEqual(modified, STRUCT_A_OFF, rebuilt, STRUCT_A_OFF, STRUCT_A_LEN);

                    // (5b) FREE RECLAIMED: the 0x2000-byte gap + the 0x4000/0x8000 holes are
                    // gone — the rebuilt ROM is materially smaller than the fragmented one.
                    Assert.True(rebuilt.Length < modified.Length,
                        $"rebuilt {rebuilt.Length:X} should be < modified {modified.Length:X}");
                    // The relocated structs are compacted right after the rebuild address,
                    // so the old 0x8000 hole is no longer occupied by struct C.
                    Assert.True(newB < STRUCT_C_OFF && newC < STRUCT_C_OFF + STRUCT_C_LEN,
                        $"structs not compacted: B=0x{newB:X} C=0x{newC:X}");

                    // (5c) IDEMPOTENCE: a second Make on the rebuilt output, with an updated
                    // struct list, produces a stable manifest (same structural shape).
                    var rebuiltRom = new ROM();
                    rebuiltRom.SwapNewROMDataDirect((byte[])rebuilt.Clone());
                    CoreState.ROM = rebuiltRom;

                    var structList2 = new List<Address>
                    {
                        new Address(PTR_B_OFF, 4, U.NOT_FOUND, "PTR_B", Address.DataTypeEnum.POINTER),
                        new Address(PTR_C_OFF, 4, U.NOT_FOUND, "PTR_C", Address.DataTypeEnum.POINTER),
                        new Address(STRUCT_A_OFF, STRUCT_A_LEN, U.NOT_FOUND, "STRUCT_A", Address.DataTypeEnum.BIN),
                        new Address(newB, STRUCT_B_LEN, 0x08000000 + PTR_B_OFF, "STRUCT_B", Address.DataTypeEnum.BIN),
                        new Address(newC, STRUCT_C_LEN, 0x08000000 + PTR_C_OFF, "STRUCT_C", Address.DataTypeEnum.BIN),
                    };
                    string manifest2Path = Path.Combine(tmp.Path, "test2.rebuild");
                    // vanilla compares against the rebuilt ROM here; structs already compacted,
                    // so Apply of this manifest must reproduce a ROM of the same length.
                    RebuildMakeCore.Make(rebuilt, vanilla, REBUILD_ADDR, structList2, manifest2Path);
                    var result2 = RebuildApplyCore.Apply(vanilla, manifest2Path, 0x09000000);
                    Assert.True(result2.Success, "2nd Apply log:\n" + result2.Log);
                    Assert.Equal(rebuilt.Length, result2.Rebuilt.Length);
                    uint newB2 = U.toOffset(ReadU32(result2.Rebuilt, PTR_B_OFF));
                    uint newC2 = U.toOffset(ReadU32(result2.Rebuilt, PTR_C_OFF));
                    AssertBytesEqual(modified, STRUCT_B_OFF, result2.Rebuilt, newB2, STRUCT_B_LEN);
                    AssertBytesEqual(modified, STRUCT_C_OFF, result2.Rebuilt, newC2, STRUCT_C_LEN);
                }
            }
            finally
            {
                CoreState.ROM = prevRom;
            }
        }

        [Fact]
        public void Apply_ForwardPointer_BackPatchedCorrectly()
        {
            // A FORWARD (missing-then-resolved) pointer: struct P points at struct T, but the
            // manifest emits P's MIX row BEFORE T is resolved (so P registers a MissingPointer
            // that ResolvedPointer must back-patch when T lands). Hand-write the manifest so
            // the ordering is guaranteed, exercising MissingPointerList / ResolvedPointer.
            ROM vanilla = BuildVanilla();
            byte[] modified = BuildModified();

            var prevRom = CoreState.ROM;
            try
            {
                var modifiedRom = new ROM();
                modifiedRom.SwapNewROMDataDirect((byte[])modified.Clone());
                CoreState.ROM = modifiedRom;

                using (var tmp = new TempDir())
                {
                    // Sidecar: struct T (the target) as a BIN, struct P (a MIX whose 4-byte
                    // column is an @-token to T's GBA pointer).
                    Directory.CreateDirectory(Path.Combine(tmp.Path, "rebuild_bin"));
                    Directory.CreateDirectory(Path.Combine(tmp.Path, "rebuild_mix"));

                    byte[] targetBytes = new byte[STRUCT_C_LEN];
                    for (int i = 0; i < targetBytes.Length; i++) targetBytes[i] = (byte)(0xC0 + i);
                    File.WriteAllBytes(Path.Combine(tmp.Path, "rebuild_bin", "T.bin"), targetBytes);

                    // P's MIX content: a single @-token pointing at T's *original* address.
                    // Since T relocates, this @-token is unknown at the time P is applied
                    // (P is emitted first) -> forward pointer.
                    string pMix = "@" + U.ToHexString(0x08000000 + STRUCT_C_OFF);
                    File.WriteAllText(Path.Combine(tmp.Path, "rebuild_mix", "P.txt"), pMix);

                    // Manifest: P BEFORE T (forces the forward/back-patch path).
                    string manifest =
                        "@_CRC32 00000000 //x\r\n" +
                        "@_REBUILDADDRESS " + U.ToHexString(REBUILD_ADDR) + " //x\r\n" +
                        "@MIX " + U.ToHexString8(STRUCT_B_OFF) + " rebuild_mix/P.txt\r\n" +
                        "@BIN " + U.ToHexString8(STRUCT_C_OFF) + " rebuild_bin/T.bin\r\n";
                    string manifestPath = Path.Combine(tmp.Path, "fwd.rebuild");
                    File.WriteAllText(manifestPath, manifest);

                    var result = RebuildApplyCore.Apply(vanilla, manifestPath, 0x09000000);
                    Assert.True(result.Success, "forward-pointer Apply left unresolved pointers:\n" + result.Log);

                    // P relocated to newP; its first 4 bytes must now point at T's NEW location,
                    // and T's bytes there must equal targetBytes.
                    byte[] rebuilt = result.Rebuilt;
                    // Find P's new home: it's the relocated copy of STRUCT_B_OFF. The pointer
                    // P originally lived at STRUCT_B_OFF; after relocation read it back via the
                    // AddressMap-driven resolution. We locate P by scanning for the back-patched
                    // pointer value that resolves to T's content.
                    bool found = false;
                    for (uint off = REBUILD_ADDR; off + 4 <= rebuilt.Length; off += 4)
                    {
                        uint val = ReadU32(rebuilt, off);
                        if (val >= 0x08000000 && val < 0x0A000000)
                        {
                            uint tgt = U.toOffset(val);
                            if (tgt + STRUCT_C_LEN <= rebuilt.Length && BytesEqual(rebuilt, tgt, targetBytes))
                            {
                                found = true;
                                break;
                            }
                        }
                    }
                    Assert.True(found, "forward pointer was not back-patched to the relocated target:\n" + result.Log);
                }
            }
            finally
            {
                CoreState.ROM = prevRom;
            }
        }

        [Fact]
        public void Apply_ForwardAsmPointer_PreservesThumbBit_OnBackPatch()
        {
            // The ASM ±1 thumb-bit path: an &-token references an ODD (thumb) pointer to a
            // forward target. P is emitted before T, so at apply time AddressMap has neither
            // the odd pointer nor its even base -> a MissingPointer{Type=ASM} is registered.
            // When T resolves (even labelPointer), ResolvedPointer's ASM branch must rewrite
            // the field to (relocatedTarget + 1), preserving the thumb bit. Without that
            // logic the relocation would silently corrupt the ASM pointer.
            ROM vanilla = BuildVanilla();
            byte[] modified = BuildModified();

            var prevRom = CoreState.ROM;
            try
            {
                var modifiedRom = new ROM();
                modifiedRom.SwapNewROMDataDirect((byte[])modified.Clone());
                CoreState.ROM = modifiedRom;

                using (var tmp = new TempDir())
                {
                    Directory.CreateDirectory(Path.Combine(tmp.Path, "rebuild_bin"));
                    Directory.CreateDirectory(Path.Combine(tmp.Path, "rebuild_mix"));

                    byte[] targetBytes = new byte[STRUCT_C_LEN];
                    for (int i = 0; i < targetBytes.Length; i++) targetBytes[i] = (byte)(0xD0 + i);
                    File.WriteAllBytes(Path.Combine(tmp.Path, "rebuild_bin", "T.bin"), targetBytes);

                    // &-token to T's ORIGINAL address with the thumb bit set (odd).
                    string pMix = "&" + U.ToHexString(0x08000000 + STRUCT_C_OFF + 1);
                    File.WriteAllText(Path.Combine(tmp.Path, "rebuild_mix", "P.txt"), pMix);

                    string manifest =
                        "@_CRC32 00000000 //x\r\n" +
                        "@_REBUILDADDRESS " + U.ToHexString(REBUILD_ADDR) + " //x\r\n" +
                        "@MIX " + U.ToHexString8(STRUCT_B_OFF) + " rebuild_mix/P.txt\r\n" +
                        "@BIN " + U.ToHexString8(STRUCT_C_OFF) + " rebuild_bin/T.bin\r\n";
                    string manifestPath = Path.Combine(tmp.Path, "fwdasm.rebuild");
                    File.WriteAllText(manifestPath, manifest);

                    var result = RebuildApplyCore.Apply(vanilla, manifestPath, 0x09000000);
                    Assert.True(result.Success, "forward ASM-pointer Apply left unresolved pointers:\n" + result.Log);

                    // Scan for an ODD GBA pointer whose (value-1) target holds targetBytes —
                    // i.e. the thumb bit survived the back-patch.
                    byte[] rebuilt = result.Rebuilt;
                    bool found = false;
                    for (uint off = REBUILD_ADDR; off + 4 <= rebuilt.Length; off += 4)
                    {
                        uint val = ReadU32(rebuilt, off);
                        if (val >= 0x08000001 && val < 0x0A000000 && U.IsValueOdd(val))
                        {
                            uint tgt = U.toOffset(val - 1);
                            if (tgt + STRUCT_C_LEN <= rebuilt.Length && BytesEqual(rebuilt, tgt, targetBytes))
                            {
                                found = true;
                                break;
                            }
                        }
                    }
                    Assert.True(found, "forward ASM pointer lost its thumb bit or was not back-patched:\n" + result.Log);
                }
            }
            finally
            {
                CoreState.ROM = prevRom;
            }
        }

        static string ReadAllSidecars(string dir)
        {
            var sb = new System.Text.StringBuilder();
            if (!Directory.Exists(dir)) return "";
            foreach (string f in Directory.GetFiles(dir))
            {
                sb.AppendLine(File.ReadAllText(f));
            }
            return sb.ToString();
        }
        static bool BytesEqual(byte[] rom, uint off, byte[] expected)
        {
            for (uint i = 0; i < expected.Length; i++)
            {
                if (rom[off + i] != expected[i]) return false;
            }
            return true;
        }
        static void AssertBytesEqual(byte[] expectedRom, uint expectedOff, byte[] actualRom, uint actualOff, uint len)
        {
            for (uint i = 0; i < len; i++)
            {
                Assert.True(expectedRom[expectedOff + i] == actualRom[actualOff + i],
                    $"byte mismatch at +0x{i:X}: expected 0x{expectedRom[expectedOff + i]:X2} got 0x{actualRom[actualOff + i]:X2} (actualOff=0x{actualOff:X})");
            }
        }
    }
}
