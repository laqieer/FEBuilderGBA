// SPDX-License-Identifier: GPL-3.0-or-later
// Tests for EventScriptReferenceScanner (#990) — the generic READ-ONLY
// event-script cross-reference scanner that backs BGReferenceFinder.
//
// Two layers:
//   1. SYNTHETIC scratch-ROM scanner tests — build a minimal EventScript with a
//      BG-typed arg command (+ POINTER_EVENT for recursion), plant the bytes in
//      a scratch region of a loaded ROM, set it as CoreState.EventScript, and
//      assert ScanScriptForArg (exposed internal via InternalsVisibleTo) finds
//      the planted id. Covers BG match, POINTER_EVENT recursion, self-cycle
//      (no hang), bgId 0 kept, keepZeroId:false drops 0, and the 0x7FFF skip.
//   2. REAL-ROM enumeration / find tests — load FE6/FE7U/FE8U (skipped when
//      absent), wire CoreState.EventScript from the shipped config, and assert
//      FindAllArgReferences(BG) is non-empty (non-stub proof) and the FE7U
//      enumeration includes a "Tutorial FE7" entry.
//
// NOTE: exhaustive per-cond-type synthetic ROM construction (Turn short-turn,
// Talk, Object shop/chest skip, Always, Tutorial, Start/End geometry) is
// approximated by the real-ROM enumeration tests — vanilla ROMs contain every
// cond type — because hand-building a full multi-map cond table is far more
// fragile than the disasm-level synthetic tests above.

using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Xunit;
using FEBuilderGBA;

namespace FEBuilderGBA.Core.Tests
{
    [Collection("SharedState")]
    public class EventScriptReferenceScannerTests
    {
        // =================================================================
        // Synthetic scratch-ROM scanner tests.
        // =================================================================

        // Scratch region inside the 16MB synthetic ROM where we plant events.
        const uint Scratch = 0x00100000u;
        const int RomSize = 0x1000000; // 16MB — FE8 LoadLow minimum.

        static EventScript BuildEventScript(params EventScript.Script[] scripts)
        {
            var es = new EventScript();
            var prop = typeof(EventScript).GetProperty("Scripts");
            prop!.SetValue(es, scripts);
            return es;
        }

        // A command "01 00 XXXX" — opcode 0x0001 then a 2-byte BG arg at byte 2.
        static EventScript.Script BgCommand()
            => EventScript.ParseScriptLine("0100XXXX\tSETBG [XXXX:BG:Background]");

        // A terminator command "0A 00 00 00".
        static EventScript.Script TermCommand()
            => EventScript.ParseScriptLine("0A000000\tENDA [TERM]");

        // A CALL command "03 00 00 00 XXXXXXXX" — a 4-byte POINTER_EVENT arg.
        static EventScript.Script CallCommand()
            => EventScript.ParseScriptLine("03000000XXXXXXXX\tCALL [X:POINTER_EVENT:Target]");

        /// <summary>
        /// Run an action with CoreState wired to a fresh 16MB synthetic ROM +
        /// the given EventScript, restoring prior CoreState afterwards.
        /// </summary>
        static void WithSyntheticEnv(EventScript es, System.Action<ROM> body)
        {
            var prevRom = CoreState.ROM;
            var prevEs = CoreState.EventScript;
            var prevComment = CoreState.CommentCache;
            try
            {
                var rom = new ROM();
                rom.LoadLow("scratch-fe8u.gba", new byte[RomSize], "BE8E01");
                CoreState.ROM = rom;
                CoreState.EventScript = es;
                // DisAseemble dereferences CoreState.CommentCache — always wire it.
                CoreState.CommentCache = new HeadlessEtcCache();
                body(rom);
            }
            finally
            {
                CoreState.ROM = prevRom;
                CoreState.EventScript = prevEs;
                CoreState.CommentCache = prevComment;
            }
        }

