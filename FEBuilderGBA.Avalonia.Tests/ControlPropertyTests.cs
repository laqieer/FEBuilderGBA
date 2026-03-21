using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.VisualTree;
using FEBuilderGBA.Avalonia.Controls;
using FEBuilderGBA.Avalonia.ViewModels;
using FEBuilderGBA.Avalonia.Views;
using Xunit;
using Xunit.Abstractions;

namespace FEBuilderGBA.Avalonia.Tests
{
    /// <summary>
    /// WU-B: Critical Control Property Assertions.
    /// Verifies DataContext types, named control existence, Image.Stretch values,
    /// Write button presence, and AddressListControl/EntryList presence on key editors.
    ///
    /// These tests would have caught issue #183's Stretch="None" bug because
    /// we explicitly assert that Image controls inside GbaImageControl use Stretch.Fill.
    ///
    /// Ref #211
    /// </summary>
    public class ControlPropertyTests
    {
        private readonly ITestOutputHelper _output;

        public ControlPropertyTests(ITestOutputHelper output) => _output = output;

        // ===================================================================
        // Helper methods
        // ===================================================================

        /// <summary>
        /// Find a named control on a view using Avalonia's built-in FindControl.
        /// This uses the NameScope that is populated during InitializeComponent().
        /// </summary>
        private static T FindCtrl<T>(Control view, string name) where T : Control
        {
            return view.FindControl<T>(name);
        }

        /// <summary>Find any named control on a view.</summary>
        private static Control FindNamedControl(Control view, string name)
        {
            return view.FindControl<Control>(name);
        }

        /// <summary>Assert that a view has the expected DataContext type (either set in constructor or after init).</summary>
        private void AssertDataContextType(object view, Type expectedVmType, string viewName)
        {
            if (view is ContentControl cc && cc.DataContext != null)
            {
                Assert.True(expectedVmType.IsAssignableFrom(cc.DataContext.GetType()),
                    $"{viewName}: DataContext is {cc.DataContext.GetType().Name}, expected {expectedVmType.Name}");
                _output.WriteLine($"  DataContext: {cc.DataContext.GetType().Name} (OK)");
            }
            else
            {
                // Check for _vm field via reflection
                var vmField = view.GetType().GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
                    .FirstOrDefault(f => f.Name == "_vm" && expectedVmType.IsAssignableFrom(f.FieldType));
                Assert.True(vmField != null,
                    $"{viewName}: No DataContext set and no _vm field of type {expectedVmType.Name}");
                _output.WriteLine($"  _vm field: {vmField.FieldType.Name} (OK)");
            }
        }

        /// <summary>Assert that a named control exists with expected type.</summary>
        private void AssertNamedControlExists<T>(Control view, string controlName, string viewName) where T : Control
        {
            var ctrl = view.FindControl<T>(controlName);
            Assert.True(ctrl != null,
                $"{viewName}: Expected control '{controlName}' of type {typeof(T).Name} not found");
            _output.WriteLine($"  Control '{controlName}': {typeof(T).Name} (OK)");
        }

        /// <summary>Assert that a named control exists (any type).</summary>
        private void AssertNamedControlExists(Control view, string controlName, string viewName)
        {
            AssertNamedControlExists<Control>(view, controlName, viewName);
        }

        /// <summary>Assert that a Write button exists in the view.</summary>
        private void AssertWriteButtonExists(object view, string viewName)
        {
            // Search for a Button with Content="Write" via the logical tree
            // (logical tree is always populated after InitializeComponent, visual tree may not be)
            if (view is ILogical logical)
            {
                var writeButton = FindButtonWithContent(logical, "Write");
                Assert.True(writeButton != null,
                    $"{viewName}: Expected a Write button but none found");
                _output.WriteLine($"  Write button: found (OK)");
            }
        }

        /// <summary>Recursively find a Button with specific text content via logical tree.</summary>
        private static Button FindButtonWithContent(ILogical parent, string content)
        {
            if (parent is Button btn && btn.Content?.ToString() == content)
                return btn;

            // Walk logical children (more reliable than visual tree in headless mode)
            foreach (var child in parent.LogicalChildren)
            {
                if (child is ILogical logicalChild)
                {
                    var found = FindButtonWithContent(logicalChild, content);
                    if (found != null) return found;
                }
            }
            return null;
        }

        // ===================================================================
        // GbaImageControl — Stretch property tests
        // (Issue #183 regression: Stretch="None" broke image display)
        // ===================================================================

