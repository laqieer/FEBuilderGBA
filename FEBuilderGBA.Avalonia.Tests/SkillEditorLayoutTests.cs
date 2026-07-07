// SPDX-License-Identifier: GPL-3.0-or-later
// Layout regression tests for the CSkillSys / FE8N skill editors (#1728-#1732).
//
// Root cause: Avalonia 11.2.3 NumericUpDown has a ~120px effective minimum
// width (the spinner-button chrome). When a NUD is placed in a fixed <=80px
// Grid column (or given Width<=80 / Width=50), it renders at its ~120px minimum
// and overflows into the adjacent label / box / next NUD. Because the columns
// are FIXED-pixel (not star), the overflow reproduces headlessly on Windows too.
//
// Three layout-only remedies (no ViewModel/ROM changes) are applied:
//   (1) Read-only / disabled NUDs -> ShowButtonSpinner="False" (removes the
//       useless spinner chrome; a consistent read-only policy).
//   (2) Editable single-NUD rows  -> widen the NUD column 80 -> 120 (a sibling
//       */Auto column absorbs the extra width). ShowFrameUpDown gets an explicit
//       element-level Width=120 instead (it lives in a StackPanel-ish row).
//   (3) Editable NUDs in dense multi-NUD grids (12 FE8N condition NUDs, 16 FE8N
//       ext-byte NUDs) -> ShowButtonSpinner="False" (keyboard up/down + typing
//       preserved because AllowSpin stays true).
//
// NOTE: App.axaml sets a *global* `NumericUpDown { MinWidth=120 }`. ShowButtonSpinner
// is therefore independent of footprint: a strategy-(1)/(3) NUD stays ~120px wide
// after the spinner is collapsed (only `MinWidth=0`, as in #1724's PaletteGrid,
// would shrink it). So the discriminating assertion differs per strategy:
//   * STRUCTURAL  (visibility/tab independent, found by Name): every strategy
//     (1)/(3) NUD must have ShowButtonSpinner==false (and AllowSpin==true for the
//     editable dense ones). These pass regardless of tab / IsVisible state, so
//     they cover the hidden N1/X-level panels and the second (Ext Bytes) tab.
//   * GEOMETRIC  (no-overlap after a forced layout pass) is asserted ONLY for the
//     strategy-(2) WIDENED rows -- where the 80->120 column genuinely lets the
//     ~120px NUD fit (FE8N Icon ID / Skill Name; FE8U Skill Name / Description;
//     the Unit/Class N1 level-up rows, force-showing their hidden panel). The
//     strategy-(1)/(3) NUDs are NOT geometric targets (they stay ~120px by design
//     under the global MinWidth) -- the plan covers them structurally instead.
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using FEBuilderGBA.Avalonia.Views;
using Xunit;
using Xunit.Abstractions;

namespace FEBuilderGBA.Avalonia.Tests
{
    public class SkillEditorLayoutTests
    {
        private readonly ITestOutputHelper _output;
        public SkillEditorLayoutTests(ITestOutputHelper output) => _output = output;

        // ==================================================================
        // Helpers
        // ==================================================================

        static NumericUpDown Nud(Control view, string name)
        {
            var nud = view.FindControl<NumericUpDown>(name);
            Assert.True(nud != null, $"NumericUpDown '{name}' must exist in the view's name scope.");
            return nud!;
        }

        /// <summary>
        /// Every named NUD must have its spinner buttons collapsed
        /// (strategy (1)/(3)). AllowSpin must stay true so the keyboard /
        /// wheel editing path still works.
        /// </summary>
        static void AssertSpinnerCollapsed(Control view, params string[] names)
        {
            foreach (var name in names)
            {
                var nud = Nud(view, name);
                Assert.False(nud.ShowButtonSpinner,
                    $"NUD '{name}' must have ShowButtonSpinner=false so its spinner chrome does " +
                    "not overflow the narrow/fixed column (#1728-#1732).");
                // The global App.axaml `NumericUpDown { MinWidth=120 }` keeps a
                // spinner-less NUD at 120px and STILL overflowing an 80px/50px slot.
                // MinWidth=0 lets it shrink to fit its cell — the exact remedy from
                // the merged #1724 ImageBattleScreenView PaletteGrid.
                Assert.Equal(0, nud.MinWidth);
                Assert.True(nud.AllowSpin,
                    $"NUD '{name}' must keep AllowSpin=true so keyboard/wheel editing still works.");
            }
        }

