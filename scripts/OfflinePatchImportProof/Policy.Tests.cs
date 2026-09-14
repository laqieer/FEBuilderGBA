using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

public static class DesktopPolicyTests
{
    sealed class ElementNotAvailableException : Exception { }
    sealed class OwnedNode
    {
        internal int Id, Pid = 7;
        internal long Handle;
        internal int[] RuntimeId = null;
        internal OwnedNode Parent, Next = null;
        internal bool OverrideNext = false, Offscreen = false, NullIdentity = false;
        internal DesktopSelector Selector;
        internal readonly List<OwnedNode> Children = new List<OwnedNode>();
    }

    sealed class OwnedNative
    {
        internal int Pid = 7;
        internal uint Root, Owner;
        internal bool Alive = true, Visible = true;
        internal string Class = "Avalonia-11111111-1111-1111-1111-111111111111";
    }

    sealed class OwnedModel : IDesktopOwnedTreeAdapter<OwnedNode>
    {
        public DesktopTreeBudget Budget { private get; set; }
        internal readonly Dictionary<uint, OwnedNative> Native = new Dictionary<uint, OwnedNative>();
        internal readonly Dictionary<uint, OwnedNode> Roots = new Dictionary<uint, OwnedNode>();
        internal readonly List<OwnedNode> SeedNodes = new List<OwnedNode>();
        internal readonly List<string> Reads = new List<string>();
        internal Action<string, OwnedNode> Before = null;
        internal Action GuardAction = () => { };
        internal OwnedNode Main, MainButton, Wizard, Close;
        internal OwnedModel()
        {
            Main = Add(null, 1, 100);
            MainButton = Add(Main, 2, 0, DesktopSelector.Main);
            Wizard = Add(Main, 3, 200);
            Close = Add(Wizard, 4, 0, DesktopSelector.Wizard);
            Native.Add(100, new OwnedNative { Root = 100 });
            Native.Add(200, new OwnedNative { Root = 200, Owner = 100 });
            Roots.Add(100, Main); Roots.Add(200, Wizard);
            SeedNodes.Add(Main);
        }
        internal OwnedNode Add(OwnedNode parent, int id, long handle = 0,
            DesktopSelector selector = DesktopSelector.Discovery)
        {
            var node = new OwnedNode { Id = id, Handle = handle, Parent = parent, Selector = selector };
            parent?.Children.Add(node);
            return node;
        }
        TValue Read<TValue>(string name, OwnedNode node, Func<TValue> value) => Budget.Call(() =>
        {
            Reads.Add(name + ":" + (node?.Id ?? 0));
            Before?.Invoke(name, node);
            return value();
        });
        internal DesktopOwnedTree<OwnedNode> Tree(string expectedClass = null) =>
            new DesktopOwnedTree<OwnedNode>(this, 7, expectedClass, "loading-handoff", () => GuardAction());
        public IReadOnlyList<OwnedNode> Seeds() => Read("seeds", null, () => SeedNodes);
        public int ProcessId(OwnedNode node) => Read("pid", node, () => node.Pid);
        public int[] Identity(OwnedNode node) => Read("identity", node, () =>
            node.NullIdentity ? null : node.RuntimeId ?? new[] { node.Id });
        public uint Handle(OwnedNode node) => Read("handle", node, () => DesktopHwnd.Key(node.Handle));
        public OwnedNode Parent(OwnedNode node) => Read("parent", node, () => node.Parent);
        public OwnedNode FirstChild(OwnedNode node) => Read("child", node, () => node.Children.Count == 0 ? null : node.Children[0]);
        public OwnedNode NextSibling(OwnedNode node) => Read("sibling", node, () =>
        {
            if (node.OverrideNext) return node.Next;
            if (node.Parent == null) return null;
            int at = node.Parent.Children.IndexOf(node) + 1;
            return at < node.Parent.Children.Count ? node.Parent.Children[at] : null;
        });
        public OwnedNode FromHandle(uint handle) => Read("from-handle", null, () => Roots.TryGetValue(handle, out var node) ? node : null);
        public bool Alive(uint handle) => Read("alive", null, () => Native.TryGetValue(handle, out var native) && native.Alive);
        public int NativePid(uint handle) => Read("native-pid", null, () => Native.TryGetValue(handle, out var native) ? native.Pid : 0);
        public uint NativeRoot(uint handle) => Read("native-root", null, () => Native.TryGetValue(handle, out var native) ? native.Root : 0);
        public uint Owner(uint handle) => Read("owner", null, () => Native[handle].Owner);
        public string Class(uint handle) => Read("class", null, () => Native[handle].Class);
        public bool Visible(uint handle) => Read("visible", null, () => Native[handle].Visible);
        public bool Offscreen(OwnedNode node) => Read("offscreen", node, () => node.Offscreen);
        public bool Matches(OwnedNode node, DesktopSelector selector) =>
            Read("match-" + selector, node, () => node.Selector == selector);
    }

