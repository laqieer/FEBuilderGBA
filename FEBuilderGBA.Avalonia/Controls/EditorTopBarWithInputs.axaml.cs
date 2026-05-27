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
