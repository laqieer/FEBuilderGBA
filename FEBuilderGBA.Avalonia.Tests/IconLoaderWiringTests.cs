using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;
using Xunit.Abstractions;

namespace FEBuilderGBA.Avalonia.Tests
{
    /// <summary>
    /// #939 detection scanner. The Avalonia list-icon loaders
    /// <c>ListIconLoaders.ClassIconLoader</c> / <c>ItemIconLoader</c> have two
    /// overloads:
    ///   - 2-arg <c>(items, i)</c>: parses the row text prefix via
    ///     <c>U.atoh(items[i].name)</c> to get the class/item id. ONLY correct
    ///     when the displayed prefix IS the entity id (the canonical
    ///     index==id editors).
    ///   - 3-arg <c>(items, i, selector)</c>: reads the real class/item id from
    ///     the entry's ROM address via an explicit selector. Required when the
    ///     prefix is the row INDEX (OP Class Demo / Arena Class), so the icon
    ///     matches the entity, not the row position.
    ///
    /// The bug (#939): three "Category A" views fed a row-index-prefixed list to
    /// the 2-arg loader → WRONG class icon; eight "Category B" views fed
    /// non-class/non-item rows (font pointers, name pointers, synthetic section
    /// labels) to a class/item loader → SPURIOUS icon.
    ///
    /// This scanner asserts:
    ///   1. The 3 Category-A views now use the 3-arg idSelector overload.
    ///   2. The 8 Category-B views no longer reference Class/ItemIconLoader at
    ///      all (their icon column was removed).
    ///   3. Every remaining 2-arg Class/ItemIconLoader call is on the explicit
    ///      allow-list of canonical index==id editors (DO-NOT-TOUCH).
    ///
    /// Pure source-text scan — no ROM, no Avalonia runtime — so it stays fast
    /// and deterministic in CI.
    /// </summary>
    public class IconLoaderWiringTests
    {
        private readonly ITestOutputHelper _output;
        public IconLoaderWiringTests(ITestOutputHelper output) => _output = output;

        // ----------------------------------------------------------------
        // Source-root discovery (same walk-up pattern as the other source
        // scanners in this project, e.g. BindingValidationTests).
        // ----------------------------------------------------------------
        private static string FindProjectRoot()
        {
            string dir = AppDomain.CurrentDomain.BaseDirectory;
            for (int i = 0; i < 12; i++)
            {
                if (File.Exists(Path.Combine(dir, "FEBuilderGBA.sln")))
                    return dir;
                string? parent = Path.GetDirectoryName(dir);
                if (parent == null || parent == dir) break;
                dir = parent;
            }
            string cwd = Directory.GetCurrentDirectory();
            for (int i = 0; i < 12; i++)
            {
                if (File.Exists(Path.Combine(cwd, "FEBuilderGBA.sln")))
                    return cwd;
                string? parent = Path.GetDirectoryName(cwd);
                if (parent == null || parent == cwd) break;
                cwd = parent;
            }
            throw new InvalidOperationException("Could not find project root (FEBuilderGBA.sln)");
        }

        private static string ViewsDir()
            => Path.Combine(FindProjectRoot(), "FEBuilderGBA.Avalonia", "Views");

        // ----------------------------------------------------------------
        // Matchers.
        // 2-arg call: SetItemsWithIcons(<list>, i => ListIconLoaders.{Class|Item}IconLoader(<list>, i))
        //   — the inner call has EXACTLY the (items, i) args, no trailing selector.
        // 3-arg call: ...IconLoader(<list>, i, <selector>)
        //
        // An icon-loader argument (<list> / index) is NOT just a bare
        // identifier: it can be a member-access chain (`_vm.Items`,
        // `this._items`) or a no-arg method call (`GetItems()`). The Arg token
        // below catches all three so the scanner can't be evaded by a
        // non-trivial list expression (#940 auto-bot review). It still stops at
        // a comma/paren, so the 2-arg pattern never swallows a 3-arg call's
        // trailing selector.
        // ----------------------------------------------------------------
        private const string Arg = @"[\w.]+(?:\(\s*\))?";

        private static readonly Regex TwoArgCall = new(
            @"ListIconLoaders\.(Class|Item)IconLoader\(\s*" + Arg + @"\s*,\s*" + Arg + @"\s*\)",
            RegexOptions.Compiled);

