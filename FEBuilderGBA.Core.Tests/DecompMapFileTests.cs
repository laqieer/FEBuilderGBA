// SPDX-License-Identifier: GPL-3.0-or-later
// Core tests for the decomp-project artifact parsers (#1130 slice 2):
//   DecompMapParser (.map), DecompSymParser (.sym), DecompSymbolJsonParser (JSON).
//
// Synthetic-first: golden in-memory text snippets exercise the parse semantics
// (symbol vs section vs wrapped-section, same-address aliases, linker-script
// noise, hex-string vs numeric addresses). All parsers are READ-ONLY and must
// never throw.
using System.Collections.Generic;
using System.Linq;
using FEBuilderGBA;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    public class DecompMapFileTests
    {
        [Fact]
        public void Map_NormalSymbols_Section_WrappedSection_AndNoise()
        {
            // A realistic ld .map snippet: a section header (single-line), two
            // symbols under it, a wrapped section name with its addr/size on the
            // next line + a symbol, plus linker-script noise that must be skipped.
            string map = string.Join("\n", new[]
            {
                "Memory Configuration",
                "",
                "Linker script and memory map",
                "",
                " .text          0x08000000     0x2000 build/main.o",
                "                0x08000100                main_init",
                "                0x08000200                main_loop",
                "                PROVIDE (__stack, .)",
                "                0x08000300                = .",
                " *fill*         0x08000400        0x4 ",
                "/DISCARD/       0x00000000        0x0",
                "LOAD build/libc.a",
                " .rodata.long_named_section_that_wraps",
                "                0x08001000      0x100 build/rodata.o",
                "                0x08001010                gRodataTable",
                "                ABSOLUTE (foo)",
            });

            List<DecompSymbol> syms = DecompMapParser.Parse(map);
            var byName = syms.ToDictionary(s => s.Name, s => s.Addr);

            Assert.True(byName.ContainsKey("main_init"));
            Assert.Equal(0x08000100u, byName["main_init"]);
            Assert.True(byName.ContainsKey("main_loop"));
            Assert.Equal(0x08000200u, byName["main_loop"]);
            Assert.True(byName.ContainsKey("gRodataTable"));
            Assert.Equal(0x08001010u, byName["gRodataTable"]);

            // None of the noise lines became a symbol.
            Assert.False(byName.ContainsKey("__stack"));
            Assert.False(byName.ContainsKey("foo"));
            Assert.DoesNotContain(syms, s => s.Name.Contains("fill"));
            Assert.Equal(3, syms.Count);
        }

        [Fact]
        public void Map_SameAddressAliases_FirstWins()
        {
            string map = string.Join("\n", new[]
            {
                " .text          0x08000000     0x1000 a.o",
                "                0x08000100                FirstName",
                "                0x08000100                AliasName",
            });

            List<DecompSymbol> syms = DecompMapParser.Parse(map);
            Assert.Single(syms);
            Assert.Equal("FirstName", syms[0].Name);
            Assert.Equal(0x08000100u, syms[0].Addr);
        }

        [Fact]
        public void Map_SizeFromNextBoundary_BestEffort()
        {
            string map = string.Join("\n", new[]
            {
                " .text          0x08000000     0x1000 a.o",
                "                0x08000100                SymA",
                "                0x08000140                SymB",
            });

            List<DecompSymbol> syms = DecompMapParser.Parse(map);
            var a = syms.First(s => s.Name == "SymA");
            // SymA's size = SymB addr - SymA addr = 0x40.
            Assert.Equal(0x40u, a.Size);
        }

        [Fact]
        public void Map_LastSymbolSize_FromSingleLineSectionEnd()
        {
            // The ONLY/LAST symbol in a single-line section gets its Size from the
            // section's 0xSIZE row (section END boundary), not 0 (#1138).
            // .text @ 0x08000000 size 0x200 -> END 0x08000200. LastSym @ 0x08000100
            // -> Size = 0x08000200 - 0x08000100 = 0x100.
            string map = string.Join("\n", new[]
            {
                " .text          0x08000000      0x200 a.o",
                "                0x08000100                LastSym",
            });

            List<DecompSymbol> syms = DecompMapParser.Parse(map);
            var last = syms.First(s => s.Name == "LastSym");
            Assert.Equal(0x100u, last.Size);

            // An address inside that final span resolves to LastSym via SearchNear.
            var resolver = ResolverFromMapSnippet(map);
            var merged = new MergedAsmMapFile(null, resolver);
            uint near = merged.SearchNear(0x080001F0u);   // inside [0x100..0x200)
            Assert.True(merged.TryGetValue(near, out var st));
            Assert.Equal("LastSym", st.Name);
        }

        [Fact]
        public void Map_LastSymbolSize_FromWrappedSectionEnd()
        {
            // Same as above but the section name wraps to its own line, with the
            // addr/size on the continuation line (#1138 wrapped-section branch).
            string map = string.Join("\n", new[]
            {
                " .rodata.long_named_section_that_wraps",
                "                0x08001000      0x100 a.o",
                "                0x08001080                WrappedLast",
            });

            List<DecompSymbol> syms = DecompMapParser.Parse(map);
            var last = syms.First(s => s.Name == "WrappedLast");
            // section END 0x08001100 - sym 0x08001080 = 0x80.
            Assert.Equal(0x80u, last.Size);
        }

        [Fact]
        public void Map_RamSectionLastSymbol_DoesNotSpanIntoRom()
        {
            // An IWRAM (0x03xxxxxx) section's last symbol must be bounded by the
            // IWRAM section END, NOT extend into the next ROM section (#1138). So a
            // ROM address does NOT resolve to the RAM symbol.
            string map = string.Join("\n", new[]
            {
                " .bss           0x03000000       0x40 a.o",   // IWRAM, END 0x03000040
                "                0x03000000                gRamVar",
                " .text          0x08000000     0x1000 b.o",   // ROM section
                "                0x08000200                RomFunc",
            });

            List<DecompSymbol> syms = DecompMapParser.Parse(map);
            var ram = syms.First(s => s.Name == "gRamVar");
            // gRamVar size bounded by IWRAM section END -> 0x40, NOT up to 0x08000000.
            Assert.Equal(0x40u, ram.Size);

            // A ROM address (0x08000100) must NOT be COVERED by the RAM symbol's span.
            // SearchNear may still return gRamVar as the nearest-at/below key (no ROM
            // symbol sits below 0x08000100), but the span check the Pointer Tool / CLI
            // apply (pointer < key + Length) must reject it — gRamVar's bounded 0x40
            // size means [0x03000000..0x03000040) does not reach ROM.
            var resolver = ResolverFromMapSnippet(map);
            var merged = new MergedAsmMapFile(null, resolver);
            uint near = merged.SearchNear(0x08000100u);
            if (near != U.NOT_FOUND && merged.TryGetValue(near, out var st)
                && st.Name == "gRamVar")
            {
                // If gRamVar is the nearest key, its span must NOT cover the ROM addr.
                Assert.True(0x08000100u >= (ulong)near + st.Length,
                    "RAM symbol span must not extend into ROM space");
            }
        }

        [Fact]
        public void Map_Null_And_Garbage_NoThrow()
        {
            Assert.Empty(DecompMapParser.Parse(null));
            Assert.Empty(DecompMapParser.Parse(""));
            // Random binary-ish garbage: never throws, returns empty.
            Assert.Empty(DecompMapParser.Parse("\x00\x01\x02 not a map \xff\xfe random"));
        }

        [Fact]
        public void Sym_NoGbaForm_ParsesAddrAndName_SkipsLowAddr()
        {
            string sym = string.Join("\n", new[]
            {
                "0800D07C event_engine_main",
                "100109C MMBDrawInventoryObjs",
                "00000050 too_low_skipped",
                "garbage line with three tokens here",
                "$t mapping_skipped",
            });

            List<DecompSymbol> syms = DecompSymParser.Parse(sym);
            var byName = syms.ToDictionary(s => s.Name, s => s.Addr);

            Assert.True(byName.ContainsKey("event_engine_main"));
            Assert.Equal(0x0800D07Cu, byName["event_engine_main"]);
            Assert.True(byName.ContainsKey("MMBDrawInventoryObjs"));
            Assert.Equal(0x100109Cu, byName["MMBDrawInventoryObjs"]);

            Assert.False(byName.ContainsKey("too_low_skipped"));   // addr <= 0x100
            // "garbage line with three tokens here": first token "garbage" -> atoh 0
            // (addr <= 0x100) -> skipped; the name token "line" is never added.
            Assert.DoesNotContain(syms, s => s.Name == "line");
        }

        [Fact]
        public void Sym_ColumnAligned_MultipleSpacesAndTabs_ParsesCorrectly()
        {
            // Column-aligned no$gba dumps pad addr->name with repeated spaces / tabs
            // and may carry trailing columns. Fix #1138: split on any whitespace run,
            // take the first two tokens, ignore the rest.
            string sym = string.Join("\n", new[]
            {
                "0800D07C    event_engine_main",        // multiple spaces
                "0800E000\tmenu_init",                  // tab separator
                "0800F000   \t  gPlayerUnits   extra",  // mixed spaces+tab + trailing column
            });

            List<DecompSymbol> syms = DecompSymParser.Parse(sym);
            var byName = syms.ToDictionary(s => s.Name, s => s.Addr);

            Assert.Equal(0x0800D07Cu, byName["event_engine_main"]);
            Assert.Equal(0x0800E000u, byName["menu_init"]);
            // The trailing "extra" column is ignored; name is just the 2nd token.
            Assert.Equal(0x0800F000u, byName["gPlayerUnits"]);
        }

        [Fact]
        public void Sym_Null_And_Garbage_NoThrow()
        {
            Assert.Empty(DecompSymParser.Parse(null));
            Assert.Empty(DecompSymParser.Parse(""));
            Assert.Empty(DecompSymParser.Parse("\x00\x01\xff garbage"));
        }

        // #1773: FE8J sym_jp.txt linker-assignment form ("Name = 0x08XXXXXX;").

        [Fact]
        public void Sym_LinkerAssign_FE8J_ParsesNameEqualsHex()
        {
            string sym = string.Join("\n", new[]
            {
                "ColorFadeTick = 0x08000234;",
                "ApplyColorAddition_ClampMax = 0x080014C4;",
                "ProcCmd_DELETE = 0x08003A48;",
            });

            List<DecompSymbol> syms = DecompSymParser.Parse(sym);
            var byName = syms.ToDictionary(s => s.Name, s => s.Addr);

            Assert.Equal(0x08000234u, byName["ColorFadeTick"]);
            Assert.Equal(0x080014C4u, byName["ApplyColorAddition_ClampMax"]);
            Assert.Equal(0x08003A48u, byName["ProcCmd_DELETE"]);
        }

        [Fact]
        public void Sym_LinkerAssign_TolerantOfCommentsWhitespaceAndSkipsNonSymbols()
        {
            string sym = string.Join("\n", new[]
            {
                "  spaced_symbol   =   0x08001000 ;",              // extra whitespace + space before ;
                "commented = 0x08002000; /* trailing comment */",  // trailing block comment
                "/* block */ inline_before = 0x08003000;",         // leading block comment
                "valid_symbol = 0x08004000;",
                ". = 0x08005000;",                                 // linker location counter -> skipped
                "low_skipped = 0x00000050;",                       // addr <= 0x100 -> skipped
            });

            List<DecompSymbol> syms = DecompSymParser.Parse(sym);
            var byName = syms.ToDictionary(s => s.Name, s => s.Addr);

            Assert.Equal(0x08001000u, byName["spaced_symbol"]);
            Assert.Equal(0x08002000u, byName["commented"]);
            Assert.Equal(0x08003000u, byName["inline_before"]);
            Assert.Equal(0x08004000u, byName["valid_symbol"]);
            Assert.False(byName.ContainsKey("."));            // location counter is not a symbol
            Assert.False(byName.ContainsKey("low_skipped"));  // addr <= 0x100
            Assert.All(syms, s => Assert.InRange(s.Addr, 0x08000000u, 0x09FFFFFFu));
        }

        [Fact]
        public void Sym_MixedLinkerAssignAndNoGba_BothParsed()
        {
            string sym = string.Join("\n", new[]
            {
                "0800D07C event_engine_main",   // no$gba form
                "ColorFadeTick = 0x08000234;",  // linker-assign form
            });
            List<DecompSymbol> syms = DecompSymParser.Parse(sym);
            var byName = syms.ToDictionary(s => s.Name, s => s.Addr);
            Assert.Equal(0x0800D07Cu, byName["event_engine_main"]);
            Assert.Equal(0x08000234u, byName["ColorFadeTick"]);
        }

        [Fact]
        public void Json_ArrayForm_HexAndNumericAddr()
        {
            string json = @"[
                { ""name"": ""sym_hex"",  ""addr"": ""0x08000100"", ""size"": 16, ""section"": "".text"" },
                { ""name"": ""sym_num"",  ""addr"": 134218496 },
                { ""name"": ""too_low"",  ""addr"": ""0x00000040"" },
                { ""name"": ""$skipped"", ""addr"": ""0x08000200"" }
            ]";

            List<DecompSymbol> syms = DecompSymbolJsonParser.Parse(json);
            var byName = syms.ToDictionary(s => s.Name, s => s);

            Assert.True(byName.ContainsKey("sym_hex"));
            Assert.Equal(0x08000100u, byName["sym_hex"].Addr);
            Assert.Equal(16u, byName["sym_hex"].Size);
            Assert.Equal(".text", byName["sym_hex"].Section);

            Assert.True(byName.ContainsKey("sym_num"));
            Assert.Equal(0x08000300u, byName["sym_num"].Addr); // 134218496 == 0x08000300

            Assert.False(byName.ContainsKey("too_low"));    // addr < 0x02000000
            Assert.False(byName.ContainsKey("$skipped"));   // $-prefixed
        }

        [Fact]
        public void Json_SymbolsWrapperForm()
        {
            string json = @"{ ""symbols"": [
                { ""name"": ""wrapped_sym"", ""addr"": ""0x08001000"" }
            ] }";

            List<DecompSymbol> syms = DecompSymbolJsonParser.Parse(json);
            Assert.Single(syms);
            Assert.Equal("wrapped_sym", syms[0].Name);
            Assert.Equal(0x08001000u, syms[0].Addr);
        }

        [Fact]
        public void Json_Null_And_Garbage_NoThrow()
        {
            Assert.Empty(DecompSymbolJsonParser.Parse(null));
            Assert.Empty(DecompSymbolJsonParser.Parse(""));
            Assert.Empty(DecompSymbolJsonParser.Parse("{ not valid json"));
            Assert.Empty(DecompSymbolJsonParser.Parse("\x00\xff garbage bytes"));
            // A valid-but-wrong-shape JSON: empty, no throw.
            Assert.Empty(DecompSymbolJsonParser.Parse("42"));
            Assert.Empty(DecompSymbolJsonParser.Parse("{ \"other\": 1 }"));
        }

        // Build a DecompSymbolResolver from a .map snippet via a throwaway project
        // dir (built-ROM stem auto-discovery), so SearchNear can be exercised.
        static DecompSymbolResolver ResolverFromMapSnippet(string mapText)
        {
            string dir = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), $"decompmapunit_{System.Guid.NewGuid():N}");
            System.IO.Directory.CreateDirectory(dir);
            System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "rom.gba"), "x");
            System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "rom.map"), mapText);
            var project = new DecompProject
            {
                ProjectRoot = dir,
                BuiltRomPath = System.IO.Path.Combine(dir, "rom.gba"),
            };
            try { return DecompSymbolResolver.Load(project); }
            finally { try { System.IO.Directory.Delete(dir, true); } catch { } }
        }
    }
}
