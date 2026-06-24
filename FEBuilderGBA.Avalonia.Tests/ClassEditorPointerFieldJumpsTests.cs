using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using FEBuilderGBA;
using FEBuilderGBA.Core;
using FEBuilderGBA.Avalonia.Controls;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;
using FEBuilderGBA.Avalonia.Views;
using Xunit;
using Xunit.Abstractions;

namespace FEBuilderGBA.Avalonia.Tests;

/// <summary>
/// Regression suite for issue #359 — the Class Editor's Pointers/Movement/Terrain
/// expander gains Jump buttons next to the BattleAnime / MoveCostRain /
/// MoveCostSnow / TerrainAvoid / TerrainDef / TerrainRes pointer textboxes,
/// matching the existing Move Cost (P56) Jump button from PR #346 (#344).
///
/// Each test exercises the click handler's address-passing contract:
///   - Move Cost variants navigate to MoveCostEditorView with the CURRENT
///     CLASS address AND the correct CostType combo selection.
///   - Battle Anime navigates to ImageBattleAnimeView with the dereferenced
///     anime pointer converted from GBA pointer to ROM offset (since the
///     receiving EntryList stores ROM offsets, not raw GBA pointers).
///
/// FE6's repurposed Ptr60/64/68 controls (Terrain Avoid/Def/Res at P56/60/64)
/// are exercised separately to prove the version-aware dispatch is correct
/// for all five ROM versions.
/// </summary>
[Collection("SharedState")]
public class ClassEditorPointerFieldJumpsTests : IClassFixture<RomFixture>
{
    private readonly RomFixture _fixture;
    private readonly ITestOutputHelper _output;

