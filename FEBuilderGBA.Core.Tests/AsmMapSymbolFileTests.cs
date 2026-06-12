// SPDX-License-Identifier: GPL-3.0-or-later
// Core tests for AsmMapSymbolFile (#1026) — the cross-platform ASM/MAP symbol
// reader that backs the Pointer Tool "What is this address?" lookup.
//
// Synthetic-first: the parser + SearchNear/TryGetValue semantics are verified
// against an in-memory asmmap text injected via the internal LoadFromLines test
// seam (no config-dir touching). One GUARDED real-ROM test confirms the
// production U.ConfigDataFilename path resolves a known FE8U symbol; it skips
// cleanly when roms/FE8U.gba is absent (worktree / CI without ROM files).
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using FEBuilderGBA;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    [Collection("SharedState")]
    public class AsmMapSymbolFileTests
    {
        // Build a non-multibyte FE8U synthetic ROM so {U} lines load and {J}
        // lines are filtered (U.OtherLangLine / U.ClipComment paths).
        static ROM MakeFe8uRom()
        {
            var data = new byte[0x1000000];
            var rom = new ROM();
            Assert.True(rom.LoadLow("asmmap-symbol-fe8u.gba", data, "BE8E01"));
            return rom;
        }

        // Build an AsmMapSymbolFile from explicit lines via the internal seam.
        static AsmMapSymbolFile FromLines(ROM rom, params string[] lines)
        {
            // The public ctor parses real config files; for synthetic unit tests
            // we construct against a version-0 (empty) ROM then inject lines.
            var emptyRom = new ROM(); // version 0 -> ctor builds an empty map
            var map = new AsmMapSymbolFile(emptyRom);
            map.LoadFromLines(rom, lines);
            return map;
        }

        [Fact]
        public void SearchNear_NearestKeyAtOrBelow_AndBeyondLastWithZeroLength()
        {
            var rom = MakeFe8uRom();
            // key 0x08001000 len 0x100 (a typed PALETTE8 = 0x100), key 0x08002000
            // len 0 (ASM). Use offsets without the 0x08 prefix in the line; the
            // parser applies U.toPointer.
            var map = FromLines(rom,
                "08001000\t&PALETTE8\tSymA",   // len 0x20*8 = 0x100
                "08002000\t&ASM\tSymB");        // len 0

            // Inside SymA's span -> nearest at/below is 0x08001000.
            Assert.Equal(0x08001000u, map.SearchNear(0x08001050u));
            // Between SymA's end (0x08001100) and SymB -> still nearest below = SymA.
            Assert.Equal(0x08001000u, map.SearchNear(0x08001500u));
            // Below the first key -> NOT_FOUND.
            Assert.Equal(U.NOT_FOUND, map.SearchNear(0x08000500u));
            // Beyond the last key (SymB len 0) -> returns the last key, NOT
            // NOT_FOUND (WF SearchNear i==0 semantics — amendment #4).
            Assert.Equal(0x08002000u, map.SearchNear(0x08002500u));
            // Exactly the first key.
            Assert.Equal(0x08001000u, map.SearchNear(0x08001000u));
        }

        [Fact]
        public void TryGetValue_ExactHit_AndMiss()
        {
            var rom = MakeFe8uRom();
            var map = FromLines(rom, "08001000\tMyFunc");

            Assert.True(map.TryGetValue(0x08001000u, out var hit));
            Assert.NotNull(hit);
            Assert.Equal("MyFunc", hit.Name);

            Assert.False(map.TryGetValue(0x08009999u, out var miss));
            Assert.Null(miss);
        }

        [Fact]
        public void Parser_TypedPaletteLength_RangeLength_AndAmpTypeRange()
        {
            var rom = MakeFe8uRom();
            var map = FromLines(rom,
                "08001000\t&PALETTE\tPal",                 // plain &TYPE: len 0x20
                "08002000\t:08002040\tRangeSym",           // untyped range: len 0x40
                "08003000\t:08003100\t&PALETTE16\tTypedRange"); // &TYPE range: len = end-start

            Assert.True(map.TryGetValue(0x08001000u, out var pal));
            Assert.Equal(0x20u, pal.Length);
            Assert.Equal("Pal", pal.Name);

            Assert.True(map.TryGetValue(0x08002000u, out var range));
            Assert.Equal(0x40u, range.Length); // range length = end - start
            Assert.Equal("RangeSym", range.Name);

            Assert.True(map.TryGetValue(0x08003000u, out var typedRange));
            // Range form: Length is end - start (0x100), NOT the &TYPE table value.
            Assert.Equal(0x100u, typedRange.Length);
            Assert.Equal("TypedRange", typedRange.Name);
            Assert.Equal("PALETTE16", typedRange.TypeName);
        }

        [Fact]
        public void Parser_NameStopsAtFirstEqualsField_AndArgsCaptured()
        {
            var rom = MakeFe8uRom();
            // Name "MyFunc"; args "r0=foo r1=bar" (name stops at first '='-field).
            var map = FromLines(rom, "08001000\tMyFunc\tr0=foo\tr1=bar");

            Assert.True(map.TryGetValue(0x08001000u, out var p));
            Assert.Equal("MyFunc", p.Name);
            Assert.Equal("r0=foo r1=bar", p.ResultAndArgs);
            // ToStringInfo joins name + args.
            Assert.Equal("MyFunc r0=foo r1=bar", p.ToStringInfo());
        }

        [Fact]
        public void Parser_MultiFieldName_ExtendsUntilEqualsField()
        {
            var rom = MakeFe8uRom();
            // Tab-separated "My" "Func" "Name" then "RET=x". The name extends
            // across the non-'=' fields; "RET=x" is the first '='-field.
            var map = FromLines(rom, "08001000\tMy\tFunc\tName\tRET=x");

            Assert.True(map.TryGetValue(0x08001000u, out var p));
            Assert.Equal("My Func Name", p.Name);
            // RET= extraction (plain form, no &TYPE) sets TypeName to "x".
            Assert.Equal("x", p.TypeName);
        }

        [Fact]
        public void Parser_LangFilter_OnlyMatchingVersionLineLoads()
        {
            // FE8U is NOT multibyte: a "\t{J}" line is skipped, a "\t{U}" line
            // loads (with the {U} token clipped). Two records at distinct keys.
            var rom = MakeFe8uRom();
            var map = FromLines(rom,
                "08001000\tJpOnly\t{J}",   // skipped on FE8U
                "08002000\tUsOnly\t{U}");  // loaded on FE8U

            Assert.False(map.TryGetValue(0x08001000u, out _));
            Assert.True(map.TryGetValue(0x08002000u, out var us));
            Assert.Equal("UsOnly", us.Name);
        }

        [Fact]
        public void Parser_SkipsCommentsStructDefsAndStructRefs()
        {
            var rom = MakeFe8uRom();
            var map = FromLines(rom,
                "# a comment line",
                "@MYSTRUCT\tb\tField",          // @STRUCT definition: skipped
                "08001000\t@MYSTRUCT\tInst",    // @STRUCT-referencing: out of scope
                "08002000\tKept");              // a normal record survives

            Assert.False(map.TryGetValue(0x08001000u, out _));
            Assert.True(map.TryGetValue(0x08002000u, out var kept));
            Assert.Equal("Kept", kept.Name);
        }

        [Fact]
        public void Ctor_VersionZeroRom_EmptyMap_NoThrow()
        {
            // A default ROM (version 0) -> empty map; SearchNear returns NOT_FOUND.
            var rom = new ROM();
            var map = new AsmMapSymbolFile(rom);
            Assert.False(map.TryGetValue(0x08000000u, out _));
            Assert.Equal(U.NOT_FOUND, map.SearchNear(0x08001000u));
        }

        [Fact]
        public void Ctor_NullRom_EmptyMap_NoThrow()
        {
            var map = new AsmMapSymbolFile(null);
            Assert.False(map.TryGetValue(0x08000000u, out _));
            Assert.Equal(U.NOT_FOUND, map.SearchNear(0x08001000u));
        }

        [Fact]
        public void HeadlessAsmMapCache_GetAsmMapFile_ReturnsNull_NoThrow()
        {
            // Interface default-body: HeadlessAsmMapCache does not override
            // GetAsmMapFile(), so it returns null (callers fall back to a
            // region hint only).
            IAsmMapCache cache = new HeadlessAsmMapCache();
            Assert.Null(cache.GetAsmMapFile());
        }

        [Fact]
        public void CoreAsmMapCache_GetAsmMapFile_ReturnsNonNull_SeparateFromHardcodeDirty()
        {
            // CoreAsmMapCache lazily builds a non-null AsmMapSymbolFile. A
            // version-0 ROM yields an empty (but non-null) map.
            var rom = new ROM();
            var cache = new CoreAsmMapCache(rom);
            var map = cache.GetAsmMapFile();
            Assert.NotNull(map);
            // Stable across calls until ClearCache.
            Assert.Same(map, cache.GetAsmMapFile());
            // ClearCache invalidates -> a fresh instance is built.
            cache.ClearCache();
            var map2 = cache.GetAsmMapFile();
            Assert.NotNull(map2);
            Assert.NotSame(map, map2);
        }

        [Fact]
        public void RealRom_FE8U_ResolvesKnownEventEngineFunction()
        {
            // GUARDED: requires roms/FE8U.gba next to the .sln. Skips cleanly
            // when absent (worktree / CI without ROM files).
            string romPath = FindRom("FE8U.gba");
            if (romPath == null) return; // skip

            var savedRom = CoreState.ROM;
            var savedBaseDir = CoreState.BaseDirectory;
            var savedLang = CoreState.Language;
            try
            {
                // BaseDirectory = repo root so ConfigDataFilename finds
                // config/data/asmmap_FE8*.txt. romPath = {root}/roms/FE8U.gba.
                string romsDir = Path.GetDirectoryName(romPath);
                string repoRoot = Path.GetDirectoryName(romsDir);
                CoreState.BaseDirectory = repoRoot;
                CoreState.Language = "en";

                var rom = new ROM();
                if (!rom.Load(romPath, out string _)) return; // skip on load failure
                CoreState.ROM = rom;

                var map = new AsmMapSymbolFile(rom);

                // 0x0800D07C is in asmmap_FE8.txt / .en.txt with a {U} tag (the
                // event-command-engine function). FE8U is not multibyte, so the
                // {U} line loads.
                Assert.True(map.TryGetValue(0x0800D07Cu, out var p),
                    "0x0800D07C must resolve in a vanilla FE8U asmmap");
                Assert.NotNull(p);
                Assert.False(string.IsNullOrEmpty(p.Name),
                    "the resolved symbol must have a name");
            }
            finally
            {
                CoreState.ROM = savedRom;
                CoreState.BaseDirectory = savedBaseDir;
                CoreState.Language = savedLang;
            }
        }

        static string FindRom(string romName)
        {
            string thisAssembly = Assembly.GetExecutingAssembly().Location;
            string dir = Path.GetDirectoryName(thisAssembly);
            for (int i = 0; i < 12 && dir != null; i++)
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
