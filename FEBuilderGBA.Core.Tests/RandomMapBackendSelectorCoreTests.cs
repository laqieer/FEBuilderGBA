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
        public void Select_Current_UsesExternalWithTypedCurrentState()
        {
            FEMapCreatorTilesetMappingEntry entry = MakeEntry();
            FEMapCreatorMappingLookupResult lookup = FEMapCreatorMappingLookupResult.Current(entry);

            RandomMapBackendSelection selection = RandomMapBackendSelectorCore.Select(lookup);

            Assert.Equal(RandomMapBackendKind.External, selection.Kind);
            Assert.Same(entry, selection.ExternalMapping);
            Assert.Equal(FEMapCreatorMappingStatus.Current, selection.MappingStatus);
            Assert.Equal(FEMapCreatorMappingReason.None, selection.MappingReason);
            Assert.Equal("", selection.MappingDetail);
        }

        [Fact]
        public void Select_Stale_UsesBuiltInAndPreservesTypedReason()
        {
            FEMapCreatorTilesetMappingEntry entry = MakeEntry();
            FEMapCreatorMappingLookupResult lookup = FEMapCreatorMappingLookupResult.Stale(
                entry,
                FEMapCreatorMappingReason.ExecutableContentChanged,
                "diagnostic");

            RandomMapBackendSelection selection = RandomMapBackendSelectorCore.Select(lookup);

            Assert.Equal(RandomMapBackendKind.BuiltIn, selection.Kind);
            Assert.Null(selection.ExternalMapping);
            Assert.Equal(FEMapCreatorMappingStatus.Stale, selection.MappingStatus);
            Assert.Equal(FEMapCreatorMappingReason.ExecutableContentChanged, selection.MappingReason);
            Assert.Equal("diagnostic", selection.MappingDetail);
        }

        [Fact]
        public void Select_Invalid_UsesBuiltInAndPreservesTypedReason()
        {
            FEMapCreatorTilesetMappingEntry entry = MakeEntry();
            FEMapCreatorMappingLookupResult lookup = FEMapCreatorMappingLookupResult.Invalid(
                entry,
                FEMapCreatorMappingReason.StoredEntryMissingRequiredFields);

            RandomMapBackendSelection selection = RandomMapBackendSelectorCore.Select(lookup);

            Assert.Equal(RandomMapBackendKind.BuiltIn, selection.Kind);
            Assert.Null(selection.ExternalMapping);
            Assert.Equal(FEMapCreatorMappingStatus.Invalid, selection.MappingStatus);
            Assert.Equal(FEMapCreatorMappingReason.StoredEntryMissingRequiredFields, selection.MappingReason);
            Assert.Equal("", selection.MappingDetail);
        }

        [Fact]
        public void Select_Stale_WithBlankReason_PreservesBlankReasonForLocalizedUiFallback()
        {
            FEMapCreatorTilesetMappingEntry entry = MakeEntry();
            FEMapCreatorMappingLookupResult lookup = FEMapCreatorMappingLookupResult.Stale(
                entry,
                FEMapCreatorMappingReason.None);

            RandomMapBackendSelection selection = RandomMapBackendSelectorCore.Select(lookup);

            Assert.Equal(RandomMapBackendKind.BuiltIn, selection.Kind);
            Assert.Equal(FEMapCreatorMappingStatus.Stale, selection.MappingStatus);
            Assert.Equal(FEMapCreatorMappingReason.None, selection.MappingReason);
            Assert.Equal("", selection.MappingDetail);
        }

        [Fact]
        public void Select_NoMapping_UsesBuiltInWithTypedNoMappingState()
        {
            FEMapCreatorMappingLookupResult lookup = FEMapCreatorMappingLookupResult.NoMapping();

            RandomMapBackendSelection selection = RandomMapBackendSelectorCore.Select(lookup);

            Assert.Equal(RandomMapBackendKind.BuiltIn, selection.Kind);
            Assert.Null(selection.ExternalMapping);
            Assert.Equal(FEMapCreatorMappingStatus.NoMapping, selection.MappingStatus);
            Assert.Equal(FEMapCreatorMappingReason.None, selection.MappingReason);
            Assert.Equal("", selection.MappingDetail);
        }

        [Fact]
        public void Select_NullLookup_Throws()
        {
            Assert.Throws<System.ArgumentNullException>(() => RandomMapBackendSelectorCore.Select(null));
        }
    }
}