    public static int RunOwnedTreeTests()
    {
        int count = 0;
        var names = new HashSet<string>(StringComparer.Ordinal);
        void Verify(bool condition)
        {
            if (!condition) throw new InvalidOperationException("Unexpected owned-tree result.");
        }
        void Case(string name, Action action)
        {
            if (!names.Add(name)) throw new InvalidOperationException("Duplicate owned-tree case.");
            try { action(); }
            catch (Exception ex) { throw new InvalidOperationException("Owned-tree case failed: " + name, ex); }
            count++;
        }
        DesktopQueryFailure Refused(Action action, string predicate)
        {
            try { action(); }
            catch (DesktopTreeException ex) when (ex.Message == predicate) { return ex.Failure; }
            throw new InvalidOperationException("Expected owned-tree refusal: " + predicate);
        }
        void GuardRefused(Action action, string predicate)
        {
            try { action(); }
            catch (DesktopTreeGuardException ex) when (ex.Message == predicate) { return; }
            throw new InvalidOperationException("Expected budget refusal: " + predicate);
        }
        OwnedNode AddRoot(OwnedModel model, uint handle, int id, OwnedNode parent,
            uint owner = 100, string windowClass = null)
        {
            var node = model.Add(parent, id, handle);
            model.Native.Add(handle, new OwnedNative { Root = handle, Owner = owner,
                Class = windowClass ?? model.Native[100].Class });
            model.Roots.Add(handle, node);
            return node;
        }
        Case("main-seed-discovers-owned-wizard", () =>
        {
            var model = new OwnedModel(); var tree = model.Tree(); tree.Discover();
            Verify(tree.Windows.Count == 2 && tree.Windows[0].Handle == 100 && tree.Windows[1].Handle == 200);
            Verify(tree.Find(100, null, DesktopSelector.Main, 1)[0] == model.MainButton);
            Verify(tree.Find(100, null, DesktopSelector.Wizard, 1).Count == 0);
            Verify(tree.Find(200, null, DesktopSelector.Wizard, 1)[0] == model.Close);
        });
        foreach (bool reverse in new[] { false, true })
        {
            Case("seed-and-owned-rediscovery-deduplicates-" + reverse, () =>
            {
                var model = new OwnedModel(); model.SeedNodes.Add(model.Wizard);
                if (reverse) model.SeedNodes.Reverse();
                var tree = model.Tree(); tree.Discover();
                Verify(tree.Windows.Count == 2 && tree.Find(200, null, DesktopSelector.Wizard, 1).Count == 1);
            });
            Case("child-order-does-not-lose-owned-root-" + reverse, () =>
            {
                var model = new OwnedModel(); if (reverse) model.Main.Children.Reverse();
                var tree = model.Tree(); tree.Discover(); Verify(tree.Windows.Count == 2);
                Verify(tree.Find(100, null, DesktopSelector.Wizard, 1).Count == 0);
            });
        }
        Case("owner-query-never-matches-wizard-descendant", () =>
        {
            var model = new OwnedModel(); var tree = model.Tree(); tree.Discover(); model.Reads.Clear();
            Verify(tree.Find(100, null, DesktopSelector.Wizard, 1).Count == 0);
            Verify(!model.Reads.Contains("match-Wizard:4") && !model.Reads.Contains("child:3"));
        });
        Case("subtree-root-is-excluded", () =>
        {
            var model = new OwnedModel(); var tree = model.Tree(); tree.Seed(model.Main);
            Verify(tree.Find(100, model.MainButton, DesktopSelector.Main, 1).Count == 0);
        });
        Case("same-native-root-child-hwnd-is-not-a-window", () =>
        {
            var model = new OwnedModel(); model.MainButton.Handle = 150;
            model.Native.Add(150, new OwnedNative { Root = 100 });
            var tree = model.Tree(); tree.Discover();
            Verify(tree.Windows.Count == 2 && tree.Find(100, null, DesktopSelector.Main, 1)[0] == model.MainButton);
        });
        Case("nearest-nonzero-hwnd-does-not-follow-filtered-parent", () =>
        {
            var model = new OwnedModel(); model.MainButton.Handle = 150;
            model.Native.Add(150, new OwnedNative { Root = 100 });
            var tree = model.Tree(); tree.Discover(); model.Reads.Clear(); tree.Validate(100, model.MainButton);
            Verify(!model.Reads.Contains("parent:2"));
        });
        Case("raw-parent-includes-native-root-through-wrappers", () =>
        {
            var model = new OwnedModel(); model.Main.Children.Remove(model.MainButton);
            var wrapper = model.Add(model.Main, 5); wrapper.Children.Add(model.MainButton);
            model.MainButton.Parent = wrapper;
            var tree = model.Tree(); tree.Discover(); model.Reads.Clear();
            tree.Validate(100, model.MainButton);
            Verify(model.Reads.Contains("parent:2") && model.Reads.Contains("parent:5"));
        });
        Case("invisible-owned-seed-still-discovers-visible-wizard", () =>
        {
            var model = new OwnedModel(); model.Native[100].Visible = false;
            var tree = model.Tree(); tree.Discover();
            Verify(!tree.Visible(tree.Windows[0]) && tree.Visible(tree.Windows[1]));
        });
        Case("offscreen-owned-root-not-returned-as-visible", () =>
        {
            var model = new OwnedModel(); model.Wizard.Offscreen = true;
            var tree = model.Tree(); tree.Discover(); Verify(!tree.Visible(tree.Windows[1]));
        });
        Case("nested-owned-confirmation-and-native-picker-discovery", () =>
        {
            var model = new OwnedModel();
            var confirmation = AddRoot(model, 300, 5, model.Wizard, 200);
            var yes = model.Add(confirmation, 6, 0, DesktopSelector.ConfirmationYes);
            var picker = AddRoot(model, 400, 7, model.Main, 100, "#32770");
            var open = model.Add(picker, 8, 401, DesktopSelector.PickerOpen);
            model.Native.Add(401, new OwnedNative { Root = 400, Class = "Button" });
            var tree = model.Tree(); tree.Discover(); Verify(tree.Windows.Count == 4);
            Verify(tree.Find(100, null, DesktopSelector.ConfirmationYes, 1).Count == 0);
            Verify(tree.Find(300, null, DesktopSelector.ConfirmationYes, 1)[0] == yes);
            Verify(tree.Find(400, null, DesktopSelector.PickerOpen, 1)[0] == open);
        });
        Case("unowned-nonmodal-editor-root-is-separately-discovered", () =>
        {
            var model = new OwnedModel(); AddRoot(model, 300, 5, model.Main, 0);
            var tree = model.Tree(); tree.Discover(); Verify(tree.Windows.Count == 3);
        });
        Case("native-root-discovered-through-native-child", () =>
        {
            var model = new OwnedModel();
            var root = AddRoot(model, 300, 5, null);
            model.Add(root, 6, 0, DesktopSelector.ConfirmationYes);
            model.Add(model.Main, 7, 301);
            model.Native.Add(301, new OwnedNative { Root = 300 });
            var tree = model.Tree(); tree.Discover();
            Verify(tree.Windows.Count == 3 && tree.Find(300, null, DesktopSelector.ConfirmationYes, 1).Count == 1);
        });
        Case("filename-edit-subtree-prunes-owned-window", () =>
        {
            var model = new OwnedModel(); var host = model.Add(model.Main, 5, 0, DesktopSelector.FilenameHost);
            var edit = model.Add(host, 6, 150, DesktopSelector.FilenameEdit);
            model.Native.Add(150, new OwnedNative { Root = 100, Class = "Edit" });
            var other = AddRoot(model, 300, 7, host);
            model.Add(other, 8, 0, DesktopSelector.FilenameEdit);
            var tree = model.Tree(); tree.Discover();
            Verify(tree.Find(100, host, DesktopSelector.FilenameEdit, 1)[0] == edit);
        });
        Case("row-and-name-query-retains-subtree-semantics", () =>
        {
            var model = new OwnedModel(); var list = model.Add(model.Main, 5, 0, DesktopSelector.List);
            var row = model.Add(list, 6, 0, DesktopSelector.Row);
            var text = model.Add(row, 7, 0, DesktopSelector.RowName);
            model.Add(model.Wizard, 8, 0, DesktopSelector.Row);
            var tree = model.Tree(); tree.Discover();
            Verify(tree.Find(100, list, DesktopSelector.Row, 1)[0] == row);
            Verify(tree.Find(100, row, DesktopSelector.RowName, 2)[0] == text);
        });
        Case("duplicate-controls-are-not-hwnd-deduplicated", () =>
        {
            var model = new OwnedModel(); model.Add(model.Main, 5, 0, DesktopSelector.Main);
            var tree = model.Tree(); tree.Discover();
            Refused(() => tree.Find(100, null, DesktopSelector.Main, 1), "query-match-bound");
        });
        foreach (int maximum in new[] { 0, 17 })
            Case("result-limit-" + maximum, () =>
            {
                var model = new OwnedModel(); var tree = model.Tree(); tree.Discover();
                Refused(() => tree.Find(100, null, DesktopSelector.Main, maximum), "query-result-limit");
            });
        Case("sixteen-matches-allowed", () =>
        {
            var model = new OwnedModel();
            for (int i = 0; i < 15; i++) model.Add(model.Main, 20 + i, 0, DesktopSelector.Main);
            var tree = model.Tree(); tree.Discover();
            Verify(tree.Find(100, null, DesktopSelector.Main, 16).Count == 16);
        });
        Case("seventeenth-match-refused", () =>
        {
            var model = new OwnedModel();
            for (int i = 0; i < 16; i++) model.Add(model.Main, 20 + i, 0, DesktopSelector.Main);
            var tree = model.Tree(); tree.Discover();
            Refused(() => tree.Find(100, null, DesktopSelector.Main, 16), "query-match-bound");
        });
        Case("foreign-uia-pid-before-all-other-node-access", () =>
        {
            var model = new OwnedModel(); model.MainButton.Pid = 8; var tree = model.Tree();
            var failure = Refused(() => tree.Discover(), "query-uia-pid");
            foreach (string read in model.Reads)
                if (read.EndsWith(":2", StringComparison.Ordinal)) Verify(read == "pid:2");
            Verify(failure.PreviouslyOwnedHandle == 0 && failure.OwnedRootBefore == 0 &&
                failure.OwnedRootAfter == 0 && failure.ExpectedOwnedRoot == 100);
        });
        Case("foreign-seed-pid-before-handle", () =>
        {
            var model = new OwnedModel(); model.Main.Pid = 8; var tree = model.Tree();
            Refused(() => tree.Discover(), "query-seed-pid");
            Verify(!model.Reads.Contains("handle:1") && !model.Reads.Contains("child:1"));
        });
        Case("foreign-native-pid-before-selector-or-descent", () =>
        {
            var model = new OwnedModel(); model.MainButton.Handle = 150;
            model.Native.Add(150, new OwnedNative { Root = 100, Pid = 8 });
            var tree = model.Tree(); var failure = Refused(() => tree.Discover(), "query-native-pid");
            Verify(!model.Reads.Contains("child:2") && !model.Reads.Contains("match-Main:2"));
            Verify(failure.PreviouslyOwnedHandle == 0 && failure.OwnedRootBefore == 0);
        });
        Case("foreign-native-root-before-selector-or-root-provider", () =>
        {
            var model = new OwnedModel(); model.MainButton.Handle = 150;
            model.Native.Add(150, new OwnedNative { Root = 900 });
            model.Native.Add(900, new OwnedNative { Root = 900, Pid = 8 });
            var tree = model.Tree(); var failure = Refused(() => tree.Discover(), "query-native-root-pid");
            Verify(failure.PreviouslyOwnedHandle == 150 && failure.OwnedRootBefore == 0 &&
                failure.OwnedRootAfter == 0 && !model.Reads.Contains("child:2"));
        });
        Case("foreign-raw-ancestor-stops-before-handle", () =>
        {
            var model = new OwnedModel(); var foreign = model.Add(null, 5, 900); foreign.Pid = 8;
            model.MainButton.Parent = foreign; var tree = model.Tree();
            Refused(() => tree.Discover(), "query-uia-pid");
            Verify(!model.Reads.Contains("handle:5") && !model.Reads.Contains("parent:5"));
        });
        Case("missing-raw-parent-refused", () =>
        {
            var model = new OwnedModel(); model.MainButton.Parent = null;
            Refused(() => model.Tree().Discover(), "query-raw-parent-missing");
        });
        Case("missing-native-root-refused", () =>
        {
            var model = new OwnedModel(); model.MainButton.Handle = 150;
            model.Native.Add(150, new OwnedNative { Root = 0 });
            Refused(() => model.Tree().Discover(), "query-native-root-pid");
        });
        Case("zero-seed-hwnd-refused", () =>
        {
            var model = new OwnedModel(); model.Main.Handle = 0;
            Refused(() => model.Tree().Discover(), "query-seed-root");
        });
        Case("native-root-must-be-top-level", () =>
        {
            var model = new OwnedModel(); model.Native[200].Root = 100;
            var tree = model.Tree(); tree.Discover();
            Refused(() => tree.Find(200, null, DesktopSelector.Wizard, 1), "query-root-self");
        });
        foreach (string windowClass in new[] { "", "other", "Avalonia-invalid" })
            Case("invalid-root-class-" + windowClass, () =>
            {
                var model = new OwnedModel(); model.Native[200].Class = windowClass;
                Refused(() => model.Tree().Discover(), "query-root-class");
            });
        Case("bound-avalonia-class-is-required", () =>
        {
            var model = new OwnedModel();
            Refused(() => model.Tree("Avalonia-22222222-2222-2222-2222-222222222222").Discover(),
                "query-root-class");
        });
        Case("foreign-owner-refused", () =>
        {
            var model = new OwnedModel(); model.Native[200].Owner = 900;
            model.Native.Add(900, new OwnedNative { Root = 900, Pid = 8 });
            Refused(() => model.Tree().Discover(), "query-owner-pid");
        });
        Case("owner-must-be-own-avalonia-top-level", () =>
        {
            var model = new OwnedModel(); model.Native[200].Owner = 900;
            model.Native.Add(900, new OwnedNative { Root = 900, Class = "#32770" });
            Refused(() => model.Tree().Discover(), "query-owner-root");
        });
        Case("owner-cycle-refused", () =>
        {
            var model = new OwnedModel(); model.Native[200].Owner = 200;
            Refused(() => model.Tree().Discover(), "query-owner-bound");
        });
        Case("root-uia-handle-alias-refused", () =>
        {
            var model = new OwnedModel(); model.Roots[200] = model.Main;
            Refused(() => model.Tree().Discover(), "query-root-uia-handle");
        });
        Case("root-uia-pid-refused", () =>
        {
            var model = new OwnedModel(); var alias = model.Add(null, 9, 200); alias.Pid = 8;
            model.Roots[200] = alias;
            Refused(() => model.Tree().Discover(), "query-root-uia-pid");
        });
        Case("changed-canonical-runtime-identity-refused", () =>
        {
            var model = new OwnedModel(); var tree = model.Tree(); tree.Discover();
            model.Main.RuntimeId = new[] { 99 };
            Refused(() => tree.Validate(100, model.MainButton), "query-root-alias");
        });
        foreach (int length in new[] { 0, 33 })
            Case("runtime-identity-length-" + length, () =>
            {
                var model = new OwnedModel(); model.MainButton.RuntimeId = new int[length];
                Refused(() => model.Tree().Discover(), "query-runtime-id");
            });
        Case("null-runtime-identity-refused", () =>
        {
            var model = new OwnedModel(); model.MainButton.NullIdentity = true;
            Refused(() => model.Tree().Discover(), "query-runtime-id");
        });
        Case("32-runtime-identity-components-allowed", () =>
        {
            var model = new OwnedModel(); model.MainButton.RuntimeId = new int[32];
            model.Tree().Discover();
        });
        Case("duplicate-raw-node-identity-refused", () =>
        {
            var model = new OwnedModel(); model.MainButton.RuntimeId = new[] { 1 };
            Refused(() => model.Tree().Discover(), "query-node-cycle");
        });
        Case("raw-child-cycle-refused", () =>
        {
            var model = new OwnedModel(); model.MainButton.Children.Add(model.Main);
            Refused(() => model.Tree().Discover(), "query-node-cycle");
        });
        Case("raw-sibling-cycle-refused", () =>
        {
            var model = new OwnedModel(); model.MainButton.OverrideNext = true; model.MainButton.Next = model.MainButton;
            Refused(() => model.Tree().Discover(), "query-node-cycle");
        });
        Case("raw-parent-cycle-refused", () =>
        {
            var model = new OwnedModel(); model.MainButton.Parent = model.MainButton;
            Refused(() => model.Tree().Discover(), "query-parent-cycle");
        });
        foreach (int depth in new[] { 32, 33 })
            Case("raw-depth-" + depth, () =>
            {
                var model = new OwnedModel(); model.Main.Children.Clear(); var parent = model.Main;
                for (int i = 1; i <= depth; i++)
                {
                    parent = model.Add(parent, 10 + i, 150);
                }
                model.Native.Add(150, new OwnedNative { Root = 100 });
                var tree = model.Tree();
                if (depth == 32) tree.Discover();
                else Refused(() => tree.Discover(), "query-depth");
            });
        foreach (int windows in new[] { 8, 9 })
            Case("canonical-window-count-" + windows, () =>
            {
                var model = new OwnedModel();
                for (int i = 2; i < windows; i++) AddRoot(model, (uint)(300 + i), 10 + i, model.Main);
                var tree = model.Tree();
                if (windows == 8) { tree.Discover(); Verify(tree.Windows.Count == 8); }
                else Refused(() => tree.Discover(), "query-window-bound");
            });
        Case("nine-seeds-refused-before-node-reads", () =>
        {
            var model = new OwnedModel(); for (int i = 1; i < 9; i++) model.SeedNodes.Add(model.Main);
            Refused(() => model.Tree().Discover(), "query-seed-bound");
            Verify(model.Reads.Count == 1 && model.Reads[0] == "seeds:0");
        });
        Case("node-budget-exact-and-one-over", () =>
        {
            var budget = new DesktopTreeBudget(() => { });
            for (int i = 0; i < 4096; i++) budget.Visit(0);
            Verify(budget.Nodes == 4096);
            GuardRefused(() => budget.Visit(0), "query-node-bound");
            int reads = 0;
            GuardRefused(() => budget.Call(() => ++reads), "query-budget-closed"); Verify(reads == 0);
        });
        Case("actual-traversal-node-budget-refuses-wide-tree", () =>
        {
            var model = new OwnedModel();
            for (int i = 0; i < 1100; i++) model.Add(model.Main, 1000 + i);
            Refused(() => model.Tree().Discover(), "query-node-bound");
        });
        Case("call-budget-exact-and-one-over", () =>
        {
            var budget = new DesktopTreeBudget(() => { }); int reads = 0;
            for (int i = 0; i < 65536; i++) budget.Call(() => ++reads);
            GuardRefused(() => budget.Call(() => ++reads), "query-call-bound");
            Verify(reads == 65536 && budget.Calls == 65536);
        });
        Case("deadline-before-first-adapter-call", () =>
        {
            var model = new OwnedModel(); model.GuardAction = () => { throw new DesktopTreeGuardException("deadline"); };
            Refused(() => model.Tree().Discover(), "deadline"); Verify(model.Reads.Count == 0);
        });
        Case("cancellation-after-provider-call-no-followup", () =>
        {
            var model = new OwnedModel();
            model.Before = (name, node) => model.GuardAction = () => { throw new DesktopTreeGuardException("cancelled"); };
            var failure = Refused(() => model.Tree().Discover(), "cancelled");
            Verify(model.Reads.Count == 1 && failure.AliveAfter == null);
        });
        Case("latched-failure-has-no-further-adapter-calls", () =>
        {
            var model = new OwnedModel(); model.MainButton.Pid = 8; var tree = model.Tree();
            var failure = Refused(() => tree.Discover(), "query-uia-pid"); int reads = model.Reads.Count;
            Verify(ReferenceEquals(failure, Refused(() => tree.Find(100, null, DesktopSelector.Main, 1), "query-uia-pid")));
            Verify(reads == model.Reads.Count);
        });
        Case("provider-message-never-leaks", () =>
        {
            var model = new OwnedModel(); model.Before = (name, node) =>
            { if (name == "handle" && node == model.MainButton) throw new Exception("PRIVATE NAME VALUE"); };
            var failure = Refused(() => model.Tree().Discover(), "query-provider-failure");
            Verify(failure.Stage == "loading-handoff" && failure.Selector == "Discovery" &&
                failure.Predicate == "query-provider-failure");
        });
        Case("stale-native-window-refused-not-skipped", () =>
        {
            var model = new OwnedModel(); var tree = model.Tree(); tree.Discover(); model.Native[100].Alive = false;
            Refused(() => tree.Validate(100, model.MainButton), "query-root-gone");
        });
        Case("reassigned-native-window-refused-not-skipped", () =>
        {
            var model = new OwnedModel(); var tree = model.Tree(); tree.Discover(); model.Native[100].Pid = 8;
            Refused(() => tree.Validate(100, model.MainButton), "query-root-pid");
        });
        Case("match-time-uia-reassignment-refused", () =>
        {
            var model = new OwnedModel(); var tree = model.Tree(); tree.Discover();
            model.Before = (name, node) => { if (name == "match-Main" && node == model.MainButton) node.Pid = 8; };
            var failure = Refused(() => tree.Find(100, null, DesktopSelector.Main, 1), "query-uia-pid");
            Verify(failure.Selector == "Main" && failure.ExpectedOwnedRoot == 100);
        });
        Case("match-time-root-change-refused", () =>
        {
            var model = new OwnedModel(); var tree = model.Tree(); tree.Discover();
            model.Before = (name, node) => { if (name == "match-Main" && node == model.MainButton) node.Parent = model.Wizard; };
            Refused(() => tree.Find(100, null, DesktopSelector.Main, 1), "query-match-root-changed");
        });
        Case("root-owner-change-refused", () =>
        {
            var model = new OwnedModel(); var tree = model.Tree(); tree.Discover(); model.Native[200].Owner = 0;
            Refused(() => tree.Validate(200, model.Close), "query-root-alias");
        });
        Case("root-change-during-canonical-provider-read-refused", () =>
        {
            var model = new OwnedModel(); model.Before = (name, node) =>
            { if (name == "from-handle") model.Native[100].Owner = model.Native[100].Owner == 0 ? 900u : 0u; };
            model.Native.Add(900, new OwnedNative { Root = 900 });
            Refused(() => model.Tree().Discover(), "query-root-changed");
        });
        Case("subtree-from-owned-other-root-is-not-owner-member", () =>
        {
            var model = new OwnedModel(); var tree = model.Tree(); tree.Discover();
            Refused(() => tree.Find(100, model.Close, DesktopSelector.Wizard, 1), "query-subtree-root");
        });
        Case("membership-from-owned-other-root-is-refused", () =>
        {
            var model = new OwnedModel(); var tree = model.Tree(); tree.Discover();
            Refused(() => tree.Validate(100, model.Close), "control-native-root");
        });
        Case("read-revalidates-native-root-after-property", () =>
        {
            var model = new OwnedModel(); var tree = model.Tree(); tree.Discover();
            Refused(() => tree.Read(100, model.MainButton, DesktopSelector.Main,
                () => { model.MainButton.Parent = model.Wizard; return true; }), "query-read-root-changed");
        });
        Case("diagnostic-refresh-is-four-calls-at-most", () =>
        {
            var model = new OwnedModel(); var tree = model.Tree(); tree.Discover();
            var failure = Refused(() => tree.Validate(100, model.Close), "control-native-root");
            Verify(failure.PreviouslyOwnedHandle == 200 && failure.OwnedRootBefore == 200 &&
                failure.OwnedRootAfter == 200 && failure.AliveBefore == true && failure.AliveAfter == true &&
                failure.OwnPidBefore == true && failure.OwnPidAfter == true && failure.RootMatchesAfter == false);
            int lastParent = model.Reads.LastIndexOf("handle:3");
            Verify(lastParent >= 0 && model.Reads.Count - lastParent - 1 == 9);
        });
        Case("diagnostics-cannot-hide-original-refusal-when-budget-closes", () =>
        {
            var model = new OwnedModel(); var tree = model.Tree(); tree.Discover(); bool mismatch = false;
            model.Before = (name, node) =>
            {
                if (name == "handle" && node == model.Wizard) mismatch = true;
                if (mismatch && name == "alive" && model.Reads.Count >= 1)
                {
                    int aliveCount = 0;
                    for (int i = model.Reads.LastIndexOf("handle:3") + 1; i < model.Reads.Count; i++)
                        if (model.Reads[i] == "alive:0") aliveCount++;
                    if (aliveCount == 3) model.GuardAction = () => { throw new DesktopTreeGuardException("deadline"); };
                }
            };
            var failure = Refused(() => tree.Validate(100, model.Close), "control-native-root");
            Verify(failure.Predicate == "control-native-root" && failure.OwnedRootBefore == 200);
        });
        Case("hwnd-low32-sign-extension-equivalence", () =>
        {
            const uint high = 0x80000001;
            Verify(DesktopHwnd.Key(unchecked((int)high)) == high && DesktopHwnd.Key(high) == high);
            Verify(DesktopHwnd.Key(DesktopHwnd.Pointer(high).ToInt64()) == high);
            Verify(DesktopHwnd.Same(new IntPtr(unchecked((int)high)), DesktopHwnd.Pointer(high)));
        });
        Case("hwnd-distinct-low-bits-remain-distinct", () =>
            Verify(DesktopHwnd.Key(0x180000001) != DesktopHwnd.Key(0x180000002) && DesktopHwnd.Key(0) == 0));
        Case("high-bit-owned-window-traversal", () =>
        {
            var model = new OwnedModel(); const uint high = 0x80000001;
            model.Native[high] = model.Native[100]; model.Native.Remove(100); model.Native[high].Root = high;
            model.Main.Handle = unchecked((int)high); model.Roots.Remove(100); model.Roots.Add(high, model.Main);
            model.Native[200].Owner = high;
            var tree = model.Tree(); tree.Discover();
            Verify(tree.Find(high, null, DesktopSelector.Main, 1)[0] == model.MainButton);
        });
        Case("high-bit-equivalence-never-bypasses-native-pid", () =>
        {
            var model = new OwnedModel(); const uint high = 0x80000001;
            model.MainButton.Handle = unchecked((int)high);
            model.Native.Add(high, new OwnedNative { Root = 100, Pid = 8 });
            Refused(() => model.Tree().Discover(), "query-native-pid");
        });
        Case("query-discovered-root-is-drained-before-return", () =>
        {
            var model = new OwnedModel(); var tree = model.Tree(); tree.Discover(); bool added = false;
            model.Before = (name, node) =>
            {
                if (!added && name == "match-Main" && node == model.MainButton)
                {
                    added = true; var extra = AddRoot(model, 300, 5, model.Main);
                    model.Add(extra, 6, 0, DesktopSelector.ConfirmationYes);
                }
            };
            Verify(tree.Find(100, null, DesktopSelector.Main, 1).Count == 1);
            Verify(tree.Windows.Count == 3 && model.Reads.Contains("child:5"));
        });
        Case("discovered-unknown-root-blocks-startup-projection", () =>
        {
            var model = new OwnedModel(); AddRoot(model, 300, 5, model.Main);
            var tree = model.Tree(); tree.Discover();
            var roots = new List<DesktopStartupRoot>();
            for (int i = 0; i < tree.Windows.Count; i++)
            {
                var root = tree.Windows[i];
                roots.Add(new DesktopStartupRoot(root.Handle, root.Owner, root.Class, tree.Visible(root),
                    tree.Find(root.Handle, null, DesktopSelector.Main, 1).Count == 1, false,
                    tree.Find(root.Handle, null, DesktopSelector.Wizard, 1).Count == 1));
            }
            Verify(roots.Count == 3 && !new DesktopStartupObservation().Observe(1, roots, false).Ready);
        });
        Case("discovered-native-picker-still-refused-at-startup", () =>
        {
            var model = new OwnedModel(); var picker = AddRoot(model, 300, 5, model.Main, 100, "#32770");
            var tree = model.Tree(); tree.Discover();
            bool refused = false;
            try
            {
                new DesktopStartupObservation().Observe(1, new[] {
                    new DesktopStartupRoot(100, 0, model.Native[100].Class, true, true, false, false),
                    new DesktopStartupRoot(300, 100, model.Native[300].Class, true, false, false, false) }, false);
            }
            catch (InvalidOperationException ex) when (ex.Message == "startup-root-class") { refused = true; }
            Verify(refused && tree.Windows.Count == 3 && picker.Handle == 300);
        });
        Case("actual-traversal-wizard-selection-drives-startup", () =>
        {
            var model = new OwnedModel(); var tree = model.Tree(); tree.Discover();
            var roots = new List<DesktopStartupRoot>();
            foreach (var root in tree.Windows)
                roots.Add(new DesktopStartupRoot(root.Handle, root.Owner, root.Class, tree.Visible(root),
                    tree.Find(root.Handle, null, DesktopSelector.Main, 1).Count == 1, false,
                    tree.Find(root.Handle, null, DesktopSelector.Wizard, 1).Count == 1));
            var decision = new DesktopStartupObservation().Observe(1, roots, false);
            Verify(decision.Ready && decision.MainHandle == 100 && decision.WizardHandle == 200);
        });
        Case("cross-native-window-cycle-is-not-consistent-rediscovery", () =>
        {
            var model = new OwnedModel(); model.Wizard.Children.Add(model.Main);
            Refused(() => model.Tree().Discover(), "query-window-cycle");
        });
        Case("unavailable-provider-is-refused-without-retry", () =>
        {
            var model = new OwnedModel(); model.Before = (name, node) =>
            { if (name == "child" && node == model.MainButton) throw new ElementNotAvailableException(); };
            var tree = model.Tree(); Refused(() => tree.Discover(), "query-element-unavailable");
            int reads = model.Reads.Count;
            Refused(() => tree.Discover(), "query-element-unavailable"); Verify(reads == model.Reads.Count);
        });
        Case("missing-canonical-uia-root-refused", () =>
        {
            var model = new OwnedModel(); model.Roots.Remove(200);
            Refused(() => model.Tree().Discover(), "query-root-uia-pid");
        });
        Case("window-key-checks-pid-before-native-handle", () =>
        {
            var model = new OwnedModel(); var tree = model.Tree(); tree.Discover();
            model.Main.Pid = 8; model.Reads.Clear();
            Refused(() => tree.WindowKey(model.Main, DesktopSelector.Main), "query-window-uia-pid");
            Verify(model.Reads.Count == 1 && model.Reads[0] == "pid:1");
        });
        Case("unsupported-selector-refused-before-matching", () =>
        {
            var model = new OwnedModel(); var tree = model.Tree(); tree.Discover(); model.Reads.Clear();
            Refused(() => tree.Find(100, null, (DesktopSelector)999, 1), "query-selector");
            Verify(model.Reads.Count == 0);
        });
        Case("owned-pid-must-be-positive", () =>
        {
            var model = new OwnedModel();
            var tree = new DesktopOwnedTree<OwnedNode>(model, 0, null, "loading-handoff", () => { });
            Refused(() => tree.Discover(), "query-owned-pid"); Verify(model.Reads.Count == 0);
        });
        foreach (int depth in new[] { 32, 33 })
            Case("raw-parent-depth-" + depth, () =>
            {
                var model = new OwnedModel(); var tree = model.Tree(); tree.Discover();
                var parent = model.Main;
                for (int i = 1; i < depth; i++) parent = model.Add(parent, 100 + i);
                model.MainButton.Parent = parent;
                if (depth == 32) tree.Validate(100, model.MainButton);
                else Refused(() => tree.Validate(100, model.MainButton), "query-depth");
            });
        foreach (int owners in new[] { 8, 9 })
            Case("native-owner-chain-" + owners, () =>
            {
                var model = new OwnedModel(); model.Native[200].Owner = 900;
                for (int i = 0; i < owners; i++)
                    model.Native.Add((uint)(900 + i), new OwnedNative { Root = (uint)(900 + i),
                        Owner = i == owners - 1 ? 0 : (uint)(901 + i) });
                if (owners == 8) model.Tree().Discover();
                else Refused(() => model.Tree().Discover(), "query-owner-bound");
            });
        Case("consistent-rediscovery-preserves-acceptance-revision", () =>
        {
            var model = new OwnedModel(); var tree = model.Tree(); tree.Discover(); int revision = tree.Revision;
            tree.Find(100, null, DesktopSelector.Main, 1); tree.CheckRevision(revision);
        });
        Case("new-observed-root-invalidates-acceptance-revision", () =>
        {
            var model = new OwnedModel(); var tree = model.Tree(); tree.Discover(); int revision = tree.Revision;
            AddRoot(model, 300, 5, model.Main); tree.Find(100, null, DesktopSelector.Main, 1);
            Refused(() => tree.CheckRevision(revision), "query-topology-changed");
        });
        Case("observed-visibility-change-invalidates-acceptance-revision", () =>
        {
            var model = new OwnedModel(); var tree = model.Tree(); tree.Discover(); int revision = tree.Revision;
            model.Native[200].Visible = false; Verify(!tree.Visible(tree.Windows[1]));
            Refused(() => tree.CheckRevision(revision), "query-topology-changed");
        });
        Case("foreign-property-node-never-invokes-content-callback", () =>
        {
            var model = new OwnedModel(); var tree = model.Tree(); tree.Discover();
            model.MainButton.Pid = 8; int reads = 0;
            Refused(() => tree.Read(100, model.MainButton, DesktopSelector.Main, () => ++reads), "query-uia-pid");
            Verify(reads == 0);
        });
        return count + RunProjectionTests();
    }

