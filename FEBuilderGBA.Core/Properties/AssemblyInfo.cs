using System.Runtime.CompilerServices;

// Allow WinForms app to access internal types (U, Log, etc.)
// Tests access Core types via WinForms project reference (public types only).
[assembly: InternalsVisibleTo("FEBuilderGBA")]
[assembly: InternalsVisibleTo("FEBuilderGBA.CLI")]
[assembly: InternalsVisibleTo("FEBuilderGBA.Core.Tests")]
