using System.Collections.Generic;
using System;
using System.Linq;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Avalonia.LogicalTree;
using Avalonia.Controls.Templates;
using Avalonia.Automation.Provider;
using System.IO;
using FEBuilderGBA.Core;
using FEBuilderGBA.Avalonia.Controls;
using FEBuilderGBA.Avalonia.Views;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.Tests;

[Collection("SharedState")]
public class RuntimeAccessibilityTests : IDisposable
{
    readonly Window? _previousMainWindow = WindowManager.Instance.MainWindow;

    public void Dispose() => WindowManager.Instance.MainWindow = _previousMainWindow;

    static string Name(Control control) =>
        ControlAutomationPeer.CreatePeerForElement(control)!.GetName();

    [AvaloniaFact]
    public void SharedTopBarFilter_AnnouncesLiveCaption()
    {
        var bar = new EditorTopBar { ShowFilter = true };
        var label = bar.FindControl<TextBlock>("FilterLabelBlock")!;
        var input = bar.FindControl<TextBox>("FilterInput")!;
        label.Text = "筛选:";
        Assert.Equal(label.Text, Name(input));
        var value = Assert.IsAssignableFrom<IValueProvider>(ControlAutomationPeer.CreatePeerForElement(input));
        value.SetValue("Lord");
        global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Equal("Lord", bar.FilterText);
    }

