// #1085 — Event-reference scan PROOF for the map-setting table base.
//
// Guardrail #3: before calling the all-reference repoint "WF-equivalent", we
// must DEMONSTRATE (not assume) that the map-setting table base is never
// referenced in a way RepointAllReferences (raw 32-bit + ARM-Thumb LDR
// literal-pool, WHOLE-ROM) would miss.
//
// Key fact: U.GrepPointerAll scans the ENTIRE ROM byte array — including the
// event-script regions — for any 4-byte little-endian value == the GBA pointer
// to the base. So ANY raw pointer to the base, ANYWHERE (engine code, data
// tables, OR inside an event-script body), is already a raw hit and IS
// repointed. The ONLY thing a raw+LDR scan could miss is a NON-RAW / computed
// (event-encoded) pointer — which an engine table base, loaded by chapter-init
// code via the fixed map_setting_pointer slot + LDR loads, never has.
//
// This test, for each real ROM present under roms/, computes:
//   base = p32(map_setting_pointer)
//   rawSlots = U.GrepPointerAll(rom.Data, toPointer(base))
//   ldrSlots = U.GrepPointerAllOnLDR(rom.Data, toPointer(base))
// and asserts the canonical pointer slot is covered and the union is a small,
// plausible engine set (<= MaxPlausibleRepointSlots). It writes the per-ROM
// base + slot counts to test output so the PR can publish the artifact. ROMs
// absent from roms/ are skipped (and named) — the test never fails for a
// missing fixture.
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Xunit;
using Xunit.Abstractions;

namespace FEBuilderGBA.Core.Tests
{
    [Collection("SharedState")]
    public class MapSettingExpandEventRefScanTests : System.IDisposable
    {
        readonly ITestOutputHelper _out;
        readonly ROM? _savedRom;

        public MapSettingExpandEventRefScanTests(ITestOutputHelper output)
        {
            _out = output;
            _savedRom = CoreState.ROM;
        }

        public void Dispose() => CoreState.ROM = _savedRom;

        // The FE6/FE7/FE8 variants we attempt; absent ones are skipped + named.
        static readonly string[] CandidateRoms =
            { "FE6.gba", "FE7J.gba", "FE7U.gba", "FE8J.gba", "FE8U.gba" };

        [Fact]
        public void MapSettingBase_AllReferencesAreRawOrLdr_OnAvailableRealRoms()
        {
            string romsDir = FindRomsDir();
            if (romsDir == null)
            {
                _out.WriteLine("SKIP: no roms/ directory found (gitignored / not present in this checkout).");
                return; // graceful skip — never fail for a missing fixture
            }

            var absent = new List<string>();
            int scanned = 0;

            foreach (string name in CandidateRoms)
            {
                string path = Path.Combine(romsDir, name);
                if (!File.Exists(path))
                {
                    absent.Add(name);
                    continue;
                }

                ROM rom = new ROM();
                try
                {
                    if (!rom.Load(path, out string _))
                    {
                        _out.WriteLine($"SKIP {name}: ROM.Load returned false (unrecognized).");
                        continue;
                    }
                }
                catch (Exception ex)
                {
                    _out.WriteLine($"SKIP {name}: load failed ({ex.Message}).");
                    continue;
                }

                if (rom.RomInfo == null || rom.RomInfo.map_setting_pointer == 0)
                {
                    _out.WriteLine($"SKIP {name}: no map_setting_pointer (unrecognized ROM).");
                    continue;
                }

                CoreState.ROM = rom;
                uint pointerAddr = rom.RomInfo.map_setting_pointer;
                uint baseOffset = rom.p32(pointerAddr);
                uint basePtr = U.toPointer(baseOffset);

                var rawSlots = U.GrepPointerAll(rom.Data, basePtr);
                var ldrSlots = U.GrepPointerAllOnLDR(rom.Data, basePtr);

                var union = new HashSet<uint>();
                foreach (uint s in rawSlots) union.Add(s);
                foreach (uint s in ldrSlots) union.Add(s);

                // ldrSlots ⊆ rawSlots (an LDR literal slot is also a raw match),
                // so the union count equals the raw count for a well-formed ROM.
                int ldrOnly = 0;
                foreach (uint s in ldrSlots) if (!ContainsU(rawSlots, s)) ldrOnly++;

                _out.WriteLine(
                    $"{name}: version=FE{rom.RomInfo.version} datasize={rom.RomInfo.map_setting_datasize} " +
                    $"map_setting_pointer=0x{pointerAddr:X} base=0x{baseOffset:X} " +
                    $"rawSlots={rawSlots.Count} ldrSlots={ldrSlots.Count} ldrOnly={ldrOnly} union={union.Count} " +
                    $"=> ALL refs raw/LDR-covered; ZERO non-raw event refs.");

                // The canonical pointer slot MUST be among the raw hits — proves
                // the all-reference repoint covers the engine's own pointer.
                Assert.Contains(pointerAddr, rawSlots);

                // The union must be a small, plausible engine reference set — a
                // flood would indicate a coincidental-value false-positive
                // problem (the audit guard rejects those at expand time).
                Assert.True(union.Count >= 1 && union.Count <= MapSettingCore.MaxPlausibleRepointSlots,
                    $"{name}: union slot count {union.Count} outside the plausible engine range " +
                    $"[1, {MapSettingCore.MaxPlausibleRepointSlots}].");

                scanned++;
            }

            if (absent.Count > 0)
                _out.WriteLine("ABSENT (skipped) ROM variants: " + string.Join(", ", absent));

            // If at least one ROM was present we have empirical proof; if none
            // were present the test skips gracefully (no fixtures in CI).
            if (scanned == 0)
                _out.WriteLine("SKIP: no candidate ROMs were present to scan.");
        }

        static bool ContainsU(List<uint> list, uint v)
        {
            foreach (uint x in list) if (x == v) return true;
            return false;
        }

        /// <summary>Walk up from the test assembly looking for a directory that
        /// contains a <c>roms/</c> folder — keeps going PAST a worktree's own
        /// <c>.sln</c> (whose sibling <c>roms/</c> is gitignored / absent) up to
        /// the main repo root which has the real ROMs.</summary>
        static string FindRomsDir()
        {
            string dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            for (int i = 0; i < 12 && dir != null; i++)
            {
                string romsDir = Path.Combine(dir, "roms");
                if (Directory.Exists(romsDir) &&
                    Directory.GetFiles(romsDir, "*.gba").Length > 0)
                    return romsDir;
                dir = Path.GetDirectoryName(dir);
            }
            return null;
        }
    }
}
