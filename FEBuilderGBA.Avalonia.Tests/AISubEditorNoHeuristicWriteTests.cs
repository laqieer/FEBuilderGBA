// SPDX-License-Identifier: GPL-3.0-or-later
// AI sub-editor heuristic-write corruption guard tests (#1414).
//
// The five Avalonia AI sub-editors — AITiles, AIUnits, AIASMCoordinate,
// AIASMRange, AIASMCALLTALK — were exposed as standalone main-menu editors that,
// with no incoming parent pointer, self-initialized their write target via a
// type-agnostic heuristic (AISubEditorHelper.FindFirstValidAISubData). The Write
// handlers then committed user-edited bytes to that GUESSED address, silently
// corrupting whatever ROM data lived there.
//
// WinForms reaches these forms ONLY from the AIScript per-parameter dispatch
// (POINTER_AITILE/AIUNIT/AICOORDINATE/AIRANGE/AICALLTALK), never standalone, and
// only ever writes to the real per-parameter script pointer (with AllocIfNeed +
// IsBrokenData/isSafetyOffset validation).
//
// These tests prove the corruption path is gone:
//   - LoadList() returns exactly ONE placeholder entry at addr 0 (no heuristic
//     non-zero target derived from scanning AI scripts);
//   - a standalone Write() (CurrentAddr == 0) mutates ZERO ROM bytes;
//   - the parent-pointer path is preserved: LoadEntry(realAddr) + Write() still
//     writes ONLY at the supplied address;
//   - static guards: the type-agnostic finder type/method is gone, and the five
//     standalone main-menu buttons + click handlers are removed from MainWindow.
//
// Marked [Collection("SharedState")] because the per-ROM helper mutates
// CoreState.ROM and related caches.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.ViewModels;
using FEBuilderGBA.Avalonia.Views;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests
{
    [Collection("SharedState")]
    public class AISubEditorNoHeuristicWriteTests
    {
        // (ViewModel factory, write-back action that sets every editable field
        // to a sentinel before Write so a mutation would be detectable).
        public static IEnumerable<object[]> SubEditors()
        {
            yield return new object[] { "AITiles", new SubEditorProbe(
                () => new AITilesViewModel(),
                vm => { var t = (AITilesViewModel)vm; t.Tile = 0x7F; },
                vm => ((AITilesViewModel)vm).LoadList(),
                (vm, a) => ((AITilesViewModel)vm).LoadEntry(a),
                vm => ((AITilesViewModel)vm).Write,
                vm => ((AITilesViewModel)vm).CurrentAddr) };

            yield return new object[] { "AIUnits", new SubEditorProbe(
                () => new AIUnitsViewModel(),
                vm => { var t = (AIUnitsViewModel)vm; t.Unit = 0x7F; t.Unknown1 = 0x7F; },
                vm => ((AIUnitsViewModel)vm).LoadList(),
                (vm, a) => ((AIUnitsViewModel)vm).LoadEntry(a),
                vm => ((AIUnitsViewModel)vm).Write,
                vm => ((AIUnitsViewModel)vm).CurrentAddr) };

            yield return new object[] { "AIASMCoordinate", new SubEditorProbe(
                () => new AIASMCoordinateViewModel(),
                vm => { var t = (AIASMCoordinateViewModel)vm; t.X = 0x7F; t.Y = 0x7F; t.Unused2 = 0x7F; t.Unused3 = 0x7F; },
                vm => ((AIASMCoordinateViewModel)vm).LoadList(),
                (vm, a) => ((AIASMCoordinateViewModel)vm).LoadEntry(a),
                vm => ((AIASMCoordinateViewModel)vm).Write,
                vm => ((AIASMCoordinateViewModel)vm).CurrentAddr) };

            yield return new object[] { "AIASMRange", new SubEditorProbe(
                () => new AIASMRangeViewModel(),
                vm => { var t = (AIASMRangeViewModel)vm; t.X1 = 0x7F; t.Y1 = 0x7F; t.X2 = 0x7F; t.Y2 = 0x7F; },
                vm => ((AIASMRangeViewModel)vm).LoadList(),
                (vm, a) => ((AIASMRangeViewModel)vm).LoadEntry(a),
                vm => ((AIASMRangeViewModel)vm).Write,
                vm => ((AIASMRangeViewModel)vm).CurrentAddr) };

            yield return new object[] { "AIASMCALLTALK", new SubEditorProbe(
                () => new AIASMCALLTALKViewModel(),
                vm => { var t = (AIASMCALLTALKViewModel)vm; t.FromUnit = 0x7F; t.ToUnit = 0x7F; t.Unused2 = 0x7F; t.Unused3 = 0x7F; },
                vm => ((AIASMCALLTALKViewModel)vm).LoadList(),
                (vm, a) => ((AIASMCALLTALKViewModel)vm).LoadEntry(a),
                vm => ((AIASMCALLTALKViewModel)vm).Write,
                vm => ((AIASMCALLTALKViewModel)vm).CurrentAddr) };
        }

        sealed class SubEditorProbe
        {
            public readonly Func<object> Create;
            public readonly Action<object> SetSentinelFields;
            public readonly Func<object, List<AddrResult>> LoadList;
            public readonly Action<object, uint> LoadEntry;
            public readonly Func<object, Action> WriteOf;
            public readonly Func<object, uint> CurrentAddrOf;

            public SubEditorProbe(
                Func<object> create,
                Action<object> setSentinelFields,
                Func<object, List<AddrResult>> loadList,
                Action<object, uint> loadEntry,
                Func<object, Action> writeOf,
                Func<object, uint> currentAddrOf)
            {
                Create = create;
                SetSentinelFields = setSentinelFields;
                LoadList = loadList;
                LoadEntry = loadEntry;
                WriteOf = writeOf;
                CurrentAddrOf = currentAddrOf;
            }

            // ToString keeps the xUnit test-explorer row readable (the probe is a
            // data row, but the leading string arg already names it).
            public override string ToString() => "probe";
        }

        // ----------------------------------------------------------------
        // 1. LoadList() returns exactly ONE placeholder at addr 0 — never a
        //    heuristically-guessed non-zero write target.
        // ----------------------------------------------------------------
        [Theory]
        [MemberData(nameof(SubEditors))]
        public void LoadList_StandaloneOpen_ReturnsSinglePlaceholderAtAddrZero(string name, object probeObj)
        {
            var probe = (SubEditorProbe)probeObj;
            RomTestHelper.WithRom("FE8U", () =>
            {
                object vm = probe.Create();
                List<AddrResult> items = probe.LoadList(vm);

                Assert.Single(items);
                Assert.Equal(0u, items[0].addr);
                // No heuristic load happened: CurrentAddr stays 0 (Write is a no-op).
                Assert.Equal(0u, probe.CurrentAddrOf(vm));
                _ = name;
            });
        }

        // ----------------------------------------------------------------
        // 2. A standalone Write() (CurrentAddr == 0) mutates ZERO ROM bytes.
        //    Byte-compares the entire ROM image before/after.
        // ----------------------------------------------------------------
        [Theory]
        [MemberData(nameof(SubEditors))]
        public void Write_StandaloneNoParentPointer_MutatesNoRomBytes(string name, object probeObj)
        {
            var probe = (SubEditorProbe)probeObj;
            RomTestHelper.WithRom("FE8U", () =>
            {
                ROM rom = CoreState.ROM;
                object vm = probe.Create();

                // Standalone open: a single placeholder at addr 0.
                List<AddrResult> items = probe.LoadList(vm);
                Assert.Single(items);
                Assert.Equal(0u, probe.CurrentAddrOf(vm));

                // User edits every field to a sentinel, then clicks Write.
                probe.SetSentinelFields(vm);

                byte[] before = (byte[])rom.Data.Clone();
                probe.WriteOf(vm)();   // Write()
                byte[] after = rom.Data;

                Assert.True(before.AsSpan().SequenceEqual(after.AsSpan()),
                    $"{name}: standalone Write() must not mutate any ROM byte (CurrentAddr==0 guard).");
            });
        }

        // ----------------------------------------------------------------
        // 3. Parent-pointer path preserved: LoadEntry(realAddr) + Write() writes
        //    ONLY at the supplied address (Copilot CLI plan-review #4).
        // ----------------------------------------------------------------
        [Theory]
        [MemberData(nameof(SubEditors))]
        public void Write_WithRealParentPointer_WritesOnlyAtSuppliedAddress(string name, object probeObj)
        {
            var probe = (SubEditorProbe)probeObj;
            RomTestHelper.WithRom("FE8U", () =>
            {
                ROM rom = CoreState.ROM;
                object vm = probe.Create();

                // A real, safe, 4-byte-aligned write target inside the ROM body
                // (well past the header), simulating the AIScript per-param jump
                // supplying a validated script pointer via NavigateTo/LoadEntry.
                uint target = 0x300000;
                Assert.True(U.isSafetyOffset(target) && target + 4 <= (uint)rom.Data.Length);

                probe.LoadEntry(vm, target);
                Assert.Equal(target, probe.CurrentAddrOf(vm));

                probe.SetSentinelFields(vm);

                byte[] before = (byte[])rom.Data.Clone();
                probe.WriteOf(vm)();   // Write()
                byte[] after = rom.Data;

                // Every byte OUTSIDE [target, target+4) is untouched.
                for (int i = 0; i < before.Length; i++)
                {
                    if (i >= target && i < target + 4) continue;
                    Assert.True(before[i] == after[i],
                        $"{name}: Write() mutated a byte outside the supplied target at 0x{i:X}.");
                }
                // At least one targeted byte changed to the sentinel (proves the
                // write still works when a real address is supplied).
                bool changedInRange = false;
                for (uint i = target; i < target + 4; i++)
                    if (before[i] != after[i]) { changedInRange = true; break; }
                Assert.True(changedInRange,
                    $"{name}: Write() with a real parent pointer must persist into the supplied target.");
            });
        }

        // ----------------------------------------------------------------
        // 4a. Static guard: the type-agnostic finder is gone (root cause removed).
        // ----------------------------------------------------------------
        [Fact]
        public void StaticGuard_AISubEditorHelper_TypeIsDeleted()
        {
            Assembly avalonia = typeof(AITilesViewModel).Assembly;
            Type? helper = avalonia.GetType("FEBuilderGBA.Avalonia.ViewModels.AISubEditorHelper");
            Assert.Null(helper);

            // No type anywhere in the Avalonia assembly still exposes the finder.
            foreach (Type t in avalonia.GetTypes())
            {
                MethodInfo? mi = t.GetMethod("FindFirstValidAISubData",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance);
                Assert.True(mi == null,
                    $"FindFirstValidAISubData still exists on {t.FullName} — the heuristic finder must be removed.");
            }
        }

        // ----------------------------------------------------------------
        // 4b. Static guard: the five standalone main-menu click handlers are gone.
        // ----------------------------------------------------------------
        [Fact]
        public void StaticGuard_MainWindow_StandaloneAISubEditorHandlers_AreRemoved()
        {
            Type mw = typeof(MainWindow);
            string[] removedHandlers =
            {
                "OpenAIASMCALLTALK_Click",
                "OpenAIASMCoordinate_Click",
                "OpenAIASMRange_Click",
                "OpenAITiles_Click",
                "OpenAIUnits_Click",
            };
            foreach (string h in removedHandlers)
            {
                MethodInfo? mi = mw.GetMethod(h,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
                Assert.True(mi == null,
                    $"MainWindow still defines the standalone handler {h} — the AI sub-editor button must be removed.");
            }
        }

        // ----------------------------------------------------------------
        // 4c. Static guard: the five standalone button x:Name fields are gone
        //     (axaml-generated fields disappear when the <Button> is removed).
        // ----------------------------------------------------------------
        [Fact]
        public void StaticGuard_MainWindow_StandaloneAISubEditorButtonFields_AreRemoved()
        {
            Type mw = typeof(MainWindow);
            string[] removedButtons =
            {
                "AIASMCallButton",
                "AICoordinateButton",
                "AIRangeButton",
                "AITilesButton",
                "AIUnitsButton",
            };
            foreach (string b in removedButtons)
            {
                FieldInfo? fi = mw.GetField(b,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
                Assert.True(fi == null,
                    $"MainWindow still has the standalone button field {b} — the AI sub-editor button must be removed.");
            }
        }
    }
}
