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
            // "garbage line ..." has > 2 tokens -> skipped.
            Assert.DoesNotContain(syms, s => s.Name == "with");
        }

        [Fact]
        public void Sym_Null_And_Garbage_NoThrow()
        {
            Assert.Empty(DecompSymParser.Parse(null));
            Assert.Empty(DecompSymParser.Parse(""));
            Assert.Empty(DecompSymParser.Parse("\x00\x01\xff garbage"));
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
    }
}