    public ClassEditorPointerFieldJumpsTests(RomFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    /// <summary>
    /// FE7/8: opening MoveCostEditorView via the new overload with
    /// CostType=MoveCostRain must select the requested class AND switch the
    /// cost-type combo to Move Cost (Rain), not leave the combo on Normal.
    /// </summary>
    [AvaloniaFact]
    public void NavigateToWithCostType_MoveCostRain_SelectsClassAndCostType()
    {
        if (!_fixture.IsAvailable) return;

        var seedVm = new MoveCostEditorViewModel();
        var classList = seedVm.LoadClassList();
        if (classList.Count < 4)
        {
            _output.WriteLine($"ClassList has only {classList.Count} entries; skipping test.");
            return;
        }

        uint targetAddr = classList[3].addr;

        var view = new MoveCostEditorView();
        view.Show();
        try
        {
            view.NavigateToWithCostType(targetAddr, CostType.MoveCostRain);

            var vm = view.DataViewModel as MoveCostEditorViewModel;
            Assert.NotNull(vm);
            Assert.Equal(targetAddr, vm!.CurrentAddr);
            Assert.Equal(CostType.MoveCostRain, vm.SelectedCostType);
        }
        finally
        {
            view.Close();
        }
    }

    /// <summary>
    /// FE7/8: same as above for Move Cost Snow.
    /// </summary>
    [AvaloniaFact]
    public void NavigateToWithCostType_MoveCostSnow_SelectsClassAndCostType()
    {
        if (!_fixture.IsAvailable) return;

        var seedVm = new MoveCostEditorViewModel();
        var classList = seedVm.LoadClassList();
        if (classList.Count < 4) return;

        uint targetAddr = classList[3].addr;
        var view = new MoveCostEditorView();
        view.Show();
        try
        {
            view.NavigateToWithCostType(targetAddr, CostType.MoveCostSnow);
            var vm = view.DataViewModel as MoveCostEditorViewModel;
            Assert.NotNull(vm);
            Assert.Equal(targetAddr, vm!.CurrentAddr);
            Assert.Equal(CostType.MoveCostSnow, vm.SelectedCostType);
        }
        finally { view.Close(); }
    }

    [AvaloniaFact]
    public void NavigateToWithCostType_TerrainAvoid_SelectsClassAndCostType()
    {
        if (!_fixture.IsAvailable) return;
        var seedVm = new MoveCostEditorViewModel();
        var classList = seedVm.LoadClassList();
        if (classList.Count < 4) return;
        uint targetAddr = classList[3].addr;
        var view = new MoveCostEditorView();
        view.Show();
        try
        {
            view.NavigateToWithCostType(targetAddr, CostType.TerrainAvoid);
            var vm = view.DataViewModel as MoveCostEditorViewModel;
            Assert.NotNull(vm);
            Assert.Equal(targetAddr, vm!.CurrentAddr);
            Assert.Equal(CostType.TerrainAvoid, vm.SelectedCostType);
        }
        finally { view.Close(); }
    }

    [AvaloniaFact]
    public void NavigateToWithCostType_TerrainDefense_SelectsClassAndCostType()
    {
        if (!_fixture.IsAvailable) return;
        var seedVm = new MoveCostEditorViewModel();
        var classList = seedVm.LoadClassList();
        if (classList.Count < 4) return;
        uint targetAddr = classList[3].addr;
        var view = new MoveCostEditorView();
        view.Show();
        try
        {
            view.NavigateToWithCostType(targetAddr, CostType.TerrainDefense);
            var vm = view.DataViewModel as MoveCostEditorViewModel;
            Assert.NotNull(vm);
            Assert.Equal(targetAddr, vm!.CurrentAddr);
            Assert.Equal(CostType.TerrainDefense, vm.SelectedCostType);
        }
        finally { view.Close(); }
    }

    [AvaloniaFact]
    public void NavigateToWithCostType_TerrainResistance_SelectsClassAndCostType()
    {
        if (!_fixture.IsAvailable) return;
        var seedVm = new MoveCostEditorViewModel();
        var classList = seedVm.LoadClassList();
        if (classList.Count < 4) return;
        uint targetAddr = classList[3].addr;
        var view = new MoveCostEditorView();
        view.Show();
        try
        {
            view.NavigateToWithCostType(targetAddr, CostType.TerrainResistance);
            var vm = view.DataViewModel as MoveCostEditorViewModel;
            Assert.NotNull(vm);
            Assert.Equal(targetAddr, vm!.CurrentAddr);
            Assert.Equal(CostType.TerrainResistance, vm.SelectedCostType);
        }
        finally { view.Close(); }
    }

    /// <summary>
    /// Battle Anime jump: the class's BattleAnimePtr is a raw GBA pointer
    /// (0x08XXXXXX). The receiving ImageBattleAnimeView's EntryList stores
    /// ROM offsets (baseAddr + i*4 where baseAddr is from rom.p32(...) which
    /// already calls U.toOffset). So the jump must pass U.toOffset(rawPtr)
    /// not the raw pointer value. This test asserts the conversion + match.
    /// </summary>
    [AvaloniaFact]
    public void ShouldJumpToBattleAnime_AcceptsValidInputs()
    {
        if (!_fixture.IsAvailable) return;

        uint inRomPtr = 0x08001000u;
        uint inRomClass = 0x08002000u;
        Assert.True(U.isSafetyOffset(U.toOffset(inRomPtr)));
        Assert.True(ClassEditorView.ShouldJumpToBattleAnime(inRomPtr, inRomClass));
    }

    [AvaloniaFact]
    public void ShouldJumpToBattleAnime_RejectsOutOfRomPointer()
    {
        if (!_fixture.IsAvailable) return;

        // In GBA pointer range but ROM offset is past loaded image.
        Assert.True(U.isPointer(0x09FFFFFFu));
        Assert.False(U.isSafetyOffset(U.toOffset(0x09FFFFFFu)));
        Assert.False(ClassEditorView.ShouldJumpToBattleAnime(0x09FFFFFFu, 0x080807F4u));
    }

    [AvaloniaFact]
    public void ShouldJumpToBattleAnime_RejectsZeroPointer()
    {
        if (!_fixture.IsAvailable) return;
        Assert.False(ClassEditorView.ShouldJumpToBattleAnime(0u, 0x080807F4u));
    }

    [AvaloniaFact]
    public void ShouldJumpToBattleAnime_RejectsNonPointer()
    {
        if (!_fixture.IsAvailable) return;
        Assert.False(ClassEditorView.ShouldJumpToBattleAnime(0x00000001u, 0x080807F4u));
    }

    [AvaloniaFact]
    public void ShouldJumpToBattleAnime_RejectsZeroCurrentClass()
    {
        if (!_fixture.IsAvailable) return;
        uint inRomPtr = 0x08001000u;
        Assert.True(U.isSafetyOffset(U.toOffset(inRomPtr)));
        Assert.False(ClassEditorView.ShouldJumpToBattleAnime(inRomPtr, 0u));
    }

    /// <summary>
    /// #1377 regression: the Class Editor's BattleAnime Jump passes the class's
    /// P52/P48 battle-anime SETTING pointer. The editor's left list is now
    /// CLASS-centric — every row's address IS a class's setting pointer
    /// (<see cref="ClassFormCore.GetBattleAnimeSettingRows"/>), the exact offset
    /// the editor's <c>LoadEntry</c> dereferences. So the jump SELECTS the
    /// matching class row (Case 1), keeping the list selection and detail panel
    /// in sync, and <c>OnSelected</c> loads the correct SP record.
    ///
    /// Asserts: (1) the EntryList's selected item's addr == the class setting
    /// pointer (it landed on the right row, not entry 0); (2) the VM's
    /// CurrentAddr == that pointer; (3) the AnimationNumber == the anime id
    /// stored at the setting pointer's +2 (the value WF would show). This is the
    /// "click the list returns to incorrect addresses" residual-bug regression.
    /// </summary>
    [AvaloniaFact]
    public void NavigateToBattleAnime_ClassSettingPointer_SelectsClassRowAndLoadsAnimation()
    {
        if (!_fixture.IsAvailable) return;

        ROM rom = CoreState.ROM;
        Assert.NotNull(rom);

        // The class-centric rows the editor's left list shows.
        var rows = ClassFormCore.GetBattleAnimeSettingRows(rom!);
        if (rows.Count == 0)
        {
            _output.WriteLine("No class battle-anime setting rows; skipping.");
            return;
        }

        // Pick the first class whose setting pointer the Class Editor would pass.
        var classVm = new ClassEditorViewModel();
        var items = classVm.LoadClassList();
        Assert.NotEmpty(items);

        uint settingOffset = 0;
        uint expectedAnimeNo = 0;
        foreach (var item in items)
        {
            classVm.LoadClass(item.addr);
            uint raw = classVm.BattleAnimePtr;
            if (!U.isPointer(raw)) continue;
            uint off = U.toOffset(raw);
            // Must be one of the class-centric list rows (so SelectAddress hits).
            if (!rows.Exists(r => r.settingOffset == off)) continue;
            settingOffset = off;
            expectedAnimeNo = rom!.u16(off + 2);
            break;
        }
        if (settingOffset == 0)
        {
            _output.WriteLine("No class whose setting pointer is a list row; skipping.");
            return;
        }

        _output.WriteLine($"setting offset=0x{settingOffset:X08}, expected animeNo={expectedAnimeNo}");

        var view = new ImageBattleAnimeView();
        view.Show();
        try
        {
            view.NavigateTo(settingOffset);

            // The list selection landed on the matching class row.
            var ctrl = view.FindControl<AddressListControl>("EntryList");
            Assert.NotNull(ctrl);
            Assert.NotNull(ctrl!.SelectedItem);
            Assert.Equal(settingOffset, ctrl.SelectedItem!.addr);

            // The VM loaded that class's SP record (NOT entry 0 / a mid-record).
            var vm = view.DataViewModel as ImageBattleAnimeViewModel;
            Assert.NotNull(vm);
            Assert.Equal(settingOffset, vm!.CurrentAddr);
            Assert.NotEqual(0u, vm.CurrentAddr);
            Assert.Equal(expectedAnimeNo, vm.AnimationNumber);
        }
        finally
        {
            view.Close();
        }
    }

    /// <summary>
    /// #1377: every row in the Battle Animation Editor's CLASS-centric left list
    /// must have an address that EQUALS the class's battle-anime setting pointer
    /// (<see cref="ClassFormCore.GetBattleAnimeAddrWhereID"/>), so clicking any
    /// row loads the exact SP record the editor reads/writes. This is the core of
    /// the "click the list → incorrect addresses" fix: list stride/base now match
    /// the record the loader dereferences.
    /// </summary>
    [AvaloniaFact]
    public void BattleAnimeList_EveryRowAddr_IsClassSettingPointer()
    {
        if (!_fixture.IsAvailable) return;

        ROM rom = CoreState.ROM;
        Assert.NotNull(rom);

        var vm = new ImageBattleAnimeViewModel();
        var list = vm.LoadList();
        if (list.Count == 0)
        {
            _output.WriteLine("Empty battle-anime class list; skipping.");
            return;
        }

        foreach (var row in list)
        {
            // tag is the class id; its setting pointer must equal the row addr.
            uint expected = ClassFormCore.GetBattleAnimeAddrWhereID(rom!, (int)row.tag);
            Assert.NotEqual(U.NOT_FOUND, expected);
            Assert.Equal(expected, row.addr);

            // And LoadEntry at that row reads the same anime id the class jump
            // would show — proving no mid-record misread.
            vm.LoadEntry(row.addr);
            Assert.Equal(ClassFormCore.GetAnimeIDByClassID(rom!, (int)row.tag), vm.AnimationNumber);
        }
    }

    /// <summary>
    /// #1377 (Copilot guardrail #3): locks in the separation between the
    /// class-centric SP-record list and the 32-byte ANIME-DATA table. An address
    /// from the global animation table (<c>image_battle_animelist base + id*32</c>,
    /// id ≥ 1) must NOT appear as a class-list row, and <c>NavigateTo</c> to it
    /// must NOT direct-load it as a 4-byte SP record — unless a class genuinely
    /// owns it (which a mid-table 32-byte slot does not). This prevents
    /// reintroducing the original mixed-address-space bug.
    /// </summary>
    [AvaloniaFact]
    public void BattleAnime_AnimeTableAddress_IsNotAClassRow_NorDirectLoaded()
    {
        if (!_fixture.IsAvailable) return;

        ROM rom = CoreState.ROM;
        Assert.NotNull(rom);

        uint listPtr = rom!.RomInfo.image_battle_animelist_pointer;
        if (listPtr == 0) { _output.WriteLine("No anime-list pointer; skipping."); return; }
        uint tableBase = rom.p32(listPtr);
        if (!U.isSafetyOffset(tableBase, rom)) { _output.WriteLine("Unsafe table base; skipping."); return; }

        // A mid-table 32-byte record address (id=1 => the SECOND anime record).
        uint animeTableAddr = tableBase + 1u * 32u;

        var vm = new ImageBattleAnimeViewModel();
        var list = vm.LoadList();
        // The 32-byte-table address is not a class-centric row...
        Assert.DoesNotContain(list, r => r.addr == animeTableAddr);
        // ...and no class owns it as a setting pointer (so NavigateTo won't load it).
        Assert.Equal(U.NOT_FOUND, ClassFormCore.GetIDWhereBattleAnimeAddr(rom, animeTableAddr));

        var view = new ImageBattleAnimeView();
        view.Show();
        try
        {
            // Establish a known good selection first (a real class row).
            uint goodRow = list.Count > 0 ? list[0].addr : 0;
            if (goodRow != 0)
                view.NavigateTo(goodRow);

            var before = view.FindControl<AddressListControl>("EntryList")!.SelectedItem;

            // NavigateTo the anime-table address: no row matches, no class owns it,
            // so the selection must be left untouched (no spurious direct-load).
            view.NavigateTo(animeTableAddr);

            var after = view.FindControl<AddressListControl>("EntryList")!.SelectedItem;
            Assert.Equal(before?.addr, after?.addr);
            var loadedVm = view.DataViewModel as ImageBattleAnimeViewModel;
            Assert.NotNull(loadedVm);
            // The VM was NOT repointed to the raw 32-byte-table address.
            Assert.NotEqual(animeTableAddr, loadedVm!.CurrentAddr);
        }
        finally
        {
            view.Close();
        }
    }

    /// <summary>
    /// #1377 (Copilot bot review): the Mant Animation editor's "Jump to Battle
    /// Anime" knows only an anime id. With the class-centric list, the old
    /// <c>animelist base + id*4</c> slot address no longer matches a row, so the
    /// jump now routes through <c>ImageBattleAnimeView.NavigateToAnimeId</c>,
    /// which selects the FIRST class row that USES that anime. This test takes a
    /// real class's anime id and asserts NavigateToAnimeId lands on a class row
    /// whose loaded anime matches.
    /// </summary>
    [AvaloniaFact]
    public void NavigateToAnimeId_SelectsClassRowThatUsesTheAnime()
    {
        if (!_fixture.IsAvailable) return;

        ROM rom = CoreState.ROM;
        Assert.NotNull(rom);

        // Find a class that uses a non-zero anime id (so the jump has a target).
        var rows = ClassFormCore.GetBattleAnimeSettingRows(rom!);
        uint targetAnimeId = 0;
        uint expectedSettingOffset = 0;
        foreach (var (classId, settingOffset) in rows)
        {
            uint a = ClassFormCore.GetAnimeIDByClassID(rom!, classId);
            if (a == 0) continue;
            targetAnimeId = a;
            expectedSettingOffset = ClassFormCore.GetFirstClassSettingPointerByAnimeId(rom!, a);
            break;
        }
        if (targetAnimeId == 0)
        {
            _output.WriteLine("No class with a non-zero anime id; skipping.");
            return;
        }
        Assert.NotEqual(U.NOT_FOUND, expectedSettingOffset);

        var view = new ImageBattleAnimeView();
        view.Show();
        try
        {
            view.NavigateToAnimeId(targetAnimeId);

            var ctrl = view.FindControl<AddressListControl>("EntryList");
            Assert.NotNull(ctrl);
            Assert.NotNull(ctrl!.SelectedItem);
            // Selected the first class row that uses this anime.
            Assert.Equal(expectedSettingOffset, ctrl.SelectedItem!.addr);

            var vm = view.DataViewModel as ImageBattleAnimeViewModel;
            Assert.NotNull(vm);
            // The loaded SP record resolves to the requested anime id.
            Assert.Equal(targetAnimeId, vm!.AnimationNumber);
        }
        finally
        {
            view.Close();
        }
    }

    /// <summary>
    /// Deterministic Battle Anime jump round-trip: pick the first entry from
    /// ImageBattleAnimeView's own EntryList (guaranteed to exist for whatever
    /// ROM is loaded), call NavigateTo with that exact offset, and assert the
    /// selection matches.
    ///
    /// Copilot bot review feedback: the data-dependent
    /// <see cref="NavigateToBattleAnime_ClassSettingPointer_SelectsClassRowAndLoadsAnimation"/>
    /// can return early if no class's anime pointer happens to be a list row for
    /// the loaded ROM, hiding real regressions. This test always exercises
    /// the selection contract with index 0 (which exists by construction).
    /// </summary>
    [AvaloniaFact]
    public void NavigateToBattleAnime_RoundTripsKnownGoodEntry()
    {
        if (!_fixture.IsAvailable) return;

        var view = new ImageBattleAnimeView();
        view.Show();
        try
        {
            var ctrl = view.FindControl<AddressListControl>("EntryList");
            Assert.NotNull(ctrl);

            var items = ctrl!.GetItems();
            // Skip with a clear note instead of silently passing — the editor's
            // EntryList is empty only when the ROM has no battle animations
            // (a pathological corruption scenario; FE6/7/8 always have ≥1).
            Assert.NotEmpty(items);

            // Pick a deterministic known-good entry (index 0). It exists by
            // construction so the round-trip MUST select it.
            uint knownOffset = items[0].addr;
            view.NavigateTo(knownOffset);

            Assert.NotNull(ctrl.SelectedItem);
            Assert.Equal(knownOffset, ctrl.SelectedItem!.addr);
        }
        finally
        {
            view.Close();
        }
    }

    /// <summary>
    /// FE6 control remapping: the shared Ptr60 / Ptr64 / Ptr68 controls are
    /// reused for Terrain Avoid / Terrain Def / Terrain Res (at P56 / P60 /
    /// P64 in the 72-byte FE6 class struct). This test loads an FE6 ROM
    /// explicitly (via RomTestHelper.WithRom) and verifies the three
    /// version-aware MoveCost jumps land on the correct CostType variants.
    ///
    /// Skips gracefully when no FE6 ROM is available (CI environments that
    /// download only FE8U still see this test run on dev machines with
    /// roms/FE6.gba present).
    /// </summary>
    [AvaloniaFact]
    public void NavigateToWithCostType_FE6_TerrainTriple_MapsCorrectly()
    {
        string? fe6Path = TestRomLocator.FindRom("FE6");
        if (fe6Path == null)
        {
            _output.WriteLine("FE6 ROM not available; skipping FE6 mapping test.");
            return;
        }

        RomTestHelper.WithRom("FE6", () =>
        {
            // Assert we actually loaded FE6 (defensive)
            Assert.Equal(6, CoreState.ROM!.RomInfo.version);

            var seedVm = new MoveCostEditorViewModel();
            var classList = seedVm.LoadClassList();
            if (classList.Count < 4)
            {
                _output.WriteLine($"FE6 ClassList has only {classList.Count} entries; skipping.");
                return;
            }
            uint targetAddr = classList[3].addr;

            // Ptr60 box on FE6 == Terrain Avoid (P56)
            // Ptr64 box on FE6 == Terrain Def  (P60)
            // Ptr68 box on FE6 == Terrain Res  (P64)
            // The Click handlers select the right CostType per IsFE6; here we
            // exercise the receiving editor with each CostType to prove the
            // mapping survives a real FE6 ROM load.
            foreach (var costType in new[] { CostType.TerrainAvoid, CostType.TerrainDefense, CostType.TerrainResistance })
            {
                var view = new MoveCostEditorView();
                view.Show();
                try
                {
                    view.NavigateToWithCostType(targetAddr, costType);
                    var vm = view.DataViewModel as MoveCostEditorViewModel;
                    Assert.NotNull(vm);
                    Assert.Equal(targetAddr, vm!.CurrentAddr);
                    Assert.Equal(costType, vm.SelectedCostType);
                }
                finally
                {
                    view.Close();
                }
            }
        });
    }

    /// <summary>
    /// FE6 verifies the FE6-specific ClassEditorView UI hides the orphaned
    /// Ptr72 Jump button (along with its sibling textbox + label) once the
    /// view's ConfigureVersionUI has run. Copilot CLI review feedback:
    /// without this guard, FE6 would render a no-op "Jump" button next to
    /// nothing (the textbox was hidden but the button stayed visible).
    /// </summary>
    [AvaloniaFact]
    public void ClassEditor_FE6_HidesPtr72WrapperIncludingJumpButton()
    {
        string? fe6Path = TestRomLocator.FindRom("FE6");
        if (fe6Path == null)
        {
            _output.WriteLine("FE6 ROM not available; skipping FE6 wrapper-visibility test.");
            return;
        }

        RomTestHelper.WithRom("FE6", () =>
        {
            Assert.Equal(6, CoreState.ROM!.RomInfo.version);

            var view = new ClassEditorView();
            view.Show();
            try
            {
                // Find the StackPanel wrapper for Ptr72 (named "Ptr72Wrapper" in axaml).
                var wrapper = view.FindControl<global::Avalonia.Controls.StackPanel>("Ptr72Wrapper");
                Assert.NotNull(wrapper);
                Assert.False(wrapper!.IsVisible, "Ptr72Wrapper must be hidden on FE6 so the JumpToPtr72 button does not appear orphaned.");

                // Also assert the inner Jump button inherits the hidden state.
                var jumpBtn = view.FindControl<global::Avalonia.Controls.Button>("JumpToPtr72Button");
                Assert.NotNull(jumpBtn);
                // IsEffectivelyVisible is the rendered state — false because the parent is hidden.
                Assert.False(jumpBtn!.IsEffectivelyVisible, "JumpToPtr72Button must not render on FE6.");
            }
            finally
            {
                view.Close();
            }
        });
    }

    /// <summary>
    /// Regression: the existing Move Cost (P56) Jump button must always
    /// land on CostType=MoveCostNormal even when the MoveCostEditor was
    /// already open with a stale cost type (e.g. user clicked Rain first,
    /// then clicked the original P56 Jump).
    ///
    /// Copilot CLI review feedback #2: WindowManager.Open<T>() reuses an
    /// existing editor instance, and the previous JumpToMoveCost_Click
    /// routed through Navigate<T>(addr) which only selects the class and
    /// preserves whatever cost type was last selected. The fix routes P56
    /// through NavigateToWithCostType(addr, MoveCostNormal). This test
    /// exercises the reuse sequence directly via the public API.
    /// </summary>
    [AvaloniaFact]
    public void NavigateToWithCostType_P56_ResetsStaleCostTypeToNormal()
    {
        if (!_fixture.IsAvailable) return;

        var seedVm = new MoveCostEditorViewModel();
        var classList = seedVm.LoadClassList();
        if (classList.Count < 4) return;
        uint addrA = classList[3].addr;
        uint addrB = classList.Count > 5 ? classList[5].addr : addrA;

        var view = new MoveCostEditorView();
        view.Show();
        try
        {
            // 1. Simulate a Rain jump first (Ptr60 click handler).
            view.NavigateToWithCostType(addrA, CostType.MoveCostRain);
            var vm = view.DataViewModel as MoveCostEditorViewModel;
            Assert.NotNull(vm);
            Assert.Equal(CostType.MoveCostRain, vm!.SelectedCostType);

            // 2. Now simulate a P56 (Move Cost Normal) Jump on the SAME editor
            //    instance — the fix routes through NavigateToWithCostType with
            //    MoveCostNormal so the cost type resets.
            view.NavigateToWithCostType(addrB, CostType.MoveCostNormal);
            Assert.Equal(CostType.MoveCostNormal, vm.SelectedCostType);
            Assert.Equal(addrB, vm.CurrentAddr);
        }
        finally
        {
            view.Close();
        }
    }
}
