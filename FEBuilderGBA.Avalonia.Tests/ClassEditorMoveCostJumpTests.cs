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
/// Regression tests for issue #344: the "Jump" button next to the Move Cost
/// pointer (P56) in the Class Editor was previously navigating to the
/// move-cost-table OFFSET, which never matches any entry in the
/// MoveCostEditor's class list, so the editor always silently fell back to
/// entry 0. The fix navigates with the CURRENT CLASS address instead, so the
/// MoveCostEditor selects the same class the user came from.
///
/// These tests exercise the MoveCostEditor receiving a class address and
/// confirm the correct class is selected (not entry 0). They also confirm
/// the "no valid pointer" path leaves CanWrite=false (defensive gate).
/// </summary>
[Collection("SharedState")]
public class ClassEditorMoveCostJumpTests : IClassFixture<RomFixture>
{
    private readonly RomFixture _fixture;
    private readonly ITestOutputHelper _output;

    public ClassEditorMoveCostJumpTests(RomFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    /// <summary>
    /// Issue #344 affirmative regression: navigating to a class address that
    /// is NOT the first entry must select THAT class in the MoveCostEditor's
    /// ClassList, not silently fall back to entry 0.
    /// </summary>
    [AvaloniaFact]
    public void NavigateToClassAddress_SelectsThatClass_NotEntryZero()
    {
        if (!_fixture.IsAvailable) return;

        var seedVm = new MoveCostEditorViewModel();
        var classList = seedVm.LoadClassList();
        // Need at least four distinct classes so we can prove we're not just
        // ending up on index 0 by coincidence.
        if (classList.Count < 4)
        {
            _output.WriteLine($"ClassList has only {classList.Count} entries; skipping test.");
            return;
        }

        // Pick a class that is NOT index 0 (Master Lord-equivalent is around 03).
        int targetIndex = 3;
        uint targetAddr = classList[targetIndex].addr;
        uint firstAddr = classList[0].addr;

        Assert.NotEqual(firstAddr, targetAddr);

        var view = new MoveCostEditorView();
        view.Show();
        try
        {
            // Window.Opened (which calls LoadList) fires from Show() in headless mode.
            view.NavigateTo(targetAddr);

            // The ClassList control must now expose the target class as the
            // current selection — both via SelectedItem.addr and via the VM
            // CurrentAddr (set by the SelectedAddressChanged event handler).
            var classCtrl = view.FindControl<AddressListControl>("ClassList");
            Assert.NotNull(classCtrl);
            Assert.NotNull(classCtrl!.SelectedItem);

            _output.WriteLine($"Selected addr=0x{classCtrl.SelectedItem!.addr:X08}, target=0x{targetAddr:X08}");

            Assert.Equal(targetAddr, classCtrl.SelectedItem!.addr);
            Assert.NotEqual(firstAddr, classCtrl.SelectedItem!.addr);

            // Also verify the VM CurrentAddr matches (set by the
            // SelectedAddressChanged -> OnClassSelected -> LoadMoveCost chain).
            var vm = view.DataViewModel as MoveCostEditorViewModel;
            Assert.NotNull(vm);
            Assert.Equal(targetAddr, vm!.CurrentAddr);
        }
        finally
        {
            view.Close();
        }
    }

    /// <summary>
    /// Issue #344 follow-up: confirm the receiving side wires up correctly
    /// after navigation — DataViewModel.CurrentAddr matches the navigated
    /// class address. The previous buggy behavior of "always entry 0" would
    /// fail this assertion for any non-zero target index.
    /// </summary>
    [AvaloniaFact]
    public void NavigateToClassAddress_VmCurrentAddrMatchesTarget()
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
            var classCtrl = view.FindControl<AddressListControl>("ClassList");
            Assert.NotNull(classCtrl);

            view.NavigateTo(targetAddr);

            Assert.NotNull(classCtrl!.SelectedItem);
            Assert.Equal(targetAddr, classCtrl.SelectedItem!.addr);

            var vm = view.DataViewModel as MoveCostEditorViewModel;
            Assert.NotNull(vm);
            Assert.Equal(targetAddr, vm!.CurrentAddr);
        }
        finally
        {
            view.Close();
        }
    }

    /// <summary>
    /// Edge case from the plan review: a class whose move-cost pointer (P56)
    /// is zero / invalid must NOT enable writes. LoadMoveCost should leave
    /// CanWrite=false and clear the terrain fields. This guards against the
    /// receiving editor silently selecting entry 0 with stale data when the
    /// defensive gate in JumpToMoveCost_Click is bypassed.
    /// </summary>
    [AvaloniaFact]
    public void ClassWithoutMoveCost_LoadsCleanly()
    {
        if (!_fixture.IsAvailable) return;

        var vm = new MoveCostEditorViewModel();
        vm.BuildCostTypeItems();
        var list = vm.LoadClassList();
        if (list.Count == 0)
        {
            _output.WriteLine("ClassList empty; skipping.");
            return;
        }

        // Find a class with a zero/invalid move-cost pointer, if any.
        ROM rom = CoreState.ROM!;
        uint? targetAddr = null;
        foreach (var entry in list)
        {
            uint pointerAddr = MoveCostEditorViewModel.GetMoveCostPointerAddr(
                entry.addr, CostType.MoveCostNormal);
            if (pointerAddr == 0) continue;
            if (pointerAddr + 3 >= (uint)rom.Data.Length) continue;
            uint moveCostPtr = rom.u32(pointerAddr);
            if (!U.isPointer(moveCostPtr))
            {
                targetAddr = entry.addr;
                _output.WriteLine($"Found class 0x{entry.addr:X08} with invalid P56=0x{moveCostPtr:X08}");
                break;
            }
        }

        if (targetAddr == null)
        {
            // Every class in this ROM has a valid P56 — synthesize via the
            // "address 0 is rejected" path which exercises the same gates
            // (U.isSafetyOffset → ClearAllFields → CanWrite=false).
            _output.WriteLine("All classes have valid P56; testing CanWrite=false via address 0 (defensive gate).");
            vm.LoadMoveCost(0); // 0 fails the pointerAddr safety check upstream
            Assert.False(vm.CanWrite);
            Assert.Equal(0u, vm.MoveCostAddr);
            for (int i = 0; i < MoveCostEditorViewModel.TerrainCount; i++)
            {
                Assert.Equal((byte)0, vm.GetCost(i));
            }
            return;
        }

        vm.LoadMoveCost(targetAddr.Value, CostType.MoveCostNormal);

        // The defensive gates in MoveCostEditorViewModel.LoadMoveCost
        // (around lines 369-376 of the VM) zero out the terrain fields and
        // leave CanWrite=false when the pointer is invalid.
        Assert.False(vm.CanWrite);
        Assert.Equal(0u, vm.MoveCostAddr);

        for (int i = 0; i < MoveCostEditorViewModel.TerrainCount; i++)
        {
            Assert.Equal((byte)0, vm.GetCost(i));
        }
    }

    /// <summary>
    /// PR review follow-up (Copilot CLI): the original v1 of this fix dropped
    /// the safety-offset guard, allowing an in-range-but-out-of-ROM P56 to
    /// still open the Move Cost Editor on the current class with a dangling
    /// pointer. The restored gate is exercised here via the extracted helper.
    ///
    /// These run as [AvaloniaFact] with the RomFixture guard because
    /// U.isSafetyOffset(uint) without an explicit rom parameter dereferences
    /// CoreState.ROM.Data — they cannot run in a no-ROM environment.
    /// </summary>
    [AvaloniaFact]
    public void ShouldJumpToMoveCost_RejectsOutOfRomPointer()
    {
        if (!_fixture.IsAvailable) return;

        // 0x09FFFFFF is inside the GBA pointer range (so U.isPointer == true)
        // but its ROM offset (0x01FFFFFF) exceeds any real ROM image, so
        // U.isSafetyOffset must reject it.
        Assert.True(U.isPointer(0x09FFFFFFu));
        Assert.False(U.isSafetyOffset(U.toOffset(0x09FFFFFFu)));

        // The gate must therefore return false even with a valid class addr.
        Assert.False(ClassEditorView.ShouldJumpToMoveCost(0x09FFFFFFu, 0x080807F4u));
    }

    [AvaloniaFact]
    public void ShouldJumpToMoveCost_RejectsZeroPointer()
    {
        if (!_fixture.IsAvailable) return;
        Assert.False(ClassEditorView.ShouldJumpToMoveCost(0u, 0x080807F4u));
    }

    [AvaloniaFact]
    public void ShouldJumpToMoveCost_RejectsNonPointer()
    {
        if (!_fixture.IsAvailable) return;
        // 0x00000001 is not in the GBA pointer range.
        Assert.False(ClassEditorView.ShouldJumpToMoveCost(0x00000001u, 0x080807F4u));
    }

    [AvaloniaFact]
    public void ShouldJumpToMoveCost_RejectsZeroCurrentClass()
    {
        if (!_fixture.IsAvailable) return;

        // Even a perfectly valid P56 must not jump when no class is loaded
        // (CurrentAddr == 0) — otherwise we'd open the editor on the previous
        // selection. Use a pointer whose offset is past the GBA header range
        // (U.isSafetyOffset rejects offsets < 0x200) and within the loaded
        // ROM, so the safety-offset check passes and we are genuinely
        // exercising the CurrentAddr==0 gate.
        uint inRomPtr = 0x08001000u;
        Assert.True(U.isPointer(inRomPtr));
        Assert.True(U.isSafetyOffset(U.toOffset(inRomPtr)));
        Assert.False(ClassEditorView.ShouldJumpToMoveCost(inRomPtr, 0u));
    }

    [AvaloniaFact]
    public void ShouldJumpToMoveCost_AcceptsValidInputs()
    {
        if (!_fixture.IsAvailable) return;

        // Use offsets that are guaranteed inside the loaded ROM. The first
        // byte is offset 0 (pointer 0x08000000); pick a small in-ROM offset
        // for the class address too. Both gates pass, so the click handler
        // may navigate.
        uint inRomPtr = 0x08001000u;
        uint inRomClass = 0x08002000u;
        Assert.True(U.isSafetyOffset(U.toOffset(inRomPtr)));
        Assert.True(ClassEditorView.ShouldJumpToMoveCost(inRomPtr, inRomClass));
    }
}
