using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using FEBuilderGBA.Avalonia;
using FEBuilderGBA.Avalonia.Dialogs;
using FEBuilderGBA.Avalonia.Services;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests
{
    /// <summary>
    /// Regression tests for the synchronous Yes/No dialog deadlock (#655).
    ///
    /// Before the fix, <see cref="AvaloniaAppServices.ShowQuestion(string)"/> called
    /// <c>task.Wait()</c> on the UI thread, which prevented the dialog's button-click
    /// events from being dispatched and froze the application after the user clicked
    /// Yes. These tests:
    ///   1. Open the dialog from the UI thread (simulating a synchronous click handler).
    ///   2. Programmatically dispatch a Yes/No button click while ShowQuestion is
    ///      still blocked synchronously on the UI thread.
    ///   3. Assert that ShowQuestion returns the expected boolean (no hang).
    ///
    /// The headless tests register a stand-in <see cref="Window"/> as the modal
    /// owner via <see cref="TestableAvaloniaAppServices"/>, because
    /// Avalonia.Headless does not provide a classic-desktop application lifetime.
    /// </summary>
    [Collection("WindowManagerSerial")]
    public class AvaloniaAppServicesDialogTests : IDisposable
    {
        static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(10);
        readonly INavigationService _originalNavigationService;

        public AvaloniaAppServicesDialogTests()
        {
            _originalNavigationService = WindowManager.Instance.Service;
            WindowManager.Instance.SetService(new DesktopNavigationService());
        }

        public void Dispose()
        {
            WindowManager.Instance.SetService(_originalNavigationService);
        }

        /// <summary>Test variant that exposes the owner-window setter.</summary>
        sealed class TestableAvaloniaAppServices : AvaloniaAppServices
        {
            public Window? Owner { get; set; }
            protected override Window? GetMainWindow() => Owner;
        }

        /// <summary>
        /// Drive the dialog: poll the visual tree for an open <see cref="MessageBoxWindow"/>,
        /// then invoke the requested button's Click event. Runs on the UI thread.
        /// </summary>
        static void ScheduleButtonClick(Window owner, string buttonName)
        {
            void TryClick()
            {
                MessageBoxWindow? msgBox = null;

                // Headless Avalonia: enumerate the owned/secondary windows
                // because there is no desktop lifetime exposing a window list.
                foreach (var w in owner.OwnedWindows)
                {
                    if (w is MessageBoxWindow mb && mb.IsVisible)
                    {
                        msgBox = mb;
                        break;
                    }
                }

                if (msgBox == null)
                {
                    Dispatcher.UIThread.Post(TryClick, DispatcherPriority.Background);
                    return;
                }

                var button = msgBox.FindControl<Button>(buttonName)
                    ?? (msgBox.Content as Control)?.FindControl<Button>(buttonName);
                Assert.NotNull(button);

                // Synthesize a click via Button.OnClick (the non-public method the
                // routed Click event handler uses). This is more reliable in
                // headless than RaiseEvent on PointerPressed/Released because we
                // don't have to simulate input device state.
                var clickMethod = typeof(Button).GetMethod(
                    "OnClick",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                Assert.NotNull(clickMethod);
                clickMethod!.Invoke(button, null);
            }

            Dispatcher.UIThread.Post(TryClick, DispatcherPriority.Background);
        }

        static Window CreateOwnerWindow()
        {
            var owner = new Window { Width = 200, Height = 100, Title = "Test Owner" };
            owner.Show();
            return owner;
        }

        [AvaloniaFact]
        public void ShowQuestion_OnUIThread_ClickYes_ReturnsTrue_WithoutDeadlock()
        {
            var owner = CreateOwnerWindow();
            var services = new TestableAvaloniaAppServices { Owner = owner };

            // Pre-arm: schedule the Yes click via the dispatcher. ShowQuestion runs
            // a nested DispatcherFrame which keeps the loop pumping; the click
            // arrives, the dialog closes, the frame exits, and ShowQuestion returns.
            ScheduleButtonClick(owner, "YesButton");

            var sw = System.Diagnostics.Stopwatch.StartNew();
            bool result = services.ShowQuestion("Regression test (Yes)?");
            sw.Stop();

            Assert.True(result, "ShowQuestion should return true after Yes click.");
            Assert.True(sw.Elapsed < TestTimeout,
                $"ShowQuestion took {sw.Elapsed} — suspect deadlock regression.");

            owner.Close();
        }

        [AvaloniaFact]
        public void ShowQuestion_OnUIThread_ClickNo_ReturnsFalse_WithoutDeadlock()
        {
            var owner = CreateOwnerWindow();
            var services = new TestableAvaloniaAppServices { Owner = owner };

            ScheduleButtonClick(owner, "NoButton");

            var sw = System.Diagnostics.Stopwatch.StartNew();
            bool result = services.ShowQuestion("Regression test (No)?");
            sw.Stop();

            Assert.False(result, "ShowQuestion should return false after No click.");
            Assert.True(sw.Elapsed < TestTimeout,
                $"ShowQuestion took {sw.Elapsed} — suspect deadlock regression.");

            owner.Close();
        }

        [AvaloniaFact]
        public void ShowYesNo_DelegatesToShowQuestion_ClickYes_ReturnsTrue()
        {
            var owner = CreateOwnerWindow();
            var services = new TestableAvaloniaAppServices { Owner = owner };

            ScheduleButtonClick(owner, "YesButton");

            bool result = services.ShowYesNo("ShowYesNo delegation test?");

            Assert.True(result);
            owner.Close();
        }

        [AvaloniaFact]
        public void ShowQuestion_NoMainWindow_ReturnsFalseSafely()
        {
            // When there is no MainWindow, ShowQuestion must not block or open a
            // modeless dialog — it should just return false (HeadlessAppServices
            // parity).
            var services = new TestableAvaloniaAppServices { Owner = null };
            bool result = services.ShowQuestion("No-owner test?");
            Assert.False(result);
        }
    }
}
