// SPDX-License-Identifier: GPL-3.0-or-later
using FEBuilderGBA;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    /// <summary>Tests for the Plan v4 backend-selection rule (#1978 Slice 3).</summary>
    public class RandomMapBackendSelectorCoreTests
    {
        static FEMapCreatorTilesetMappingEntry MakeEntry() =>
            new FEMapCreatorTilesetMappingEntry(
                "fp1", "tileset-a",
                "image.png", 10, 100, "sha-image",
                "gen.json", 20, 200, "sha-gen",
                "femapcreator.exe", 30, 300, "sha-exe",
                "");

        [Fact]
        public void Select_Current_UsesExternalWithMappingAndNoNotice()
        {
            FEMapCreatorTilesetMappingEntry entry = MakeEntry();
            FEMapCreatorMappingLookupResult lookup = FEMapCreatorMappingLookupResult.Current(entry);

            RandomMapBackendSelection selection = RandomMapBackendSelectorCore.Select(lookup);

            Assert.Equal(RandomMapBackendKind.External, selection.Kind);
            Assert.Same(entry, selection.ExternalMapping);
            Assert.Equal("", selection.Notice);
        }

        [Fact]
        public void Select_Stale_UsesBuiltInWithNonEmptyNotice()
        {
            FEMapCreatorTilesetMappingEntry entry = MakeEntry();
            FEMapCreatorMappingLookupResult lookup = FEMapCreatorMappingLookupResult.Stale(entry, "executable content changed");

            RandomMapBackendSelection selection = RandomMapBackendSelectorCore.Select(lookup);

            Assert.Equal(RandomMapBackendKind.BuiltIn, selection.Kind);
            Assert.Null(selection.ExternalMapping);
            Assert.False(string.IsNullOrWhiteSpace(selection.Notice));
            Assert.Contains("executable content changed", selection.Notice);
        }

        [Fact]
        public void Select_Invalid_UsesBuiltInWithNonEmptyNotice()
        {
            FEMapCreatorTilesetMappingEntry entry = MakeEntry();
            FEMapCreatorMappingLookupResult lookup = FEMapCreatorMappingLookupResult.Invalid(entry, "malformed entry");

            RandomMapBackendSelection selection = RandomMapBackendSelectorCore.Select(lookup);

            Assert.Equal(RandomMapBackendKind.BuiltIn, selection.Kind);
            Assert.Null(selection.ExternalMapping);
            Assert.False(string.IsNullOrWhiteSpace(selection.Notice));
            Assert.Contains("malformed entry", selection.Notice);
        }

        [Fact]
        public void Select_Stale_WithBlankReason_StillProducesNonEmptyNotice()
        {
            FEMapCreatorTilesetMappingEntry entry = MakeEntry();
            FEMapCreatorMappingLookupResult lookup = FEMapCreatorMappingLookupResult.Stale(entry, "");

            RandomMapBackendSelection selection = RandomMapBackendSelectorCore.Select(lookup);

            Assert.Equal(RandomMapBackendKind.BuiltIn, selection.Kind);
            Assert.False(string.IsNullOrWhiteSpace(selection.Notice));
        }

        [Fact]
        public void Select_NoMapping_UsesBuiltInWithEmptyNotice()
        {
            FEMapCreatorMappingLookupResult lookup = FEMapCreatorMappingLookupResult.NoMapping();

            RandomMapBackendSelection selection = RandomMapBackendSelectorCore.Select(lookup);

            Assert.Equal(RandomMapBackendKind.BuiltIn, selection.Kind);
            Assert.Null(selection.ExternalMapping);
            Assert.Equal("", selection.Notice);
        }

        [Fact]
        public void Select_NullLookup_Throws()
        {
            Assert.Throws<System.ArgumentNullException>(() => RandomMapBackendSelectorCore.Select(null));
        }
    }
}
