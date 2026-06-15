// SPDX-License-Identifier: GPL-3.0-or-later
// #1122 — pure navigation-stack core tests. No Avalonia / windowing system
// needed: NavigationStack<TPage> is Avalonia-free, so these run as plain xUnit
// facts on every CI platform. They cover push/pop/back, modal-overlay
// precedence, the pick/modal result-await primitive (resolve, cancel-to-null,
// double-completion no-op), and CloseAll cancelling pending results.
using System.Threading.Tasks;
using FEBuilderGBA.Avalonia.Services;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests;

public class NavigationStackTests
{
    static NavigationStack<string> NewStackWithRoot(string root = "root")
    {
        var s = new NavigationStack<string>();
        s.Reset(root);
        return s;
    }

    [Fact]
    public void Reset_seeds_single_root_page()
    {
        var s = NewStackWithRoot("home");
        Assert.Equal(1, s.PageCount);
        Assert.Equal(0, s.ModalCount);
        Assert.Equal("home", s.CurrentTop!.Page);
        Assert.False(s.CanGoBack);
    }

    [Fact]
    public void Push_adds_page_and_updates_top()
    {
        var s = NewStackWithRoot();
        s.Push("editor");
        Assert.Equal(2, s.PageCount);
        Assert.Equal("editor", s.CurrentTop!.Page);
        Assert.True(s.CanGoBack);
    }

    [Fact]
    public void Back_pops_pages_but_never_below_root()
    {
        var s = NewStackWithRoot("root");
        s.Push("a");
        s.Push("b");
        Assert.True(s.Back());
        Assert.Equal("a", s.CurrentTop!.Page);
        Assert.True(s.Back());
        Assert.Equal("root", s.CurrentTop!.Page);
        // Root is never popped by Back.
        Assert.False(s.CanGoBack);
        Assert.False(s.Back());
        Assert.Equal("root", s.CurrentTop!.Page);
    }

    [Fact]
    public void Modal_overlay_sits_above_normal_pages()
    {
        var s = NewStackWithRoot();
        s.Push("page");
        s.PushModal("dialog");
        Assert.Equal("dialog", s.CurrentTop!.Page);
        Assert.True(s.CurrentTop!.IsModal);
        Assert.Equal(1, s.ModalCount);
        // Back dismisses the modal first, leaving the page.
        Assert.True(s.Back());
        Assert.Equal(0, s.ModalCount);
        Assert.Equal("page", s.CurrentTop!.Page);
    }

    [Fact]
    public void StackChanged_fires_on_mutations()
    {
        var s = new NavigationStack<string>();
        int count = 0;
        s.StackChanged += () => count++;
        s.Reset("r");      // 1
        s.Push("a");       // 2
        s.PushModal("m");  // 3
        s.Pop();           // 4
        Assert.Equal(4, count);
    }

    [Fact]
    public async Task PushForResult_resolves_on_CompleteTop()
    {
        var s = NewStackWithRoot();
        var (_, task) = s.PushForResult<int>("pick");
        Assert.False(task.IsCompleted);
        bool completed = s.CompleteTop(42);
        Assert.True(completed);
        Assert.Equal(42, await task);
    }

    [Fact]
    public async Task PushForResult_cancels_to_null_on_pop()
    {
        var s = NewStackWithRoot();
        var (_, task) = s.PushForResult<string>("pick");
        s.Pop(); // back without selecting
        Assert.Null(await task);
    }

    [Fact]
    public async Task PushForResult_cancels_to_null_on_back()
    {
        var s = NewStackWithRoot();
        var (_, task) = s.PushForResult<string>("pick");
        Assert.True(s.Back());
        Assert.Null(await task);
    }

    [Fact]
    public async Task CompleteTop_is_idempotent_double_completion_is_noop()
    {
        var s = NewStackWithRoot();
        var (_, task) = s.PushForResult<int>("pick");
        Assert.True(s.CompleteTop(7));
        // Second completion (e.g. the view also closing) must NOT throw or
        // change the resolved value — the task already holds 7.
        s.CompleteTop(99);
        Assert.Equal(7, await task);
    }

