// SPDX-License-Identifier: GPL-3.0-or-later
// Avalonia wiring tests for the Pointer Tool cross-ROM AutoSearch (#1113).
//   a. RunAutoSearch with no target ROM -> "Load a target ROM first."
//   b. After LoadOtherRom on the SAME current ROM file, AutoSearch populates a
//      summary line + a real address field (self-cross-ROM grep finds matches).
//   c. The PointerToolView exposes the Auto Search button + the bound summary
//      TextBlock.
// Guarded with IsAvailable so the suite skips cleanly when no ROM is present.
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.ViewModels;
using FEBuilderGBA.Avalonia.Views;
using Xunit;
using Xunit.Abstractions;

namespace FEBuilderGBA.Avalonia.Tests;

[Collection("SharedState")]
public class PointerToolAutoSearchTests : IClassFixture<RomFixture>
{
    private readonly RomFixture _fixture;
    private readonly ITestOutputHelper _output;

    public PointerToolAutoSearchTests(RomFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    /// <summary>
    /// a. With no target ROM loaded, RunAutoSearch must short-circuit with the
    /// "Load a target ROM first." summary and not throw.
    /// </summary>
    [AvaloniaFact]
    public void RunAutoSearch_NoOtherRom_PromptsToLoadTarget()
    {
        var vm = new PointerToolViewModel();
        vm.Initialize();
        vm.AddressInput = "0x08000100";

        var ex = Record.Exception(() => vm.RunAutoSearch());
        Assert.Null(ex);
        Assert.Equal("Load a target ROM first.", vm.AutoSearchSummary);
    }

    /// <summary>
    /// b. Load the SAME current ROM file as the "other ROM", pick a genuinely
    /// referenced address, and prove AutoSearch populates a non-empty summary and
    /// a real address field. Self-cross-ROM guarantees the grep finds matches.
    /// </summary>
    [AvaloniaFact]
    public void RunAutoSearch_SelfCrossRom_PopulatesSummaryAndAddress()
    {
        if (!_fixture.IsAvailable || _fixture.RomPath == null)
        {
            _output.WriteLine("No ROM available; skipping self-cross-ROM AutoSearch test.");
            return;
        }

        var rom = CoreState.ROM;
        Assert.NotNull(rom);

        // Find a data address referenced by at least one pointer in the live ROM:
        // if offset R holds a pointer to offset T, then T is referenced at R.
        uint targetOffset = 0;
        for (uint i = 0x200; i + 3 < (uint)rom!.Data.Length; i += 4)
        {
            uint v = rom.u32(i);
            if (v >= 0x08000000 && v < 0x0A000000)
            {
                uint off = v - 0x08000000;
                if (off >= 0x200 && off + 0x20 < (uint)rom.Data.Length)
                {
                    targetOffset = off;
                    break;
                }
            }
        }
        if (targetOffset == 0)
        {
            _output.WriteLine("No referenced address found; skipping.");
            return;
        }

        var vm = new PointerToolViewModel();
        vm.Initialize();
        vm.AddressInput = $"0x{targetOffset:X08}";
        // WarningLevel 2 (ignore warnings) so a genuine cross-ROM match is
        // accepted regardless of the zero-region / very-far heuristics.
        vm.WarningLevel = 2;

        // Load the SAME ROM file as the target.
        vm.LoadOtherRom(_fixture.RomPath);

        // LoadOtherRom auto-runs AutoSearch (an address is present). The summary
        // must be non-empty and report a match.
        Assert.False(string.IsNullOrEmpty(vm.AutoSearchSummary));
        _output.WriteLine($"AutoSearchSummary: {vm.AutoSearchSummary}");
        Assert.StartsWith("Matched via", vm.AutoSearchSummary);

        // At least one address field must be populated by the match.
        bool anyAddr = !string.IsNullOrEmpty(vm.OtherRomAddress)
                       || !string.IsNullOrEmpty(vm.OtherRomLdrAddress);
        Assert.True(anyAddr, "AutoSearch should populate a direct or LDR address field on a self-cross match.");
    }

    /// <summary>
    /// c. The PointerToolView exposes the Auto Search button and the bound
    /// AutoSearchSummary TextBlock (so the result is visible / captureable).
    /// </summary>
    [AvaloniaFact]
    public void PointerToolView_HasAutoSearchButtonAndSummaryLabel()
    {
        var view = new PointerToolView();
        view.Show();
        try
        {
            var button = view.FindControl<Button>("AutoSearchButton");
            Assert.NotNull(button);
            Assert.Equal("Auto Search", button!.Content);

            var label = view.FindControl<TextBlock>("AutoSearchSummaryLabel");
            Assert.NotNull(label);

            // The label binds to AutoSearchSummary: setting the VM property should
            // flow through to the rendered text.
            if (view.DataContext is PointerToolViewModel vm)
            {
                vm.AutoSearchSummary = "Matched via name (Demo): direct=0x08001234, ldr=0xFFFFFFFF";
                // Force a layout pass so the binding updates.
                view.UpdateLayout();
                Assert.Equal(vm.AutoSearchSummary, label!.Text);
            }
        }
        finally
        {
            view.Close();
        }
    }
}
