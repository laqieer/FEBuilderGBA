namespace FEBuilderGBA.Tests.Unit
{
    /// <summary>
    /// #985 source-scan tests: verify the Avalonia Unit Palette Editor view wires
    /// the new <c>UnitPaletteClassResolverCore</c> palette->class resolution into
    /// its selection handler so the Edit-tab Battle Animation id + sample preview
    /// populate. The resolver itself carries the behavioral coverage
    /// (FEBuilderGBA.Core.Tests/UnitPaletteClassResolverCoreTests); these tests
    /// just guard that the wire-up is present (regression on the original empty
    /// Battle Animation bug).
    /// </summary>
    public class ImageUnitPaletteViewWiringTests
    {
        private static string SolutionDir
        {
            get
            {
                var dir = AppContext.BaseDirectory;
                while (dir != null && !File.Exists(Path.Combine(dir, "FEBuilderGBA.sln")))
                    dir = Path.GetDirectoryName(dir);
                return dir ?? throw new InvalidOperationException("Cannot find solution root");
            }
        }

        private string ViewSrc => File.ReadAllText(
            Path.Combine(SolutionDir, "FEBuilderGBA.Avalonia", "Views", "ImageUnitPaletteView.axaml.cs"));

        [Fact]
        public void OnSelected_CallsResolveDefaultPreviewClass()
        {
            Assert.Contains("ResolveDefaultPreviewClass", ViewSrc);
        }

        [Fact]
        public void OnSelected_FallsBackToFindFirstClassWithAnime()
        {
            Assert.Contains("FindFirstClassWithAnime", ViewSrc);
        }

        [Fact]
        public void OnSelected_SetsClassIdAndNameFromResolvedClass()
        {
            var src = ViewSrc;
            // The resolved class must flow into the VM (ClassID) + name display.
            Assert.Contains("_vm.ClassID = cls", src);
            Assert.Contains("NameResolver.GetClassName(cls)", src);
        }

        [Fact]
        public void OnSelected_ResolvesUsingZeroBasedSlotIndex()
        {
            // SelectedPaletteSlot is 1-based; the resolver wants the 0-based index.
            Assert.Contains("_vm.SelectedPaletteSlot - 1", ViewSrc);
        }
    }
}
