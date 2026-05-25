using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using FEBuilderGBA.Avalonia.Views;
using Xunit;
using Xunit.Abstractions;

namespace FEBuilderGBA.Avalonia.Tests
{
    /// <summary>
    /// Regression tests for NumericUpDown FormatString across ALL Avalonia views.
    /// Avalonia NumericUpDown uses decimal internally, so hex format specifiers
    /// (X2, X4, X8) cause FormatException at runtime. All NumericUpDown controls
    /// must use decimal format "0".
    /// See #253.
    /// </summary>
    public class NumericUpDownFormatStringTests
    {
        private readonly ITestOutputHelper _output;

        public NumericUpDownFormatStringTests(ITestOutputHelper output) => _output = output;

        /// <summary>
        /// Collect all NumericUpDown controls from a view's logical tree.
        /// </summary>
        private static List<NumericUpDown> CollectNumericUpDowns(Control root)
        {
            return root.GetLogicalDescendants()
                       .OfType<NumericUpDown>()
                       .ToList();
        }

        // ===================================================================
        // Global MinWidth style regression test (#315)
        // ===================================================================

        [Fact]
        public void AppAndTestApp_NumericUpDown_GlobalMinWidth_IsAtLeast120()
        {
            // Verify both App.axaml and TestApp.axaml define MinWidth >= 120 for NumericUpDown (#315).
            string dir = AppContext.BaseDirectory;
            while (dir != null && !System.IO.File.Exists(System.IO.Path.Combine(dir, "FEBuilderGBA.Avalonia", "App.axaml")))
                dir = System.IO.Path.GetDirectoryName(dir);
            Assert.NotNull(dir);

            // Check both files
            var files = new[]
            {
                System.IO.Path.Combine(dir!, "FEBuilderGBA.Avalonia", "App.axaml"),
                System.IO.Path.Combine(dir!, "FEBuilderGBA.Avalonia.Tests", "TestApp.axaml"),
            };
            // Regex scoped to NumericUpDown style block (Singleline: . matches \n)
            var pattern = new System.Text.RegularExpressions.Regex(
                @"Selector=""NumericUpDown"".+?MinWidth.+?Value=""(\d+)""",
                System.Text.RegularExpressions.RegexOptions.Singleline);

            foreach (var file in files)
            {
                string content = System.IO.File.ReadAllText(file);
                var match = pattern.Match(content);
                Assert.True(match.Success, $"{System.IO.Path.GetFileName(file)} should define MinWidth for NumericUpDown style");
                int minWidth = int.Parse(match.Groups[1].Value);
                Assert.True(minWidth >= 120, $"{System.IO.Path.GetFileName(file)}: NumericUpDown MinWidth should be >= 120, was {minWidth}");
            }
        }

        // ===================================================================
        // ClassEditorView: all NumericUpDown controls use decimal format
        // ===================================================================

        [AvaloniaFact]
        public void ClassEditorView_AllNumericUpDowns_UseDecimalFormat()
        {
            var view = new ClassEditorView();
            var nuds = CollectNumericUpDowns(view);

            Assert.True(nuds.Count > 0, "ClassEditorView should have NumericUpDown controls");
            _output.WriteLine($"ClassEditorView: {nuds.Count} NumericUpDown controls found");

            var violations = new List<string>();
            foreach (var nud in nuds)
            {
                if (nud.FormatString != "0" && nud.FormatString != null)
                {
                    violations.Add($"{nud.Name ?? "(unnamed)"}: FormatString=\"{nud.FormatString}\"");
                }
            }

            foreach (var v in violations)
                _output.WriteLine($"  VIOLATION: {v}");

            Assert.Empty(violations);
        }

        // ===================================================================
        // EventCondView: all NumericUpDown controls use decimal format
        // ===================================================================

