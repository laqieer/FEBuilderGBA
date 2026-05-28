// SPDX-License-Identifier: GPL-3.0-or-later
//
// Editable variant of EditorTopBar (#701 Slice A foundation).
//
// This control bundles the Read Start / Read Count / Read Size NumericUpDown
// inputs + a Reload button that several Avalonia editor views ship as ad-hoc
// inline Grids. By centralising the layout, automation ids, and event surface,
// per-editor migrations become mechanical follow-ups.
//
// Currently used by NO editor — added in PR for #701 Slice A to establish the
// API. Per-editor migration is deferred to follow-up issue #713 because
// investigation revealed each editor has unique additional controls
// (FilterCombo, ChangeType combo, FilterBox, ListExpandButton, etc.) in its
// top strip that don't fit a uniform replacement.
//
// AutomationId mapping
//   Hosts set ONE AutomationId on the EditorTopBarWithInputs (e.g.
//   "MonsterItem_TopBar") and the control derives suffixed ids on the inner
//   controls so E2E tests can address them and so multiple instances in the
//   same visual tree never collide:
//     {base}_ReadStart_Input, {base}_ReadCount_Input,
//     {base}_ReadSize_Input,  {base}_Reload_Button.
//   "{base}" strips a trailing "_TopBar" if present, mirroring EditorTopBar.
//
//   To preserve legacy ids pre-#701 selectors rely on (e.g.
//   "SongInstrument_ReadStartAddress_Input"), hosts may set any of the
//   *AutomationId styled properties — those override the derived id wholesale.
using System;
using global::Avalonia;
using global::Avalonia.Automation;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;

namespace FEBuilderGBA.Avalonia.Controls
{
    /// <summary>
    /// Editable variant of EditorTopBar for editors that allow the user
    /// to type a Read Start / Read Count / Read Size and trigger Reload.
    ///
    /// <para>Currently used by no editor — added in PR for #701 Slice A
    /// to establish the API. Per-editor migration is tracked in follow-up
    /// #713 once each editor's additional controls (FilterCombo, ChangeType,
    /// FilterBox, ListExpandButton, etc.) have been audited.</para>
    /// </summary>
    public partial class EditorTopBarWithInputs : UserControl
    {
        // -----------------------------------------------------------------
        // Slot-visibility styled properties (#649 Slice B)
        //
        // Most editors that migrate to this control have ReadStart +
        // ReadCount (no Size). Without these gates the unified bar
        // introduces a phantom "Read Size:" slot into views that never
        // had one before — a UX regression. Hosts set ShowReadSize="False"
        // (etc.) to keep their original field set.
        // -----------------------------------------------------------------

        public static readonly StyledProperty<bool> ShowReadStartProperty =
            AvaloniaProperty.Register<EditorTopBarWithInputs, bool>(nameof(ShowReadStart), defaultValue: true);

        public static readonly StyledProperty<bool> ShowReadCountProperty =
            AvaloniaProperty.Register<EditorTopBarWithInputs, bool>(nameof(ShowReadCount), defaultValue: true);

        public static readonly StyledProperty<bool> ShowReadSizeProperty =
            AvaloniaProperty.Register<EditorTopBarWithInputs, bool>(nameof(ShowReadSize), defaultValue: true);

        // -----------------------------------------------------------------
        // Input editability (#649 Slice B)
        //
        // Pre-migration editors often shipped `IsReadOnly="True"` or
        // `IsEnabled="False"` NumericUpDowns acting as labels. Setting
        // InputsEnabled=false on the unified bar disables the three
        // NumericUpDowns at once so the migrated view keeps that UX.
        // -----------------------------------------------------------------

        public static readonly StyledProperty<bool> InputsEnabledProperty =
            AvaloniaProperty.Register<EditorTopBarWithInputs, bool>(nameof(InputsEnabled), defaultValue: true);

        // -----------------------------------------------------------------
        // Label-text styled properties (#649 Slice B)
        //
        // Lets i18n / per-editor overrides retitle the slots without
        // re-templating the control. Defaults match the original AXAML
        // text so unmigrated hosts see no change.
        // -----------------------------------------------------------------

        public static readonly StyledProperty<string> ReadStartLabelProperty =
            AvaloniaProperty.Register<EditorTopBarWithInputs, string>(nameof(ReadStartLabel), defaultValue: "Read Start:");

