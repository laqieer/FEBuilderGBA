using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.Views;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests;

public class AIEditorEmbeddableTests
{
    static IEnumerable<(Func<Control> Factory, string Title, double Width, double Height)> Cases()
    {
        yield return (() => new AIASMCALLTALKView(), "AI ASM Call Talk", 626, 388);
        yield return (() => new AIASMCoordinateView(), "AI Coordinate Editor", 626, 388);
        yield return (() => new AIASMRangeView(), "AI Range Editor", 626, 388);
        yield return (() => new AIMapSettingView(), "AI Map Settings", 800, 500);
        yield return (() => new AIPerformItemView(), "AI Item Performance", 800, 500);
        yield return (() => new AIPerformStaffView(), "AI Staff Performance", 800, 500);
        yield return (() => new AIStealItemView(), "AI Steal Item Logic", 800, 400);
        yield return (() => new AITargetView(), "AI Targeting", 800, 820);
        yield return (() => new AITilesView(), "AI Tiles Evaluation", 800, 350);
        yield return (() => new AIUnitsView(), "AI Units Evaluation", 800, 380);
    }

    [AvaloniaFact]
    public void AI_editors_are_embeddable_usercontrols_with_descriptors()
    {
        foreach (var (factory, title, width, height) in Cases())
        {
            var view = factory();
            var embeddable = Assert.IsAssignableFrom<IEmbeddableEditor>(view);

            Assert.IsAssignableFrom<UserControl>(view);
            Assert.IsNotType<Window>(view);
            Assert.Equal(title, embeddable.Descriptor.Title);
            Assert.Equal(width, embeddable.Descriptor.PreferredWidth);
            Assert.Equal(height, embeddable.Descriptor.PreferredHeight);
            Assert.True(embeddable.Descriptor.SizeToContent);
        }
    }
}