        /// <summary>
        /// Effective minimum width of a NUD: its explicit Width if set, else the
        /// absolute pixel width of the Grid column it occupies. Column-widened
        /// NUDs keep Width=NaN (only ShowFrameUpDown sets an element-level Width),
        /// so for those we read the parent Grid's column width instead.
        /// </summary>
        static double EffectiveMinWidth(NumericUpDown nud)
        {
            if (!double.IsNaN(nud.Width)) return nud.Width;
            var grid = nud.FindLogicalAncestorOfType<Grid>();
            if (grid != null)
            {
                int col = Grid.GetColumn(nud);
                if (col >= 0 && col < grid.ColumnDefinitions.Count)
                {
                    var w = grid.ColumnDefinitions[col].Width;
                    if (w.IsAbsolute) return w.Value;
                }
            }
            return double.NaN;
        }

        static void AssertEffectiveMinWidthAtLeast120(Control view, params string[] names)
        {
            foreach (var name in names)
            {
                var nud = Nud(view, name);
                double w = EffectiveMinWidth(nud);
                Assert.True(w >= 120,
                    $"NUD '{name}' must have an effective minimum width >=120 (explicit Width or its " +
                    $"Grid column's absolute width); got {w}. Without it the ~120px spinner chrome " +
                    "overflows the old 80px column (#1728-#1732).");
            }
        }

        static void ForceLayout(Control view)
        {
            view.UpdateLayout();
            var size = new Size(1600, 1200);
            view.Measure(size);
            view.Arrange(new Rect(size));
            view.UpdateLayout();
        }

        /// <summary>
        /// Counts, per grid row (partitioned by Grid.GetRow so multi-row grids are
        /// handled correctly), how many adjacent (left-to-right) child pairs
        /// overlap where at least one child is a NumericUpDown. A NUD whose ~120px
        /// footprint overflows its narrow cell crosses the next cell's left edge;
        /// after the 80->120 widening every NUD fits its cell so the count is zero.
        /// </summary>
        static int CountNudOverflows(Grid grid, Visual root, ITestOutputHelper output, string tag)
        {
            int overlaps = 0;
            foreach (var rowGroup in grid.Children.GroupBy(c => Grid.GetRow(c)))
            {
                var kids = rowGroup
                    .Select(c => new { C = c, P = c.TranslatePoint(new Point(0, 0), root) })
                    .Where(x => x.P.HasValue && x.C.Bounds.Width > 0)
                    .OrderBy(x => x.P!.Value.X)
                    .ToList();

                for (int i = 0; i + 1 < kids.Count; i++)
                {
                    bool nudInvolved = kids[i].C is NumericUpDown || kids[i + 1].C is NumericUpDown;
                    if (!nudInvolved) continue;

                    double aRight = kids[i].P!.Value.X + kids[i].C.Bounds.Width;
                    double bLeft = kids[i + 1].P!.Value.X;
                    if (aRight > bLeft + 0.5)
                    {
                        overlaps++;
                        output.WriteLine(
                            $"{tag}: '{(kids[i].C as Control)?.Name}' right={aRight:F1} > " +
                            $"'{(kids[i + 1].C as Control)?.Name}' left={bLeft:F1}");
                    }
                }
            }
            return overlaps;
        }

