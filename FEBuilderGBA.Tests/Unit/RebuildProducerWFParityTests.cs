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
    /// WF-parity validation harness for the cross-platform ROM-rebuild PRODUCER (#1261 slice 2z-wire).
    /// <para>
    /// Proves the Core producer (<see cref="RebuildProducerCore.MakeAllStructPointers"/> +
    /// <see cref="RebuildProducerCore.AppendAllAsmStructPointers"/>) is byte-faithful to the WinForms
    /// producer (<c>U.MakeAllStructPointersList</c> + <c>U.AppendAllASMStructPointersList</c>) for every
    /// form Core has ported. This test MUST live in <c>FEBuilderGBA.Tests</c> (net9.0-windows) because it
    /// calls the WinForms <c>U</c> producer, which does not exist in the net9.0 Core assembly.
    /// </para>
    /// <para>
    /// <b>Comparison model.</b> WinForms runs every form (including the deferred ones — PatchForm and the
    /// data-path forms Core has not yet ported); Core runs only the ported subset. So WF's list is a
    /// SUPERSET. The faithful, regression-proof assertion is therefore:
    /// <list type="bullet">
    ///   <item>Every Core entry (keyed by <c>Addr</c>/<c>Length</c>/<c>Pointer</c>/<c>DataType</c>) MUST
    ///   appear in the WF list — i.e. Core ⊆ WF. A <b>Core-extra</b> (Core emits an entry WF does not)
    ///   is a real faithfulness regression and FAILS the test with a dump of the first differing entries.</item>
    ///   <item><b>WF-extras</b> (WF emits entries Core lacks) are EXPECTED — they are the deferred forms
    ///   (PatchForm + the still-un-ported data-path forms). They are logged but do not fail the test;
    ///   isolating them here is exactly what keeps PatchForm's known-deferred contribution from masking a
    ///   real Core regression.</item>
    ///   <item><b>GraphicsTool LZ77 re-discoveries</b> are the ONE documented, root-caused class of
    ///   expected Core-extra. The slice-2y GraphicsTool whole-ROM LZ77 scan ignores every image address
    ///   already in <c>list</c> (<c>MakeIgnoreDictionnaryFromList</c>). WF runs ALL data forms first, so
    ///   its list claims those images and WF's GraphicsTool ignores them; Core DEFERS some of those forms,
    ///   so their image addresses are not in Core's list and Core's GraphicsTool legitimately re-discovers
    ///   them as <c>LZ77IMG</c>. Such a Core-extra is always a <c>LZ77IMG</c> whose data address WF ALSO
    ///   covers (it is in WF's list via the deferred form). They are bounded and vanish as the deferred
    ///   forms are ported. The test still FAILS on any Core-extra that is NOT this exact shape (a non-
    ///   LZ77IMG extra, or a LZ77IMG at an address WF does not know at all = a real regression).</item>
    /// </list>
    /// The <c>Info</c>/name field is INFORMATIONAL only (a documented cosmetic divergence — Core and WF
    /// name some entries differently); name mismatches are never asserted.
    /// </para>
    /// <para>
    /// <b>LDR map.</b> Both sides build the ASM-path LDR map with the SAME WF rebuild args
    /// (<c>MakeLDRMap(.., 0x100, compress_image_borderline_address, true)</c> — bounded + pointer-only,
    /// per <c>ToolROMRebuildMake.cs:818</c>), so the ASM producers receive identical input. Core uses
    /// <see cref="RebuildProducerCore.BuildRebuildLdrMap"/>, which reproduces exactly that.
    /// </para>
    /// <para>SKIP-IF-NO-ROM: requires <c>roms/FE8U.gba</c> (gitignored — absent in CI / most worktrees,
    /// present in the user's main checkout). When no ROM is found the test returns early (skips) and
    /// never fails CI.</para>
    /// <para>In the <c>SharedState</c> xUnit collection: it mutates global WinForms/Core state
    /// (<c>CoreState.BaseDirectory</c>, <c>Program.BaseDirectory</c>/<c>Config</c>/<c>ROM</c> via
    /// <c>LoadROM</c>), so it must not run in parallel with other state-mutating tests (Copilot PR #1302
    /// review).</para>
    /// </summary>
    [Collection("SharedState")]
    public class RebuildProducerWFParityTests
    {
        // WF rebuild flags — match the ToolROMRebuildMake.Make defaults (ToolROMRebuildMake.cs:820-826).
        const bool IS_PATCH_INSTALL_ONLY = true;
        const bool IS_PATCH_POINTER_ONLY = false;
        const bool IS_PATCH_STRUCT_ONLY = false;
        const bool IS_USE_OTHER_GRAPHICS = true;
        const bool IS_USE_OAMSP = false;

        [Fact]
        public void CoreProducer_IsSubsetOf_WinFormsProducer_ForAllPortedForms()
        {
            string? repoRoot = FindRepoRoot();
            if (repoRoot == null) return; // cannot locate the solution — skip
            string romPath = Path.Combine(repoRoot, "roms", "FE8U.gba");
            if (!File.Exists(romPath)) return; // SKIP-IF-NO-ROM (gitignored, absent in CI)

            // CoreState.BaseDirectory lets the config/patch trees + producer config files resolve.
            string savedBaseDir = CoreState.BaseDirectory;
            try
            {
                CoreState.BaseDirectory = repoRoot;
                // Skip the WinForms background AsmMap cache rebuild (test-only): the producer only needs
                // the synchronous base symbol table. Program.IsCommandLine has a private setter.
                ForceCommandLineMode();

                // Replicate the WinForms Main() pre-init that LoadROM/InitSystem assumes (Program.cs
                // L60-68): Program.BaseDirectory + a non-null Program.Config. Without them
                // OptionForm.lang_low() NREs on Program.Config inside InitSystem. The WF app normally
                // does this in Main; a headless test must do it before the first LoadROM.
                BootstrapWinFormsProgram(repoRoot);

                // Full WinForms ROM load — populates Program.ROM, every InputFormRef registry, the
                // PatchForm CheckIF scan, and all PreLoadResource config the WF producer reads.
                bool loaded = Program.LoadROM(romPath, "");
                if (!loaded || Program.ROM == null) return; // skip if the ROM did not load
                if (Program.ROM.RomInfo.version != 8) return; // this harness is calibrated on FE8U

                // ---- WinForms producer (the parity reference) ----
                List<Address> wf = U.MakeAllStructPointersList(false);
                List<DisassemblerTrumb.LDRPointer> ldrmap = DisassemblerTrumb.MakeLDRMap(
                    Program.ROM.Data, 0x100, Program.ROM.RomInfo.compress_image_borderline_address, true);
                U.AppendAllASMStructPointersList(wf, ldrmap,
                    isPatchInstallOnly: IS_PATCH_INSTALL_ONLY,
                    isPatchPointerOnly: IS_PATCH_POINTER_ONLY,
                    isPatchStructOnly: IS_PATCH_STRUCT_ONLY,
                    isUseOtherGraphics: IS_USE_OTHER_GRAPHICS,
                    isUseOAMSP: IS_USE_OAMSP);

                // ---- Core producer (same flags, same rebuild LDR map) ----
                RebuildProducerCore.ProducerResult coreData =
                    RebuildProducerCore.MakeAllStructPointers(Program.ROM);
                List<DisassemblerTrumb.LDRPointer> coreLdr =
                    RebuildProducerCore.BuildRebuildLdrMap(Program.ROM);
                RebuildProducerCore.AppendAllAsmStructPointers(
                    Program.ROM, coreData.List, coreLdr,
                    isUseOtherGraphics: IS_USE_OTHER_GRAPHICS, isUseOAMSP: IS_USE_OAMSP);
                List<Address> core = coreData.List;

                Assert.NotEmpty(wf);
                Assert.NotEmpty(core);

                // Key on the load-bearing fields (Addr, Length, Pointer, DataType). Name/Info is cosmetic.
                var wfKeys = new HashSet<Key>(wf.Select(Key.Of));
                var coreKeys = new HashSet<Key>(core.Select(Key.Of));
                var wfAddrs = new HashSet<uint>(wf.Select(a => a.Addr));

                // Core entries WF does NOT emit on all 4 fields (Addr/Length/Pointer/DataType).
                var coreExtras = core.Where(a => !wfKeys.Contains(Key.Of(a))).ToList();
                // WF-extras = the deferred forms WF still emits (PatchForm + un-ported data-path forms).
                int wfExtraCount = wfKeys.Count(k => !coreKeys.Contains(k));

                // ---- isolate the ONE documented, root-caused class of expected divergence ----
                // GraphicsTool LZ77 whole-ROM scan (slice 2y) ignores every image-data address already in
                // `list` (MakeIgnoreDictionnaryFromList adds a.Addr for each entry). WF runs ALL data forms
                // first, so its list already claims the images those forms own and WF's GraphicsTool ignores
                // them. Core DEFERS some of those forms, so their image addresses are NOT in Core's list and
                // Core's GraphicsTool legitimately RE-DISCOVERS them as LZ77IMG. These are EXPECTED: each
                // such Core-extra is a LZ77IMG whose data address WF ALSO covers (it is in WF's list, just
                // via the deferred form's entry, not a GraphicsTool one). When those forms are ported, the
                // extras vanish. They are bounded and never mask a real regression because we still FAIL on
                // any Core-extra that is NOT this exact shape.
                var expectedGfxReDiscovery = coreExtras
                    .Where(a => a.DataType == Address.DataTypeEnum.LZ77IMG && wfAddrs.Contains(a.Addr))
                    .ToList();
                // A REAL regression = a Core-extra that is NOT an expected GraphicsTool re-discovery:
                // either a non-LZ77IMG entry, or a LZ77IMG at an address WF does not know at all.
                var realRegressions = coreExtras
                    .Where(a => !(a.DataType == Address.DataTypeEnum.LZ77IMG && wfAddrs.Contains(a.Addr)))
                    .Select(Key.Of).Distinct().ToList();

                if (realRegressions.Count > 0)
                {
                    const int N = 30;
                    string dump = string.Join("\n", realRegressions.Take(N).Select(k =>
                        $"  Addr=0x{k.Addr:X} Len=0x{k.Length:X} Ptr=0x{k.Pointer:X} Type={k.Type}"
                        + $" wfHasAddr={wfAddrs.Contains(k.Addr)}"));
                    Assert.Fail(
                        $"Core producer emitted {realRegressions.Count} entr{(realRegressions.Count == 1 ? "y" : "ies")} "
                        + "NOT present in the WinForms producer and NOT an expected GraphicsTool re-discovery "
                        + "(faithfulness regression).\n"
                        + $"WF total={wf.Count}, Core total={core.Count}, WF-only (deferred forms)={wfExtraCount}, "
                        + $"expected GraphicsTool re-discoveries={expectedGfxReDiscovery.Count}.\n"
                        + $"First {Math.Min(N, realRegressions.Count)} unexpected Core-only entries:\n{dump}");
                }

                // PROVEN: every Core entry is either byte-identical to a WF entry OR a documented, bounded
                // GraphicsTool re-discovery of an image WF already covers via a deferred form. No real
                // faithfulness regression across the ported forms.
                Assert.Empty(realRegressions);
            }
            finally
            {
                CoreState.BaseDirectory = savedBaseDir;
            }
        }

        /// <summary>
        /// EMPIRICAL PROOF for #1261 slice 2ae: the WinForms producer
        /// (<c>U.MakeAllStructPointersList</c>) emits ZERO <c>EventUnitForm.RecycleReserveUnits</c>
        /// entries on a freshly-loaded ROM — confirming the Core
        /// <see cref="RebuildProducerCore.EmitEventUnitReserveUnits"/> no-op omits nothing real.
        /// <para>
        /// <c>RecycleReserveUnits</c> (EventUnitForm.cs:1746) iterates the static
        /// <c>EventUnitForm.NewAllocData</c> list (EventUnitForm.cs:1660, declared EMPTY); its ONLY
        /// producer is the interactive "NEW" button (<c>CreateNewData</c> → <c>NewAllocData.Add</c>,
        /// EventUnitForm.cs:1689, gated on <c>EventUnitNewAllocForm.ShowDialog()==OK</c>). On a loaded ROM
        /// with no live NEW clicks, <c>NewAllocData</c> is empty, so <c>RecycleReserveUnits</c> emits
        /// nothing. Each entry it WOULD emit goes through <c>RecycleOldUnitsLow(ref list, "NEW", ..)</c>
        /// (EventUnitForm.cs:1776), whose <c>Info</c> is <c>"NEW" + " EVENT UNIT"</c> (and, on FE8,
        /// per-entry <c>"NEW EVENT UNIT COORD &lt;i&gt;"</c>). So filtering the WF producer list by
        /// <c>Info.StartsWith("NEW EVENT UNIT")</c> and asserting count==0 directly proves the no-op safe.
        /// </para>
        /// <para>NO-ROM EARLY-EXIT (reported as Pass): requires <c>roms/FE8U.gba</c> (gitignored — absent
        /// in CI / the worktree; present in the user's main checkout, found by walking up to a root that
        /// has BOTH the solution AND the ROM). This project has no SkippableFact package, so — exactly like
        /// the sibling <see cref="CoreProducer_IsSubsetOf_WinFormsProducer_ForAllPortedForms"/> harness —
        /// the no-ROM guards <c>return;</c> early and xUnit reports the test as Pass (a silent early-exit,
        /// NOT a true "skipped" outcome). The empirical proof is therefore only ENFORCED where the ROM is
        /// present (the user's main checkout, run locally before this slice merged).</para>
        /// </summary>
        [Fact]
        public void WinFormsProducer_EmitsNoReserveUnitEntries_OnLoadedRom()
        {
            string? repoRoot = FindRepoRootWithRom();
            if (repoRoot == null) return; // no checkout with roms/FE8U.gba reachable — early-exit (Pass)
            string romPath = Path.Combine(repoRoot, "roms", "FE8U.gba");
            if (!File.Exists(romPath)) return; // no ROM (gitignored, absent in CI) — early-exit (Pass)

            string savedBaseDir = CoreState.BaseDirectory;
            try
            {
                CoreState.BaseDirectory = repoRoot;
                ForceCommandLineMode();
                BootstrapWinFormsProgram(repoRoot);

                bool loaded = Program.LoadROM(romPath, "");
                if (!loaded || Program.ROM == null) return; // ROM did not load — early-exit (Pass)
                if (Program.ROM.RomInfo.version != 8) return; // calibrated on FE8U — early-exit (Pass)

                // The full WF data-path producer (the parity reference). It DOES call
                // EventUnitForm.RecycleReserveUnits(ref list) at U.cs:2485.
                List<Address> wf = U.MakeAllStructPointersList(false);
                Assert.NotEmpty(wf);

                // RecycleReserveUnits → RecycleOldUnitsLow(ref list, "NEW", ..) names every entry
                // "NEW EVENT UNIT" (+ FE8 "NEW EVENT UNIT COORD <i>"). On a loaded ROM NewAllocData is
                // empty, so there must be ZERO such entries. A non-zero count would mean WF CAN emit
                // reserve-unit entries from a loaded ROM — which would make the Core no-op UNSAFE.
                var reserveEntries = wf
                    .Where(a => a.Info != null && a.Info.StartsWith("NEW EVENT UNIT", StringComparison.Ordinal))
                    .ToList();

                Assert.True(reserveEntries.Count == 0,
                    $"EMPIRICAL PROOF FAILED: the WinForms producer emitted {reserveEntries.Count} "
                    + "RecycleReserveUnits entr"
                    + (reserveEntries.Count == 1 ? "y" : "ies")
                    + " (Info starts with \"NEW EVENT UNIT\") on a freshly-loaded ROM. This means "
                    + "EventUnitForm.NewAllocData was NOT empty on load, so the Core "
                    + "EmitEventUnitReserveUnits no-op is NOT a faithful reproduction. First few: "
                    + string.Join("; ", reserveEntries.Take(5).Select(a =>
                        $"[{a.Info}] Addr=0x{a.Addr:X} Len=0x{a.Length:X}")));
            }
            finally
            {
                CoreState.BaseDirectory = savedBaseDir;
            }
        }

        /// <summary>
        /// PARTIAL WF-parity for #1261 slice s2pf-3 — the PatchForm producer ADDR + SWITCH
        /// dispatch arms (<see cref="RebuildProducerCore.EmitPatchAddr"/> /
        /// <see cref="RebuildProducerCore.EmitPatchSwitch"/>). Both the WinForms reference
        /// (<c>PatchForm.MakePatchStructDataList</c>, PatchForm.cs:7126) and the Core orchestrator
        /// (<see cref="RebuildProducerCore.MakePatchStructDataListCore"/>) are run on the SAME real
        /// FE8U ROM with the SAME rebuild flags (isPointerOnly=false, isInstallOnly=true,
        /// isStructOnly=false — the <c>ToolROMRebuildMake.Make</c> -&gt; <c>AppendAllASMStructPointersList</c>
        /// callsite, ToolROMRebuildMake.cs:820). Each side is FILTERED to entries whose <c>Info</c> ends
        /// <c>@ADDRESS</c> / <c>@SWITCH</c> (the names the two arms emit), then compared as
        /// Core ⊆ WF on the load-bearing fields (Addr/Length/Pointer/DataType) — Info/name is cosmetic
        /// (Core's leaner LoadPatch omits Name, so its Info is just "@ADDRESS"; WF prepends the patch
        /// name). A Core-extra (Core emits an @ADDRESS/@SWITCH entry WF does not) is a faithfulness
        /// regression and FAILS. WF-extras are expected only if Core's gate diverges — but the gate is a
        /// faithful port, so for ADDR/SWITCH the two key sets must match EXACTLY when the patch tree
        /// is present. This test asserts FULL set equality — both Core-extras (Core emits an entry WF
        /// does not = faithfulness regression) AND WF-extras (WF emits an entry Core does not = Core
        /// silently dropped a real patch entry) FAIL. The latter direction closes the gap a Core ⊆ WF
        /// only check would leave (it passes vacuously even if Core drops every entry — Copilot CLI
        /// PR #1313 review).
        /// <para>SKIP-IF-NO-ROM: requires <c>roms/FE8U.gba</c> (gitignored — absent in CI / the worktree,
        /// present in the user's main checkout). When no ROM is found the test returns early (Pass).</para>
        /// <para><b>NON-VACUOUS only where <c>config/patch2</c> is CHECKED OUT.</b> Both producers walk the
        /// <c>config/patch2/&lt;version&gt;</c> submodule tree; where that submodule is un-initialized (the
        /// known-env state of this worktree and the local main checkout — <c>git submodule status</c> shows a
        /// leading <c>-</c>), there are no ADDR/SWITCH patch files, so BOTH filtered lists are empty and the
        /// Core ⊆ WF assertion holds vacuously (still a valid, passing real-ROM run that proves no Core-extra,
        /// just with nothing to compare). The branch-level verification — every ADDR/SWITCH code path, the
        /// BIN/MIX length boundary, the per-address isSafetyOffset skip, the inherited unsafe-$0x divergence —
        /// is carried by the synthetic Core.Tests (<c>RebuildProducerPatchAddrSwitchTests</c>, 21 cases). This
        /// harness gains teeth automatically wherever <c>config/patch2</c> is populated, and s2pf-11's
        /// full-producer parity (once the orchestrator is wired) covers the end-to-end path.</para>
        /// </summary>
        [Fact]
        public void CorePatchAddrSwitchArms_MatchExactly_WinFormsPatchProducer()
        {
            string? repoRoot = FindRepoRootWithRom();
            if (repoRoot == null) return; // no checkout with roms/FE8U.gba reachable — early-exit (Pass)
            string romPath = Path.Combine(repoRoot, "roms", "FE8U.gba");
            if (!File.Exists(romPath)) return; // no ROM (gitignored, absent in CI) — early-exit (Pass)

            string savedBaseDir = CoreState.BaseDirectory;
            try
            {
                CoreState.BaseDirectory = repoRoot;
                ForceCommandLineMode();
                BootstrapWinFormsProgram(repoRoot);

                bool loaded = Program.LoadROM(romPath, "");
                if (!loaded || Program.ROM == null) return; // ROM did not load — early-exit (Pass)
                if (Program.ROM.RomInfo.version != 8) return; // calibrated on FE8U — early-exit (Pass)

                // Re-scan CheckIF exactly as ToolROMRebuildMake.Make does (PatchForm.ClearCheckIF
                // before the producer) so the WF and Core gates see the same installed state.
                PatchForm.ClearCheckIF();

                // ---- WinForms reference: the public patch producer, same flags as the rebuild ----
                var wfAll = new List<Address>();
                PatchForm.MakePatchStructDataList(wfAll,
                    isPointerOnly: IS_PATCH_POINTER_ONLY,
                    isInstallOnly: IS_PATCH_INSTALL_ONLY,
                    isStructOnly: IS_PATCH_STRUCT_ONLY);

                // ---- Core: the orchestrator, same flags + the SAME ROM ----
                var coreAll = new List<Address>();
                RebuildProducerCore.MakePatchStructDataListCore(Program.ROM, coreAll,
                    isPointerOnly: IS_PATCH_POINTER_ONLY,
                    isInstallOnly: IS_PATCH_INSTALL_ONLY,
                    isStructOnly: IS_PATCH_STRUCT_ONLY);

                // Filter BOTH to the two arms' entries (Info ends @ADDRESS / @SWITCH).
                static bool IsArmEntry(Address a) =>
                    a.Info != null && (a.Info.EndsWith("@ADDRESS", StringComparison.Ordinal)
                                       || a.Info.EndsWith("@SWITCH", StringComparison.Ordinal));

                var wfArm = wfAll.Where(IsArmEntry).ToList();
                var coreArm = coreAll.Where(IsArmEntry).ToList();

                var wfKeys = new HashSet<Key>(wfArm.Select(Key.Of));
                var coreKeys = new HashSet<Key>(coreArm.Select(Key.Of));

                // Core-extras = Core emits an @ADDRESS/@SWITCH key WF does not. ALWAYS a regression
                // (faithfulness). WF-extras = WF emits a key Core does not = Core SILENTLY DROPPED a
                // real patch entry — for these two arms (same faithful gate + same resolver) the key
                // sets MUST match exactly when the patch tree is present, so a WF-extra is ALSO a
                // regression (the gap Copilot CLI flagged: a Core⊆WF-only check passes vacuously even
                // when Core drops every entry). Assert FULL set equality. When config/patch2 is absent
                // (this worktree / un-init submodule) BOTH sets are empty and equality holds trivially.
                var coreExtras = coreArm.Where(a => !wfKeys.Contains(Key.Of(a)))
                                        .Select(Key.Of).Distinct().ToList();
                var wfExtras = wfArm.Where(a => !coreKeys.Contains(Key.Of(a)))
                                    .Select(Key.Of).Distinct().ToList();

                static string Dump(System.Collections.Generic.IEnumerable<Key> keys)
                {
                    const int N = 30;
                    var l = keys.ToList();
                    return string.Join("\n", l.Take(N).Select(k =>
                        $"  Addr=0x{k.Addr:X} Len=0x{k.Length:X} Ptr=0x{k.Pointer:X} Type={k.Type}"))
                        + (l.Count > N ? $"\n  ... (+{l.Count - N} more)" : "");
                }

                if (coreExtras.Count > 0 || wfExtras.Count > 0)
                {
                    Assert.Fail(
                        "Core PatchForm ADDR/SWITCH arms diverge from the WinForms patch producer "
                        + "(faithfulness regression).\n"
                        + $"WF @ADDRESS/@SWITCH total={wfArm.Count}, Core total={coreArm.Count}.\n"
                        + $"Core-only entries (Core emits, WF does not) [{coreExtras.Count}]:\n{Dump(coreExtras)}\n"
                        + $"WF-only entries (Core SILENTLY DROPPED) [{wfExtras.Count}]:\n{Dump(wfExtras)}");
                }

                // PROVEN: the Core and WinForms @ADDRESS/@SWITCH key sets are EQUAL on
                // (Addr/Length/Pointer/DataType) — no Core-extra (faithfulness) and no dropped
                // Core entry — for the two ported arms on the real ROM (or both empty when the
                // submodule is absent).
                Assert.Empty(coreExtras);
                Assert.Empty(wfExtras);
            }
            finally
            {
                CoreState.BaseDirectory = savedBaseDir;
            }
        }

        /// <summary>
        /// PARTIAL WF-parity for #1261 slice s2pf-4/7 — the PatchForm producer TYPE=IMAGE dispatch arm
        /// (<see cref="RebuildProducerCore.EmitPatchImage"/>, ALL 8 variants). Both the WinForms
        /// reference (<c>PatchForm.MakePatchStructDataList</c> -&gt; <c>MakePatchStructDataListForIMAGE</c>,
        /// PatchForm.cs:6738) and the Core orchestrator (<see cref="RebuildProducerCore.MakePatchStructDataListCore"/>)
        /// run on the SAME real FE8U ROM with the SAME rebuild flags (the <c>ToolROMRebuildMake.Make</c>
        /// callsite). Each side is FILTERED to the IMAGE arm's ported variants — which surface as
        /// NINE Info suffixes because the PALETTE variant emits under two param keys (<c>@PALETTE_POINTER</c>
        /// for the deref form and <c>@PALETTE_ADDRESS</c> for the direct-address else-fallback), and ZIMAGE
        /// covers both <c>@ZIMAGE_POINTER</c> and the <c>@Z256IMAGE_POINTER</c> alias (Info ends
        /// <c>@IMAGE_POINTER</c> / <c>@ZIMAGE_POINTER</c> / <c>@Z256IMAGE_POINTER</c> / <c>@TSA_POINTER</c> /
        /// <c>@ZTSA_POINTER</c> / <c>@ZHEADERTSA_POINTER</c> / <c>@HEADERTSA_POINTER</c> / <c>@PALETTE_POINTER</c> /
        /// <c>@PALETTE_ADDRESS</c>) — then compared as Core ⊆ WF on the load-bearing fields
        /// (Addr/Length/Pointer/DataType) — Info/name is cosmetic (Core's leaner LoadPatch omits the patch
        /// Name, so its Info is just "@VARIANT"; WF prepends the patch name). A Core-extra (Core emits an
        /// IMAGE entry WF does not) is a faithfulness regression and FAILS.
        /// <para><b>HEADERTSA_POINTER NOW INCLUDED (s2pf-7).</b> The NON-Z header-TSA variant
        /// (<c>@HEADERTSA_POINTER</c>, WF <c>AddHeaderTSAPointer</c> -&gt; <c>DataTypeEnum.HEADERTSA</c>) is
        /// wired via <c>EmitHeaderTsaPointer</c> / <c>CalcHeaderTsaLength</c> (= WF
        /// <c>ImageUtil.CalcByteLengthForHeaderTSAData</c>); it is compared on BOTH sides like the other
        /// variants. WF-extras (an IMAGE entry WF emits that Core lacks) are LOGGED (not asserted) exactly as
        /// the subset-direction harness does, keeping the teeth on the regression direction (Core-extra).</para>
        /// <para>SKIP-IF-NO-ROM: requires <c>roms/FE8U.gba</c> (gitignored — absent in CI / the worktree,
        /// present in the user's main checkout). When no ROM is found the test returns early (Pass).</para>
        /// <para><b>NON-VACUOUS only where <c>config/patch2</c> is CHECKED OUT</b> (same posture as the
        /// ADDR/SWITCH harness): an un-init submodule yields no IMAGE patch files, so both filtered lists are
        /// empty and Core ⊆ WF holds trivially. The branch-level verification (all eight variants, the
        /// /2-vs-/32 raw sizes, the palette count, the LZ77 lengths, the header-TSA byte length, the
        /// per-variant safety gates) is carried by the synthetic Core.Tests (<c>RebuildProducerPatchImageTests</c>).</para>
        /// </summary>
        [Fact]
        public void CorePatchImageArm_IsSubsetOf_WinFormsPatchProducer_AllVariants()
        {
            string? repoRoot = FindRepoRootWithRom();
            if (repoRoot == null) return; // no checkout with roms/FE8U.gba reachable — early-exit (Pass)
            string romPath = Path.Combine(repoRoot, "roms", "FE8U.gba");
            if (!File.Exists(romPath)) return; // no ROM (gitignored, absent in CI) — early-exit (Pass)

            string savedBaseDir = CoreState.BaseDirectory;
            try
            {
                CoreState.BaseDirectory = repoRoot;
                ForceCommandLineMode();
                BootstrapWinFormsProgram(repoRoot);

                bool loaded = Program.LoadROM(romPath, "");
                if (!loaded || Program.ROM == null) return; // ROM did not load — early-exit (Pass)
                if (Program.ROM.RomInfo.version != 8) return; // calibrated on FE8U — early-exit (Pass)

                PatchForm.ClearCheckIF();

                // ---- WinForms reference: the public patch producer, same flags as the rebuild ----
                var wfAll = new List<Address>();
                PatchForm.MakePatchStructDataList(wfAll,
                    isPointerOnly: IS_PATCH_POINTER_ONLY,
                    isInstallOnly: IS_PATCH_INSTALL_ONLY,
                    isStructOnly: IS_PATCH_STRUCT_ONLY);

                // ---- Core: the orchestrator, same flags + the SAME ROM ----
                var coreAll = new List<Address>();
                RebuildProducerCore.MakePatchStructDataListCore(Program.ROM, coreAll,
                    isPointerOnly: IS_PATCH_POINTER_ONLY,
                    isInstallOnly: IS_PATCH_INSTALL_ONLY,
                    isStructOnly: IS_PATCH_STRUCT_ONLY);

                // ALL eight ported IMAGE variant param-key suffixes (HEADERTSA_POINTER now INCLUDED — s2pf-7).
                string[] portedSuffixes =
                {
                    "@IMAGE_POINTER", "@ZIMAGE_POINTER", "@Z256IMAGE_POINTER",
                    "@TSA_POINTER", "@ZTSA_POINTER", "@ZHEADERTSA_POINTER",
                    "@HEADERTSA_POINTER",
                    "@PALETTE_POINTER", "@PALETTE_ADDRESS",
                };

                // An IMAGE arm entry for one of the ported variants (all eight). HEADERTSA_POINTER is now
                // wired (s2pf-7), so it is INCLUDED (its @HEADERTSA_POINTER suffix is in portedSuffixes and
                // its DataTypeEnum.HEADERTSA is no longer filtered out).
                bool IsPortedImageEntry(Address a)
                {
                    if (a.Info == null) return false;
                    foreach (string s in portedSuffixes)
                    {
                        if (a.Info.EndsWith(s, StringComparison.Ordinal)) return true;
                    }
                    return false;
                }

                var wfImg = wfAll.Where(IsPortedImageEntry).ToList();
                var coreImg = coreAll.Where(IsPortedImageEntry).ToList();

                var wfKeys = new HashSet<Key>(wfImg.Select(Key.Of));
                var coreKeys = new HashSet<Key>(coreImg.Select(Key.Of));

                // Core-extras = Core emits a (ported) IMAGE key WF does not. ALWAYS a faithfulness
                // regression -> FAIL. WF-extras (WF emits a ported-IMAGE key Core lacks) are logged but
                // not asserted (subset direction), mirroring the data-path harness — all eight variants
                // are now ported, so a WF-extra would signal a gate divergence worth surfacing, not a
                // silent-drop the way ADDR/SWITCH's exact-equality catches it. Core ⊆ WF is the
                // load-bearing guarantee for this (now 8-of-8) slice.
                var coreExtras = coreImg.Where(a => !wfKeys.Contains(Key.Of(a)))
                                        .Select(Key.Of).Distinct().ToList();
                int wfExtraCount = wfKeys.Count(k => !coreKeys.Contains(k));

                if (coreExtras.Count > 0)
                {
                    const int N = 30;
                    string dump = string.Join("\n", coreExtras.Take(N).Select(k =>
                        $"  Addr=0x{k.Addr:X} Len=0x{k.Length:X} Ptr=0x{k.Pointer:X} Type={k.Type}"));
                    Assert.Fail(
                        $"Core PatchForm IMAGE arm emitted {coreExtras.Count} (ported-variant) entr"
                        + (coreExtras.Count == 1 ? "y" : "ies")
                        + " NOT present in the WinForms patch producer (faithfulness regression).\n"
                        + $"WF ported-IMAGE total={wfImg.Count}, Core total={coreImg.Count}, "
                        + $"WF-only (gate divergence?)={wfExtraCount}.\n"
                        + $"First {Math.Min(N, coreExtras.Count)} Core-only entries:\n{dump}");
                }

                // PROVEN: every Core IMAGE entry (of all eight ported variants, including HEADERTSA_POINTER)
                // is byte-identical to a WF entry on (Addr/Length/Pointer/DataType) — no faithfulness
                // regression (or both empty when config/patch2 is absent).
                Assert.Empty(coreExtras);
            }
            finally
            {
                CoreState.BaseDirectory = savedBaseDir;
            }
        }

        /// <summary>
        /// PARTIAL WF-parity for #1261 slice s2pf-5/6 — the PatchForm producer TYPE=STRUCT dispatch arm
        /// (<see cref="RebuildProducerCore.EmitPatchStruct"/>). Both the WinForms reference
        /// (<c>PatchForm.MakePatchStructDataList</c> -&gt; <c>MakePatchStructDataListForSTRUCT</c>,
        /// PatchForm.cs:6461) and the Core orchestrator
        /// (<see cref="RebuildProducerCore.MakePatchStructDataListCore"/>) run on the SAME real FE8U ROM
        /// with the SAME rebuild flags. Each side is FILTERED to the FULLY-IMPLEMENTED STRUCT arms only —
        /// the MAIN struct InputFormRef entry (Info ends <c>@STRUCT</c>) + the per-entry ASM and
        /// PatchImage_* arms (Info contains <c>@STRUCT </c> then <c>ASM</c>/<c>IMAGE</c>/<c>TSA</c>/
        /// <c>ZTSA</c>/<c>ZHEADERTSA</c>/<c>PALETTE</c>) + the EVENT arm (s2pf-6: the
        /// <c>EventScriptForm.ScanScript</c> walk's <c>@STRUCT DATA n</c> EVENTSCRIPT/IFR/BIN entries) —
        /// then compared Core ⊆ WF on the load-bearing fields (Addr/Length/Pointer/DataType). A Core-extra
        /// (Core emits a STRUCT entry WF does not) is a faithfulness regression and FAILS.
        /// <para><b>EVENT is INCLUDED (s2pf-6).</b> Its <c>@STRUCT DATA n</c> entries are emitted by the
        /// real ScanScript walk (<see cref="RebuildProducerCore.EmitScanScript"/>), disasm-gated; they are
        /// non-MIX (EVENTSCRIPT for the script blocks, IFR/BIN for POINTER_UNIT/AICOORDINATE sub-data), so
        /// the filter includes any non-MIX <c>@STRUCT DATA n</c> entry.</para>
        /// <para><b>PatchImage_HEADERTSA is NOW INCLUDED (s2pf-7).</b> The non-Z header-TSA field
        /// (<c>@STRUCT HEADERTSA n</c>, WF <c>AddHeaderTSAPointer</c> -&gt; <c>DataTypeEnum.HEADERTSA</c>) is
        /// wired via <see cref="RebuildProducerCore.EmitHeaderTsaPointer"/>; its <c>@STRUCT HEADERTSA </c>
        /// info token + HEADERTSA data type are compared on BOTH sides.</para>
        /// <para><b>THE REMAINING INTERIM FORM-BOUND ARMS ARE EXCLUDED (deferred to s2pf-8..10).</b> The
        /// BATTLEANIMEPOINTER/AP/ROMTCS/PROCS/VENNOUWEAPONLOCK/SMEPROMOLIST/CLASSLIST/
        /// TERRAINBATTLELISTPOINTER/BATTLEBGLISTPOINTER/AOERANGEPOINTER fields are
        /// routed through Core's INTERIM default-MIX (a length-0 MIX entry named <c>... DATA n</c>) this
        /// slice — they INTENTIONALLY diverge from WF (WF emits the precise sub-walked TARGET region). So
        /// every MIX-typed <c>... DATA </c>-suffixed entry is filtered OUT of BOTH sides before the
        /// comparison. Those arms gain their own parity teeth in s2pf-8..10, and the FULL STRUCT parity
        /// (no exclusions) lands at s2pf-11 when the gate token is removed.</para>
        /// <para><b>CSTRING is NOT in the merged-list parity scope:</b> WF/Core both name a CSTRING entry
        /// the DECODED STRING (no <c>@STRUCT</c> marker), so it cannot be reliably attributed to a STRUCT
        /// patch within the merged producer list. The CSTRING arm's byte-faithfulness is carried by the
        /// synthetic <c>RebuildProducerPatchStructTests.EmitPatchStruct_CStringField_*</c>.</para>
        /// <para>SKIP-IF-NO-ROM: requires <c>roms/FE8U.gba</c> (gitignored — absent in CI / the worktree,
        /// present in the user's main checkout). When no ROM is found the test returns early (Pass).</para>
        /// <para><b>NON-VACUOUS only where <c>config/patch2</c> is CHECKED OUT</b> (same posture as the
        /// ADDR/SWITCH + IMAGE harnesses): an un-init submodule yields no STRUCT patch files, so both
        /// filtered lists are empty and Core ⊆ WF holds trivially. The branch-level verification (the
        /// skeleton arithmetic, the DATACOUNT guards, every safe arm, the 6624-6632 defect, the interim
        /// default-MIX) is carried by the synthetic Core.Tests (<c>RebuildProducerPatchStructTests</c>).</para>
        /// </summary>
        [Fact]
        public void CorePatchStructArm_IsSubsetOf_WinFormsPatchProducer_ExcludingInterimFormArms()
        {
            string? repoRoot = FindRepoRootWithRom();
            if (repoRoot == null) return; // no checkout with roms/FE8U.gba reachable — early-exit (Pass)
            string romPath = Path.Combine(repoRoot, "roms", "FE8U.gba");
            if (!File.Exists(romPath)) return; // no ROM (gitignored, absent in CI) — early-exit (Pass)

            string savedBaseDir = CoreState.BaseDirectory;
            try
            {
                CoreState.BaseDirectory = repoRoot;
                ForceCommandLineMode();
                BootstrapWinFormsProgram(repoRoot);

                bool loaded = Program.LoadROM(romPath, "");
                if (!loaded || Program.ROM == null) return; // ROM did not load — early-exit (Pass)
                if (Program.ROM.RomInfo.version != 8) return; // calibrated on FE8U — early-exit (Pass)

                PatchForm.ClearCheckIF();

                // ---- WinForms reference: the public patch producer, same flags as the rebuild ----
                var wfAll = new List<Address>();
                PatchForm.MakePatchStructDataList(wfAll,
                    isPointerOnly: IS_PATCH_POINTER_ONLY,
                    isInstallOnly: IS_PATCH_INSTALL_ONLY,
                    isStructOnly: IS_PATCH_STRUCT_ONLY);

                // ---- Core: the orchestrator, same flags + the SAME ROM ----
                var coreAll = new List<Address>();
                RebuildProducerCore.MakePatchStructDataListCore(Program.ROM, coreAll,
                    isPointerOnly: IS_PATCH_POINTER_ONLY,
                    isInstallOnly: IS_PATCH_INSTALL_ONLY,
                    isStructOnly: IS_PATCH_STRUCT_ONLY);

                // A FULLY-IMPLEMENTED STRUCT-arm entry: the MAIN struct entry (Info ends "@STRUCT") OR a
                // per-entry ASM / PatchImage_* arm ("@STRUCT " + ASM/IMAGE/TSA/ZTSA/ZHEADERTSA/HEADERTSA/
                // PALETTE) OR an EVENT-walk entry (s2pf-6: the "@STRUCT DATA n" entries the
                // EventScriptForm.ScanScript walk emits — EVENTSCRIPT script blocks + their
                // POINTER_UNIT/AICOORDINATE sub-data IFR/BIN blocks). The STILL-INTERIM form-bound arms
                // (AP/ROMTCS/PROCS s2pf-8 / Vennou/AOE/SMEPromo/SomeClass/Terrain* s2pf-9 / BattleAnime
                // s2pf-10) emit "@STRUCT DATA n" as a length-0 MIX placeholder — those DIVERGE from WF this
                // slice and are EXCLUDED. Since EmitPatchStructDefaultMix is the ONLY producer of a MIX-typed
                // "@STRUCT DATA " entry, a simple rule cleanly separates the two: a MIX-typed DATA entry is
                // interim (EXCLUDE), any other DATA entry came from the now-precise EVENT walk (INCLUDE).
                // PatchImage_HEADERTSA is now precise (s2pf-7), emitting a "@STRUCT HEADERTSA n" / HEADERTSA
                // entry — INCLUDED via the safeArmTokens. CSTRING is out of merged-list scope (named the
                // decoded string) — see doc-comment.
                string[] safeArmTokens = { " ASM ", " IMAGE ", " TSA ", " ZTSA ", " ZHEADERTSA ", " HEADERTSA ", " PALETTE " };
                bool IsImplementedStructEntry(Address a)
                {
                    if (a.Info == null) return false;
                    // A per-entry "... DATA n" entry: the STILL-INTERIM arms emit it as a length-0 MIX
                    // placeholder (EXCLUDE); the EVENT walk (s2pf-6) emits EVENTSCRIPT/IFR/BIN (INCLUDE).
                    if (a.Info.Contains("@STRUCT DATA ", StringComparison.Ordinal))
                    {
                        return a.DataType != Address.DataTypeEnum.MIX;
                    }
                    // The MAIN struct InputFormRef entry: Info ends "@STRUCT".
                    if (a.Info.EndsWith("@STRUCT", StringComparison.Ordinal)) return true;
                    // A per-entry safe arm: "...@STRUCT <ARM> ..." for one of the implemented arms.
                    if (a.Info.IndexOf("@STRUCT", StringComparison.Ordinal) < 0) return false;
                    foreach (string t in safeArmTokens)
                    {
                        if (a.Info.Contains("@STRUCT" + t, StringComparison.Ordinal)) return true;
                    }
                    return false;
                }

                var wfStruct = wfAll.Where(IsImplementedStructEntry).ToList();
                var coreStruct = coreAll.Where(IsImplementedStructEntry).ToList();

                var wfKeys = new HashSet<Key>(wfStruct.Select(Key.Of));

                // Core-extras = Core emits an implemented-STRUCT key WF does not. ALWAYS a faithfulness
                // regression -> FAIL. WF-extras (WF emits an implemented-STRUCT key Core lacks) are NOT
                // asserted (subset direction): the interim form-bound arms are already excluded, so a
                // WF-extra among the implemented arms would signal a gate divergence worth surfacing, not
                // a silent drop. Core ⊆ WF is the load-bearing guarantee for this skeleton+safe-arms slice.
                var coreExtras = coreStruct.Where(a => !wfKeys.Contains(Key.Of(a)))
                                           .Select(Key.Of).Distinct().ToList();

                if (coreExtras.Count > 0)
                {
                    const int N = 30;
                    string dump = string.Join("\n", coreExtras.Take(N).Select(k =>
                        $"  Addr=0x{k.Addr:X} Len=0x{k.Length:X} Ptr=0x{k.Pointer:X} Type={k.Type}"));
                    Assert.Fail(
                        $"Core PatchForm STRUCT arm emitted {coreExtras.Count} (implemented-arm) entr"
                        + (coreExtras.Count == 1 ? "y" : "ies")
                        + " NOT present in the WinForms patch producer (faithfulness regression).\n"
                        + $"WF implemented-STRUCT total={wfStruct.Count}, Core total={coreStruct.Count}.\n"
                        + $"First {Math.Min(N, coreExtras.Count)} Core-only entries:\n{dump}");
                }

                // PROVEN: every Core STRUCT entry (of the implemented skeleton + safe arms) is
                // byte-identical to a WF entry on (Addr/Length/Pointer/DataType) — no faithfulness
                // regression — with the interim form-bound arms excluded from both sides (or both empty
                // when config/patch2 is absent). FULL parity (no exclusions) lands at s2pf-11.
                Assert.Empty(coreExtras);
            }
            finally
            {
                CoreState.BaseDirectory = savedBaseDir;
            }
        }

        // ----------------------------------------------------------------
        readonly struct Key : IEquatable<Key>
        {
            public readonly uint Addr;
            public readonly uint Length;
            public readonly uint Pointer;
            public readonly Address.DataTypeEnum Type;
            Key(uint addr, uint length, uint pointer, Address.DataTypeEnum type)
            {
                Addr = addr; Length = length; Pointer = pointer; Type = type;
            }
            public static Key Of(Address a) => new Key(a.Addr, a.Length, a.Pointer, a.DataType);
            public bool Equals(Key o) => Addr == o.Addr && Length == o.Length
                                          && Pointer == o.Pointer && Type == o.Type;
            public override bool Equals(object? o) => o is Key k && Equals(k);
            public override int GetHashCode() => HashCode.Combine(Addr, Length, Pointer, (int)Type);
        }

        // Program.IsCommandLine has a private setter; force it on for headless test init.
        static void ForceCommandLineMode()
        {
            try
            {
                PropertyInfo? p = typeof(Program).GetProperty(
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
        /// <c>InitSystem</c> assumes (Program.cs L60-68): <c>Program.BaseDirectory</c> pointed at the repo
        /// root (so config/translate trees resolve) and a non-null <c>Program.Config</c> (so
        /// <c>OptionForm.lang_low()</c> does not NRE). Both have private/internal setters, so this uses
        /// reflection — strictly test-only setup. <c>Config.at("func_lang","auto")</c> falls back to "auto"
        /// when <c>config.xml</c> is absent, so an empty Config instance is sufficient.
        /// </summary>
        static void BootstrapWinFormsProgram(string repoRoot)
        {
            Type prog = typeof(Program);

            // Program.BaseDirectory = repoRoot (private static setter).
            PropertyInfo? baseDirProp = prog.GetProperty(
                "BaseDirectory", BindingFlags.Public | BindingFlags.Static);
            baseDirProp?.SetValue(null, repoRoot);

            // Program.Config = new ConfigWinForms(); Config.Load(repoRoot/config/config.xml).
            // ConfigWinForms is internal to the FEBuilderGBA assembly — construct it via reflection.
            PropertyInfo? configProp = prog.GetProperty(
                "Config", BindingFlags.Public | BindingFlags.Static);
            if (configProp != null && configProp.GetValue(null) == null)
            {
                Type? configType = prog.Assembly.GetType("FEBuilderGBA.ConfigWinForms");
                if (configType != null)
                {
                    object? cfg = Activator.CreateInstance(configType, nonPublic: true);
                    if (cfg != null)
                    {
                        MethodInfo? load = configType.GetMethod("Load", new[] { typeof(string) });
                        load?.Invoke(cfg, new object[] { Path.Combine(repoRoot, "config", "config.xml") });
                        configProp.SetValue(null, cfg);
                        CoreState.Config = (Config)cfg;
                    }
                }
            }
        }

        static string? FindRepoRoot()
        {
            string? dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            for (int i = 0; i < 12 && dir != null; i++)
            {
                if (File.Exists(Path.Combine(dir, "FEBuilderGBA.sln"))) return dir;
                dir = Path.GetDirectoryName(dir);
            }
            return null;
        }

        /// <summary>
        /// Like <see cref="FindRepoRoot"/> but walks up to a root that has BOTH the solution AND
        /// <c>roms/FE8U.gba</c>. The worktree root has the solution but NOT the gitignored ROM, so a
        /// plain <c>FindRepoRoot</c> would resolve to the worktree and skip; the ROM lives in the user's
        /// main checkout, which is an ancestor of the worktree (<c>&lt;main&gt;/.claude/worktrees/X</c>).
        /// Walking up to the first ancestor that has both lets the slice-2ae empirical proof actually run
        /// locally while still cleanly skipping in CI (where no ancestor has the ROM).
        /// </summary>
        static string? FindRepoRootWithRom()
        {
            string? dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
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
