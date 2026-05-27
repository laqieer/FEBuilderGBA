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
// API. Per-editor migration is deferred to a separate follow-up issue because
// investigation revealed each editor has unique additional controls
// (FilterCombo, ChangeType combo, FilterBox, ListExpandButton, etc.) in its
// top strip that don't fit a uniform replacement.
using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;

namespace FEBuilderGBA.Avalonia.Controls
{
    /// <summary>
    /// Editable variant of EditorTopBar for editors that allow the user
    /// to type a Read Start / Read Count / Read Size and trigger Reload.
    ///
    /// <para>Currently used by no editor — added in PR for #701 Slice A
    /// to establish the API. Per-editor migration is tracked in a follow-up
    /// issue once each editor's additional controls (FilterCombo, ChangeType,
    /// FilterBox, ListExpandButton, etc.) have been audited.</para>
    /// </summary>
    public partial class EditorTopBarWithInputs : UserControl
    {
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
    }
}
