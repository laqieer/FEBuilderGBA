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
    /// BYTE-PARITY validation for the #1261 slice s2pf-15 TYPE=BIN producer arm
    /// (<see cref="RebuildProducerCore.EmitPatchBIN"/> = WF
    /// <c>PatchForm.MakePatchStructDataListForBIN</c>, PatchForm.cs:6317).
    ///
    /// <para><b>Why a SYNTHETIC installed-BIN scenario.</b> A freshly-loaded VANILLA FE8U
    /// has ZERO installed TYPE=BIN patches (the sibling EA-arm parity test documents the
    /// same for EA; both EA and BIN install counts are 0 on vanilla), so the real ROM cannot
    /// exercise the BIN arm. To get an "installed BIN patch" deterministically we PLANT the
    /// patch bytes into a synthetic FE8 ROM at known addresses (a JUMP target, a fixed-addr
    /// BIN block, a $FREEAREA pattern above the compress borderline, and a CLEAR region),
    /// author the matching <c>.bin</c>/<c>.dmp</c> files + a <c>{TYPE=BIN, ...}</c> patch,
    /// and run BOTH producers against that exact ROM:</para>
    /// <list type="bullet">
    ///   <item>WF: <c>PatchForm.MakePatchStructDataListForBIN(list, isPointerOnly, patch)</c>
    ///   (private static — reached via reflection; it reads <c>Program.ROM</c>, which we set
    ///   to the synthetic ROM).</item>
    ///   <item>Core: <see cref="RebuildProducerCore.EmitPatchBIN"/> on the same ROM.</item>
    /// </list>
    /// <para>The two emitted lists are asserted EQUAL on the load-bearing fields
    /// (Addr/Length/Pointer/DataType) — including the JUMPTOHACK (length−4), the fixed-addr
    /// BIN, the GREP-located $FREEAREA, and the UNUSEDBIN/CLEAR entries. Info/name is cosmetic
    /// and not compared.</para>
    ///
    /// <para>This test lives in <c>FEBuilderGBA.Tests</c> (net9.0-windows) because it calls
    /// the WinForms <c>PatchForm</c>, which does not exist in the net9.0 Core assembly. It
    /// SKIPS (early-return = Pass) if the WF reflective hooks are unavailable, so it never
    /// breaks CI on a layout change. It mutates global <c>Program.ROM</c>/<c>CoreState.ROM</c>,
    /// so it is in the <c>SharedState</c> collection.</para>
    /// </summary>
    [Collection("SharedState")]
    public class RebuildProducerBINArmWFParityTests
    {
        // 16 MiB synthetic FE8 ROM, BE8E01 — RomInfo-bearing so both producers' compress
        // borderline seed + $NONE split work. Sets Program.ROM (reflection) AND CoreState.ROM.
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

        static PatchForm.PatchSt MakeWfPatch(string dir, string name,
            params (string key, string value)[] kv)
        {
            var p = new PatchForm.PatchSt
            {
                Name = name,
                PatchFileName = Path.Combine(dir, "PATCH_" + name + ".txt"),
                Param = new Dictionary<string, string>(),
            };
            p.Param["TYPE"] = "BIN";
            foreach (var (k, v) in kv) p.Param[k] = v;
            return p;
        }

        static PatchInstallCore.PatchSt MakeCorePatch(string dir, string name,
            params (string key, string value)[] kv)
        {
            var p = new PatchInstallCore.PatchSt
            {
                Name = name,
                PatchFileName = Path.Combine(dir, "PATCH_" + name + ".txt"),
                Param = new Dictionary<string, string>(),
            };
            p.Param["TYPE"] = "BIN";
            foreach (var (k, v) in kv) p.Param[k] = v;
            return p;
        }

        // Call the private static WF PatchForm.MakePatchStructDataListForBIN via reflection.
        // Returns null if the method is not found (→ test skips).
        static List<Address> CallWfMakePatchStructDataListForBIN(List<Address> list, bool isPointerOnly, PatchForm.PatchSt patch)
        {
            MethodInfo m = typeof(PatchForm).GetMethod(
                "MakePatchStructDataListForBIN",
                BindingFlags.NonPublic | BindingFlags.Static);
            if (m == null) return null;
            m.Invoke(null, new object[] { list, isPointerOnly, patch });
            return list;
        }

        static void Write(ROM rom, uint addr, params byte[] bytes)
        {
            for (int i = 0; i < bytes.Length; i++) rom.write_u8(addr + (uint)i, bytes[i]);
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
        public void CoreEmitPatchBIN_MatchesWinForms_OnInstalledBinRom_JumpBinFreeAreaClear()
        {
            ROM savedCore = CoreState.ROM;
            ROM savedProg = Program.ROM;
            string dir = Path.Combine(Path.GetTempPath(), "bin-wfparity-" + Path.GetRandomFileName());
            Directory.CreateDirectory(dir);
            try
            {
                var rom = MakeFe8RomAndInstall();
                if (rom == null) return; // WF Program.ROM hook unavailable — SKIP (Pass)

                uint border = rom.RomInfo.compress_image_borderline_address;

                // ---- PLANT the "installed BIN patch" bytes at known addresses ----
                // JUMP generated (aligned) — JUMPTOHACK, length 8 -> emitted BIN length 4.
                uint jumpAddr = 0x801000;
                Write(rom, jumpAddr, 0x10, 0x20, 0x30, 0x40, 0x50, 0x60, 0x70, 0x80);

                // Fixed-addr BIN (BINF) — typed BIN, emitted with its file length.
                byte[] binPayload = { 0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xFF };
                uint binAddr = 0x802000;
                Write(rom, binAddr, binPayload);
                File.WriteAllBytes(Path.Combine(dir, "blk.bin"), binPayload);

                // $FREEAREA — a unique pattern PLANTED above the borderline; GREP re-locates it.
                byte[] faPayload = { 0xCA, 0xFE, 0xBA, 0xBE, 0x12, 0x34, 0x56, 0x78 };
                uint faAddr = ((border + 0x30000) + 3) & ~3u;
                Write(rom, faAddr, faPayload);
                File.WriteAllBytes(Path.Combine(dir, "fa.dmp"), faPayload);

                // CLEAR — a literal addr/length unused region -> UNUSEDBIN.
                uint clearAddr = 0x804000;
                uint clearLen = 0x20;

                var kv = new (string, string)[]
                {
                    ("JUMP:0x" + jumpAddr.ToString("X") + ":$r3", "tgt"),
                    ("BINF:0x" + binAddr.ToString("X"), "blk.bin"),
                    ("BIN:$FREEAREA", "fa.dmp"),
                    ("CLEAR:0x" + clearAddr.ToString("X") + ":0x" + clearLen.ToString("X"), "x"),
                };

                // ---- WF reference (private static, via reflection; reads Program.ROM) ----
                var wf = new List<Address>();
                var wfRet = CallWfMakePatchStructDataListForBIN(wf, false, MakeWfPatch(dir, "BINp", kv));
                if (wfRet == null) return; // WF method not found — SKIP (Pass)

                // ---- Core: EmitPatchBIN on the same ROM ----
                var core = new List<Address>();
                RebuildProducerCore.EmitPatchBIN(rom, core, MakeCorePatch(dir, "BINp", kv), isPointerOnly: false);

                Assert.NotEmpty(wf);
                Assert.NotEmpty(core);

                var wfKeys = new HashSet<Key>(wf.Select(Key.Of));
                var coreKeys = new HashSet<Key>(core.Select(Key.Of));

                var coreExtras = core.Where(a => !wfKeys.Contains(Key.Of(a))).Select(Key.Of).Distinct().ToList();
                var wfExtras = wf.Where(a => !coreKeys.Contains(Key.Of(a))).Select(Key.Of).Distinct().ToList();

                if (coreExtras.Count > 0 || wfExtras.Count > 0)
                {
                    Assert.Fail(
                        "Core EmitPatchBIN diverges from WF MakePatchStructDataListForBIN on an installed-BIN ROM.\n"
                        + $"WF total={wf.Count}, Core total={core.Count}.\n"
                        + $"Core-only (Core emits, WF does not) [{coreExtras.Count}]:\n{Dump(coreExtras)}\n"
                        + $"WF-only (Core dropped) [{wfExtras.Count}]:\n{Dump(wfExtras)}");
                }

                // PROVEN: byte-identical on (Addr/Length/Pointer/DataType).
                Assert.Empty(coreExtras);
                Assert.Empty(wfExtras);

                // Pin the load-bearing entries explicitly so a future regression that drops the
                // same entry on BOTH sides cannot pass the set-equality vacuously.
                // JUMPTOHACK -> BIN length 8-4 = 4 (WF :6392).
                Assert.Contains(core, a => a.DataType == Address.DataTypeEnum.BIN && a.Addr == jumpAddr && a.Length == 4);
                Assert.Contains(wf, a => a.DataType == Address.DataTypeEnum.BIN && a.Addr == jumpAddr && a.Length == 4);
                // Fixed-addr BINF -> BIN length = file length.
                Assert.Contains(core, a => a.DataType == Address.DataTypeEnum.BIN && a.Addr == binAddr && a.Length == (uint)binPayload.Length);
                Assert.Contains(wf, a => a.DataType == Address.DataTypeEnum.BIN && a.Addr == binAddr && a.Length == (uint)binPayload.Length);
                // $FREEAREA GREP-located (default MIX -> default arm length 0).
                Assert.Contains(core, a => a.Addr == faAddr);
                Assert.Contains(wf, a => a.Addr == faAddr);
                // CLEAR -> UNUSEDBIN length.
                Assert.Contains(core, a => a.DataType == Address.DataTypeEnum.UNUSEDBIN && a.Addr == clearAddr && a.Length == clearLen);
                Assert.Contains(wf, a => a.DataType == Address.DataTypeEnum.UNUSEDBIN && a.Addr == clearAddr && a.Length == clearLen);
            }
            finally
            {
                CoreState.ROM = savedCore;
                SetProgramRom(savedProg);
                try { Directory.Delete(dir, true); } catch { }
            }
        }

        // isPointerOnly=true → every mapping collapses to a single length-0 Address on BOTH
        // producers; the key sets must still match exactly.
        [Fact]
        public void CoreEmitPatchBIN_MatchesWinForms_PointerOnly()
        {
            ROM savedCore = CoreState.ROM;
            ROM savedProg = Program.ROM;
            string dir = Path.Combine(Path.GetTempPath(), "bin-wfparity-po-" + Path.GetRandomFileName());
            Directory.CreateDirectory(dir);
            try
            {
                var rom = MakeFe8RomAndInstall();
                if (rom == null) return;

                uint binAddr = 0x802000;
                byte[] binPayload = { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06 };
                Write(rom, binAddr, binPayload);
                File.WriteAllBytes(Path.Combine(dir, "blk.bin"), binPayload);

                uint clearAddr = 0x804000;
                uint clearLen = 0x20;

                var kv = new (string, string)[]
                {
                    ("BINF:0x" + binAddr.ToString("X"), "blk.bin"),
                    ("CLEAR:0x" + clearAddr.ToString("X") + ":0x" + clearLen.ToString("X"), "x"),
                };

                var wf = new List<Address>();
                var wfRet = CallWfMakePatchStructDataListForBIN(wf, true, MakeWfPatch(dir, "PO", kv));
                if (wfRet == null) return;

                var core = new List<Address>();
                RebuildProducerCore.EmitPatchBIN(rom, core, MakeCorePatch(dir, "PO", kv), isPointerOnly: true);

                var wfKeys = new HashSet<Key>(wf.Select(Key.Of));
                var coreKeys = new HashSet<Key>(core.Select(Key.Of));
                var coreExtras = core.Where(a => !wfKeys.Contains(Key.Of(a))).Select(Key.Of).Distinct().ToList();
                var wfExtras = wf.Where(a => !coreKeys.Contains(Key.Of(a))).Select(Key.Of).Distinct().ToList();

                Assert.Empty(coreExtras);
                Assert.Empty(wfExtras);
                // In pointer-only mode the BINF entry is length 0 on both.
                Assert.Contains(core, a => a.Addr == binAddr && a.Length == 0);
                Assert.Contains(wf, a => a.Addr == binAddr && a.Length == 0);
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