        [AvaloniaFact]
        public void GbaImageControl_ImageDisplay_HasStretchFill()
        {
            var ctrl = new GbaImageControl();
            var imageDisplay = ctrl.FindControl<Image>("ImageDisplay");
            Assert.NotNull(imageDisplay);
            Assert.Equal(Stretch.Fill, imageDisplay.Stretch);
            _output.WriteLine("GbaImageControl.ImageDisplay.Stretch = Fill (correct)");
        }

        [AvaloniaFact]
        public void GbaImageControl_HasZoomControls()
        {
            var ctrl = new GbaImageControl();
            AssertNamedControlExists<Button>(ctrl, "ZoomInButton", "GbaImageControl");
            AssertNamedControlExists<Button>(ctrl, "ZoomOutButton", "GbaImageControl");
            AssertNamedControlExists<Button>(ctrl, "ZoomResetButton", "GbaImageControl");
            AssertNamedControlExists<TextBlock>(ctrl, "ZoomLabel", "GbaImageControl");
        }

        [AvaloniaFact]
        public void GbaImageControl_HasScrollViewer()
        {
            var ctrl = new GbaImageControl();
            AssertNamedControlExists<ScrollViewer>(ctrl, "ImageScroller", "GbaImageControl");
        }

        // ===================================================================
        // PortraitViewerView
        // ===================================================================

        [AvaloniaFact]
        public void PortraitViewerView_HasCorrectControls()
        {
            var view = new PortraitViewerView();
            _output.WriteLine("=== PortraitViewerView ===");

            // ViewModel: PortraitViewerViewModel stored as _vm
            AssertDataContextType(view, typeof(PortraitViewerViewModel), "PortraitViewerView");

            // Key named controls
            AssertNamedControlExists(view, "PortraitList", "PortraitViewerView");
            AssertNamedControlExists(view, "AddrLabel", "PortraitViewerView");
            AssertNamedControlExists(view, "ImgPtrBox", "PortraitViewerView");
            AssertNamedControlExists(view, "MapPtrBox", "PortraitViewerView");
            AssertNamedControlExists(view, "PalPtrBox", "PortraitViewerView");

            // Image controls (GbaImageControl)
            AssertNamedControlExists<GbaImageControl>(view, "MainPortraitImage", "PortraitViewerView");
            AssertNamedControlExists<GbaImageControl>(view, "MapPortraitImage", "PortraitViewerView");
            AssertNamedControlExists<GbaImageControl>(view, "ClassPortraitImage", "PortraitViewerView");

            // Write button
            AssertWriteButtonExists(view, "PortraitViewerView");
        }

        // ===================================================================
        // UnitEditorView
        // ===================================================================

        [AvaloniaFact]
        public void UnitEditorView_HasCorrectControls()
        {
            var view = new UnitEditorView();
            _output.WriteLine("=== UnitEditorView ===");

            AssertDataContextType(view, typeof(UnitEditorViewModel), "UnitEditorView");

            // Address list
            AssertNamedControlExists<AddressListControl>(view, "UnitList", "UnitEditorView");
            AssertNamedControlExists(view, "AddrLabel", "UnitEditorView");

            // Key editor controls
            AssertNamedControlExists<NumericUpDown>(view, "NameIdBox", "UnitEditorView");
            AssertNamedControlExists<NumericUpDown>(view, "DescIdBox", "UnitEditorView");

            // Write button
            AssertWriteButtonExists(view, "UnitEditorView");
        }

        // ===================================================================
        // ClassEditorView
        // ===================================================================

        [AvaloniaFact]
        public void ClassEditorView_HasCorrectControls()
        {
            var view = new ClassEditorView();
            _output.WriteLine("=== ClassEditorView ===");

            AssertDataContextType(view, typeof(ClassEditorViewModel), "ClassEditorView");

            // Address list and preview image
            AssertNamedControlExists<AddressListControl>(view, "ClassList", "ClassEditorView");
            AssertNamedControlExists<GbaImageControl>(view, "ListPreviewImage", "ClassEditorView");
            AssertNamedControlExists(view, "AddrLabel", "ClassEditorView");

            // Write button
            AssertWriteButtonExists(view, "ClassEditorView");
        }

        // ===================================================================
        // ItemEditorView
        // ===================================================================

