// SPDX-License-Identifier: GPL-3.0-or-later
using System;

namespace FEBuilderGBA.Core
{
    /// <summary>
    /// Stable failure classification for the font-library command.  The CLI
    /// maps command/input/environment faults to exit 1 and semantic package
    /// outcomes to exit 2.
    /// </summary>
    public enum FontLibraryFailureKind
    {
        None,
        Usage,
        Schema,
        Path,
        Io,
        Font,
        Provenance,
        MissingGlyph,
        Conflict,
        Exhaustion,
        Tamper,
        Validation,
        RoundtripMismatch,
    }

    public static class FontLibraryFailure
    {
        public static int ExitCode(FontLibraryFailureKind kind)
            => IsSemantic(kind) ? 2 : kind == FontLibraryFailureKind.None ? 0 : 1;

        public static bool IsSemantic(FontLibraryFailureKind kind)
            => kind == FontLibraryFailureKind.MissingGlyph
                || kind == FontLibraryFailureKind.Conflict
                || kind == FontLibraryFailureKind.Exhaustion
                || kind == FontLibraryFailureKind.Tamper
                || kind == FontLibraryFailureKind.Validation
                || kind == FontLibraryFailureKind.RoundtripMismatch;
    }

    internal sealed class FontLibraryOperationException : Exception
    {
        internal FontLibraryOperationException(
            FontLibraryFailureKind kind,
            string message)
            : base(message)
        {
            Kind = kind;
        }

        internal FontLibraryFailureKind Kind { get; }
    }
}
