// SPDX-License-Identifier: GPL-3.0-or-later
//
// Reusable editor top-bar (#649).
//
// Audit (2026-05-26): 40+ Avalonia editor views shipped their own ad-hoc
// top-bar with Start Address / Read Count / Size / Reload / (sometimes)
// Filter affordances — same intent, wildly inconsistent layouts (FontSize,
// label text, borders, placement). This control replaces those per-view
// inline implementations with a single layout.
//
// Layout summary
//   [Filter:][Filter input]  [Start Address:][hex value]  [Read Count:][n]
//   [Size:][n]                                                     [Reload]
//
// Slot defaults
//   Start Address / Read Count / Size / Reload are visible by default
//   (ShowStartAddress / ShowReadCount / ShowSize / ShowReload default = true).
//   The Filter input is hidden by default (ShowFilter default = false).
//
// Hidden slots collapse automatically — each migrated editor toggles
// whichever slots it doesn't need via the Show* styled properties; examples
// of how to opt in/out (NOT a binding contract — actual host usage may
// change over time, grep the Views\ tree for the canonical list):
//   * Show only Start Address + Read Count (no Size):  ShowSize="False"
//   * Show only Start Address + Size (no Read Count):  ShowReadCount="False"
//   * Surface the inline Filter TextBox:               ShowFilter="True"
//   * Keep all defaults → renders the full Start Address + Read Count +
//     Size + Reload row.
// The Reload button is right-aligned within the row regardless of which
// slots are visible (see the AXAML's Grid layout for how this is achieved).
//
// AutomationId mapping
//   Hosts set ONE AutomationId on the EditorTopBar (e.g. "UnitEditor_TopBar")
//   and the control derives suffixed ids on the inner controls so E2E tests
//   can address them: {base}_StartAddress_Label, {base}_ReadCount_Label,
//   {base}_Size_Label, {base}_Filter_Input, {base}_Reload_Button.
//
//   To preserve legacy ids that pre-#649 tests still rely on, hosts may set
//   any of the *AutomationId styled properties — those override the derived
//   id wholesale and let the migration keep existing selectors working.
using System;
using global::Avalonia;
using global::Avalonia.Automation;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;

namespace FEBuilderGBA.Avalonia.Controls
{
    /// <summary>
    /// Routed event args raised when the inline Filter TextBox text changes.
    /// </summary>
    public class EditorTopBarFilterChangedEventArgs : RoutedEventArgs
    {
        public string NewText { get; }
        public string OldText { get; }
        public EditorTopBarFilterChangedEventArgs(RoutedEvent routedEvent, string oldText, string newText)
            : base(routedEvent)
        {
            OldText = oldText ?? string.Empty;
            NewText = newText ?? string.Empty;
        }
    }

    /// <summary>
    /// Unified editor top-bar — see file header for design notes and audit
    /// scope (#649).
    /// </summary>
    public partial class EditorTopBar : UserControl
    {
        // -----------------------------------------------------------------
        // Display text styled properties
        // -----------------------------------------------------------------

        public static readonly StyledProperty<string> StartAddressTextProperty =
            AvaloniaProperty.Register<EditorTopBar, string>(nameof(StartAddressText), defaultValue: "");

        public static readonly StyledProperty<string> ReadCountTextProperty =
            AvaloniaProperty.Register<EditorTopBar, string>(nameof(ReadCountText), defaultValue: "");

        public static readonly StyledProperty<string> SizeTextProperty =
            AvaloniaProperty.Register<EditorTopBar, string>(nameof(SizeText), defaultValue: "");

        public static readonly StyledProperty<string> FilterTextProperty =
            AvaloniaProperty.Register<EditorTopBar, string>(
                nameof(FilterText), defaultValue: "",
                defaultBindingMode: global::Avalonia.Data.BindingMode.TwoWay);

        // -----------------------------------------------------------------
        // Label-text styled properties (let i18n callers override)
        // -----------------------------------------------------------------

        public static readonly StyledProperty<string> StartAddressLabelProperty =
            AvaloniaProperty.Register<EditorTopBar, string>(nameof(StartAddressLabel), defaultValue: "Start Address:");

        public static readonly StyledProperty<string> ReadCountLabelProperty =
            AvaloniaProperty.Register<EditorTopBar, string>(nameof(ReadCountLabel), defaultValue: "Read Count:");

