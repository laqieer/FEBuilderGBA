using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    /// <summary>
    /// Regression tests ensuring all Avalonia views have proper scrolling support
    /// to prevent content clipping on forms with fixed sizes.
    /// </summary>
    public class AvaloniaScrollViewerTests
    {
        private static string GetViewsDirectory()
        {
            // Walk up from bin/Debug/net9.0 to find the repo root
            var dir = AppContext.BaseDirectory;
            for (int i = 0; i < 10; i++)
            {
                var candidate = Path.Combine(dir, "FEBuilderGBA.Avalonia", "Views");
                if (Directory.Exists(candidate))
                    return candidate;
                dir = Path.GetDirectoryName(dir);
                if (dir == null) break;
            }
            return null;
        }

        /// <summary>
        /// Views that are exempt from the ScrollViewer requirement.
        /// These are either: progress dialogs, simple notifications, or views
        /// that manage their own scrolling through internal ScrollViewers.
        /// </summary>
        private static readonly HashSet<string> ScrollViewerExempt = new(StringComparer.OrdinalIgnoreCase)
        {
            "NotifyPleaseWaitView.axaml",  // Simple progress indicator
            "EasyModePanel.axaml",          // UserControl, not Window
        };

        [Fact]
        public void AllWindowViews_HaveSizeToContent()
        {
            var viewsDir = GetViewsDirectory();
            if (viewsDir == null)
            {
                // Skip if running in CI without full repo structure
                return;
            }

            var violations = new List<string>();
            foreach (var file in Directory.GetFiles(viewsDir, "*.axaml"))
            {
                var content = File.ReadAllText(file);
                if (!content.Contains("<Window")) continue;

                if (!content.Contains("SizeToContent="))
                {
                    violations.Add(Path.GetFileName(file));
                }
            }

            Assert.True(violations.Count == 0,
                $"The following Window views are missing SizeToContent attribute " +
                $"(needed to prevent content clipping):\n  " +
                string.Join("\n  ", violations));
        }

        [Fact]
        public void AllWindowViews_HaveScrollViewerOrAreExempt()
        {
            var viewsDir = GetViewsDirectory();
            if (viewsDir == null)
            {
                // Skip if running in CI without full repo structure
                return;
            }

            var violations = new List<string>();
            foreach (var file in Directory.GetFiles(viewsDir, "*.axaml"))
            {
                var filename = Path.GetFileName(file);
                if (ScrollViewerExempt.Contains(filename)) continue;

                var content = File.ReadAllText(file);
                if (!content.Contains("<Window")) continue;

                if (!content.Contains("ScrollViewer"))
                {
                    violations.Add(filename);
                }
            }

            Assert.True(violations.Count == 0,
                $"The following Window views have no ScrollViewer " +
                $"(content may be clipped). Add ScrollViewer or add to exempt list:\n  " +
                string.Join("\n  ", violations));
        }

        [Fact]
        public void SizeToContent_Count_MatchesWindowCount()
        {
            var viewsDir = GetViewsDirectory();
            if (viewsDir == null) return;

            int windowCount = 0;
            int sizeToContentCount = 0;

            foreach (var file in Directory.GetFiles(viewsDir, "*.axaml"))
            {
                var content = File.ReadAllText(file);
                if (!content.Contains("<Window")) continue;
                windowCount++;
                if (content.Contains("SizeToContent="))
                    sizeToContentCount++;
            }

            Assert.Equal(windowCount, sizeToContentCount);
        }
    }
}
