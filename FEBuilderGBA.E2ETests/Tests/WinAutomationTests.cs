using System;
using System.Collections.Generic;
using FEBuilderGBA.E2ETests.Helpers;
using Xunit;

namespace FEBuilderGBA.E2ETests.Tests
{
    public class WinAutomationTests
    {
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
    }
}