        public static readonly StyledProperty<string> SizeLabelProperty =
            AvaloniaProperty.Register<EditorTopBar, string>(nameof(SizeLabel), defaultValue: "Size:");

        public static readonly StyledProperty<string> FilterLabelProperty =
            AvaloniaProperty.Register<EditorTopBar, string>(nameof(FilterLabel), defaultValue: "Filter:");

        public static readonly StyledProperty<string> ReloadButtonTextProperty =
            AvaloniaProperty.Register<EditorTopBar, string>(nameof(ReloadButtonText), defaultValue: "Reload");

        // -----------------------------------------------------------------
        // Slot-visibility styled properties
        // -----------------------------------------------------------------

        public static readonly StyledProperty<bool> ShowStartAddressProperty =
            AvaloniaProperty.Register<EditorTopBar, bool>(nameof(ShowStartAddress), defaultValue: true);

        public static readonly StyledProperty<bool> ShowReadCountProperty =
            AvaloniaProperty.Register<EditorTopBar, bool>(nameof(ShowReadCount), defaultValue: true);

        public static readonly StyledProperty<bool> ShowSizeProperty =
            AvaloniaProperty.Register<EditorTopBar, bool>(nameof(ShowSize), defaultValue: true);

        public static readonly StyledProperty<bool> ShowFilterProperty =
            AvaloniaProperty.Register<EditorTopBar, bool>(nameof(ShowFilter), defaultValue: false);

        public static readonly StyledProperty<bool> ShowReloadProperty =
            AvaloniaProperty.Register<EditorTopBar, bool>(nameof(ShowReload), defaultValue: true);

        // -----------------------------------------------------------------
        // AutomationId overrides (for back-compat with pre-#649 E2E ids)
        // -----------------------------------------------------------------

        public static readonly StyledProperty<string> StartAddressAutomationIdProperty =
            AvaloniaProperty.Register<EditorTopBar, string>(nameof(StartAddressAutomationId), defaultValue: "");

        public static readonly StyledProperty<string> ReadCountAutomationIdProperty =
            AvaloniaProperty.Register<EditorTopBar, string>(nameof(ReadCountAutomationId), defaultValue: "");

        public static readonly StyledProperty<string> SizeAutomationIdProperty =
            AvaloniaProperty.Register<EditorTopBar, string>(nameof(SizeAutomationId), defaultValue: "");

        public static readonly StyledProperty<string> FilterAutomationIdProperty =
            AvaloniaProperty.Register<EditorTopBar, string>(nameof(FilterAutomationId), defaultValue: "");

        public static readonly StyledProperty<string> ReloadAutomationIdProperty =
            AvaloniaProperty.Register<EditorTopBar, string>(nameof(ReloadAutomationId), defaultValue: "");

        // -----------------------------------------------------------------
        // Clr accessors
        // -----------------------------------------------------------------

        /// <summary>Display text for the Start Address slot (typically a hex string).</summary>
        public string StartAddressText
        {
            get => GetValue(StartAddressTextProperty);
            set => SetValue(StartAddressTextProperty, value);
        }

        /// <summary>Display text for the Read Count slot.</summary>
        public string ReadCountText
        {
            get => GetValue(ReadCountTextProperty);
            set => SetValue(ReadCountTextProperty, value);
        }

        /// <summary>Display text for the Size slot.</summary>
        public string SizeText
        {
            get => GetValue(SizeTextProperty);
            set => SetValue(SizeTextProperty, value);
        }

        /// <summary>Two-way bound text for the inline Filter TextBox.</summary>
        public string FilterText
        {
            get => GetValue(FilterTextProperty);
            set => SetValue(FilterTextProperty, value);
        }

        /// <summary>Label text rendered to the left of the Start Address value.</summary>
        public string StartAddressLabel
        {
            get => GetValue(StartAddressLabelProperty);
            set => SetValue(StartAddressLabelProperty, value);
        }

        /// <summary>Label text rendered to the left of the Read Count value.</summary>
        public string ReadCountLabel
        {
            get => GetValue(ReadCountLabelProperty);
            set => SetValue(ReadCountLabelProperty, value);
        }