        /// <summary>
        /// Force any IsVisible-hidden container(s) visible so their NUD rows lay
        /// out and can be geometrically checked. The plan permits this for the
        /// hidden N1/X-level panels ("force ... panel IsVisible=true before the
        /// geometric check"). The set is a local value applied after Show(); no
        /// data update re-asserts the binding during the synchronous test pass.
        /// </summary>
        static void ForceVisible(Control view, params string[] controlNames)
        {
            foreach (var n in controlNames)
            {
                var c = view.FindControl<Control>(n);
                if (c != null) c.IsVisible = true;
            }
        }

        /// <summary>
        /// Geometric no-overlap over the (now-laid-out) row that owns each named
        /// strategy-(2) widened NUD. Asserts the NUD is actually realized
        /// (Bounds.Width>0) so the check can never pass vacuously, then that no
        /// NUD-involved adjacent pair overlaps.
        /// </summary>
        void AssertRowsHaveNoNudOverflow(Control view, string tag, params string[] nudNames)
        {
            foreach (var name in nudNames)
            {
                var nud = Nud(view, name);
                Assert.True(nud.Bounds.Width > 0,
                    $"Widened NUD '{name}' must be laid out (Bounds.Width>0) for the geometric check.");
                var grid = nud.FindLogicalAncestorOfType<Grid>();
                Assert.True(grid != null, $"NUD '{name}' must live inside a Grid.");
                int overlaps = CountNudOverflows(grid!, view, _output, $"{tag}/{name}");
                Assert.True(overlaps == 0,
                    $"{overlaps} NUD overflow(s) in the row owning '{name}' ({tag}); the 80->120 column " +
                    "widening must let the ~120px NUD sit in its cell without crossing the next control.");
            }
        }

        // ==================================================================
        // #1728  SkillAssignmentUnitCSkillSysView
        // ==================================================================

        [AvaloniaFact]
        public void Unit_ReadOnlyNuds_SpinnerCollapsed()
        {
            var view = new SkillAssignmentUnitCSkillSysView();
            view.Show();
            try
            {
                AssertSpinnerCollapsed(view,
                    "AddressBox", "BlockSizeBox", "SelectedAddressBox",
                    "N1AddressBox", "N1BlockSizeBox", "N1SelectedAddressBox");
            }
            finally { view.Close(); }
        }

        [AvaloniaFact]
        public void Unit_EditableNuds_HaveMinWidth120()
        {
            var view = new SkillAssignmentUnitCSkillSysView();
            view.Show();
            try
            {
                AssertEffectiveMinWidthAtLeast120(view,
                    "N1ReadCountBox", "N1B0Box", "N1B1Box");
            }
            finally { view.Close(); }
        }

        [AvaloniaFact]
        public void Unit_WidenedN1Rows_NoNudOverflow()
        {
            var view = new SkillAssignmentUnitCSkillSysView();
            view.Show();
            try
            {
                // N1ReadCountBox/N1B0Box/N1B1Box live in LevelUpGroup, hidden via
                // {Binding HasLevelUpTable} (false without a ROM). Force it visible
                // so the widened 120px columns can be geometrically verified.
                ForceVisible(view, "LevelUpGroup");
                ForceLayout(view);
                AssertRowsHaveNoNudOverflow(view, "Unit/N1",
                    "N1ReadCountBox", "N1B0Box", "N1B1Box");
            }
            finally { view.Close(); }
        }

        // ==================================================================
        // #1729  SkillAssignmentClassCSkillSysView
        // ==================================================================

        [AvaloniaFact]
        public void Class_ReadOnlyNuds_SpinnerCollapsed()
        {
            var view = new SkillAssignmentClassCSkillSysView();
            view.Show();
            try
            {
                AssertSpinnerCollapsed(view,
                    "AddressBox", "BlockSizeBox", "SelectedAddressBox",
                    "N1AddressBox", "N1BlockSizeBox", "N1SelectedAddressBox");
            }
            finally { view.Close(); }
        }

