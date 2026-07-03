// SPDX-License-Identifier: GPL-3.0-or-later
// Core tests for the decomp-project symbol resolver + merged asmmap (#1130).
//
// Covers:
//   - ELF decomp-mode filter: keeps FUNC + data-OBJECT (incl. st_shndx>1), drops
//     SHN_UNDEF extern, STT_SECTION, $-mapping syms; default (non-decomp) mode
//     still drops the data-object (old behaviour preserved).
//   - MergedAsmMapFile precedence (project wins at same addr).
//   - MergedAsmMapFile.SearchNear span-covering (zero-length project key doesn't
//     mask a covering shipped span).
//   - DecompSymbolResolver.Load auto-discovery by built-ROM stem + malformed
//     artifact graceful handling.
//   - Classic-mode regression: CoreAsmMapCache.GetAsmMapFile() returns a plain
//     AsmMapSymbolFile (NOT a MergedAsmMapFile) when no DecompProject.
using System;
using System.Collections.Generic;
using System.IO;
using FEBuilderGBA;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    [Collection("SharedState")]
    public class DecompSymbolResolverTests
    {
        // ---------------------------------------------------------------- ELF builder

        // Build a minimal-but-real ELF byte[] with a SYMTAB + STRTAB. Section header
        // table is laid out as: [0]=null, [1]=.symtab, [2]=.strtab. The symbol
        // entries are provided as (name, value, size, info, shndx) tuples.
        static byte[] BuildElf((string Name, uint Value, uint Size, byte Info, ushort Shndx)[] syms)
        {
            // ---- string table (symbol names) ----
            var strtab = new List<byte> { 0 };          // index 0 = empty string
            var nameOffsets = new int[syms.Length];
            for (int i = 0; i < syms.Length; i++)
            {
                nameOffsets[i] = strtab.Count;
                foreach (char c in syms[i].Name) strtab.Add((byte)c);
                strtab.Add(0);
            }

            // ---- symbol table (with a leading null entry, ELF convention) ----
            int symCount = syms.Length + 1;
            var symtab = new byte[symCount * 0x10];      // null entry already zeroed
            for (int i = 0; i < syms.Length; i++)
            {
                int off = (i + 1) * 0x10;
                WriteU32(symtab, off + 0x00, (uint)nameOffsets[i]);  // st_name
                WriteU32(symtab, off + 0x04, syms[i].Value);          // st_value
                WriteU32(symtab, off + 0x08, syms[i].Size);           // st_size
                symtab[off + 0x0C] = syms[i].Info;                    // st_info
                symtab[off + 0x0D] = 0;                               // st_other
                WriteU16(symtab, off + 0x0E, syms[i].Shndx);          // st_shndx
            }

            // ---- layout: header (0x34) + symtab + strtab + 3 section headers ----
            const int ehSize = 0x34;
            const int shSize = 0x28;
            int symOff = ehSize;
            int strOff = symOff + symtab.Length;
            int shoff = strOff + strtab.Count;
            int total = shoff + 3 * shSize;

            var bin = new byte[total];

            // ELF header
            bin[0] = 0x7F; bin[1] = (byte)'E'; bin[2] = (byte)'L'; bin[3] = (byte)'F';
            bin[4] = 1; // ELFCLASS32
            bin[5] = 1; // ELFDATA2LSB
            bin[6] = 1; // EV_CURRENT
            WriteU16(bin, 0x10, 1);          // e_type = ET_REL
            WriteU16(bin, 0x12, 40);         // e_machine = EM_ARM
            WriteU32(bin, 0x14, 1);          // e_version
            WriteU32(bin, 0x20, (uint)shoff);// e_shoff
            WriteU16(bin, 0x28, ehSize);     // e_ehsize
            WriteU16(bin, 0x2E, shSize);     // e_shentsize
            WriteU16(bin, 0x30, 3);          // e_shnum
            WriteU16(bin, 0x32, 2);          // e_shstrndx (points at .strtab; ok)

            Array.Copy(symtab, 0, bin, symOff, symtab.Length);
            strtab.CopyTo(bin, strOff);

            // Section header [0] = SHT_NULL (already zeroed).
            // Section header [1] = .symtab. sh_link -> section 2 (.strtab).
            int sh1 = shoff + 1 * shSize;
            WriteU32(bin, sh1 + 0x04, 2);                 // sh_type = SHT_SYMTAB
            WriteU32(bin, sh1 + 0x10, (uint)symOff);      // sh_offset
            WriteU32(bin, sh1 + 0x14, (uint)symtab.Length); // sh_size
            WriteU32(bin, sh1 + 0x18, 2);                 // sh_link -> .strtab
            WriteU32(bin, sh1 + 0x24, 0x10);              // sh_entsize

            // Section header [2] = .strtab.
            int sh2 = shoff + 2 * shSize;
            WriteU32(bin, sh2 + 0x04, 3);                 // sh_type = SHT_STRTAB
            WriteU32(bin, sh2 + 0x10, (uint)strOff);      // sh_offset
            WriteU32(bin, sh2 + 0x14, (uint)strtab.Count);// sh_size

            return bin;
        }

        static void WriteU32(byte[] b, int o, uint v)
        {
            b[o] = (byte)(v & 0xFF);
            b[o + 1] = (byte)((v >> 8) & 0xFF);
            b[o + 2] = (byte)((v >> 16) & 0xFF);
            b[o + 3] = (byte)((v >> 24) & 0xFF);
        }
        static void WriteU16(byte[] b, int o, ushort v)
        {
            b[o] = (byte)(v & 0xFF);
            b[o + 1] = (byte)((v >> 8) & 0xFF);
        }

        const byte STT_NOTYPE = 0, STT_OBJECT = 1, STT_FUNC = 2, STT_SECTION = 3;

        [Fact]
        public void Elf_DecompMode_KeepsFuncAndDataObject_DropsExternSectionMapping()
        {
            var syms = new (string, uint, uint, byte, ushort)[]
            {
                ("text_func",  0x08000100u, 0x40u, STT_FUNC,    1),    // .text func
                ("data_obj",   0x08001000u, 0x10u, STT_OBJECT,  3),    // shndx>1 (previously DROPPED)
                ("undef_ext",  0x00000000u, 0u,    STT_NOTYPE,  0),    // SHN_UNDEF extern
                ("a_section",  0x08002000u, 0u,    STT_SECTION, 1),    // STT_SECTION
                ("$t",         0x08000100u, 0u,    STT_NOTYPE,  1),    // $-mapping sym
            };
            string path = WriteTempElf(syms);
            try
            {
                var decomp = new Elf(path, useHookMode: false, decompMode: true);
                var names = new HashSet<string>();
                uint dataObjLen = 0;
                foreach (var s in decomp.SymList)
                {
                    names.Add(s.name);
                    if (s.name == "data_obj") dataObjLen = s.length;
                }

                Assert.Contains("text_func", names);
                Assert.Contains("data_obj", names);     // KEPT in decomp mode
                Assert.DoesNotContain("undef_ext", names);
                Assert.DoesNotContain("a_section", names);
                Assert.DoesNotContain("$t", names);
                Assert.Equal(0x10u, dataObjLen);        // st_size recorded in decomp mode

                // Default (non-decomp) mode: the data-object (shndx>1) is DROPPED
                // (old behaviour preserved), and length stays 0.
                var classic = new Elf(path, useHookMode: false);
                var classicNames = new HashSet<string>();
                foreach (var s in classic.SymList) classicNames.Add(s.name);
                Assert.DoesNotContain("data_obj", classicNames);
            }
            finally { TryDelete(path); }
        }

        static string WriteTempElf((string, uint, uint, byte, ushort)[] syms)
        {
            string path = Path.Combine(Path.GetTempPath(), $"decomp_{Guid.NewGuid():N}.elf");
            File.WriteAllBytes(path, BuildElf(syms));
            return path;
        }

        static void TryDelete(string path) { try { File.Delete(path); } catch { } }

        // ---------------------------------------------------------------- Merged map

        // Build a resolver directly from a .map text via a throwaway project dir.
        static DecompSymbolResolver ResolverFromMap(string mapText)
        {
            string dir = Path.Combine(Path.GetTempPath(), $"decompmap_{Guid.NewGuid():N}");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "rom.gba"), "x"); // tiny placeholder
            File.WriteAllText(Path.Combine(dir, "rom.map"), mapText);
            var project = new DecompProject { ProjectRoot = dir, BuiltRomPath = Path.Combine(dir, "rom.gba") };
            try { return DecompSymbolResolver.Load(project); }
            finally { try { Directory.Delete(dir, true); } catch { } }
        }

        // Build a resolver from a JSON artifact (auto-discovered as <stem>.sym.json),
        // so symbols carry EXPLICIT sizes the .map parser can't express.
        static DecompSymbolResolver ResolverFromJson(string jsonText)
        {
            string dir = Path.Combine(Path.GetTempPath(), $"decompjson_{Guid.NewGuid():N}");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "rom.gba"), "x");
            File.WriteAllText(Path.Combine(dir, "rom.sym.json"), jsonText);
            var project = new DecompProject { ProjectRoot = dir, BuiltRomPath = Path.Combine(dir, "rom.gba") };
            try { return DecompSymbolResolver.Load(project); }
            finally { try { Directory.Delete(dir, true); } catch { } }
        }

        // #1773: an FE8J project ships sym_jp.txt (linker-assign format), NOT a
        // <stem>.sym — the resolver must auto-discover it and resolve its symbols.
        [Fact]
        public void SymJpTxt_AutoDiscovered_ForFE8JProject_ResolvesNames()
        {
            string dir = Path.Combine(Path.GetTempPath(), $"decompsymjp_{Guid.NewGuid():N}");
            Directory.CreateDirectory(dir);
            try
            {
                // stem = "fireemblem8"; there is NO fireemblem8.sym, only sym_jp.txt.
                File.WriteAllText(Path.Combine(dir, "fireemblem8.gba"), "x");
                File.WriteAllText(Path.Combine(dir, "sym_jp.txt"), string.Join("\n", new[]
                {
                    "ApplyColorAddition_ClampMax = 0x080014C4;",
                    "ProcCmd_DELETE = 0x08003A48;",
                }));
                var project = new DecompProject { ProjectRoot = dir, BuiltRomPath = Path.Combine(dir, "fireemblem8.gba") };
                var resolver = DecompSymbolResolver.Load(project);

                // sym_jp.txt was auto-discovered and its linker-assign lines parsed.
                Assert.True(resolver.CountSym >= 2);
                Assert.Contains(resolver.Symbols.Values, v => v.Name == "ApplyColorAddition_ClampMax");
                Assert.Contains(resolver.Symbols.Values, v => v.Name == "ProcCmd_DELETE");

                // Resolve-by-address through the merged view (acceptance #3).
                var shipped = new AsmMapSymbolFile(new ROM());
                shipped.LoadFromLines(MakeFe8uRom(), new string[0]);
                var merged = new MergedAsmMapFile(shipped, resolver);
                Assert.Equal("ApplyColorAddition_ClampMax", merged.GetName(0x080014C4u));
                Assert.Equal("ProcCmd_DELETE", merged.GetName(0x08003A48u));
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
        }

        [Fact]
        public void Merged_Precedence_ProjectWinsAtSameAddr()
        {
            // Shipped map: one symbol at 0x08001000.
            var emptyRom = new ROM();
            var shipped = new AsmMapSymbolFile(emptyRom);
            shipped.LoadFromLines(MakeFe8uRom(), new[] { "08001000\tShippedName" });

            // Project map: a symbol at the SAME address.
            var resolver = ResolverFromMap(string.Join("\n", new[]
            {
                " .text          0x08000000     0x2000 a.o",
                "                0x08001000                ProjectName",
            }));

            var merged = new MergedAsmMapFile(shipped, resolver);

            Assert.True(merged.TryGetValue(0x08001000u, out var p));
            Assert.Equal("ProjectName", p.Name);        // project WINS
            Assert.Equal("ProjectName", merged.GetName(0x08001000u));
            Assert.Equal(0x08001000u, merged.SearchName("ProjectName"));
        }

        [Fact]
        public void Merged_SearchNear_ZeroLengthProjectKey_DoesNotMaskCoveringShippedSpan()
        {
            // Shipped: a covering span [0x08001000 .. 0x08001100).
            var shipped = new AsmMapSymbolFile(new ROM());
            shipped.LoadFromLines(MakeFe8uRom(), new[] { "08001000\t&PALETTE8\tShippedSpan" }); // len 0x100

            // Project: a ZERO-LENGTH key at 0x08001080 (between base and the query).
            // Use a section with size 0 so NO section-END boundary is added (#1138),
            // keeping ProjectPoint genuinely zero-length (it's also the last symbol).
            var resolver = ResolverFromMap(string.Join("\n", new[]
            {
                " .text          0x08000000        0x0 a.o",
                "                0x08001080                ProjectPoint",  // size 0
            }));

            var merged = new MergedAsmMapFile(shipped, resolver);

            // Sanity: ProjectPoint really is zero-length.
            Assert.True(merged.TryGetValue(0x08001080u, out var pt));
            Assert.Equal(0u, pt.Length);

            // Query 0x080010C0 is inside the shipped span but ABOVE the zero-length
            // project key. SearchNear must return the covering shipped base, not the
            // project point.
            uint near = merged.SearchNear(0x080010C0u);
            Assert.Equal(0x08001000u, near);
            Assert.True(merged.TryGetValue(near, out var sp));
            Assert.Equal("ShippedSpan", sp.Name);
        }

        [Fact]
        public void Merged_SearchNear_ProjectSide_CoveringSymbolNotMaskedByLaterZeroLengthPoint()
        {
            // PROJECT-vs-PROJECT masking regression (#1138). Build a project symbol
            // table from a JSON artifact with EXPLICIT sizes so a sized symbol's span
            // can extend PAST a later zero-length point:
            //   SizedSym @ 0x08001000 len 0x100  -> span [0x1000 .. 0x1100)
            //   PointSym @ 0x08001080 len 0      -> a zero-length point INSIDE the span
            // Resolving 0x080010C0 (ABOVE the point, still inside SizedSym's span) must
            // return 0x08001000 — the covering walk must NOT be masked by the nearest
            // at/below PointSym (which does not cover). No shipped table.
            string json = @"[
                { ""name"": ""SizedSym"", ""addr"": ""0x08001000"", ""size"": 256 },
                { ""name"": ""PointSym"", ""addr"": ""0x08001080"", ""size"": 0 }
            ]";
            var resolver = ResolverFromJson(json);

            Assert.True(resolver.Symbols.TryGetValue(0x08001000u, out var sized));
            Assert.Equal(0x100u, sized.Length);
            Assert.True(resolver.Symbols.TryGetValue(0x08001080u, out var point));
            Assert.Equal(0u, point.Length);

            var merged = new MergedAsmMapFile(null, resolver);

            // Nearest at/below 0x080010C0 is PointSym (0x1080), which does NOT cover.
            // The project-side covering walk must instead return the covering SizedSym.
            uint near = merged.SearchNear(0x080010C0u);
            Assert.Equal(0x08001000u, near);
            Assert.True(merged.TryGetValue(near, out var st));
            Assert.Equal("SizedSym", st.Name);
        }

        // ---------------------------------------------------------------- Discovery

        [Fact]
        public void Load_AutoDiscovery_FindsMapByBuiltRomStem()
        {
            string dir = Path.Combine(Path.GetTempPath(), $"decompdisc_{Guid.NewGuid():N}");
            Directory.CreateDirectory(dir);
            try
            {
                File.WriteAllText(Path.Combine(dir, "febuilder.project.json"),
                    "{ \"schemaVersion\": 1, \"builtRom\": \"foo.gba\" }");
                File.WriteAllText(Path.Combine(dir, "foo.gba"), "x");
                File.WriteAllText(Path.Combine(dir, "foo.map"), string.Join("\n", new[]
                {
                    " .text          0x08000000     0x1000 a.o",
                    "                0x08000200                discovered_sym",
                }));

                var project = new DecompProject
                {
                    ProjectRoot = dir,
                    BuiltRomPath = Path.Combine(dir, "foo.gba"),
                    Manifest = DecompProjectDetector.ParseManifest(Path.Combine(dir, "febuilder.project.json")),
                };

                var resolver = DecompSymbolResolver.Load(project);
                uint key = U.toPointer(0x08000200u);
                Assert.True(resolver.Symbols.ContainsKey(key));
                Assert.Equal("discovered_sym", resolver.Symbols[key].Name);
                Assert.Equal(1, resolver.CountMap);
                Assert.True(resolver.TryGetSource(key, out var src));
                Assert.Equal(DecompArtifactSource.Map, src);
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
        }

        [Fact]
        public void Load_MalformedArtifacts_NoThrow_GracefulEmpty()
        {
            string dir = Path.Combine(Path.GetTempPath(), $"decompbad_{Guid.NewGuid():N}");
            Directory.CreateDirectory(dir);
            try
            {
                File.WriteAllText(Path.Combine(dir, "bad.gba"), "x");
                // Garbage .map / .elf / .sym.json — all must skip cleanly.
                File.WriteAllBytes(Path.Combine(dir, "bad.map"), new byte[] { 0x00, 0xFF, 0x01, 0x02 });
                File.WriteAllBytes(Path.Combine(dir, "bad.elf"), new byte[] { 0xDE, 0xAD, 0xBE, 0xEF });
                File.WriteAllText(Path.Combine(dir, "bad.sym.json"), "{ not valid");

                var project = new DecompProject { ProjectRoot = dir, BuiltRomPath = Path.Combine(dir, "bad.gba") };
                var resolver = DecompSymbolResolver.Load(project);
                Assert.Equal(0, resolver.Count);     // graceful empty, never threw
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
        }

        // ---------------------------------------------------------------- Classic regression

        [Fact]
        public void ClassicMode_NoDecompProject_GetAsmMapFile_ReturnsPlainSymbolFile()
        {
            var savedProject = CoreState.DecompProject;
            try
            {
                CoreState.DecompProject = null;     // classic mode
                var rom = new ROM();
                var cache = new CoreAsmMapCache(rom);
                var map = cache.GetAsmMapFile();
                Assert.NotNull(map);
                Assert.IsType<AsmMapSymbolFile>(map);          // NOT a MergedAsmMapFile
                Assert.IsNotType<MergedAsmMapFile>(map);
            }
            finally { CoreState.DecompProject = savedProject; }
        }

        // ---------------------------------------------------------------- helper

        static ROM MakeFe8uRom()
        {
            var data = new byte[0x1000000];
            var rom = new ROM();
            Assert.True(rom.LoadLow("decomp-resolver-fe8u.gba", data, "BE8E01"));
            return rom;
        }
    }
}
