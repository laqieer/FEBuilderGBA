using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using FEBuilderGBA;
using Xunit;

namespace FEBuilderGBA.Tests.Unit
{
    /// <summary>
    /// #1261 — the END-TO-END ROM-rebuild ROUND-TRIP PROOF on a real FE8U ROM (Core-only pipeline).
    /// <para>
    /// The sibling <see cref="RebuildProducerWFParityTests"/> proves the Core PRODUCER is byte-faithful to
    /// the WinForms producer (real-ROM gap=0) and that <see cref="RebuildProducerCore.MakeWithProducer"/>
    /// PROCEEDS on a vanilla FE8U (the <c>IsComplete</c> gate is OPEN). The synthetic
    /// <c>RebuildCoreTests.RebuildMakeApplyRoundTripTests</c> proves <see cref="RebuildApplyCore.Apply"/> on a
    /// HAND-BUILT manifest. What neither covers — and what this test adds — is the FULL pipeline driven by
    /// the ACTUAL producer manifest on a real ROM:
    /// </para>
    /// <list type="number">
    ///   <item>Load vanilla FE8U via the WinForms <c>Program.LoadROM</c> path (which wires
    ///   <c>CoreState.EventScript</c>/<c>ProcsScript</c>/<c>AIScript</c>/<c>CommentCache</c> — so the
    ///   event-script disassembler is ready and <see cref="RebuildProducerCore.MakeWithProducer"/> does not
    ///   refuse via the disasm gate).</item>
    ///   <item><see cref="RebuildProducerCore.MakeWithProducer"/> on the real ROM, into a temp dir → a REAL
    ///   <c>.rebuild</c> manifest + <c>rebuild_{ifr,mix,bin}/</c> sidecars.</item>
    ///   <item><see cref="RebuildApplyCore.Apply"/> on the SAME vanilla base + that manifest →
    ///   <c>ApplyResult{ Rebuilt }</c>.</item>
    ///   <item>VALIDATE the rebuilt ROM is FAITHFUL (the strongest available proof — see below).</item>
    /// </list>
    /// <para>
    /// <b>Two rebuild addresses, both faithful (a [Theory]).</b>
    /// <list type="bullet">
    ///   <item><b>EXTENDS</b> — <c>rebuildAddress = U.toOffset(RomInfo.extends_address)</c> (= 0x01000000 for
    ///   FE8U), the value the GUI uses by default (<c>ToolROMRebuildForm.cs:190</c>). On a VANILLA FE8U this
    ///   is exactly end-of-ROM, so NOTHING relocates: the proof is that the full producer→apply pipeline runs
    ///   end-to-end on the real ROM and yields a faithful ROM byte-identical to vanilla. This is the realistic
    ///   GUI flow.</item>
    ///   <item><b>RELOCATE</b> — <c>rebuildAddress = 0x00B00000</c>, chosen so a real slab of FE8U structs
    ///   above it actually RELOCATES. The proof is the STRONG one: the relocated data is preserved, every
    ///   pointer is fixed up to its new address (0 <c>Missing!</c>), and the rebuilt ROM differs from vanilla
    ///   (relocation genuinely happened) while staying a structurally valid, completely re-parseable FE8U.</item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>Validation method chosen — (B) STRUCTURAL FAITHFULNESS + (C) CONSISTENCY.</b> The brief's (A)
    /// byte-parity-vs-WF option is not available headlessly: the WinForms apply path
    /// (<c>ToolROMRebuildForm</c>/<c>ToolROMRebuildApply</c>) is Forms-coupled (PleaseWait dialog +
    /// InputFormRef). <see cref="RebuildApplyCore"/> is the headless Core port of exactly that apply path,
    /// and the PRODUCER side is already proven byte-identical to WF (gap=0) by the sibling parity harness, so
    /// B+C closes the apply side rigorously:
    /// <list type="bullet">
    ///   <item><b>(B1) No unresolved pointers.</b> <c>ApplyResult.Success</c> is true ⇔ the self-check found
    ///   ZERO <c>Missing!</c> entries — every relocated pointer was fixed up to its new address. This is the
    ///   load-bearing faithfulness proof: an <c>@DEF</c>/unknown-length entry the producer emitted that Apply
    ///   could not resolve would surface here as <c>Missing!</c> and FAIL — the precise "real gap" the brief
    ///   asks to catch (it would point at an omitted <c>ResolvUnkLength</c>/<c>AppendLDR</c> Make phase).</item>
    ///   <item><b>(B2) Vanilla base preserved.</b> The non-rebuild region <c>[0, rebuildAddress)</c> of the
    ///   rebuilt ROM is byte-identical to vanilla (Apply seeds the output from the vanilla base and only
    ///   relocates the rebuild region above it).</item>
    ///   <item><b>(B3) Real reload re-detects FE8U.</b> The rebuilt bytes are written to a temp <c>.gba</c>
    ///   and re-loaded through <c>Program.LoadROM</c> (NOT <c>SwapNewROMDataDirect</c>, which would leave
    ///   stale <c>RomInfo</c>/caches): the relocated ROM still detects as <c>version==8</c> from its own
    ///   header + RomInfo pointers, proving the relocation kept the ROM structurally a valid FE8U.</item>
    ///   <item><b>(C) Re-parse consistency.</b> The Core producer is re-run on the freshly-reloaded ACTIVE
    ///   rebuilt ROM (<c>MakeAllStructPointers</c> requires <c>ReferenceEquals(rom, CoreState.ROM)</c>); it
    ///   must return a non-empty list AND <c>IsComplete==true</c> — i.e. the rebuilt ROM carries no
    ///   corruption signature that would make a second producer pass incomplete or throw.</item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>KNOWN SLICE-1 LIMITATION (documented, NOT exercised as a passing case).</b> Driving the RELOCATE
    /// case from an even LOWER <c>rebuildAddress = 0x00800000</c> reveals a real gap: two event-script MIX
    /// blocks (chapters 0x07 / 0x11) carry a pointer <c>0x08072628</c> into the NON-rebuild base region, which
    /// the append-only slice-1 Apply leaves as a permanent <c>Missing!</c> (<c>Success=false</c>). The
    /// base-region target is never registered in the AddressMap because only RELOCATED entries register, so
    /// the MIX <c>@</c>-token never resolves. The full WinForms tool handles this via the additional
    /// base-pointer <c>@DEF</c>/<c>ResolvUnkLength</c> Make phases that slice-1 omits. This is reported as the
    /// follow-up Make-phase slice, NOT papered over — the two faithful cases above use addresses where the
    /// relocated structs do not forward-reference the base region.
    /// </para>
    /// <para>SKIP-IF-NO-ROM: requires <c>roms/FE8U.gba</c> (gitignored — absent in CI / most worktrees,
    /// present in the user's checkout). When no ROM is found the test returns early and xUnit reports Pass (a
    /// silent early-exit — this project has no SkippableFact). NON-VACUOUS only where the ROM is present.</para>
    /// <para>In the <c>SharedState</c> xUnit collection — it mutates global WinForms/Core state
    /// (<c>CoreState.BaseDirectory</c>, <c>Program.ROM</c>/<c>Config</c> via <c>LoadROM</c>), so it must not
    /// run in parallel with other state-mutating tests.</para>
    /// </summary>
    [Collection("SharedState")]
    public class RebuildEndToEndRoundTripTests
    {
        const bool IS_USE_OTHER_GRAPHICS = true;   // matches ToolROMRebuildMake.Make default
        const bool IS_USE_OAMSP = false;           // matches ToolROMRebuildMake.Make default

