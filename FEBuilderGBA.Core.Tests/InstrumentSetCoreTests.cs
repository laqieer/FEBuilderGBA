using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;
using Xunit.Abstractions;

namespace FEBuilderGBA.Core.Tests
{
    /// <summary>
    /// Tests for <see cref="InstrumentSetCore"/> — the cross-platform port of
    /// WinForms <c>PatchUtil.SearchInstrumentSet</c> used by the Avalonia
    /// SongTrack instrument-set browser. (#787)
    ///
    /// Important: the NIMAP / NIMAP2 / AllInstrument byte signatures in
    /// <c>config/data/song_instrumentset_ALL.txt</c> only appear in a ROM that
    /// has had the "NativeInstrumentMap" / "AllInstrument" patch installed and
    /// relocated ABOVE <c>compress_image_borderline_address</c> (the grep starts
    /// at that address). A vanilla <c>roms/FE8U.gba</c> therefore yields ONLY the
    /// "Current" seed — verified by <see cref="SearchInstrumentSet_VanillaFE8U_ReturnsSeedOnly"/>.
    /// The full discovery / NIMAP2-supersedes-NIMAP / AllInstrument-deref logic
    /// is exercised against a real FE8U ROM with the signatures injected above
    /// the borderline by <see cref="SearchInstrumentSet_InjectedSignatures_DiscoversSetsAndAppliesRules"/>.
    /// Real-ROM tests skip gracefully when FE8U.gba is unavailable.
    /// </summary>
    [Collection("SharedState")]
    public class InstrumentSetCoreTests
    {
        private readonly ITestOutputHelper _output;

        public InstrumentSetCoreTests(ITestOutputHelper output)
        {
            _output = output;
        }

        // Walk up from the test assembly looking for the repo root (a dir with
        // config/data). When running from an isolated git worktree the worktree
        // has config/data (git-tracked) but no roms/ (gitignored) — so we keep
        // walking and prefer the first ancestor that ALSO has roms/, falling
        // back to the nearest config/data root for config-file resolution.
        static string FindRepoRoot()
        {
            string dir = AppContext.BaseDirectory;
            string firstConfigRoot = null;
            for (int i = 0; i < 12; i++)
            {
                if (Directory.Exists(Path.Combine(dir, "config", "data")))
                {
                    firstConfigRoot ??= dir;
                    if (Directory.Exists(Path.Combine(dir, "roms")))
                        return dir;
                }
                string parent = Path.GetDirectoryName(dir);
                if (parent == null) break;
                dir = parent;
            }
            return firstConfigRoot;
        }

        // Resolve a ROM file. config-data files come from FindRepoRoot()'s
        // config dir; the ROM itself may live in a parent (main) checkout's
        // roms/ when running from a worktree.
        static string FindRom(string romName)
        {
            string dir = AppContext.BaseDirectory;
            for (int i = 0; i < 12; i++)
            {
                string path = Path.Combine(dir, "roms", romName);
                if (File.Exists(path)) return path;
                string parent = Path.GetDirectoryName(dir);
                if (parent == null) break;
                dir = parent;
            }
            return null;
        }

        // FE8U signature rows copied verbatim from config/data/song_instrumentset_ALL.txt.
        static byte[] Sig(string hex) => hex.Split(' ').Select(x => (byte)U.atoh(x)).ToArray();

        static readonly byte[] FE8U_NIMAP2 = Sig("00 3C 00 00 B8 2A 51 08 FF FA 00 CC 00 3C 00 00 68 80 2A 08 FF FA 00 CC 00 3C 00 00 8C 91 29 08 FF F9 00 A5 01 3C 00 00 02 00 00 00 00 00 0F 00 00 3C 00 00 F4 B7 2B 08 FF FD 00 CC 01 3C 00 00 02 00 00 00 00 00 0F 00 00 3C 00 00 24 F5 28 08 FF F9 00 A5 00 3C 00 00 24 F5 28 08 FF F5 96 96");
        static readonly byte[] FE8U_NIMAP = Sig("00 3C 00 00 B8 2A 51 08 FF FA 00 CC 00 3C 00 00 68 80 2A 08 FF FA 00 CC 00 3C 00 00 8C 91 29 08 FF F9 00 A5 01 3C 00 00 02 00 00 00 00 00 0F 00 00 3C 00 00 F4 B7 2B 08 FF FD 00 CC 01 3C 00 00");
        static readonly byte[] ALL_INSTRUMENT = Sig("00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 01 01 01 02 02 03 03 04 05 05 05 05 06 06 06 07 07 07 07 08 08 09 09 09 0A 0A 0A 0A 0B 0C 0C 0C 0D 0D 0D 0D 0E 0E 0E 0F 0F 0F 0F 10 10 10 11 11 11 11 12 12 12 13 13 13 13 14 14 15 15 15 16 16 16 16 17 17 17 17 17 17 17 17 17 17 17 17 17 17 17 17 17 17 17 17 17 17 17");

