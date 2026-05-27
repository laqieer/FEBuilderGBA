namespace FEBuilderGBA.Tests.Unit
{
    /// <summary>
    /// Regression tests for issue #651 — Button overlaps Label in Item Editor
    /// (Weapon Properties section, "Indirect Weapon Effect" button next to
    /// the "Dmg Effect (B31):" label).
    ///
    /// The bug: the StackPanel containing the NumericUpDown (Width=100) +
    /// the Button with Content="Indirect Weapon Effect" was wider than the
    /// 200 px Grid column hosting it, so the button visually overflowed
    /// into the next column and overlapped the neighbouring label.
    ///
    /// The fix: shorten the button content to "Jump" (matching the other
    /// Jump buttons in the same editor — Stat Bonuses Jump, Effective Jump,
    /// Desc Jump, etc.) so that NumericUpDown(100) + Spacing(4) +
    /// Button("Jump") fits comfortably inside the 200 px column.
    /// The full descriptive text is preserved in the ToolTip.Tip.
    /// </summary>
    public class AvaloniaButtonLabelOverlapTests
    {
        private static string SolutionDir
        {
            get
            {
                var dir = AppContext.BaseDirectory;
                while (dir != null && !File.Exists(Path.Combine(dir, "FEBuilderGBA.sln")))
                    dir = Path.GetDirectoryName(dir);
                return dir ?? throw new InvalidOperationException("Cannot find solution root");
            }
        }

        private string AvaloniaViewsDir => Path.Combine(SolutionDir, "FEBuilderGBA.Avalonia", "Views");

        [Fact]
        public void ItemEditorView_JumpToWeaponEffectButton_HasShortContent()
        {
            // The button must NOT contain the long "Indirect Weapon Effect"
            // label (which overflowed the 200 px Grid column). It should now
            // use the short "Jump" label.
            var src = File.ReadAllText(Path.Combine(AvaloniaViewsDir, "ItemEditorView.axaml"));

            // Long label must no longer be the button content.
            Assert.DoesNotContain("Content=\"Indirect Weapon Effect\"", src);

            // The fix: short "Jump" content for the JumpToWeaponEffect button.
            // Match the <Button ...> element that owns the target AutomationId
            // and assert it carries Content="Jump" somewhere in its attribute
            // list — the regex is intentionally order-independent so future
            // attribute re-orderings don't break the test.
            AssertButtonHasShortJumpContent(src, "ItemEditor_JumpToWeaponEffect_Button");

            // The full descriptive text MUST still be available in the tooltip
            // so the affordance is not lost.
            Assert.Contains("Open the indirect weapon effect table at the row for this item.", src);
        }

        [Fact]
        public void ItemFE6View_JumpToWeaponEffectButton_HasShortContent()
        {
            // Same overlap pattern exists in the FE6 item editor — verify
            // the same fix was applied there.
            var src = File.ReadAllText(Path.Combine(AvaloniaViewsDir, "ItemFE6View.axaml"));

            Assert.DoesNotContain("Content=\"Indirect Weapon Effect\"", src);

            AssertButtonHasShortJumpContent(src, "ItemFE6_JumpToWeaponEffect_Button");

            Assert.Contains("Open the indirect weapon effect table at the row for this item.", src);
        }

        /// <summary>
        /// Asserts that the .axaml source contains a single &lt;Button ...&gt;
        /// element whose attribute list includes both
        /// <c>AutomationId="{automationId}"</c> and <c>Content="Jump"</c> —
        /// regardless of the order in which those attributes appear. This
        /// keeps the regression test resilient to harmless attribute
        /// re-orderings while still proving the long "Indirect Weapon Effect"
        /// label was replaced with the short "Jump" label on the right button.
        /// </summary>
        private static void AssertButtonHasShortJumpContent(string src, string automationId)
        {
            // Capture every <Button ... /> or <Button ...>...</Button> opening
            // tag (we only need the attributes, which all live before the
            // first '>' that isn't inside an attribute value). XAML doesn't
            // allow '>' inside attribute values without escaping, so a
            // non-greedy match up to the next '>' is safe here.
            var matches = System.Text.RegularExpressions.Regex.Matches(
                src,
                @"<Button\b[^>]*?>",
                System.Text.RegularExpressions.RegexOptions.Singleline);

            foreach (System.Text.RegularExpressions.Match m in matches)
            {
                var openTag = m.Value;
                if (openTag.Contains($"AutomationId=\"{automationId}\"")
                    && openTag.Contains("Content=\"Jump\""))
                {
                    return; // Found the expected button — pass.
                }
            }

            Assert.Fail(
                $"Expected a <Button> element with AutomationId=\"{automationId}\" "
                + "and Content=\"Jump\" (in any attribute order), but none was found.");
        }

        [Fact]
        public void NoOtherViews_HaveLongIndirectWeaponEffectButton()
        {
            // Audit the whole Avalonia Views directory: no other view
            // should re-introduce the long button label that overflows.
            foreach (var axaml in Directory.GetFiles(AvaloniaViewsDir, "*.axaml"))
            {
                var src = File.ReadAllText(axaml);
                Assert.False(
                    src.Contains("Content=\"Indirect Weapon Effect\""),
                    $"{Path.GetFileName(axaml)} still uses the long 'Indirect Weapon Effect' button label that overflows the Grid column (#651).");
            }
        }
    }
}