        // A rebuildAddress mode. EXTENDS == U.toOffset(RomInfo.extends_address) (the GUI default; nothing
        // relocates on a vanilla FE8U). RELOCATE == a fixed lower address that forces a real slab of FE8U
        // structs to relocate (above it) while staying faithful (no base-region forward-refs at 0x00B00000).
        public enum Mode { Extends, Relocate }
        const uint RELOCATE_REBUILD_ADDRESS = 0x00B00000u;

        [Theory]
        [InlineData(Mode.Extends)]
        [InlineData(Mode.Relocate)]
        public void FullPipeline_RealFE8U_MakeWithProducer_Apply_RebuiltRomIsFaithful(Mode mode)
        {
            string repoRoot = FindRepoRootWithRom();
            if (repoRoot == null) return; // no checkout with roms/FE8U.gba reachable — early-exit (Pass)
            string romPath = Path.Combine(repoRoot, "roms", "FE8U.gba");
            if (!File.Exists(romPath)) return; // no ROM (gitignored, absent in CI) — early-exit (Pass)

            string savedBaseDir = CoreState.BaseDirectory;
            string tmpDir = Path.Combine(Path.GetTempPath(), "feb_e2e_rt_" + Guid.NewGuid().ToString("N"));
            // The two Program.LoadROM calls below persist Last_Rom_Filename through Program.Config.Save()
            // into <repoRoot>/config/config.xml — which would DIRTY the checkout (Copilot PR-review finding
            // 1). Snapshot that file so the finally block can restore it exactly (or delete it if it did not
            // exist before), keeping the test fully isolated / non-persisting.
            string configXmlPath = Path.Combine(repoRoot, "config", "config.xml");
            bool configXmlExisted = File.Exists(configXmlPath);
            byte[] configXmlSnapshot = configXmlExisted ? File.ReadAllBytes(configXmlPath) : null;
            try
            {
                Directory.CreateDirectory(tmpDir);
                CoreState.BaseDirectory = repoRoot;
                ForceCommandLineMode();
                BootstrapWinFormsProgram(repoRoot);

                // ---- (1) Load vanilla FE8U via the full WF path (wires the event-script disassembler). ----
                // The ONLY allowed skip is no-ROM (handled above, before any state mutation). Once the ROM
                // file EXISTS, a load/version/multibyte failure is NOT a skip — it would silently report a
                // passing test that never exercised the producer->apply->reload->reparse contract. So these
                // are ASSERTIONS, not early-returns (Copilot PR-review finding 2): with a present FE8U the
                // proof is always non-vacuous.
                bool loaded = Program.LoadROM(romPath, "");
                Assert.True(loaded && Program.ROM != null,
                    "roms/FE8U.gba exists but Program.LoadROM failed — the ROM is present so the e2e proof "
                    + "must run, not skip (a broken bootstrap or corrupt ROM path must FAIL, not pass).");
                Assert.Equal(8, Program.ROM.RomInfo.version);        // must be FE8U (this harness is calibrated on it)
                Assert.False(Program.ROM.RomInfo.is_multibyte);      // FE8U is non-multibyte; FE8J is a different gate

                ROM rom = Program.ROM;
                // The "vanilla" base is the unmodified FE8U — a fresh ROM re-loaded from the same bytes
                // (Apply seeds the rebuilt ROM from this and only relocates the rebuild region above it).
                var vanilla = new ROM();
                Assert.True(vanilla.Load(romPath, out string _), "vanilla FE8U must load");

                uint rebuildAddress = mode == Mode.Extends
                    ? U.toOffset(rom.RomInfo.extends_address)   // GUI default (ToolROMRebuildForm.cs:190)
                    : RELOCATE_REBUILD_ADDRESS;                 // forces real struct relocation
                Assert.True(U.isPadding4(rebuildAddress), "rebuild address must be 4-aligned");

                string manifestPath = Path.Combine(tmpDir, "fe8u.rebuild");
                var progress = new NullProgress();

                // ---- (2) MakeWithProducer on the real ROM → REAL .rebuild manifest + sidecars. ----
                // ANY refusal here means the full round trip is never exercised — FAIL with the precise
                // cause (Copilot plan-review finding 1). A legitimate gate is now OPEN (real-ROM gap=0);
                // the only possible refusal would be the s2pf-12 EA/BIN backstop on an INSTALLED un-emittable
                // patch — vanilla FE8U has none, so a refusal here is a real regression worth surfacing.
                try
                {
                    RebuildProducerCore.MakeWithProducer(
                        rom, vanilla, rebuildAddress, manifestPath,
                        isUseOtherGraphics: IS_USE_OTHER_GRAPHICS, isUseOAMSP: IS_USE_OAMSP,
                        progress: progress, ct: CancellationToken.None);
                }
                catch (Exception ex)
                {
                    Assert.Fail(
                        "MakeWithProducer REFUSED on vanilla FE8U (mode=" + mode + "), so the end-to-end round "
                        + "trip could not be exercised. On a vanilla FE8U the IsComplete gate is OPEN "
                        + "(real-ROM producer gap=0) and no EA/BIN patch is installed, so a refusal is a "
                        + "regression. If the message names a 'not yet ported' form, a producer form regressed; "
                        + "if it names an EA/BIN patch, the s2pf-12 backstop tripped on an unexpectedly-installed "
                        + "patch.\n" + ex.GetType().Name + ": " + ex.Message);
                }

                Assert.True(File.Exists(manifestPath),
                    "MakeWithProducer must write the .rebuild manifest when the gate is open");
                // Sanity: the producer wrote the rebuild address into the manifest.
                string manifestText = File.ReadAllText(manifestPath);
                Assert.Contains("@_REBUILDADDRESS ", manifestText);

                // ---- (3) Apply on the SAME vanilla base + the REAL manifest → rebuilt bytes. ----
                RebuildApplyCore.ApplyResult result = RebuildApplyCore.Apply(
                    vanilla, manifestPath, rom.RomInfo.extends_address,
                    isReserved: null, progress: progress);

                Assert.NotNull(result);
                Assert.NotNull(result.Rebuilt);

                // ---- (4) VALIDATE FAITHFULNESS ----

                // (B1) No unresolved pointers. Success==false ⇔ the self-check log contains "Missing!"
                // entries — a producer-emitted @DEF/unknown-length pointer Apply could not fix up. That is a
                // REAL gap (an omitted ResolvUnkLength/AppendLDR Make phase): report it precisely, do not
                // paper over it.
                Assert.True(result.Success,
                    "Apply on the REAL FE8U producer manifest (mode=" + mode + ") left UNRESOLVED pointers — "
                    + "the full pipeline did NOT yield a faithful rebuilt ROM. Each 'Missing!' line below is a "
                    + "producer-emitted pointer Apply could not resolve (would need the omitted "
                    + "ResolvUnkLength/AppendLDR Make phase). Self-check log:\n" + (result.Log ?? "<null>"));
                // Belt-and-suspenders: the log must carry no Missing! marker even if Success were mis-set.
                Assert.DoesNotContain("Missing!", result.Log ?? "");

                byte[] rebuilt = result.Rebuilt;

                // (B2) Vanilla base preserved: the FULL non-rebuild region [0, rebuildAddress) is byte-
                // identical to vanilla. Require BOTH ROMs to actually contain that whole range first — a
                // clamped Math.Min compare would silently shrink the validated window on a trimmed/short ROM
                // and pass vacuously (Copilot PR-review inline finding). Compare exactly [0, rebuildAddress).
                byte[] vanData = vanilla.Data;
                Assert.True(vanData.Length >= rebuildAddress,
                    $"vanilla FE8U (0x{vanData.Length:X} bytes) must contain the entire non-rebuild base "
                    + $"region [0, 0x{rebuildAddress:X}) for the faithfulness compare to be non-vacuous.");
                Assert.True(rebuilt.Length >= rebuildAddress,
                    $"rebuilt ROM (0x{rebuilt.Length:X} bytes) must contain the entire preserved non-rebuild "
                    + $"base region [0, 0x{rebuildAddress:X}).");
                for (uint i = 0; i < rebuildAddress; i++)
                {
                    if (rebuilt[i] != vanData[i])
                    {
                        Assert.Fail($"rebuilt non-rebuild base diverges from vanilla at offset 0x{i:X}: "
                            + $"expected 0x{vanData[i]:X2} got 0x{rebuilt[i]:X2} (rebuildAddress=0x{rebuildAddress:X})");
                    }
                }

                // Mode-specific relocation invariant: EXTENDS relocates nothing (rebuilt == vanilla);
                // RELOCATE genuinely moved a slab of structs (rebuilt differs from vanilla above the rebuild
                // address — proving relocation + pointer fix-up actually ran, not a no-op copy).
                if (mode == Mode.Extends)
                {
                    Assert.Equal(vanData.Length, rebuilt.Length);
                    Assert.True(BytesEqual(rebuilt, 0, vanData, 0, (uint)vanData.Length),
                        "EXTENDS mode must reproduce vanilla byte-for-byte (nothing relocates).");
                }
                else
                {
                    Assert.True(rebuilt.Length != vanData.Length
                                || !BytesEqual(rebuilt, 0, vanData, 0, (uint)vanData.Length),
                        "RELOCATE mode must actually relocate structs (rebuilt must differ from vanilla); "
                        + "an identical rebuilt ROM means no relocation happened — the proof would be vacuous.");
                }

                // (B3) Real reload — write the rebuilt bytes to a temp .gba and LOAD it (re-detects RomInfo +
                // re-wires CoreState.EventScript/caches; NOT SwapNewROMDataDirect, which leaves them stale).
                string rebuiltPath = Path.Combine(tmpDir, "fe8u_rebuilt.gba");
                File.WriteAllBytes(rebuiltPath, rebuilt);
                bool rebuiltLoaded = Program.LoadROM(rebuiltPath, "");
                Assert.True(rebuiltLoaded && Program.ROM != null,
                    "the rebuilt ROM must re-load through the full WF path");
                Assert.Equal(8, Program.ROM.RomInfo.version); // still detects as FE8U after relocation
                Assert.False(Program.ROM.RomInfo.is_multibyte);

                // (C) Re-parse consistency: run the Core producer on the freshly-reloaded ACTIVE rebuilt ROM.
                // It must return a non-empty list AND be IsComplete — no corruption signature that makes a
                // second producer pass incomplete or throws. (MakeAllStructPointers requires the ROM be the
                // active CoreState.ROM, which Program.LoadROM just made it.)
                RebuildProducerCore.ProducerResult reparse =
                    RebuildProducerCore.MakeAllStructPointers(Program.ROM, progress, CancellationToken.None);
                Assert.NotEmpty(reparse.List);
                Assert.True(reparse.IsComplete,
                    "the rebuilt FE8U re-parsed INCOMPLETE (mode=" + mode + ") — relocation produced a ROM the "
                    + "producer can no longer fully enumerate (a corruption signature). NotYetPorted at "
                    + "re-parse: " + string.Join(", ", reparse.NotYetPorted));

                System.Console.WriteLine(
                    $"[#1261 e2e] FE8U round trip FAITHFUL (mode={mode}): vanilla {vanData.Length:X} bytes -> "
                    + $"rebuilt {rebuilt.Length:X} bytes (rebuildAddress=0x{rebuildAddress:X}); "
                    + $"Apply.Success={result.Success} (0 Missing!); rebuilt re-detects version=8; "
                    + $"re-parse list={reparse.List.Count} entries, IsComplete={reparse.IsComplete}.");
            }
            finally
            {
                CoreState.BaseDirectory = savedBaseDir;
                try { Directory.Delete(tmpDir, true); } catch { }
                // Restore config/config.xml to its pre-test state so the test never dirties the checkout:
                // re-write the snapshot if it existed, otherwise delete the file the load calls created.
                try
                {
                    if (configXmlExisted)
                    {
                        File.WriteAllBytes(configXmlPath, configXmlSnapshot);
                    }
                    else if (File.Exists(configXmlPath))
                    {
                        File.Delete(configXmlPath);
                    }
                }
                catch { }
            }
        }

