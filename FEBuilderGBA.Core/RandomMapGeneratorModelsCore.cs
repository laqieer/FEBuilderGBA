// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Collections.Generic;
using System.Threading;

namespace FEBuilderGBA
{
    /// <summary>
    /// Injectable process-runner seam for FEMapCreator shell-outs.
    /// Defaults to <see cref="ProcessRunnerCore.Run"/> when a caller passes null.
    /// </summary>
    public delegate ProcessRunResult ProcessRunnerDelegate(
        string command,
        IEnumerable<string> args,
        string workingDir,
        int timeoutMs,
        int maximumOutputChars);

    /// <summary>
    /// Cancellation-aware variant of <see cref="ProcessRunnerDelegate"/> (#1978 Slice 3
    /// review). When a caller does not override this seam, it defaults to the exact-arity
    /// <see cref="ProcessRunnerCore.Run(string, IEnumerable{string}, string, int, int, CancellationToken)"/>
    /// overload, which owns and terminates its own process on cancellation instead of merely
    /// abandoning an awaited <see cref="System.Threading.Tasks.Task"/>.
    /// </summary>
    public delegate ProcessRunResult ProcessRunnerCancellableDelegate(
        string command,
        IEnumerable<string> args,
        string workingDir,
        int timeoutMs,
        int maximumOutputChars,
        CancellationToken cancellationToken);

    /// <summary>FEMapCreator generation algorithms supported by its public CLI.</summary>
    public static class RandomMapGeneratorAlgorithms
    {
        public const string Experimental = "experimental";
        public const string Legacy = "legacy";
        public const string Hybrid = "hybrid";
        public const string Default = Experimental;

        static readonly string[] Values = { Experimental, Legacy, Hybrid };

        public static IReadOnlyList<string> All { get; } = Array.AsReadOnly(Values);

        public static bool TryNormalize(string value, out string normalized)
        {
            normalized = "";
            if (string.IsNullOrWhiteSpace(value))
                return false;

            string candidate = value.Trim();
            foreach (string supported in Values)
            {
                if (string.Equals(candidate, supported, StringComparison.OrdinalIgnoreCase))
                {
                    normalized = supported;
                    return true;
                }
            }
            return false;
        }
    }

    /// <summary>
    /// Failure categories surfaced by the FEMapCreator random-map adapter.
    /// </summary>
    public enum RandomMapGeneratorErrorCategory
    {
        /// <summary>No failure.</summary>
        None,
        /// <summary>A required path or request field was invalid.</summary>
        InvalidPath,
        /// <summary>A managed FEMapCreator DLL could not be launched because the .NET host was unavailable.</summary>
        HostUnavailable,
        /// <summary>The process could not be started.</summary>
        ProcessStartFailed,
        /// <summary>The process exceeded the timeout and was terminated.</summary>
        TimedOut,
        /// <summary>The process exceeded the bounded stdout/stderr capture limit.</summary>
        OutputLimitExceeded,
        /// <summary>The process exited with a non-zero exit code.</summary>
        NonZeroExit,
        /// <summary>The expected output file was not produced.</summary>
        OutputMissing,
        /// <summary>The generated tileset or its assets were incompatible with FEBuilderGBA.</summary>
        IncompatibleTileset,
        /// <summary>Generated output or discovery JSON could not be parsed safely.</summary>
        ParseFailed,
        /// <summary>The caller's <see cref="CancellationToken"/> was signalled; any in-flight or
        /// just-completed process result was discarded and must not be applied.</summary>
        Cancelled,
    }

    internal static class FEMapCreatorProcessDiagnosticCore
    {
        const int MaximumDetailChars = 4096;

        internal static string AppendNonZeroExitDetail(
            string baseMessage,
            ProcessRunResult processResult)
        {
            string detail;
            if (!string.IsNullOrWhiteSpace(processResult.Stderr))
                detail = processResult.Stderr;
            else if (!string.IsNullOrWhiteSpace(processResult.Stdout))
                detail = processResult.Stdout;
            else
                detail = processResult.ErrorMessage;
            if (string.IsNullOrWhiteSpace(detail))
                return baseMessage ?? "";

            detail = detail.Trim();
            if (detail.Length > MaximumDetailChars)
            {
                detail = detail.Substring(
                    detail.Length - MaximumDetailChars,
                    MaximumDetailChars);
            }

            return (baseMessage ?? "")
                + Environment.NewLine
                + "FEMapCreator detail: "
                + detail;
        }
    }

    /// <summary>
    /// Parameters for one FEMapCreator random-map generation request.
    /// </summary>
    public sealed class RandomMapGenerationRequest
    {
        /// <summary>Requested map width in tiles.</summary>
        public int Width { get; set; }

        /// <summary>Requested map height in tiles.</summary>
        public int Height { get; set; }

        /// <summary>Tileset name passed to FEMapCreator.</summary>
        public string TilesetName { get; set; } = "";

        /// <summary>Algorithm name passed to FEMapCreator.</summary>
        public string Algorithm { get; set; } = RandomMapGeneratorAlgorithms.Default;

        /// <summary>Deterministic generation seed passed to FEMapCreator.</summary>
        public int Seed { get; set; }

        /// <summary>
        /// User-selected FEMapCreator program path. Must resolve to an existing absolute local
        /// Windows <c>.exe</c>, managed <c>.dll</c>, or executable native file on non-Windows.
        /// </summary>
        public string FEMapCreatorPath { get; set; } = "";

        /// <summary>
        /// Optional absolute assets directory override passed as <c>--assets-dir</c>.
        /// When blank, FEMapCreator uses its own default asset root.
        /// </summary>
        public string AssetsDir { get; set; } = "";

    }

