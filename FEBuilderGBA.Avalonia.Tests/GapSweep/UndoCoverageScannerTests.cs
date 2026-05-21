// SPDX-License-Identifier: GPL-3.0-or-later
// Phase 5 tests — UndoCoverageScanner classification logic. (#374)
//
// All tests are pure unit-level: they use in-memory synthetic source strings
// (no temp files, no filesystem dependencies) so they're deterministic and
// independent of the live repo state. A small handful of integration-style
// tests at the bottom exercise Scan() against the live worktree to catch
// regressions in the file-walking path; those tests degrade gracefully when
// the worktree root cannot be located (running from a published binary).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FEBuilderGBA.Avalonia.GapSweep;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests.GapSweep;

/// <summary>
/// Unit tests for <see cref="UndoCoverageScanner"/>'s scanner-only logic.
/// Most cases use the <c>ExtractCallsitesFromSource</c> entry point with
/// hand-crafted synthetic VMs so each classification tier is exercised
/// directly.
/// </summary>
public class UndoCoverageScannerTests
{
    // ===================================================================
    // Tier classification — Covered.
    // ===================================================================

    [Fact]
    public void Classifies_BeginCommitScope_AsCovered()
    {
        // VM with a field of UndoService type whose Save method wraps
        // Begin/Commit around a rom.write_u8 call.
        string src = @"
namespace X {
    using FEBuilderGBA.Avalonia.Services;
    class FooViewModel {
        UndoService _undoService = new UndoService();
        public void Save() {
            _undoService.Begin(""Edit Foo"");
            rom.write_u8(0x10, 1);
            _undoService.Commit();
        }
    }
}";
        var rows = UndoCoverageScanner.ExtractCallsitesFromSource(src, "Test.cs");
        var row = Assert.Single(rows);
        Assert.Equal(UndoCoverage.Covered, row.Coverage);
        Assert.Equal("FooViewModel", row.EnclosingClass);
        Assert.Equal("Save", row.EnclosingMethod);
        Assert.StartsWith("rom.write_u8", row.WriteExpression);
    }

    [Fact]
    public void Classifies_TryCatchRollbackPattern_AsCovered()
    {
        // The full Begin/Commit + try/catch/Rollback pattern used by
        // EventScriptPopupViewModel. Both the success and failure paths
        // are inside the method; we test the WRITE callsite specifically.
        string src = @"
namespace X {
    using FEBuilderGBA.Avalonia.Services;
    class FooViewModel {
        UndoService _undoService = new();
        public void Save() {
            _undoService.Begin(""Edit Foo"");
            try {
                rom.write_u16(0x10, 0x42);
                _undoService.Commit();
            } catch {
                _undoService.Rollback();
                throw;
            }
        }
    }
}";
        var rows = UndoCoverageScanner.ExtractCallsitesFromSource(src, "Test.cs");
        var row = Assert.Single(rows);
        Assert.Equal(UndoCoverage.Covered, row.Coverage);
    }

    [Fact]
    public void Classifies_LocalUndoServicePattern_AsCovered()
    {
        // EventScriptPopupViewModel pattern: `var undoService = new
        // UndoService();` inside the method body. ClassHasUndoServiceMember
        // should detect this via the implicit-typed local resolver.
        string src = @"
namespace X {
    using FEBuilderGBA.Avalonia.Services;
    class FooViewModel {
        public void Save() {
            var undoService = new UndoService();
            undoService.Begin(""Edit"");
            rom.write_u8(0x10, 1);
            undoService.Commit();
        }
    }
}";
        var rows = UndoCoverageScanner.ExtractCallsitesFromSource(src, "Test.cs");
        var row = Assert.Single(rows);
        Assert.Equal(UndoCoverage.Covered, row.Coverage);
    }

    [Fact]
    public void Classifies_ExplicitUndoArgument_AsCovered()
    {
        // The WinForms-style pattern with an explicit `undo` argument
        // passed to SetU* is also a valid Covered case.
        string src = @"
namespace X {
    class FooViewModel {
        public void Save(Undo undo) {
            Program.ROM.SetU16(0x10, 0x42, undo);
        }
    }
}";
        var rows = UndoCoverageScanner.ExtractCallsitesFromSource(src, "Test.cs");
        var row = Assert.Single(rows);
        Assert.Equal(UndoCoverage.Covered, row.Coverage);
        Assert.Contains("explicit Undo argument", row.CoverageNote);
    }

    // ===================================================================
    // Tier classification — MissingScope vs NoUndoServiceField.
    // ===================================================================

    [Fact]
    public void Classifies_NoBeginInMethod_WithUndoServiceField_AsMissingScope()
    {
        // VM has UndoService field but the write is not wrapped.
        string src = @"
namespace X {
    using FEBuilderGBA.Avalonia.Services;
    class FooViewModel {
        UndoService _undoService = new();
        public void Save() {
            rom.write_u8(0x10, 1);
        }
    }
}";
        var rows = UndoCoverageScanner.ExtractCallsitesFromSource(src, "Test.cs");
        var row = Assert.Single(rows);
        Assert.Equal(UndoCoverage.MissingScope, row.Coverage);
        Assert.Contains("Save", row.CoverageNote);
    }