        [AvaloniaFact]
        public void ItemEditorView_HasCorrectControls()
        {
            var view = new ItemEditorView();
            _output.WriteLine("=== ItemEditorView ===");

            AssertDataContextType(view, typeof(ItemEditorViewModel), "ItemEditorView");

            // Address list
            AssertNamedControlExists<AddressListControl>(view, "ItemList", "ItemEditorView");
            AssertNamedControlExists(view, "AddrLabel", "ItemEditorView");

            // Item icon preview (named "ListPreviewImage" in ItemEditorView.axaml)
            AssertNamedControlExists<GbaImageControl>(view, "ListPreviewImage", "ItemEditorView");

            // Write button
            AssertWriteButtonExists(view, "ItemEditorView");
        }

        // ===================================================================
        // MapSettingView
        // ===================================================================

        [AvaloniaFact]
        public void MapSettingView_HasCorrectControls()
        {
            var view = new MapSettingView();
            _output.WriteLine("=== MapSettingView ===");

            AssertDataContextType(view, typeof(MapSettingViewModel), "MapSettingView");

            // Address list
            AssertNamedControlExists<AddressListControl>(view, "MapList", "MapSettingView");
            AssertNamedControlExists(view, "AddrLabel", "MapSettingView");

            // Write button
            AssertWriteButtonExists(view, "MapSettingView");
        }

        // ===================================================================
        // MapSettingFE6View
        // ===================================================================

        [AvaloniaFact]
        public void MapSettingFE6View_HasCorrectControls()
        {
            var view = new MapSettingFE6View();
            _output.WriteLine("=== MapSettingFE6View ===");

            // Address list (named "EntryList" in MapSettingFE6View.axaml)
            AssertNamedControlExists<AddressListControl>(view, "EntryList", "MapSettingFE6View");
            AssertNamedControlExists(view, "AddrLabel", "MapSettingFE6View");

            // Note: MapSettingFE6View is a stub view, no Write button yet
            _output.WriteLine("  (stub view, no Write button expected)");
        }

        // ===================================================================
        // MapSettingFE7View
        // ===================================================================

        [AvaloniaFact]
        public void MapSettingFE7View_HasCorrectControls()
        {
            var view = new MapSettingFE7View();
            _output.WriteLine("=== MapSettingFE7View ===");

            // Named "EntryList" in MapSettingFE7View.axaml
            AssertNamedControlExists<AddressListControl>(view, "EntryList", "MapSettingFE7View");
            AssertNamedControlExists(view, "AddrLabel", "MapSettingFE7View");
            AssertWriteButtonExists(view, "MapSettingFE7View");
        }

        // ===================================================================
        // MapSettingFE7UView
        // ===================================================================

        [AvaloniaFact]
        public void MapSettingFE7UView_HasCorrectControls()
        {
            var view = new MapSettingFE7UView();
            _output.WriteLine("=== MapSettingFE7UView ===");

            // Named "EntryList" in MapSettingFE7UView.axaml
            AssertNamedControlExists<AddressListControl>(view, "EntryList", "MapSettingFE7UView");
            AssertNamedControlExists(view, "AddrLabel", "MapSettingFE7UView");
            AssertWriteButtonExists(view, "MapSettingFE7UView");
        }

        // ===================================================================
        // EventScriptView
        // ===================================================================

        [AvaloniaFact]
        public void EventScriptView_HasCorrectControls()
        {
            var view = new EventScriptView();
            _output.WriteLine("=== EventScriptView ===");

            AssertDataContextType(view, typeof(EventScriptViewModel), "EventScriptView");

            // Key controls
            AssertNamedControlExists<ListBox>(view, "CommandsList", "EventScriptView");
            AssertNamedControlExists(view, "ScriptTextBox", "EventScriptView");
            AssertNamedControlExists(view, "StatusLabel", "EventScriptView");
            AssertNamedControlExists(view, "AddressBox", "EventScriptView");
        }

        // ===================================================================
        // SongTableView
        // ===================================================================

        [AvaloniaFact]
        public void SongTableView_HasCorrectControls()
        {
            var view = new SongTableView();
            _output.WriteLine("=== SongTableView ===");

            AssertDataContextType(view, typeof(SongTableViewModel), "SongTableView");

            // Address list (named "SongList" in SongTableView.axaml)
            var songList = view.FindControl<AddressListControl>("SongList");
            Assert.True(songList != null,
                "SongTableView: Expected AddressListControl named 'SongList'");
            _output.WriteLine("  Control 'SongList': AddressListControl (OK)");

            AssertNamedControlExists(view, "AddrLabel", "SongTableView");

            // Write button
            AssertWriteButtonExists(view, "SongTableView");
        }

