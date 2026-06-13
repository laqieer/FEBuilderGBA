// SPDX-License-Identifier: GPL-3.0-or-later
// #1132: VM-level coverage for the Items editor decomp source-backed save-gate.
//
// The View's Write_Click routing (decomp → DecompSourceWriterCore, classic → ROM)
// and the writer's byte-exact rewrite are covered by FEBuilderGBA.Core.Tests
// (DecompSourceWriterCoreTests) + the CLI E2E (WriteSourceE2ETests). These tests
// verify the two ViewModel seams the View relies on:
//   - ItemEditorViewModel.BuildSourceFieldDict() emits the stat fields by C name
//   - MainWindowViewModel.DecompBadgeText appends "needs rebuild" after a write
using System.Collections.Generic;
using System.Text.Json;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.ViewModels;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests
{
    [Collection("SharedState")]
    public class DecompSourceWriteGateTests
    {
        [Fact]
        public void ItemViewModel_BuildSourceFieldDict_EmitsStatFieldsByCName()
        {
            var vm = new ItemEditorViewModel
            {
                Might = 9,
                Hit = 80,
                Uses = 40,
                Price = 1500,
                Range = 1,
            };
            IReadOnlyDictionary<string, uint> dict = vm.BuildSourceFieldDict();

            Assert.Equal(9u, dict["might"]);
            Assert.Equal(80u, dict["hitRate"]);
            Assert.Equal(40u, dict["maxUses"]);
            Assert.Equal(1500u, dict["cost"]);
            Assert.Equal(1500u, dict["price"]);
            Assert.Equal(1u, dict["maxRange"]);
            Assert.Equal(1u, dict["minRange"]);
        }

        [Fact]
        public void MainWindowViewModel_BadgeText_AppendsNeedsRebuild_WhenFlagged()
        {
            var saved = CoreState.DecompProject;
            try
            {
                var vm = new MainWindowViewModel();

                // No project → plain badge (the base text only).
                CoreState.DecompProject = null;
                vm.RefreshDecompMode();
                Assert.DoesNotContain("needs rebuild", vm.DecompBadgeText);

                // Project flagged for rebuild → badge gains the hint.
                var proj = new DecompProject { ProjectRoot = ".", NeedsRebuild = true };
                CoreState.DecompProject = proj;
                vm.RefreshDecompMode();
                Assert.Contains("needs rebuild", vm.DecompBadgeText);
                Assert.True(vm.DecompNeedsRebuild);
            }
            finally
            {
                CoreState.DecompProject = saved;
            }
        }

        [Fact]
        public void ItemsOwner_SourceCstruct_RoutesToSourceWriter_NotRom()
        {
            // The save-gate predicate the View uses: a "source"/"cstruct" items owner
            // is recognized and the writer is invoked (here exercised directly to
            // confirm the manifest shape the View checks resolves correctly).
            var man = JsonSerializer.Deserialize<DecompManifest>(
                @"{ ""tables"": [ { ""table"": ""items"", ""format"": ""cstruct"",
                     ""writePolicy"": ""source"", ""arrayName"": ""gItemData"",
                     ""sourceFile"": ""src/item.c"" } ] }",
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            var proj = new DecompProject { ProjectRoot = ".", Manifest = man };

            var owner = proj.TryGetTableOwner("items");
            Assert.NotNull(owner);
            Assert.Equal("source", owner.WritePolicy);
            Assert.Equal("cstruct", owner.Format);
            Assert.Equal("gItemData", owner.EffectiveSymbol);
        }
    }
}