        // Class-specific Category-A variants: those three views are ALL
        // ClassIconLoader callsites, so an unrelated 3-arg *Item* loader in the
        // same file must NOT satisfy the "3-arg overload present" check, and a
        // residual 2-arg *Item* loader must NOT mask a class-loader regression
        // (#940 auto-bot review). Matching ClassIconLoader explicitly avoids
        // both false passes.
        private static readonly Regex ClassThreeArgCall = new(
            @"ListIconLoaders\.ClassIconLoader\(\s*" + Arg + @"\s*,\s*" + Arg + @"\s*,",
            RegexOptions.Compiled);

        private static readonly Regex ClassTwoArgCall = new(
            @"ListIconLoaders\.ClassIconLoader\(\s*" + Arg + @"\s*,\s*" + Arg + @"\s*\)",
            RegexOptions.Compiled);

        private static readonly Regex AnyIconLoaderRef = new(
            @"ListIconLoaders\.(Class|Item)IconLoader\b",
            RegexOptions.Compiled);

        // ----------------------------------------------------------------
        // The three Category-A views fixed in #939 — must use the 3-arg
        // idSelector overload (and must NOT use the 2-arg overload).
        // ----------------------------------------------------------------
        private static readonly string[] CategoryAViews =
        {
            "ClassOPDemoView.axaml.cs",
            "OPClassDemoViewerView.axaml.cs",
            "ArenaClassViewerView.axaml.cs",
        };

        // ----------------------------------------------------------------
        // The eight Category-B views fixed in #939 — rows are NOT a
        // class/item table, so they must reference NEITHER icon loader.
        // ----------------------------------------------------------------
        private static readonly string[] CategoryBViews =
        {
            "OPClassFontViewerView.axaml.cs",
            "OPClassFontFE8UView.axaml.cs",
            "OPClassAlphaNameFE6View.axaml.cs",
            "ItemEffectivenessMainView.axaml.cs",
            // #1597: ItemStatBonusesVennoView / ItemStatBonusesSkillSystemsView
            // were Category-B (synthetic section-label row, no real item id) but
            // now walk the item table and prefix the REAL item id, so they are
            // canonical index==id editors. Moved to TwoArgAllowList.
            "FE8SpellMenuExtendsView.axaml.cs",
            "OPClassAlphaNameFE6ExtraView.axaml.cs",
        };

        // ----------------------------------------------------------------
        // Allow-list of canonical index==id (or real-id-prefixed) editors
        // that legitimately keep the 2-arg text-prefix overload. Their list
        // builders emit the REAL class/item id as the row prefix, so
        // U.atoh(name) yields the correct id. (DO-NOT-TOUCH set.)
        // ----------------------------------------------------------------
        private static readonly HashSet<string> TwoArgAllowList = new(StringComparer.OrdinalIgnoreCase)
        {
            // --- Class tables (prefix == real class id) ---
            "ClassEditorView.axaml.cs",
            "ClassFE6View.axaml.cs",
            "CCBranchEditorView.axaml.cs",
            "MoveCostEditorView.axaml.cs",
            "MoveCostFE6View.axaml.cs",
            "SkillAssignmentClassSkillSystemView.axaml.cs",
            "OPClassAlphaNameView.axaml.cs",       // FE8J — prefixes real cid
            "SomeClassListView.axaml.cs",          // prefix == rom.u8(addr)
            "SoundFootStepsViewerView.axaml.cs",
            // FE7/FE7U/FE8U OP Class Demo — VMs prefix the real cid
            // (u8(addr+15) / u8(addr+11) / u8(addr+5)), verified in #939.
            "OPClassDemoFE7View.axaml.cs",
            "OPClassDemoFE7UView.axaml.cs",
            "OPClassDemoFE8UView.axaml.cs",
            // --- Item tables (prefix == real item id) ---
            "ItemEditorView.axaml.cs",
            "ItemFE6View.axaml.cs",
            "ItemEffectivenessViewerView.axaml.cs",
            "ItemEffectivenessSkillSystemsReworkView.axaml.cs",
            "ItemStatBonusesViewerView.axaml.cs",
            // #1597 — patched-ROM stat-bonus editors now walk the item table and
            // prefix the REAL item id (identical list to ItemStatBonusesViewerView).
            "ItemStatBonusesSkillSystemsView.axaml.cs",
            "ItemStatBonusesVennoView.axaml.cs",
            "ItemWeaponEffectViewerView.axaml.cs",
            "ItemRandomChestView.axaml.cs",
            "ItemUsagePointerViewerView.axaml.cs",
            "ItemPromotionViewerView.axaml.cs",    // prefix == real item/class id
            "ItemShopViewerView.axaml.cs",         // ItemShopCore prefixes real item id (#654)
        };