        [AvaloniaFact]
        public void EventCondView_AllNumericUpDowns_UseDecimalFormat()
        {
            var view = new EventCondView();
            var nuds = CollectNumericUpDowns(view);

            Assert.True(nuds.Count > 0, "EventCondView should have NumericUpDown controls");
            _output.WriteLine($"EventCondView: {nuds.Count} NumericUpDown controls found");

            var violations = new List<string>();
            foreach (var nud in nuds)
            {
                if (nud.FormatString != "0" && nud.FormatString != null)
                {
                    violations.Add($"{nud.Name ?? "(unnamed)"}: FormatString=\"{nud.FormatString}\"");
                }
            }

            foreach (var v in violations)
                _output.WriteLine($"  VIOLATION: {v}");

            Assert.Empty(violations);
        }

        // ===================================================================
        // SongInstrumentView: all NumericUpDown controls use decimal format
        // ===================================================================

        [AvaloniaFact]
        public void SongInstrumentView_AllNumericUpDowns_UseDecimalFormat()
        {
            var view = new SongInstrumentView();
            var nuds = CollectNumericUpDowns(view);

            Assert.True(nuds.Count > 0, "SongInstrumentView should have NumericUpDown controls");
            _output.WriteLine($"SongInstrumentView: {nuds.Count} NumericUpDown controls found");

            var violations = new List<string>();
            foreach (var nud in nuds)
            {
                if (nud.FormatString != "0" && nud.FormatString != null)
                {
                    violations.Add($"{nud.Name ?? "(unnamed)"}: FormatString=\"{nud.FormatString}\"");
                }
            }

            foreach (var v in violations)
                _output.WriteLine($"  VIOLATION: {v}");

            Assert.Empty(violations);
        }

        // ===================================================================
        // EventCondView: specific named controls verified
        // ===================================================================

        [AvaloniaTheory]
        [InlineData("CondTypeBox")]
        [InlineData("SubTypeBox")]
        [InlineData("FlagIdBox")]
        [InlineData("EventPtrBox")]
        [InlineData("ExtraB8Box")]
        [InlineData("ExtraB9Box")]
        [InlineData("ExtraB10Box")]
        [InlineData("ExtraB11Box")]
        [InlineData("ExtraB12Box")]
        [InlineData("ExtraB13Box")]
        [InlineData("ExtraB14Box")]
        [InlineData("ExtraB15Box")]
        public void EventCondView_NamedControl_HasDecimalFormat(string controlName)
        {
            var view = new EventCondView();
            var nud = view.FindControl<NumericUpDown>(controlName);
            Assert.NotNull(nud);
            Assert.Equal("0", nud!.FormatString);
            _output.WriteLine($"{controlName}: FormatString={nud.FormatString} (OK)");
        }

        // ===================================================================
        // SongInstrumentView: specific named controls verified
        // ===================================================================

        // After PR #387 parity rebuild, the view exposes per-tab numeric
        // inputs (Nxx_Bn_Box / Nxx_Pn_Box) instead of the old per-category
        // named controls. Verify a representative selection from each
        // tab — they all share the FormatString="0" requirement.
        [AvaloniaTheory]
        [InlineData("HeaderByteBox")]
        [InlineData("N00_P4_Box")]   // DirectSound wave pointer (was WavePtrBox)
        [InlineData("N01_B3_Box")]   // SquareWave1 sweep raw byte (was SweepBox)
        [InlineData("N01_B2_Box")]   // SquareWave1 duty/len raw byte (was DutyLenBox)
        [InlineData("N01_B1_Box")]   // SquareWave1 envelope step raw byte (was EnvStepBox)
        [InlineData("N04_P4_Box")]   // Noise period field (was PeriodBox)
        [InlineData("N40_P4_Box")]   // MultiSample key-map pointer (was KeyMapPtrBox)
        [InlineData("N40_P8_Box")]   // MultiSample sub-instrument pointer
        [InlineData("N80_P4_Box")]   // Drum sub-instrument pointer (was DR_SubInstrPtrBox)
        public void SongInstrumentView_NamedControl_HasDecimalFormat(string controlName)
        {
            var view = new SongInstrumentView();
            var nud = view.FindControl<NumericUpDown>(controlName);
            Assert.NotNull(nud);
            Assert.Equal("0", nud!.FormatString);
            _output.WriteLine($"{controlName}: FormatString={nud.FormatString} (OK)");
        }