    static DesktopStartupSample<OwnedNode> ProjectStartup(OwnedModel model)
    {
        var tree = model.Tree();
        tree.Discover();
        return DesktopStartupSample<OwnedNode>.Capture(tree, root =>
            new DesktopStartupRoot(root.Handle, root.Owner, root.Class, tree.Visible(root),
                tree.Find(root.Handle, null, DesktopSelector.Main, 1).Count == 1,
                tree.Find(root.Handle, null, DesktopSelector.Loading, 1).Count == 1,
                tree.Find(root.Handle, null, DesktopSelector.Wizard, 1).Count == 1));
    }

    static int RunProjectionTests()
    {
        int count = 0;
        var names = new HashSet<string>(StringComparer.Ordinal);
        void Verify(bool condition)
        {
            if (!condition) throw new InvalidOperationException("Unexpected projection result.");
        }
        void Case(string name, Action action)
        {
            if (!names.Add(name)) throw new InvalidOperationException("Duplicate projection case.");
            try { action(); }
            catch (Exception ex) { throw new InvalidOperationException("Projection case failed: " + name, ex); }
            count++;
        }
        void Refused(Action action, string code = "query-topology-changed")
        {
            try { action(); }
            catch (DesktopTreeException ex) when (ex.Message == code) { return; }
            throw new InvalidOperationException("Expected projection refusal: " + code);
        }
        OwnedModel Unknown(bool visible = false, bool first = true)
        {
            var model = new OwnedModel();
            var unknown = model.Add(model.Main, 5, 300);
            model.Native.Add(300, new OwnedNative { Root = 300, Owner = 100, Visible = visible });
            model.Roots.Add(300, unknown);
            if (first) model.SeedNodes.Insert(0, unknown);
            else model.SeedNodes.Add(unknown);
            return model;
        }
        void ChangeDuringMainQuery(OwnedModel model, bool visible)
        {
            model.Before = (name, node) =>
            {
                if (name == "match-Main" && node == model.MainButton) model.Native[300].Visible = visible;
            };
        }
        Case("initial-projection-cannot-omit-earlier-hidden-root", () =>
        {
            var model = Unknown(); ChangeDuringMainQuery(model, true);
            Refused(() => ProjectStartup(model).Observe(new DesktopStartupObservation(), 10, false, false));
        });
        Case("acceptance-refresh-cannot-omit-earlier-hidden-root", () =>
        {
            var model = Unknown(); var observation = new DesktopStartupObservation();
            Verify(ProjectStartup(model).Observe(observation, 10, false, false).Ready);
            ChangeDuringMainQuery(model, true);
            Refused(() =>
            {
                var refresh = ProjectStartup(model);
                var decision = refresh.Observe(observation, 11, false, true);
                refresh.CheckRevision();
                Verify(!decision.Ready);
            });
        });
        Case("later-hidden-root-change-also-refuses", () =>
        {
            var model = Unknown(first: false); ChangeDuringMainQuery(model, true);
            Refused(() => ProjectStartup(model).Observe(new DesktopStartupObservation(), 10, false, false));
        });
        Case("already-projected-root-visibility-change-refuses", () =>
        {
            var model = Unknown(visible: true); ChangeDuringMainQuery(model, false);
            Refused(() => ProjectStartup(model));
        });
        Case("new-root-during-projection-refuses-instead-of-rebasing", () =>
        {
            var model = new OwnedModel(); bool added = false;
            model.Before = (name, node) =>
            {
                if (!added && name == "match-Main" && node == model.MainButton)
                {
                    added = true;
                    var unknown = model.Add(model.Main, 5, 300);
                    model.Native.Add(300, new OwnedNative { Root = 300, Owner = 100 });
                    model.Roots.Add(300, unknown);
                }
            };
            Refused(() => ProjectStartup(model));
        });
        Case("visibility-change-before-first-projection-read-refuses", () =>
        {
            var model = Unknown(); var tree = model.Tree(); tree.Discover();
            model.Native[300].Visible = true;
            Refused(() => DesktopStartupSample<OwnedNode>.Capture(tree, root =>
                new DesktopStartupRoot(root.Handle, root.Owner, root.Class, true, false, false, false)));
        });
        Case("observed-change-between-capture-and-decision-refuses", () =>
        {
            var model = Unknown(); var sample = ProjectStartup(model); int revision = sample.Revision;
            model.Native[300].Visible = true; sample.Tree.Find(100, null, DesktopSelector.Main, 1);
            Verify(sample.Revision == revision && sample.Tree.Revision != revision);
            Refused(() => sample.Observe(new DesktopStartupObservation(), 10, false, false));
        });
        Case("observed-change-after-refresh-decision-refuses-final-acceptance", () =>
        {
            var model = Unknown(); var observation = new DesktopStartupObservation();
            Verify(ProjectStartup(model).Observe(observation, 10, false, false).Ready);
            var refresh = ProjectStartup(model);
            Verify(refresh.Observe(observation, 11, false, true).Ready);
            model.Native[300].Visible = true; refresh.Tree.Find(100, null, DesktopSelector.Main, 1);
            Refused(() => refresh.CheckRevision());
        });
        Case("stable-hidden-root-keeps-honest-candidate-and-refresh", () =>
        {
            var model = Unknown(); var observation = new DesktopStartupObservation();
            var sample = ProjectStartup(model);
            Verify(sample.Windows.Count == 2 && sample.Roots.Count == 2);
            Verify(sample.Observe(observation, 10, false, false).Route == DesktopStartupObservation.UnobservedRoute);
            var refresh = ProjectStartup(model);
            Verify(refresh.Observe(observation, 11, false, true).Ready); refresh.CheckRevision();
        });
        Case("stable-visible-unknown-root-is-present-and-blocks", () =>
        {
            var sample = ProjectStartup(Unknown(visible: true));
            Verify(sample.Windows.Count == 3 && sample.Roots.Count == 3);
            Verify(sample.Roots[0].Handle == 300 &&
                !sample.Observe(new DesktopStartupObservation(), 10, false, false).Ready);
        });
        Case("stable-main-only-projection-remains-valid", () =>
        {
            var model = new OwnedModel(); model.Native[200].Visible = false;
            var sample = ProjectStartup(model);
            var decision = sample.Observe(new DesktopStartupObservation(), 10, false, false);
            Verify(sample.Roots.Count == 1 && decision.Ready && decision.WizardHandle == 0);
        });
        Case("stable-observed-loading-route-remains-strict", () =>
        {
            var model = Unknown(visible: true); model.Roots[300].Selector = DesktopSelector.Loading;
            model.Add(model.Roots[300], 6, 0, DesktopSelector.Loading);
            var observation = new DesktopStartupObservation();
            Verify(!ProjectStartup(model).Observe(observation, 10, false, false).Ready && observation.LoadingObserved);
            model.Main.Children.Remove(model.Roots[300]); model.SeedNodes.Remove(model.Roots[300]);
            model.Native[300].Alive = false;
            var sample = ProjectStartup(model);
            Verify(sample.Observe(observation, 20, false, false).Route == DesktopStartupObservation.ObservedRoute);
            Verify(ProjectStartup(model).Observe(observation, 21, false, true).Ready);
        });
        Case("stable-missing-main-control-does-not-become-ready", () =>
        {
            var model = new OwnedModel(); model.MainButton.Selector = DesktopSelector.Discovery;
            Verify(!ProjectStartup(model).Observe(new DesktopStartupObservation(), 10, false, false).Ready);
        });
        Case("projection-preserves-specific-ownership-refusal", () =>
        {
            var model = new OwnedModel();
            model.Before = (name, node) =>
            {
                if (name == "match-Main" && node == model.MainButton) model.MainButton.Pid = 8;
            };
            Refused(() => ProjectStartup(model), "query-uia-pid");
        });
        return count;
    }