        public static readonly StyledProperty<string> ReadCountLabelProperty =
            AvaloniaProperty.Register<EditorTopBarWithInputs, string>(nameof(ReadCountLabel), defaultValue: "Read Count:");

        public static readonly StyledProperty<string> ReadSizeLabelProperty =
            AvaloniaProperty.Register<EditorTopBarWithInputs, string>(nameof(ReadSizeLabel), defaultValue: "Read Size:");

        // -----------------------------------------------------------------
        // Per-slot Min/Max styled properties (#741 review)
        //
        // Pre-migration editors used stricter NumericUpDown limits (e.g.
        // SongTrack/SongInstrument/AIScript: ReadCount max 4096;
        // SMEPromoList: ReadCount max 256). Without these properties the
        // unified bar's hard-coded defaults would WIDEN the accepted range,
        // letting users type values the legacy UI prevented. Hosts set
        // these to the same limits the pre-migration NumericUpDowns had.
        // -----------------------------------------------------------------

        public static readonly StyledProperty<decimal> ReadStartMinimumProperty =
            AvaloniaProperty.Register<EditorTopBarWithInputs, decimal>(nameof(ReadStartMinimum), defaultValue: 0m);

        public static readonly StyledProperty<decimal> ReadStartMaximumProperty =
            AvaloniaProperty.Register<EditorTopBarWithInputs, decimal>(nameof(ReadStartMaximum), defaultValue: 4294967295m);

        public static readonly StyledProperty<decimal> ReadCountMinimumProperty =
            AvaloniaProperty.Register<EditorTopBarWithInputs, decimal>(nameof(ReadCountMinimum), defaultValue: 0m);

        public static readonly StyledProperty<decimal> ReadCountMaximumProperty =
            AvaloniaProperty.Register<EditorTopBarWithInputs, decimal>(nameof(ReadCountMaximum), defaultValue: 65535m);

        public static readonly StyledProperty<decimal> ReadSizeMinimumProperty =
            AvaloniaProperty.Register<EditorTopBarWithInputs, decimal>(nameof(ReadSizeMinimum), defaultValue: 0m);

        public static readonly StyledProperty<decimal> ReadSizeMaximumProperty =
            AvaloniaProperty.Register<EditorTopBarWithInputs, decimal>(nameof(ReadSizeMaximum), defaultValue: 65535m);

        /// <summary>Minimum allowed value for the Read Start input.</summary>
        public decimal ReadStartMinimum
        {
            get => GetValue(ReadStartMinimumProperty);
            set => SetValue(ReadStartMinimumProperty, value);
        }

        /// <summary>Maximum allowed value for the Read Start input.</summary>
        public decimal ReadStartMaximum
        {
            get => GetValue(ReadStartMaximumProperty);
            set => SetValue(ReadStartMaximumProperty, value);
        }

        /// <summary>Minimum allowed value for the Read Count input.</summary>
        public decimal ReadCountMinimum
        {
            get => GetValue(ReadCountMinimumProperty);
            set => SetValue(ReadCountMinimumProperty, value);
        }

        /// <summary>Maximum allowed value for the Read Count input.</summary>
        public decimal ReadCountMaximum
        {
            get => GetValue(ReadCountMaximumProperty);
            set => SetValue(ReadCountMaximumProperty, value);
        }

        /// <summary>Minimum allowed value for the Read Size input.</summary>
        public decimal ReadSizeMinimum
        {
            get => GetValue(ReadSizeMinimumProperty);
            set => SetValue(ReadSizeMinimumProperty, value);
        }

        /// <summary>Maximum allowed value for the Read Size input.</summary>
        public decimal ReadSizeMaximum
        {
            get => GetValue(ReadSizeMaximumProperty);
            set => SetValue(ReadSizeMaximumProperty, value);
        }

        // -----------------------------------------------------------------
        // AutomationId overrides (for back-compat with pre-#701 E2E ids)
        // -----------------------------------------------------------------

        public static readonly StyledProperty<string> ReadStartAutomationIdProperty =
            AvaloniaProperty.Register<EditorTopBarWithInputs, string>(nameof(ReadStartAutomationId), defaultValue: "");

        public static readonly StyledProperty<string> ReadCountAutomationIdProperty =
            AvaloniaProperty.Register<EditorTopBarWithInputs, string>(nameof(ReadCountAutomationId), defaultValue: "");