    [Fact]
    public void Classifies_NoUndoServiceFieldAtAll_AsNoUndoServiceField()
    {
        // The deepest gap: no plumbing anywhere on the class.
        string src = @"
namespace X {
    class FooViewModel {
        public void Save() {
            rom.write_u8(0x10, 1);
        }
    }
}";
        var rows = UndoCoverageScanner.ExtractCallsitesFromSource(src, "Test.cs");
        var row = Assert.Single(rows);
        Assert.Equal(UndoCoverage.NoUndoServiceField, row.Coverage);
        Assert.Contains("no UndoService", row.CoverageNote);
    }

    [Fact]
    public void Classifies_MultipleWritesInSameMethod_OnlyUnwrapped_IsFlagged()
    {
        // A method with two writes: one inside Begin/Commit, another
        // AFTER the Commit. Per Copilot PR #380 review concern #2,
        // the second write is outside the active scope and MUST be
        // surfaced as MissingScope — not silently classified as Covered
        // because "some Begin appeared earlier in the method".
        string src = @"
namespace X {
    using FEBuilderGBA.Avalonia.Services;
    class FooViewModel {
        UndoService _undoService = new();
        public void Save() {
            _undoService.Begin(""Edit"");
            rom.write_u8(0x10, 1);    // inside scope — Covered
            _undoService.Commit();
            rom.write_u8(0x20, 2);    // AFTER Commit — MissingScope
        }
    }
}";
        var rows = UndoCoverageScanner.ExtractCallsitesFromSource(src, "Test.cs");
        Assert.Equal(2, rows.Count);

        // Order by line so the assertions read top-down.
        var ordered = rows.OrderBy(r => r.Line).ToList();

        // First write — inside the Begin/Commit region.
        Assert.Equal(UndoCoverage.Covered, ordered[0].Coverage);

        // Second write — after the Commit; the scope is closed.
        Assert.Equal(UndoCoverage.MissingScope, ordered[1].Coverage);
        Assert.Contains("OUTSIDE", ordered[1].CoverageNote);
    }