        /// <summary>Label text rendered to the left of the Size value.</summary>
        public string SizeLabel
        {
            get => GetValue(SizeLabelProperty);
            set => SetValue(SizeLabelProperty, value);
        }

        /// <summary>Label text rendered to the left of the Filter input.</summary>
        public string FilterLabel
        {
            get => GetValue(FilterLabelProperty);
            set => SetValue(FilterLabelProperty, value);
        }

        /// <summary>Caption of the Reload button.</summary>
        public string ReloadButtonText
        {
            get => GetValue(ReloadButtonTextProperty);
            set => SetValue(ReloadButtonTextProperty, value);
        }

        /// <summary>Whether the Start Address slot is visible.</summary>
        public bool ShowStartAddress
        {
            get => GetValue(ShowStartAddressProperty);
            set => SetValue(ShowStartAddressProperty, value);
        }

        /// <summary>Whether the Read Count slot is visible.</summary>
        public bool ShowReadCount
        {
            get => GetValue(ShowReadCountProperty);
            set => SetValue(ShowReadCountProperty, value);
        }

        /// <summary>Whether the Size slot is visible.</summary>
        public bool ShowSize
        {
            get => GetValue(ShowSizeProperty);
            set => SetValue(ShowSizeProperty, value);
        }

        /// <summary>Whether the inline Filter input is visible.</summary>
        public bool ShowFilter
        {
            get => GetValue(ShowFilterProperty);
            set => SetValue(ShowFilterProperty, value);
        }

        /// <summary>Whether the Reload button is visible.</summary>
        public bool ShowReload
        {
            get => GetValue(ShowReloadProperty);
            set => SetValue(ShowReloadProperty, value);
        }

        /// <summary>
        /// Optional explicit AutomationId override for the Start Address value
        /// label. When non-empty this value is set on the inner control instead
        /// of the derived "{base}_StartAddress_Label" id. Lets a migrated view
        /// keep its legacy AutomationId so existing E2E selectors don't break.
        /// </summary>
        public string StartAddressAutomationId
        {
            get => GetValue(StartAddressAutomationIdProperty);
            set => SetValue(StartAddressAutomationIdProperty, value);
        }

        /// <summary>Explicit AutomationId override for the Read Count value label.</summary>
        public string ReadCountAutomationId
        {
            get => GetValue(ReadCountAutomationIdProperty);
            set => SetValue(ReadCountAutomationIdProperty, value);
        }

        /// <summary>Explicit AutomationId override for the Size value label.</summary>
        public string SizeAutomationId
        {
            get => GetValue(SizeAutomationIdProperty);
            set => SetValue(SizeAutomationIdProperty, value);
        }