        [Fact]
        public void SearchInstrumentSet_NullRom_ReturnsEmpty()
        {
            var result = InstrumentSetCore.SearchInstrumentSet(null, 0x12345678);
            Assert.Empty(result);
        }

        /// <summary>
        /// On a clean (unpatched) FE8U ROM there is no NativeInstrumentMap /
        /// AllInstrument table above the compressed-image borderline, so the
        /// only entry returned is the "Current" seed. This matches the WinForms
        /// behaviour byte-for-byte.
        /// </summary>
        [Fact]
        public void SearchInstrumentSet_VanillaFE8U_ReturnsSeedOnly()
        {
            string romPath = FindRom("FE8U.gba");
            if (romPath == null)
            {
                _output.WriteLine("SKIP: FE8U.gba not found");
                return;
            }

            var savedRom = CoreState.ROM;
            var savedBase = CoreState.BaseDirectory;
            try
            {
                CoreState.BaseDirectory = FindRepoRoot();
                var rom = new ROM();
                rom.Load(romPath, out _);
                CoreState.ROM = rom;
                InstrumentSetCore.ClearCache();

                uint seed = 0x08123456;
                List<AddrResult> iset = InstrumentSetCore.SearchInstrumentSet(rom, seed);
                foreach (var e in iset) _output.WriteLine($"0x{e.addr:X08}  {e.name}");

                Assert.Single(iset);
                Assert.Equal(seed, iset[0].addr);
                Assert.Equal(U.ToHexString(seed) + "=Current", iset[0].name);
            }
            finally
            {
                CoreState.ROM = savedRom;
                CoreState.BaseDirectory = savedBase;
                InstrumentSetCore.ClearCache();
            }
        }

