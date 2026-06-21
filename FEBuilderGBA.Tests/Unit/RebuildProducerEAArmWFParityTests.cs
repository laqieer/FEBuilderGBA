using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using FEBuilderGBA;
using Xunit;

namespace FEBuilderGBA.Tests.Unit
{
    /// <summary>
    /// BYTE-PARITY validation for the #1261 slice s2pf-14 TYPE=EA producer arm
    /// (<see cref="RebuildProducerCore.EmitPatchEA"/> = WF
    /// <c>PatchForm.MakePatchStructDataListForEA</c>, PatchForm.cs:6259).
    ///
    /// <para><b>Why a SYNTHETIC installed-EA scenario.</b> A freshly-loaded VANILLA FE8U
    /// has ZERO installed TYPE=EA patches (proven by the sibling
    /// <c>CorePatchProducer_IsStrictSubsetOf_WinFormsPatchProducer_EaBinGapDocumented</c> —
    /// its <c>eaBinPatchCount</c> is 0), so the real ROM cannot exercise the EA arm. To get
    /// an "installed EA patch" deterministically we PLANT the patch bytes into a synthetic
    /// FE8 ROM at known addresses (a valid PROCS table, a unique BIN pattern, and an ORG
    /// anchor), author a matching <c>.event</c> + a <c>{TYPE=EA, EA=...}</c> patch, and run
    /// BOTH producers against that exact ROM:</para>
    /// <list type="bullet">
    ///   <item>WF: <c>PatchForm.MakePatchStructDataListForEA(list, isPointerOnly, patch)</c>
    ///   (private static — reached via reflection; it reads <c>Program.ROM</c>, which we set
    ///   to the synthetic ROM).</item>
    ///   <item>Core: <see cref="RebuildProducerCore.EmitPatchEA"/> on the same ROM.</item>
    /// </list>
    /// <para>The two emitted lists are then asserted EQUAL on the load-bearing fields
    /// (Addr/Length/Pointer/DataType) — including the PROCS entry the producer emits (WF via
    /// <c>ProcsScriptForm.CalcLengthAndCheck</c>, Core via the verbatim
    /// <c>CalcProcsLengthAndCheck</c>) and the BIN/ORG entries the shared s2pf-13 walker
    /// reconstructs. Info/name is cosmetic and not compared.</para>
    ///
    /// <para>This test lives in <c>FEBuilderGBA.Tests</c> (net9.0-windows) because it calls
    /// the WinForms <c>PatchForm</c>, which does not exist in the net9.0 Core assembly. It
    /// SKIPS (early-return = Pass) if the WF reflective hooks are unavailable, so it never
    /// breaks CI on a layout change. It mutates global <c>Program.ROM</c>/<c>CoreState.ROM</c>,
    /// so it is in the <c>SharedState</c> collection.</para>
    /// </summary>
    [Collection("SharedState")]
    public class RebuildProducerEAArmWFParityTests
    {
        // 16 MiB synthetic FE8 ROM, BE8E01 — RomInfo-bearing so both producers' GREP seed +
        // PROCS length detector work. Sets Program.ROM (reflection) AND CoreState.ROM.
        static ROM MakeFe8RomAndInstall()
        {
            var data = new byte[0x1000000];
            byte[] code = System.Text.Encoding.ASCII.GetBytes("BE8E01");
            Array.Copy(code, 0, data, 0xAC, code.Length);

            var rom = new ROM();
            bool ok = rom.LoadLow("synthetic-FE8.gba", data, "BE8E01");
            if (!ok) return null;
            CoreState.ROM = rom;
            return SetProgramRom(rom) ? rom : null;
        }

        // Program.ROM has a private setter — set it via reflection for this headless WF test.
        static bool SetProgramRom(ROM rom)
        {
            try
            {
                PropertyInfo p = typeof(Program).GetProperty(
                    "ROM", BindingFlags.Public | BindingFlags.Static);
                if (p == null) return false;
                p.SetValue(null, rom, BindingFlags.NonPublic | BindingFlags.Instance, null, null, null);
                return ReferenceEquals(Program.ROM, rom);
            }
            catch
            {
                return false;
            }
        }

        // Build a WinForms PatchForm.PatchSt mirroring the Core PatchInstallCore.PatchSt.
        static PatchForm.PatchSt MakeWfPatch(string dir, string name, string eaFileName,
            params (string key, string value)[] extra)
        {
            var p = new PatchForm.PatchSt
            {
                Name = name,
                PatchFileName = Path.Combine(dir, "PATCH_" + name + ".txt"),
                Param = new Dictionary<string, string>(),
            };
            p.Param["TYPE"] = "EA";
            p.Param["EA"] = eaFileName;
            foreach (var (k, v) in extra) p.Param[k] = v;
            return p;
        }