        [Fact]
        public void ScanScriptForArg_FindsPlantedBG()
        {
            var es = BuildEventScript(BgCommand(), TermCommand());
            WithSyntheticEnv(es, rom =>
            {
                // SETBG bgId=0x0042 ; ENDA
                rom.write_u16(Scratch + 0, 0x0001);
                rom.write_u16(Scratch + 2, 0x0042);
                rom.write_u32(Scratch + 4, 0x0000000A);

                var bucket = new Dictionary<uint, List<AddrResult>>();
                var trace = new List<uint>();
                EventScriptReferenceScanner.ScanScriptForArg(
                    rom, es, Scratch, "MAP 0 test",
                    EventScript.ArgType.BG, keepZeroId: true, trace, bucket);

                Assert.True(bucket.ContainsKey(0x42), "BG id 0x42 must be found");
                Assert.Single(bucket[0x42]);
                Assert.Equal(Scratch, bucket[0x42][0].addr);
            });
        }

        [Fact]
        public void ScanScriptForArg_RecursesThroughPointerEvent()
        {
            var es = BuildEventScript(CallCommand(), BgCommand(), TermCommand());
            WithSyntheticEnv(es, rom =>
            {
                // Nested event at Scratch+0x100: SETBG 0x0077 ; ENDA
                uint nested = Scratch + 0x100;
                rom.write_u16(nested + 0, 0x0001);
                rom.write_u16(nested + 2, 0x0077);
                rom.write_u32(nested + 4, 0x0000000A);

                // Root event: CALL nested ; ENDA
                rom.write_u32(Scratch + 0, 0x00000003);
                rom.write_u32(Scratch + 4, U.toPointer(nested));
                rom.write_u32(Scratch + 8, 0x0000000A);

                var bucket = new Dictionary<uint, List<AddrResult>>();
                var trace = new List<uint>();
                EventScriptReferenceScanner.ScanScriptForArg(
                    rom, es, Scratch, "MAP 0 root",
                    EventScript.ArgType.BG, keepZeroId: true, trace, bucket);

                Assert.True(bucket.ContainsKey(0x77), "nested BG id 0x77 must be found via POINTER_EVENT recursion");
                // The reference's event-start addr is the NESTED event start.
                Assert.Equal(nested, bucket[0x77][0].addr);
            });
        }

        [Fact]
        public void ScanScriptForArg_SelfCyclePointerEvent_DoesNotHang()
        {
            var es = BuildEventScript(CallCommand(), TermCommand());
            WithSyntheticEnv(es, rom =>
            {
                // CALL self ; (no real terminator reached on first command, but
                // the tracelist guard must prevent infinite recursion).
                rom.write_u32(Scratch + 0, 0x00000003);
                rom.write_u32(Scratch + 4, U.toPointer(Scratch)); // points to itself
                rom.write_u32(Scratch + 8, 0x0000000A);

                var bucket = new Dictionary<uint, List<AddrResult>>();
                var trace = new List<uint>();
                // Must terminate (the test framework would hang otherwise).
                EventScriptReferenceScanner.ScanScriptForArg(
                    rom, es, Scratch, "MAP 0 cycle",
                    EventScript.ArgType.BG, keepZeroId: true, trace, bucket);

                Assert.Empty(bucket); // no BG arg present
            });
        }

        [Fact]
        public void ScanScriptForArg_BgIdZero_KeptWhenKeepZeroId()
        {
            var es = BuildEventScript(BgCommand(), TermCommand());
            WithSyntheticEnv(es, rom =>
            {
                rom.write_u16(Scratch + 0, 0x0001);
                rom.write_u16(Scratch + 2, 0x0000); // bgId 0
                rom.write_u32(Scratch + 4, 0x0000000A);

                var bucket = new Dictionary<uint, List<AddrResult>>();
                var trace = new List<uint>();
                EventScriptReferenceScanner.ScanScriptForArg(
                    rom, es, Scratch, "MAP 0 zero",
                    EventScript.ArgType.BG, keepZeroId: true, trace, bucket);

                Assert.True(bucket.ContainsKey(0), "BG id 0 must be kept when keepZeroId:true");
            });
        }