    [Fact]
    public async Task Pop_after_completion_does_not_overwrite_result()
    {
        var s = NewStackWithRoot();
        var (entry, task) = s.PushForResult<int>("pick");
        s.CompleteEntry(entry, 5);
        s.Pop(); // host pops after confirm; must keep the 5, not null it.
        Assert.Equal(5, await task);
    }

    [Fact]
    public async Task ClearToRoot_cancels_all_pending_results_to_null()
    {
        var s = NewStackWithRoot();
        var (_, t1) = s.PushForResult<string>("pick1", asModal: false);
        var (_, t2) = s.PushForResult<string>("pick2", asModal: true);
        s.ClearToRoot();
        Assert.Null(await t1);
        Assert.Null(await t2);
        Assert.Equal(1, s.PageCount);
        Assert.Equal(0, s.ModalCount);
    }

    [Fact]
    public async Task Reset_cancels_all_pending_results_to_null()
    {
        var s = NewStackWithRoot();
        var (_, t1) = s.PushForResult<string>("a");
        var (_, t2) = s.PushForResult<string>("b");
        s.Reset("newroot");
        Assert.Null(await t1);
        Assert.Null(await t2);
        Assert.Equal("newroot", s.CurrentTop!.Page);
    }

    [Fact]
    public void CompleteEntry_noop_when_entry_not_on_stack()
    {
        var s = NewStackWithRoot();
        var (entry, _) = s.PushForResult<int>("pick");
        s.Pop(); // removes entry (and cancels it)
        // Completing a removed entry is a no-op (returns false).
        Assert.False(s.CompleteEntry(entry, 1));
    }

    [Fact]
    public void Empty_stack_guards()
    {
        var s = new NavigationStack<string>(); // never Reset
        Assert.Null(s.CurrentTop);
        Assert.Equal(0, s.Count);
        Assert.False(s.CanGoBack);
        Assert.False(s.Back());
        Assert.Null(s.Pop());
        Assert.False(s.CompleteTop(1));
    }

    [Fact]
    public void MoveToTop_surfaces_existing_page_without_duplicating()
    {
        var s = NewStackWithRoot("root");
        s.Push("a");
        s.Push("b");
        Assert.Equal(3, s.PageCount);
        // Surface "a" without adding a 4th entry.
        s.MoveToTop("a");
        Assert.Equal(3, s.PageCount);
        Assert.Equal("a", s.CurrentTop!.Page);
        // Order is now root, b, a — one back reveals b, not a duplicate a.
        s.Back();
        Assert.Equal("b", s.CurrentTop!.Page);
    }

    [Fact]
    public void MoveToTop_falls_back_to_push_when_absent()
    {
        var s = NewStackWithRoot("root");
        var entry = s.MoveToTop("new");
        Assert.Equal(2, s.PageCount);
        Assert.Equal("new", entry.Page);
        Assert.Equal("new", s.CurrentTop!.Page);
    }

    [Fact]
    public void MoveToTop_is_noop_when_already_top()
    {
        var s = NewStackWithRoot("root");
        s.Push("a");
        int changes = 0;
        s.StackChanged += () => changes++;
        s.MoveToTop("a"); // already top — no mutation, no event
        Assert.Equal(0, changes);
        Assert.Equal(2, s.PageCount);
    }

    [Fact]
    public void Nested_modals_pop_in_lifo_order()
    {
        var s = NewStackWithRoot();
        s.PushModal("m1");
        s.PushModal("m2");
        Assert.Equal(2, s.ModalCount);
        Assert.Equal("m2", s.CurrentTop!.Page);
        s.Pop();
        Assert.Equal("m1", s.CurrentTop!.Page);
        s.Pop();
        Assert.Equal(0, s.ModalCount);
        Assert.Equal("root", s.CurrentTop!.Page);
    }
}