        static PatchInstallCore.PatchSt MakeCorePatch(string dir, string name, string eaFileName,
            params (string key, string value)[] extra)
        {
            var p = new PatchInstallCore.PatchSt
            {
                Name = name,
                PatchFileName = Path.Combine(dir, "PATCH_" + name + ".txt"),
                Param = new Dictionary<string, string>(),
            };
            p.Param["TYPE"] = "EA";
            p.Param["EA"] = eaFileName;
            foreach (var (k, v) in extra) p.Param[k] = v;
            return p;
        }

        // Call the private static WF PatchForm.MakePatchStructDataListForEA via reflection.
        // Returns null if the method is not found (→ test skips).
        static List<Address> CallWfMakePatchStructDataListForEA(List<Address> list, bool isPointerOnly, PatchForm.PatchSt patch)
        {
            MethodInfo m = typeof(PatchForm).GetMethod(
                "MakePatchStructDataListForEA",
                BindingFlags.NonPublic | BindingFlags.Static);
            if (m == null) return null;
            m.Invoke(null, new object[] { list, isPointerOnly, patch });
            return list;
        }

        readonly struct Key : IEquatable<Key>
        {
            public readonly uint Addr;
            public readonly uint Length;
            public readonly uint Pointer;
            public readonly Address.DataTypeEnum Type;
            Key(uint a, uint l, uint p, Address.DataTypeEnum t) { Addr = a; Length = l; Pointer = p; Type = t; }
            public static Key Of(Address a) => new Key(a.Addr, a.Length, a.Pointer, a.DataType);
            public bool Equals(Key o) => Addr == o.Addr && Length == o.Length && Pointer == o.Pointer && Type == o.Type;
            public override bool Equals(object o) => o is Key k && Equals(k);
            public override int GetHashCode() => HashCode.Combine(Addr, Length, Pointer, (int)Type);
        }

        static string Dump(IEnumerable<Key> keys)
        {
            return string.Join("\n", keys.Take(30).Select(k =>
                $"  Addr=0x{k.Addr:X} Len=0x{k.Length:X} Ptr=0x{k.Pointer:X} Type={k.Type}"));
        }

        [Fact]
        public void CoreEmitPatchEA_MatchesWinForms_OnInstalledEaRom_OrgBinProcs()
        {
            ROM savedCore = CoreState.ROM;
            ROM savedProg = Program.ROM;
            string dir = Path.Combine(Path.GetTempPath(), "ea-wfparity-" + Path.GetRandomFileName());
            Directory.CreateDirectory(dir);
            try
            {
                var rom = MakeFe8RomAndInstall();
                if (rom == null) return; // WF Program.ROM hook unavailable — SKIP (Pass)

                // ---- PLANT the "installed EA patch" bytes at known addresses ----
                const uint orgAddr = 0x800000;     // ORG anchor → lastMatchAddr
                const uint procsAdd = 0x1000;
                const uint advanced = orgAddr + procsAdd;   // PROCS resolves here (0x801000)
                // A valid PROCS EXIT (code=0,sarg=0,parg=0) → length 8 on both producers.
                for (int i = 0; i < 8; i++) rom.write_u8(advanced + (uint)i, 0x00);
                // A unique BIN pattern after the PROCS region.
                byte[] pattern = { 0xCA, 0xFE, 0xBA, 0xBE, 0x12, 0x34, 0x56, 0x78 };
                const uint binAddr = advanced + 0x4000;     // 0x805000
                for (int i = 0; i < pattern.Length; i++) rom.write_u8(binAddr + (uint)i, pattern[i]);

                File.WriteAllBytes(Path.Combine(dir, "blk.bin"), pattern);
                File.WriteAllText(Path.Combine(dir, "main.event"),
                    "ORG 0x" + orgAddr.ToString("X") + "\r\n" +
                    "procs_label: // HINT=PROCS ADD=" + procsAdd.ToString() + "\r\n" +
                    "#incbin \"blk.bin\" // HINT=BIN\r\n");

                // ---- WF reference (private static, via reflection; reads Program.ROM) ----
                var wf = new List<Address>();
                var wfRet = CallWfMakePatchStructDataListForEA(wf,
                    false, MakeWfPatch(dir, "EAp", "main.event"));
                if (wfRet == null) return; // WF method not found — SKIP (Pass)

                // ---- Core: EmitPatchEA on the same ROM ----
                var core = new List<Address>();
                RebuildProducerCore.EmitPatchEA(rom, core,
                    MakeCorePatch(dir, "EAp", "main.event"), isPointerOnly: false);

                Assert.NotEmpty(wf);
                Assert.NotEmpty(core);

                var wfKeys = new HashSet<Key>(wf.Select(Key.Of));
                var coreKeys = new HashSet<Key>(core.Select(Key.Of));

                var coreExtras = core.Where(a => !wfKeys.Contains(Key.Of(a))).Select(Key.Of).Distinct().ToList();
                var wfExtras = wf.Where(a => !coreKeys.Contains(Key.Of(a))).Select(Key.Of).Distinct().ToList();

                if (coreExtras.Count > 0 || wfExtras.Count > 0)
                {
                    Assert.Fail(
                        "Core EmitPatchEA diverges from WF MakePatchStructDataListForEA on an installed-EA ROM.\n"
                        + $"WF total={wf.Count}, Core total={core.Count}.\n"
                        + $"Core-only (Core emits, WF does not) [{coreExtras.Count}]:\n{Dump(coreExtras)}\n"
                        + $"WF-only (Core dropped) [{wfExtras.Count}]:\n{Dump(wfExtras)}");
                }

                // PROVEN: byte-identical on (Addr/Length/Pointer/DataType).
                Assert.Empty(coreExtras);
                Assert.Empty(wfExtras);

                // And the PROCS entry IS present on both (the producer-only emit the uninstall
                // path skips) — pin it explicitly so a future regression that drops PROCS on
                // BOTH sides cannot pass the set-equality vacuously.
                Assert.Contains(core, a => a.DataType == Address.DataTypeEnum.PROCS && a.Addr == advanced && a.Length == 8);
                Assert.Contains(wf, a => a.DataType == Address.DataTypeEnum.PROCS && a.Addr == advanced && a.Length == 8);
                // The BIN entry matched the planted pattern on both.
                Assert.Contains(core, a => a.Addr == binAddr);
                Assert.Contains(wf, a => a.Addr == binAddr);
            }
            finally
            {
                CoreState.ROM = savedCore;
                SetProgramRom(savedProg);
                try { Directory.Delete(dir, true); } catch { }
            }
        }

