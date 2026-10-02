using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using FEBuilderGBA.E2ETests.Helpers;
using Xunit;

namespace FEBuilderGBA.E2ETests.Tests
{
    public class WinAutomationTests
    {
        [Fact]
        public void CaptureWindows_ExcludeInvisibleNonpositiveForeignAndZeroHandles()
        {
            var native = new DiscoveryNative(new[]
            {
                new Window(new(0), 42, true, 100, 100),
                new Window(new(1), 42, false, 100, 100),
                new Window(new(2), 42, true, 0, 100),
                new Window(new(3), 42, true, 100, 0),
                new Window(new(4), 42, true, -1, 100),
                new Window(new(5), 42, true, 100, -1),
                new Window(new(6), 43, true, 100, 100),
                new Window(new(7), 42, true, 100, 100)
            });

            Assert.Equal(new[] { new IntPtr(7) }, WinAutomation.GetCaptureWindows(42, native));
            Assert.DoesNotContain(new IntPtr(1), native.BoundsTargets);
            Assert.DoesNotContain(new IntPtr(6), native.BoundsTargets);
        }

        [Fact]
        public void CaptureWindows_RecheckOwnershipAfterGeometry()
        {
            var native = new DiscoveryNative(new[] { new Window(new(1), 42, true, 100, 100) });
            native.OnBounds = () => native.OwnerOverride = 43;

            Assert.Empty(WinAutomation.GetCaptureWindows(42, native));
        }

        [Fact]
        public void CaptureWindows_NativeBoundsFailureIsExplicit()
        {
            var native = new DiscoveryNative(new[] { new Window(new(1), 42, true, 100, 100) })
            {
                OnBounds = () => throw new Win32Exception("Window bounds query failed")
            };

            Assert.Throws<Win32Exception>(() => WinAutomation.GetCaptureWindows(42, native));
        }

        [Fact]
        public void StartupDiscovery_WaitsForHiddenWindowToBecomeVisibleAndSized()
        {
            var native = new DiscoveryNative(
                new[] { new Window(new(1), 42, false, 100, 100) },
                new[] { new Window(new(1), 42, true, 0, 100) },
                new[] { new Window(new(1), 42, true, 100, 100) });
            int matches = 0;

            IntPtr result = WinAutomation.WaitForMatchingWindow(
                () => WinAutomation.GetCaptureWindows(42, native),
                _ => { matches++; return true; },
                () => false, timeoutMs: 1_000, pollMs: 0);

            Assert.Equal(new IntPtr(1), result);
            Assert.Equal(3, native.Enumerations);
            Assert.Equal(1, matches);
        }

        [Fact]
        public void StartupDiscovery_ReturnsZeroOnTimeoutWithoutEligibleWindow()
        {
            var native = new DiscoveryNative(new[] { new Window(new(1), 42, false, 100, 100) });

            IntPtr result = WinAutomation.WaitForMatchingWindow(
                () => WinAutomation.GetCaptureWindows(42, native),
                _ => throw new InvalidOperationException("Hidden windows must not be matched"),
                () => false, timeoutMs: 0, pollMs: 0);

            Assert.Equal(IntPtr.Zero, result);
            Assert.Equal(1, native.Enumerations);
        }

        [Fact]
        public void StartupDiscovery_StopsBeforeEnumerationWhenProcessExited()
        {
            IntPtr result = WinAutomation.WaitForMatchingWindow(
                () => throw new InvalidOperationException("Exited process must not be inspected"),
                _ => true, () => true, timeoutMs: 1_000, pollMs: 0);

            Assert.Equal(IntPtr.Zero, result);
        }

        [Fact]
        public void NewCaptureDiscovery_SettlesAfterHiddenCompanionBecomesCapturable()
        {
            var baseline = new HashSet<IntPtr> { new(1) };
            var main = new Window(new(1), 42, true, 100, 100);
            var child = new Window(new(2), 42, true, 100, 100);
            var companion = new Window(new(3), 42, true, 100, 100);
            var native = new DiscoveryNative(
                new[] { main, child with { Visible = false } },
                new[] { main, child with { Width = 0 } },
                new[] { main, child, companion with { Visible = false } },
                new[] { main, child, companion },
                new[] { main, child, companion });

            List<IntPtr> result = WinAutomation.WaitForNewWindows(
                () => WinAutomation.GetCaptureWindows(42, native),
                baseline, timeoutMs: 1_000, pollMs: 0, stablePollCount: 2);

            Assert.Equal(new[] { new IntPtr(2), new IntPtr(3) }, result.OrderBy(handle => handle));
            Assert.Equal(5, native.Enumerations);
        }

        [Fact]
        public void NewCaptureDiscovery_RecognizesPreexistingHiddenWindowWhenShown()
        {
            var main = new Window(new(1), 42, true, 100, 100);
            var child = new Window(new(2), 42, true, 100, 100);
            var native = new DiscoveryNative(
                new[] { main, child with { Visible = false } },
                new[] { main, child });
            var baseline = new HashSet<IntPtr>(WinAutomation.GetCaptureWindows(42, native));

            List<IntPtr> result = WinAutomation.WaitForNewWindows(
                () => WinAutomation.GetCaptureWindows(42, native),
                baseline, timeoutMs: 1_000, pollMs: 0, stablePollCount: 2);

            Assert.Equal(new[] { new IntPtr(2) }, result);
            Assert.Equal(3, native.Enumerations);
        }

