// SPDX-License-Identifier: GPL-3.0-or-later
// WrappingNumericUpDown (#1985): a NumericUpDown variant used ONLY by the
// Portrait Import Wizard's Frame selector (ImagePortraitImporterView's
// FrameInput, Minimum 0 / Maximum 10) so spinning past either bound wraps to
// the other end (e.g. 10 -> 0 on increase, 0 -> 10 on decrease) instead of
// clamping and disabling the spin button.
//
// Avalonia 11.2.3's NumericUpDown keeps both its "Spinner" template-part
// property and its SetValidSpinDirection() maintenance method PRIVATE
// (confirmed via reflection against Avalonia.Controls.dll 11.2.3 and the
// upstream source), so a derived class cannot reach either directly. This
// control instead:
//   - Overrides OnApplyTemplate to reacquire "PART_Spinner" itself into an
//     independent, read-only field. No event is subscribed on it (all spin
//     interception happens via the OnSpin override below), so there is
//     nothing to leak or duplicate across template reapplication.
//   - Overrides the protected virtual On*Changed hooks that base's private
//     SetValidSpinDirection() reacts to (OnValueChanged, OnMaximumChanged,
//     OnMinimumChanged, OnIncrementChanged, OnIsReadOnlyChanged) plus
//     AllowSpin via OnPropertyChanged, and re-applies our own "both
//     directions stay enabled whenever spinning is possible at all" rule to
//     the reacquired Spinner AFTER calling base, synchronously in the same
//     call frame. Because there is no dispatcher hop between base's
//     transient "disable the direction at the bound" write and our
//     immediate correction, the template's RepeatButton never actually
//     observes the narrowed state - i.e. a transient-free fix, so holding
//     the spin button across a wrap keeps repeating instead of stalling.
//   - Overrides OnSpin (the protected virtual choke point common to
//     spin-button clicks, mouse wheel, and keyboard-driven spins) to detect
//     "value was already at/past a bound before this spin" and wrap Value
//     to the opposite bound instead of leaving base's clamped (unchanged)
//     value in place. base.OnSpin still runs first, so the public Spinned
//     event and the normal (non-boundary) increment/decrement math are
//     reused unchanged.
//   - Overrides OnTextChanged, OnLostFocus, and OnKeyDown[Enter] to
//     re-apply ValidSpinDirection AFTER those paths too, via the dedicated
//     ApplyValidSpinDirectionAfterTextSync() (NOT the plain
//     ApplyValidSpinDirection() the On*Changed overrides above use).
//     NumericUpDown's private SyncTextAndValueProperties() (reached from
//     all three: OnTextChanged runs it directly; OnLostFocus/OnKeyDown[Enter]
//     reach it via the private CommitInput()) unconditionally calls base's
//     own private SetValidSpinDirection() as its very last step - AFTER any
//     nested OnValueChanged our overrides above already reacted to - so
//     typing a *valid* boundary value (e.g. "10") directly into the text
//     box, or committing it via Tab/Enter/losing focus, re-narrows
//     ValidSpinDirection right after our fix ran and silently breaks the
//     opposite direction (a real dead-end: DoIncrement/DoDecrement gate on
//     ValidSpinDirection before doing anything, so the click/spin becomes a
//     no-op). BUT when the in-progress text is INVALID (e.g. "x") and the
//     change came from the user actually typing, SyncTextAndValueProperties
//     takes a different branch entirely and explicitly sets
//     Spinner.ValidSpinDirection = None WITHOUT updating Value - the stock
//     control's deliberate "can't spin from garbage text" state. Naively
//     re-widening after every OnTextChanged (as an earlier revision did)
//     stomped on that explicit None and let a click silently discard the
//     invalid text while incrementing/decrementing the stale Value.
//     ApplyValidSpinDirectionAfterTextSync() discriminates the two cases:
//     when spinning is otherwise possible (AllowSpin/!IsReadOnly/Increment
//     != 0) yet base left ValidSpinDirection == None, that combination can
//     only be the invalid-text branch (SetValidSpinDirection() itself can
//     never produce None here because Minimum(0) < Maximum(10) always holds
//     for the wizard's Frame bounds), so it is left untouched; any other
//     result (Increase-only, Decrease-only, or already Both) reflects a
//     genuinely synced, valid Value and is widened to Both as before.
//     OnKeyDown further restricts re-application to the Enter key, since
//     that is the only key base's CommitInput()/SyncTextAndValueProperties
//     path actually runs on.
using System;
using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Controls.Metadata;
using global::Avalonia.Controls.Primitives;
using global::Avalonia.Input;
using global::Avalonia.Interactivity;