    public static int RunStartupTests()
    {
        const string firstClass = "Avalonia-11111111-1111-1111-1111-111111111111";
        const string otherClass = "Avalonia-22222222-2222-2222-2222-222222222222";
        int count = 0;
        DesktopStartupRoot Root(long handle = 200, bool main = true, bool loading = false,
            bool wizard = false, long owner = 0, string windowClass = firstClass, bool visible = true)
            => new DesktopStartupRoot(handle, owner, windowClass, visible, main, loading, wizard);
        DesktopStartupRoot Loading() => Root(100, main: false, loading: true);
        DesktopStartupRoot Wizard() => Root(300, main: false, wizard: true, owner: 200);
        DesktopStartupRoot Unknown() => Root(100, main: false);
        void Verify(bool condition)
        {
            if (!condition) throw new InvalidOperationException("Unexpected startup decision.");
        }
        void Refused(Action action, string reason)
        {
            try { action(); }
            catch (InvalidOperationException ex) when (ex.Message == reason) { return; }
            throw new InvalidOperationException("Expected startup refusal: " + reason);
        }
        void Case(string name, Action action)
        {
            try { action(); }
            catch (Exception ex) { throw new InvalidOperationException("Startup case failed: " + name, ex); }
            count++;
        }
        DesktopStartupObservation Observed()
        {
            var state = new DesktopStartupObservation();
            Verify(!state.Observe(5, new[] { Loading() }, true).Ready);
            Verify(state.LoadingObserved && state.LoadingAt == 5 && state.LoadingHandle == 100 &&
                state.WindowClass == firstClass);
            return state;
        }
        Case("main-first-honest-fast-route", () =>
        {
            var state = new DesktopStartupObservation();
            var decision = state.Observe(10, new[] { Root() }, false);
            Verify(decision.Ready && decision.Route == DesktopStartupObservation.UnobservedRoute &&
                decision.MainHandle == 200 && decision.WizardHandle == 0 && decision.WindowClass == firstClass &&
                !state.LoadingObserved && state.LoadingHandle == 0 && state.LoadingAt == -1);
            Verify(state.Revalidate(10, new[] { Root() }, false).Ready);
        });
        Case("original-handoff-still-requires-observation", () =>
            Verify(!DesktopPolicy.Handoff(false, -1, 10, 0, 200, true, true)));
        Case("observed-destroyed-loading-route", () =>
        {
            var state = Observed();
            var decision = state.Observe(10, new[] { Root() }, false);
            Verify(decision.Ready && decision.Route == DesktopStartupObservation.ObservedRoute);
            Verify(state.Revalidate(11, new[] { Root() }, false).Route == decision.Route);
        });
        Case("empty-sample-waits", () =>
            Verify(!new DesktopStartupObservation().Observe(0, Array.Empty<DesktopStartupRoot>(), false).Ready));
        Case("loading-without-main-waits", () =>
            Verify(!Observed().Observe(10, new[] { Loading() }, true).Ready));
        Case("unrecognized-root-without-main-waits", () =>
            Verify(!new DesktopStartupObservation().Observe(1, new[] { Unknown() }, false).Ready));
        Case("wizard-without-main-waits", () =>
            Verify(!new DesktopStartupObservation().Observe(1, new[] { Wizard() }, false).Ready));
        Case("offscreen-main-control-is-not-main", () =>
            Verify(!new DesktopStartupObservation().Observe(1, new[] { Root(main: false) }, false).Ready));
        Case("invisible-main-projection-refused", () =>
            Refused(() => new DesktopStartupObservation().Observe(1, new[] { Root(visible: false) }, false),
                "startup-root-projection"));
        foreach (bool mainFirst in new[] { false, true })
        {
            Case("loading-and-main-order-" + mainFirst, () =>
            {
                var state = new DesktopStartupObservation();
                var sample = mainFirst ? new[] { Root(), Loading() } : new[] { Loading(), Root() };
                Verify(!state.Observe(5, sample, false).Ready && state.LoadingObserved);
                Verify(state.Observe(10, new[] { Root() }, false).Route == DesktopStartupObservation.ObservedRoute);
            });
            Case("unlabeled-leftover-order-" + mainFirst, () =>
            {
                var sample = mainFirst ? new[] { Root(), Unknown() } : new[] { Unknown(), Root() };
                var state = new DesktopStartupObservation();
                Verify(!state.Observe(5, sample, false).Ready && !state.LoadingObserved);
            });
            Case("exact-main-owned-wizard-order-" + mainFirst, () =>
            {
                var state = new DesktopStartupObservation();
                var sample = mainFirst ? new[] { Root(), Wizard() } : new[] { Wizard(), Root() };
                var decision = state.Observe(5, sample, false);
                Verify(decision.Ready && decision.WizardHandle == 300 &&
                    decision.Route == DesktopStartupObservation.UnobservedRoute);
                Verify(state.Revalidate(5, sample, false).Ready);
            });
        }
        Case("loading-label-loss-is-sticky", () =>
        {
            var state = Observed();
            Verify(!state.Observe(10, new[] { Root(), Unknown() }, true).Ready);
            Verify(state.LoadingObserved && state.LoadingAt == 5);
            Verify(!state.Observe(15, new[] { Root() }, true).Ready);
            Verify(state.Observe(20, new[] { Root() }, false).Route == DesktopStartupObservation.ObservedRoute);
        });
        Case("unlabeled-retained-root-blocks-even-gone-flag", () =>
            Verify(!Observed().Observe(10, new[] { Root(), Unknown() }, false).Ready));
        Case("loading-still-exists-outside-visible-sample", () =>
            Verify(!Observed().Observe(10, new[] { Root() }, true).Ready));
        Case("named-loading-blocks-even-gone-flag", () =>
            Verify(!Observed().Observe(10, new[] { Root(), Loading() }, false).Ready));
        Case("unobserved-loading-exists-flag-is-invalid", () =>
            Refused(() => new DesktopStartupObservation().Observe(10, new[] { Root() }, true),
                "startup-loading-projection"));
        Case("unknown-root-disappearance-is-not-loading-proof", () =>
        {
            var state = new DesktopStartupObservation();
            Verify(!state.Observe(5, new[] { Unknown(), Root() }, false).Ready);
            Verify(state.Observe(10, new[] { Root() }, false).Route == DesktopStartupObservation.UnobservedRoute &&
                !state.LoadingObserved);
        });
        Case("observed-main-with-owned-wizard", () =>
            Verify(Observed().Observe(10, new[] { Root(), Wizard() }, false).Route ==
                DesktopStartupObservation.ObservedRoute));
        Case("wizard-plus-unknown-still-blocks", () =>
            Verify(!new DesktopStartupObservation().Observe(10, new[] { Root(), Wizard(), Unknown() }, false).Ready));
        foreach (long owner in new long[] { 0, 999, 300 })
            Case("wizard-wrong-immediate-owner-" + owner, () =>
                Refused(() => new DesktopStartupObservation().Observe(10,
                    new[] { Root(), Root(300, main: false, wizard: true, owner: owner) }, false),
                    "startup-setup-wizard"));
        Case("wizard-wrong-class", () =>
            Refused(() => new DesktopStartupObservation().Observe(10,
                new[] { Root(), Root(300, main: false, wizard: true, owner: 200, windowClass: otherClass) }, false),
                "startup-setup-wizard"));
        Case("wizard-missing-visible-control-is-unknown", () =>
            Verify(!new DesktopStartupObservation().Observe(10,
                new[] { Root(), Root(300, main: false, owner: 200) }, false).Ready));
        Case("duplicate-wizards", () =>
            Refused(() => new DesktopStartupObservation().Observe(10,
                new[] { Root(), Wizard(), Root(301, main: false, wizard: true, owner: 200) }, false),
                "startup-duplicate-wizard"));
        Case("wizard-reuses-main-root", () =>
            Refused(() => new DesktopStartupObservation().Observe(10,
                new[] { Root(), Root(main: false, wizard: true, owner: 200) }, false), "startup-duplicate-root"));
        Case("native-picker-not-startup-root", () =>
            Refused(() => new DesktopStartupObservation().Observe(10,
                new[] { Root(), Root(300, main: false, owner: 200, windowClass: "#32770") }, false),
                "startup-root-class"));
        Case("duplicate-root-projections", () =>
            Refused(() => new DesktopStartupObservation().Observe(10, new[] { Root(), Root() }, false),
                "startup-duplicate-root"));
        Case("duplicate-main-candidates", () =>
            Refused(() => new DesktopStartupObservation().Observe(10, new[] { Root(), Root(201) }, false),
                "startup-duplicate-main"));
        Case("duplicate-loading-candidates", () =>
            Refused(() => new DesktopStartupObservation().Observe(10,
                new[] { Loading(), Root(101, main: false, loading: true) }, true), "startup-duplicate-loading"));
        Case("ambiguous-main-loading-root", () =>
            Refused(() => new DesktopStartupObservation().Observe(10, new[] { Root(loading: true) }, false),
                "startup-root-role"));
        Case("ambiguous-main-wizard-root", () =>
            Refused(() => new DesktopStartupObservation().Observe(10, new[] { Root(wizard: true) }, false),
                "startup-root-role"));
        Case("zero-native-root", () =>
            Refused(() => new DesktopStartupObservation().Observe(10, new[] { Root(0) }, false),
                "startup-root-projection"));
        Case("null-root-projection", () =>
            Refused(() => new DesktopStartupObservation().Observe(10, new DesktopStartupRoot[] { null }, false),
                "startup-root-projection"));
        Case("null-sample", () =>
            Refused(() => new DesktopStartupObservation().Observe(10, null, false), "startup-root-bound"));
        Case("eight-roots-with-unknowns-waits", () =>
        {
            var roots = new DesktopStartupRoot[8];
            for (int i = 0; i < roots.Length; i++) roots[i] = Root(200 + i, main: i == 0);
            Verify(!new DesktopStartupObservation().Observe(10, roots, false).Ready);
        });
        Case("nine-roots-refused", () =>
        {
            var roots = new DesktopStartupRoot[9];
            for (int i = 0; i < roots.Length; i++) roots[i] = Root(200 + i, main: i == 0);
            Refused(() => new DesktopStartupObservation().Observe(10, roots, false), "startup-root-bound");
        });
        foreach (string windowClass in new[] { null, "", "Avalonia-invalid", "other" })
            Case("invalid-native-class-" + (windowClass ?? "null"), () =>
                Refused(() => new DesktopStartupObservation().Observe(10,
                    new[] { Root(windowClass: windowClass) }, false), "startup-root-class"));
        Case("observed-class-remains-pinned", () =>
            Refused(() => Observed().Observe(10, new[] { Root(windowClass: otherClass) }, false),
                "startup-class-changed"));
        Case("observed-wizard-class-remains-pinned", () =>
            Refused(() => Observed().Observe(10,
                new[] { Root(), Root(300, main: false, wizard: true, owner: 200, windowClass: otherClass) }, false),
                "startup-class-changed"));
        Case("observed-loading-handle-cannot-change", () =>
            Refused(() => Observed().Observe(10, new[] { Root(101, main: false, loading: true), Root() }, false),
                "startup-loading-changed"));
        Case("main-cannot-reuse-observed-loading-handle", () =>
            Refused(() => Observed().Observe(10, new[] { Root(100) }, false), "startup-handoff-refused"));
        Case("negative-initial-observation", () =>
            Refused(() => new DesktopStartupObservation().Observe(-1, new[] { Root() }, false),
                "startup-observation-order"));
        foreach (long at in new long[] { 4, 5 })
        {
            Case("observed-unchanged-or-backward-" + at, () =>
                Refused(() => Observed().Observe(at, new[] { Root() }, false), "startup-observation-order"));
            Case("empty-unchanged-or-backward-" + at, () =>
            {
                var state = new DesktopStartupObservation();
                state.Observe(5, Array.Empty<DesktopStartupRoot>(), false);
                Refused(() => state.Observe(at, new[] { Root() }, false), "startup-observation-order");
            });
        }
        Case("revalidation-requires-candidate", () =>
            Refused(() => new DesktopStartupObservation().Revalidate(10, new[] { Root() }, false),
                "startup-acceptance-candidate"));
        Case("observed-waiting-cannot-bypass-candidate", () =>
            Refused(() => Observed().Revalidate(10, new[] { Root() }, false), "startup-acceptance-candidate"));
        Case("revalidation-clock-cannot-go-backward", () =>
        {
            var state = new DesktopStartupObservation();
            state.Observe(10, new[] { Root() }, false);
            Refused(() => state.Revalidate(9, new[] { Root() }, false), "startup-observation-order");
        });
        Case("acceptance-main-root-must-remain-identical", () =>
        {
            var state = new DesktopStartupObservation();
            state.Observe(10, new[] { Root() }, false);
            Refused(() => state.Revalidate(11, new[] { Root(201) }, false), "startup-acceptance-changed");
        });
        Case("acceptance-class-must-remain-identical", () =>
        {
            var state = new DesktopStartupObservation();
            state.Observe(10, new[] { Root() }, false);
            Refused(() => state.Revalidate(11, new[] { Root(windowClass: otherClass) }, false),
                "startup-class-changed");
        });
        Case("acceptance-missing-main-blocks", () =>
        {
            var state = new DesktopStartupObservation();
            state.Observe(10, new[] { Root() }, false);
            Verify(!state.Revalidate(11, Array.Empty<DesktopStartupRoot>(), false).Ready);
        });
        Case("acceptance-offscreen-main-control-blocks", () =>
        {
            var state = new DesktopStartupObservation();
            state.Observe(10, new[] { Root() }, false);
            Verify(!state.Revalidate(11, new[] { Root(main: false) }, false).Ready);
        });
        Case("acceptance-hidden-native-main-refused", () =>
        {
            var state = new DesktopStartupObservation();
            state.Observe(10, new[] { Root() }, false);
            Refused(() => state.Revalidate(11, new[] { Root(visible: false) }, false),
                "startup-root-projection");
        });
        foreach (bool mainFirst in new[] { false, true })
            Case("acceptance-unlabeled-leftover-order-" + mainFirst, () =>
            {
                var state = new DesktopStartupObservation();
                state.Observe(10, new[] { Root() }, false);
                var roots = mainFirst ? new[] { Root(), Unknown() } : new[] { Unknown(), Root() };
                Verify(!state.Revalidate(11, roots, false).Ready && !state.LoadingObserved);
            });
        Case("late-loading-makes-observation-sticky", () =>
        {
            var state = new DesktopStartupObservation();
            state.Observe(10, new[] { Root() }, false);
            Verify(!state.Revalidate(10, new[] { Root(), Loading() }, false).Ready);
            Verify(state.LoadingObserved && state.LoadingAt == 10);
            Verify(!state.Observe(15, new[] { Root(), Unknown() }, true).Ready);
            Verify(state.Observe(20, new[] { Root() }, false).Route == DesktopStartupObservation.ObservedRoute);
        });
        Case("acceptance-loading-still-exists-blocks", () =>
        {
            var state = Observed();
            state.Observe(10, new[] { Root() }, false);
            Verify(!state.Revalidate(11, new[] { Root() }, true).Ready);
        });
        Case("acceptance-duplicate-main-refused", () =>
        {
            var state = new DesktopStartupObservation();
            state.Observe(10, new[] { Root() }, false);
            Refused(() => state.Revalidate(11, new[] { Root(), Root(201) }, false), "startup-duplicate-main");
        });
        Case("acceptance-wizard-owner-rechecked", () =>
        {
            var state = new DesktopStartupObservation();
            state.Observe(10, new[] { Root(), Wizard() }, false);
            Refused(() => state.Revalidate(11,
                new[] { Root(), Root(300, main: false, wizard: true, owner: 999) }, false),
                "startup-setup-wizard");
        });
        Case("acceptance-wizard-control-rechecked", () =>
        {
            var state = new DesktopStartupObservation();
            state.Observe(10, new[] { Root(), Wizard() }, false);
            Verify(!state.Revalidate(11, new[] { Root(), Root(300, main: false, owner: 200) }, false).Ready);
        });
        Case("acceptance-wizard-may-have-closed", () =>
        {
            var state = new DesktopStartupObservation();
            state.Observe(10, new[] { Root(), Wizard() }, false);
            var accepted = state.Revalidate(11, new[] { Root() }, false);
            Verify(accepted.Ready && accepted.WizardHandle == 0);
        });
        Case("acceptance-exact-wizard-may-have-appeared", () =>
        {
            var state = new DesktopStartupObservation();
            state.Observe(10, new[] { Root() }, false);
            Verify(state.Revalidate(11, new[] { Root(), Wizard() }, false).WizardHandle == 300);
        });
        foreach (string rejection in new[] { "chronology", "loading-handle", "class", "reused-main" })
            Case("invalid-observed-handoff-never-falls-back-" + rejection, () =>
            {
                var state = Observed();
                string reason;
                Action reject;
                switch (rejection)
                {
                    case "chronology":
                        reason = "startup-observation-order";
                        reject = () => state.Observe(5, new[] { Root() }, false);
                        break;
                    case "loading-handle":
                        reason = "startup-loading-changed";
                        reject = () => state.Observe(10, new[] { Root(101, main: false, loading: true) }, false);
                        break;
                    case "class":
                        reason = "startup-class-changed";
                        reject = () => state.Observe(10, new[] { Root(windowClass: otherClass) }, false);
                        break;
                    default:
                        reason = "startup-handoff-refused";
                        reject = () => state.Observe(10, new[] { Root(100) }, false);
                        break;
                }
                Refused(reject, reason);
                Refused(() => state.Observe(20, new[] { Root() }, false), reason);
                Verify(state.LoadingObserved);
            });
        return count;
    }