        [Fact]
        public void ScanScriptForArg_BgIdZero_DroppedWhenNotKeepZeroId()
        {
            var es = BuildEventScript(BgCommand(), TermCommand());
            WithSyntheticEnv(es, rom =>
            {
                rom.write_u16(Scratch + 0, 0x0001);
                rom.write_u16(Scratch + 2, 0x0000); // bgId 0
                rom.write_u32(Scratch + 4, 0x0000000A);

                var bucket = new Dictionary<uint, List<AddrResult>>();
                var trace = new List<uint>();
                EventScriptReferenceScanner.ScanScriptForArg(
                    rom, es, Scratch, "MAP 0 zero",
                    EventScript.ArgType.BG, keepZeroId: false, trace, bucket);

                Assert.False(bucket.ContainsKey(0), "BG id 0 must be dropped when keepZeroId:false");
            });
        }

        [Fact]
        public void ScanScriptForArg_HighId_Skipped()
        {
            var es = BuildEventScript(BgCommand(), TermCommand());
            WithSyntheticEnv(es, rom =>
            {
                rom.write_u16(Scratch + 0, 0x0001);
                rom.write_u16(Scratch + 2, 0x7FFF); // >= 0x7FFF, must be skipped
                rom.write_u32(Scratch + 4, 0x0000000A);

                var bucket = new Dictionary<uint, List<AddrResult>>();
                var trace = new List<uint>();
                EventScriptReferenceScanner.ScanScriptForArg(
                    rom, es, Scratch, "MAP 0 high",
                    EventScript.ArgType.BG, keepZeroId: true, trace, bucket);

                Assert.Empty(bucket);
            });
        }

        [Fact]
        public void FindAllArgReferences_Gating_NullEventScript_ReturnsEmpty()
        {
            var prevRom = CoreState.ROM;
            var prevEs = CoreState.EventScript;
            try
            {
                var rom = new ROM();
                rom.LoadLow("gate-fe8u.gba", new byte[0x1000000], "BE8E01");
                CoreState.ROM = rom;
                CoreState.EventScript = null;

                var map = EventScriptReferenceScanner.FindAllArgReferences(
                    rom, EventScript.ArgType.BG, keepZeroId: true);
                Assert.Empty(map);
            }
            finally
            {
                CoreState.ROM = prevRom;
                CoreState.EventScript = prevEs;
            }
        }

        [Fact]
        public void FindAllArgReferences_Gating_NonActiveRom_ReturnsEmpty()
        {
            var es = BuildEventScript(BgCommand(), TermCommand());
            var prevRom = CoreState.ROM;
            var prevEs = CoreState.EventScript;
            try
            {
                var active = new ROM();
                active.LoadLow("active-fe8u.gba", new byte[0x1000000], "BE8E01");
                var other = new ROM();
                other.LoadLow("other-fe8u.gba", new byte[0x1000000], "BE8E01");

                CoreState.ROM = active;
                CoreState.EventScript = es;

                // Passing a ROM that is NOT the active CoreState.ROM must gate out.
                var map = EventScriptReferenceScanner.FindAllArgReferences(
                    other, EventScript.ArgType.BG, keepZeroId: true);
                Assert.Empty(map);
            }
            finally
            {
                CoreState.ROM = prevRom;
                CoreState.EventScript = prevEs;
            }
        }

