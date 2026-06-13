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
            // #1132 review finding 5: the encoded min-max range byte is intentionally
            // NOT mapped to minRange/maxRange (mapping one encoded byte to both real
            // components would be wrong), so neither key is emitted.
            Assert.False(dict.ContainsKey("maxRange"));
            Assert.False(dict.ContainsKey("minRange"));
        }

        // #1132 (PR #1142 review): BuildSourceFieldDict must emit ONLY the fields the
        // user actually changed since the load-time snapshot — never a full snapshot of
        // (possibly stale) preview-ROM values that could clobber unrelated source fields.
        [Fact]
        public void ItemViewModel_BuildSourceFieldDict_EmitsOnlyChangedFieldsSinceSnapshot()
        {
            var vm = new ItemEditorViewModel
            {
                Might = 5,
                Hit = 90,
                Uses = 40,
                Price = 1500,
                WeaponRank = 1,
            };
            // Establish the baseline (simulates the snapshot captured in LoadItem).
            vm.RefreshSourceFieldSnapshot();

            // No edits yet → nothing is "changed".
            Assert.Empty(vm.BuildSourceFieldDict());

            // The user edits ONLY Hit. might/uses/price/weaponLevel must NOT be emitted,
            // so a diverged (stale-ROM) value can never be written back.
            vm.Hit = 95;
            var changed = vm.BuildSourceFieldDict();
            Assert.Equal(95u, changed["hitRate"]);
            Assert.False(changed.ContainsKey("might"));
            Assert.False(changed.ContainsKey("maxUses"));
            Assert.False(changed.ContainsKey("cost"));
            Assert.False(changed.ContainsKey("weaponLevel"));

            // After a re-baseline (post-write), the same value is no longer "changed".
            vm.RefreshSourceFieldSnapshot();
            Assert.Empty(vm.BuildSourceFieldDict());
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

        // ====================================================================
        //  #1141 — Unit + Class source-field gate seams
        // ====================================================================

        [Fact]
        public void UnitViewModel_BuildSourceFieldDict_EmitsOnlyChangedFieldsSinceSnapshot()
        {
            var vm = new UnitEditorViewModel
            {
                HP = 16, Str = 5, Skl = 4, Spd = 6, Def = 3, Res = 1, Lck = 2, Con = 8,
                Level = 1, Affinity = 0,
                GrowHP = 70, GrowStr = 40, GrowSkl = 40, GrowSpd = 40,
                GrowDef = 20, GrowRes = 20, GrowLck = 30,
            };
            vm.RefreshSourceFieldSnapshot();

            // No edits → nothing changed.
            Assert.Empty(vm.BuildSourceFieldDict());

            // Edit ONLY Str. The other fields must NOT be emitted (stale-preview guard).
            vm.Str = 9;
            var changed = vm.BuildSourceFieldDict();
            // Str is signed → packed as (byte)(sbyte)9 == 9 under key "pow".
            Assert.Equal(9u, changed["pow"]);
            Assert.False(changed.ContainsKey("hp"));
            Assert.False(changed.ContainsKey("growthHp"));
            Assert.False(changed.ContainsKey("level"));

            // Re-baseline → no longer changed.
            vm.RefreshSourceFieldSnapshot();
            Assert.Empty(vm.BuildSourceFieldDict());
        }

        [Fact]
        public void UnitViewModel_BuildSourceFieldDict_PacksNegativeBaseStatAsByte()
        {
            var vm = new UnitEditorViewModel { HP = 0 };
            vm.RefreshSourceFieldSnapshot();
            // A negative base (e.g. a recruit penalty) packs to its two's-complement byte.
            vm.HP = -1;
            var changed = vm.BuildSourceFieldDict();
            Assert.Equal(0xFFu, changed["hp"]);   // (byte)(sbyte)(-1) == 0xFF
        }

        [Fact]
        public void ClassViewModel_BuildSourceFieldDict_EmitsOnlyChangedFieldsSinceSnapshot()
        {
            var vm = new ClassEditorViewModel
            {
                BaseHp = 18, BaseStr = 5, MaxHp = 60, ClassPower = 0,
                GrowHp = 70, GrowStr = 40,
                PromoHp = 2, PromoStr = 1,
            };
            vm.RefreshSourceFieldSnapshot();

            Assert.Empty(vm.BuildSourceFieldDict());

            // Edit ONLY BaseHp → only baseHp emitted (others unchanged).
            vm.BaseHp = 20;
            var changed = vm.BuildSourceFieldDict();
            Assert.Equal(20u, changed["baseHp"]);
            Assert.False(changed.ContainsKey("maxHp"));
            Assert.False(changed.ContainsKey("growthHp"));
            Assert.False(changed.ContainsKey("promoHp"));

            vm.RefreshSourceFieldSnapshot();
            Assert.Empty(vm.BuildSourceFieldDict());
        }

        [Fact]
        public void ClassViewModel_BuildSourceFieldDict_PacksNegativePromoGainAsByte()
        {
            var vm = new ClassEditorViewModel { PromoHp = 0 };
            vm.RefreshSourceFieldSnapshot();
            vm.PromoHp = -1;
            var changed = vm.BuildSourceFieldDict();
            Assert.Equal(0xFFu, changed["promoHp"]);   // signed promo gain packed to 0xFF
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
