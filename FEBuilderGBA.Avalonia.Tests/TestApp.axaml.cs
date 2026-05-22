using Avalonia;
using Avalonia.Headless;
using Avalonia.Markup.Xaml;

[assembly: AvaloniaTestApplication(typeof(FEBuilderGBA.Avalonia.Tests.TestApp))]

namespace FEBuilderGBA.Avalonia.Tests;

public class TestApp : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public static AppBuilder BuildAvaloniaApp()
        // UseSkia + UseHeadlessDrawing=false lets tests call
        // HeadlessWindowExtensions.CaptureRenderedFrame to grab real pixels
        // (issue #349 screenshot capture). Without these, the default
        // headless drawing returns null frames. Existing visual sweep
        // tests (VisualRenderingSweepTests, etc.) still use
        // RenderTargetBitmap.Save directly and continue to work either way.
        => AppBuilder.Configure<TestApp>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