        [Fact]
        public void NewCaptureDiscovery_TimesOutWithOnlyIneligibleNewHandles()
        {
            var native = new DiscoveryNative(new[]
            {
                new Window(new(1), 42, true, 100, 100),
                new Window(new(2), 42, false, 100, 100),
                new Window(new(3), 42, true, 100, 0)
            });

            List<IntPtr> result = WinAutomation.WaitForNewWindows(
                () => WinAutomation.GetCaptureWindows(42, native),
                new HashSet<IntPtr> { new(1) }, timeoutMs: 0, pollMs: 0, stablePollCount: 2);

            Assert.Empty(result);
        }

        [Fact]
        public void NewCaptureDiscovery_TimeoutReturnsOnlyObservedEligibleSet()
        {
            var native = new DiscoveryNative(new[]
            {
                new Window(new(1), 42, true, 100, 100),
                new Window(new(2), 42, true, 100, 100),
                new Window(new(3), 42, false, 100, 100)
            });

            List<IntPtr> result = WinAutomation.WaitForNewWindows(
                () => WinAutomation.GetCaptureWindows(42, native),
                new HashSet<IntPtr> { new(1) }, timeoutMs: 0, pollMs: 0, stablePollCount: 2);

            Assert.Equal(new[] { new IntPtr(2) }, result);
        }

        [Fact]
        public void LifecycleDiscovery_HiddenUnsizedHandleStillPreventsClosure()
        {
            var native = new DiscoveryNative(new[] { new Window(new(2), 42, false, 0, 0) });
            Assert.Empty(WinAutomation.GetCaptureWindows(42, native));

            Assert.False(WinAutomation.WaitForWindowsClosed(
                () => native.GetProcessWindows(42), new[] { new IntPtr(2) },
                timeoutMs: 0, pollMs: 0));
        }

        [Fact]
        public void WaitForNewWindows_WaitsForStableNewSet()
        {
            var baseline = new HashSet<IntPtr> { new(1) };
            var probe = CreateProbe(
                new[] { new IntPtr(1) },
                new[] { new IntPtr(1), new IntPtr(2) },
                new[] { new IntPtr(1), new IntPtr(2), new IntPtr(3) },
                new[] { new IntPtr(1), new IntPtr(2), new IntPtr(3) });

            List<IntPtr> result = WinAutomation.WaitForNewWindows(
                probe, baseline, timeoutMs: 1_000, pollMs: 0, stablePollCount: 2);

            Assert.Equal(2, result.Count);
            Assert.Contains(new IntPtr(2), result);
            Assert.Contains(new IntPtr(3), result);
        }

        [Fact]
        public void WaitForNewWindows_ReturnsEmptyWhenTimeoutExpires()
        {
            var baseline = new HashSet<IntPtr> { new(1) };
            var probe = CreateProbe(new[] { new IntPtr(1) });

            List<IntPtr> result = WinAutomation.WaitForNewWindows(
                probe, baseline, timeoutMs: 0, pollMs: 0, stablePollCount: 2);

            Assert.Empty(result);
        }

        [Fact]
        public void WaitForNewWindows_ReturnsObservedSetWhenStabilityTimesOut()
        {
            var baseline = new HashSet<IntPtr> { new(1) };
            var probe = CreateProbe(new[] { new IntPtr(1), new IntPtr(2) });

            List<IntPtr> result = WinAutomation.WaitForNewWindows(
                probe, baseline, timeoutMs: 0, pollMs: 0, stablePollCount: 2);

            Assert.Equal(new[] { new IntPtr(2) }, result);
        }

        [Fact]
        public void WaitForWindowsClosed_WaitsForTargetsToDisappear()
        {
            var targets = new HashSet<IntPtr> { new(2), new(3) };
            var probe = CreateProbe(
                new[] { new IntPtr(1), new IntPtr(2), new IntPtr(3) },
                new[] { new IntPtr(1), new IntPtr(3) },
                new[] { new IntPtr(1) });

            bool closed = WinAutomation.WaitForWindowsClosed(
                probe, targets, timeoutMs: 1_000, pollMs: 0);

            Assert.True(closed);
        }

        [Fact]
        public void WaitForWindowsClosed_ReturnsFalseWhenTimeoutExpires()
        {
            var targets = new HashSet<IntPtr> { new(2) };
            var probe = CreateProbe(new[] { new IntPtr(1), new IntPtr(2) });

            bool closed = WinAutomation.WaitForWindowsClosed(
                probe, targets, timeoutMs: 0, pollMs: 0);

            Assert.False(closed);
        }

        private static Func<IReadOnlyCollection<IntPtr>> CreateProbe(params IntPtr[][] snapshots)
        {
            int index = 0;
            return () =>
            {
                int current = Math.Min(index, snapshots.Length - 1);
                index++;
                return snapshots[current];
            };
        }

        private readonly record struct Window(IntPtr Handle, uint Owner, bool Visible, int Width, int Height);

        private sealed class DiscoveryNative(params Window[][] snapshots) : IWindowDiscoveryNative
        {
            private Window[] current = Array.Empty<Window>();
            public int Enumerations;
            public uint? OwnerOverride;
            public Action? OnBounds;
            public List<IntPtr> BoundsTargets = new();

            public IReadOnlyCollection<IntPtr> GetProcessWindows(int processId)
            {
                Assert.Equal(42, processId);
                current = snapshots[Math.Min(Enumerations++, snapshots.Length - 1)];
                return current.Select(window => window.Handle).ToArray();
            }

            public uint GetWindowOwner(IntPtr handle) =>
                OwnerOverride ?? current.Single(window => window.Handle == handle).Owner;

            public bool IsVisible(IntPtr handle) =>
                current.Single(window => window.Handle == handle).Visible;

            public (int Width, int Height) GetWindowSize(IntPtr handle)
            {
                BoundsTargets.Add(handle);
                OnBounds?.Invoke();
                Window window = current.Single(window => window.Handle == handle);
                return (window.Width, window.Height);
            }
        }
    }
}