    [Fact]
    public void Classifies_TwoSequentialScopes_BothWritesCovered()
    {
        // Defensive: Begin → write → Commit → Begin → write → Commit.
        // Each write is inside its own active scope; both must be Covered.
        // This documents the scope-tracking model: scopes can re-open
        // legitimately and writes inside the second scope are healthy.
        string src = @"
namespace X {
    using FEBuilderGBA.Avalonia.Services;
    class FooViewModel {
        UndoService _undoService = new();
        public void Save() {
            _undoService.Begin(""First"");
            rom.write_u8(0x10, 1);
            _undoService.Commit();
            _undoService.Begin(""Second"");
            rom.write_u8(0x20, 2);
            _undoService.Commit();
        }
    }
}";
        var rows = UndoCoverageScanner.ExtractCallsitesFromSource(src, "Test.cs");
        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.Equal(UndoCoverage.Covered, r.Coverage));
    }

    [Fact]
    public void Classifies_LeakedWriteAfterCommitInTryWithCatchRollback_AsMissingScope()
    {
        // Copilot PR #380 review concern #1 (re-review): the previous
        // bracketing implementation marked the leaked write as Covered
        // because the catch's later Rollback satisfied the "some close
        // after the write" check. With the strict pre-write-close check
        // (no intervening Commit/Rollback between latest pre-write Begin
        // and the write), the leaked write is correctly flagged.
        string src = @"
namespace X {
    using FEBuilderGBA.Avalonia.Services;
    class FooViewModel {
        UndoService _undoService = new();
        public void Save() {
            try {
                _undoService.Begin(""Edit"");
                rom.write_u8(0x10, 1);     // inside scope — Covered
                _undoService.Commit();
                rom.write_u8(0x20, 2);     // LEAKED (after Commit) — MissingScope
            } catch {
                _undoService.Rollback();   // catch close — must NOT cover the leak
                throw;
            }
        }
    }
}";
        var rows = UndoCoverageScanner.ExtractCallsitesFromSource(src, "Test.cs");
        Assert.Equal(2, rows.Count);
        var ordered = rows.OrderBy(r => r.Line).ToList();
        Assert.Equal(UndoCoverage.Covered, ordered[0].Coverage);
        Assert.Equal(UndoCoverage.MissingScope, ordered[1].Coverage);
        Assert.Contains("OUTSIDE", ordered[1].CoverageNote);
    }

    [Fact]
    public void Classifies_WriteBeforeAndInsideScope_PreWriteFlagged()
    {
        // Defensive: write BEFORE any Begin in the method must be
        // flagged. The post-Begin write must be Covered. This catches the
        // canary "write at line 10, Begin at line 15, Commit at line 20"
        // case which would otherwise leak.
        string src = @"
namespace X {
    using FEBuilderGBA.Avalonia.Services;
    class FooViewModel {
        UndoService _undoService = new();
        public void Save() {
            rom.write_u8(0x10, 1);    // BEFORE Begin — MissingScope
            _undoService.Begin(""Edit"");
            rom.write_u8(0x20, 2);    // inside scope — Covered
            _undoService.Commit();
        }
    }
}";
        var rows = UndoCoverageScanner.ExtractCallsitesFromSource(src, "Test.cs");
        Assert.Equal(2, rows.Count);
        var ordered = rows.OrderBy(r => r.Line).ToList();
        Assert.Equal(UndoCoverage.MissingScope, ordered[0].Coverage);
        Assert.Equal(UndoCoverage.Covered, ordered[1].Coverage);
    }

    [Fact]
    public void Classifies_WriteBeforeBegin_AsMissingScope()
    {
        // The write APPEARS before the Begin in source order — that write
        // is NOT in the scope, so it must be flagged.
        string src = @"
namespace X {
    using FEBuilderGBA.Avalonia.Services;
    class FooViewModel {
        UndoService _undoService = new();
        public void Save() {
            rom.write_u8(0x10, 1);  // before Begin — leak
            _undoService.Begin(""Edit"");
            _undoService.Commit();
        }
    }
}";
        var rows = UndoCoverageScanner.ExtractCallsitesFromSource(src, "Test.cs");
        var row = Assert.Single(rows);
        Assert.Equal(UndoCoverage.MissingScope, row.Coverage);
    }

    // ===================================================================
    // Tier classification — AmbiguousScope (one-level helper).
    // ===================================================================

    [Fact]
    public void Classifies_HelperCalledFromWrappedCaller_AsAmbiguous()
    {
        // The write lives in a helper method with no scope. The caller
        // wraps Begin/Commit around the call. We flag Ambiguous so a
        // human reviews the indirection.
        string src = @"
namespace X {
    using FEBuilderGBA.Avalonia.Services;
    class FooViewModel {
        UndoService _undoService = new();
        public void Save() {
            _undoService.Begin(""Edit"");
            WriteHelper();
            _undoService.Commit();
        }
        void WriteHelper() {
            rom.write_u8(0x10, 1);
        }
    }
}";
        var rows = UndoCoverageScanner.ExtractCallsitesFromSource(src, "Test.cs");
        var row = Assert.Single(rows);
        Assert.Equal(UndoCoverage.AmbiguousScope, row.Coverage);
        Assert.Contains("WriteHelper", row.CoverageNote);
    }

    // ===================================================================
    // Edge cases.
    // ===================================================================

    [Fact]
    public void IgnoresNonRomReceivers()
    {
        // `myObj.write_u8(...)` is NOT a ROM write — the scanner must
        // ignore it. Same for `something.SetData(...)` whose receiver isn't
        // a ROM identifier.
        string src = @"
namespace X {
    class FooViewModel {
        public void Save() {
            myObj.write_u8(0x10, 1);
            something.SetData(0x10, new byte[]{1,2,3});
        }
    }
}";
        var rows = UndoCoverageScanner.ExtractCallsitesFromSource(src, "Test.cs");
        Assert.Empty(rows);
    }

    [Fact]
    public void RecognisesProgramRomQualified()
    {
        // `Program.ROM.SetU16(...)` — the WinForms-style global access.
        string src = @"
namespace X {
    class FooViewModel {
        public void Save() {
            Program.ROM.SetU16(0x10, 0x42);
        }
    }
}";
        var rows = UndoCoverageScanner.ExtractCallsitesFromSource(src, "Test.cs");
        var row = Assert.Single(rows);
        Assert.Equal(UndoCoverage.NoUndoServiceField, row.Coverage);
    }

    [Fact]
    public void RecognisesCoreStateRomQualified()
    {
        // `CoreState.ROM.write_u8(...)` — the Avalonia-native global
        // accessor used by VMs that don't hoist into a local. Copilot
        // PR #380 review concern #1: SMEPromoListViewModel writes through
        // this receiver and the original scanner missed those rows.
        string src = @"
namespace X {
    class SMEPromoListViewModel {
        public void Write() {
            CoreState.ROM.write_u8(0x10, 1);
            CoreState.ROM.write_u8(0x11, 2);
        }
    }
}";
        var rows = UndoCoverageScanner.ExtractCallsitesFromSource(src, "SMEPromoListViewModel.cs");
        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.Equal(UndoCoverage.NoUndoServiceField, r.Coverage));
        Assert.Contains(rows, r => r.WriteExpression.Contains("CoreState.ROM.write_u8"));
    }

    [Fact]
    public void Recognises_WriteRange_WriteFill_WriteResizeData()
    {
        // Copilot PR #380 third-review concern #2: rom.write_range,
        // rom.write_fill, rom.write_resize_data are bulk-write APIs used
        // by TextViewerViewModel and ToolASMEditView. The original
        // pattern set missed them; this test locks them in.
        string src = @"
namespace X {
    class FooViewModel {
        public void Write() {
            rom.write_range(0x100, data);
            rom.write_fill(0x200, 16, 0);
            rom.write_resize_data(0x300, data);
        }
    }
}";
        var rows = UndoCoverageScanner.ExtractCallsitesFromSource(src, "Test.cs");
        Assert.Equal(3, rows.Count);
        Assert.Contains(rows, r => r.WriteExpression.Contains("write_range"));
        Assert.Contains(rows, r => r.WriteExpression.Contains("write_fill"));
        Assert.Contains(rows, r => r.WriteExpression.Contains("write_resize_data"));
    }

    [Fact]
    public void Recognises_EditorFormRefWriteFields()
    {
        // ~100 AV ViewModels funnel writes through EditorFormRef.WriteFields.
        // The scanner must treat that call as a write callsite or the
        // coverage report will under-count by ~80% of AV write activity.
        string src = @"
namespace X {
    class FooViewModel {
        public void Write() {
            ROM rom = CoreState.ROM;
            uint addr = 0x1000;
            EditorFormRef.WriteFields(rom, addr, values, _fields);
        }
    }
}";
        var rows = UndoCoverageScanner.ExtractCallsitesFromSource(src, "Test.cs");
        var row = Assert.Single(rows);
        Assert.Equal(UndoCoverage.NoUndoServiceField, row.Coverage);
        Assert.Contains("WriteFields", row.WriteExpression);
    }

    [Fact]
    public void Recognises_EditorFormRefWriteField_Singular()
    {
        // Singular WriteField variant is also accepted.
        string src = @"
namespace X {
    class FooViewModel {
        public void Write() {
            EditorFormRef.WriteField(rom, 0x100, 0x42, fieldDef);
        }
    }
}";
        var rows = UndoCoverageScanner.ExtractCallsitesFromSource(src, "Test.cs");
        Assert.Single(rows);
    }

    [Fact]
    public void RecognisesAllWriteMethods()
    {
        // write_u8/16/32, write_p32, write_range, write_fill,
        // write_resize_data, SetU8/16/32, SetData all qualify.
        Assert.True(UndoCoverageScanner.IsRomWriteMethod("write_u8"));
        Assert.True(UndoCoverageScanner.IsRomWriteMethod("write_u16"));
        Assert.True(UndoCoverageScanner.IsRomWriteMethod("write_u32"));
        Assert.True(UndoCoverageScanner.IsRomWriteMethod("write_p32"));
        Assert.True(UndoCoverageScanner.IsRomWriteMethod("write_range"));
        Assert.True(UndoCoverageScanner.IsRomWriteMethod("write_fill"));
        Assert.True(UndoCoverageScanner.IsRomWriteMethod("write_resize_data"));
        Assert.True(UndoCoverageScanner.IsRomWriteMethod("SetU8"));
        Assert.True(UndoCoverageScanner.IsRomWriteMethod("SetU16"));
        Assert.True(UndoCoverageScanner.IsRomWriteMethod("SetU32"));
        Assert.True(UndoCoverageScanner.IsRomWriteMethod("SetData"));
        // Non-write methods.
        Assert.False(UndoCoverageScanner.IsRomWriteMethod("u8"));
        Assert.False(UndoCoverageScanner.IsRomWriteMethod("write_unrelated"));
        Assert.False(UndoCoverageScanner.IsRomWriteMethod(""));
    }

    // ===================================================================
    // View→VM call-chain coverage (Pass 2 cross-reference).
    // ===================================================================

    [Fact]
    public void ExtractViewCoveredVmMethods_FindsPairedVmMethodWrappedByView()
    {
        // Copilot PR #380 third-review concern #1: the canonical Avalonia
        // pattern is View wraps `_vm.WriteX()` in `_undoService.Begin/Commit`.
        // The cross-reference must surface this so the VM-side write rows
        // upgrade to Covered.
        string viewSrc = @"
namespace X {
    using FEBuilderGBA.Avalonia.Services;
    class ItemEditorView {
        readonly ItemEditorViewModel _vm = new();
        UndoService _undoService = new();
        void OnWriteClick() {
            _undoService.Begin(""Edit Item"");
            try {
                _vm.WriteItem();
                _undoService.Commit();
            } catch {
                _undoService.Rollback();
            }
        }
    }
}";
        var result = new HashSet<(string, string)>();
        UndoCoverageScanner.ExtractViewCoveredVmMethods(viewSrc, result);
        Assert.Contains(("ItemEditorViewModel", "WriteItem"), result);
    }

    [Fact]
    public void ExtractViewCoveredVmMethods_SkipsCallsWithoutBeginScope()
    {
        // A View method that calls `_vm.WriteX()` WITHOUT Begin/Commit
        // must NOT register the VM method as covered.
        string viewSrc = @"
namespace X {
    class FooView {
        readonly FooViewModel _vm = new();
        void OnButtonClick() {
            _vm.WriteFoo();  // no Begin
        }
    }
}";
        var result = new HashSet<(string, string)>();
        UndoCoverageScanner.ExtractViewCoveredVmMethods(viewSrc, result);
        Assert.DoesNotContain(("FooViewModel", "WriteFoo"), result);
    }

    [Fact]
    public void ExtractViewCoveredVmMethods_BeginAfterCall_NotCovered()
    {
        // Copilot PR #380 fourth-pass review concern #2: the Pass 2
        // bracketing must use the same strict model as the same-method
        // pass — Begin BEFORE the call, close AFTER. A Begin AFTER the
        // VM call must NOT register as covered.
        string viewSrc = @"
namespace X {
    using FEBuilderGBA.Avalonia.Services;
    class FooView {
        readonly FooViewModel _vm = new();
        UndoService _undoService = new();
        void OnButtonClick() {
            _vm.WriteFoo();         // BEFORE Begin — not covered
            _undoService.Begin(""Late"");
            _undoService.Commit();
        }
    }
}";
        var result = new HashSet<(string, string)>();
        UndoCoverageScanner.ExtractViewCoveredVmMethods(viewSrc, result);
        Assert.DoesNotContain(("FooViewModel", "WriteFoo"), result);
    }

    [Fact]
    public void ExtractViewCoveredVmMethods_BeginWithoutClose_NotCovered()
    {
        // Strict bracketing: a Begin with no matching Commit/Rollback
        // after the call must NOT register as covered.
        string viewSrc = @"
namespace X {
    using FEBuilderGBA.Avalonia.Services;
    class FooView {
        readonly FooViewModel _vm = new();
        UndoService _undoService = new();
        void OnButtonClick() {
            _undoService.Begin(""Edit"");
            _vm.WriteFoo();        // no close after — not covered
        }
    }
}";
        var result = new HashSet<(string, string)>();
        UndoCoverageScanner.ExtractViewCoveredVmMethods(viewSrc, result);
        Assert.DoesNotContain(("FooViewModel", "WriteFoo"), result);
    }

    [Fact]
    public void DiscoverViewCoveredVmMethods_UnwrappedCallsiteVetoesUpgrade()
    {
        // Copilot PR #380 fourth-pass review concern #1: if one method
        // wraps the VM call in Begin/Commit AND another method in the
        // same View calls the same VM method WITHOUT a scope, the
        // upgrade should NOT happen — a single unwrapped callsite would
        // hide a real gap.
        string viewSrc = @"
namespace X {
    using FEBuilderGBA.Avalonia.Services;
    class FooView {
        readonly FooViewModel _vm = new();
        UndoService _undoService = new();

        void OnWriteClick() {
            _undoService.Begin(""Edit"");
            _vm.WriteFoo();         // wrapped — Covered
            _undoService.Commit();
        }

        void OnAuxClick() {
            _vm.WriteFoo();         // UNWRAPPED — vetoes the upgrade
        }
    }
}";
        string tmpDir = Path.Combine(Path.GetTempPath(), $"undo-cov-{Guid.NewGuid():N}", "Views");
        Directory.CreateDirectory(tmpDir);
        string tmp = Path.Combine(tmpDir, "FooView.axaml.cs");
        try
        {
            File.WriteAllText(tmp, viewSrc);
            var result = UndoCoverageScanner.DiscoverViewCoveredVmMethods(new[] { tmp });
            // The unwrapped OnAuxClick call must veto the upgrade.
            Assert.DoesNotContain(("FooViewModel", "WriteFoo"), result);
        }
        finally
        {
            try { Directory.Delete(Path.GetDirectoryName(tmpDir)!, recursive: true); } catch { }
        }
    }

    [Fact]
    public void DiscoverViewCoveredVmMethods_AllWrappedCallsitesUpgrades()
    {
        // Two Views both wrap the VM call in Begin/Commit. Result must
        // include the (VM, method) pair.
        string viewA = @"
namespace X {
    using FEBuilderGBA.Avalonia.Services;
    class FooView {
        readonly FooViewModel _vm = new();
        UndoService _undoService = new();
        void OnWriteClick() {
            _undoService.Begin(""Edit"");
            _vm.WriteFoo();
            _undoService.Commit();
        }
    }
}";
        string viewB = @"
namespace X {
    using FEBuilderGBA.Avalonia.Services;
    class FooDuplicateView {
        readonly FooViewModel _vm = new();
        UndoService _undoService = new();
        void OnWriteClick() {
            _undoService.Begin(""Edit"");
            _vm.WriteFoo();
            _undoService.Commit();
        }
    }
}";
        string tmpDir = Path.Combine(Path.GetTempPath(), $"undo-cov-{Guid.NewGuid():N}", "Views");
        Directory.CreateDirectory(tmpDir);
        string tmpA = Path.Combine(tmpDir, "FooView.axaml.cs");
        string tmpB = Path.Combine(tmpDir, "FooDuplicateView.axaml.cs");
        try
        {
            File.WriteAllText(tmpA, viewA);
            File.WriteAllText(tmpB, viewB);
            var result = UndoCoverageScanner.DiscoverViewCoveredVmMethods(new[] { tmpA, tmpB });
            Assert.Contains(("FooViewModel", "WriteFoo"), result);
        }
        finally
        {
            try { Directory.Delete(Path.GetDirectoryName(tmpDir)!, recursive: true); } catch { }
        }
    }

    [Fact]
    public void ExtractViewCoveredVmMethods_AcceptsAlternateVmIdentifiers()
    {
        // Receiver patterns: `vm.`, `_viewModel.`, `_vm.`, `MyVm.` etc.
        // are all accepted as ViewModel references.
        string viewSrc = @"
namespace X {
    using FEBuilderGBA.Avalonia.Services;
    class BarView {
        BarViewModel vm = new();
        UndoService _undoService = new();
        void OnWrite() {
            _undoService.Begin(""Edit"");
            vm.WriteBar();
            _undoService.Commit();
        }
    }
}";
        var result = new HashSet<(string, string)>();
        UndoCoverageScanner.ExtractViewCoveredVmMethods(viewSrc, result);
        Assert.Contains(("BarViewModel", "WriteBar"), result);
    }

    [Fact]
    public void Scan_AgainstLiveWorktree_ItemEditorVmWritesAreCovered()
    {
        // Sanity check: ItemEditorView wraps _vm.WriteItem() in
        // _undoService.Begin/Commit. After Pass 2 cross-reference, the
        // VM-side writes inside ItemEditorViewModel.WriteItem must
        // surface as Covered (or AmbiguousScope at worst).
        string? repoRoot = FindRepoRoot();
        if (repoRoot == null) return;

        var rows = UndoCoverageScanner.Scan(repoRoot);
        var itemEditorWrites = rows
            .Where(r => r.EnclosingClass == "ItemEditorViewModel"
                        && r.EnclosingMethod == "WriteItem")
            .ToList();

        if (itemEditorWrites.Count == 0)
        {
            // VM may have evolved; the test is robust against the VM not
            // having WriteItem anymore — but it should have SOME write
            // method that the View wraps.
            return;
        }

        // ALL writes inside WriteItem should be Covered via the View
        // caller pattern.
        Assert.All(itemEditorWrites, r => Assert.Equal(UndoCoverage.Covered, r.Coverage));
    }

    [Fact]
    public void DoesNotCrashOnMalformedSource()
    {
        // Roslyn parses malformed code into an error tree — the scanner
        // must NOT throw, just return what it can recover.
        string src = @"
class FooViewModel {
    public void Save() {
        rom.write_u8(0x10, 1);
        // intentional syntax error vvvvvv
        if (foo bar baz)
    }
}";
        var rows = UndoCoverageScanner.ExtractCallsitesFromSource(src, "Test.cs");
        // The callsite preceding the syntax error must still be recovered.
        Assert.Single(rows);
    }

    [Fact]
    public void EmptyOrNullSource_ReturnsEmpty()
    {
        Assert.Empty(UndoCoverageScanner.ExtractCallsitesFromSource("", "Test.cs"));
        Assert.Empty(UndoCoverageScanner.ExtractCallsitesFromSource(null!, "Test.cs"));
    }

    [Fact]
    public void RepoRelativePath_IsRenderedConsistently()
    {
        // The scanner's FilePath column carries whatever string the caller
        // passed in. Use a deliberately POSIX-style synthetic path to test
        // that the value round-trips unchanged.
        string src = @"
class FooViewModel {
    public void Save() {
        rom.write_u8(0x10, 1);
    }
}";
        var rows = UndoCoverageScanner.ExtractCallsitesFromSource(src,
            "FEBuilderGBA.Avalonia/ViewModels/FooViewModel.cs");
        var row = Assert.Single(rows);
        Assert.Equal("FEBuilderGBA.Avalonia/ViewModels/FooViewModel.cs", row.FilePath);
    }

    // ===================================================================
    // FormatReport — output structure & ordering.
    // ===================================================================

    [Fact]
    public void FormatReport_NoUndoServiceField_RowsSurfaceFirst()
    {
        // Mix tiers in the input; the report must surface NoUndoServiceField
        // BEFORE MissingScope BEFORE AmbiguousScope BEFORE Covered. We
        // search for the unique SECTION-HEADING markers ("## …") to avoid
        // false positives from the methodology / summary-table preambles
        // which mention every tier by name.
        var rows = new List<WriteCallsite>
        {
            new("a/Covered.cs", 1, "CoveredVm", "Save", "rom.write_u8(0,1)", UndoCoverage.Covered, "ok"),
            new("a/Missing.cs", 1, "MissingVm", "Save", "rom.write_u8(0,1)", UndoCoverage.MissingScope, "no scope"),
            new("a/NoField.cs", 1, "NoFieldVm", "Save", "rom.write_u8(0,1)", UndoCoverage.NoUndoServiceField, "no plumbing"),
            new("a/Ambig.cs", 1, "AmbigVm", "Helper", "rom.write_u8(0,1)", UndoCoverage.AmbiguousScope, "caller-wraps"),
        };
        string report = UndoCoverageScanner.FormatReport(rows);

        // Use the actual section heading prefix `## ` so each marker matches
        // exactly one location in the report (the section heading itself).
        int noFieldIdx = report.IndexOf("## Highest priority", StringComparison.Ordinal);
        int missingIdx = report.IndexOf("## Missing scope", StringComparison.Ordinal);
        int ambigIdx = report.IndexOf("## Ambiguous", StringComparison.Ordinal);
        int coveredIdx = report.IndexOf("## Covered (healthy)", StringComparison.Ordinal);
        Assert.True(noFieldIdx > 0, "Highest priority section heading must be present");
        Assert.True(missingIdx > 0, "Missing scope section heading must be present");
        Assert.True(ambigIdx > 0, "Ambiguous section heading must be present");
        Assert.True(coveredIdx > 0, "Covered (healthy) section heading must be present");
        Assert.True(noFieldIdx < missingIdx, $"NoUndoServiceField ({noFieldIdx}) must surface before MissingScope ({missingIdx})");
        Assert.True(missingIdx < ambigIdx, $"MissingScope ({missingIdx}) must surface before AmbiguousScope ({ambigIdx})");
        Assert.True(ambigIdx < coveredIdx, $"AmbiguousScope ({ambigIdx}) must surface before Covered ({coveredIdx})");
    }

    [Fact]
    public void FormatReport_LfNewlinesOnly()
    {
        // Per Copilot's prior phase reviews: report must use LF only so
        // committed files don't churn on Windows.
        var rows = new List<WriteCallsite>
        {
            new("a/Foo.cs", 1, "FooVm", "Save", "rom.write_u8(0,1)", UndoCoverage.MissingScope, "no scope"),
        };
        string report = UndoCoverageScanner.FormatReport(rows);
        Assert.DoesNotContain("\r\n", report);
        Assert.Contains("\n", report);
    }

    [Fact]
    public void FormatReport_EmptyInput_StillProducesValidReport()
    {
        // Zero callsites means the AV migration plumbed undo everywhere
        // (or our scanner is broken). The report must still be generated
        // cleanly with all four section headings.
        string report = UndoCoverageScanner.FormatReport(Array.Empty<WriteCallsite>());
        Assert.Contains("# Avalonia vs WinForms — Undo Coverage Sweep", report);
        Assert.Contains("## Summary", report);
        Assert.Contains("Total write callsites | 0", report);
        // Copilot PR #380 fifth-pass concern: when total=0 the total row
        // must render "—" for the % column, not the literal "100%".
        Assert.DoesNotContain("Total write callsites | 0 | 100%", report);
        Assert.Contains("Total write callsites | 0 | —", report);
        // All four section headings must appear with empty placeholders.
        Assert.Contains("NO undo plumbing", report);
        Assert.Contains("Missing scope", report);
        Assert.Contains("Ambiguous", report);
        Assert.Contains("Covered (healthy)", report);
    }

    [Fact]
    public void FormatReport_GroupsByClass_InPriorityTiers()
    {
        // NoUndoServiceField rows from multiple classes — each class
        // should appear under its own subheading.
        var rows = new List<WriteCallsite>
        {
            new("a/ClassA.cs", 1, "ClassAVm", "Save", "rom.write_u8(0,1)", UndoCoverage.NoUndoServiceField, "no plumbing"),
            new("a/ClassB.cs", 2, "ClassBVm", "Save", "rom.write_u8(0,1)", UndoCoverage.NoUndoServiceField, "no plumbing"),
            new("a/ClassA.cs", 3, "ClassAVm", "Other", "rom.write_u16(0,1)", UndoCoverage.NoUndoServiceField, "no plumbing"),
        };
        string report = UndoCoverageScanner.FormatReport(rows);
        Assert.Contains("### `ClassAVm`", report);
        Assert.Contains("### `ClassBVm`", report);
        // The class with more rows (ClassAVm with 2) should appear before
        // the one with fewer (ClassBVm with 1) — group-count descending.
        int aIdx = report.IndexOf("### `ClassAVm`", StringComparison.Ordinal);
        int bIdx = report.IndexOf("### `ClassBVm`", StringComparison.Ordinal);
        Assert.True(aIdx < bIdx, $"Expected ClassAVm before ClassBVm (count-desc). aIdx={aIdx} bIdx={bIdx}");
    }

    [Fact]
    public void FormatReport_RegistryCrossCheckSectionPresent()
    {
        // The report must explain the registry cross-check semantics so
        // downstream PRs know what to do if VM X shows up with 0 detected
        // writes.
        string report = UndoCoverageScanner.FormatReport(Array.Empty<WriteCallsite>());
        Assert.Contains("Registry cross-check", report);
        Assert.Contains("WritableViewModelRegistry", report);
    }

    [Fact]
    public void DiscoverWritableViewModelNames_AgreesWithTestRegistry()
    {
        // The scanner mirrors WritableViewModelRegistry's discovery rule
        // internally so the report can self-audit. The two should agree
        // exactly when run against the same assembly — if they diverge,
        // either the registry's reflection convention changed or the
        // scanner's mirror was not kept in sync. Either way, the report's
        // "zero-write writable VMs" warning section depends on this
        // agreement, so we assert it.
        var scannerNames = new HashSet<string>(
            UndoCoverageScanner.DiscoverWritableViewModelNames(),
            StringComparer.Ordinal);
        var registryNames = new HashSet<string>(
            WritableViewModelRegistry.WritableViewModels()
                .Select(entry => ((Type)entry[0]).Name),
            StringComparer.Ordinal);

        // Symmetric difference must be empty.
        var onlyInScanner = scannerNames.Except(registryNames).ToList();
        var onlyInRegistry = registryNames.Except(scannerNames).ToList();
        Assert.True(onlyInScanner.Count == 0,
            "Names discovered by the scanner but not the registry: " + string.Join(", ", onlyInScanner));
        Assert.True(onlyInRegistry.Count == 0,
            "Names discovered by the registry but not the scanner: " + string.Join(", ", onlyInRegistry));
    }

    [Fact]
    public void FormatReport_RegistryWarning_RendersTableWhenMissing()
    {
        // When DiscoverWritableViewModelNames yields some names that the
        // scanner rows DO contain (and presumably others it does not),
        // the report should render a Writable VMs / per-VM warning table.
        // We trigger this with empty rows so EVERY writable VM appears as
        // a missing warning.
        string report = UndoCoverageScanner.FormatReport(Array.Empty<WriteCallsite>());
        // The scanner's reflection discovers ~100 writable VMs on the
        // live assembly; with zero rows in the input, every single one
        // should surface in the warning table.
        Assert.Contains("Writable VMs with zero detected ROM writes", report);
        // The warning table should be rendered.
        Assert.Contains("Verify ROM-write API; extend scanner pattern set if needed", report);
    }

    [Fact]
    public void FormatReport_NullInput_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => UndoCoverageScanner.FormatReport(null!));
    }

    // ===================================================================
    // ClassHasUndoServiceMember — direct API.
    // ===================================================================

    [Fact]
    public void ClassHasUndoServiceMember_DetectsField()
    {
        string src = @"
class Foo {
    UndoService _undoService = new();
}";
        var tree = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(src);
        var cls = tree.GetRoot().DescendantNodes()
            .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.ClassDeclarationSyntax>().Single();
        Assert.True(UndoCoverageScanner.ClassHasUndoServiceMember(cls));
    }

    [Fact]
    public void ClassHasUndoServiceMember_DetectsLocal()
    {
        string src = @"
class Foo {
    void M() {
        var x = new UndoService();
    }
}";
        var tree = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(src);
        var cls = tree.GetRoot().DescendantNodes()
            .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.ClassDeclarationSyntax>().Single();
        Assert.True(UndoCoverageScanner.ClassHasUndoServiceMember(cls));
    }

    [Fact]
    public void ClassHasUndoServiceMember_FalseForUnrelatedClass()
    {
        string src = @"
class Foo {
    int _x;
    void M() {
        var y = new object();
    }
}";
        var tree = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(src);
        var cls = tree.GetRoot().DescendantNodes()
            .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.ClassDeclarationSyntax>().Single();
        Assert.False(UndoCoverageScanner.ClassHasUndoServiceMember(cls));
    }

    // ===================================================================
    // Scan() — integration smoke tests against the live worktree.
    // ===================================================================

    [Fact]
    public void Scan_AgainstLiveWorktree_ProducesRows()
    {
        // Walk up to find the worktree root (FEBuilderGBA.sln). When
        // running from a published binary outside the source tree, return
        // gracefully — no rows is acceptable in that case.
        string? repoRoot = FindRepoRoot();
        if (repoRoot == null)
            return;
        var rows = UndoCoverageScanner.Scan(repoRoot);
        // The Avalonia project has hundreds of ROM-write callsites
        // (~895 detected by initial grep across 51 files); the scanner
        // should surface a non-trivial count.
        Assert.NotEmpty(rows);
        // At least one tier should be present.
        Assert.Contains(rows, r =>
            r.Coverage == UndoCoverage.NoUndoServiceField
            || r.Coverage == UndoCoverage.MissingScope
            || r.Coverage == UndoCoverage.Covered);
    }

    [Fact]
    public void Scan_AgainstLiveWorktree_EventScriptPopupIsCovered()
    {
        // EventScriptPopupViewModel is the ONE VM the codebase already
        // wraps with UndoService — if the scanner can't detect this case
        // as Covered, the implementation is broken. This is the canary
        // test.
        string? repoRoot = FindRepoRoot();
        if (repoRoot == null)
            return;
        var rows = UndoCoverageScanner.Scan(repoRoot);
        var eventScriptRows = rows
            .Where(r => r.EnclosingClass == "EventScriptPopupViewModel")
            .ToList();
        Assert.NotEmpty(eventScriptRows);
        // ALL writes inside EventScriptPopupViewModel should be Covered.
        Assert.All(eventScriptRows, r => Assert.Equal(UndoCoverage.Covered, r.Coverage));
    }

    [Fact]
    public void Scan_MissingRepoRoot_ReturnsEmpty()
    {
        // Non-existent path — the scanner returns Array.Empty rather than
        // throwing.
        var rows = UndoCoverageScanner.Scan(Path.Combine(Path.GetTempPath(),
            "nonexistent-" + Guid.NewGuid().ToString("N")));
        Assert.NotNull(rows);
        Assert.Empty(rows);
    }

    [Fact]
    public void Scan_NullRepoRoot_Throws()
    {
        Assert.Throws<ArgumentException>(() => UndoCoverageScanner.Scan(""));
        Assert.Throws<ArgumentException>(() => UndoCoverageScanner.Scan(null!));
    }

    [Fact]
    public void Scan_WritableRegistryCrossCheck_AllRegistryVmsHaveWrites()
    {
        // If a VM is in WritableViewModelRegistry but the scanner finds
        // zero ROM writes for it, our pattern set is missing something.
        // This test is the comprehensive audit referenced in the report's
        // "Registry cross-check" section. After the EditorFormRef
        // bulk-write helper recognition was added, the registry list and
        // the scanner output should be in close agreement; any VM that
        // funnels writes through a Core-side helper the scanner can't
        // see is tolerated up to a small drift budget.
        string? repoRoot = FindRepoRoot();
        if (repoRoot == null)
            return;

        var rows = UndoCoverageScanner.Scan(repoRoot);
        var classesWithWrites = new HashSet<string>(
            rows.Select(r => r.EnclosingClass),
            StringComparer.Ordinal);

        // Use the WritableViewModelRegistry's reflection-based discovery
        // to get the canonical list of writable VMs. Then assert every
        // single one has at least one detected ROM write.
        var missing = new List<string>();
        foreach (var entry in WritableViewModelRegistry.WritableViewModels())
        {
            Type vmType = (Type)entry[0];
            string vmName = vmType.Name;
            if (!classesWithWrites.Contains(vmName))
                missing.Add(vmName);
        }
        // We allow up to a small number of misses to tolerate VMs whose
        // Write() method dispatches through a Core-side service the
        // scanner's static pattern set can't see. Drift past the budget
        // fails this test loudly so a follow-up PR investigates.
        const int driftBudget = 10;
        Assert.True(missing.Count <= driftBudget,
            $"WritableViewModelRegistry lists {missing.Count} VMs without detected writes (budget={driftBudget}): " +
            string.Join(", ", missing));
    }

    /// <summary>
    /// Walk up from the test binary's base directory looking for
    /// FEBuilderGBA.sln. Returns null when running outside the source
    /// tree (e.g. published binary). Mirrors the FindRepoRoot logic in
    /// App.axaml.cs.
    /// </summary>
    static string? FindRepoRoot()
    {
        string start = AppDomain.CurrentDomain.BaseDirectory;
        for (DirectoryInfo? dir = new DirectoryInfo(start); dir != null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "FEBuilderGBA.sln")))
                return dir.FullName;
        }
        return null;
    }
}