namespace FEBuilderGBA.Avalonia.Controls
{
    [TemplatePart("PART_Spinner", typeof(Spinner))]
    public class WrappingNumericUpDown : NumericUpDown
    {
        // Preserve the standard NumericUpDown theme/template - this control
        // only changes spin-wrapping behavior, never its appearance.
        protected override Type StyleKeyOverride => typeof(NumericUpDown);

        // Our own reference to the Spinner template part. NumericUpDown's
        // own `Spinner` property and `SetValidSpinDirection()` are both
        // private, so this is the only way a derived class can read/set
        // ValidSpinDirection. Read-only lookup - never subscribed to any
        // event on it, so reacquiring it on every OnApplyTemplate call
        // cannot leak or duplicate handlers.
        private Spinner _spinner;

        protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
        {
            base.OnApplyTemplate(e);
            _spinner = e.NameScope.Find<Spinner>("PART_Spinner");
            ApplyValidSpinDirection();
        }

        protected override void OnValueChanged(decimal? oldValue, decimal? newValue)
        {
            base.OnValueChanged(oldValue, newValue);
            ApplyValidSpinDirection();
        }

        protected override void OnMaximumChanged(decimal oldValue, decimal newValue)
        {
            base.OnMaximumChanged(oldValue, newValue);
            ApplyValidSpinDirection();
        }

        protected override void OnMinimumChanged(decimal oldValue, decimal newValue)
        {
            base.OnMinimumChanged(oldValue, newValue);
            ApplyValidSpinDirection();
        }

        protected override void OnIncrementChanged(decimal oldValue, decimal newValue)
        {
            base.OnIncrementChanged(oldValue, newValue);
            ApplyValidSpinDirection();
        }

        protected override void OnIsReadOnlyChanged(bool oldValue, bool newValue)
        {
            base.OnIsReadOnlyChanged(oldValue, newValue);
            ApplyValidSpinDirection();
        }