    [AvaloniaFact]
    public void BuildOutput_AnnouncesLocalizedPurpose_NotOutputText()
    {
        var window = new MainWindow();
        try
        {
            var output = window.GetLogicalDescendants().OfType<TextBox>()
                .Single(x => AutomationProperties.GetAutomationId(x) == "Main_DecompBuildOutput_Control");
            output.Text = "Compiler diagnostic";
            Assert.False(string.IsNullOrWhiteSpace(Name(output)));
            output.Watermark = "构建输出";
            Assert.Equal(output.Watermark, Name(output));
            var value = Assert.IsAssignableFrom<IValueProvider>(ControlAutomationPeer.CreatePeerForElement(output));
            Assert.True(value.IsReadOnly);
            Assert.Equal(output.Text, value.Value);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void BuildOutput_UsesProductionLanguageRefresh()
    {
        var window = new MainWindow();
        try
        {
            var root = new DirectoryInfo(AppContext.BaseDirectory);
            while (!File.Exists(Path.Combine(root.FullName, "FEBuilderGBA.sln")))
                root = root.Parent!;
            var output = window.FindControl<TextBox>("DecompBuildOutputBox")!;
            foreach (var (language, expected) in new[] { ("ja", "ビルド出力"), ("zh", "构建输出") })
            {
                MyTranslateResource.LoadResource(Path.Combine(root.FullName, "config", "translate", $"{language}.txt"));
                CoreState.RaiseLanguageChanged();
                global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Assert.Equal(expected, Name(output));
            }
        }
        finally
        {
            window.Close();
            MyTranslateResource.Clear();
        }
    }

    [AvaloniaFact]
    public void Classes_AllFieldsAndNumericTemplates_TrackTheirActualLabels()
    {
        var view = new ClassEditorView();
        var pairs = new[]
        {
            "NameIdBox|Name ID (W0):", "DescIdBox|Desc ID (W2):",
            "ClassNumberBox|Class # (B4):", "PromotionLevelBox|Promo Lv (B5):",
            "WaitIconBox|Wait Icon (B6):", "WalkSpeedBox|Walk Spd (B7):",
            "PortraitIdBox|Portrait (W8):", "SortOrderBox|Sort Order (B10):",
            "BaseHpBox|HP (B11):", "BaseStrBox|Str (B12):", "BaseSklBox|Skl (B13):",
            "BaseSpdBox|Spd (B14):", "BaseDefBox|Def (B15):", "BaseResBox|Res (B16):",
            "BaseConBox|Con (B17):", "BaseMovBox|Mov (B18):",
            "MaxHpBox|Max HP (B19):", "MaxStrBox|Max Str (B20):", "MaxSklBox|Max Skl (B21):",
            "MaxSpdBox|Max Spd (B22):", "MaxDefBox|Max Def (B23):", "MaxResBox|Max Res (B24):",
            "MaxConBox|Max Con (B25):", "ClassPowerBox|Power (B26):",
            "GrowHpBox|HP (B27):", "GrowStrBox|Str (B28):", "GrowSklBox|Skl (B29):",
            "GrowSpdBox|Spd (B30):", "GrowDefBox|Def (B31):", "GrowResBox|Res (B32):",
            "GrowLckBox|Lck (B33):",
            "PromoHpBox|HP (b34):", "PromoStrBox|Str (b35):", "PromoSklBox|Skl (b36):",
            "PromoSpdBox|Spd (b37):", "PromoDefBox|Def (b38):", "PromoResBox|Res (b39):",
            "B44Box|Sword (B44):", "B45Box|Lance (B45):", "B46Box|Axe (B46):",
            "B47Box|Bow (B47):", "B48Box|Staff (B48):", "B49Box|Anima (B49):",
            "B50Box|Light (B50):", "B51Box|Dark (B51):",
            "Ptr52Box|Battle Anime (P52):", "Ptr56Box|Move Cost (P56):",
            "Ptr60Box|Move Cost Rain (P60):", "Ptr64Box|Move Cost Snow (P64):",
            "Ptr68Box|Terrain Avoid (P68):", "Ptr72Box|Terrain Def (P72):",
            "Ptr76Box|Terrain Res (P76):", "D80Box|??? (D80):", "SimLevelBox|Sim Level:",
        };
        var labels = pairs.Select(pair => pair.Split('|'))
            .Select(pair => (Field: view.FindControl<Control>(pair[0])!,
                Label: view.GetLogicalDescendants().OfType<TextBlock>().Single(x => x.Text == pair[1])))
            .ToArray();
        var fields = view.GetLogicalDescendants().OfType<Control>()
            .Where(x => (x is NumericUpDown or TextBox) &&
                AutomationProperties.GetAutomationId(x)?.StartsWith("ClassEditor_") == true).ToArray();
        Assert.Equal(fields.OrderBy(x => x.Name), labels.Select(x => x.Field).OrderBy(x => x.Name));
        var window = new Window { Content = view, Width = 1400, Height = 1000 };
        try
        {
            window.Show();
            window.UpdateLayout();
            foreach (var (field, label) in labels)
            {
                Assert.Same(label, AutomationProperties.GetLabeledBy(field));
                label.Text = $"{field.Name} 翻译:";
                Assert.Equal(label.Text, Name(field));
                if (field is NumericUpDown numeric)
                {
                    var input = numeric.GetVisualDescendants().OfType<TextBox>().Single();
                    Assert.Equal(label.Text, Name(input));
                    Assert.Equal(AutomationControlType.Edit,
                        ControlAutomationPeer.CreatePeerForElement(input)!.GetAutomationControlType());
                    var provider = Assert.IsAssignableFrom<IValueProvider>(ControlAutomationPeer.CreatePeerForElement(input));
                    Assert.Equal(input.Text, provider.Value);
                    var value = numeric.Value;
                    numeric.Template = new FuncControlTemplate<NumericUpDown>((_, scope) =>
                    {
                        var text = new TextBox { Name = "PART_TextBox" };
                        scope.Register(text.Name, text);
                        return text;
                    });
                    numeric.ApplyTemplate();
                    var replacement = numeric.GetVisualDescendants().OfType<TextBox>().Single();
                    Assert.NotSame(input, replacement);
                    label.Text += " 更新";
                    Assert.Equal(label.Text, Name(replacement));
                    Assert.Equal(value, numeric.Value);
                }
                else
                    Assert.Equal(AutomationControlType.Edit,
                        ControlAutomationPeer.CreatePeerForElement(field)!.GetAutomationControlType());
            }
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void MainFilter_AnnouncesCurrentTranslatedLabel()
    {
        var window = new MainWindow();
        try
        {
            var label = window.FindControl<TextBlock>("FilterLabel")!;
            var input = window.FindControl<TextBox>("FilterTextBox")!;
            label.Text = "筛选:";
            Assert.Equal(label.Text, Name(input));
            Assert.Equal(AutomationControlType.Edit, ControlAutomationPeer.CreatePeerForElement(input)!.GetAutomationControlType());
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void SharedList_AnnouncesEntriesInsteadOfClrType_AndTranslatedSearch()
    {
        var view = new AddressListControl();
        view.SetItems(new List<AddrResult> { new(0x1000, "0x01 Knight"), new(0x1004, "0x02 Mage") });
        var window = new Window { Content = view, Width = 300, Height = 300 };
        try
        {
            window.Show();
            window.UpdateLayout();
            var search = view.FindControl<TextBox>("SearchBox")!;
            search.Watermark = "搜索...";
            Assert.Equal(search.Watermark, Name(search));
            var list = view.FindControl<ListBox>("AddressList")!;
            Assert.Equal("0x01 Knight", Name((ListBoxItem)list.ContainerFromIndex(0)!));
            Assert.Equal("0x02 Mage", Name((ListBoxItem)list.ContainerFromIndex(1)!));
            view.SelectAddress(0x1004);
            Assert.Equal(0x1004u, view.SelectedItem!.addr);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void MoveCost_AnnouncesCostTypeAndLiveTerrainLabels_InTemplateInput()
    {
        var view = new MoveCostEditorView();
        var window = new Window { Content = view, Width = 1600, Height = 800 };
        try
        {
            window.Show();
            window.UpdateLayout();
            var combo = view.FindControl<ComboBox>("CostTypeCombo")!;
            var caption = view.FindControl<TextBlock>("CostTypeCaption")!;
            caption.Text = "消费类型:";
            Assert.Equal(caption.Text, Name(combo));
            var fields = view.GetVisualDescendants().OfType<NumericUpDown>().ToArray();
            Assert.Equal(65, fields.Length);
            foreach (var field in fields)
            {
                var label = Assert.IsType<TextBlock>(AutomationProperties.GetLabeledBy(field));
                label.Text = $"0x{(int)field.Tag!:X2} 平地";
                Assert.Equal(label.Text, Name(field));
                var input = field.GetVisualDescendants().OfType<TextBox>().Single();
                Assert.Equal(label.Text, Name(input));
            }
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void SetupUrls_AnnounceTheirRepositoryLabel()
    {
        var view = new ContentRepoSetupWizardView
        {
            DataContext = new
            {
                Rows = new[]
                {
                    new { DisplayName = "Patches", Url = "https://example.invalid/patches" },
                    new { DisplayName = "Community", Url = "https://example.invalid/community" },
                },
            },
        };
        var window = new Window { Content = view, Width = 900, Height = 500 };
        try
        {
            window.Show();
            window.UpdateLayout();
            var inputs = view.GetVisualDescendants().OfType<TextBox>().Where(x => x.Watermark?.ToString() == "Remote URL").ToArray();
            Assert.Equal(2, inputs.Length);
            Assert.Equal("Patches", Name(inputs[0]));
            Assert.Equal("Community", Name(inputs[1]));
        }
        finally { window.Close(); }
    }
}