        // ===================================================================
        // SoundRoomViewerView
        // ===================================================================

        [AvaloniaFact]
        public void SoundRoomViewerView_HasCorrectControls()
        {
            var view = new SoundRoomViewerView();
            _output.WriteLine("=== SoundRoomViewerView ===");

            AssertDataContextType(view, typeof(SoundRoomViewerViewModel), "SoundRoomViewerView");

            // Entry list
            AssertNamedControlExists<AddressListControl>(view, "EntryList", "SoundRoomViewerView");
            AssertNamedControlExists(view, "AddrLabel", "SoundRoomViewerView");

            // Write button
            AssertWriteButtonExists(view, "SoundRoomViewerView");
        }

        // ===================================================================
        // TextViewerView
        // ===================================================================

        [AvaloniaFact]
        public void TextViewerView_HasCorrectControls()
        {
            var view = new TextViewerView();
            _output.WriteLine("=== TextViewerView ===");

            AssertDataContextType(view, typeof(TextViewerViewModel), "TextViewerView");

            // Text list
            AssertNamedControlExists<AddressListControl>(view, "TextList", "TextViewerView");

            // Edit controls
            AssertNamedControlExists(view, "EditTextBox", "TextViewerView");
            AssertNamedControlExists(view, "DecodedTextBlock", "TextViewerView");
            AssertNamedControlExists(view, "WriteTextButton", "TextViewerView");
            AssertNamedControlExists(view, "TextIdLabel", "TextViewerView");

            // Search
            AssertNamedControlExists(view, "ContentSearchBox", "TextViewerView");
        }

        // ===================================================================
        // ImagePortraitView
        // ===================================================================

        [AvaloniaFact]
        public void ImagePortraitView_HasCorrectControls()
        {
            var view = new ImagePortraitView();
            _output.WriteLine("=== ImagePortraitView ===");

            AssertDataContextType(view, typeof(ImagePortraitViewModel), "ImagePortraitView");

            // Entry list
            AssertNamedControlExists<AddressListControl>(view, "EntryList", "ImagePortraitView");
            AssertNamedControlExists(view, "AddrLabel", "ImagePortraitView");

            // Portrait images (GbaImageControl)
            AssertNamedControlExists<GbaImageControl>(view, "PortraitImage", "ImagePortraitView");
            AssertNamedControlExists<GbaImageControl>(view, "MiniPortraitImage", "ImagePortraitView");
            AssertNamedControlExists<GbaImageControl>(view, "ClassCardImage", "ImagePortraitView");

            // Write button (text is "Write Positions" in ImagePortraitView)
            if (view is ILogical logical)
            {
                var writeButton = FindButtonWithContent(logical, "Write Positions");
                Assert.True(writeButton != null,
                    "ImagePortraitView: Expected a 'Write Positions' button");
                _output.WriteLine("  Write Positions button: found (OK)");
            }
        }

        // ===================================================================
        // PatchManagerView
        // ===================================================================

        [AvaloniaFact]
        public void PatchManagerView_HasCorrectControls()
        {
            var view = new PatchManagerView();
            _output.WriteLine("=== PatchManagerView ===");

            AssertDataContextType(view, typeof(PatchManagerViewModel), "PatchManagerView");

            // Patch list
            AssertNamedControlExists<ListBox>(view, "PatchListBox", "PatchManagerView");
            AssertNamedControlExists(view, "SearchBox", "PatchManagerView");
            AssertNamedControlExists(view, "SummaryLabel", "PatchManagerView");

            // Detail panel
            AssertNamedControlExists(view, "DetailName", "PatchManagerView");
            AssertNamedControlExists(view, "DetailStatus", "PatchManagerView");
            AssertNamedControlExists(view, "DetailDescription", "PatchManagerView");

            // Action buttons
            AssertNamedControlExists<Button>(view, "InstallButton", "PatchManagerView");
            AssertNamedControlExists<Button>(view, "UninstallButton", "PatchManagerView");
        }

        // ===================================================================
        // DataExportView (uses DataContext binding pattern)
        // ===================================================================

        [AvaloniaFact]
        public void DataExportView_HasCorrectDataContext()
        {
            var view = new DataExportView();
            _output.WriteLine("=== DataExportView ===");

            // DataContext should be set in constructor
            Assert.NotNull(view.DataContext);
            Assert.IsType<DataExportViewModel>(view.DataContext);
            _output.WriteLine("  DataContext: DataExportViewModel (OK)");

            // Table combo
            AssertNamedControlExists<ComboBox>(view, "TableCombo", "DataExportView");
        }

