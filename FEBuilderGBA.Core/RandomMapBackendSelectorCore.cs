// SPDX-License-Identifier: GPL-3.0-or-later
using System;

namespace FEBuilderGBA
{
    /// <summary>Which random-map generation backend a one-click request should use (#1978 Slice 3).</summary>
    public enum RandomMapBackendKind
    {
        /// <summary>The in-process, clean-room <see cref="BuiltInRandomMapGeneratorCore"/> engine.</summary>
        BuiltIn,

        /// <summary>The external FEMapCreator process adapter (<see cref="RandomMapGeneratorCore"/>).</summary>
        External,
    }

    /// <summary>
    /// Pure, deterministic outcome of <see cref="RandomMapBackendSelectorCore.Select"/>. Never
    /// touches disk, a process, or the network itself — it only interprets an already-computed
    /// <see cref="FEMapCreatorSetupSnapshot"/> and <see cref="FEMapCreatorMappingLookupResult"/>.
    /// </summary>
    public sealed class RandomMapBackendSelection
    {
        RandomMapBackendSelection(
            RandomMapBackendKind kind,
            FEMapCreatorTilesetMappingEntry externalMapping,
            FEMapCreatorMappingStatus mappingStatus,
            FEMapCreatorMappingReason mappingReason,
            string mappingDetail)
        {
            Kind = kind;
            ExternalMapping = externalMapping;
            MappingStatus = mappingStatus;
            MappingReason = mappingReason;
            MappingDetail = mappingDetail ?? "";
        }

        /// <summary>Which backend to run.</summary>
        public RandomMapBackendKind Kind { get; }

        /// <summary>
        /// The validated-current mapping entry to drive the external adapter with. Non-null
        /// only when <see cref="Kind"/> is <see cref="RandomMapBackendKind.External"/>.
        /// </summary>
        public FEMapCreatorTilesetMappingEntry ExternalMapping { get; }

        /// <summary>
        /// Typed mapping state preserved for the UI boundary. Core deliberately does not build
        /// an English user-facing fallback notice.
        /// </summary>
        public FEMapCreatorMappingStatus MappingStatus { get; }

        /// <summary>Locale-neutral reason associated with a stale/invalid mapping.</summary>
        public FEMapCreatorMappingReason MappingReason { get; }

        /// <summary>Optional technical detail kept separate from localized user-facing text.</summary>
        public string MappingDetail { get; }

        internal static RandomMapBackendSelection UseExternal(FEMapCreatorTilesetMappingEntry mapping) =>
            new RandomMapBackendSelection(
                RandomMapBackendKind.External,
                mapping,
                FEMapCreatorMappingStatus.Current,
                FEMapCreatorMappingReason.None,
                "");

        internal static RandomMapBackendSelection UseBuiltIn(
            FEMapCreatorMappingStatus mappingStatus,
            FEMapCreatorMappingReason mappingReason = FEMapCreatorMappingReason.None,
            string mappingDetail = "") =>
            new RandomMapBackendSelection(
                RandomMapBackendKind.BuiltIn,
                null,
                mappingStatus,
                mappingReason,
                mappingDetail);
    }

    /// <summary>
    /// Decides, per Plan v4, whether a one-click random-map request should run the external
    /// FEMapCreator adapter or the built-in engine (#1978 Slice 3). Pure function of its inputs:
    /// performs no filesystem, process, or network access itself (callers are expected to have
    /// already produced <paramref name="profile"/> via <see cref="FEMapCreatorProfileCore.Validate"/>
    /// and <paramref name="mappingLookup"/> via <see cref="FEMapCreatorTilesetMappingStoreCore.Lookup"/>).
    /// <list type="bullet">
    /// <item>Mapping <see cref="FEMapCreatorMappingStatus.Current"/> (which itself already implies
    /// the profile was <see cref="FEMapCreatorSetupStatus.Configured"/> at lookup time) selects
    /// <see cref="RandomMapBackendKind.External"/> with no notice.</item>
    /// <item>Mapping <see cref="FEMapCreatorMappingStatus.Stale"/> or <see cref="FEMapCreatorMappingStatus.Invalid"/>
    /// selects <see cref="RandomMapBackendKind.BuiltIn"/> while preserving the typed status and
    /// reason so the caller can visibly explain the fallback in the active locale.</item>
    /// <item>Mapping <see cref="FEMapCreatorMappingStatus.NoMapping"/> selects
    /// <see cref="RandomMapBackendKind.BuiltIn"/> with NoMapping status — this is the expected,
    /// unremarkable default before any mapping has ever been configured.</item>
    /// </list>
    /// </summary>
    public static class RandomMapBackendSelectorCore
    {
        public static RandomMapBackendSelection Select(FEMapCreatorMappingLookupResult mappingLookup)
        {
            ArgumentNullException.ThrowIfNull(mappingLookup);

            switch (mappingLookup.Status)
            {
                case FEMapCreatorMappingStatus.Current:
                    return RandomMapBackendSelection.UseExternal(mappingLookup.Entry);

                case FEMapCreatorMappingStatus.Stale:
                case FEMapCreatorMappingStatus.Invalid:
                    return RandomMapBackendSelection.UseBuiltIn(
                        mappingLookup.Status,
                        mappingLookup.Reason,
                        mappingLookup.Detail);

                case FEMapCreatorMappingStatus.NoMapping:
                default:
                    return RandomMapBackendSelection.UseBuiltIn(FEMapCreatorMappingStatus.NoMapping);
            }
        }
    }
}
