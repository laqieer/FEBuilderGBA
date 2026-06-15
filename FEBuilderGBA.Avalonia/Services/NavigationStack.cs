// SPDX-License-Identifier: GPL-3.0-or-later
// #1122 — Android single-activity navigation model.
//
// Pure, Avalonia-free, desktop-unit-testable navigation-stack core. This is the
// testable heart of the Android single-view nav host: push/pop/back plus a
// modal-overlay sub-stack and a pick-result await primitive.
//
// It carries NO reference to Avalonia types (Window/Control) on purpose — the
// AndroidNavigationService composes this with the actual UI content. Keeping it
// pure means the back/modal/pick-result logic runs in a headless desktop unit
// test (no windowing system, works in CI on every platform).
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace FEBuilderGBA.Avalonia.Services
{
    /// <summary>
    /// A pending pick/modal result entry on the stack — the page plus the
    /// optional <see cref="TaskCompletionSource{TResult}"/> that a
    /// <see cref="NavigationStack{TPage}.PushForResult{TResult}"/> caller awaits.
    /// The TCS is boxed as <see cref="object"/> so the stack stays non-generic in
    /// its result type (different pushes may await different result types).
    /// </summary>
    /// <typeparam name="TPage">The page payload type (a UI content control on Android, a fake in tests).</typeparam>
    public sealed class NavigationEntry<TPage>
    {
        public TPage Page { get; }

        /// <summary>True when this entry sits in the modal-overlay sub-stack.</summary>
        public bool IsModal { get; }

        /// <summary>
        /// Boxed completion source for a result-awaiting push (else null).
        /// Resolving it (or cancelling to default) is funnelled through
        /// <see cref="ResultCompleter"/> so double-completion is a no-op.
        /// </summary>
        internal Action<object?>? ResultCompleter { get; }

        internal NavigationEntry(TPage page, bool isModal, Action<object?>? resultCompleter)
        {
            Page = page;
            IsModal = isModal;
            ResultCompleter = resultCompleter;
        }
    }

    /// <summary>
    /// Pure navigation stack with a normal page stack and a modal-overlay
    /// sub-stack. Modal overlays always sit ABOVE normal pages
    /// (<see cref="CurrentTop"/> returns the top modal if any, else the top
    /// page). Result-awaiting pushes resolve on explicit completion, and are
    /// cancelled to <c>default</c> (null) on pop/back/clear.
    ///
    /// Thread-affinity note: this type is NOT internally synchronized — it is
    /// meant to be driven from the single UI thread (the same place every
    /// WindowManager call already runs). The result TCS uses
    /// <see cref="TaskCreationOptions.RunContinuationsAsynchronously"/> so an
    /// awaiting continuation never runs inline inside a selection handler.
    /// </summary>
    /// <typeparam name="TPage">The page payload type.</typeparam>
    public sealed class NavigationStack<TPage>
    {
        readonly List<NavigationEntry<TPage>> _pages = new();
        readonly List<NavigationEntry<TPage>> _modals = new();

        /// <summary>Raised after ANY mutation (push/pop/back/clear) so a host view can re-render.</summary>
        public event Action? StackChanged;

        /// <summary>Total number of entries (normal pages + modal overlays).</summary>
        public int Count => _pages.Count + _modals.Count;

        /// <summary>Number of normal (non-modal) pages.</summary>
        public int PageCount => _pages.Count;

        /// <summary>Number of modal overlays currently shown.</summary>
        public int ModalCount => _modals.Count;

        /// <summary>
        /// True when a <see cref="Back"/> would do something — i.e. there is a
        /// modal overlay to dismiss, or more than one normal page (the root
        /// page is never popped by Back).
        /// </summary>
        public bool CanGoBack => _modals.Count > 0 || _pages.Count > 1;

        /// <summary>
        /// The currently visible entry: the top modal overlay if any, otherwise
        /// the top normal page. Null when the stack is empty.
        /// </summary>
        public NavigationEntry<TPage>? CurrentTop =>
            _modals.Count > 0 ? _modals[^1] :
            _pages.Count > 0 ? _pages[^1] : null;

        /// <summary>Snapshot of all entries, root-first (pages then modals).</summary>
        public IReadOnlyList<NavigationEntry<TPage>> Entries =>
            _pages.Concat(_modals).ToList();

        /// <summary>Push a normal page onto the page stack.</summary>
        public NavigationEntry<TPage> Push(TPage page)
        {
            var entry = new NavigationEntry<TPage>(page, isModal: false, resultCompleter: null);
            _pages.Add(entry);
            StackChanged?.Invoke();
            return entry;
        }

        /// <summary>
        /// Surface an already-present normal page to the TOP of the page stack
        /// WITHOUT duplicating it (re-activating a singleton). Moves the existing
        /// entry to the end of the page list; if the page is not present, falls
        /// back to <see cref="Push"/>. Modal overlays are untouched (they stay
        /// above pages). Returns the surfaced entry.
        /// </summary>
        public NavigationEntry<TPage> MoveToTop(TPage page)
        {
            int idx = _pages.FindIndex(e => EqualityComparer<TPage>.Default.Equals(e.Page, page));
            if (idx < 0)
                return Push(page);

            var entry = _pages[idx];
            if (idx != _pages.Count - 1)
            {
                _pages.RemoveAt(idx);
                _pages.Add(entry);
                StackChanged?.Invoke();
            }
            return entry;
        }

        /// <summary>Push a modal overlay above all normal pages.</summary>
        public NavigationEntry<TPage> PushModal(TPage page)
        {
            var entry = new NavigationEntry<TPage>(page, isModal: true, resultCompleter: null);
            _modals.Add(entry);
            StackChanged?.Invoke();
            return entry;
        }

        /// <summary>
        /// Push a page that the caller awaits a result from (pick / modal-with-
        /// return). Returns a task resolved by <see cref="CompleteTop{TResult}"/>
        /// (or whatever entry the returned <see cref="NavigationEntry{TPage}"/>
        /// is) and cancelled to <c>default</c> (null) when the entry is popped,
        /// dismissed by Back, or cleared.
        /// </summary>
        /// <param name="page">The page payload (the pick/modal view content).</param>
        /// <param name="asModal">When true, push onto the modal-overlay sub-stack; else a normal page.</param>
        public (NavigationEntry<TPage> Entry, Task<TResult?> Result) PushForResult<TResult>(TPage page, bool asModal = true)
        {
            // RunContinuationsAsynchronously: a SelectionConfirmed handler that
            // calls CompleteTop must NOT have the awaiting continuation execute
            // inline on its stack (that could re-enter navigation mid-handler).
            var tcs = new TaskCompletionSource<TResult?>(TaskCreationOptions.RunContinuationsAsynchronously);
            bool completed = false;

            // Idempotent completer: first call wins, the rest are no-ops. This
            // covers "selection confirmed AND THEN window also closed" races and
            // double pops.
            void Completer(object? result)
            {
                if (completed) return;
                completed = true;
                tcs.TrySetResult(result is TResult typed ? typed : default);
            }

            var entry = new NavigationEntry<TPage>(page, isModal: asModal, resultCompleter: Completer);
            if (asModal) _modals.Add(entry); else _pages.Add(entry);
            StackChanged?.Invoke();
            return (entry, tcs.Task);
        }

        /// <summary>
        /// Resolve the result of the topmost result-awaiting entry. No-op if the
        /// top entry has no pending result. Does NOT pop the entry — the caller
        /// typically pops afterwards (or the host does on confirm). Returns true
        /// if a result completer was invoked.
        /// </summary>
        public bool CompleteTop<TResult>(TResult result)
        {
            var top = CurrentTop;
            if (top?.ResultCompleter == null) return false;
            top.ResultCompleter(result);
            return true;
        }

        /// <summary>
        /// Resolve a specific entry's result (used when a pick view confirms
        /// while it is not necessarily the visual top, e.g. a follow-on dialog
        /// was layered). No-op if the entry has no pending result or is not on
        /// the stack. Returns true if a completer was invoked.
        /// </summary>
        public bool CompleteEntry<TResult>(NavigationEntry<TPage> entry, TResult result)
        {
            if (entry?.ResultCompleter == null) return false;
            if (!_pages.Contains(entry) && !_modals.Contains(entry)) return false;
            entry.ResultCompleter(result);
            return true;
        }

        /// <summary>
        /// Pop the topmost entry (modal first, else page). The root page is
        /// never popped (a single normal page stays). Any pending result on the
        /// popped entry is cancelled to <c>default</c> (null). Returns the popped
        /// entry, or null when nothing was popped.
        /// </summary>
        public NavigationEntry<TPage>? Pop()
        {
            NavigationEntry<TPage>? popped = null;
            if (_modals.Count > 0)
            {
                popped = _modals[^1];
                _modals.RemoveAt(_modals.Count - 1);
            }
            else if (_pages.Count > 1)
            {
                popped = _pages[^1];
                _pages.RemoveAt(_pages.Count - 1);
            }

            if (popped != null)
            {
                // Cancel a pending awaited result (back-without-selecting => null).
                popped.ResultCompleter?.Invoke(null);
                StackChanged?.Invoke();
            }
            return popped;
        }

        /// <summary>
        /// Hardware/back-button semantics: dismiss the top modal if any, else
        /// pop one normal page (never below the root). Identical to
        /// <see cref="Pop"/> here, named separately so call sites read as
        /// "Back" at the host level. Returns true if it consumed the back.
        /// </summary>
        public bool Back() => Pop() != null;

        /// <summary>
        /// Clear everything to a single root page. All pending results are
        /// cancelled to <c>default</c> (null). Used by CloseAll — every awaiting
        /// pick/modal resolves null so no caller hangs.
        /// </summary>
        public void Reset(TPage root)
        {
            CancelAllPending();
            _modals.Clear();
            _pages.Clear();
            _pages.Add(new NavigationEntry<TPage>(root, isModal: false, resultCompleter: null));
            StackChanged?.Invoke();
        }

        /// <summary>
        /// Pop every entry except the root page (CloseAll without re-seeding the
        /// root). All pending results are cancelled to null. Keeps the existing
        /// root page entry as-is.
        /// </summary>
        public void ClearToRoot()
        {
            CancelAllPending();
            _modals.Clear();
            if (_pages.Count > 1)
                _pages.RemoveRange(1, _pages.Count - 1);
            StackChanged?.Invoke();
        }

        void CancelAllPending()
        {
            foreach (var e in _modals) e.ResultCompleter?.Invoke(null);
            foreach (var e in _pages) e.ResultCompleter?.Invoke(null);
        }
    }
}