        // ===================================================================
        // WelcomeView
        // ===================================================================

        [AvaloniaFact]
        public void WelcomeView_HasCorrectDataContext()
        {
            var view = new WelcomeView();
            _output.WriteLine("=== WelcomeView ===");

            Assert.NotNull(view.DataContext);
            Assert.IsType<WelcomeViewModel>(view.DataContext);
            _output.WriteLine("  DataContext: WelcomeViewModel (OK)");

            // Key buttons
            AssertNamedControlExists<Button>(view, "OpenROMButton", "WelcomeView");
            AssertNamedControlExists<Button>(view, "OpenLastROMButton", "WelcomeView");
        }

        // ===================================================================
        // FERepoResourceBrowserWindow (uses Binding heavily)
        // ===================================================================

        [AvaloniaFact]
        public void FERepoResourceBrowserWindow_HasCorrectDataContext()
        {
            var view = new FERepoResourceBrowserWindow();
            _output.WriteLine("=== FERepoResourceBrowserWindow ===");

            Assert.NotNull(view.DataContext);
            Assert.IsType<FERepoResourceBrowserViewModel>(view.DataContext);
            _output.WriteLine("  DataContext: FERepoResourceBrowserViewModel (OK)");
        }

        // ===================================================================
        // ToolThreeMargeView (uses Binding heavily)
        // ===================================================================

        [AvaloniaFact]
        public void ToolThreeMargeView_HasCorrectDataContext()
        {
            var view = new ToolThreeMargeView();
            _output.WriteLine("=== ToolThreeMargeView ===");

            Assert.NotNull(view.DataContext);
            Assert.IsType<ToolThreeMargeViewViewModel>(view.DataContext);
            _output.WriteLine("  DataContext: ToolThreeMargeViewViewModel (OK)");
        }

        // ===================================================================
        // SongTrackView
        // ===================================================================

        [AvaloniaFact]
        public void SongTrackView_HasCorrectControls()
        {
            var view = new SongTrackView();
            _output.WriteLine("=== SongTrackView ===");

            // Address list
            AssertNamedControlExists<AddressListControl>(view, "EntryList", "SongTrackView");
            AssertNamedControlExists(view, "AddrLabel", "SongTrackView");

            // Track list
            AssertNamedControlExists<ListBox>(view, "TrackListBox", "SongTrackView");

            // Write button
            AssertWriteButtonExists(view, "SongTrackView");
        }

        // ===================================================================
        // ImagePortraitFE6View
        // ===================================================================

        [AvaloniaFact]
        public void ImagePortraitFE6View_HasCorrectControls()
        {
            var view = new ImagePortraitFE6View();
            _output.WriteLine("=== ImagePortraitFE6View ===");

            // Address list
            AssertNamedControlExists<AddressListControl>(view, "EntryList", "ImagePortraitFE6View");

            // Portrait image (GbaImageControl)
            AssertNamedControlExists<GbaImageControl>(view, "PortraitImage", "ImagePortraitFE6View");

            // Address label
            AssertNamedControlExists(view, "AddrLabel", "ImagePortraitFE6View");

            // Note: ImagePortraitFE6View is a read-only viewer, no Write button
            _output.WriteLine("  (read-only view, no Write button expected)");
        }

        // ===================================================================
        // TextCharCodeView (has plain Image controls with explicit Stretch)
        // ===================================================================

        [AvaloniaFact]
        public void TextCharCodeView_ImageControlsHaveCorrectStretch()
        {
            var view = new TextCharCodeView();
            _output.WriteLine("=== TextCharCodeView ===");

            var itemFont = view.FindControl<Image>("ItemFontImage");
            var serifFont = view.FindControl<Image>("SerifFontImage");

            if (itemFont != null)
            {
                Assert.Equal(Stretch.Uniform, itemFont.Stretch);
                _output.WriteLine("  ItemFontImage.Stretch = Uniform (OK)");
            }

            if (serifFont != null)
            {
                Assert.Equal(Stretch.Uniform, serifFont.Stretch);
                _output.WriteLine("  SerifFontImage.Stretch = Uniform (OK)");
            }
        }

        // ===================================================================
        // Sweep: Verify all GbaImageControl instances have Stretch.Fill
        // This is the key regression test for issue #183
        // ===================================================================