        public static readonly StyledProperty<string> ReadSizeAutomationIdProperty =
            AvaloniaProperty.Register<EditorTopBarWithInputs, string>(nameof(ReadSizeAutomationId), defaultValue: "");

        public static readonly StyledProperty<string> ReloadAutomationIdProperty =
            AvaloniaProperty.Register<EditorTopBarWithInputs, string>(nameof(ReloadAutomationId), defaultValue: "");

        // -----------------------------------------------------------------
        // CLR accessors for the new styled properties.
        // -----------------------------------------------------------------

        /// <summary>Whether the Read Start slot (label + input) is visible.</summary>
        public bool ShowReadStart
        {
            get => GetValue(ShowReadStartProperty);
            set => SetValue(ShowReadStartProperty, value);
        }

        /// <summary>Whether the Read Count slot (label + input) is visible.</summary>
        public bool ShowReadCount
        {
            get => GetValue(ShowReadCountProperty);
            set => SetValue(ShowReadCountProperty, value);
        }

        /// <summary>Whether the Read Size slot (label + input) is visible.</summary>
        public bool ShowReadSize
        {
            get => GetValue(ShowReadSizeProperty);
            set => SetValue(ShowReadSizeProperty, value);
        }

        /// <summary>
        /// When false, the three NumericUpDown inputs render disabled
        /// (IsEnabled=false). Mirrors the pre-migration IsReadOnly /
        /// IsEnabled="False" pattern many editors shipped on their hand-rolled
        /// top-bar.
        /// </summary>
        public bool InputsEnabled
        {
            get => GetValue(InputsEnabledProperty);
            set => SetValue(InputsEnabledProperty, value);
        }

        /// <summary>Label text rendered to the left of the Read Start input.</summary>
        public string ReadStartLabel
        {
            get => GetValue(ReadStartLabelProperty);
            set => SetValue(ReadStartLabelProperty, value);
        }

        /// <summary>Label text rendered to the left of the Read Count input.</summary>
        public string ReadCountLabel
        {
            get => GetValue(ReadCountLabelProperty);
            set => SetValue(ReadCountLabelProperty, value);
        }

        /// <summary>Label text rendered to the left of the Read Size input.</summary>
        public string ReadSizeLabel
        {
            get => GetValue(ReadSizeLabelProperty);
            set => SetValue(ReadSizeLabelProperty, value);
        }

        /// <summary>
        /// Optional explicit AutomationId override for the Read Start input.
        /// When non-empty this is set on the inner NumericUpDown instead of
        /// the derived "{base}_ReadStart_Input" id, letting a migrated view
        /// keep its legacy selector (e.g. "SongInstrument_ReadStartAddress_Input").
        /// </summary>
        public string ReadStartAutomationId
        {
            get => GetValue(ReadStartAutomationIdProperty);
            set => SetValue(ReadStartAutomationIdProperty, value);
        }

        /// <summary>Explicit AutomationId override for the Read Count input.</summary>
        public string ReadCountAutomationId
        {
            get => GetValue(ReadCountAutomationIdProperty);
            set => SetValue(ReadCountAutomationIdProperty, value);
        }

        /// <summary>Explicit AutomationId override for the Read Size input.</summary>
        public string ReadSizeAutomationId
        {
            get => GetValue(ReadSizeAutomationIdProperty);
            set => SetValue(ReadSizeAutomationIdProperty, value);
        }

        /// <summary>Explicit AutomationId override for the Reload button.</summary>
        public string ReloadAutomationId
        {
            get => GetValue(ReloadAutomationIdProperty);
            set => SetValue(ReloadAutomationIdProperty, value);
        }

        // -----------------------------------------------------------------
        // Routed events
        // -----------------------------------------------------------------

        /// <summary>
        /// Routed event raised when the user clicks the Reload button. Hosts
        /// subscribe via <see cref="ReloadRequested"/> to refresh their list
        /// from the address/count/size currently in the inputs.
        /// </summary>
        public static readonly RoutedEvent<RoutedEventArgs> ReloadRequestedEvent =
            RoutedEvent.Register<EditorTopBarWithInputs, RoutedEventArgs>(
                nameof(ReloadRequested), RoutingStrategies.Bubble);