        /// <summary>
        /// Inject the FE8U NIMAP2, NIMAP and AllInstrument signatures above the
        /// borderline of a real FE8U ROM and verify the FAITHFUL WinForms
        /// behaviour:
        ///  - the "Current" seed is row 0,
        ///  - the NatveInstrumentMap2(NIMAP2) signature is discovered,
        ///  - the NatveInstrumentMap(NIMAP) signature is ALSO discovered. (The
        ///    WinForms NIMAP2-supersedes-NIMAP branch keys on sp[0]==
        ///    "NatveInstrumentMap2"/"NatveInstrumentMap", but the config's first
        ///    column is actually "NatveInstrumentMap2(NIMAP2)" /
        ///    "NatveInstrumentMap(NIMAP)" — so that branch is dormant in
        ///    WinForms too. The Core port reproduces this exactly.)
        ///  - the AllInstrument row IS dereferenced (-8) because its config first
        ///    column is the bare "AllInstrument", which the branch matches,
        ///  - every discovered entry is a "0x........=<name>" GBA pointer.
        /// </summary>
        [Fact]
        public void SearchInstrumentSet_InjectedSignatures_DiscoversSetsAndAppliesRules()
        {
            string romPath = FindRom("FE8U.gba");
            if (romPath == null)
            {
                _output.WriteLine("SKIP: FE8U.gba not found");
                return;
            }

            var savedRom = CoreState.ROM;
            var savedBase = CoreState.BaseDirectory;
            try
            {
                CoreState.BaseDirectory = FindRepoRoot();
                var rom = new ROM();
                rom.Load(romPath, out _);

                // All offsets are 4-aligned and above compress_image_borderline_address
                // (0xDB000) so U.Grep (start = borderline, blocksize 4) finds them.
                uint nimap2Off = 0x00200000;
                uint nimapOff = 0x00280000;
                uint allOff = 0x00300000;
                uint allPtrOff = 0x00380000; // holds a pointer to allOff for the deref
                Assert.True(nimap2Off >= rom.RomInfo.compress_image_borderline_address);

                Inject(rom, nimap2Off, FE8U_NIMAP2);
                Inject(rom, nimapOff, FE8U_NIMAP);
                Inject(rom, allOff, ALL_INSTRUMENT);
                rom.write_u32(allPtrOff, U.toPointer(allOff)); // 0x08300000

                CoreState.ROM = rom;
                InstrumentSetCore.ClearCache();

                uint seed = 0x08999999;
                List<AddrResult> iset = InstrumentSetCore.SearchInstrumentSet(rom, seed);
                foreach (var e in iset) _output.WriteLine($"0x{e.addr:X08}  {e.name}");

                var names = iset.Select(e => e.name).ToList();

                // Seed row.
                Assert.Equal(seed, iset[0].addr);
                Assert.Equal(U.ToHexString(seed) + "=Current", iset[0].name);

                // NIMAP2 discovered at the injected offset.
                Assert.Contains(iset, e => e.name == U.ToHexString(U.toPointer(nimap2Off)) + "=NatveInstrumentMap2(NIMAP2)");

                // NIMAP ALSO discovered (the supersede branch is dormant in WF —
                // see the doc-comment above). The NIMAP signature is a prefix of
                // the NIMAP2 signature, so it matches at the same offset.
                Assert.Contains(names, n => n.EndsWith("=NatveInstrumentMap(NIMAP)"));

                // AllInstrument discovered via pointer deref: result = pointerOffset - 8.
                Assert.Contains(iset, e => e.name.EndsWith("=AllInstrument"));
                Assert.Contains(iset, e => e.addr == U.toPointer(allPtrOff - 8) && e.name.EndsWith("=AllInstrument"));

                // Every non-seed entry is a "0x........=<name>" GBA pointer.
                for (int i = 1; i < iset.Count; i++)
                {
                    Assert.True(iset[i].addr >= 0x08000000u, $"entry {i} ({iset[i].name}) should be a GBA pointer");
                    Assert.StartsWith(U.ToHexString(iset[i].addr) + "=", iset[i].name);
                }
            }
            finally
            {
                CoreState.ROM = savedRom;
                CoreState.BaseDirectory = savedBase;
                InstrumentSetCore.ClearCache();
            }
        }

        /// <summary>
        /// Result is memoised per (ROM, currentData); a second call with the same
        /// arguments returns the same instance, ClearCache and a different
        /// currentData each force a rebuild. Uses the vanilla ROM (seed-only) —
        /// caching does not depend on the discovered set.
        /// </summary>
        [Fact]
        public void SearchInstrumentSet_FE8U_IsMemoised()
        {
            string romPath = FindRom("FE8U.gba");
            if (romPath == null)
            {
                _output.WriteLine("SKIP: FE8U.gba not found");
                return;
            }

            var savedRom = CoreState.ROM;
            var savedBase = CoreState.BaseDirectory;
            try
            {
                CoreState.BaseDirectory = FindRepoRoot();
                var rom = new ROM();
                rom.Load(romPath, out _);
                CoreState.ROM = rom;
                InstrumentSetCore.ClearCache();

                var a = InstrumentSetCore.SearchInstrumentSet(rom, 0);
                var b = InstrumentSetCore.SearchInstrumentSet(rom, 0);
                Assert.Same(a, b);

                var c = InstrumentSetCore.SearchInstrumentSet(rom, 0x99);
                Assert.NotSame(a, c);

                InstrumentSetCore.ClearCache();
                var d = InstrumentSetCore.SearchInstrumentSet(rom, 0);
                Assert.NotSame(a, d);
            }
            finally
            {
                CoreState.ROM = savedRom;
                CoreState.BaseDirectory = savedBase;
                InstrumentSetCore.ClearCache();
            }
        }

        static void Inject(ROM rom, uint off, byte[] sig)
        {
            for (uint i = 0; i < sig.Length; i++)
                rom.write_u8(off + i, sig[i]);
        }
    }
}