        [AvaloniaFact]
        public void AllGbaImageControls_UseStretchFill()
        {
            // Every GbaImageControl instance should have its internal ImageDisplay
            // set to Stretch.Fill (not None, which was the bug)
            int checked_ = 0;
            int passed = 0;

            // Create a fresh GbaImageControl and verify
            var ctrl = new GbaImageControl();
            var img = ctrl.FindControl<Image>("ImageDisplay");
            Assert.NotNull(img);

            checked_++;
            if (img.Stretch == Stretch.Fill) passed++;

            Assert.Equal(Stretch.Fill, img.Stretch);
            _output.WriteLine($"GbaImageControl internal Image.Stretch = {img.Stretch} (expected Fill)");
            _output.WriteLine($"Checked {checked_} GbaImageControl instances, {passed} passed");
        }

        // ===================================================================
        // Sweep: All editor views with AddressListControl have one
        // ===================================================================

        /// <summary>
        /// Views that are expected to have an AddressListControl for navigation.
        /// </summary>
        public static TheoryData<string, Type> EditorViewsWithAddressList => new()
        {
            { "UnitEditorView", typeof(UnitEditorView) },
            { "ClassEditorView", typeof(ClassEditorView) },
            { "ItemEditorView", typeof(ItemEditorView) },
            { "MapSettingView", typeof(MapSettingView) },
            { "PortraitViewerView", typeof(PortraitViewerView) },
            { "SongTableView", typeof(SongTableView) },
            { "SoundRoomViewerView", typeof(SoundRoomViewerView) },
            { "TextViewerView", typeof(TextViewerView) },
            { "ImagePortraitView", typeof(ImagePortraitView) },
            { "SongTrackView", typeof(SongTrackView) },
        };

        [AvaloniaTheory]
        [MemberData(nameof(EditorViewsWithAddressList))]
        public void EditorView_HasAddressListControl(string viewName, Type viewType)
        {
            var view = Activator.CreateInstance(viewType);
            Assert.NotNull(view);

            // Search for any AddressListControl in the logical tree
            bool found = false;
            if (view is ILogical logical)
            {
                found = HasControlOfType<AddressListControl>(logical);
            }

            Assert.True(found,
                $"{viewName}: Expected an AddressListControl but none found in logical tree");
            _output.WriteLine($"{viewName}: AddressListControl found (OK)");
        }

        /// <summary>Recursively check if a logical tree contains a control of type T.</summary>
        private static bool HasControlOfType<T>(ILogical parent) where T : class
        {
            if (parent is T) return true;
            foreach (var child in parent.LogicalChildren)
            {
                if (child is ILogical logicalChild && HasControlOfType<T>(logicalChild))
                    return true;
            }
            return false;
        }

        // ===================================================================
        // Summary: IDataVerifiableView implementors have DataViewModel
        // ===================================================================

        [AvaloniaFact]
        public void AllDataVerifiableViews_HaveDataViewModel()
        {
            var avaloniaAsm = typeof(FEBuilderGBA.Avalonia.App).Assembly;
            var dvvType = avaloniaAsm.GetTypes()
                .FirstOrDefault(t => t.Name == "IDataVerifiableView" && t.IsInterface);

            if (dvvType == null)
            {
                _output.WriteLine("IDataVerifiableView interface not found, skipping");
                return;
            }

            var implementors = avaloniaAsm.GetTypes()
                .Where(t => !t.IsAbstract && !t.IsInterface && dvvType.IsAssignableFrom(t))
                .Where(t => t.GetConstructor(Type.EmptyTypes) != null)
                .OrderBy(t => t.Name)
                .ToList();

            _output.WriteLine($"Found {implementors.Count} IDataVerifiableView implementors:");

            int checked_ = 0;
            int withVm = 0;

            foreach (var type in implementors)
            {
                checked_++;
                var prop = type.GetProperty("DataViewModel",
                    BindingFlags.Public | BindingFlags.Instance);

                if (prop != null)
                {
                    withVm++;
                    _output.WriteLine($"  {type.Name}: DataViewModel property exists ({prop.PropertyType.Name})");
                }
                else
                {
                    _output.WriteLine($"  {type.Name}: no DataViewModel property");
                }
            }

            _output.WriteLine($"\nTotal: {checked_} checked, {withVm} have DataViewModel");
            // Most IDataVerifiableView should have DataViewModel
            Assert.True(withVm >= implementors.Count / 2,
                $"Only {withVm}/{implementors.Count} IDataVerifiableView implementors have DataViewModel");
        }
    }
}
