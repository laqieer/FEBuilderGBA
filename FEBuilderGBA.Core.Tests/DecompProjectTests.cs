// SPDX-License-Identifier: GPL-3.0-or-later
// #1129 slice 1 — Core tests for the decomp-project open-mode detector + resolver.
//
// These tests build synthetic project layouts in temp dirs and assert on
// DecompProjectDetector.Detect / ResolveBuiltRom / ParseManifest / ParseMakefileRomStem.
// Detection + resolution only check File.Exists / Directory.Exists / file heads, so a
// tiny placeholder .gba (not a real ROM) is enough — actual ROM loading is covered by
// the E2E suite. CoreState mutation cases use [Collection("SharedState")].
using System;
using System.IO;
using FEBuilderGBA;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    [Collection("SharedState")]
    public class DecompProjectTests
    {
        // ---- fixture helpers -------------------------------------------------

        static string NewTempDir()
        {
            string dir = Path.Combine(Path.GetTempPath(), "decompfix_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return dir;
        }

        static void WriteFile(string dir, string name, string content)
            => File.WriteAllText(Path.Combine(dir, name), content);

        static void WriteElf(string dir, string name)
            => File.WriteAllBytes(Path.Combine(dir, name), new byte[] { 0x7F, 0x45, 0x4C, 0x46 });

        static void TouchGba(string dir, string name)
            => File.WriteAllBytes(Path.Combine(dir, name), new byte[] { 0, 1, 2, 3 });

        // ---- Detect: manifest short-circuit ----------------------------------

        [Fact]
        public void Detect_ManifestSchemaV1WithReservedKey_Accepts()
        {
            string dir = NewTempDir();
            try
            {
                WriteFile(dir, DecompProject.ManifestFileName,
                    "{ \"schemaVersion\": 1, \"builtRom\": \"synth.gba\", \"futureThing\": { \"a\": 1 } }");
                TouchGba(dir, "synth.gba");

                var p = DecompProjectDetector.Detect(dir);
                Assert.NotNull(p);
                Assert.Equal(Path.GetFullPath(dir), p.ProjectRoot);
                Assert.NotNull(p.Manifest);
                Assert.Equal(1, p.Manifest.SchemaVersion);
            }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void Detect_ManifestKnownFieldNoSchema_Accepts()
        {
            string dir = NewTempDir();
            try
            {
                WriteFile(dir, DecompProject.ManifestFileName, "{ \"builtRom\": \"synth.gba\" }");
                TouchGba(dir, "synth.gba");
                Assert.NotNull(DecompProjectDetector.Detect(dir));
            }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void Detect_EmptyManifestNoHeuristics_ReturnsNull()
        {
            string dir = NewTempDir();
            try
            {
                WriteFile(dir, DecompProject.ManifestFileName, "{}");
                Assert.Null(DecompProjectDetector.Detect(dir));
            }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void Detect_EmptyManifestButHeuristicsPresent_AcceptsViaHeuristics()
        {
            string dir = NewTempDir();
            try
            {
                WriteFile(dir, DecompProject.ManifestFileName, "{}");
                WriteFile(dir, "Makefile", "ROM := synth.gba\n");   // weight 2 → accept
                Assert.NotNull(DecompProjectDetector.Detect(dir));
            }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void Detect_UnsupportedSchemaVersion_ReturnsNull()
        {
            string dir = NewTempDir();
            try
            {
                WriteFile(dir, DecompProject.ManifestFileName, "{ \"schemaVersion\": 999, \"builtRom\": \"synth.gba\" }");
                TouchGba(dir, "synth.gba");
                // Amendment 1: unsupported schemaVersion is a hard reject (no fall-through).
                Assert.Null(DecompProjectDetector.Detect(dir));
            }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void Detect_MalformedManifestNoHeuristics_ReturnsNull()
        {
            string dir = NewTempDir();
            try
            {
                WriteFile(dir, DecompProject.ManifestFileName, "{ this is not valid json ");
                Assert.Null(DecompProjectDetector.Detect(dir));
            }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void Detect_MalformedManifestButHeuristicsPresent_AcceptsViaHeuristics()
        {
            string dir = NewTempDir();
            try
            {
                WriteFile(dir, DecompProject.ManifestFileName, "garbage");
                WriteFile(dir, "Makefile", "BUILD_NAME := synth\n");  // weight 2
                Assert.NotNull(DecompProjectDetector.Detect(dir));
            }
            finally { Directory.Delete(dir, true); }
        }

        // ---- Detect: heuristic scoring ---------------------------------------

        [Fact]
        public void Detect_HeuristicProject_Accepts()
        {
            string dir = NewTempDir();
            try
            {
                WriteFile(dir, "Makefile", "ROM := synth.gba\nCC := agbcc\n"); // weight 2
                WriteFile(dir, "synth.sha1", "00\n");                           // +1
                WriteFile(dir, "ldscript.txt", "MEMORY {}\n");                  // +1
                Directory.CreateDirectory(Path.Combine(dir, "src"));
                Directory.CreateDirectory(Path.Combine(dir, "asm"));           // src+asm +1
                TouchGba(dir, "synth.gba");
                WriteElf(dir, "synth.elf");

                var p = DecompProjectDetector.Detect(dir);
                Assert.NotNull(p);
                Assert.Null(p.Manifest);          // heuristic path → no manifest
                Assert.Null(p.ForceVersion);
            }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void Detect_MakefileOnlyWithRomLine_ScoresTwo_Accepts()
        {
            string dir = NewTempDir();
            try
            {
                WriteFile(dir, "Makefile", "ROM := x.gba\n");  // weight 2 alone
                Assert.NotNull(DecompProjectDetector.Detect(dir));
            }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void Detect_AgbccOnlyMakefileNoRomLine_ScoresOne_ReturnsNull()
        {
            string dir = NewTempDir();
            try
            {
                // Amendment 2: agbcc present but no ROM:=/BUILD_NAME:= → weight 1 only.
                WriteFile(dir, "Makefile", "CC := tools/agbcc/bin/agbcc\nall:\n\t$(CC)\n");
                Assert.Null(DecompProjectDetector.Detect(dir));
            }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void Detect_SrcOnlyDir_ReturnsNull()
        {
            string dir = NewTempDir();
            try
            {
                Directory.CreateDirectory(Path.Combine(dir, "src"));  // src without asm/data → 0
                Assert.Null(DecompProjectDetector.Detect(dir));
            }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void Detect_NotAProject_ReturnsNull()
        {
            string dir = NewTempDir();
            try
            {
                WriteFile(dir, "README.md", "hello");
                Assert.Null(DecompProjectDetector.Detect(dir));
            }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void Detect_NonexistentDir_ReturnsNullNoThrow()
        {
            string dir = Path.Combine(Path.GetTempPath(), "decomp_missing_" + Guid.NewGuid().ToString("N"));
            Assert.Null(DecompProjectDetector.Detect(dir));
            Assert.Null(DecompProjectDetector.Detect(null));
            Assert.Null(DecompProjectDetector.Detect(""));
        }

        // ---- ResolveBuiltRom -------------------------------------------------

        [Fact]
        public void Resolve_ManifestBuiltRom_Ok()
        {
            string dir = NewTempDir();
            try
            {
                WriteFile(dir, DecompProject.ManifestFileName, "{ \"schemaVersion\": 1, \"builtRom\": \"build/out.gba\" }");
                Directory.CreateDirectory(Path.Combine(dir, "build"));
                TouchGba(Path.Combine(dir, "build"), "out.gba");

                var p = DecompProjectDetector.Detect(dir);
                Assert.NotNull(p);
                var r = DecompProjectDetector.ResolveBuiltRom(dir, p);
                Assert.Equal(DecompResolveStatus.Ok, r.Status);
                Assert.EndsWith("out.gba", r.Path);
            }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void Resolve_MakefileStem_Ok()
        {
            string dir = NewTempDir();
            try
            {
                WriteFile(dir, "Makefile", "ROM := fe8\n");
                WriteFile(dir, "fe8.sha1", "00\n");
                TouchGba(dir, "fe8.gba");  // stem fe8 → fe8.gba

                var p = DecompProjectDetector.Detect(dir);
                Assert.NotNull(p);
                var r = DecompProjectDetector.ResolveBuiltRom(dir, p);
                Assert.Equal(DecompResolveStatus.Ok, r.Status);
                Assert.EndsWith("fe8.gba", r.Path);
            }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void Resolve_SiblingElfGlob_Ok()
        {
            string dir = NewTempDir();
            try
            {
                // No manifest, no Makefile ROM stem — fall to glob: out.gba + out.elf.
                WriteFile(dir, "Makefile", "BUILD_NAME := whatever\nagbcc\n");
                WriteFile(dir, "x.sha1", "00\n");  // score 2+ → accepted
                TouchGba(dir, "out.gba");
                WriteElf(dir, "out.elf");

                var p = DecompProjectDetector.Detect(dir);
                Assert.NotNull(p);
                var r = DecompProjectDetector.ResolveBuiltRom(dir, p);
                Assert.Equal(DecompResolveStatus.Ok, r.Status);
                Assert.EndsWith("out.gba", r.Path);
            }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void Resolve_MultiGba_PrefersMakefileStem()
        {
            string dir = NewTempDir();
            try
            {
                WriteFile(dir, "Makefile", "ROM := built\n");
                WriteFile(dir, "x.sha1", "00\n");
                // two .gba each with a sibling .elf; built.gba should win via stem match.
                TouchGba(dir, "built.gba"); WriteElf(dir, "built.elf");
                TouchGba(dir, "other.gba"); WriteElf(dir, "other.elf");

                var p = DecompProjectDetector.Detect(dir);
                Assert.NotNull(p);
                var r = DecompProjectDetector.ResolveBuiltRom(dir, p);
                Assert.Equal(DecompResolveStatus.Ok, r.Status);
                Assert.EndsWith("built.gba", r.Path);
            }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void Resolve_BaseromOnlyNoElf_NotBuilt()
        {
            string dir = NewTempDir();
            try
            {
                // Amendment 4: baserom.gba without baserom.elf is NOT a built ROM.
                WriteFile(dir, "Makefile", "ROM := fe8\n");  // stem fe8, but no fe8.gba
                WriteFile(dir, "fe8.sha1", "00\n");
                TouchGba(dir, "baserom.gba");                // no baserom.elf

                var p = DecompProjectDetector.Detect(dir);
                Assert.NotNull(p);
                var r = DecompProjectDetector.ResolveBuiltRom(dir, p);
                Assert.Equal(DecompResolveStatus.NotBuilt, r.Status);
            }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void Resolve_UnbuiltProject_NotBuiltNoThrow()
        {
            string dir = NewTempDir();
            try
            {
                WriteFile(dir, "Makefile", "ROM := synth.gba\n");
                WriteFile(dir, "synth.sha1", "00\n");
                // NO synth.gba present.

                var p = DecompProjectDetector.Detect(dir);
                Assert.NotNull(p);
                var r = DecompProjectDetector.ResolveBuiltRom(dir, p);
                Assert.Equal(DecompResolveStatus.NotBuilt, r.Status);
            }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void Resolve_AbsoluteManifestBuiltRom_RejectedFallsThrough()
        {
            string dir = NewTempDir();
            try
            {
                string abs = Path.Combine(Path.GetTempPath(), "elsewhere.gba").Replace("\\", "\\\\");
                WriteFile(dir, DecompProject.ManifestFileName, "{ \"schemaVersion\": 1, \"builtRom\": \"" + abs + "\" }");
                // no Makefile stem, no sibling-elf glob → NotBuilt after rejecting the abs path.

                var p = DecompProjectDetector.Detect(dir);
                Assert.NotNull(p);
                var r = DecompProjectDetector.ResolveBuiltRom(dir, p);
                Assert.Equal(DecompResolveStatus.NotBuilt, r.Status);
            }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void Resolve_DotDotEscapeManifestBuiltRom_RejectedFallsThrough()
        {
            string dir = NewTempDir();
            try
            {
                WriteFile(dir, DecompProject.ManifestFileName, "{ \"schemaVersion\": 1, \"builtRom\": \"../escape.gba\" }");
                var p = DecompProjectDetector.Detect(dir);
                Assert.NotNull(p);
                var r = DecompProjectDetector.ResolveBuiltRom(dir, p);
                // ..-escape rejected; no other source → NotBuilt.
                Assert.Equal(DecompResolveStatus.NotBuilt, r.Status);
            }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void Resolve_NullProject_NotProject()
        {
            string dir = NewTempDir();
            try
            {
                var r = DecompProjectDetector.ResolveBuiltRom(dir, null);
                Assert.Equal(DecompResolveStatus.NotProject, r.Status);
            }
            finally { Directory.Delete(dir, true); }
        }

        // ---- ParseManifest tolerance ----------------------------------------

        [Fact]
        public void ParseManifest_FutureKeyAndChangedShapeSection_StillParses()
        {
            string dir = NewTempDir();
            try
            {
                // reserved "artifacts" changed shape (string instead of object) + a future key.
                WriteFile(dir, DecompProject.ManifestFileName,
                    "{ \"schemaVersion\": 1, \"builtRom\": \"synth.gba\", \"artifacts\": \"a string instead of object\", \"brandNewKey\": [1,2,3] }");
                string path = Path.Combine(dir, DecompProject.ManifestFileName);
                var m = DecompProjectDetector.ParseManifest(path);
                Assert.NotNull(m);
                Assert.Equal("synth.gba", m.BuiltRom);
                Assert.NotNull(m.Artifacts);   // captured as a tolerant JsonElement
                Assert.NotNull(m.Extra);
                Assert.True(m.Extra.ContainsKey("brandNewKey"));
            }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void ParseManifest_ObjectShapedBuildSection_StillParses()
        {
            // Copilot PR #1136 finding: reserved `build` as an OBJECT (e.g.
            // {command,args}) must parse tolerantly, not throw + reject the project.
            string dir = NewTempDir();
            try
            {
                WriteFile(dir, DecompProject.ManifestFileName,
                    "{ \"schemaVersion\": 1, \"builtRom\": \"synth.gba\", \"build\": { \"command\": \"make\", \"args\": [\"-j8\"] } }");
                TouchGba(dir, "synth.gba");

                var m = DecompProjectDetector.ParseManifest(Path.Combine(dir, DecompProject.ManifestFileName));
                Assert.NotNull(m);
                Assert.Equal("make", m.BuildCommand);   // extracted from object .command

                // And the manifest-only project must still be ACCEPTED (not rejected
                // because the object-shaped build threw during parse).
                var p = DecompProjectDetector.Detect(dir);
                Assert.NotNull(p);
            }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void ParseManifest_ScalarBuildAndElf_SurfacedAsStrings()
        {
            string dir = NewTempDir();
            try
            {
                WriteFile(dir, DecompProject.ManifestFileName,
                    "{ \"schemaVersion\": 1, \"builtRom\": \"synth.gba\", \"build\": \"make all\", \"elf\": \"synth.elf\" }");
                var m = DecompProjectDetector.ParseManifest(Path.Combine(dir, DecompProject.ManifestFileName));
                Assert.NotNull(m);
                Assert.Equal("make all", m.BuildCommand);
                Assert.Equal("synth.elf", m.ElfPath);
            }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void ParseManifest_ObjectShapedElfMapSym_StillParsesNoThrow()
        {
            string dir = NewTempDir();
            try
            {
                WriteFile(dir, DecompProject.ManifestFileName,
                    "{ \"schemaVersion\": 1, \"builtRom\": \"synth.gba\", \"elf\": { \"path\": \"a.elf\" }, \"map\": [1,2], \"sym\": { } }");
                var m = DecompProjectDetector.ParseManifest(Path.Combine(dir, DecompProject.ManifestFileName));
                Assert.NotNull(m);
                // Non-string shapes surface as null (no throw).
                Assert.Null(m.ElfPath);
                Assert.Null(m.MapPath);
                Assert.Null(m.SymPath);
            }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void ParseManifest_Missing_ReturnsNullNoThrow()
        {
            Assert.Null(DecompProjectDetector.ParseManifest(null));
            Assert.Null(DecompProjectDetector.ParseManifest(""));
            Assert.Null(DecompProjectDetector.ParseManifest(Path.Combine(Path.GetTempPath(), "no_such_" + Guid.NewGuid().ToString("N") + ".json")));
        }

        // ---- ParseMakefileRomStem -------------------------------------------

        [Theory]
        [InlineData("ROM := mygame.gba\n", "mygame")]
        [InlineData("ROM = mygame\n", "mygame")]
        [InlineData("BUILD_NAME := fe8u.gba\n", "fe8u")]
        [InlineData("  ROM := spaced.gba\n", "spaced")]
        public void ParseMakefileRomStem_Variants(string makefile, string expectedStem)
        {
            string dir = NewTempDir();
            try
            {
                WriteFile(dir, "Makefile", makefile);
                Assert.Equal(expectedStem, DecompProjectDetector.ParseMakefileRomStem(dir));
            }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void ParseMakefileRomStem_NoRomLine_ReturnsNull()
        {
            string dir = NewTempDir();
            try
            {
                WriteFile(dir, "Makefile", "all:\n\tgcc\n");
                Assert.Null(DecompProjectDetector.ParseMakefileRomStem(dir));
            }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void ParseMakefileRomStem_VariableReference_ReturnsNull()
        {
            string dir = NewTempDir();
            try
            {
                WriteFile(dir, "Makefile", "ROM := $(NAME).gba\n");
                Assert.Null(DecompProjectDetector.ParseMakefileRomStem(dir));
            }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void ParseMakefileRomStem_RootedStem_ReturnsNull()
        {
            // A rooted stem would let Path.Combine ignore the project root, escaping
            // the containment rule — reject it (Copilot PR #1136 finding).
            string dir = NewTempDir();
            try
            {
                string rooted = OperatingSystem.IsWindows() ? @"C:\out.gba" : "/tmp/out.gba";
                WriteFile(dir, "Makefile", "ROM := " + rooted + "\n");
                Assert.Null(DecompProjectDetector.ParseMakefileRomStem(dir));
            }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void Resolve_RootedMakefileStem_NotBuiltNoEscape()
        {
            // Even if a rooted ROM := line points at an existing absolute .gba, the
            // resolver must NOT load it from outside the project (containment).
            string outside = Path.Combine(Path.GetTempPath(), "decomp_outside_" + Guid.NewGuid().ToString("N") + ".gba");
            File.WriteAllBytes(outside, new byte[] { 0, 1, 2, 3 });
            string dir = NewTempDir();
            try
            {
                string rooted = Path.ChangeExtension(outside, null); // strip .gba → stem
                WriteFile(dir, "Makefile", "ROM := " + rooted + "\n");
                WriteFile(dir, "x.sha1", "00\n");  // score 2 → accepted as a project

                var p = DecompProjectDetector.Detect(dir);
                Assert.NotNull(p);
                var r = DecompProjectDetector.ResolveBuiltRom(dir, p);
                Assert.Equal(DecompResolveStatus.NotBuilt, r.Status);
            }
            finally
            {
                try { File.Delete(outside); } catch { }
                Directory.Delete(dir, true);
            }
        }

        // ---- CoreState.IsDecompMode regression -------------------------------

        [Fact]
        public void CoreState_IsDecompMode_TogglesWithProject()
        {
            var prev = CoreState.DecompProject;
            try
            {
                CoreState.DecompProject = null;
                Assert.False(CoreState.IsDecompMode);

                CoreState.DecompProject = new DecompProject { ProjectRoot = "x", BuiltRomPath = "y.gba" };
                Assert.True(CoreState.IsDecompMode);
            }
            finally
            {
                CoreState.DecompProject = prev;
            }
        }

        [Fact]
        public void DecompProject_IsBuilt_TrueOnlyWhenFileExists()
        {
            string dir = NewTempDir();
            try
            {
                TouchGba(dir, "real.gba");
                var p = new DecompProject { ProjectRoot = dir, BuiltRomPath = Path.Combine(dir, "real.gba") };
                Assert.True(p.IsBuilt);

                p.BuiltRomPath = Path.Combine(dir, "missing.gba");
                Assert.False(p.IsBuilt);

                p.BuiltRomPath = "";
                Assert.False(p.IsBuilt);
            }
            finally { Directory.Delete(dir, true); }
        }
    }
}
