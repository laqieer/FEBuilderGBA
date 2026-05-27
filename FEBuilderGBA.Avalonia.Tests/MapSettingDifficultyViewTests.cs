using Xunit;
using Avalonia.Headless.XUnit;
using Avalonia.Controls;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.Views;

namespace FEBuilderGBA.Avalonia.Tests
{
    /// <summary>
    /// Headless Avalonia tests for the MapSettingDifficultyView banner logic
    /// added in #678. Verifies that the FE6-unsupported banner appears only on
    /// FE6 ROMs, the no-ROM banner appears only when no ROM is loaded, and
    /// FE7/FE8 ROMs show neither banner with all inputs enabled.
    /// </summary>
    [Collection("SharedState")]
    public class MapSettingDifficultyViewTests
    {
        [AvaloniaFact]
        public void View_OnFE6_ShowsUnsupportedBanner()
        {
            var origRom = CoreState.ROM;
            try
            {
                var rom = new ROM();
                rom.LoadLow("test.gba", new byte[0x1000000], "AFEJ01");
                CoreState.ROM = rom;

                var view = new MapSettingDifficultyView();
                view.Show();
                global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();

                var banner = view.FindControl<TextBlock>("UnsupportedBanner");
                var noRom = view.FindControl<TextBlock>("NoRomBanner");
                var writeBtn = view.FindControl<Button>("WriteButton");
                Assert.NotNull(banner);
                Assert.NotNull(noRom);
                Assert.NotNull(writeBtn);
                Assert.True(banner!.IsVisible);
                Assert.False(noRom!.IsVisible);
                Assert.False(writeBtn!.IsEnabled);

                view.Close();
            }
            finally { CoreState.ROM = origRom; }
        }

        [AvaloniaFact]
        public void View_OnNoRom_ShowsNoRomBanner()
        {
            var origRom = CoreState.ROM;
            try
            {
                CoreState.ROM = null;
                var view = new MapSettingDifficultyView();
                view.Show();
                global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();

                var noRom = view.FindControl<TextBlock>("NoRomBanner");
                var banner = view.FindControl<TextBlock>("UnsupportedBanner");
                Assert.NotNull(noRom);
                Assert.NotNull(banner);
                Assert.True(noRom!.IsVisible);
                Assert.False(banner!.IsVisible);

                view.Close();
            }
            finally { CoreState.ROM = origRom; }
        }

        [AvaloniaFact]
        public void View_OnFE8U_HidesBanners()
        {
            var origRom = CoreState.ROM;
            try
            {
                var rom = new ROM();
                rom.LoadLow("test.gba", new byte[0x1000000], "BE8E01");
                CoreState.ROM = rom;

                var view = new MapSettingDifficultyView();
                view.Show();
                global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();

                var banner = view.FindControl<TextBlock>("UnsupportedBanner");
                var noRom = view.FindControl<TextBlock>("NoRomBanner");
                var writeBtn = view.FindControl<Button>("WriteButton");
                Assert.NotNull(banner);
                Assert.NotNull(noRom);
                Assert.NotNull(writeBtn);
                Assert.False(banner!.IsVisible);
                Assert.False(noRom!.IsVisible);
                Assert.True(writeBtn!.IsEnabled);

                view.Close();
            }
            finally { CoreState.ROM = origRom; }
        }
    }
}