        /// <summary>Explicit AutomationId override for the Filter TextBox.</summary>
        public string FilterAutomationId
        {
            get => GetValue(FilterAutomationIdProperty);
            set => SetValue(FilterAutomationIdProperty, value);
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

        public static readonly RoutedEvent<RoutedEventArgs> ReloadRequestedEvent =
            RoutedEvent.Register<EditorTopBar, RoutedEventArgs>(
                nameof(ReloadRequested), RoutingStrategies.Bubble);

        public static readonly RoutedEvent<EditorTopBarFilterChangedEventArgs> FilterTextChangedEvent =
            RoutedEvent.Register<EditorTopBar, EditorTopBarFilterChangedEventArgs>(
                nameof(FilterTextChanged), RoutingStrategies.Bubble);

        /// <summary>Raised when the user clicks the Reload button.</summary>
        public event EventHandler<RoutedEventArgs>? ReloadRequested
        {
            add => AddHandler(ReloadRequestedEvent, value);
            remove => RemoveHandler(ReloadRequestedEvent, value);
        }

        /// <summary>
        /// Raised whenever <see cref="FilterText"/> changes — either via host
        /// programmatic update or user typing in the inline TextBox.
        /// </summary>
        public event EventHandler<EditorTopBarFilterChangedEventArgs>? FilterTextChanged
        {
            add => AddHandler(FilterTextChangedEvent, value);
            remove => RemoveHandler(FilterTextChangedEvent, value);
        }

        // Suppress recursive callbacks when FilterText <-> TextBox.Text sync.
        bool _filterSyncing;

        public EditorTopBar()
        {
            InitializeComponent();

            if (FilterInput != null)
            {
                // Seed the inner TextBox value so it always matches the
                // styled-property default (mirrors IdFieldControl's
                // initial-sync pattern, #498/#502/#509).
                FilterInput.Text = FilterText ?? string.Empty;
                FilterInput.TextChanged += OnFilterInputTextChanged;
            }

            ApplyAllVisibility();
            ApplyAllLabels();
            ApplyAllValues();

            AttachedToVisualTree += (_, _) => PropagateInnerAutomationIds();
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);

            // Value labels
            if (change.Property == StartAddressTextProperty)
            {
                if (StartAddressValueBlock != null) StartAddressValueBlock.Text = StartAddressText;
            }
            else if (change.Property == ReadCountTextProperty)
            {
                if (ReadCountValueBlock != null) ReadCountValueBlock.Text = ReadCountText;
            }
            else if (change.Property == SizeTextProperty)
            {
                if (SizeValueBlock != null) SizeValueBlock.Text = SizeText;
            }
            else if (change.Property == FilterTextProperty)
            {
                string oldText = change.GetOldValue<string>() ?? string.Empty;
                string newText = change.GetNewValue<string>() ?? string.Empty;
                if (!_filterSyncing && FilterInput != null && FilterInput.Text != newText)
                {
                    _filterSyncing = true;
                    try { FilterInput.Text = newText; }
                    finally { _filterSyncing = false; }
                }
                RaiseEvent(new EditorTopBarFilterChangedEventArgs(
                    FilterTextChangedEvent, oldText, newText));
            }
            // Label text
            else if (change.Property == StartAddressLabelProperty)
            {
                if (StartAddressLabelBlock != null) StartAddressLabelBlock.Text = StartAddressLabel;
            }
            else if (change.Property == ReadCountLabelProperty)
            {
                if (ReadCountLabelBlock != null) ReadCountLabelBlock.Text = ReadCountLabel;
            }
            else if (change.Property == SizeLabelProperty)
            {
                if (SizeLabelBlock != null) SizeLabelBlock.Text = SizeLabel;
            }
            else if (change.Property == FilterLabelProperty)
            {
                if (FilterLabelBlock != null) FilterLabelBlock.Text = FilterLabel;
            }
            else if (change.Property == ReloadButtonTextProperty)
            {
                if (ReloadButton != null) ReloadButton.Content = ReloadButtonText;
            }
            // Slot visibility — re-propagate ids because visibility gates
            // whether the derived "{base}_{slot}_Label" id is applied (see
            // PropagateInnerAutomationIds: hidden slots get an empty id so the
            // host view can reuse the same suffix on another visible control).
            else if (change.Property == ShowStartAddressProperty)
            {
                if (StartAddressSlot != null) StartAddressSlot.IsVisible = ShowStartAddress;
                PropagateInnerAutomationIds();
            }
            else if (change.Property == ShowReadCountProperty)
            {
                if (ReadCountSlot != null) ReadCountSlot.IsVisible = ShowReadCount;
                PropagateInnerAutomationIds();
            }
            else if (change.Property == ShowSizeProperty)
            {
                if (SizeSlot != null) SizeSlot.IsVisible = ShowSize;
                PropagateInnerAutomationIds();
            }
            else if (change.Property == ShowFilterProperty)
            {
                if (FilterSlot != null) FilterSlot.IsVisible = ShowFilter;
                PropagateInnerAutomationIds();
            }
            else if (change.Property == ShowReloadProperty)
            {
                if (ReloadButton != null) ReloadButton.IsVisible = ShowReload;
                PropagateInnerAutomationIds();
            }
            // AutomationId overrides
            else if (change.Property == StartAddressAutomationIdProperty
                  || change.Property == ReadCountAutomationIdProperty
                  || change.Property == SizeAutomationIdProperty
                  || change.Property == FilterAutomationIdProperty
                  || change.Property == ReloadAutomationIdProperty)
            {
                PropagateInnerAutomationIds();
            }
        }

        void ApplyAllValues()
        {
            if (StartAddressValueBlock != null) StartAddressValueBlock.Text = StartAddressText;
            if (ReadCountValueBlock != null) ReadCountValueBlock.Text = ReadCountText;
            if (SizeValueBlock != null) SizeValueBlock.Text = SizeText;
        }