        [Fact]
        public void FindAllArgReferences_Gating_NullCommentCache_ReturnsEmpty()
        {
            // #992 Copilot review #3: es.DisAseemble dereferences
            // CoreState.CommentCache, so a null CommentCache must gate out (no
            // NullRef) even when ROM + EventScript are wired.
            var es = BuildEventScript(BgCommand(), TermCommand());
            var prevRom = CoreState.ROM;
            var prevEs = CoreState.EventScript;
            var prevComment = CoreState.CommentCache;
            try
            {
                var rom = new ROM();
                rom.LoadLow("nocomment-fe8u.gba", new byte[0x1000000], "BE8E01");
                CoreState.ROM = rom;
                CoreState.EventScript = es;
                CoreState.CommentCache = null;

                var map = EventScriptReferenceScanner.FindAllArgReferences(
                    rom, EventScript.ArgType.BG, keepZeroId: true);
                Assert.Empty(map);
            }
            finally
            {
                CoreState.ROM = prevRom;
                CoreState.EventScript = prevEs;
                CoreState.CommentCache = prevComment;
            }
        }

        [Fact]
        public void EnumerateEventEntries_NullRom_ReturnsEmpty()
        {
            var list = EventScriptReferenceScanner.EnumerateEventEntries(null);
            Assert.NotNull(list);
            Assert.Empty(list);
        }

        /// <summary>
        /// #992 Copilot review #2: a TURN cond record sitting at the very end of
        /// ROM must NOT throw (U.u32/u8 throw past EOF). WalkTurn now bounds-checks
        /// the current record before reading. We plant a map whose TURN cond-slot
        /// points at an address near EOF and assert EnumerateEventEntries (which
        /// drives WalkTurn) never throws.
        /// </summary>
        [Fact]
        public void EnumerateEventEntries_TurnRecordNearEOF_DoesNotThrow()
        {
            var prevRom = CoreState.ROM;
            try
            {
                var rom = new ROM();
                rom.LoadLow("eof-fe8u.gba", new byte[0x1000000], "BE8E01");
                CoreState.ROM = rom;

                uint romLen = (uint)rom.Data.Length;

                // 1) Plant a valid map setting (id 0) with event_plist = 1.
                uint mapTableBase = 0x00700000u;
                uint dataSize = rom.RomInfo.map_setting_datasize;
                WriteU32(rom, rom.RomInfo.map_setting_pointer, mapTableBase | 0x08000000u);
                WriteU32(rom, mapTableBase + 0, 0x08123456u); // first dword = pointer → valid
                rom.Data[mapTableBase + rom.RomInfo.map_setting_event_plist_pos] = 1;
                // Terminator row: next record's first dword = 0 (non-pointer).
                WriteU32(rom, mapTableBase + dataSize, 0x00000000u);

                // 2) map_event_pointer table: entry[1] → cond block.
                uint eventTableBase = 0x00710000u;
                WriteU32(rom, rom.RomInfo.map_event_pointer, eventTableBase | 0x08000000u);
                uint condBlock = 0x00720000u;
                WriteU32(rom, eventTableBase + 1 * 4, condBlock | 0x08000000u);

                // 3) Cond block slot 0 (Turn) → a turn record placed 2 bytes
                //    before EOF, so the unguarded u32(addr) read would throw.
                uint turnRec = romLen - 2;
                WriteU32(rom, condBlock + 0, turnRec | 0x08000000u);

                // Must NOT throw despite the near-EOF turn record.
                var ex = Record.Exception(() =>
                    EventScriptReferenceScanner.EnumerateEventEntries(rom));
                Assert.Null(ex);
            }
            finally
            {
                CoreState.ROM = prevRom;
            }
        }

        static void WriteU32(ROM rom, uint offset, uint value)
        {
            rom.Data[offset + 0] = (byte)(value & 0xFF);
            rom.Data[offset + 1] = (byte)((value >> 8) & 0xFF);
            rom.Data[offset + 2] = (byte)((value >> 16) & 0xFF);
            rom.Data[offset + 3] = (byte)((value >> 24) & 0xFF);
        }

        // =================================================================
        // Real-ROM enumeration / find tests (skipped when ROM absent).
        // =================================================================

        [Fact]
        public void RealRom_FE8U_FindAllBG_NonEmpty()
        {
            RealRomFindBgNonEmpty("FE8U.gba");
        }

        [Fact]
        public void RealRom_FE7U_FindAllBG_NonEmpty()
        {
            RealRomFindBgNonEmpty("FE7U.gba");
        }