    public static int RunSnapshotTests(string parent)
    {
        int count = 0, guards = 0;
        void Verify(bool condition)
        {
            count++;
            if (!condition) throw new InvalidOperationException("Installed snapshot case failed: " + count);
        }
        void Refused(Action action, string expected)
        {
            bool refused = false;
            try { action(); }
            catch (InvalidOperationException ex) when (ex.Message == expected) { refused = true; }
            Verify(refused);
        }
        string root = Path.Combine(parent, "snapshot-" + Guid.NewGuid().ToString("N"));
        string proof = Path.Combine(root, "proof");
        string descriptor = Path.Combine(proof, "PATCH_offline.txt");
        string payload = Path.Combine(proof, "payload.bin");
        string marker = Path.Combine(root, ".febuilder-patch-import.json");
        const string owner = "FEBuilderGBA.PatchDatabaseImport";
        const string id = "abcdef0123456789abcdef0123456789";
        string valid = owner + "\n" + id + "\nFE8U\n";
        string Hash(string path)
        {
            using var stream = File.OpenRead(path);
            return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        }
        string Capture() => InstalledDatabaseSnapshot.Capture(root, () => guards++, Hash);
        void Marker(string text) => File.WriteAllBytes(marker, Encoding.ASCII.GetBytes(text));
        Directory.CreateDirectory(proof);
        try
        {
            File.WriteAllText(descriptor, "NAME=Owned proof", new UTF8Encoding(false));
            File.WriteAllBytes(payload, new byte[] { 1 });
            Marker(valid);
            string before = Capture();
            Verify(DesktopPolicy.Sha256(before));
            Verify(Capture() == before);
            File.Delete(marker);
            Refused(() => Capture(), "installed-tree-shape");
            foreach (string invalidMarker in new[] { "", "X" + valid.Substring(1),
                valid.Replace("FE8U", "FE7U"), valid.Replace(id, id.ToUpperInvariant()),
                valid.Replace(id, new string('g', 32)), valid.TrimEnd('\n'),
                valid + "x", valid.Replace("\n", "\r\n") })
            {
                Marker(invalidMarker);
                Refused(() => Capture(), "installed-marker");
            }
            byte[] nonAscii = Encoding.ASCII.GetBytes(valid);
            nonAscii[0] = 0x80;
            File.WriteAllBytes(marker, nonAscii);
            Refused(() => Capture(), "installed-marker");
            Marker(valid.Replace(id, new string('1', 32)));
            string changed = Capture();
            Verify(changed != before);
            string[] original = { before, before, before, before };
            string[] altered = { before, before, before, changed };
            Verify(!DesktopPolicy.Preserved(original, altered, "row", "row", true));
            Marker(valid);
            Verify(Capture() == before);
            Verify(DesktopPolicy.Preserved(original, original, "row", "row", true));
            string extra = Path.Combine(root, "extra.txt");
            File.WriteAllText(extra, "unexpected");
            Refused(() => Capture(), "unexpected-installed-file");
            File.Delete(extra);
            string nestedMarker = Path.Combine(proof, ".febuilder-patch-import.json");
            File.Copy(marker, nestedMarker);
            Refused(() => Capture(), "unexpected-installed-file");
            File.Delete(nestedMarker);
            File.Delete(marker);
            Directory.CreateDirectory(marker);
            Refused(() => Capture(), "unexpected-installed-directory");
            Directory.Delete(marker);
            Marker(valid);
            File.Delete(payload);
            Refused(() => Capture(), "installed-tree-shape");
            File.WriteAllBytes(payload, new byte[] { 1 });
            string unexpectedDirectory = Path.Combine(root, "other");
            Directory.CreateDirectory(unexpectedDirectory);
            Refused(() => Capture(), "unexpected-installed-directory");
            Directory.Delete(unexpectedDirectory);
            using (var stream = new FileStream(payload, FileMode.Open, FileAccess.Write))
                stream.SetLength(16777216);
            Verify(DesktopPolicy.Sha256(Capture()));
            using (var stream = new FileStream(payload, FileMode.Open, FileAccess.Write))
                stream.SetLength(16777217);
            Refused(() => Capture(), "hash-size-bound");
            File.WriteAllBytes(payload, new byte[] { 1 });
            for (int i = 0; i < 17; i++) File.WriteAllText(Path.Combine(root, "extra-" + i), "x");
            Refused(() => Capture(), "unexpected-installed-file");
            for (int i = 0; i < 17; i++) File.Delete(Path.Combine(root, "extra-" + i));
            File.Delete(descriptor);
            Refused(() => Capture(), "installed-tree-shape");
            Verify(guards > 0);
            return count;
        }
        finally { Directory.Delete(root, true); }
    }