        // isPointerOnly=true → the PROCS/EA lengths become 0 on BOTH producers; the key sets
        // must still match exactly.
        [Fact]
        public void CoreEmitPatchEA_MatchesWinForms_PointerOnly()
        {
            ROM savedCore = CoreState.ROM;
            ROM savedProg = Program.ROM;
            string dir = Path.Combine(Path.GetTempPath(), "ea-wfparity-po-" + Path.GetRandomFileName());
            Directory.CreateDirectory(dir);
            try
            {
                var rom = MakeFe8RomAndInstall();
                if (rom == null) return;

                const uint orgAddr = 0x800000;
                const uint procsAdd = 0x1000;
                const uint advanced = orgAddr + procsAdd;
                for (int i = 0; i < 8; i++) rom.write_u8(advanced + (uint)i, 0x00);

                File.WriteAllText(Path.Combine(dir, "main.event"),
                    "ORG 0x" + orgAddr.ToString("X") + "\r\n" +
                    "procs_label: // HINT=PROCS ADD=" + procsAdd.ToString() + "\r\n");

                var wf = new List<Address>();
                var wfRet = CallWfMakePatchStructDataListForEA(wf, true, MakeWfPatch(dir, "PO", "main.event"));
                if (wfRet == null) return;

                var core = new List<Address>();
                RebuildProducerCore.EmitPatchEA(rom, core, MakeCorePatch(dir, "PO", "main.event"), isPointerOnly: true);

                var wfKeys = new HashSet<Key>(wf.Select(Key.Of));
                var coreKeys = new HashSet<Key>(core.Select(Key.Of));
                var coreExtras = core.Where(a => !wfKeys.Contains(Key.Of(a))).Select(Key.Of).Distinct().ToList();
                var wfExtras = wf.Where(a => !coreKeys.Contains(Key.Of(a))).Select(Key.Of).Distinct().ToList();

                Assert.Empty(coreExtras);
                Assert.Empty(wfExtras);
                // PROCS length is 0 in pointer-only mode on both.
                Assert.Contains(core, a => a.DataType == Address.DataTypeEnum.PROCS && a.Addr == advanced && a.Length == 0);
                Assert.Contains(wf, a => a.DataType == Address.DataTypeEnum.PROCS && a.Addr == advanced && a.Length == 0);
            }
            finally
            {
                CoreState.ROM = savedCore;
                SetProgramRom(savedProg);
                try { Directory.Delete(dir, true); } catch { }
            }
        }
    }
}