        /// <summary>
        /// Raised when the Reload button is clicked. Bubbles like a normal
        /// routed event so a parent view-host can handle it without having
        /// to wire each editor explicitly.
        /// </summary>
        public event System.EventHandler<RoutedEventArgs>? ReloadRequested
        {
            add => AddHandler(ReloadRequestedEvent, value);
            remove => RemoveHandler(ReloadRequestedEvent, value);
        }

        public EditorTopBarWithInputs()
        {
            InitializeComponent();

            ApplyAllVisibility();
            ApplyAllLabels();
            ApplyInputsEnabled();
            ApplyAllLimits();

            AttachedToVisualTree += (_, _) => PropagateInnerAutomationIds();
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);

            if (change.Property == ReadStartAutomationIdProperty
             || change.Property == ReadCountAutomationIdProperty
             || change.Property == ReadSizeAutomationIdProperty
             || change.Property == ReloadAutomationIdProperty)
            {
                PropagateInnerAutomationIds();
            }
            else if (change.Property == ShowReadStartProperty)
            {
                ApplySlotVisibility(ReadStartLabelBlock, ReadStartInput, ShowReadStart);
            }
            else if (change.Property == ShowReadCountProperty)
            {
                ApplySlotVisibility(ReadCountLabelBlock, ReadCountInput, ShowReadCount);
            }
            else if (change.Property == ShowReadSizeProperty)
            {
                ApplySlotVisibility(ReadSizeLabelBlock, ReadSizeInput, ShowReadSize);
            }
            else if (change.Property == InputsEnabledProperty)
            {
                ApplyInputsEnabled();
            }
            else if (change.Property == ReadStartLabelProperty)
            {
                if (ReadStartLabelBlock != null) ReadStartLabelBlock.Text = ReadStartLabel;
            }
            else if (change.Property == ReadCountLabelProperty)
            {
                if (ReadCountLabelBlock != null) ReadCountLabelBlock.Text = ReadCountLabel;
            }
            else if (change.Property == ReadSizeLabelProperty)
            {
                if (ReadSizeLabelBlock != null) ReadSizeLabelBlock.Text = ReadSizeLabel;
            }
            else if (change.Property == ReadStartMinimumProperty)
            {
                if (ReadStartInput != null) ReadStartInput.Minimum = ReadStartMinimum;
            }
            else if (change.Property == ReadStartMaximumProperty)
            {
                if (ReadStartInput != null) ReadStartInput.Maximum = ReadStartMaximum;
            }
            else if (change.Property == ReadCountMinimumProperty)
            {
                if (ReadCountInput != null) ReadCountInput.Minimum = ReadCountMinimum;
            }
            else if (change.Property == ReadCountMaximumProperty)
            {
                if (ReadCountInput != null) ReadCountInput.Maximum = ReadCountMaximum;
            }
            else if (change.Property == ReadSizeMinimumProperty)
            {
                if (ReadSizeInput != null) ReadSizeInput.Minimum = ReadSizeMinimum;
            }
            else if (change.Property == ReadSizeMaximumProperty)
            {
                if (ReadSizeInput != null) ReadSizeInput.Maximum = ReadSizeMaximum;
            }
        }

        void ApplyAllLimits()
        {
            // Push styled-property defaults onto the inner NumericUpDowns so
            // the initial render matches what XAML defined. Per-host overrides
            // flow through OnPropertyChanged.
            if (ReadStartInput != null)
            {
                ReadStartInput.Minimum = ReadStartMinimum;
                ReadStartInput.Maximum = ReadStartMaximum;
            }
            if (ReadCountInput != null)
            {
                ReadCountInput.Minimum = ReadCountMinimum;
                ReadCountInput.Maximum = ReadCountMaximum;
            }
            if (ReadSizeInput != null)
            {
                ReadSizeInput.Minimum = ReadSizeMinimum;
                ReadSizeInput.Maximum = ReadSizeMaximum;
            }
        }

        void ApplyAllVisibility()
        {
            ApplySlotVisibility(ReadStartLabelBlock, ReadStartInput, ShowReadStart);
            ApplySlotVisibility(ReadCountLabelBlock, ReadCountInput, ShowReadCount);
            ApplySlotVisibility(ReadSizeLabelBlock, ReadSizeInput, ShowReadSize);
        }