    static int cases;
    static void Check(bool condition)
    {
        cases++;
        if (!condition) throw new InvalidOperationException("Pure decision case failed: " + cases);
    }

    public static int Run()
    {
        cases = 0;
        foreach (int session in new[] { -1, 0, 2 })
        foreach (int state in new[] { -1, 0, 1, 4, 5, 9, 10 })
        foreach (bool interactive in new[] { false, true })
        foreach (bool input in new[] { false, true })
        foreach (bool foreground in new[] { false, true })
            Check(BoundedWindowsReadiness.Decide(session, state, interactive, input, foreground) ==
                (session > 0 && state == 0 && interactive && input && foreground));

        const string exe = @"C:\owned\app\FEBuilderGBA.Avalonia.exe";
        Check(DesktopPolicy.Process(42, 123, exe, 42, 123, exe, false));
        Check(!DesktopPolicy.Process(42, 123, exe, 41, 123, exe, false));
        Check(!DesktopPolicy.Process(42, 123, exe, 42, 124, exe, false));
        Check(!DesktopPolicy.Process(42, 123, exe, 42, 123, exe + ".other", false));
        Check(!DesktopPolicy.Process(42, 123, exe, 42, 123, exe, true));
        Check(!DesktopPolicy.Process(0, 123, exe, 0, 123, exe, false));
        Check(!DesktopPolicy.Process(42, 0, exe, 42, 0, exe, false));
        Check(!DesktopPolicy.Process(42, 123, "", 42, 123, "", false));
        Check(DesktopPolicy.AvaloniaClass("Avalonia-1cbfe925-1340-4151-890b-03b818cd779f"));
        foreach (string name in new[] { "", "Avalonia-", "Other-1cbfe925-1340-4151-890b-03b818cd779f", "#32770" })
            Check(!DesktopPolicy.AvaloniaClass(name));

        Check(DesktopPolicy.Window(42, 42, 42, 100, 100, "expected", "expected", true, true));
        Check(!DesktopPolicy.Window(42, 41, 42, 100, 100, "expected", "expected", true, true));
        Check(!DesktopPolicy.Window(42, 42, 41, 100, 100, "expected", "expected", true, true));
        Check(!DesktopPolicy.Window(42, 42, 42, 0, 0, "expected", "expected", true, true));
        Check(!DesktopPolicy.Window(42, 42, 42, 100, 101, "expected", "expected", true, true));
        Check(!DesktopPolicy.Window(42, 42, 42, 100, 100, "other", "expected", true, true));
        Check(!DesktopPolicy.Window(42, 42, 42, 100, 100, "expected", "expected", false, true));
        Check(!DesktopPolicy.Window(42, 42, 42, 100, 100, "expected", "expected", true, false));
        Check(DesktopPolicy.Picker(200, 200, 200, true, true, true, "Edit", "Button", 1));
        Check(!DesktopPolicy.Picker(200, 201, 200, true, true, true, "Edit", "Button", 1));
        Check(!DesktopPolicy.Picker(200, 200, 201, true, true, true, "Edit", "Button", 1));
        Check(!DesktopPolicy.Picker(200, 200, 200, false, true, true, "Edit", "Button", 1));
        Check(!DesktopPolicy.Picker(200, 200, 200, true, false, true, "Edit", "Button", 1));
        Check(!DesktopPolicy.Picker(200, 200, 200, true, true, false, "Edit", "Button", 1));
        Check(!DesktopPolicy.Picker(200, 200, 200, true, true, true, "Other", "Button", 1));
        Check(!DesktopPolicy.Picker(200, 200, 200, true, true, true, "Edit", "Other", 1));
        Check(!DesktopPolicy.Picker(200, 200, 200, true, true, true, "Edit", "Button", 2));
        Check(DesktopPolicy.Handoff(true, 5, 10, 100, 200, true, true));
        Check(!DesktopPolicy.Handoff(false, 5, 10, 100, 200, true, true));
        Check(!DesktopPolicy.Handoff(true, 10, 5, 100, 200, true, true));
        Check(!DesktopPolicy.Handoff(true, 5, 5, 100, 200, true, true));
        Check(!DesktopPolicy.Handoff(true, 5, 10, 100, 100, true, true));
        Check(!DesktopPolicy.Handoff(true, 5, 10, 100, 200, false, true));
        Check(!DesktopPolicy.Handoff(true, 5, 10, 100, 200, true, false));

        string hash = new string('a', 64);
        string other = new string('b', 64);
        string[] before = { hash, hash, hash, hash };
        Check(DesktopPolicy.Preserved(before, (string[])before.Clone(), "row", "row", true));
        for (int i = 0; i < before.Length; i++)
        {
            var after = (string[])before.Clone();
            after[i] = other;
            Check(!DesktopPolicy.Preserved(before, after, "row", "row", true));
        }
        Check(!DesktopPolicy.Preserved(before, before, "row", "changed", true));
        Check(!DesktopPolicy.Preserved(before, before, "row", "row", false));
        Check(!DesktopPolicy.Preserved(new[] { "" }, new[] { "" }, "row", "row", true));
        Check(!DesktopPolicy.Preserved(before, null, "row", "row", true));
        Check(!DesktopPolicy.Expired(99, 100));
        Check(DesktopPolicy.Expired(100, 100));
        Check(DesktopPolicy.Expired(101, 100));
        Check(DesktopPolicy.Expired(-1, 100));
        Check(DesktopPolicy.Expired(0, 0));
        // Exercise the actual dispatch gate, including a deschedule after the last check.
        var beforeAdmission = new DesktopDispatchGate();
        Check(!beforeAdmission.IsClosed);
        beforeAdmission.Close();
        Check(!beforeAdmission.TryBegin());
        Check(!beforeAdmission.CanReturn(false));
        Check(beforeAdmission.CanReturn(true));

        var admitted = new DesktopDispatchGate();
        Check(admitted.TryBegin());
        Check(!admitted.TryBegin());
        Check(!admitted.IsClosed); // The worker can deschedule here, before its native call.
        admitted.Close();
        Check(!admitted.TryBegin());
        Check(!admitted.CanReturn(false));
        Check(!admitted.CanReturn(true)); // A closed gate does not abort an admitted call.
        admitted.End();
        Check(!admitted.TryBegin());
        Check(!admitted.CanReturn(false)); // Lease release is not a thread-join receipt.
        Check(admitted.CanReturn(true));
        admitted.Close();
        Check(!admitted.TryBegin());

        var completedBeforeCancel = new DesktopDispatchGate();
        Check(completedBeforeCancel.TryBegin());
        completedBeforeCancel.End();
        Check(completedBeforeCancel.TryBegin());
        completedBeforeCancel.End();
        Check(!completedBeforeCancel.CanReturn(true));
        completedBeforeCancel.Close();
        Check(completedBeforeCancel.CanReturn(true));
        Check(!completedBeforeCancel.TryBegin());
        bool unmatchedEndRefused = false;
        try { completedBeforeCancel.End(); }
        catch (InvalidOperationException) { unmatchedEndRefused = true; }
        Check(unmatchedEndRefused);
        for (int mask = 0; mask < 8; mask++)
        {
            bool boundaryExited = (mask & 1) != 0, retainedIdentity = (mask & 2) != 0;
            bool killAttempted = (mask & 4) != 0;
            Check(DesktopPolicy.MayCleanupApp(boundaryExited, retainedIdentity, killAttempted) ==
                (mask == 3));
        }
        for (int mask = 0; mask < 256; mask++)
        {
            bool requested = (mask & 1) != 0, editorOpen = (mask & 2) != 0;
            bool mainGone = (mask & 4) != 0, editorGone = (mask & 8) != 0;
            bool exited = (mask & 16) != 0, zero = (mask & 32) != 0;
            bool killed = (mask & 64) != 0, timedOut = (mask & 128) != 0;
            Check(DesktopPolicy.NormalClose(requested, editorOpen, mainGone, editorGone, exited,
                zero ? 0 : 1, killed, timedOut) == (mask == 63));
        }
        Check(DesktopPolicy.RelativeFile(@"config\patch2\FE8U\proof\PATCH_offline.txt"));
        foreach (string bad in new[] { "", ".", "..", @"..\escape", @"C:\outside", @"\absolute",
            "a/b", @"a\..\b", @"a\.\b", @"a\\b", "a:stream", "a.", "a ", @"a\NUL", "CON.txt",
            "CONIN$", "CONOUT$", "NUL .txt" })
            Check(!DesktopPolicy.RelativeFile(bad));
        return cases;
    }
}