        // AllowSpin has no dedicated On*Changed virtual on NumericUpDown, so
        // it is watched directly via the generic property-changed hook.
        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);
            if (change.Property == AllowSpinProperty)
            {
                ApplyValidSpinDirection();
            }
        }

        // base.OnTextChanged runs the private SyncTextAndValueProperties,
        // which - when the typed text is a VALID number - both updates
        // Value (re-entering OnValueChanged above, which already
        // re-widens) AND unconditionally calls base's own private
        // SetValidSpinDirection() as its very last step, narrowing again
        // right after. When the typed text is INVALID, base instead
        // explicitly disables both directions (ValidSpinDirection = None)
        // without touching Value. ApplyValidSpinDirectionAfterTextSync()
        // (not the plain ApplyValidSpinDirection() used elsewhere in this
        // class) re-applies our widened state for the valid case while
        // preserving that explicit None for the invalid case - see the
        // file-header comment for the full discriminator rationale.
        protected override void OnTextChanged(string oldValue, string newValue)
        {
            base.OnTextChanged(oldValue, newValue);
            ApplyValidSpinDirectionAfterTextSync();
        }

        // Losing focus commits pending text via the private CommitInput(),
        // which calls the same SyncTextAndValueProperties tail logic as
        // OnTextChanged above - but does so directly, without re-raising
        // the Text property (so OnTextChanged is not re-entered when the
        // committed text already matches Text, e.g. Tab/click-away right
        // after the last keystroke already synced it). Re-apply here too,
        // via the same invalid-text-preserving helper, so losing focus
        // right after typing a valid boundary value cannot leave a spin
        // direction narrowed.
        protected override void OnLostFocus(RoutedEventArgs e)
        {
            base.OnLostFocus(e);
            ApplyValidSpinDirectionAfterTextSync();
        }

        // Pressing Enter commits pending text via the same private
        // CommitInput() path as OnLostFocus above (bypassing OnTextChanged
        // for the same reason), so it needs the same re-application after
        // base runs. Restricted to the Enter key specifically: it is the
        // only key base.OnKeyDown actually forwards to CommitInput() /
        // SyncTextAndValueProperties(), so re-applying on every other key
        // would be pointless extra work with no corresponding base state
        // change to correct.
        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.Key == Key.Enter)
            {
                ApplyValidSpinDirectionAfterTextSync();
            }
        }

        /// <summary>
        /// Keep BOTH spin directions enabled on the reacquired Spinner
        /// whenever spinning is possible at all
        /// (AllowSpin &amp;&amp; !IsReadOnly &amp;&amp; Increment != 0) -
        /// regardless of where Value sits relative to Minimum/Maximum.
        /// Disabled/read-only/zero-increment fall back to the same "no spin
        /// directions" state the base control would use.
        /// </summary>
        private void ApplyValidSpinDirection()
        {
            if (_spinner == null)
            {
                return;
            }

            _spinner.ValidSpinDirection = (AllowSpin && !IsReadOnly && Increment != 0)
                ? (ValidSpinDirections.Increase | ValidSpinDirections.Decrease)
                : ValidSpinDirections.None;
        }

        /// <summary>
        /// Variant of <see cref="ApplyValidSpinDirection"/> used ONLY after
        /// a base text-sync path (OnTextChanged / OnLostFocus /
        /// OnKeyDown[Enter]) has just run. Unlike the plain method above,
        /// this preserves an explicit <see cref="ValidSpinDirections.None"/>
        /// that base's private SyncTextAndValueProperties() may have just
        /// written even though spinning is otherwise possible (AllowSpin
        /// &amp;&amp; !IsReadOnly &amp;&amp; Increment != 0). That specific
        /// combination - guards passing yet the result is None - can only
        /// mean base took its "user typed text that fails to parse" branch:
        /// it disables both directions without touching Value, because
        /// there is no valid Value yet to spin. (The plain,
        /// always-computed-from-Value SetValidSpinDirection() can never
        /// itself produce None while guards pass here, since the wizard's
        /// Frame bounds fix Minimum(0) &lt; Maximum(10), so at least one
        /// direction is always valid for any in-range Value.) Widening that
        /// None to Both - what an earlier revision of this control did -
        /// let a spin click silently discard the malformed text and
        /// increment/decrement a stale Value the user could not see
        /// reflected on screen. Any other post-base result (Increase-only,
        /// Decrease-only, or already Both) reflects a genuinely valid,
        /// synced Value and is widened exactly like
        /// <see cref="ApplyValidSpinDirection"/> would.
        /// </summary>
        private void ApplyValidSpinDirectionAfterTextSync()
        {
            if (_spinner == null)
            {
                return;
            }

            if (!(AllowSpin && !IsReadOnly && Increment != 0))
            {
                _spinner.ValidSpinDirection = ValidSpinDirections.None;
                return;
            }

            if (_spinner.ValidSpinDirection == ValidSpinDirections.None)
            {
                // Spinning is otherwise possible, yet base just left both
                // directions disabled: invalid in-progress text. Leave it -
                // do not widen a state that does not reflect a real Value.
                return;
            }

            _spinner.ValidSpinDirection = ValidSpinDirections.Increase | ValidSpinDirections.Decrease;
        }

        protected override void OnSpin(SpinEventArgs e)
        {
            if (e == null)
            {
                throw new ArgumentNullException(nameof(e));
            }

            if (!AllowSpin || IsReadOnly || Increment == 0)
            {
                // Disabled/read-only/zero-increment: defer entirely to the
                // standard (non-wrapping) behavior.
                base.OnSpin(e);
                return;
            }

            var before = Value;
            base.OnSpin(e);

            if (e.Direction == SpinDirection.Increase && before.HasValue && before.Value >= Maximum)
            {
                // Already at/past the top before this spin: base just
                // clamped Value back to Maximum (no visible change). Wrap.
                SetCurrentValue(ValueProperty, Minimum);
            }
            else if (e.Direction == SpinDirection.Decrease && before.HasValue && before.Value <= Minimum)
            {
                // Already at/past the bottom before this spin: base just
                // clamped Value back to Minimum (no visible change). Wrap.
                SetCurrentValue(ValueProperty, Maximum);
            }
        }
    }
}
