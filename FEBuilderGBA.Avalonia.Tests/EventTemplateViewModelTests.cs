using System.Linq;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.ViewModels;
using Xunit;
using Xunit.Abstractions;

namespace FEBuilderGBA.Avalonia.Tests;

/// <summary>
/// Headless ROM-backed tests for the Event Template view-models (#1434).
/// Verifies the numbered Template 1-6 windows generate real event bytes and a
/// disassembled hex preview, and that the "Templates" browser surfaces
/// context-required templates honestly instead of emitting partial bytes.
/// </summary>
[Collection("SharedState")]
public class EventTemplateViewModelTests : IClassFixture<RomFixture>
{
    private readonly RomFixture _fixture;
    private readonly ITestOutputHelper _output;

    public EventTemplateViewModelTests(RomFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    bool Skip()
    {
        if (!_fixture.IsAvailable)
        {
            _output.WriteLine("RomFixture not available — skipping ROM-backed assertions.");
            return true;
        }
        return false;
    }

    // Button tables exist without a ROM (compile-time/config-free shape).
    [Theory]
    [InlineData(1, 5)]
    [InlineData(2, 7)]
    [InlineData(3, 7)]
    [InlineData(4, 4)]
    [InlineData(5, 1)]
    [InlineData(6, 2)]
    public void NumberedTemplate_HasExpectedButtonCount(int n, int expected)
    {
        EventTemplateViewModelBase vm = MakeVm(n);
        var buttons = vm.GetButtons();
        Assert.Equal(expected, buttons.Count);
        Assert.True(buttons[0].IsBlank);
    }

    [Fact]
    public void Template1_Blank_GeneratesToplevelBytes()
    {
        if (Skip()) return;
        var vm = new EventTemplate1ViewModel();
        var blank = vm.GetButtons()[0];
        Assert.True(vm.GenerateButton(blank));
        Assert.True(vm.HasGenerated);
        Assert.False(string.IsNullOrWhiteSpace(vm.GeneratedHex));
    }

    [Fact]
    public void Template1_VillageTalk_GeneratesDisassemblablePreview()
    {
        if (Skip()) return;
        var vm = new EventTemplate1ViewModel();
        var villageTalk = vm.GetButtons().First(b => !b.IsBlank);
        Assert.True(vm.GenerateButton(villageTalk));
        Assert.True(vm.HasGenerated);
        Assert.False(string.IsNullOrWhiteSpace(vm.GeneratedHex));
        // The preview holds readable disassembled config lines.
        Assert.Contains("//", vm.Preview);
    }

    [Fact]
    public void Browser_LoadsTemplates_AndSelectsFirst()
    {
        if (Skip()) return;
        var vm = new EventScriptTemplateViewModel();
        vm.LoadList();
        Assert.NotEmpty(vm.TemplateInfos);
        // First entry auto-selected => either previewed or context-required status.
        Assert.True(vm.HasGenerated || !string.IsNullOrEmpty(vm.Status));
    }

    [Fact]
    public void Browser_ContextRequiredTemplate_NotCopyable()
    {
        if (Skip()) return;
        var vm = new EventScriptTemplateViewModel();
        vm.LoadList();
        // Find a context-required entry by its label marker and select it.
        int idx = -1;
        for (int i = 0; i < vm.TemplateInfos.Count; i++)
        {
            if (vm.TemplateInfos[i].Contains("requires event editor context"))
            {
                idx = i;
                break;
            }
        }
        if (idx < 0)
        {
            _output.WriteLine("No context-required template in shipped list — skipping.");
            return;
        }
        vm.SelectedIndex = idx;
        Assert.False(vm.HasGenerated);            // never copyable
        Assert.True(string.IsNullOrEmpty(vm.GeneratedHex));
        Assert.False(string.IsNullOrEmpty(vm.Status)); // honest status shown

        // #1585: context-required template yields NO codes for in-editor insert either.
        var codes = vm.GetGeneratedCodes();
        Assert.NotNull(codes);
        Assert.Empty(codes);
    }

    [Fact]
    public void Browser_GeneratableTemplate_GetGeneratedCodes_ReturnsCommands()
    {
        // #1585 in-editor template insert: a placeholder-free template must yield a
        // non-empty list of editable commands for "Send to Event Editor".
        if (Skip()) return;
        var vm = new EventScriptTemplateViewModel();
        vm.LoadList();

        // Find a generatable (placeholder-free) entry: select each and stop on first
        // that produced hex.
        for (int i = 0; i < vm.TemplateInfos.Count; i++)
        {
            vm.SelectedIndex = i;
            if (vm.HasGenerated)
            {
                var codes = vm.GetGeneratedCodes();
                Assert.NotNull(codes);
                Assert.NotEmpty(codes);
                return;
            }
        }
        _output.WriteLine("No generatable template in shipped list — skipping.");
    }

    static EventTemplateViewModelBase MakeVm(int n) => n switch
    {
        1 => new EventTemplate1ViewModel(),
        2 => new EventTemplate2ViewModel(),
        3 => new EventTemplate3ViewModel(),
        4 => new EventTemplate4ViewModel(),
        5 => new EventTemplate5ViewModel(),
        6 => new EventTemplate6ViewModel(),
        _ => new EventTemplate1ViewModel(),
    };
}
