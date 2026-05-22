using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.Controls;
using FEBuilderGBA.Avalonia.ViewModels;
using FEBuilderGBA.Avalonia.Views;
using Xunit;
using Xunit.Abstractions;

namespace FEBuilderGBA.Avalonia.Tests;

/// <summary>
/// Regression tests for issue #363: the Item Editor's Effectiveness Jump
/// (vanilla path, i.e. <see cref="ItemEffectivenessViewerView"/>) previously
/// loaded a list from <c>rom.RomInfo.weapon_effectiveness_2x3x_address</c>
/// (the sacred-weapons 2x/3x byte-table) instead of enumerating items by
/// their P16 effectiveness pointer. The address the source jump passes
/// (<c>item.P16 - 0x08000000</c>) never matched any row, so
/// <c>AddressListControl.SelectAddress</c> linearly scanned, failed to match,
/// and silently left the initial <c>SelectFirst()</c> selection in place —
/// the user landed on the first sacred-weapons row, not on their item.
///
/// After the fix the view-model enumerates items by their P16 effectiveness
/// pointer (mirroring the WinForms <c>ItemEffectivenessForm</c> outer list +
/// the Avalonia <c>ItemEffectivenessSkillSystemsReworkViewModel.LoadList()</c>
/// for the Skill Systems Rework variant), so the address passed by the source
/// side resolves cleanly to the correct list row, and rows render item icons.
///
/// This is the third entry in the "address-mismatched jump receiver" family
/// for Avalonia: #344 to PR #346 (stat bonuses), #362 to PR #456
/// (effectiveness SkillSystems rework), and now #363 (effectiveness vanilla).
/// </summary>
[Collection("SharedState")]
public class ItemEffectivenessViewerJumpTests : IClassFixture<RomFixture>
{
    private readonly RomFixture _fixture;
    private readonly ITestOutputHelper _output;

