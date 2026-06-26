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
        // Find a context-required entry and select it (via the VM's own flag, robust
        // to the display-label wording change in #1591).
        int idx = FindContextRequiredIndex(vm);
        if (idx < 0)
        {
            _output.WriteLine("No context-required template in shipped list — skipping.");
            return;
        }
        vm.SelectedIndex = idx;
        Assert.True(vm.SelectedRequiresContext);
        Assert.False(vm.HasGenerated);            // standalone preview never copyable
        Assert.True(string.IsNullOrEmpty(vm.GeneratedHex));
        Assert.False(string.IsNullOrEmpty(vm.Status)); // honest status shown

        // #1585: context-required template yields NO codes for the NO-CONTEXT in-editor
        // insert path either (the gate holds without a host).
        var codes = vm.GetGeneratedCodes();
        Assert.NotNull(codes);
        Assert.Empty(codes);

        // #1591: even the context-aware path refuses with a NULL host (gate holds).
        var noHostCodes = vm.GetGeneratedCodesWithContext(null);
        Assert.NotNull(noHostCodes);
        Assert.Empty(noHostCodes);
    }

    [Fact]
    public void Browser_ContextRequired_CondTemplate_WithHost_GeneratesCodes()
    {
        // #1591: a _COND_ template now generates real substituted codes when given a
        // host context (label allocator only, no map needed). Without a host the same
        // template produces nothing (the gate).
        if (Skip()) return;
        var vm = new EventScriptTemplateViewModel();
        vm.LoadList();

        int idx = FindCondTemplateIndex(vm);
        if (idx < 0)
        {
            _output.WriteLine("No _COND_ template in shipped list — skipping.");
            return;
        }
        vm.SelectedIndex = idx;
        Assert.True(vm.SelectedRequiresContext);

        // no host => empty (gate)
        Assert.Empty(vm.GetGeneratedCodesWithContext(null));

        // host with no labels used + "no map" (Cond doesn't need a map) => real codes
        var host = new TestHost(hasMap: false, mapid: 0);
        var codes = vm.GetGeneratedCodesWithContext(host);
        Assert.NotNull(codes);
        Assert.NotEmpty(codes);
    }

    static int FindContextRequiredIndex(EventScriptTemplateViewModel vm)
    {
        for (int i = 0; i < vm.TemplateInfos.Count; i++)
        {
            vm.SelectedIndex = i;
            if (vm.SelectedRequiresContext) return i;
        }
        return -1;
    }

    int FindCondTemplateIndex(EventScriptTemplateViewModel vm)
    {
        for (int i = 0; i < vm.TemplateInfos.Count; i++)
        {
            vm.SelectedIndex = i;
            // The standalone preview Status names the file? No — use Filename which the VM
            // exposes for the selected entry.
            if (vm.SelectedRequiresContext &&
                (vm.Filename ?? "").IndexOf("_COND_", System.StringComparison.Ordinal) >= 0)
            {
                return i;
            }
        }
        return -1;
    }

    sealed class TestHost : IEventEditorHostContext
    {
        readonly bool _hasMap;
        readonly uint _mapid;
        public TestHost(bool hasMap, uint mapid) { _hasMap = hasMap; _mapid = mapid; }
        public bool TryGetMapID(out uint mapid) { mapid = _hasMap ? _mapid : 0; return _hasMap; }
        public bool IsUseLabelID(uint labelID) => false;
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
