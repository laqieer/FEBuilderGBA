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
            string notice)
        {
            Kind = kind;
            ExternalMapping = externalMapping;
            Notice = notice ?? "";
        }

        /// <summary>Which backend to run.</summary>
        public RandomMapBackendKind Kind { get; }

        /// <summary>
        /// The validated-current mapping entry to drive the external adapter with. Non-null
        /// only when <see cref="Kind"/> is <see cref="RandomMapBackendKind.External"/>.
        /// </summary>
        public FEMapCreatorTilesetMappingEntry ExternalMapping { get; }

        /// <summary>
        /// Non-empty, user-facing explanation for why the built-in engine was used even though
        /// a mapping exists but is no longer valid (<see cref="FEMapCreatorMappingStatus.Stale"/>
        /// or <see cref="FEMapCreatorMappingStatus.Invalid"/>). Empty when there was simply no
        /// mapping at all, or when the external backend was selected — this is deliberately NOT
        /// a generic status message, so callers can distinguish "no mapping configured" (silent,
        /// expected default) from "a mapping exists but could not be trusted" (must be shown to
        /// the user, never silently swallowed).
        /// </summary>
        public string Notice { get; }

        internal static RandomMapBackendSelection UseExternal(FEMapCreatorTilesetMappingEntry mapping) =>
            new RandomMapBackendSelection(RandomMapBackendKind.External, mapping, "");

        internal static RandomMapBackendSelection UseBuiltIn(string notice) =>
            new RandomMapBackendSelection(RandomMapBackendKind.BuiltIn, null, notice);
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
    /// selects <see cref="RandomMapBackendKind.BuiltIn"/> with a non-empty, actionable
    /// <see cref="RandomMapBackendSelection.Notice"/> so the caller can visibly explain the
    /// fallback rather than silently using the built-in engine.</item>
    /// <item>Mapping <see cref="FEMapCreatorMappingStatus.NoMapping"/> selects
    /// <see cref="RandomMapBackendKind.BuiltIn"/> with an empty notice — this is the expected,
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
                    string reason = string.IsNullOrWhiteSpace(mappingLookup.Reason)
                        ? "The saved FEMapCreator tileset mapping is no longer valid."
                        : mappingLookup.Reason;
                    return RandomMapBackendSelection.UseBuiltIn(
                        "The saved FEMapCreator tileset mapping no longer matches the current setup ("
                        + reason
                        + "); used the built-in generator instead.");

                case FEMapCreatorMappingStatus.NoMapping:
                default:
                    return RandomMapBackendSelection.UseBuiltIn("");
            }
        }
    }
}
