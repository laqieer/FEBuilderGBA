using System.Collections.Generic;
using System;
using System.Linq;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
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