        // ----------------------------------------------------------------
        static bool BytesEqual(byte[] a, uint aOff, byte[] b, uint bOff, uint len)
        {
            if (aOff + len > a.Length || bOff + len > b.Length) return false;
            for (uint i = 0; i < len; i++)
            {
                if (a[aOff + i] != b[bOff + i]) return false;
            }
            return true;
        }

        // Test-only headless WinForms bootstrap (self-contained local copies of the sibling
        // RebuildProducerWFParityTests helpers — the harness keeps each test file independent).

        sealed class NullProgress : IProgress<string>
        {
            public void Report(string value) { }
        }

        // Program.IsCommandLine has a private setter; force it on for headless test init.
        static void ForceCommandLineMode()
        {
            try
            {
                PropertyInfo p = typeof(Program).GetProperty(
                    "IsCommandLine", BindingFlags.Public | BindingFlags.Static);
                p?.SetValue(null, true, BindingFlags.NonPublic | BindingFlags.Instance, null, null, null);
            }
            catch
            {
                // Non-fatal: without it InitSystem just runs the synchronous ClearCache instead.
            }
        }

        /// <summary>
        /// Replicate the minimal WinForms <c>Program.Main</c> pre-init that <c>Program.LoadROM</c> →
        /// <c>InitSystem</c> assumes: <c>Program.BaseDirectory</c> = repo root and a non-null
        /// <c>Program.Config</c> (so <c>OptionForm.lang_low()</c> does not NRE). Both have private setters,
        /// so this uses reflection — strictly test-only setup.
        /// </summary>
        static void BootstrapWinFormsProgram(string repoRoot)
        {
            Type prog = typeof(Program);

            PropertyInfo baseDirProp = prog.GetProperty(
                "BaseDirectory", BindingFlags.Public | BindingFlags.Static);
            baseDirProp?.SetValue(null, repoRoot);

            PropertyInfo configProp = prog.GetProperty(
                "Config", BindingFlags.Public | BindingFlags.Static);
            if (configProp != null && configProp.GetValue(null) == null)
            {
                Type configType = prog.Assembly.GetType("FEBuilderGBA.ConfigWinForms");
                if (configType != null)
                {
                    object cfg = Activator.CreateInstance(configType, nonPublic: true);
                    if (cfg != null)
                    {
                        MethodInfo load = configType.GetMethod("Load", new[] { typeof(string) });
                        load?.Invoke(cfg, new object[] { Path.Combine(repoRoot, "config", "config.xml") });
                        configProp.SetValue(null, cfg);
                        CoreState.Config = (Config)cfg;
                    }
                }
            }
        }

        /// <summary>
        /// Walk up to the first ancestor that has BOTH <c>FEBuilderGBA.sln</c> AND <c>roms/FE8U.gba</c>. The
        /// worktree may or may not carry the gitignored ROM; the user's main checkout (an ancestor of any
        /// worktree at <c>&lt;main&gt;/.claude/worktrees/X</c>) always does. Returns null in CI (no ancestor
        /// has the ROM) so the test cleanly early-exits (Pass).
        /// </summary>
        static string FindRepoRootWithRom()
        {
            string dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            for (int i = 0; i < 16 && dir != null; i++)
            {
                if (File.Exists(Path.Combine(dir, "FEBuilderGBA.sln"))
                    && File.Exists(Path.Combine(dir, "roms", "FE8U.gba")))
                {
                    return dir;
                }
                dir = Path.GetDirectoryName(dir);
            }
            return null;
        }
    }
}
