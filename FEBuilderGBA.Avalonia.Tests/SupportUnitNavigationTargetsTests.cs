// SPDX-License-Identifier: GPL-3.0-or-later
// Tests for the SupportUnit + Unit navigation manifests (#358 / #437 / #436).
//
// Asserts the INavigationTargetSource entries that drive Phase 4 gap-sweep
// parity reporting are wired correctly to the right Avalonia view types.
using System.Linq;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;
using FEBuilderGBA.Avalonia.Views;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests
{
    public class SupportUnitNavigationTargetsTests
    {
        [Fact]
        public void SupportUnitEditorViewModel_DeclaresTalkAndUnitJumps()
        {
            var vm = new SupportUnitEditorViewModel();
            var targets = vm.GetNavigationTargets();
            Assert.NotNull(targets);
            // SupportTalk jumps for all 3 ROM families.
            Assert.Contains(targets, t => t.TargetViewType == typeof(SupportTalkFE6View));
            Assert.Contains(targets, t => t.TargetViewType == typeof(SupportTalkFE7View));
            Assert.Contains(targets, t => t.TargetViewType == typeof(SupportTalkView));
            // Source-unit back-jumps for the FE7/8 families this VM serves.
            Assert.Contains(targets, t => t.TargetViewType == typeof(UnitEditorView));
            Assert.Contains(targets, t => t.TargetViewType == typeof(UnitFE7View));
        }

        [Fact]
        public void SupportUnitFE6ViewModel_DeclaresFE6JumpsOnly()
        {
            var vm = new SupportUnitFE6ViewModel();
            var targets = vm.GetNavigationTargets();
            Assert.NotNull(targets);
            Assert.Contains(targets, t => t.TargetViewType == typeof(SupportTalkFE6View));
            Assert.Contains(targets, t => t.TargetViewType == typeof(UnitFE6View));
        }

        [Fact]
        public void UnitEditorViewModel_DeclaresSupportUnitJump()
        {
            var vm = new UnitEditorViewModel();
            var targets = vm.GetNavigationTargets();
            Assert.Contains(targets, t => t.TargetViewType == typeof(SupportUnitEditorView));
        }

        [Fact]
        public void UnitFE6ViewModel_DeclaresFE6SupportUnitJump()
        {
            var vm = new UnitFE6ViewModel();
            var targets = vm.GetNavigationTargets();
            Assert.Contains(targets, t => t.TargetViewType == typeof(SupportUnitFE6View));
        }

        [Fact]
        public void UnitFE7ViewModel_DeclaresFE7SupportUnitJump()
        {
            var vm = new UnitFE7ViewModel();
            var targets = vm.GetNavigationTargets();
            Assert.Contains(targets, t => t.TargetViewType == typeof(SupportUnitEditorView));
        }

        // The new JumpToUnitPair surface on the three SupportTalk viewmodels.
        // Mirrors the WinForms `SupportTalk*Form.JumpTo(unit1, unit2)` callers.
        [Fact]
        public void SupportTalkViewModel_HasFindAddrForUnitPairMethod()
        {
            var t = typeof(SupportTalkViewModel);
            Assert.NotNull(t.GetMethod("FindAddrForUnitPair"));
        }

        [Fact]
        public void SupportTalkFE6ViewModel_HasFindAddrForUnitPairMethod()
        {
            var t = typeof(SupportTalkFE6ViewModel);
            Assert.NotNull(t.GetMethod("FindAddrForUnitPair"));
        }

        [Fact]
        public void SupportTalkFE7ViewModel_HasFindAddrForUnitPairMethod()
        {
            var t = typeof(SupportTalkFE7ViewModel);
            Assert.NotNull(t.GetMethod("FindAddrForUnitPair"));
        }
    }
}