        // ===================================================================
        // Runtime regression: setting Value on NumericUpDown must not throw
        // ===================================================================

        [AvaloniaFact]
        public void ClassEditorView_SetNumericValues_DoesNotThrow()
        {
            // Regression test: with hex FormatString (X2/X4), setting Value
            // on NumericUpDown would throw FormatException. With decimal "0",
            // setting values must succeed without exceptions.
            var view = new ClassEditorView();
            var nuds = CollectNumericUpDowns(view);

            _output.WriteLine($"Setting Value on {nuds.Count} NumericUpDown controls...");
            var exceptions = new List<string>();

            foreach (var nud in nuds)
            {
                try
                {
                    nud.Value = 42;
                }
                catch (Exception ex)
                {
                    exceptions.Add($"{nud.Name ?? "(unnamed)"}: {ex.GetType().Name}: {ex.Message}");
                }
            }

            foreach (var e in exceptions)
                _output.WriteLine($"  EXCEPTION: {e}");

            Assert.Empty(exceptions);
        }

        [AvaloniaFact]
        public void EventCondView_SetNumericValues_DoesNotThrow()
        {
            var view = new EventCondView();
            var nuds = CollectNumericUpDowns(view);

            _output.WriteLine($"Setting Value on {nuds.Count} NumericUpDown controls...");
            var exceptions = new List<string>();

            foreach (var nud in nuds)
            {
                try
                {
                    nud.Value = 42;
                }
                catch (Exception ex)
                {
                    exceptions.Add($"{nud.Name ?? "(unnamed)"}: {ex.GetType().Name}: {ex.Message}");
                }
            }

            foreach (var e in exceptions)
                _output.WriteLine($"  EXCEPTION: {e}");

            Assert.Empty(exceptions);
        }

        [AvaloniaFact]
        public void SongInstrumentView_SetNumericValues_DoesNotThrow()
        {
            var view = new SongInstrumentView();
            var nuds = CollectNumericUpDowns(view);

            _output.WriteLine($"Setting Value on {nuds.Count} NumericUpDown controls...");
            var exceptions = new List<string>();

            foreach (var nud in nuds)
            {
                try
                {
                    nud.Value = 42;
                }
                catch (Exception ex)
                {
                    exceptions.Add($"{nud.Name ?? "(unnamed)"}: {ex.GetType().Name}: {ex.Message}");
                }
            }

            foreach (var e in exceptions)
                _output.WriteLine($"  EXCEPTION: {e}");

            Assert.Empty(exceptions);
        }

        // ===================================================================
        // Desc text preview controls exist (#317)
        // ===================================================================

        [AvaloniaFact]
        public void UnitEditorView_HasDescTextLabel()
        {
            var view = new UnitEditorView();
            var label = view.FindControl<TextBlock>("DescTextLabel");
            Assert.NotNull(label);
            _output.WriteLine("UnitEditorView: DescTextLabel found");
        }

        [AvaloniaFact]
        public void UnitFE6View_HasDescTextLabel()
        {
            var view = new UnitFE6View();
            var label = view.FindControl<TextBlock>("DescTextLabel");
            Assert.NotNull(label);
            _output.WriteLine("UnitFE6View: DescTextLabel found");
        }

        [AvaloniaFact]
        public void UnitFE7View_HasDescTextLabel()
        {
            var view = new UnitFE7View();
            var label = view.FindControl<TextBlock>("DescTextLabel");
            Assert.NotNull(label);
            _output.WriteLine("UnitFE7View: DescTextLabel found");
        }

        [AvaloniaFact]
        public void ItemFE6View_HasDescTextLabel()
        {
            var view = new ItemFE6View();
            var label = view.FindControl<TextBlock>("DescTextLabel");
            Assert.NotNull(label);
            _output.WriteLine("ItemFE6View: DescTextLabel found");
        }
    }
}
