using System;
using System.Threading.Tasks;
using global::Avalonia.Controls;
using global::Avalonia.Headless.XUnit;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Dialogs;
using FEBuilderGBA.Avalonia.Views;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests;

[Collection("WindowManagerSerial")]
public class MessageBoxSelectableTests
{
    [AvaloniaFact]
    public void Configure_SelectableMode_IsOptIn_AndRevertsToStandardMode()
    {
        var content = new MessageBoxContent();

        content.Configure("Standard", "Info", MessageBoxMode.Ok);

        var standard = Assert.IsType<ScrollViewer>(content.FindControl<ScrollViewer>("StandardMessageScroller"));
        var selectable = Assert.IsType<TextBox>(content.FindControl<TextBox>("SelectableMessageText"));
        var copy = Assert.IsType<Button>(content.FindControl<Button>("CopyButton"));
        var status = Assert.IsType<TextBlock>(content.FindControl<TextBlock>("CopyStatus"));

        Assert.True(standard.IsVisible);
        Assert.False(selectable.IsVisible);
        Assert.False(selectable.Focusable);
        Assert.False(selectable.IsTabStop);
        Assert.False(copy.IsVisible);
        Assert.False(copy.Focusable);
        Assert.False(copy.IsTabStop);
        Assert.False(status.IsVisible);

        content.Configure("Selectable", "About", MessageBoxMode.Ok, selectable: true);

        Assert.False(standard.IsVisible);
        Assert.True(selectable.IsVisible);
        Assert.True(selectable.IsReadOnly);
        Assert.True(selectable.AcceptsReturn);
        Assert.Equal("Selectable", selectable.Text);
        Assert.True(selectable.Focusable);
        Assert.True(selectable.IsTabStop);
        Assert.True(copy.IsVisible);
        Assert.True(copy.Focusable);
        Assert.True(copy.IsTabStop);

        content.Configure("Standard again", "Info", MessageBoxMode.Ok);

        Assert.True(standard.IsVisible);
        Assert.False(selectable.IsVisible);
        Assert.False(selectable.Focusable);
        Assert.False(selectable.IsTabStop);
        Assert.False(copy.IsVisible);
        Assert.False(copy.Focusable);
        Assert.False(copy.IsTabStop);
        Assert.False(status.IsVisible);
    }

    [AvaloniaFact]
    public void CopyButton_UsesClickedInstanceWriter_FullText_AndDoesNotClose()
    {
        var first = new MessageBoxContent();
        var second = new MessageBoxContent();
        first.Configure("First full text", "About", MessageBoxMode.Ok, selectable: true);
        second.Configure("Second full text", "About", MessageBoxMode.Ok, selectable: true);

        int firstCalls = 0;
        int secondCalls = 0;
        string? copied = null;
        bool closed = false;
        first.CloseRequested += (_, _) => closed = true;
        first.ClipboardWriterOverride = text =>
        {
            firstCalls++;
            copied = text;
            return Task.CompletedTask;
        };
        second.ClipboardWriterOverride = _ =>
        {
            secondCalls++;
            return Task.CompletedTask;
        };

        var copy = Assert.IsType<Button>(first.FindControl<Button>("CopyButton"));
        copy.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        Assert.Equal(1, firstCalls);
        Assert.Equal(0, secondCalls);
        Assert.Equal("First full text", copied);
        Assert.False(closed);
        var status = Assert.IsType<TextBlock>(first.FindControl<TextBlock>("CopyStatus"));
        Assert.True(status.IsVisible);
        Assert.Equal(R._("Copied to clipboard"), status.Text);
    }

    [AvaloniaFact]
    public void CopyButton_UnavailableClipboard_ShowsStatus_AndDoesNotClose()
    {
        var content = new MessageBoxContent();
        content.Configure("About text", "About", MessageBoxMode.Ok, selectable: true);
        bool closed = false;
        content.CloseRequested += (_, _) => closed = true;

        var copy = Assert.IsType<Button>(content.FindControl<Button>("CopyButton"));
        copy.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        var status = Assert.IsType<TextBlock>(content.FindControl<TextBlock>("CopyStatus"));
        Assert.True(status.IsVisible);
        Assert.Equal(R._("Clipboard is not available."), status.Text);
        Assert.False(closed);
    }

    [AvaloniaFact]
    public void CopyButton_Failure_ShowsLocalizedError_AndDoesNotClose()
    {
        var content = new MessageBoxContent();
        content.Configure("About text", "About", MessageBoxMode.Ok, selectable: true);
        bool closed = false;
        content.CloseRequested += (_, _) => closed = true;
        content.ClipboardWriterOverride = _ =>
            Task.FromException(new InvalidOperationException("clipboard denied"));

        var copy = Assert.IsType<Button>(content.FindControl<Button>("CopyButton"));
        copy.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        var status = Assert.IsType<TextBlock>(content.FindControl<TextBlock>("CopyStatus"));
        Assert.True(status.IsVisible);
        Assert.Equal(R._("Clipboard operation failed: {0}", "clipboard denied"), status.Text);
        Assert.False(closed);
    }

    [AvaloniaFact]
    public async Task AttachedDesktopCopyButton_UsesLiveTopLevelClipboard_AndStaysOpen()
    {
        const string message = "Version 9.8.7";
        var window = new MessageBoxWindow(message, "About", MessageBoxMode.Ok, selectable: true);
        window.Show();
        try
        {
            var content = Assert.IsType<MessageBoxContent>(window.Content);
            var copy = Assert.IsType<Button>(content.FindControl<Button>("CopyButton"));

            copy.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Task.Yield();

            Assert.Equal(message, await window.Clipboard!.GetTextAsync());
            Assert.True(window.IsVisible);
            var status = Assert.IsType<TextBlock>(content.FindControl<TextBlock>("CopyStatus"));
            Assert.Equal(R._("Copied to clipboard"), status.Text);
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void AboutText_ContainsVersionLicensePreviewAndHomepage()
    {
        string text = MainWindow.BuildAboutText();

        Assert.Equal(new[]
        {
            R._("FEBuilderGBA"),
            R._("Avalonia Cross-Platform Preview"),
            R._("Version") + ": " + U.getAppVersion(),
            R._("Copyright 2017- GPLv3"),
            "",
            MainWindow.HomepageUrl,
        }, text.Split(Environment.NewLine, StringSplitOptions.None));
    }
}