    public ItemEffectivenessViewerJumpTests(RomFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    /// <summary>
    /// T1 — Affirmative regression: navigating to a real item's P16 offset
    /// must select THAT row in the receiving view's list, not silently fall
    /// back to entry 0. Picks a target row whose addr differs from list[0] so
    /// the assertion is meaningful (vanilla ROMs commonly have multiple items
    /// sharing the same effectiveness pointer; matching list[0] would not
    /// prove non-first selection).
    /// </summary>
    [AvaloniaFact]
    public void NavigateToValidItemAddress_SelectsThatItem_NotEntryZero()
    {
        if (!_fixture.IsAvailable) return;

        var seedVm = new ItemEffectivenessViewerViewModel();
        var list = seedVm.LoadItemEffectivenessList();
        if (list.Count < 2)
        {
            _output.WriteLine($"List has only {list.Count} entries; cannot test non-first selection.");
            return;
        }
        uint firstAddr = list[0].addr;
        int targetIndex = -1;
        for (int i = 1; i < list.Count; i++)
        {
            if (list[i].addr != firstAddr)
            {
                targetIndex = i;
                break;
            }
        }
        if (targetIndex < 0)
        {
            _output.WriteLine($"No row with distinct addr from list[0]=0x{firstAddr:X08}; skipping.");
            return;
        }
        uint targetAddr = list[targetIndex].addr;

        Assert.NotEqual(firstAddr, targetAddr);
        Assert.NotEqual(0u, targetAddr);

        var view = new ItemEffectivenessViewerView();
        view.Show();
        try
        {
            view.NavigateTo(targetAddr);

            var ctrl = view.FindControl<AddressListControl>("EntryList");
            Assert.NotNull(ctrl);
            Assert.NotNull(ctrl!.SelectedItem);

            _output.WriteLine($"Selected addr=0x{ctrl.SelectedItem!.addr:X08}, target=0x{targetAddr:X08}");

            Assert.Equal(targetAddr, ctrl.SelectedItem!.addr);
            Assert.NotEqual(firstAddr, ctrl.SelectedItem!.addr);
        }
        finally
        {
            view.Close();
        }
    }

    /// <summary>
    /// T2 — After navigating to a target address, the receiving view-model's
    /// <c>CurrentAddr</c> must match the navigated address (set via the
    /// <c>SelectedAddressChanged → OnSelected → LoadItemEffectiveness</c>
    /// chain). Accesses the view-model via the <c>DataViewModel</c> property.
    /// </summary>
    [AvaloniaFact]
    public void NavigateToValidItemAddress_VmCurrentAddrMatchesTarget()
    {
        if (!_fixture.IsAvailable) return;

        var seedVm = new ItemEffectivenessViewerViewModel();
        var list = seedVm.LoadItemEffectivenessList();
        uint firstAddr = list.Count > 0 ? list[0].addr : 0;
        int targetIndex = -1;
        for (int i = 1; i < list.Count; i++)
        {
            if (list[i].addr != firstAddr)
            {
                targetIndex = i;
                break;
            }
        }
        if (targetIndex < 0)
        {
            _output.WriteLine($"No row with distinct addr from list[0]; skipping.");
            return;
        }
        uint targetAddr = list[targetIndex].addr;

        var view = new ItemEffectivenessViewerView();
        view.Show();
        try
        {
            view.NavigateTo(targetAddr);

            var ctrl = view.FindControl<AddressListControl>("EntryList");
            Assert.NotNull(ctrl);
            Assert.NotNull(ctrl!.SelectedItem);
            Assert.Equal(targetAddr, ctrl.SelectedItem!.addr);

            var vm = view.DataViewModel as ItemEffectivenessViewerViewModel;
            Assert.NotNull(vm);
            Assert.Equal(targetAddr, vm!.CurrentAddr);
        }
        finally
        {
            view.Close();
        }
    }

    /// <summary>
    /// T3 — Proves the broken sacred-weapons-table data model is gone: on
    /// FE8U the new VM must enumerate many items with valid P16 pointers, so
    /// the list count must exceed 1. Before the fix this returned at most a
    /// few class-byte entries from <c>weapon_effectiveness_2x3x_address</c>,
    /// none of which matched the item P16 offsets the source jump passes.
    /// </summary>
    [Fact]
    public void LoadList_PopulatesMoreThanOneItem()
    {
        if (!_fixture.IsAvailable) return;
        if (_fixture.Version != "FE8U")
        {
            _output.WriteLine($"This test is FE8U-specific (ROM is {_fixture.Version}); skipping.");
            return;
        }

        var vm = new ItemEffectivenessViewerViewModel();
        var list = vm.LoadItemEffectivenessList();
        _output.WriteLine($"LoadItemEffectivenessList returned {list.Count} entries on FE8U");
        Assert.True(list.Count > 1,
            $"Expected more than 1 entry on FE8U after fix; got {list.Count} (sacred-weapons-table list?)");
    }

    /// <summary>
    /// T4 — Every emitted entry's address must be non-zero AND inside the ROM
    /// safety range. Catches any iteration regression that lets bogus rows
    /// (addr=0 stubs, out-of-bounds pointers) slip through.
    /// </summary>
    [Fact]
    public void LoadList_EveryEntry_HasValidEffectivenessPointer()
    {
        if (!_fixture.IsAvailable) return;

        var vm = new ItemEffectivenessViewerViewModel();
        var list = vm.LoadItemEffectivenessList();
        foreach (var entry in list)
        {
            Assert.NotEqual(0u, entry.addr);
            Assert.True(U.isSafetyOffset(entry.addr),
                $"Entry name='{entry.name}' addr=0x{entry.addr:X08} fails U.isSafetyOffset");
        }
    }

    /// <summary>
    /// T5 — Defensive smoke test: <c>LoadItemEffectivenessList()</c> must
    /// return a usable <c>List&lt;AddrResult&gt;</c> (possibly empty) on
    /// whatever ROM <see cref="RomFixture"/> loads (typically FE8U), without
    /// throwing. <c>RomFixture</c> only loads ONE ROM per session, so this is
    /// fixture-ROM coverage — not exhaustive cross-version coverage.
    /// </summary>
    [Fact]
    public void LoadList_OnAnyRom_DoesNotCrash()
    {
        if (!_fixture.IsAvailable) return;

        var vm = new ItemEffectivenessViewerViewModel();
        var list = vm.LoadItemEffectivenessList();
        Assert.NotNull(list);
        _output.WriteLine($"ROM={_fixture.Version}, LoadItemEffectivenessList returned {list.Count} entries");
    }

    /// <summary>
    /// T6 — End-to-end address alignment: verify the source's
    /// <c>JumpToEffectiveness_Click</c> offset (<c>ptr - 0x08000000</c>)
    /// matches at least one address in the new list for every sampled item
    /// that has a valid P16 effectiveness pointer. This is the precise
    /// alignment proof that #363 was failing on.
    /// </summary>
    [Fact]
    public void JumpAddress_MatchesAnEntryInList_ForItemsWithEffectiveness()
    {
        if (!_fixture.IsAvailable) return;

        var itemVm = new ItemEditorViewModel();
        var items = itemVm.LoadItemList();

        var effVm = new ItemEffectivenessViewerViewModel();
        var effList = effVm.LoadItemEffectivenessList();
        if (effList.Count == 0)
        {
            _output.WriteLine("No effectiveness entries on this ROM; skipping.");
            return;
        }

        int sampled = 0;
        int matched = 0;
        for (int i = 1; i < items.Count; i++)
        {
            itemVm.LoadItem(items[i].addr);
            if (itemVm.EffectivenessPtr == 0) continue;
            if (!U.isPointer(itemVm.EffectivenessPtr)) continue;
            uint targetOffset = itemVm.EffectivenessPtr - 0x08000000;
            if (!U.isSafetyOffset(targetOffset)) continue;

            sampled++;
            if (effList.Any(e => e.addr == targetOffset)) matched++;
            if (sampled >= 10) break;
        }

        _output.WriteLine($"Sampled {sampled} items with valid P16, {matched} matched the new list");
        Assert.True(sampled > 0, "Expected to sample at least one item with valid P16");
        Assert.Equal(sampled, matched);
    }
}
