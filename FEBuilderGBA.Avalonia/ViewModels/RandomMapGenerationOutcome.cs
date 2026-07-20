#nullable enable

using System;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    /// <summary>
    /// Backend-neutral outcome of one random-map generation attempt (#1978 Slice 3), consumed by
    /// <see cref="FEBuilderGBA.Avalonia.Services.GenerateRandomMapWorkflow"/>'s apply/rollback
    /// helpers regardless of whether <see cref="Mars"/> came from the built-in engine or the
    /// external FEMapCreator adapter. Replaces the dialog-specific
    /// <c>GenerateRandomMapDialogResult</c> retired with the one-click Map Editor button.
    /// </summary>
    public sealed class RandomMapGenerationOutcome
    {
        public ushort[] Mars { get; init; } = Array.Empty<ushort>();
        public int Width { get; init; }
        public int Height { get; init; }

        /// <summary>
        /// The user/caller-supplied seed that produced <see cref="Mars"/> — directly replayable:
        /// generating again with this exact value reproduces the same layout. Never an
        /// internally-derived per-restart value.
        /// </summary>
        public int EffectiveSeed { get; init; }
    }

    /// <summary>One tileset choice offered by the explicit "map this tileset" action (#1978 Slice 3).</summary>
    public sealed class FEMapCreatorTilesetOption
    {
        public string Name { get; init; } = "";
        public string Diagnostic { get; init; } = "";
        public string ImagePath { get; init; } = "";
        public string GenerationDataPath { get; init; } = "";
        public override string ToString() => Name;
    }
}
