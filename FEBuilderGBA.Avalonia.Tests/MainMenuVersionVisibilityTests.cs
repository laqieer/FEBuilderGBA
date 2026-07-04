// SPDX-License-Identifier: GPL-3.0-or-later
// #1798 reproduction: on an FE8J ROM the main menu must HIDE editor buttons that
// are specific to other versions (FE6 / FE7 / FE7U / FE8U). This loads a real
// FE8J ROM into a headless MainWindow, runs the production UpdateEditorVisibility,
// and asserts the version-mismatched buttons are hidden while FE8-generic ones stay.
using System.IO;
using System.Reflection;
using System.Text;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.Views;
using global::Avalonia.Controls;
using global::Avalonia.Headless.XUnit;
using Xunit;
using Xunit.Abstractions;

namespace FEBuilderGBA.Avalonia.Tests
{
    [Collection("SharedState")]
    public class MainMenuVersionVisibilityTests : System.IDisposable
    {
        private readonly ITestOutputHelper _output;
        private readonly bool _available;
        private readonly ROM? _prevRom;
        private readonly IEtcCache? _prevComment, _prevLint, _prevWork;
        private readonly ISystemTextEncoder? _prevEncoder;
        private readonly string? _prevBaseDir;

        public MainMenuVersionVisibilityTests(ITestOutputHelper output)
        {
            _output = output;
            _prevRom = CoreState.ROM;
            _prevComment = CoreState.CommentCache;
            _prevLint = CoreState.LintCache;
            _prevWork = CoreState.WorkSupportCache;
            _prevEncoder = CoreState.SystemTextEncoder;
            _prevBaseDir = CoreState.BaseDirectory;

            string? path = TestRomLocator.FindRom("FE8J");
            if (path == null) { _available = false; return; }
            try
            {
                string dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? ".";
                CoreState.BaseDirectory = dir;
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                string cfg = Path.Combine(dir, "config", "config.xml");
                CoreState.Config = Config.LoadOrCreate(cfg);

                var rom = new ROM();
                if (!rom.Load(path, out string _)) { _available = false; return; }
                CoreState.ROM = rom;
                CoreState.CommentCache = new HeadlessEtcCache();
                CoreState.LintCache = new HeadlessEtcCache();
                CoreState.WorkSupportCache = new HeadlessEtcCache();
                try { CoreState.SystemTextEncoder = new SystemTextEncoder(CoreState.TextEncoding, rom); }
                catch { CoreState.SystemTextEncoder = new HeadlessSystemTextEncoder(rom); }
                CoreState.TextEscape ??= new TextEscape();
                CoreState.Undo ??= new Undo();
                _available = true;
            }
            catch { _available = false; }
        }

        public void Dispose()
        {
            CoreState.ROM = _prevRom;
            CoreState.CommentCache = _prevComment;
            CoreState.LintCache = _prevLint;
            CoreState.WorkSupportCache = _prevWork;
            CoreState.SystemTextEncoder = _prevEncoder;
            if (_prevBaseDir != null) CoreState.BaseDirectory = _prevBaseDir;
        }

        [AvaloniaFact]
        public void FE8J_HidesOtherVersionSpecificEditorButtons()
        {
            if (!_available) { _output.WriteLine("SKIP: FE8J.gba unavailable"); return; }

            var rom = CoreState.ROM!;
            Assert.Equal(8, rom.RomInfo.version);
            Assert.True(rom.RomInfo.is_multibyte); // FE8J

            var window = new MainWindow();

            // Version-specific buttons that must be HIDDEN on FE8J (FE6/FE7/FE7U/FE8U).
            string[] mustHide =
            {
                "UnitFE6Button", "ClassFE6Button", "ItemsFE6Button", "MoveCostFE6Button",
                "SupportFE6Button", "SupTalkFE6Button", "SupTalkFE7Button", "UnitsFE7Button",
                "EventUnitFE6Button", "EventUnitFE7Button", "BattleTalkFE6Button", "BattleTalkFE7Button",
                "MapSettingsFE6Button", "MapSettingsFE7Button", "MapSettingsFE7UButton",
                "OPDemoFE7Button", "OPDemoFE7UButton", "OPDemoFE8UButton", "OPFontFE8UButton",
                "AlphaFE6Button", "SoundRoomFE6Button", "CGFE7UButton", "ExtraFE8UButton",
                "EDFE6Button", "EDFE7Button", "PortraitFE6Button",
            };

            // Simulate RefreshEditorButtons() in Japanese: the version-specific buttons'
            // Content gets full-width parens (e.g. "Unit （FE6）"), which DEFEATS the old
            // ASCII "(FE6)" Content check. The Name-based gate (#1798 fix) must still hide
            // them. Converting ASCII parens -> full-width reproduces the exact ja.txt shape.
            foreach (string name in mustHide)
            {
                var b = window.FindControl<Button>(name);
                if (b == null) continue;
                string c = b.Content?.ToString() ?? "";
                b.Content = c.Replace('(', '\uFF08').Replace(')', '\uFF09');
            }

            // Run the production version-gating exactly as the ROM-load path does.
            typeof(MainWindow)
                .GetMethod("UpdateEditorVisibility", BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(window, null);

            // With full-width (localized) Content, gating must still hide them by Name.
            var stillVisible = new System.Collections.Generic.List<string>();
            foreach (string name in mustHide)
            {
                var btn = window.FindControl<Button>(name);
                if (btn != null && btn.IsVisible) stillVisible.Add(name);
            }
            Assert.True(stillVisible.Count == 0,
                "These version-mismatched buttons are still visible on FE8J (#1798): " +
                string.Join(", ", stillVisible));

            // Sanity: an FE8-applicable button stays visible (nothing over-hidden).
            var fe8Extra = window.FindControl<Button>("ExtraUnitButton"); // no version tag → always shown
            Assert.True(fe8Extra == null || fe8Extra.IsVisible);
        }
    }
}
