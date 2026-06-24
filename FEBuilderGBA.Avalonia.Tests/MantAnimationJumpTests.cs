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
/// Regression suite for issue #1408 — off-by-one in the Mant Animation editor's
/// "Jump to Battle Anime" (regression from #1407).
///
/// #1407 made the Battle Animation editor's left list CLASS-centric and routed
/// the Mant jump through the new 1-based <see cref="ImageBattleAnimeView.NavigateToAnimeId"/>.
/// The Mant view kept passing the WF <b>zero-based</b>
/// <see cref="MantAnimationViewModel.GetJumpBattleAnimeId"/> (= <c>atoh(label) - 1</c>),
/// so the first row no-opped and later rows showed the PREVIOUS animation.
///
/// The fix has the view pass the <b>1-based</b>
/// <see cref="MantAnimationViewModel.GetJumpBattleAnime1BasedId"/> (= <c>atoh(label)</c>).
///
/// Unlike the existing <c>NavigateToAnimeId_*</c> test (which passed a 1-based id
/// directly and therefore could not catch the caller bug), this suite drives the
/// REAL Mant jump call site: it selects a Mant row through the view's own
/// <c>EntryList</c> (which loads the entry into the VM exactly as a user click
/// would) and then invokes <see cref="MantAnimationView.OpenBattleAnimeJump"/> —
/// the very method the Jump button's click handler calls. If the view ever
/// reverts to the zero-based helper, the asserted <c>AnimationNumber</c> would be
/// off by one and the test fails.
/// </summary>
[Collection("SharedState")]
public class MantAnimationJumpTests : IClassFixture<RomFixture>
{
    private readonly RomFixture _fixture;
    private readonly ITestOutputHelper _output;

    public MantAnimationJumpTests(RomFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    /// <summary>
    /// End-to-end: select a real Mant row whose 1-based label id is an anime used
    /// by some class, invoke the actual view jump method, and assert the Battle
    /// Animation editor lands on that SAME 1-based anime id (no off-by-one).
    /// </summary>
    [AvaloniaFact]
    public void MantJump_DrivesRealCallSite_LandsOnOneBasedAnime()
    {
        if (!_fixture.IsAvailable) return;

        ROM rom = CoreState.ROM;
        Assert.NotNull(rom);

        var mantView = new MantAnimationView();
        mantView.Show();
        try
        {
            var entryList = mantView.FindControl<AddressListControl>("EntryList");
            Assert.NotNull(entryList);
            var rows = entryList!.GetItems();
            if (rows.Count == 0)
            {
                _output.WriteLine("No Mant rows for this ROM; skipping.");
                return;
            }

            // Find a Mant row whose label (1-based anime id) is genuinely used by
            // a class, so NavigateToAnimeId can select a class row (Case 1) and we
            // get a concrete, comparable AnimationNumber. atoh(name) IS the 1-based
            // id (mant label = index + startadd = ToHexString(i) of the WF list).
            uint targetOneBasedId = 0;
            uint targetRowAddr = 0;
            uint expectedSettingOffset = 0;
            foreach (var row in rows)
            {
                uint oneBased = U.atoh(row.name);
                if (oneBased == 0) continue;
                uint settingOffset = ClassFormCore.GetFirstClassSettingPointerByAnimeId(rom!, oneBased);
                if (settingOffset == U.NOT_FOUND) continue;
                targetOneBasedId = oneBased;
                targetRowAddr = row.addr;
                expectedSettingOffset = settingOffset;
                break;
            }
            if (targetOneBasedId == 0)
            {
                _output.WriteLine("No Mant row whose 1-based id is used by a class; skipping.");
                return;
            }

            _output.WriteLine(
                $"Mant row addr=0x{targetRowAddr:X08}, 1-based anime id=0x{targetOneBasedId:X}, " +
                $"expected class setting offset=0x{expectedSettingOffset:X08}");

            // Drive the REAL selection path: SelectAddress fires SelectedAddressChanged
            // → the view's OnSelected → _vm.LoadEntry, exactly like a user click.
            Assert.True(entryList.SelectAddress(targetRowAddr));

            var mantVm = mantView.DataViewModel as MantAnimationViewModel;
            Assert.NotNull(mantVm);
            Assert.True(mantVm!.IsLoaded);

            // The VM resolves the CORRECT 1-based id (NOT off by one) for the jump.
            Assert.Equal(targetOneBasedId, mantVm.GetJumpBattleAnime1BasedId());
            // Sanity: the WF zero-based helper is exactly one less.
            Assert.Equal(targetOneBasedId - 1u, mantVm.GetJumpBattleAnimeId());

            // Invoke the ACTUAL jump method the Jump button's click handler calls.
            var battleAnimeView = mantView.OpenBattleAnimeJump();
            Assert.NotNull(battleAnimeView);
            try
            {
                // Case 1: a class uses this anime → the editor selected that class row.
                var ctrl = battleAnimeView!.FindControl<AddressListControl>("EntryList");
                Assert.NotNull(ctrl);
                Assert.NotNull(ctrl!.SelectedItem);
                Assert.Equal(expectedSettingOffset, ctrl.SelectedItem!.addr);

                // The loaded SP record resolves to the requested 1-based anime id —
                // PROVING there is no off-by-one in the Mant → BattleAnime jump.
                var animeVm = battleAnimeView.DataViewModel as ImageBattleAnimeViewModel;
                Assert.NotNull(animeVm);
                Assert.Equal(targetOneBasedId, animeVm!.AnimationNumber);
            }
            finally
            {
                battleAnimeView!.Close();
            }
        }
        finally
        {
            mantView.Close();
        }
    }

    /// <summary>
    /// #1408 first-row symptom: the editor auto-selects the FIRST Mant row on
    /// open (<c>SetItemsWithIcons → SelectFirst</c>). Before the fix that row's
    /// zero-based <c>GetJumpBattleAnimeId()</c> was <c>atoh(label) - 1</c> — for
    /// the lowest label that is 0, which <c>NavigateToAnimeId</c> no-ops on. This
    /// test asserts the auto-selected first row reports a NON-ZERO 1-based id
    /// equal to its label, so the default-state jump is no longer a no-op /
    /// off-by-one.
    /// </summary>
    [AvaloniaFact]
    public void MantJump_FirstRowAutoSelected_ResolvesNonZeroOneBasedId()
    {
        if (!_fixture.IsAvailable) return;

        var mantView = new MantAnimationView();
        mantView.Show();
        try
        {
            var entryList = mantView.FindControl<AddressListControl>("EntryList");
            Assert.NotNull(entryList);
            var rows = entryList!.GetItems();
            if (rows.Count == 0)
            {
                _output.WriteLine("No Mant rows for this ROM; skipping.");
                return;
            }

            // The view auto-selected the first row on open.
            var mantVm = mantView.DataViewModel as MantAnimationViewModel;
            Assert.NotNull(mantVm);
            Assert.True(mantVm!.IsLoaded);

            uint firstLabelId = U.atoh(rows[0].name);
            // The 1-based helper returns the bare label (e.g. mant_command_startadd
            // for the first row), NOT label - 1. Mant ids start at startadd >= 1,
            // so the first-row jump is a real, non-zero id — not the old no-op 0.
            Assert.Equal(firstLabelId, mantVm.GetJumpBattleAnime1BasedId());
            Assert.NotEqual(0u, mantVm.GetJumpBattleAnime1BasedId());
        }
        finally
        {
            mantView.Close();
        }
    }
}
