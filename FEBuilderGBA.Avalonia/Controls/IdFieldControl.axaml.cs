// SPDX-License-Identifier: GPL-3.0-or-later
// Reusable type-ID input control (#366).
//
// Bundles the affordances WinForms gets free from `InputFormRef.MakeJumpEvent`
// auto-wiring (hyperlink label, jump, pick, inline name preview) into a single
// Avalonia UserControl. Hosts wire three routed events:
//   - JumpRequested  → open the target editor at the current id
//   - PickRequested  → open the target editor in pick mode
//   - ValueChanged   → refresh NameText (NameResolver-based preview)
// The control is intentionally agnostic about WHICH editor to open or HOW to
// resolve the name — those concerns stay in the host code-behind so each call
// site keeps its typed `WindowManager.Navigate<TView>` reference (greppable
// for the gap-sweep scanner).
using System;
using global::Avalonia;
using global::Avalonia.Automation;
using global::Avalonia.Controls;
using global::Avalonia.Input;
using global::Avalonia.Interactivity;

namespace FEBuilderGBA.Avalonia.Controls
{
    /// <summary>
    /// Event args for <see cref="IdFieldControl.ValueChanged"/>.
    /// </summary>
    public class IdFieldValueChangedEventArgs : RoutedEventArgs
    {
        public uint NewValue { get; }
        public uint OldValue { get; }
        public IdFieldValueChangedEventArgs(RoutedEvent routedEvent, uint oldValue, uint newValue)
            : base(routedEvent)
        {
            OldValue = oldValue;
            NewValue = newValue;
        }
    }

    /// <summary>
    /// Reusable ID input with hyperlink label, NumericUpDown value box, inline
    /// name preview, and Jump + Pick buttons. See file header for design notes.
    /// </summary>
    public partial class IdFieldControl : UserControl
    {
        // ---- StyledProperty declarations ----

        public static readonly StyledProperty<uint> ValueProperty =
            AvaloniaProperty.Register<IdFieldControl, uint>(nameof(Value), defaultValue: 0u);

        public static readonly StyledProperty<string> LabelProperty =
            AvaloniaProperty.Register<IdFieldControl, string>(nameof(Label), defaultValue: "");

        public static readonly StyledProperty<string> NameTextProperty =
            AvaloniaProperty.Register<IdFieldControl, string>(nameof(NameText), defaultValue: "");

        public static readonly StyledProperty<int> MaximumProperty =
            AvaloniaProperty.Register<IdFieldControl, int>(nameof(Maximum), defaultValue: 255);

        /// <summary>The current id.</summary>
        public uint Value
        {
            get => GetValue(ValueProperty);
            set => SetValue(ValueProperty, value);
        }

        /// <summary>Caption rendered as a clickable hyperlink.</summary>
        public string Label
        {
            get => GetValue(LabelProperty);
            set => SetValue(LabelProperty, value);
        }

        /// <summary>Inline name preview text (set by the host via NameResolver).</summary>
        public string NameText
        {
            get => GetValue(NameTextProperty);
            set => SetValue(NameTextProperty, value);
        }

        /// <summary>Upper bound for the NumericUpDown.</summary>
        public int Maximum
        {
            get => GetValue(MaximumProperty);
            set => SetValue(MaximumProperty, value);
        }

        // ---- Routed events ----

        public static readonly RoutedEvent<RoutedEventArgs> JumpRequestedEvent =
            RoutedEvent.Register<IdFieldControl, RoutedEventArgs>(nameof(JumpRequested), RoutingStrategies.Bubble);

        public static readonly RoutedEvent<RoutedEventArgs> PickRequestedEvent =
            RoutedEvent.Register<IdFieldControl, RoutedEventArgs>(nameof(PickRequested), RoutingStrategies.Bubble);

        public static readonly RoutedEvent<IdFieldValueChangedEventArgs> ValueChangedEvent =
            RoutedEvent.Register<IdFieldControl, IdFieldValueChangedEventArgs>(nameof(ValueChanged), RoutingStrategies.Bubble);

        /// <summary>Raised when the user requests to open the target editor at this id.</summary>
        public event EventHandler<RoutedEventArgs>? JumpRequested
        {
            add => AddHandler(JumpRequestedEvent, value);
            remove => RemoveHandler(JumpRequestedEvent, value);
        }

        /// <summary>Raised when the user requests to pick a value from the target editor.</summary>
        public event EventHandler<RoutedEventArgs>? PickRequested
        {
            add => AddHandler(PickRequestedEvent, value);
            remove => RemoveHandler(PickRequestedEvent, value);
        }

        /// <summary>Raised whenever the inner NumericUpDown value changes (user input OR programmatic).</summary>
        public event EventHandler<IdFieldValueChangedEventArgs>? ValueChanged
        {
            add => AddHandler(ValueChangedEvent, value);
            remove => RemoveHandler(ValueChangedEvent, value);
        }