        void ApplyAllLabels()
        {
            if (StartAddressLabelBlock != null) StartAddressLabelBlock.Text = StartAddressLabel;
            if (ReadCountLabelBlock != null) ReadCountLabelBlock.Text = ReadCountLabel;
            if (SizeLabelBlock != null) SizeLabelBlock.Text = SizeLabel;
            if (FilterLabelBlock != null) FilterLabelBlock.Text = FilterLabel;
            if (ReloadButton != null) ReloadButton.Content = ReloadButtonText;
        }

        void ApplyAllVisibility()
        {
            if (StartAddressSlot != null) StartAddressSlot.IsVisible = ShowStartAddress;
            if (ReadCountSlot != null) ReadCountSlot.IsVisible = ShowReadCount;
            if (SizeSlot != null) SizeSlot.IsVisible = ShowSize;
            if (FilterSlot != null) FilterSlot.IsVisible = ShowFilter;
            if (ReloadButton != null) ReloadButton.IsVisible = ShowReload;
        }

        /// <summary>
        /// Derive AutomationIds for the inner controls from the host's id.
        /// Explicit overrides take precedence so legacy E2E ids survive
        /// migration (UnitEditor_Reload_Button, ItemFE6_Filter_Input, etc.).
        ///
        /// Hidden slots intentionally do NOT receive a derived id so the host
        /// view can reuse the same suffix elsewhere without colliding (e.g.
        /// UnitEditorView sets ShowSize=false and then attaches
        /// "UnitEditor_Size_Label" to its own Selected-Address Size label
        /// further down the view; auto-deriving "UnitEditor_Size_Label" on
        /// the hidden TopBar block too would trip the
        /// "No duplicate AutomationIds" test).
        /// Hosts that explicitly set the *AutomationId override still get
        /// that id applied — the visibility gate only suppresses *derivation*.
        /// </summary>
        void PropagateInnerAutomationIds()
        {
            string hostId = AutomationProperties.GetAutomationId(this) ?? string.Empty;
            string baseId = hostId;
            // Idempotent: strip a trailing "_TopBar" suffix if the host id
            // already encodes it, so the derived ids stay stable across
            // re-attaches (mirrors IdFieldControl's "_Input/_Control" strip).
            if (baseId.EndsWith("_TopBar", StringComparison.Ordinal))
                baseId = baseId.Substring(0, baseId.Length - "_TopBar".Length);

            SetInnerId(StartAddressValueBlock, StartAddressAutomationId,
                       baseId, "_StartAddress_Label", ShowStartAddress);
            SetInnerId(ReadCountValueBlock, ReadCountAutomationId,
                       baseId, "_ReadCount_Label", ShowReadCount);
            SetInnerId(SizeValueBlock, SizeAutomationId,
                       baseId, "_Size_Label", ShowSize);
            SetInnerId(FilterInput, FilterAutomationId,
                       baseId, "_Filter_Input", ShowFilter);
            SetInnerId(ReloadButton, ReloadAutomationId,
                       baseId, "_Reload_Button", ShowReload);
        }

        static void SetInnerId(Control? target, string explicitId, string baseId, string suffix, bool slotVisible)
        {
            if (target == null) return;
            if (!string.IsNullOrEmpty(explicitId))
            {
                // Explicit override always wins — host is asserting they want
                // this exact id on the inner control regardless of visibility.
                AutomationProperties.SetAutomationId(target, explicitId);
            }
            else if (slotVisible && !string.IsNullOrEmpty(baseId))
            {
                AutomationProperties.SetAutomationId(target, baseId + suffix);
            }
            else
            {
                // Hidden slot + no explicit override → clear any previously
                // derived id so a re-attach can't leave a stale value behind.
                AutomationProperties.SetAutomationId(target, string.Empty);
            }
        }

        void OnReloadClick(object? sender, RoutedEventArgs e)
        {
            RaiseEvent(new RoutedEventArgs(ReloadRequestedEvent));
        }

        void OnFilterInputTextChanged(object? sender, TextChangedEventArgs e)
        {
            if (_filterSyncing) return;
            string newText = FilterInput?.Text ?? string.Empty;
            _filterSyncing = true;
            try { FilterText = newText; }
            finally { _filterSyncing = false; }
            // OnPropertyChanged(FilterTextProperty) raises the routed event.
        }
    }
}
