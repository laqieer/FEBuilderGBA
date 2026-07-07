using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Dialogs;
using FEBuilderGBA.Avalonia.Services;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests;

[Collection("WindowManagerSerial")]
public class SingleViewDialogTests
{
    [AvaloniaFact]
    public async Task MessageBoxWindow_Show_UsesSingleViewContent_WhenAndroidNavigationIsActive()
    {
        var original = WindowManager.Instance.Service;
        var service = new AndroidNavigationService();
        try
        {
            WindowManager.Instance.SetService(service);
            var resultTask = MessageBoxWindow.Show(null, "Continue?", "Question", MessageBoxMode.YesNo);

            var content = Assert.IsType<MessageBoxContent>(service.CurrentContent);
            Assert.Equal("Question", content.ViewTitle);
            Assert.Null(service.CurrentContent as MessageBoxWindow);

            Click(content, "YesButton");

            Assert.Equal(MessageBoxResult.Yes, await resultTask);
        }
        finally
        {
            WindowManager.Instance.SetService(original);
        }
    }

    [AvaloniaFact]
    public async Task NumberInputDialog_Show_UsesSingleViewContent_WhenAndroidNavigationIsActive()
    {
        var original = WindowManager.Instance.Service;
        var service = new AndroidNavigationService();
        try
        {
            WindowManager.Instance.SetService(service);
            var resultTask = NumberInputDialog.Show(null, "Count", "Expand", 7, 1, 20);

            var content = Assert.IsType<NumberInputContent>(service.CurrentContent);
            Assert.Equal("Expand", content.ViewTitle);
            Assert.Null(service.CurrentContent as NumberInputDialog);

            var valueBox = Assert.IsType<NumericUpDown>(content.FindControl<NumericUpDown>("ValueBox"));
            valueBox.Value = 12;
            Click(content, "OkButton");

            Assert.Equal((uint)12, await resultTask);
        }
        finally
        {
            WindowManager.Instance.SetService(original);
        }
    }

    [AvaloniaFact]
    public async Task NumberInputDialog_Show_ReturnsNull_WhenSingleViewContentCancels()
    {
        var original = WindowManager.Instance.Service;
        var service = new AndroidNavigationService();
        try
        {
            WindowManager.Instance.SetService(service);
            var resultTask = NumberInputDialog.Show(null, "Count", "Expand", 7, 1, 20);

            var content = Assert.IsType<NumberInputContent>(service.CurrentContent);
            Click(content, "CancelButton");

            Assert.Null(await resultTask);
        }
        finally
        {
            WindowManager.Instance.SetService(original);
        }
    }

    [AvaloniaFact]
    public void DesktopDialogWindows_StillWrapReusableContent()
    {
        var message = new MessageBoxWindow("Saved", "Info", MessageBoxMode.Ok);
        Assert.IsType<MessageBoxContent>(message.Content);

        var number = new NumberInputDialog("Count", "Expand", 3, 1, 10);
        Assert.IsType<NumberInputContent>(number.Content);
    }

    static void Click(Control root, string buttonName)
    {
        var button = Assert.IsType<Button>(root.FindControl<Button>(buttonName));
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }
}