        // Suppress recursive callbacks when Value <-> NumericUpDown.Value sync.
        bool _syncing;

        public IdFieldControl()
        {
            InitializeComponent();
            // Wire NumericUpDown.ValueChanged → routed ValueChanged event.
            if (ValueBox != null)
            {
                ValueBox.ValueChanged += OnNumericValueChanged;
            }
            // Propagate the host's AutomationId to the inner controls so E2E
            // selectors that expect "{host}_Input" to point at the NumericUpDown
            // still resolve when typing values. We derive suffixed ids for the
            // remaining inner controls so each is independently addressable.
            AttachedToVisualTree += (_, _) => PropagateInnerAutomationIds();
        }

        /// <summary>
        /// Map the host's AutomationId onto the four inner interactive controls
        /// (NumericUpDown, NameLabel, JumpButton, PickButton) and CLEAR the
        /// host's own AutomationId so the "_Input" id resolves UNAMBIGUOUSLY
        /// to the NumericUpDown. The host itself gets a "_Control" id derived
        /// from the same base so automation tests can still locate the host
        /// when needed. This addresses Copilot CLI's second-round review on
        /// PR #477.
        /// </summary>
        void PropagateInnerAutomationIds()
        {
            string hostId = AutomationProperties.GetAutomationId(this);
            if (string.IsNullOrEmpty(hostId)) return;

            // Strip a trailing "_Input" if present (CCBranchEditor_Promo1_Input →
            // CCBranchEditor_Promo1) so derived ids stay tidy.
            string baseId = hostId;
            if (baseId.EndsWith("_Input", StringComparison.Ordinal))
                baseId = baseId.Substring(0, baseId.Length - "_Input".Length);

            // Move the host id onto the actual input control. To keep the
            // "_Input" id unique in the logical tree, REPLACE the host's
            // AutomationId with a distinct "_Control" id.
            string inputId = baseId + "_Input";
            string hostControlId = baseId + "_Control";

            if (ValueBox != null && string.IsNullOrEmpty(AutomationProperties.GetAutomationId(ValueBox)))
                AutomationProperties.SetAutomationId(ValueBox, inputId);
            if (NameLabel != null && string.IsNullOrEmpty(AutomationProperties.GetAutomationId(NameLabel)))
                AutomationProperties.SetAutomationId(NameLabel, baseId + "_Name");
            if (JumpButton != null && string.IsNullOrEmpty(AutomationProperties.GetAutomationId(JumpButton)))
                AutomationProperties.SetAutomationId(JumpButton, baseId + "_Jump_Button");
            if (PickButton != null && string.IsNullOrEmpty(AutomationProperties.GetAutomationId(PickButton)))
                AutomationProperties.SetAutomationId(PickButton, baseId + "_Pick_Button");

            // Reassign the host id last so the "_Input" id only resolves to the
            // NumericUpDown, not the IdFieldControl host.
            AutomationProperties.SetAutomationId(this, hostControlId);
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);
            if (change.Property == ValueProperty)
            {
                if (!_syncing && ValueBox != null)
                {
                    _syncing = true;
                    try { ValueBox.Value = (decimal)Value; }
                    finally { _syncing = false; }
                }
                uint oldVal = change.GetOldValue<uint>();
                uint newVal = change.GetNewValue<uint>();
                RaiseEvent(new IdFieldValueChangedEventArgs(ValueChangedEvent, oldVal, newVal));
            }
            else if (change.Property == LabelProperty)
            {
                if (LabelText != null) LabelText.Text = Label;
            }
            else if (change.Property == NameTextProperty)
            {
                if (NameLabel != null) NameLabel.Text = NameText;
            }
            else if (change.Property == MaximumProperty)
            {
                if (ValueBox != null) ValueBox.Maximum = Maximum;
            }
        }

        void OnNumericValueChanged(object? sender, NumericUpDownValueChangedEventArgs e)
        {
            if (_syncing) return;
            uint newVal = 0;
            if (e.NewValue.HasValue)
            {
                decimal v = e.NewValue.Value;
                if (v < 0m) v = 0m;
                if (v > uint.MaxValue) v = uint.MaxValue;
                newVal = (uint)v;
            }
            _syncing = true;
            try { Value = newVal; }
            finally { _syncing = false; }
            // OnPropertyChanged(ValueProperty) will raise ValueChanged.
        }

        void OnLabelClick(object? sender, PointerPressedEventArgs e)
        {
            RaiseEvent(new RoutedEventArgs(JumpRequestedEvent));
        }

        void OnJumpClick(object? sender, RoutedEventArgs e)
        {
            RaiseEvent(new RoutedEventArgs(JumpRequestedEvent));
        }

        void OnPickClick(object? sender, RoutedEventArgs e)
        {
            RaiseEvent(new RoutedEventArgs(PickRequestedEvent));
        }
    }
}
