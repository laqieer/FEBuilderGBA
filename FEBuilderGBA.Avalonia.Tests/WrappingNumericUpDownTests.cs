// SPDX-License-Identifier: GPL-3.0-or-later
// Headless UI tests for WrappingNumericUpDown (#1985) — the NumericUpDown
// variant used only by the Portrait Import Wizard's Frame selector so
// spinning past either bound wraps to the other end instead of clamping and
// disabling the spin button.
//
// These tests exercise the REAL template parts (the reacquired PART_Spinner
// and its PART_IncreaseButton/PART_DecreaseButton RepeatButtons, per the
// Fluent NumericUpDown/ButtonSpinner control themes) rather than only a bare
// helper method, per the accepted plan's "exercise actual template parts
// where feasible" requirement.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FEBuilderGBA.Avalonia.Controls;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests
{
    public class WrappingNumericUpDownTests
    {
        private static (Window window, WrappingNumericUpDown control) CreateShownControl(
            decimal minimum = 0, decimal maximum = 10, decimal value = 0, decimal increment = 1,
            bool allowSpin = true, bool isReadOnly = false)
        {
            var control = new WrappingNumericUpDown
            {
                Minimum = minimum,
                Maximum = maximum,
                Value = value,
                Increment = increment,
                AllowSpin = allowSpin,
                IsReadOnly = isReadOnly,
                FormatString = "0",
            };
            var window = new Window { Width = 200, Height = 100, Content = control };
            window.Show();
            return (window, control);
        }

        private static Spinner GetSpinner(WrappingNumericUpDown control) =>
            control.GetVisualDescendants().OfType<Spinner>().First();

        private static RepeatButton GetIncreaseButton(WrappingNumericUpDown control) =>
            control.GetVisualDescendants().OfType<RepeatButton>().First(b => b.Name == "PART_IncreaseButton");

        private static RepeatButton GetDecreaseButton(WrappingNumericUpDown control) =>
            control.GetVisualDescendants().OfType<RepeatButton>().First(b => b.Name == "PART_DecreaseButton");

        private static TextBox GetTextBox(WrappingNumericUpDown control) =>
            control.GetVisualDescendants().OfType<TextBox>().First();

        private static void Click(RepeatButton button) =>
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        [AvaloniaFact]
        public void Constructor_CreatesControl()
        {
            var control = new WrappingNumericUpDown();
            Assert.NotNull(control);
        }

        [AvaloniaFact]
        public void IncreaseAtMaximum_WrapsToMinimum_ViaRealIncreaseButton()
        {
            var (window, control) = CreateShownControl(minimum: 0, maximum: 10, value: 10, increment: 1);
            try
            {
                Click(GetIncreaseButton(control));
                Assert.Equal(0m, control.Value);
            }
            finally
            {
                window.Close();
            }
        }

        [AvaloniaFact]
        public void DecreaseAtMinimum_WrapsToMaximum_ViaRealDecreaseButton()
        {
            var (window, control) = CreateShownControl(minimum: 0, maximum: 10, value: 0, increment: 1);
            try
            {
                Click(GetDecreaseButton(control));
                Assert.Equal(10m, control.Value);
            }
            finally
            {
                window.Close();
            }
        }

        [AvaloniaFact]
        public void RepeatedIncreaseClicks_CrossMaximumBoundaryMultipleTimes()
        {
            var (window, control) = CreateShownControl(minimum: 0, maximum: 3, value: 0, increment: 1);
            try
            {
                var increase = GetIncreaseButton(control);
                var expected = new decimal[] { 1, 2, 3, 0, 1, 2, 3, 0 };
                foreach (var exp in expected)
                {
                    Click(increase);
                    Assert.Equal(exp, control.Value);
                }
            }
            finally
            {
                window.Close();
            }
        }

        [AvaloniaFact]
        public void RepeatedDecreaseClicks_CrossMinimumBoundaryMultipleTimes()
        {
            var (window, control) = CreateShownControl(minimum: 0, maximum: 3, value: 0, increment: 1);
            try
            {
                var decrease = GetDecreaseButton(control);
                var expected = new decimal[] { 3, 2, 1, 0, 3, 2, 1, 0 };
                foreach (var exp in expected)
                {
                    Click(decrease);
                    Assert.Equal(exp, control.Value);
                }
            }
            finally
            {
                window.Close();
            }
        }

        [AvaloniaFact]
        public void AtMaximum_BothSpinDirectionsRemainEnabled_OnRealTemplateParts()
        {
            var (window, control) = CreateShownControl(minimum: 0, maximum: 10, value: 10, increment: 1);
            try
            {
                var spinner = GetSpinner(control);
                Assert.Equal(ValidSpinDirections.Increase | ValidSpinDirections.Decrease, spinner.ValidSpinDirection);
                Assert.True(GetIncreaseButton(control).IsEnabled);
                Assert.True(GetDecreaseButton(control).IsEnabled);
            }
            finally
            {
                window.Close();
            }
        }

        [AvaloniaFact]
        public void AtMinimum_BothSpinDirectionsRemainEnabled_OnRealTemplateParts()
        {
            var (window, control) = CreateShownControl(minimum: 0, maximum: 10, value: 0, increment: 1);
            try
            {
                var spinner = GetSpinner(control);
                Assert.Equal(ValidSpinDirections.Increase | ValidSpinDirections.Decrease, spinner.ValidSpinDirection);
                Assert.True(GetIncreaseButton(control).IsEnabled);
                Assert.True(GetDecreaseButton(control).IsEnabled);
            }
            finally
            {
                window.Close();
            }
        }

        [AvaloniaFact]
        public void AfterWrapping_BothSpinDirectionsStillEnabled()
        {
            var (window, control) = CreateShownControl(minimum: 0, maximum: 10, value: 10, increment: 1);
            try
            {
                Click(GetIncreaseButton(control));
                Assert.Equal(0m, control.Value);
                var spinner = GetSpinner(control);
                Assert.Equal(ValidSpinDirections.Increase | ValidSpinDirections.Decrease, spinner.ValidSpinDirection);
                Assert.True(GetIncreaseButton(control).IsEnabled);
                Assert.True(GetDecreaseButton(control).IsEnabled);
            }
            finally
            {
                window.Close();
            }
        }

        [AvaloniaFact]
        public void ReadOnly_DoesNotWrap_ValueUnchanged()
        {
            var (window, control) = CreateShownControl(minimum: 0, maximum: 10, value: 10, increment: 1, isReadOnly: true);
            try
            {
                // IsReadOnly disables spinning entirely (base + our own
                // ApplyValidSpinDirection both agree None), so the spin
                // buttons should be disabled and Value must not change.
                Assert.False(GetIncreaseButton(control).IsEnabled);
                Assert.False(GetDecreaseButton(control).IsEnabled);
                Assert.Equal(10m, control.Value);
            }
            finally
            {
                window.Close();
            }
        }

        [AvaloniaFact]
        public void AllowSpinFalse_DoesNotWrap_SpinButtonsDisabled()
        {
            var (window, control) = CreateShownControl(minimum: 0, maximum: 10, value: 10, increment: 1, allowSpin: false);
            try
            {
                Assert.False(GetIncreaseButton(control).IsEnabled);
                Assert.False(GetDecreaseButton(control).IsEnabled);
                Assert.Equal(10m, control.Value);
            }
            finally
            {
                window.Close();
            }
        }

        [AvaloniaFact]
        public void ZeroIncrement_DoesNotWrap_SpinButtonsDisabled()
        {
            var (window, control) = CreateShownControl(minimum: 0, maximum: 10, value: 10, increment: 0);
            try
            {
                Assert.False(GetIncreaseButton(control).IsEnabled);
                Assert.False(GetDecreaseButton(control).IsEnabled);
                Assert.Equal(10m, control.Value);
            }
            finally
            {
                window.Close();
            }
        }

        [AvaloniaFact]
        public void NormalSpin_AwayFromBounds_IncrementsNormally()
        {
            var (window, control) = CreateShownControl(minimum: 0, maximum: 10, value: 5, increment: 1);
            try
            {
                Click(GetIncreaseButton(control));
                Assert.Equal(6m, control.Value);
                Click(GetDecreaseButton(control));
                Assert.Equal(5m, control.Value);
            }
            finally
            {
                window.Close();
            }
        }

        [AvaloniaFact]
        public void StyleKeyOverride_ResolvesToStandardNumericUpDownTheme()
        {
            // #1985 requires the control to keep the standard NumericUpDown
            // theme/template rather than needing its own control theme.
            var control = new WrappingNumericUpDown();
            Assert.Equal(typeof(NumericUpDown), control.StyleKey);
        }

        [AvaloniaFact]
        public void ReapplyingTemplate_DoesNotDuplicateWrapBehavior()
        {
            // Force a second OnApplyTemplate pass (re-parent through a fresh
            // Window) and confirm a single increase-click still only wraps
            // once (no doubled/duplicated handler firing multiple increments
            // per click).
            var control = new WrappingNumericUpDown
            {
                Minimum = 0,
                Maximum = 10,
                Value = 10,
                Increment = 1,
                FormatString = "0",
            };
            var window1 = new Window { Width = 200, Height = 100, Content = control };
            window1.Show();
            window1.Content = null;
            window1.Close();

            var window2 = new Window { Width = 200, Height = 100, Content = control };
            window2.Show();
            try
            {
                Click(GetIncreaseButton(control));
                Assert.Equal(0m, control.Value);
            }
            finally
            {
                window2.Close();
            }
        }

        // --------------------------------------------------------------
        // Text-sync boundary regression (#1985): NumericUpDown's private
        // SyncTextAndValueProperties() unconditionally re-narrows
        // ValidSpinDirection as its very last step whenever Text is set (or
        // committed via Enter/lost focus) to a value that parses cleanly —
        // AFTER any OnValueChanged our overrides above already reacted to.
        // Setting Text directly to a boundary value reproduces this exactly
        // (this is the literal repro reported: "Setting Text=\"10\" leaves
        // increase disabled").
        // --------------------------------------------------------------

        [AvaloniaFact]
        public void SettingTextToMaximumBoundary_KeepsBothDirectionsEnabled_ThenIncreaseWraps()
        {
            var (window, control) = CreateShownControl(minimum: 0, maximum: 10, value: 5, increment: 1);
            try
            {
                control.Text = "10";

                Assert.Equal(10m, control.Value);
                var spinner = GetSpinner(control);
                Assert.Equal(ValidSpinDirections.Increase | ValidSpinDirections.Decrease, spinner.ValidSpinDirection);
                Assert.True(GetIncreaseButton(control).IsEnabled, "Increase must stay enabled after Text=\"10\"");
                Assert.True(GetDecreaseButton(control).IsEnabled);

                Click(GetIncreaseButton(control));
                Assert.Equal(0m, control.Value);
            }
            finally
            {
                window.Close();
            }
        }

        [AvaloniaFact]
        public void SettingTextToMinimumBoundary_KeepsBothDirectionsEnabled_ThenDecreaseWraps()
        {
            var (window, control) = CreateShownControl(minimum: 0, maximum: 10, value: 5, increment: 1);
            try
            {
                control.Text = "0";

                Assert.Equal(0m, control.Value);
                var spinner = GetSpinner(control);
                Assert.Equal(ValidSpinDirections.Increase | ValidSpinDirections.Decrease, spinner.ValidSpinDirection);
                Assert.True(GetDecreaseButton(control).IsEnabled, "Decrease must stay enabled after Text=\"0\"");
                Assert.True(GetIncreaseButton(control).IsEnabled);

                Click(GetDecreaseButton(control));
                Assert.Equal(10m, control.Value);
            }
            finally
            {
                window.Close();
            }
        }

        [AvaloniaFact]
        public void TypingMaximumBoundaryThenCommittingWithEnter_KeepsBothDirectionsEnabled_ThenIncreaseWraps()
        {
            // Realistic path: focus the actual PART_TextBox, replace its
            // selection via the real text-input pipeline (not a direct
            // property set), then commit with Enter - which reaches
            // SyncTextAndValueProperties via CommitInput()/OnKeyDown,
            // bypassing OnTextChanged entirely.
            var (window, control) = CreateShownControl(minimum: 0, maximum: 10, value: 5, increment: 1);
            try
            {
                var textBox = GetTextBox(control);
                textBox.Focus();
                textBox.SelectAll();
                window.KeyTextInput("10");
                window.KeyPress(Key.Enter, RawInputModifiers.None);
                window.KeyRelease(Key.Enter, RawInputModifiers.None);

                Assert.Equal(10m, control.Value);
                var spinner = GetSpinner(control);
                Assert.Equal(ValidSpinDirections.Increase | ValidSpinDirections.Decrease, spinner.ValidSpinDirection);
                Assert.True(GetIncreaseButton(control).IsEnabled);
                Assert.True(GetDecreaseButton(control).IsEnabled);

                Click(GetIncreaseButton(control));
                Assert.Equal(0m, control.Value);
            }
            finally
            {
                window.Close();
            }
        }

        [AvaloniaFact]
        public void TypingMinimumBoundaryThenLosingFocus_KeepsBothDirectionsEnabled_ThenDecreaseWraps()
        {
            // Realistic path: type via the real text-input pipeline, then
            // move focus to another control (no Enter) - this reaches
            // SyncTextAndValueProperties via OnLostFocus's CommitInput(true),
            // which - like the Enter path above - bypasses OnTextChanged
            // when the committed text already matches the property.
            var (window, control) = CreateShownControl(minimum: 0, maximum: 10, value: 5, increment: 1);
            try
            {
                var textBox = GetTextBox(control);
                textBox.Focus();
                textBox.SelectAll();
                window.KeyTextInput("0");

                GetIncreaseButton(control).Focus();

                Assert.Equal(0m, control.Value);
                var spinner = GetSpinner(control);
                Assert.Equal(ValidSpinDirections.Increase | ValidSpinDirections.Decrease, spinner.ValidSpinDirection);
                Assert.True(GetDecreaseButton(control).IsEnabled, "Decrease must stay enabled after losing focus at Text=\"0\"");
                Assert.True(GetIncreaseButton(control).IsEnabled);

                Click(GetDecreaseButton(control));
                Assert.Equal(10m, control.Value);
            }
            finally
            {
                window.Close();
            }
        }

        // --------------------------------------------------------------
        // Invalid-text regression (#1985): ApplyValidSpinDirectionAfterTextSync
        // must NOT re-widen an explicit ValidSpinDirections.None left by
        // base's SyncTextAndValueProperties() when the in-progress text
        // fails to parse. Repro: focus PART_TextBox, select all, type "x" —
        // stock NumericUpDown disables both spin directions and leaves
        // Value untouched; an earlier revision of this fix unconditionally
        // widened to Both afterward, letting a click silently discard the
        // malformed text and spin the stale Value.
        // --------------------------------------------------------------

        [AvaloniaFact]
        public void TypingInvalidText_LeavesBothDirectionsDisabled_AndClickIsNoOp()
        {
            var (window, control) = CreateShownControl(minimum: 0, maximum: 10, value: 5, increment: 1);
            try
            {
                var textBox = GetTextBox(control);
                textBox.Focus();
                textBox.SelectAll();
                window.KeyTextInput("x");

                Assert.Equal("x", control.Text);
                Assert.Equal(5m, control.Value); // stale: base never updated Value from unparsable text

                var spinner = GetSpinner(control);
                Assert.Equal(ValidSpinDirections.None, spinner.ValidSpinDirection);
                Assert.False(GetIncreaseButton(control).IsEnabled, "Increase must stay disabled while Text=\"x\" is invalid");
                Assert.False(GetDecreaseButton(control).IsEnabled, "Decrease must stay disabled while Text=\"x\" is invalid");

                Click(GetIncreaseButton(control));
                Assert.Equal(5m, control.Value); // must be a genuine no-op, not a silent discard+spin
                Click(GetDecreaseButton(control));
                Assert.Equal(5m, control.Value);
            }
            finally
            {
                window.Close();
            }
        }

        [AvaloniaFact]
        public void TypingInvalidTextThenPressingEnter_LeavesBothDirectionsDisabled_AndClickIsNoOp()
        {
            var (window, control) = CreateShownControl(minimum: 0, maximum: 10, value: 5, increment: 1);
            try
            {
                var textBox = GetTextBox(control);
                textBox.Focus();
                textBox.SelectAll();
                window.KeyTextInput("x");

                window.KeyPress(Key.Enter, RawInputModifiers.None);
                window.KeyRelease(Key.Enter, RawInputModifiers.None);

                Assert.Equal("x", control.Text);
                Assert.Equal(5m, control.Value);
                var spinner = GetSpinner(control);
                Assert.Equal(ValidSpinDirections.None, spinner.ValidSpinDirection);
                Assert.False(GetIncreaseButton(control).IsEnabled);
                Assert.False(GetDecreaseButton(control).IsEnabled);

                Click(GetIncreaseButton(control));
                Assert.Equal(5m, control.Value);
            }
            finally
            {
                window.Close();
            }
        }

        [AvaloniaFact]
        public void TypingInvalidTextThenLosingFocus_LeavesBothDirectionsDisabled_AndClickIsNoOp()
        {
            var (window, control) = CreateShownControl(minimum: 0, maximum: 10, value: 5, increment: 1);
            try
            {
                var textBox = GetTextBox(control);
                textBox.Focus();
                textBox.SelectAll();
                window.KeyTextInput("x");

                GetIncreaseButton(control).Focus();

                Assert.Equal("x", control.Text);
                Assert.Equal(5m, control.Value);
                var spinner = GetSpinner(control);
                Assert.Equal(ValidSpinDirections.None, spinner.ValidSpinDirection);
                Assert.False(GetIncreaseButton(control).IsEnabled);
                Assert.False(GetDecreaseButton(control).IsEnabled);

                Click(GetDecreaseButton(control));
                Assert.Equal(5m, control.Value);
            }
            finally
            {
                window.Close();
            }
        }

        // --------------------------------------------------------------
        // Genuine pointer press-and-hold (#1985 acceptance-test gap): real
        // window.MouseDown/MouseUp on the actual PART_IncreaseButton /
        // PART_DecreaseButton RepeatButton, with a very short Delay/Interval,
        // capturing the real ValueChanged sequence over the hold duration.
        // Not a substitute - no ClickEvent calls are used here.
        // --------------------------------------------------------------

        private static Point ButtonCenterInWindow(Window window, RepeatButton button)
        {
            var size = button.Bounds.Size;
            var local = new Point(
                size.Width > 0 ? size.Width / 2 : 1,
                size.Height > 0 ? size.Height / 2 : 1);
            return button.TranslatePoint(local, window) ?? local;
        }

        // Polls (via short async delays that let the headless dispatcher
        // process pending RepeatButton timer ticks) until either the held
        // sequence has crossed the boundary and continued past the wrap, or
        // a generous iteration budget is exhausted. Keeps the test
        // deterministic under slow/CI timing without hard-coding an exact
        // tick count.
        private static async Task WaitUntilAsync(Func<bool> condition, int maxIterations = 60, int delayMs = 25)
        {
            for (var i = 0; i < maxIterations && !condition(); i++)
            {
                await Task.Delay(delayMs);
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            }
        }

        [AvaloniaFact]
        public async Task PressAndHold_RealPointerOnIncreaseButton_WrapsAndContinuesBeyondMaximum()
        {
            var (window, control) = CreateShownControl(minimum: 0, maximum: 3, value: 1, increment: 1);
            try
            {
                var button = GetIncreaseButton(control);
                button.Delay = 15;
                button.Interval = 15;
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);

                var values = new List<decimal?>();
                control.ValueChanged += (_, e) => values.Add(e.NewValue);

                var point = ButtonCenterInWindow(window, button);
                window.MouseDown(point, MouseButton.Left, RawInputModifiers.None);

                // Long enough to survive slow/CI timing: initial click +
                // Delay + many Intervals, so the hold reliably reaches the
                // Maximum (3), wraps to Minimum (0), and keeps going well
                // past it - all from ONE press, no fresh MouseDown.
                await WaitUntilAsync(() =>
                    values.Contains(3m) && values.Contains(0m) &&
                    values.IndexOf(0m) > values.IndexOf(3m) &&
                    values.Count > values.IndexOf(0m) + 1);

                window.MouseUp(point, MouseButton.Left, RawInputModifiers.None);
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);

                Assert.Contains(3m, values);
                Assert.Contains(0m, values);
                var maxIndex = values.IndexOf(3m);
                var wrapIndex = values.IndexOf(0m);
                Assert.True(maxIndex >= 0 && wrapIndex > maxIndex,
                    $"Expected 0 (wrap) to follow 3 (maximum) in the held sequence: [{string.Join(", ", values)}]");
                Assert.True(values.Count > wrapIndex + 1,
                    $"Expected the hold to keep incrementing past the wrap without a fresh press: [{string.Join(", ", values)}]");
            }
            finally
            {
                window.Close();
            }
        }

        [AvaloniaFact]
        public async Task PressAndHold_RealPointerOnDecreaseButton_WrapsAndContinuesBeyondMinimum()
        {
            var (window, control) = CreateShownControl(minimum: 0, maximum: 3, value: 2, increment: 1);
            try
            {
                var button = GetDecreaseButton(control);
                button.Delay = 15;
                button.Interval = 15;
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);

                var values = new List<decimal?>();
                control.ValueChanged += (_, e) => values.Add(e.NewValue);

                var point = ButtonCenterInWindow(window, button);
                window.MouseDown(point, MouseButton.Left, RawInputModifiers.None);

                await WaitUntilAsync(() =>
                    values.Contains(0m) && values.Contains(3m) &&
                    values.IndexOf(3m) > values.IndexOf(0m) &&
                    values.Count > values.IndexOf(3m) + 1);

                window.MouseUp(point, MouseButton.Left, RawInputModifiers.None);
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);

                Assert.Contains(0m, values);
                Assert.Contains(3m, values);
                var minIndex = values.IndexOf(0m);
                var wrapIndex = values.IndexOf(3m);
                Assert.True(minIndex >= 0 && wrapIndex > minIndex,
                    $"Expected 3 (wrap) to follow 0 (minimum) in the held sequence: [{string.Join(", ", values)}]");
                Assert.True(values.Count > wrapIndex + 1,
                    $"Expected the hold to keep decrementing past the wrap without a fresh press: [{string.Join(", ", values)}]");
            }
            finally
            {
                window.Close();
            }
        }
    }
}
