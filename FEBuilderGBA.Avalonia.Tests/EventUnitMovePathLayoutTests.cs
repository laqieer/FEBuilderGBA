// SPDX-License-Identifier: GPL-3.0-or-later
// #1713 — the FE8 "After Coordinate Move Path" grid's NumericUpDown spinners
// overlapped/squished into adjacent columns (NUD min-width > the 70-90px columns).
// The fix widens the header + row-template ColumnDefinitions and the per-cell NUD
// Width so each value+spinner fits inside its column. These headless tests realize
// a populated row and assert the eight spinners do NOT overlap horizontally.
using System.Collections.Generic;
using System.Linq;
using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Headless.XUnit;
using global::Avalonia.VisualTree;
using FEBuilderGBA.Avalonia.Views;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests
{
    public class EventUnitMovePathLayoutTests
    {
        // Public stand-in exposing the same property names the DataTemplate binds
        // ({Binding X}, {Binding Y}, … {Binding Wait}, {Binding IsAfterRow},
        // {Binding StartRowMarker}). Avalonia binds by name, so this drives the real
        // AXAML row template without needing the (private) Fe8CoordRowViewModel.
        public sealed class Row
        {
            public string StartRowMarker { get; set; } = "1";
            public int X { get; set; } = 10;
            public int Y { get; set; } = 8;
            public int Ext { get; set; } = 0;
            public int Speed { get; set; } = 200;
            public int UnitId { get; set; } = 255;
            public int Unk1 { get; set; } = 255;
            public int Unk2 { get; set; } = 255;
            public int Wait { get; set; } = 65535;
            public bool IsAfterRow { get; set; } = true;
        }

        [AvaloniaFact]
        public void EventUnitView_CanInstantiate()
        {
            var v = new EventUnitView();
            Assert.NotNull(v.Content);
        }

        [AvaloniaFact]
        public void AfterCoordsList_Row_NumericUpDowns_DoNotOverlap()
        {
            var v = new EventUnitView();

            // The After-Coordinate-Move-Path panel is FE8-gated (hidden until a FE8
            // ROM loads). Force it visible so the list realizes in this ROM-less test.
            var panel = v.FindControl<Control>("AfterCoordsPanel");
            Assert.NotNull(panel);
            panel!.IsVisible = true;

            var list = v.FindControl<ListBox>("AfterCoordsList");
            Assert.NotNull(list);

            // Non-virtualizing panel so both rows realize deterministically headless.
            list!.ItemsPanel = new global::Avalonia.Controls.Templates.FuncTemplate<Panel?>(
                () => new StackPanel());
            list!.ItemsSource = new List<Row> { new Row(), new Row() };

            // EventUnitView is itself a Window (top-level) — show it directly.
            var window = v;
            window.Width = 1902;
            window.Height = 1047;
            window.Show();
            window.UpdateLayout();

            // All realized spinners in the list (8 per row).
            var nuds = list.GetVisualDescendants().OfType<NumericUpDown>().ToList();
            Assert.True(nuds.Count >= 8, $"expected >=8 realized NumericUpDowns, got {nuds.Count}");

            // Take the first realized row (the 8 left-most by window X), ordered L->R.
            var firstRow = nuds
                .Select(n => new { Nud = n, P = n.TranslatePoint(new Point(0, 0), window) })
                .Where(x => x.P.HasValue)
                .OrderBy(x => x.P!.Value.Y).ThenBy(x => x.P!.Value.X)
                .Take(8)
                .OrderBy(x => x.P!.Value.X)
                .ToList();
            Assert.Equal(8, firstRow.Count);

            string dump = string.Join(" | ", firstRow.Select((x, i) =>
                $"#{i} x={x.P!.Value.X:F0} w={x.Nud.Bounds.Width:F0} r={x.P!.Value.X + x.Nud.Bounds.Width:F0}"));

            // No spinner's right edge may pass the next spinner's left edge.
            for (int i = 0; i + 1 < firstRow.Count; i++)
            {
                double aRight = firstRow[i].P!.Value.X + firstRow[i].Nud.Bounds.Width;
                double bLeft = firstRow[i + 1].P!.Value.X;
                Assert.True(
                    aRight <= bLeft + 0.5,
                    $"NumericUpDown #{i} (right={aRight:F1}) overlaps #{i + 1} (left={bLeft:F1}) " +
                    $"— After Coordinate Move Path spinners are colliding (#1713). [{dump}]");
            }
        }
    }
}
