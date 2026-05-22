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
/// Regression tests for issue #362: the Item Editor's Effectiveness Jump
/// (Skill Systems Rework variant) previously opened
/// <see cref="ItemEffectivenessSkillSystemsReworkView"/> with a stub list
/// containing a single <c>AddrResult(addr=0, ...)</c>. Any non-zero target
/// address from <c>JumpToEffectiveness_Click</c> never matched, so
/// <c>AddressListControl.SelectAddress</c> silently fell back to the initial
/// <c>SelectFirst()</c> selection (the stub).
///
/// After the fix the view-model enumerates items by their P16 effectiveness
/// pointer (mirroring the WinForms <c>ItemEffectivenessSkillSystemsReworkForm</c>
/// data model), so the address passed by the source side resolves cleanly to
/// the correct list row.
///
/// Note: the SkillSystems Rework patch is normally absent from a vanilla
/// FE8U.gba, but the view-model's <c>LoadList()</c> only looks at item-pointer
/// metadata that exists for every ROM version (the patch detection happens at
/// the source side that picks the variant to open). The tests therefore drive
/// it against the unmodified FE8U ROM that <see cref="RomFixture"/> loads.
/// </summary>
[Collection("SharedState")]
public class ItemEffectivenessSkillSystemsReworkJumpTests : IClassFixture<RomFixture>
{
    private readonly RomFixture _fixture;
    private readonly ITestOutputHelper _output;

    public ItemEffectivenessSkillSystemsReworkJumpTests(RomFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    /// <summary>
    /// T1 — Affirmative regression: navigating to a real item's P16 offset
    /// must select THAT row in the receiving view's list, not silently fall
    /// back to entry 0.
    /// </summary>
    [AvaloniaFact]
    public void NavigateToValidItemAddress_SelectsThatItem_NotEntryZero()
    {
        if (!_fixture.IsAvailable) return;

        var seedVm = new ItemEffectivenessSkillSystemsReworkViewModel();
        var list = seedVm.LoadList();
        // Need at least two rows with DIFFERENT addresses to prove we're not
        // coincidentally on index 0. Vanilla ROMs commonly have multiple
        // items sharing the same effectiveness pointer (e.g. Rapier and a
        // dummy slot), so we pick the first row whose addr differs from
        // list[0].
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

        var view = new ItemEffectivenessSkillSystemsReworkView();
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
    /// <c>SelectedAddressChanged → OnSelected → LoadEntry</c> chain).
    /// Accesses the view-model via the newly-exposed <c>DataViewModel</c>
    /// property.
    /// </summary>
    [AvaloniaFact]
    public void NavigateToValidItemAddress_VmCurrentAddrMatchesTarget()
    {
        if (!_fixture.IsAvailable) return;

        var seedVm = new ItemEffectivenessSkillSystemsReworkViewModel();
        var list = seedVm.LoadList();
        // Same selection logic as T1 — pick the first row whose addr differs
        // from list[0] so the assertion is meaningful.
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

        var view = new ItemEffectivenessSkillSystemsReworkView();
        view.Show();
        try
        {
            view.NavigateTo(targetAddr);

            var ctrl = view.FindControl<AddressListControl>("EntryList");
            Assert.NotNull(ctrl);
            Assert.NotNull(ctrl!.SelectedItem);
            Assert.Equal(targetAddr, ctrl.SelectedItem!.addr);

            var vm = view.DataViewModel as ItemEffectivenessSkillSystemsReworkViewModel;
            Assert.NotNull(vm);
            Assert.Equal(targetAddr, vm!.CurrentAddr);
        }
        finally
        {
            view.Close();
        }
    }

    /// <summary>
    /// T3 — Proves the stub is gone: the list count must exceed 1 on FE8U
    /// (which has many items with valid P16 pointers). Before the fix this
    /// returned exactly 1 entry (the stub).
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

        var vm = new ItemEffectivenessSkillSystemsReworkViewModel();
        var list = vm.LoadList();
        _output.WriteLine($"LoadList returned {list.Count} entries on FE8U");
        Assert.True(list.Count > 1,
            $"Expected more than 1 entry on FE8U after fix; got {list.Count} (stub list?)");
    }

    /// <summary>
    /// T4 — Every emitted entry's address must be non-zero AND inside the ROM
    /// safety range. Catches any iteration regression that lets addr=0 entries
    /// (the old stub shape) slip through.
    /// </summary>
    [Fact]
    public void LoadList_EveryEntry_HasValidEffectivenessPointer()
    {
        if (!_fixture.IsAvailable) return;

        var vm = new ItemEffectivenessSkillSystemsReworkViewModel();
        var list = vm.LoadList();
        foreach (var entry in list)
        {
            Assert.NotEqual(0u, entry.addr);
            Assert.True(U.isSafetyOffset(entry.addr),
                $"Entry name='{entry.name}' addr=0x{entry.addr:X08} fails U.isSafetyOffset");
        }
    }

    /// <summary>
    /// T5 — Defensive smoke test: <c>LoadList()</c> must return a usable
    /// <c>List&lt;AddrResult&gt;</c> (possibly empty) regardless of ROM
    /// version, without throwing. <c>RomFixture</c> prefers FE8U but the
    /// view-model must remain safe to call on any of the five supported ROM
    /// variants (the patch detection happens elsewhere; this VM only reads
    /// item-pointer metadata that exists for every ROM).
    /// </summary>
    [Fact]
    public void LoadList_OnAnyRom_DoesNotCrash()
    {
        if (!_fixture.IsAvailable) return;

        var vm = new ItemEffectivenessSkillSystemsReworkViewModel();
        var list = vm.LoadList();
        Assert.NotNull(list);
        _output.WriteLine($"ROM={_fixture.Version}, LoadList returned {list.Count} entries");
    }

    /// <summary>
    /// T6 — Verify the source's <c>JumpToEffectiveness_Click</c> offset
    /// (<c>ptr - 0x08000000</c>) matches at least one address in the new list
    /// for items that have valid P16 pointers. This is the end-to-end address
    /// alignment proof that #362 was failing on.
    /// </summary>
    [Fact]
    public void JumpAddress_MatchesAnEntryInList_ForItemsWithEffectiveness()
    {
        if (!_fixture.IsAvailable) return;

        var itemVm = new ItemEditorViewModel();
        var items = itemVm.LoadItemList();

        var effVm = new ItemEffectivenessSkillSystemsReworkViewModel();
        var effList = effVm.LoadList();
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
