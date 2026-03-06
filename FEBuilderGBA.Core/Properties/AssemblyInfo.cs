using System.Reflection;
using System.Runtime.CompilerServices;

// Allow WinForms app to access internal types (U, Log, etc.)
// Tests access Core types via WinForms project reference (public types only).
[assembly: InternalsVisibleTo("FEBuilderGBA")]
[assembly: InternalsVisibleTo("FEBuilderGBA.CLI")]
[assembly: InternalsVisibleTo("FEBuilderGBA.Core.Tests")]
[assembly: InternalsVisibleTo("FEBuilderGBA.Avalonia")]

// Auto-versioning: Build = days since 2000-01-01, Revision = seconds/2 since midnight.
// This matches the WinForms AssemblyInfo.cs pattern so U.getVersion() returns a real date.
#if DEBUG
[assembly: AssemblyVersion("1.0.0.0")]
#else
[assembly: AssemblyVersion("1.0.*")]
#endif