        [AvaloniaFact]
        public void Class_EditableNuds_HaveMinWidth120()
        {
            var view = new SkillAssignmentClassCSkillSysView();
            view.Show();
            try
            {
                AssertEffectiveMinWidthAtLeast120(view,
                    "N1ReadCountBox", "N1B0Box", "N1B1Box", "XLvValueBox");
            }
            finally { view.Close(); }
        }

        [AvaloniaFact]
        public void Class_WidenedN1Rows_NoNudOverflow()
        {
            var view = new SkillAssignmentClassCSkillSysView();
            view.Show();
            try
            {
                // The Class N1 rows are always visible (no IsVisible binding);
                // XLvValueBox lives in the hidden XLevelAddPanel, so force it.
                ForceVisible(view, "XLevelAddPanel");
                ForceLayout(view);
                AssertRowsHaveNoNudOverflow(view, "Class/N1",
                    "N1ReadCountBox", "N1B0Box", "N1B1Box", "XLvValueBox");
            }
            finally { view.Close(); }
        }

        // ==================================================================
        // #1730 / #1731  SkillConfigFE8NSkillView
        // ==================================================================

        static readonly string[] Fe8nCondNuds =
        {
            "CondUnit1Box", "CondUnit2Box", "CondUnit3Box", "CondUnit4Box",
            "CondClass1Box", "CondClass2Box", "CondClass3Box", "CondClass4Box",
            "CondItem1Box", "CondItem2Box", "CondItem3Box", "CondItem4Box",
        };

        static readonly string[] Fe8nExtNuds =
        {
            "ExtB16Box", "ExtB17Box", "ExtB18Box", "ExtB19Box",
            "ExtB20Box", "ExtB21Box", "ExtB22Box", "ExtB23Box",
            "ExtB24Box", "ExtB25Box", "ExtB26Box", "ExtB27Box",
            "ExtB28Box", "ExtB29Box", "ExtB30Box", "ExtB31Box",
        };

        [AvaloniaFact]
        public void Fe8n_DisabledAndDenseNuds_SpinnerCollapsed()
        {
            var view = new SkillConfigFE8NSkillView();
            view.Show();
            try
            {
                AssertSpinnerCollapsed(view, "AddressBox", "BlockSizeBox", "AnimationPointerBox");
                AssertSpinnerCollapsed(view, Fe8nCondNuds);
                AssertSpinnerCollapsed(view, Fe8nExtNuds);
            }
            finally { view.Close(); }
        }

        [AvaloniaFact]
        public void Fe8n_IconAndNameNuds_HaveMinWidth120()
        {
            var view = new SkillConfigFE8NSkillView();
            view.Show();
            try
            {
                AssertEffectiveMinWidthAtLeast120(view, "IconIdBox", "TextDetailBox");
            }
            finally { view.Close(); }
        }

        [AvaloniaFact]
        public void Fe8n_IconAndNameRows_NoNudOverflow()
        {
            var view = new SkillConfigFE8NSkillView();
            view.Show();
            try
            {
                ForceLayout(view);
                // Icon ID / Skill Name rows are strategy-(2) widened (80->120) and
                // always visible on the default tab. The dense condition rows are
                // covered by Fe8n_ConditionRows_NoNudOverflow below.
                AssertRowsHaveNoNudOverflow(view, "FE8N", "IconIdBox", "TextDetailBox");
            }
            finally { view.Close(); }
        }

        [AvaloniaFact]
        public void Fe8n_DenseNuds_KeepAllowSpin()
        {
            var view = new SkillConfigFE8NSkillView();
            view.Show();
            try
            {
                foreach (var name in Fe8nCondNuds.Concat(Fe8nExtNuds))
                    Assert.True(Nud(view, name).AllowSpin,
                        $"Dense-grid NUD '{name}' must keep AllowSpin=true (keyboard editing) after the spinner is collapsed.");
            }
            finally { view.Close(); }
        }

        // ==================================================================
        // #1732  SkillConfigFE8UCSkillSys09xView
        // ==================================================================