        void ApplyAllLabels()
        {
            if (ReadStartLabelBlock != null) ReadStartLabelBlock.Text = ReadStartLabel;
            if (ReadCountLabelBlock != null) ReadCountLabelBlock.Text = ReadCountLabel;
            if (ReadSizeLabelBlock != null) ReadSizeLabelBlock.Text = ReadSizeLabel;
        }

        void ApplyInputsEnabled()
        {
            // Mirrors pre-migration `IsEnabled="False"` UX — the inputs render
            // disabled but the slot stays visible (use ShowReadX to fully hide).
            if (ReadStartInput != null) ReadStartInput.IsEnabled = InputsEnabled;
            if (ReadCountInput != null) ReadCountInput.IsEnabled = InputsEnabled;
            if (ReadSizeInput != null) ReadSizeInput.IsEnabled = InputsEnabled;
        }

        static void ApplySlotVisibility(Control? labelBlock, Control? inputBlock, bool visible)
        {
            // A slot is one label + one input. Hide both so the StackPanel
            // collapses them together (otherwise the orphaned partner leaves
            // a gap).
            if (labelBlock != null) labelBlock.IsVisible = visible;
            if (inputBlock != null) inputBlock.IsVisible = visible;
        }

        /// <summary>
        /// Current Read Start address as a uint. Round-trips through the inner
        /// NumericUpDown so the value reflects exactly what the user has typed.
        /// Setter clamps to the NumericUpDown's Minimum/Maximum bounds.
        /// </summary>
        public uint ReadStartAddress
        {
            get => (uint)(ReadStartInput?.Value ?? 0);
            set { if (ReadStartInput != null) ReadStartInput.Value = value; }
        }

        /// <summary>
        /// Current Read Count as an int. Round-trips through the inner
        /// NumericUpDown. Negative inner values clamp to 0 in the getter since
        /// a row count is never meaningfully negative.
        /// </summary>
        public int ReadCount
        {
            get
            {
                var v = ReadCountInput?.Value ?? 0;
                return v < 0 ? 0 : (int)v;
            }
            set { if (ReadCountInput != null) ReadCountInput.Value = value; }
        }

        /// <summary>
        /// Current Read Size (per-entry byte length) as an int. Round-trips
        /// through the inner NumericUpDown. Negative inner values clamp to 0.
        /// </summary>
        public int ReadSize
        {
            get
            {
                var v = ReadSizeInput?.Value ?? 0;
                return v < 0 ? 0 : (int)v;
            }
            set { if (ReadSizeInput != null) ReadSizeInput.Value = value; }
        }

        void OnReloadClick(object? sender, RoutedEventArgs e)
        {
            RaiseEvent(new RoutedEventArgs(ReloadRequestedEvent));
        }

        /// <summary>
        /// Derive AutomationIds for the inner controls from the host's id.
        /// Explicit overrides take precedence so legacy E2E ids survive
        /// migration. Idempotent — strips a trailing "_TopBar" suffix so the
        /// derived ids stay stable across re-attaches.
        /// </summary>
        void PropagateInnerAutomationIds()
        {
            string hostId = AutomationProperties.GetAutomationId(this) ?? string.Empty;
            string baseId = hostId;
            if (baseId.EndsWith("_TopBar", StringComparison.Ordinal))
                baseId = baseId.Substring(0, baseId.Length - "_TopBar".Length);

            SetInnerId(ReadStartInput, ReadStartAutomationId, baseId, "_ReadStart_Input");
            SetInnerId(ReadCountInput, ReadCountAutomationId, baseId, "_ReadCount_Input");
            SetInnerId(ReadSizeInput, ReadSizeAutomationId, baseId, "_ReadSize_Input");
            SetInnerId(ReloadButton, ReloadAutomationId, baseId, "_Reload_Button");
        }

        static void SetInnerId(Control? target, string explicitId, string baseId, string suffix)
        {
            if (target == null) return;
            if (!string.IsNullOrEmpty(explicitId))
            {
                AutomationProperties.SetAutomationId(target, explicitId);
            }
            else if (!string.IsNullOrEmpty(baseId))
            {
                AutomationProperties.SetAutomationId(target, baseId + suffix);
            }
            else
            {
                // Host hasn't set an id and no explicit override → clear any
                // previously derived id so a re-attach can't leave a stale
                // value behind. Critical when multiple instances coexist.
                AutomationProperties.SetAutomationId(target, string.Empty);
            }
        }
    }
}
