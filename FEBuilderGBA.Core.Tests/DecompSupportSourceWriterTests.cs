using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using FEBuilderGBA;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    /// <summary>
    /// Unit tests for #1149 — entry-id resolvers and decomp source-write routing for
    /// support_units, support_attributes, and support_talks tables.
    ///
    /// All ROM-requiring tests are skipped when no test ROM is available.
    /// The source-write routing tests use synthetic temp-dir projects (no ROM needed).
    /// </summary>
    [Collection("SharedState")]
    public class DecompSupportSourceWriterTests : IDisposable
    {
        readonly DecompProject? _savedProject;

        public DecompSupportSourceWriterTests()
        {
            _savedProject = CoreState.DecompProject;
            CoreState.DecompProject = null;
        }

        public void Dispose()
        {
            CoreState.DecompProject = null;
            CoreState.DecompProject = _savedProject;
        }

        // ---- helpers ----

        static string NewTempDir()
        {
            string d = Path.Combine(Path.GetTempPath(), "decomp_support_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(d);
            return d;
        }

        static DecompTableEntry SupportUnitsOwner(string sourceFile)
        {
            return new DecompTableEntry
            {
                Table = "support_units",
                Format = "cstruct",
                WritePolicy = "source",
                ArrayName = "gSupportData",
                SourceFile = sourceFile,
                Fields = new List<DecompTableField>
                {
                    new DecompTableField { Name = "b0" },
                    new DecompTableField { Name = "b1" },
                    new DecompTableField { Name = "b7" },
                    new DecompTableField { Name = "b14" },
                    new DecompTableField { Name = "b21" },
                },
            };
        }

        static DecompTableEntry SupportAttributesOwner(string sourceFile)
        {
            return new DecompTableEntry
            {
                Table = "support_attributes",
                Format = "cstruct",
                WritePolicy = "source",
                ArrayName = "gAffinityData",
                SourceFile = sourceFile,
                Fields = new List<DecompTableField>
                {
                    new DecompTableField { Name = "b0" },
                    new DecompTableField { Name = "b1" },
                    new DecompTableField { Name = "b2" },
                    new DecompTableField { Name = "b3" },
                    new DecompTableField { Name = "b4" },
                    new DecompTableField { Name = "b5" },
                    new DecompTableField { Name = "b6" },
                    new DecompTableField { Name = "b7" },
                },
            };
        }

        static DecompTableEntry SupportTalksOwner(string sourceFile)
        {
            return new DecompTableEntry
            {
                Table = "support_talks",
                Format = "cstruct",
                WritePolicy = "source",
                ArrayName = "gSupportConvos",
                SourceFile = sourceFile,
                Fields = new List<DecompTableField>
                {
                    new DecompTableField { Name = "b0" },
                    new DecompTableField { Name = "b2" },
                    new DecompTableField { Name = "w4" },
                    new DecompTableField { Name = "w6" },
                },
            };
        }

        static DecompProject ProjectWith(string root, params DecompTableEntry[] owners)
        {
            var man = new DecompManifest();
            var arr = JsonSerializer.SerializeToElement(owners);
            man.Tables = arr;
            return new DecompProject { ProjectRoot = root, Manifest = man };
        }

        // ---- TryGetTableOwner contract ----

        [Fact]
        public void TryGetTableOwner_SupportUnits_ReturnsOwnerWhenDeclared()
        {
            string dir = NewTempDir();
            try
            {
                string src = Path.Combine(dir, "support.c");
                File.WriteAllText(src, "// stub\n");
                var project = ProjectWith(dir, SupportUnitsOwner("support.c"));

                var owner = project.TryGetTableOwner("support_units");
                Assert.NotNull(owner);
                Assert.Equal("support_units", owner!.Table);
            }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void TryGetTableOwner_SupportAttributes_ReturnsOwnerWhenDeclared()
        {
            string dir = NewTempDir();
            try
            {
                string src = Path.Combine(dir, "affinity.c");
                File.WriteAllText(src, "// stub\n");
                var project = ProjectWith(dir, SupportAttributesOwner("affinity.c"));

                var owner = project.TryGetTableOwner("support_attributes");
                Assert.NotNull(owner);
                Assert.Equal("support_attributes", owner!.Table);
            }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void TryGetTableOwner_SupportTalks_ReturnsOwnerWhenDeclared()
        {
            string dir = NewTempDir();
            try
            {
                string src = Path.Combine(dir, "talks.c");
                File.WriteAllText(src, "// stub\n");
                var project = ProjectWith(dir, SupportTalksOwner("talks.c"));

                var owner = project.TryGetTableOwner("support_talks");
                Assert.NotNull(owner);
                Assert.Equal("support_talks", owner!.Table);
            }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void TryGetTableOwner_SupportUnits_ReturnsNullWhenNotDeclared()
        {
            string dir = NewTempDir();
            try
            {
                // Project with only items — support_units not declared
                var itemsOwner = new DecompTableEntry
                {
                    Table = "items",
                    Format = "cstruct",
                    WritePolicy = "source",
                    ArrayName = "gItemData",
                    SourceFile = "item.c",
                };
                var project = ProjectWith(dir, itemsOwner);

                var owner = project.TryGetTableOwner("support_units");
                Assert.Null(owner);
            }
            finally { Directory.Delete(dir, true); }
        }

        // ---- WriteTableEntry returns correct status ----

        [Fact]
        public void WriteTableEntry_SupportUnits_WritesOk_WhenOwnerDeclared()
        {
            string dir = NewTempDir();
            try
            {
                // Minimal C source with a 3-element support array
                string src = Path.Combine(dir, "support.c");
                File.WriteAllText(src,
                    "const SupportData gSupportData[] = {\n" +
                    "    {0x06, 0x07, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00},\n" +
                    "    {0x08, 0x09, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00},\n" +
                    "};\n");

                var owner = SupportUnitsOwner("support.c");
                var project = ProjectWith(dir, owner);
                // WriteTableEntry checks CoreState.IsDecompMode (via DecompProject), so set it.
                CoreState.DecompProject = project;

                var changed = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase)
                {
                    { "b0", 0x11 },
                };

                var res = DecompSourceWriterCore.WriteTableEntry(project, "support_units", 0, changed);
                // The stub source is a valid positional cstruct → deterministic strict success.
                // (This is the same DecompSourceWriterCore.WriteTableEntry path the CLI/E2E uses,
                // so it locks in the hex re-emit the E2E test asserts.)
                Assert.Equal(DecompSourceWriteStatus.Ok, res.Status);

                // b0 was 0x06; the writer preserves the hex radix and emits the shortest form
                // (ToString("X")), so 0x06 -> 0x11 here. b1 (0x07) is untouched. Row 1 (id 1) is
                // left byte-identical.
                string after = File.ReadAllText(src);
                Assert.Contains("{0x11, 0x07, 0x00", after);   // b0 rewritten, b1 preserved
                Assert.DoesNotContain("{0x06,", after);        // old token gone
                Assert.Contains("{0x08, 0x09, 0x00", after);   // id-1 row untouched
            }
            finally
            {
                CoreState.DecompProject = null;
                Directory.Delete(dir, true);
            }
        }

        // ---- #1159 finding 4 (re-review): macro-token field reported in SkippedFields ----
        // A bulk (multi-field) write where one DECLARED field's source token is a MACRO
        // (not a plain integer literal) writes only the numeric field(s), returns Ok, and
        // REPORTS the macro field in SkippedFields. The support Views treat a non-empty
        // SkippedFields as a PARTIAL (failed all-or-nothing) save — they must NOT MarkClean.
        [Fact]
        public void WriteTableEntry_SupportUnits_MacroField_ReportedInSkippedFields()
        {
            string dir = NewTempDir();
            try
            {
                // Element 0's b0 slot is a MACRO token (SOME_MACRO); b1 is a plain literal.
                string src = Path.Combine(dir, "support.c");
                File.WriteAllText(src,
                    "const SupportData gSupportData[] = {\n" +
                    "    {SOME_MACRO, 0x07, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00},\n" +
                    "};\n");

                // Owner declares both b0 and b1.
                var owner = new DecompTableEntry
                {
                    Table = "support_units",
                    Format = "cstruct",
                    WritePolicy = "source",
                    ArrayName = "gSupportData",
                    SourceFile = "support.c",
                    Fields = new List<DecompTableField>
                    {
                        new DecompTableField { Name = "b0" },
                        new DecompTableField { Name = "b1" },
                    },
                };
                var project = ProjectWith(dir, owner);
                CoreState.DecompProject = project;

                // Change BOTH b0 (the macro slot — unwritable) and b1 (numeric — writable).
                var changed = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase)
                {
                    { "b0", 0x11 },
                    { "b1", 0x22 },
                };

                var res = DecompSourceWriterCore.WriteTableEntry(project, "support_units", 0, changed);

                // Ok status, but PARTIAL: only b1 written, b0 skipped (macro).
                Assert.Equal(DecompSourceWriteStatus.Ok, res.Status);
                Assert.Contains("b1", res.ChangedFields);
                Assert.DoesNotContain("b0", res.ChangedFields);
                Assert.NotNull(res.SkippedFields);
                Assert.Contains("b0", res.SkippedFields);
                Assert.DoesNotContain("b1", res.SkippedFields);

                // The macro token is preserved verbatim; b1 was rewritten to 0x22.
                string after = File.ReadAllText(src);
                Assert.Contains("{SOME_MACRO, 0x22, 0x00", after);
            }
            finally
            {
                CoreState.DecompProject = null;
                Directory.Delete(dir, true);
            }
        }

        // #1159 (re-review): an ALL-SKIPPED bulk write (no field writable) returns Ok with
        // empty ChangedFields + non-empty SkippedFields, and the message says "skipped" —
        // NOT the misleading "No change needed." (the edits were unwritable, not equal).
        [Fact]
        public void WriteTableEntry_SupportUnits_AllSkipped_MessageSaysSkipped_NotNoChange()
        {
            string dir = NewTempDir();
            try
            {
                // BOTH b0 and b1 slots are MACRO tokens (unwritable).
                string src = Path.Combine(dir, "support.c");
                const string original =
                    "const SupportData gSupportData[] = {\n" +
                    "    {MACRO_A, MACRO_B, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00},\n" +
                    "};\n";
                File.WriteAllText(src, original);

                var owner = new DecompTableEntry
                {
                    Table = "support_units",
                    Format = "cstruct",
                    WritePolicy = "source",
                    ArrayName = "gSupportData",
                    SourceFile = "support.c",
                    Fields = new List<DecompTableField>
                    {
                        new DecompTableField { Name = "b0" },
                        new DecompTableField { Name = "b1" },
                    },
                };
                var project = ProjectWith(dir, owner);
                CoreState.DecompProject = project;

                // Change both — both are macros → both skipped, nothing written.
                var changed = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase)
                {
                    { "b0", 0x11 },
                    { "b1", 0x22 },
                };

                var res = DecompSourceWriterCore.WriteTableEntry(project, "support_units", 0, changed);

                Assert.Equal(DecompSourceWriteStatus.Ok, res.Status);
                Assert.Empty(res.ChangedFields);
                Assert.NotNull(res.SkippedFields);
                Assert.Equal(2, res.SkippedFields.Count);
                Assert.Contains("b0", res.SkippedFields);
                Assert.Contains("b1", res.SkippedFields);
                // The honest message names "skipped", not "No change needed".
                Assert.Contains("skipped", res.Message, System.StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("No change needed", res.Message);
                // No byte changed — both macro tokens preserved verbatim.
                Assert.Equal(original, File.ReadAllText(src));
            }
            finally
            {
                CoreState.DecompProject = null;
                Directory.Delete(dir, true);
            }
        }

        // No-op vs skip: a field whose value already equals the requested value is a
        // legitimate NO-OP — it must appear in NEITHER ChangedFields NOR SkippedFields.
        [Fact]
        public void WriteTableEntry_SupportUnits_NoOpField_NotInChangedNorSkipped()
        {
            string dir = NewTempDir();
            try
            {
                string src = Path.Combine(dir, "support.c");
                File.WriteAllText(src,
                    "const SupportData gSupportData[] = {\n" +
                    "    {0x06, 0x07, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00},\n" +
                    "};\n");

                var owner = new DecompTableEntry
                {
                    Table = "support_units",
                    Format = "cstruct",
                    WritePolicy = "source",
                    ArrayName = "gSupportData",
                    SourceFile = "support.c",
                    Fields = new List<DecompTableField>
                    {
                        new DecompTableField { Name = "b0" },
                        new DecompTableField { Name = "b1" },
                    },
                };
                var project = ProjectWith(dir, owner);
                CoreState.DecompProject = project;

                // b0 changes (0x06->0x11); b1 is set to its CURRENT value (0x07) → no-op.
                var changed = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase)
                {
                    { "b0", 0x11 },
                    { "b1", 0x07 },
                };

                var res = DecompSourceWriterCore.WriteTableEntry(project, "support_units", 0, changed);

                Assert.Equal(DecompSourceWriteStatus.Ok, res.Status);
                Assert.Contains("b0", res.ChangedFields);
                Assert.DoesNotContain("b1", res.ChangedFields);   // no-op, not changed
                Assert.NotNull(res.SkippedFields);
                Assert.DoesNotContain("b1", res.SkippedFields);    // no-op, not skipped
                Assert.Empty(res.SkippedFields);
            }
            finally
            {
                CoreState.DecompProject = null;
                Directory.Delete(dir, true);
            }
        }

        [Fact]
        public void WriteTableEntry_SupportTalks_ReturnsNotOwned_WhenNoManifestEntry()
        {
            string dir = NewTempDir();
            try
            {
                // Empty project — no tables declared
                var project = ProjectWith(dir);
                CoreState.DecompProject = project;
                var changed = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase) { { "w4", 0x1A0 } };
                var res = DecompSourceWriterCore.WriteTableEntry(project, "support_talks", 3, changed);
                Assert.Equal(DecompSourceWriteStatus.NotOwned, res.Status);
            }
            finally
            {
                CoreState.DecompProject = null;
                Directory.Delete(dir, true);
            }
        }

        [Fact]
        public void WriteTableEntry_SupportAttributes_ReturnsNotOwned_WhenNoManifestEntry()
        {
            string dir = NewTempDir();
            try
            {
                var project = ProjectWith(dir);
                CoreState.DecompProject = project;
                var changed = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase) { { "b1", 5 } };
                var res = DecompSourceWriterCore.WriteTableEntry(project, "support_attributes", 2, changed);
                Assert.Equal(DecompSourceWriteStatus.NotOwned, res.Status);
            }
            finally
            {
                CoreState.DecompProject = null;
                Directory.Delete(dir, true);
            }
        }

        // ---- #1149 finding 4b: the writer rejects fields the owner does not declare ----
        // The View intersection gate drops undeclared fields BEFORE calling the writer, but
        // if a caller bypasses the gate the writer must still refuse (validate-all): an owner
        // declaring ONLY b7, given a changed dict with the UNDECLARED field b0, must return
        // UnsupportedField with NO source write.
        [Fact]
        public void WriteTableEntry_SupportUnits_UndeclaredField_ReturnsUnsupportedField_NoWrite()
        {
            string dir = NewTempDir();
            try
            {
                string src = Path.Combine(dir, "support.c");
                const string original =
                    "const SupportData gSupportData[] = {\n" +
                    "    {0x06, 0x07, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00},\n" +
                    "};\n";
                File.WriteAllText(src, original);

                // Owner declares ONLY b7.
                var owner = new DecompTableEntry
                {
                    Table = "support_units",
                    Format = "cstruct",
                    WritePolicy = "source",
                    ArrayName = "gSupportData",
                    SourceFile = "support.c",
                    Fields = new List<DecompTableField> { new DecompTableField { Name = "b7" } },
                };
                var project = ProjectWith(dir, owner);
                CoreState.DecompProject = project;

                // Changed dict contains the UNDECLARED field b0.
                var changed = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase) { { "b0", 0x11 } };
                var res = DecompSourceWriterCore.WriteTableEntry(project, "support_units", 0, changed);

                Assert.Equal(DecompSourceWriteStatus.UnsupportedField, res.Status);
                // No write occurred — the source file is byte-identical.
                Assert.Equal(original, File.ReadAllText(src));
            }
            finally
            {
                CoreState.DecompProject = null;
                Directory.Delete(dir, true);
            }
        }

        // ---- SupportUnitNavigation resolver guards ----

        [Fact]
        public void GetSupportUnitEntryIdFromAddr_NullRom_ReturnsNotFound()
        {
            uint result = SupportUnitNavigation.GetSupportUnitEntryIdFromAddr(null!, 0x100, 24);
            Assert.Equal(U.NOT_FOUND, result);
        }

        [Fact]
        public void GetSupportAttributeEntryIdFromAddr_NullRom_ReturnsNotFound()
        {
            uint result = SupportUnitNavigation.GetSupportAttributeEntryIdFromAddr(null!, 0x100);
            Assert.Equal(U.NOT_FOUND, result);
        }

        [Fact]
        public void GetSupportTalkEntryIdFromAddr_NullRom_ReturnsNotFound()
        {
            uint result = SupportUnitNavigation.GetSupportTalkEntryIdFromAddr(null!, 0x100, 16);
            Assert.Equal(U.NOT_FOUND, result);
        }

        // ---- loaded-ROM resolver coverage (#1159 finding 2) ----
        // The ZeroBlockSize guard sits AFTER the rom-null check, so it can only be
        // exercised with a LOADED ROM. Build a synthetic FE8U ROM (same MakeRom pattern
        // as SupportUnitNavigationTests), plant the support-table base pointer, and drive
        // the resolver through its real arithmetic guards.

        static void WriteU32(byte[] data, uint addr, uint value)
        {
            int i = checked((int)addr);
            data[i + 0] = (byte)(value & 0xFF);
            data[i + 1] = (byte)((value >> 8) & 0xFF);
            data[i + 2] = (byte)((value >> 16) & 0xFF);
            data[i + 3] = (byte)((value >> 24) & 0xFF);
        }

        static ROM MakeRom(string sig = "BE8E01")
        {
            var rom = new ROM();
            rom.LoadLow("test.gba", new byte[0x1000000], sig);
            Assert.NotNull(rom.RomInfo);
            return rom;
        }

        [Fact]
        public void GetSupportUnitEntryIdFromAddr_LoadedRom_ZeroBlockSize_ReturnsNotFound()
        {
            // Loaded ROM + valid in-range addr → the only thing that fails is blockSize==0,
            // proving the post-null-check blockSize guard fires (not the rom-null guard).
            var rom = MakeRom("BE8E01");
            uint baseAddr = 0x300000;
            WriteU32(rom.Data, rom.RomInfo.support_unit_pointer, baseAddr | 0x08000000);

            uint result = SupportUnitNavigation.GetSupportUnitEntryIdFromAddr(rom, baseAddr, 0);
            Assert.Equal(U.NOT_FOUND, result);
        }

        [Fact]
        public void GetSupportTalkEntryIdFromAddr_LoadedRom_ZeroBlockSize_ReturnsNotFound()
        {
            var rom = MakeRom("BE8E01");
            uint baseAddr = 0x300000;
            WriteU32(rom.Data, rom.RomInfo.support_talk_pointer, baseAddr | 0x08000000);

            uint result = SupportUnitNavigation.GetSupportTalkEntryIdFromAddr(rom, baseAddr, 0);
            Assert.Equal(U.NOT_FOUND, result);
        }

        [Fact]
        public void GetSupportUnitEntryIdFromAddr_LoadedRom_ComputesEntryId()
        {
            // Plant the table base, then ask for entry 3 at base + 3*24 → must return 3.
            var rom = MakeRom("BE8E01");
            uint baseAddr = 0x300000;
            const uint block = 24;
            WriteU32(rom.Data, rom.RomInfo.support_unit_pointer, baseAddr | 0x08000000);

            uint id = SupportUnitNavigation.GetSupportUnitEntryIdFromAddr(rom, baseAddr + 3 * block, block);
            Assert.Equal(3u, id);
        }

        [Fact]
        public void GetSupportUnitEntryIdFromAddr_LoadedRom_MisalignedAddr_ReturnsNotFound()
        {
            var rom = MakeRom("BE8E01");
            uint baseAddr = 0x300000;
            const uint block = 24;
            WriteU32(rom.Data, rom.RomInfo.support_unit_pointer, baseAddr | 0x08000000);

            // base + 5 is not a multiple of the 24-byte block → misaligned guard.
            uint id = SupportUnitNavigation.GetSupportUnitEntryIdFromAddr(rom, baseAddr + 5, block);
            Assert.Equal(U.NOT_FOUND, id);
        }

        [Fact]
        public void GetSupportUnitEntryIdFromAddr_LoadedRom_AddrBelowBase_ReturnsNotFound()
        {
            var rom = MakeRom("BE8E01");
            uint baseAddr = 0x300000;
            const uint block = 24;
            WriteU32(rom.Data, rom.RomInfo.support_unit_pointer, baseAddr | 0x08000000);

            // Address below the planted table base → below-base guard.
            uint id = SupportUnitNavigation.GetSupportUnitEntryIdFromAddr(rom, baseAddr - block, block);
            Assert.Equal(U.NOT_FOUND, id);
        }

        [Fact]
        public void GetSupportAttributeEntryIdFromAddr_LoadedRom_ComputesEntryId()
        {
            // support_attributes block is a fixed 8 bytes.
            var rom = MakeRom("BE8E01");
            uint baseAddr = 0x300000;
            WriteU32(rom.Data, rom.RomInfo.support_attribute_pointer, baseAddr | 0x08000000);

            uint id = SupportUnitNavigation.GetSupportAttributeEntryIdFromAddr(rom, baseAddr + 2 * 8);
            Assert.Equal(2u, id);
        }

        [Fact]
        public void GetSupportTalkEntryIdFromAddr_LoadedRom_ComputesEntryId()
        {
            var rom = MakeRom("BE8E01");
            uint baseAddr = 0x300000;
            const uint block = 16;
            WriteU32(rom.Data, rom.RomInfo.support_talk_pointer, baseAddr | 0x08000000);

            uint id = SupportUnitNavigation.GetSupportTalkEntryIdFromAddr(rom, baseAddr + 4 * block, block);
            Assert.Equal(4u, id);
        }
    }
}