        [Fact]
        public void RealRom_FE6_FindAllBG_NonEmpty()
        {
            RealRomFindBgNonEmpty("FE6.gba");
        }

        [Fact]
        public void RealRom_FE7U_Enumeration_IncludesTutorialFE7()
        {
            string romPath = FindRom("FE7U.gba");
            if (romPath == null) return; // skip

            WithRealRomEnv(romPath, rom =>
            {
                var entries = EventScriptReferenceScanner.EnumerateEventEntries(rom);
                bool hasTutorial = false;
                foreach (var e in entries)
                {
                    if (e.name == "Tutorial FE7") { hasTutorial = true; break; }
                }
                Assert.True(hasTutorial, "FE7U enumeration must include >=1 'Tutorial FE7' entry");
            });
        }

        static void RealRomFindBgNonEmpty(string romName)
        {
            string romPath = FindRom(romName);
            if (romPath == null) return; // skip

            WithRealRomEnv(romPath, rom =>
            {
                var map = EventScriptReferenceScanner.FindAllArgReferences(
                    rom, EventScript.ArgType.BG, keepZeroId: true);
                Assert.NotEmpty(map);

                // Non-stub proof: at least one bucket has at least one ref whose
                // event-start address is a real in-ROM offset.
                bool hasRealRef = false;
                foreach (var kv in map)
                {
                    foreach (var r in kv.Value)
                    {
                        if (U.isSafetyOffset(r.addr, rom)) { hasRealRef = true; break; }
                    }
                    if (hasRealRef) break;
                }
                Assert.True(hasRealRef, $"{romName}: must find >=1 real BG reference");
            });
        }

        // -----------------------------------------------------------------
        // Real-ROM env: load ROM, wire CoreState (ROM/EventScript/caches/
        // BaseDirectory) so the disasm path resolves the shipped event config.
        // -----------------------------------------------------------------
        static void WithRealRomEnv(string romPath, System.Action<ROM> body)
        {
            var savedRom = CoreState.ROM;
            var savedEs = CoreState.EventScript;
            var savedEnc = CoreState.SystemTextEncoder;
            var savedComment = CoreState.CommentCache;
            var savedBaseDir = CoreState.BaseDirectory;
            try
            {
                // BaseDirectory must point at the test output dir (config/ is
                // copied there) so EventScript.Load resolves the event config.
                string asmDir = Path.GetDirectoryName(
                    Assembly.GetExecutingAssembly().Location);
                CoreState.BaseDirectory = asmDir;

                var rom = new ROM();
                if (!rom.Load(romPath, out string _)) return;
                CoreState.ROM = rom;
                CoreState.SystemTextEncoder = new HeadlessSystemTextEncoder(rom);
                if (CoreState.CommentCache == null)
                    CoreState.CommentCache = new HeadlessEtcCache();

                var es = new EventScript();
                es.Load(EventScript.EventScriptType.Event);
                CoreState.EventScript = es;

                body(rom);
            }
            finally
            {
                CoreState.ROM = savedRom;
                CoreState.EventScript = savedEs;
                CoreState.SystemTextEncoder = savedEnc;
                CoreState.CommentCache = savedComment;
                if (savedBaseDir != null)
                    CoreState.BaseDirectory = savedBaseDir;
            }
        }

        // Walk up from the test assembly to find roms/<name>.
        static string FindRom(string romName)
        {
            string thisAssembly = Assembly.GetExecutingAssembly().Location;
            string dir = Path.GetDirectoryName(thisAssembly);
            for (int i = 0; i < 10 && dir != null; i++)
            {
                if (File.Exists(Path.Combine(dir, "FEBuilderGBA.sln")))
                {
                    string path = Path.Combine(dir, "roms", romName);
                    if (File.Exists(path)) return path;
                    break;
                }
                dir = Path.GetDirectoryName(dir);
            }
            return null;
        }
    }
}