        [AvaloniaFact]
        public void Fe8u_DisabledNuds_SpinnerCollapsed()
        {
            var view = new SkillConfigFE8UCSkillSys09xView();
            view.Show();
            try
            {
                AssertSpinnerCollapsed(view, "AddressBox", "BlockSizeBox");
            }
            finally { view.Close(); }
        }

        [AvaloniaFact]
        public void Fe8u_EditableNuds_HaveMinWidth120()
        {
            var view = new SkillConfigFE8UCSkillSys09xView();
            view.Show();
            try
            {
                // SkillNameBox / DescriptionBox are column-widened; ShowFrameUpDown
                // gets an explicit element-level Width=120 (it lives in the hidden
                // AnimationPanel so it is found by Name, not by realized layout).
                AssertEffectiveMinWidthAtLeast120(view,
                    "SkillNameBox", "DescriptionBox", "ShowFrameUpDown");
            }
            finally { view.Close(); }
        }

        [AvaloniaFact]
        public void Fe8u_NameAndDescriptionRows_NoNudOverflow()
        {
            var view = new SkillConfigFE8UCSkillSys09xView();
            view.Show();
            try
            {
                ForceLayout(view);
                AssertRowsHaveNoNudOverflow(view, "FE8U", "SkillNameBox", "DescriptionBox");
            }
            finally { view.Close(); }
        }

        // ==================================================================
        // Strategy-(1)/(3) no-overlap — proves the MinWidth=0 (#1724 precedent)
        // fix. Each row below FAILS without MinWidth=0: the global App.axaml
        // `NumericUpDown { MinWidth=120 }` keeps a spinner-less NUD at 120px and
        // overflowing its fixed 80px slot; with MinWidth=0 the NUD fits its cell.
        // ==================================================================

        [AvaloniaFact]
        public void Unit_SelectionBars_NoNudOverflow()
        {
            var view = new SkillAssignmentUnitCSkillSysView();
            view.Show();
            try
            {
                // BlockSizeBox (top bar) and N1BlockSizeBox (N1 bar) are read-only
                // NUDs in fixed 80px Grid columns; N1 lives in the binding-hidden
                // LevelUpGroup, so force it visible.
                ForceVisible(view, "LevelUpGroup");
                ForceLayout(view);
                AssertRowsHaveNoNudOverflow(view, "Unit/bars", "BlockSizeBox", "N1BlockSizeBox");
            }
            finally { view.Close(); }
        }

        [AvaloniaFact]
        public void Fe8n_ConditionRows_NoNudOverflow()
        {
            var view = new SkillConfigFE8NSkillView();
            view.Show();
            try
            {
                // The 3 Icon-Display-Condition rows (4 NUDs each in 100,80,80,80,80)
                // are on the default tab. Pre-fix they overlapped each other (#1730).
                ForceLayout(view);
                AssertRowsHaveNoNudOverflow(view, "FE8N/cond",
                    "CondUnit1Box", "CondClass1Box", "CondItem1Box");
            }
            finally { view.Close(); }
        }

        [AvaloniaFact]
        public void Fe8n_ExtByteRows_NoNudOverflow()
        {
            var view = new SkillConfigFE8NSkillView();
            view.Show();
            try
            {
                // Ext Bytes (B16..B31, 80px NUD columns) live on the "Unit Skill
                // List" tab (#1731). Select it so the rows realize, then verify.
                // AssertRowsHaveNoNudOverflow asserts Bounds.Width>0, so a wrong tab
                // would fail loudly rather than pass vacuously.
                var tabs = view.FindControl<TabControl>("MainTabControl");
                Assert.NotNull(tabs);
                tabs!.SelectedIndex = 1;
                ForceLayout(view);
                AssertRowsHaveNoNudOverflow(view, "FE8N/ext", "ExtB16Box", "ExtB17Box");
            }
            finally { view.Close(); }
        }
    }
}