    /// <summary>
    /// Result of one FEMapCreator random-map generation attempt.
    /// </summary>
    public sealed class RandomMapGenerationResult
    {
        ushort[] _mars = Array.Empty<ushort>();

        /// <summary>True when map generation and MAR parsing succeeded.</summary>
        public bool Success { get; set; }

        /// <summary>Typed failure category, or <see cref="RandomMapGeneratorErrorCategory.None"/> on success.</summary>
        public RandomMapGeneratorErrorCategory ErrorCategory { get; set; }

        /// <summary>Human-readable failure detail, or a short success summary.</summary>
        public string ErrorMessage { get; set; } = "";

        /// <summary>Converted FEBuilder MAR values in row-major order. Empty on failure.</summary>
        public ushort[] Mars
        {
            get => _mars.Length == 0 ? Array.Empty<ushort>() : (ushort[])_mars.Clone();
            set => _mars = value == null || value.Length == 0
                ? Array.Empty<ushort>()
                : (ushort[])value.Clone();
        }

        internal ushort[] MarsBuffer => _mars;

        /// <summary>Bounded tail of FEMapCreator standard output for diagnostics.</summary>
        public string StdoutTail { get; set; } = "";

        /// <summary>Bounded tail of FEMapCreator standard error for diagnostics.</summary>
        public string StderrTail { get; set; } = "";

        /// <summary>Process exit code when available; -1 when the process never started.</summary>
        public int ExitCode { get; set; } = -1;
    }

    /// <summary>
    /// One tileset entry returned by FEMapCreator <c>tilesets list --json</c>.
    /// </summary>
    public sealed class FEMapCreatorTilesetInfo
    {
        /// <summary>Logical FEMapCreator tileset name.</summary>
        public string Name { get; set; } = "";

        /// <summary>Raw <c>imagePath</c> value reported by FEMapCreator.</summary>
        public string ImagePath { get; set; } = "";

        /// <summary>Raw <c>generationDataPath</c> value reported by FEMapCreator.</summary>
        public string GenerationDataPath { get; set; } = "";

        /// <summary>True when FEMapCreator reported that the image asset exists.</summary>
        public bool HasImage { get; set; }

        /// <summary>True when FEMapCreator reported that the generation-data asset exists.</summary>
        public bool HasGenerationData { get; set; }

        /// <summary>Diagnostic detail from FEMapCreator or FEBuilderGBA path/PNG validation.</summary>
        public string Diagnostic { get; set; } = "";

        /// <summary>Confined absolute image path under the effective asset root, when valid.</summary>
        public string ResolvedImagePath { get; set; } = "";

        /// <summary>Confined absolute generation-data path under the effective asset root, when valid.</summary>
        public string ResolvedGenerationDataPath { get; set; } = "";

        /// <summary>Tileset PNG width read from IHDR, or 0 when unavailable.</summary>
        public int ImageWidth { get; set; }

        /// <summary>Tileset PNG height read from IHDR, or 0 when unavailable.</summary>
        public int ImageHeight { get; set; }

        /// <summary>
        /// True when the tileset image parsed successfully and is exactly 512 pixels wide
        /// (32 chipset columns × 16 pixels per chipset).
        /// </summary>
        public bool IsCompatible { get; set; }

        /// <summary>True when the FEMapCreator-reported asset pair is present, confined, and undiagnosed.</summary>
        public bool IsComplete =>
            !string.IsNullOrWhiteSpace(Name)
            && HasImage
            && HasGenerationData
            && !string.IsNullOrWhiteSpace(ResolvedImagePath)
            && !string.IsNullOrWhiteSpace(ResolvedGenerationDataPath)
            && string.IsNullOrWhiteSpace(Diagnostic);

        /// <summary>True when the tileset is complete and FEBuilder-compatible.</summary>
        public bool IsUsable => IsComplete && IsCompatible;
    }

    /// <summary>
    /// Result of one FEMapCreator tileset-discovery attempt.
    /// </summary>
    public sealed class FEMapCreatorTilesetDiscoveryResult
    {
        /// <summary>True when the discovery command ran and its JSON parsed successfully.</summary>
        public bool Success { get; set; }

        /// <summary>Typed failure category, or <see cref="RandomMapGeneratorErrorCategory.None"/> on success.</summary>
        public RandomMapGeneratorErrorCategory ErrorCategory { get; set; }

        /// <summary>Human-readable failure detail, or a short success summary.</summary>
        public string ErrorMessage { get; set; } = "";

        /// <summary>Raw top-level <c>assetsRoot</c> reported by FEMapCreator.</summary>
        public string AssetsRoot { get; set; } = "";

        /// <summary>Effective absolute asset root actually used for confinement checks.</summary>
        public string EffectiveAssetsRoot { get; set; } = "";

        /// <summary>All parsed tileset entries, including incomplete or incompatible entries.</summary>
        public List<FEMapCreatorTilesetInfo> Tilesets { get; } = new List<FEMapCreatorTilesetInfo>();

        /// <summary>Only complete, compatible tilesets safe for generation.</summary>
        public List<FEMapCreatorTilesetInfo> UsableTilesets { get; } = new List<FEMapCreatorTilesetInfo>();

        /// <summary>Bounded tail of FEMapCreator standard output for diagnostics.</summary>
        public string StdoutTail { get; set; } = "";

        /// <summary>Bounded tail of FEMapCreator standard error for diagnostics.</summary>
        public string StderrTail { get; set; } = "";

        /// <summary>Process exit code when available; -1 when the process never started.</summary>
        public int ExitCode { get; set; } = -1;
    }
}