        /// <summary>
        /// Category A: each of the three regressed views must now use the
        /// 3-arg idSelector overload and must NOT use the 2-arg overload.
        /// </summary>
        [Fact]
        public void CategoryA_Views_Use_ThreeArg_IdSelector_Overload()
        {
            string viewsDir = ViewsDir();
            var failures = new List<string>();

            foreach (string view in CategoryAViews)
            {
                string path = Path.Combine(viewsDir, view);
                Assert.True(File.Exists(path), $"Expected Category-A view not found: {view}");
                string src = File.ReadAllText(path);

                if (!ClassThreeArgCall.IsMatch(src))
                    failures.Add($"{view}: expected a 3-arg ListIconLoaders.ClassIconLoader(items, i, selector) call but none found.");
                if (ClassTwoArgCall.IsMatch(src))
                    failures.Add($"{view}: still uses the 2-arg ListIconLoaders.ClassIconLoader(items, i) overload (prefix is the row INDEX → wrong icon).");
            }

            Assert.True(failures.Count == 0,
                "Category-A icon wiring regressed:\n" + string.Join("\n", failures));
        }

        /// <summary>
        /// Category B: each of the eight views whose rows are NOT a
        /// class/item table must reference NEITHER Class/ItemIconLoader.
        /// </summary>
        [Fact]
        public void CategoryB_Views_Do_Not_Reference_ClassOrItemIconLoader()
        {
            string viewsDir = ViewsDir();
            var failures = new List<string>();

            foreach (string view in CategoryBViews)
            {
                string path = Path.Combine(viewsDir, view);
                Assert.True(File.Exists(path), $"Expected Category-B view not found: {view}");
                string src = File.ReadAllText(path);

                if (AnyIconLoaderRef.IsMatch(src))
                    failures.Add($"{view}: still references ListIconLoaders.Class/ItemIconLoader (rows are not a class/item table → spurious icon).");
            }

            Assert.True(failures.Count == 0,
                "Category-B icon wiring regressed:\n" + string.Join("\n", failures));
        }

        /// <summary>
        /// Guard: every remaining 2-arg Class/ItemIconLoader call across ALL
        /// Views/*.axaml.cs must come from a file on the canonical allow-list.
        /// Any new view that text-prefixes a row INDEX but feeds the 2-arg
        /// (text-parse) loader will trip this assertion — that is exactly the
        /// #939 class of bug.
        /// </summary>
        [Fact]
        public void All_TwoArg_IconLoader_Callsites_Are_AllowListed()
        {
            string viewsDir = ViewsDir();
            Assert.True(Directory.Exists(viewsDir), $"Views dir not found: {viewsDir}");

            var offenders = new List<string>();
            foreach (string path in Directory.GetFiles(viewsDir, "*.axaml.cs", SearchOption.TopDirectoryOnly))
            {
                string fileName = Path.GetFileName(path);
                string src = File.ReadAllText(path);
                if (!TwoArgCall.IsMatch(src)) continue;
                if (!TwoArgAllowList.Contains(fileName))
                    offenders.Add(fileName);
            }

            if (offenders.Count > 0)
                _output.WriteLine("Unexpected 2-arg icon-loader callsites: " + string.Join(", ", offenders));

            Assert.True(offenders.Count == 0,
                "Found 2-arg ClassIconLoader/ItemIconLoader callsites NOT on the canonical index==id allow-list " +
                "(potential #939-style wrong/spurious icon). If a new view legitimately prefixes the REAL " +
                "class/item id, add it to TwoArgAllowList; otherwise switch it to the 3-arg idSelector overload " +
                "or drop the icon column:\n" + string.Join("\n", offenders));
        }

        /// <summary>
        /// Sanity: the new 3-arg overloads actually exist in the source so the
        /// scanner's premise holds (a typo'd overload would otherwise let the
        /// Category-A views fail to compile while this test still passes on the
        /// stale view source).
        /// </summary>
        [Fact]
        public void ThreeArg_IdSelector_Overloads_Exist_In_Source()
        {
            string loaders = Path.Combine(
                FindProjectRoot(), "FEBuilderGBA.Avalonia", "Services", "ListIconLoaders.cs");
            Assert.True(File.Exists(loaders), "ListIconLoaders.cs not found.");
            string src = File.ReadAllText(loaders);

            Assert.Matches(
                new Regex(@"ClassIconLoader\([^)]*Func<AddrResult,\s*uint>\s+\w+\)"),
                src);
            Assert.Matches(
                new Regex(@"ItemIconLoader\([^)]*Func<AddrResult,\s*uint>\s+\w+\)"),
                src);
        }
    }
}
