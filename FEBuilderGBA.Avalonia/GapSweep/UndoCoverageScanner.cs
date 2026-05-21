// SPDX-License-Identifier: GPL-3.0-or-later
// FEBuilderGBA gap-sweep tooling (#374) — Phase 5: undo coverage sweep.
//
// WinForms is the ground truth for ROM-write undo: every WF call to
// `Program.ROM.SetU8/16/32(addr, val, undo)` takes an Undo argument
// explicitly, so the compiler enforces undo plumbing at every callsite.
// Avalonia uses a different pattern — `UndoService.Begin(name)` opens a
// scope that `rom.write_u*` calls register against automatically — but the
// migration applied this only inside `EventScriptPopupViewModel`. Every
// other AV write currently bypasses the undo buffer entirely.
//
// This scanner is a static analyzer that surfaces every Avalonia-side ROM
// write callsite and classifies it into one of four coverage tiers so the
// downstream fix-PRs can attack them in priority order:
//
//   - NoUndoServiceField — VM has no `UndoService` field/property/local at
//     all; the highest-priority backlog because the plumbing itself is
//     missing.
//   - MissingScope       — VM has an UndoService field but THIS particular
//     write is not surrounded by a `Begin(...)` call in the enclosing
//     method body.
//   - AmbiguousScope     — The write lives in a helper method whose caller
//     might (or might not) wrap a Begin/Commit scope around it. The
//     scanner uses a one-level-deep name-match heuristic; manual review is
//     required to decide.
//   - Covered            — Same method body contains both a Begin and the
//     write, plus a matching Commit/Rollback somewhere in the method.
//
// Critical safety: this is a static analyzer. It does NOT modify any
// production write callsite. Phase 5 produces the inventory; downstream
// PRs fix the gaps one VM at a time.
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace FEBuilderGBA.Avalonia.GapSweep
{
    /// <summary>
    /// Classification of one write callsite's undo coverage. The ordering
    /// of these enum values is significant — see <see cref="UndoCoverageScanner.Scan"/>
    /// which sorts rows so the highest-priority tiers (NoUndoServiceField,
    /// MissingScope) surface first in reports.
    /// </summary>
    public enum UndoCoverage
    {
        /// <summary>Class has no UndoService field at all — the deepest gap.</summary>
        NoUndoServiceField,
        /// <summary>Class has UndoService but this write is not inside a Begin scope.</summary>
        MissingScope,
        /// <summary>Write lives in a helper; caller may or may not wrap a scope. Verify manually.</summary>
        AmbiguousScope,
        /// <summary>Write is inside a Begin/Commit (or Begin/Rollback) scope in the same method. Healthy.</summary>
        Covered,
    }

    /// <summary>
    /// One ROM-write callsite discovered by the Roslyn scan.
    /// </summary>
    /// <param name="FilePath">Repo-relative path to the .cs file.</param>
    /// <param name="Line">1-based source line number of the write expression.</param>
    /// <param name="EnclosingClass">Class declaration name (e.g. "ItemEditorViewModel").</param>
    /// <param name="EnclosingMethod">Method declaration name (e.g. "Save"). "" when the write is in a property setter or field initialiser.</param>
    /// <param name="WriteExpression">Source text of the invocation (e.g. "rom.write_u16(addr, val)").</param>
    /// <param name="Coverage">Classification tier — see <see cref="UndoCoverage"/>.</param>
    /// <param name="CoverageNote">Short human-readable explanation of WHY this tier was chosen.</param>
    public record WriteCallsite(
        string FilePath,
        int Line,
        string EnclosingClass,
        string EnclosingMethod,
        string WriteExpression,
        UndoCoverage Coverage,
        string? CoverageNote);

    /// <summary>
    /// Phase 5 scanner: surfaces every ROM-write callsite in
    /// `FEBuilderGBA.Avalonia/` that is NOT wrapped in an
    /// <c>UndoService.Begin/Commit/Rollback</c> scope. WinForms is the
    /// ground truth — every WF <c>SetU*</c> takes an Undo arg.
    /// </summary>
    public static class UndoCoverageScanner
    {
        /// <summary>
        /// Methods on the receiver `rom` (instance variable convention),
        /// `Program.ROM`, or `ROM` that count as ROM writes. We deliberately
        /// keep this list narrow — see <see cref="IsRomWriteMethod"/> for
        /// classification logic.
        /// </summary>
        static readonly HashSet<string> WriteMethodNames = new(StringComparer.Ordinal)
        {
            "write_u8", "write_u16", "write_u32", "write_p32",
            "SetU8", "SetU16", "SetU32",
            "SetData",
        };

        /// <summary>
        /// Receiver names that the scanner treats as ROM references when
        /// resolving the receiver of a write method call. We accept three
        /// patterns: local variable `rom` (common in Avalonia VMs that hoist
        /// `var rom = CoreState.ROM` or take it as a method argument),
        /// `Program.ROM` (WinForms-style global), and bare `ROM` (the Core
        /// static class accessor — uncommon but seen).
        /// </summary>
        static readonly HashSet<string> RomReceiverNames = new(StringComparer.Ordinal)
        {
            "rom", "ROM",
        };

        /// <summary>
        /// Names the scanner accepts as UndoService field/property/local
        /// identifiers when resolving a Begin/Commit/Rollback receiver.
        /// The intent is to mirror the codebase convention without
        /// requiring a semantic model: VMs either expose `_undoService` (a
        /// private field), `UndoService` (a property/local), or
        /// `undoService` (a local — used by EventScriptPopupViewModel).
        /// </summary>
        static readonly HashSet<string> UndoServiceMemberNames = new(StringComparer.Ordinal)
        {
            "_undoService", "undoService", "UndoService",
        };

        /// <summary>
        /// Run the full scan against <paramref name="repoRoot"/>. Returns a
        /// deterministically-ordered list of <see cref="WriteCallsite"/>s
        /// suitable for both the markdown report and the xunit Theory data
        /// source.
        ///
        /// Ordering: <see cref="UndoCoverage"/> tier ascending (so
        /// NoUndoServiceField surfaces first, Covered last), then by class
        /// name and line number.
        /// </summary>
        public static IReadOnlyList<WriteCallsite> Scan(string repoRoot)
        {
            if (string.IsNullOrEmpty(repoRoot))
                throw new ArgumentException("repoRoot must be non-empty", nameof(repoRoot));
            if (!Directory.Exists(repoRoot))
                return Array.Empty<WriteCallsite>();

            string avRoot = Path.Combine(repoRoot, "FEBuilderGBA.Avalonia");
            if (!Directory.Exists(avRoot))
                return Array.Empty<WriteCallsite>();

            // Glob every .cs file under FEBuilderGBA.Avalonia/, EXCLUDING
            // GapSweep/ (the scanners themselves do not write to ROM) and
            // any obj/ / bin/ build output. We skip the Tests project by
            // construction: Tests lives in a sibling project, not under
            // FEBuilderGBA.Avalonia/.
            var files = Directory
                .GetFiles(avRoot, "*.cs", SearchOption.AllDirectories)
                .Where(f => !IsExcludedPath(f))
                .ToArray();

            var callsites = new ConcurrentBag<WriteCallsite>();
            Parallel.ForEach(files, file =>
            {
                try
                {
                    string code = File.ReadAllText(file);
                    string relPath = MakeRepoRelative(repoRoot, file);
                    foreach (var c in ExtractCallsitesFromSource(code, relPath))
                        callsites.Add(c);
                }
                catch
                {
                    // File-level failures are tolerated — the per-file
                    // try/catch keeps the sweep robust against malformed
                    // or in-flight edits. Phase 7 will surface error rows
                    // explicitly.
                }
            });

            return callsites
                .OrderBy(c => (int)c.Coverage)
                .ThenBy(c => c.EnclosingClass, StringComparer.Ordinal)
                .ThenBy(c => c.FilePath, StringComparer.Ordinal)
                .ThenBy(c => c.Line)
                .ToList();
        }

        /// <summary>
        /// True for files we deliberately skip during the scan. Currently:
        /// GapSweep scanners (they don't write to ROM by construction),
        /// build output (obj/, bin/), Designer.cs (Avalonia doesn't have
        /// these but be defensive), and the GapSweep namespace itself
        /// regardless of physical path.
        /// </summary>
        static bool IsExcludedPath(string path)
        {
            // Normalize separators so the substring checks work on both
            // Windows ("\\GapSweep\\") and POSIX ("/GapSweep/") layouts.
            string normalized = path.Replace('\\', '/');
            if (normalized.Contains("/GapSweep/", StringComparison.Ordinal))
                return true;
            if (normalized.Contains("/obj/", StringComparison.Ordinal))
                return true;
            if (normalized.Contains("/bin/", StringComparison.Ordinal))
                return true;
            return false;
        }

        /// <summary>
        /// Convert an absolute path to repo-relative form for stable
        /// rendering in the report. Falls back to the absolute path when
        /// the file lives outside <paramref name="repoRoot"/> (shouldn't
        /// happen in production but is defensive against symlinks).
        /// </summary>
        static string MakeRepoRelative(string repoRoot, string absolute)
        {
            try
            {
                string full = Path.GetFullPath(absolute);
                string rootFull = Path.GetFullPath(repoRoot);
                if (full.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
                {
                    string rel = full.Substring(rootFull.Length).TrimStart('\\', '/');
                    return rel.Replace('\\', '/');
                }
                return absolute.Replace('\\', '/');
            }
            catch
            {
                return absolute.Replace('\\', '/');
            }
        }

        /// <summary>
        /// Extract ROM-write callsites from a single source file. Exposed
        /// for unit testing without touching the file system. The
        /// <paramref name="relPath"/> is the value that will appear in the
        /// <see cref="WriteCallsite.FilePath"/> column — tests typically
        /// pass a synthetic "Test.cs" name.
        /// </summary>
        public static IReadOnlyList<WriteCallsite> ExtractCallsitesFromSource(string sourceCode, string relPath)
        {
            if (string.IsNullOrEmpty(sourceCode))
                return Array.Empty<WriteCallsite>();

            SyntaxTree tree;
            SyntaxNode root;
            try
            {
                tree = CSharpSyntaxTree.ParseText(sourceCode);
                root = tree.GetRoot();
            }
            catch
            {
                return Array.Empty<WriteCallsite>();
            }

            var list = new List<WriteCallsite>();

            // First pass: gather every InvocationExpressionSyntax in the
            // file. We process each as a potential write, then classify
            // its undo coverage using the surrounding class + method
            // structure.
            foreach (var inv in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                if (!TryAnalyzeWriteInvocation(inv, out string writeExpr))
                    continue;

                ClassDeclarationSyntax? cls = FindEnclosingClass(inv);
                if (cls == null)
                    continue;
                string className = cls.Identifier.Text;

                // Find the enclosing method (or "" if the write lives in a
                // property setter, field initializer, etc.).
                MethodDeclarationSyntax? method = FindEnclosingMethod(inv);
                string methodName = method?.Identifier.Text ?? "";

                int line = inv.GetLocation().GetLineSpan().StartLinePosition.Line + 1;

                var (coverage, note) = ClassifyCoverage(inv, cls, method);
                list.Add(new WriteCallsite(
                    FilePath: relPath,
                    Line: line,
                    EnclosingClass: className,
                    EnclosingMethod: methodName,
                    WriteExpression: writeExpr,
                    Coverage: coverage,
                    CoverageNote: note));
            }

            return list;
        }

        /// <summary>
        /// True when <paramref name="inv"/> is a recognised ROM-write call.
        /// Sets <paramref name="writeExpr"/> to the invocation's source
        /// text so the report can render the exact line.
        ///
        /// We accept the following call shapes:
        ///   - <c>rom.write_u*(...)</c> / <c>rom.SetU*(...)</c>  — instance via
        ///     local variable named "rom".
        ///   - <c>Program.ROM.write_u*(...)</c> / <c>Program.ROM.SetU*(...)</c>
        ///     — WinForms-style global access.
        ///   - <c>ROM.write_u*(...)</c> / <c>ROM.SetU*(...)</c>             — Core
        ///     static accessor.
        ///   - <c>EditorFormRef.WriteFields(rom, addr, values, fields)</c> — the
        ///     codebase's conventional bulk-write helper. Many Avalonia VMs
        ///     funnel every ROM write through this single call, so missing
        ///     it would under-report. We treat the call itself as the write
        ///     callsite (one row per WriteFields invocation) regardless of
        ///     how many actual byte-writes the helper executes internally.
        ///   - <c>EditorFormRef.WriteField(rom, addr, value, …)</c> — singular
        ///     variant of WriteFields.
        ///
        /// <c>SetData</c> is also accepted but only when the receiver is one
        /// of the ROM patterns above (we do NOT match generic
        /// <c>foo.SetData(...)</c> calls).
        /// </summary>
        public static bool TryAnalyzeWriteInvocation(InvocationExpressionSyntax inv, out string writeExpr)
        {
            writeExpr = "";
            if (inv == null) return false;
            if (inv.Expression is not MemberAccessExpressionSyntax memberAccess)
                return false;

            string methodName = memberAccess.Name switch
            {
                IdentifierNameSyntax id => id.Identifier.Text,
                GenericNameSyntax gn => gn.Identifier.Text,
                _ => "",
            };

            // Receiver of the invocation. Used by both the ROM-receiver
            // check below and the EditorFormRef short-circuit.
            ExpressionSyntax receiver = memberAccess.Expression;

            // EditorFormRef.WriteFields / WriteField — the helper-funnel
            // pattern used by ~100 AV ViewModels. The receiver must be the
            // exact identifier "EditorFormRef" (qualified or unqualified),
            // and the call must pass at least one argument (the ROM
            // reference). We don't try to verify the ROM-ness of the first
            // argument here — the pattern is conventional and false
            // positives would be implausible.
            if (IsEditorFormRefWriteCall(memberAccess, methodName))
            {
                if (inv.ArgumentList.Arguments.Count == 0)
                    return false;
                writeExpr = CompactExpression(inv.ToString());
                return true;
            }

            if (!IsRomWriteMethod(methodName))
                return false;

            // Receiver must resolve to a ROM instance. We accept three
            // patterns (see XML doc for the method).
            if (!IsRomReceiver(receiver))
                return false;

            // Render the invocation text — Roslyn's ToString preserves
            // whitespace within the syntax span which is good enough for
            // report rendering. We trim and condense internal whitespace
            // so a multi-line call surfaces as a single-line cell.
            writeExpr = CompactExpression(inv.ToString());
            return true;
        }

        /// <summary>
        /// True when the member-access is
        /// <c>EditorFormRef.WriteFields(...)</c> or
        /// <c>EditorFormRef.WriteField(...)</c>. Trailing-identifier match
        /// also accepts a fully-qualified receiver
        /// (<c>FEBuilderGBA.EditorFormRef.WriteFields</c>).
        /// </summary>
        static bool IsEditorFormRefWriteCall(MemberAccessExpressionSyntax memberAccess, string methodName)
        {
            if (methodName != "WriteFields" && methodName != "WriteField")
                return false;
            string receiverName = memberAccess.Expression switch
            {
                IdentifierNameSyntax id => id.Identifier.Text,
                MemberAccessExpressionSyntax m => m.Name.Identifier.Text,
                _ => "",
            };
            return receiverName == "EditorFormRef";
        }

        /// <summary>
        /// Compact whitespace inside an invocation source span so the
        /// report's table cell renders cleanly. Multi-line invocations are
        /// collapsed to a single line; runs of whitespace become a single
        /// space.
        /// </summary>
        static string CompactExpression(string expr)
        {
            if (string.IsNullOrEmpty(expr)) return "";
            var sb = new StringBuilder(expr.Length);
            bool inSpace = false;
            foreach (char c in expr)
            {
                if (c == '\r' || c == '\n' || c == '\t' || c == ' ')
                {
                    if (!inSpace)
                    {
                        sb.Append(' ');
                        inSpace = true;
                    }
                }
                else
                {
                    sb.Append(c);
                    inSpace = false;
                }
            }
            return sb.ToString().Trim();
        }

        /// <summary>Recognise a method name as a ROM-write API.</summary>
        public static bool IsRomWriteMethod(string methodName)
        {
            if (string.IsNullOrEmpty(methodName)) return false;
            return WriteMethodNames.Contains(methodName);
        }

        /// <summary>
        /// True when <paramref name="receiver"/> appears to refer to a ROM
        /// instance. Matches:
        /// <list type="bullet">
        ///   <item><c>rom</c> — local/field/property of any type named "rom".</item>
        ///   <item><c>ROM</c> — bare static accessor (uncommon).</item>
        ///   <item><c>Program.ROM</c> — WinForms-style global (legacy).</item>
        ///   <item><c>CoreState.ROM</c> — the Avalonia-native global accessor
        ///     used by VMs that don't hoist into a local. Identified after
        ///     Copilot PR #380 review concern #1 — Avalonia's
        ///     <c>SMEPromoListViewModel</c> writes through this receiver and
        ///     the original scanner missed those rows.</item>
        /// </list>
        /// Anything else returns false — a generic <c>foo.bar.SetData(...)</c>
        /// is NOT a ROM write.
        /// </summary>
        public static bool IsRomReceiver(ExpressionSyntax receiver)
        {
            return receiver switch
            {
                IdentifierNameSyntax id => RomReceiverNames.Contains(id.Identifier.Text),
                // `Program.ROM` / `CoreState.ROM` — outer = the holder
                // identifier, inner identifier = ROM.
                MemberAccessExpressionSyntax m => IsRomMemberAccess(m),
                _ => false,
            };
        }

        /// <summary>
        /// True when the member-access names <c>{X}.ROM</c> where X is one
        /// of the recognised ROM-host identifiers (<c>Program</c>,
        /// <c>CoreState</c>). The host-identifier check accepts both bare
        /// <c>Program.ROM</c> and fully-qualified
        /// <c>FEBuilderGBA.Program.ROM</c> / <c>FEBuilderGBA.CoreState.ROM</c>.
        /// </summary>
        static bool IsRomMemberAccess(MemberAccessExpressionSyntax m)
        {
            string name = m.Name.Identifier.Text;
            if (name != "ROM") return false;
            // Receiver of `.ROM` should be one of the recognised host
            // identifiers. The trailing identifier of a possibly-qualified
            // receiver is what matters.
            string holder = m.Expression switch
            {
                IdentifierNameSyntax id => id.Identifier.Text,
                MemberAccessExpressionSyntax inner => inner.Name.Identifier.Text,
                _ => "",
            };
            return holder == "Program" || holder == "CoreState";
        }

        /// <summary>
        /// Classify the undo coverage of a write callsite. The decision
        /// tree (in priority order):
        ///
        /// <list type="number">
        ///   <item>If the enclosing class has no UndoService field, this is
        ///     <see cref="UndoCoverage.NoUndoServiceField"/> — the deepest
        ///     gap. The whole VM is unplumbed; no individual fix will work
        ///     without first introducing a service field.</item>
        ///   <item>If there's no enclosing method (write is in a property
        ///     setter, field initialiser, etc.) we also flag <c>MissingScope</c>
        ///     — those callsites can't be wrapped without restructuring.</item>
        ///   <item>If the enclosing method body contains a <c>Begin(...)</c>
        ///     call before the write AND a <c>Commit()</c>/<c>Rollback()</c>
        ///     anywhere in the method (or its try/catch/finally), this is
        ///     <see cref="UndoCoverage.Covered"/>.</item>
        ///   <item>Special case: the write may also be Covered if it
        ///     passes an Undo argument explicitly (WinForms-style
        ///     <c>SetU*(addr, val, undo)</c> with 3 args). This recognises
        ///     the alternate undo pattern.</item>
        ///   <item>If the method has no scope but the caller of the method
        ///     does (one-level helper indirection), this is
        ///     <see cref="UndoCoverage.AmbiguousScope"/>.</item>
        ///   <item>Otherwise <see cref="UndoCoverage.MissingScope"/>.</item>
        /// </list>
        ///
        /// <paramref name="cls"/> is the enclosing class declaration;
        /// <paramref name="method"/> may be null when the write lives
        /// outside any method (property setter, field initialiser).
        /// </summary>
        public static (UndoCoverage Coverage, string Note) ClassifyCoverage(
            InvocationExpressionSyntax inv,
            ClassDeclarationSyntax cls,
            MethodDeclarationSyntax? method)
        {
            bool classHasUndoService = ClassHasUndoServiceMember(cls);

            // Tier 4 fallback first: explicit-Undo-arg pattern (WinForms-
            // style SetU8/16/32 with a trailing Undo argument) is Covered
            // regardless of scope.
            if (HasExplicitUndoArgument(inv))
                return (UndoCoverage.Covered, "explicit Undo argument");

            // No enclosing method: write is in a property setter or field
            // initializer. Can't be wrapped in a scope without restructuring.
            if (method == null)
            {
                return (
                    classHasUndoService
                        ? UndoCoverage.MissingScope
                        : UndoCoverage.NoUndoServiceField,
                    "write outside any method body");
            }

            // No UndoService anywhere on the class: deepest gap.
            if (!classHasUndoService)
            {
                return (UndoCoverage.NoUndoServiceField,
                    $"class '{cls.Identifier.Text}' has no UndoService field/property/local");
            }

            // Determine whether the write is INSIDE an open Begin/Commit (or
            // Begin/Rollback) region in source order. The previous
            // implementation accepted "Begin before write AND any Commit
            // anywhere" — but that classified a write AFTER an already-
            // committed scope as Covered, which hides real gaps. Copilot
            // PR #380 review concern #2 caught this; the new check
            // tracks Begin/Commit/Rollback positions in source order and
            // verifies the write falls between an open Begin and its
            // next-following Commit or Rollback.
            var (insideOpenScope, hadScope) = IsWriteInsideOpenUndoScope(method, inv);
            if (insideOpenScope)
            {
                return (UndoCoverage.Covered, "write is inside an active Begin/Commit scope");
            }
            // If the method has any UndoService activity at all
            // (Begin/Commit/Rollback) but this particular write is outside
            // every scope, it's a leak — surface as MissingScope rather
            // than falling through to the caller-helper check.
            if (hadScope)
            {
                return (UndoCoverage.MissingScope,
                    $"method '{method.Identifier.Text}' has an UndoService scope but this write is OUTSIDE it");
            }

            // Caller-helper heuristic: this method has no Begin, but is it
            // called by another method in the same class that itself wraps
            // Begin/Commit? If so, classify Ambiguous (the caller's scope
            // applies transitively).
            if (HasCallerWithBeginCommit(cls, method))
                return (UndoCoverage.AmbiguousScope,
                    $"helper '{method.Identifier.Text}' is called from a method that wraps Begin/Commit (verify manually)");

            return (UndoCoverage.MissingScope,
                $"method '{method.Identifier.Text}' has no UndoService.Begin scope");
        }

        /// <summary>
        /// True when the class declaration contains a field or property
        /// whose declared type is <c>UndoService</c>, or a local variable
        /// of that type inside any method body. The check is name-based
        /// (no semantic model) so the type name must match exactly.
        ///
        /// We accept the field/property type "UndoService" directly. We do
        /// NOT accept derived/wrapper types — the codebase convention is
        /// to use UndoService verbatim.
        ///
        /// Edge case: VMs that construct UndoService inline (`var
        /// undoService = new UndoService();`) inside a method body STILL
        /// count as having the plumbing — that pattern is used by
        /// EventScriptPopupViewModel.
        /// </summary>
        public static bool ClassHasUndoServiceMember(ClassDeclarationSyntax cls)
        {
            if (cls == null) return false;

            // 1) Fields of type UndoService.
            foreach (var field in cls.Members.OfType<FieldDeclarationSyntax>())
            {
                if (IsUndoServiceType(field.Declaration.Type))
                    return true;
            }

            // 2) Properties of type UndoService.
            foreach (var prop in cls.Members.OfType<PropertyDeclarationSyntax>())
            {
                if (IsUndoServiceType(prop.Type))
                    return true;
            }

            // 3) Local declarations or `var undoService = new UndoService(...)`
            // anywhere inside the class body. We walk all VariableDeclarators
            // and check either an explicit type annotation or an
            // ObjectCreationExpressionSyntax initializer whose type is
            // UndoService.
            foreach (var decl in cls.DescendantNodes().OfType<VariableDeclarationSyntax>())
            {
                if (IsUndoServiceType(decl.Type))
                    return true;
                foreach (var v in decl.Variables)
                {
                    if (v.Initializer?.Value is ObjectCreationExpressionSyntax oc &&
                        IsUndoServiceType(oc.Type))
                        return true;
                }
            }

            // 4) Implicit-typed locals (`var x = new UndoService(...)`).
            foreach (var oc in cls.DescendantNodes().OfType<ObjectCreationExpressionSyntax>())
            {
                if (IsUndoServiceType(oc.Type))
                    return true;
            }

            return false;
        }

        /// <summary>True when the TypeSyntax names the <c>UndoService</c> type.</summary>
        static bool IsUndoServiceType(TypeSyntax type)
        {
            return type switch
            {
                IdentifierNameSyntax id => id.Identifier.Text == "UndoService",
                QualifiedNameSyntax q => q.Right.Identifier.Text == "UndoService",
                _ => false,
            };
        }

        /// <summary>
        /// True when <paramref name="method"/> contains an invocation of
        /// <c>Begin(...)</c> on a recognised UndoService identifier
        /// (<c>_undoService</c>, <c>undoService</c>, or <c>UndoService</c>)
        /// AT OR BEFORE the position of <paramref name="writeNode"/> in
        /// source order.
        ///
        /// Source-order (line) comparison is a simplification: it doesn't
        /// follow actual control flow (e.g. an `if (false) { Begin(); }`
        /// would still register). For the gap-sweep use-case the
        /// simplification is acceptable because the vast majority of real
        /// scopes are linear in source order.
        /// </summary>
        public static bool HasUndoBeginBefore(MethodDeclarationSyntax method, SyntaxNode writeNode)
        {
            if (method == null || writeNode == null) return false;
            int writeLine = writeNode.GetLocation().GetLineSpan().StartLinePosition.Line;
            foreach (var inv in method.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                if (inv == writeNode) continue;
                if (!IsUndoServiceCall(inv, "Begin")) continue;
                int beginLine = inv.GetLocation().GetLineSpan().StartLinePosition.Line;
                if (beginLine <= writeLine)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// True when <paramref name="writeNode"/> falls inside an OPEN
        /// Begin/(Commit|Rollback) region in source order. The check uses
        /// the "bracketing" interpretation:
        /// <list type="bullet">
        ///   <item>Find the LATEST <c>Begin(...)</c> BEFORE the write.</item>
        ///   <item>Find the EARLIEST <c>Commit()</c> or <c>Rollback()</c>
        ///     AFTER the write that is also AFTER the latest pre-write
        ///     Begin.</item>
        ///   <item>If both exist, the write is bracketed by a Begin and a
        ///     matching close — it's inside an active scope.</item>
        /// </list>
        ///
        /// This interpretation correctly handles the common try/catch
        /// pattern: <c>Begin → early-exit Rollback (in an if/return
        /// branch) → main-path writes → Commit → catch { Rollback }</c>.
        /// The intermediate Rollbacks are inside conditional branches; a
        /// pure event-counting approach would mistakenly mark the
        /// main-path writes as outside-scope. The bracketing approach
        /// follows the natural Begin/Commit pairing used by the codebase.
        ///
        /// It also correctly rejects the post-Commit-write pattern Copilot
        /// PR #380 review concern #2 flagged:
        /// <c>Begin; write1; Commit; write2;</c> — write2 has Begin@0
        /// before it AND Commit@2 before it, but NO Commit/Rollback AFTER
        /// the write @3. So write2 is NOT bracketed → MissingScope.
        ///
        /// Returns a tuple: (insideOpenScope, hadAnyScope). The second
        /// flag tells the caller whether the method has ANY scope at all
        /// (so a write outside every scope can be classified MissingScope
        /// rather than NoUndoServiceField when the class IS plumbed).
        ///
        /// Limitations (acknowledged):
        /// <list type="bullet">
        ///   <item>Source-order analysis does not follow actual control
        ///     flow. <c>if (false) { Begin(); } write;</c> would still
        ///     count Begin as bracketing. Real codebases don't write this.</item>
        ///   <item>Nested Begin scopes (Begin → Begin → write) are
        ///     accepted as Covered — UndoService.Begin replaces rather
        ///     than stacks in practice, so the inner scope is the active
        ///     one. The bracketing test is correct for the common case.</item>
        /// </list>
        /// </summary>
        public static (bool InsideOpenScope, bool HadAnyScope) IsWriteInsideOpenUndoScope(
            MethodDeclarationSyntax method, SyntaxNode writeNode)
        {
            if (method == null || writeNode == null) return (false, false);
            int writeLine = writeNode.GetLocation().GetLineSpan().StartLinePosition.Line;
            int writeColumn = writeNode.GetLocation().GetLineSpan().StartLinePosition.Character;

            // Collect all Begin / Commit / Rollback invocations with their
            // source positions. We separate Begin from close events because
            // the bracketing test cares about each independently.
            var beginPositions = new List<(int Line, int Column)>();
            var closePositions = new List<(int Line, int Column)>();
            bool hadAnyScope = false;
            foreach (var inv in method.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                var pos = inv.GetLocation().GetLineSpan().StartLinePosition;
                if (IsUndoServiceCall(inv, "Begin"))
                {
                    beginPositions.Add((pos.Line, pos.Character));
                    hadAnyScope = true;
                }
                else if (IsUndoServiceCall(inv, "Commit") || IsUndoServiceCall(inv, "Rollback"))
                {
                    closePositions.Add((pos.Line, pos.Character));
                    hadAnyScope = true;
                }
            }

            if (!hadAnyScope) return (false, false);

            // Find latest Begin BEFORE the write.
            (int Line, int Column)? latestBeginBefore = null;
            foreach (var b in beginPositions)
            {
                if (ComparePos(b.Line, b.Column, writeLine, writeColumn) < 0)
                {
                    if (latestBeginBefore == null ||
                        ComparePos(b.Line, b.Column, latestBeginBefore.Value.Line, latestBeginBefore.Value.Column) > 0)
                    {
                        latestBeginBefore = b;
                    }
                }
            }
            if (latestBeginBefore == null) return (false, true);

            // Find any Commit/Rollback AFTER the write.
            (int Line, int Column)? earliestCloseAfter = null;
            foreach (var c in closePositions)
            {
                if (ComparePos(c.Line, c.Column, writeLine, writeColumn) > 0)
                {
                    if (earliestCloseAfter == null ||
                        ComparePos(c.Line, c.Column, earliestCloseAfter.Value.Line, earliestCloseAfter.Value.Column) < 0)
                    {
                        earliestCloseAfter = c;
                    }
                }
            }
            if (earliestCloseAfter == null) return (false, true);

            // Both bracket positions exist; the write is inside an active
            // scope.
            return (true, true);
        }

        /// <summary>
        /// Compare two source positions. Returns negative when (l1,c1) is
        /// before (l2,c2), zero when equal, positive when after.
        /// </summary>
        static int ComparePos(int l1, int c1, int l2, int c2)
        {
            int cmp = l1.CompareTo(l2);
            return cmp != 0 ? cmp : c1.CompareTo(c2);
        }

        /// <summary>True when the method body contains any <c>...Commit()</c> on an UndoService.</summary>
        public static bool HasUndoCommit(MethodDeclarationSyntax method)
        {
            if (method == null) return false;
            return method.DescendantNodes().OfType<InvocationExpressionSyntax>()
                .Any(i => IsUndoServiceCall(i, "Commit"));
        }

        /// <summary>True when the method body contains any <c>...Rollback()</c> on an UndoService.</summary>
        public static bool HasUndoRollback(MethodDeclarationSyntax method)
        {
            if (method == null) return false;
            return method.DescendantNodes().OfType<InvocationExpressionSyntax>()
                .Any(i => IsUndoServiceCall(i, "Rollback"));
        }

        /// <summary>
        /// Recognise an invocation as <c>&lt;undoService&gt;.{methodName}(...)</c>
        /// where the receiver is one of the recognised UndoService
        /// identifiers (see <see cref="UndoServiceMemberNames"/>).
        /// </summary>
        static bool IsUndoServiceCall(InvocationExpressionSyntax inv, string methodName)
        {
            if (inv.Expression is not MemberAccessExpressionSyntax memberAccess)
                return false;
            if (memberAccess.Name.Identifier.Text != methodName)
                return false;
            string receiverName = memberAccess.Expression switch
            {
                IdentifierNameSyntax id => id.Identifier.Text,
                MemberAccessExpressionSyntax m => m.Name.Identifier.Text,
                _ => "",
            };
            // We also accept the conventional `this._undoService` /
            // `this.undoService` form.
            if (memberAccess.Expression is MemberAccessExpressionSyntax inner &&
                inner.Expression is ThisExpressionSyntax)
            {
                receiverName = inner.Name.Identifier.Text;
            }
            return UndoServiceMemberNames.Contains(receiverName);
        }

        /// <summary>
        /// True when the write invocation has an explicit trailing Undo
        /// argument — the WinForms-style pattern <c>SetU8(addr, val, undo)</c>.
        /// This is a separate, valid undo coverage form.
        ///
        /// We treat the LAST argument as a candidate Undo if its source
        /// text contains "undo" (case-insensitive) and is not a string
        /// literal or numeric constant. This is heuristic — the
        /// alternative would require a semantic model — but in this
        /// codebase the convention is universally an identifier named
        /// `undo`, `_undo`, etc.
        /// </summary>
        public static bool HasExplicitUndoArgument(InvocationExpressionSyntax inv)
        {
            if (inv == null) return false;
            var args = inv.ArgumentList.Arguments;
            if (args.Count == 0) return false;
            var last = args[args.Count - 1].Expression;
            // Exclude literals — the heuristic is "an identifier expression
            // whose name contains 'undo'".
            if (last is LiteralExpressionSyntax) return false;
            string txt = last.ToString();
            if (string.IsNullOrEmpty(txt)) return false;
            // Be defensive: a string literal containing the substring
            // "undo" passed to a SetU8 would be implausible, but the
            // earlier literal check already filtered. We additionally
            // require the text NOT to start with a quote or digit.
            if (txt[0] == '"' || char.IsDigit(txt[0])) return false;
            return txt.IndexOf("undo", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// One-level-deep caller heuristic. Returns true when any OTHER
        /// method in the same class invokes <paramref name="helper"/> by
        /// name AND that other method itself wraps a Begin/Commit scope
        /// around the call.
        ///
        /// The "by name" check is intentionally string-only — no semantic
        /// model. This means we may produce false positives when two
        /// methods share a name across overloads, but the AmbiguousScope
        /// classification is itself the disclaimer: the report tells the
        /// reviewer to verify manually.
        /// </summary>
        public static bool HasCallerWithBeginCommit(ClassDeclarationSyntax cls, MethodDeclarationSyntax helper)
        {
            if (cls == null || helper == null) return false;
            string helperName = helper.Identifier.Text;
            if (string.IsNullOrEmpty(helperName)) return false;

            foreach (var caller in cls.Members.OfType<MethodDeclarationSyntax>())
            {
                if (caller == helper) continue;
                // Look for any invocation of helperName inside caller.
                bool callsHelper = caller.DescendantNodes()
                    .OfType<InvocationExpressionSyntax>()
                    .Any(i => GetInvokedSimpleName(i) == helperName);
                if (!callsHelper) continue;
                // Caller has a Begin AND a Commit/Rollback? Then the helper
                // is inside that scope (one-level deep).
                bool callerHasBegin = caller.DescendantNodes()
                    .OfType<InvocationExpressionSyntax>()
                    .Any(i => IsUndoServiceCall(i, "Begin"));
                bool callerHasCommitOrRollback =
                    HasUndoCommit(caller) || HasUndoRollback(caller);
                if (callerHasBegin && callerHasCommitOrRollback)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Extract the simple method name from an invocation, regardless
        /// of receiver. Used by the caller heuristic.
        /// </summary>
        static string GetInvokedSimpleName(InvocationExpressionSyntax inv)
        {
            return inv.Expression switch
            {
                MemberAccessExpressionSyntax m => m.Name.Identifier.Text,
                IdentifierNameSyntax id => id.Identifier.Text,
                _ => "",
            };
        }

        /// <summary>Walk up to find the enclosing class declaration.</summary>
        static ClassDeclarationSyntax? FindEnclosingClass(SyntaxNode node)
        {
            for (SyntaxNode? cur = node.Parent; cur != null; cur = cur.Parent)
            {
                if (cur is ClassDeclarationSyntax cls)
                    return cls;
            }
            return null;
        }

        /// <summary>
        /// Walk up to find the enclosing method declaration. Returns null
        /// when the node lives in a property setter / accessor /
        /// initialiser / lambda etc.
        /// </summary>
        static MethodDeclarationSyntax? FindEnclosingMethod(SyntaxNode node)
        {
            for (SyntaxNode? cur = node.Parent; cur != null; cur = cur.Parent)
            {
                if (cur is MethodDeclarationSyntax m)
                    return m;
                // If we hit a class first, no enclosing method exists.
                if (cur is ClassDeclarationSyntax)
                    return null;
            }
            return null;
        }

        /// <summary>
        /// Format the markdown body of the undo coverage report (sans
        /// front-matter — the caller adds it via <see cref="ReportWriter"/>).
        ///
        /// Sections (in priority order):
        /// <list type="number">
        ///   <item>Methodology header + WF ground-truth comparison</item>
        ///   <item>Summary table (counts per tier + percentages)</item>
        ///   <item>NoUndoServiceField — highest priority, grouped by class</item>
        ///   <item>MissingScope — VMs with UndoService but unwrapped writes</item>
        ///   <item>AmbiguousScope — caller-helper indirection, verify manually</item>
        ///   <item>Covered — collapsible counts only</item>
        ///   <item>Registry cross-check warnings (Writable VMs with no detected writes)</item>
        /// </list>
        ///
        /// Uses LF line endings throughout so committed reports don't churn
        /// on Windows checkouts.
        /// </summary>
        public static string FormatReport(IReadOnlyList<WriteCallsite> rows)
        {
            if (rows == null) throw new ArgumentNullException(nameof(rows));

            var sb = new StringBuilder();
            sb.Append("# Avalonia vs WinForms — Undo Coverage Sweep\n\n");
            sb.Append("This report inventories every ROM-write callsite in\n");
            sb.Append("`FEBuilderGBA.Avalonia/` and classifies its undo coverage. WinForms is\n");
            sb.Append("the ground truth — every WF call to `Program.ROM.SetU8/16/32(addr, val,\n");
            sb.Append("undo)` takes an `Undo` argument so the compiler enforces undo plumbing\n");
            sb.Append("at every callsite. Avalonia uses a different pattern\n");
            sb.Append("(`UndoService.Begin(name)` opens a scope `rom.write_u*` calls register\n");
            sb.Append("against automatically), but the migration applied this only inside\n");
            sb.Append("`EventScriptPopupViewModel`.\n\n");
            sb.Append("**Methodology:**\n\n");
            sb.Append("- Roslyn scans every `.cs` file under `FEBuilderGBA.Avalonia/`,\n");
            sb.Append("  excluding `GapSweep/`, `obj/`, and `bin/`.\n");
            sb.Append("- Each `InvocationExpressionSyntax` whose method name is in\n");
            sb.Append("  {`write_u8`, `write_u16`, `write_u32`, `write_p32`, `SetU8`, `SetU16`,\n");
            sb.Append("  `SetU32`, `SetData`} AND whose receiver resolves to a ROM reference\n");
            sb.Append("  (`rom`, `ROM`, `Program.ROM`, or `CoreState.ROM`) is captured as a\n");
            sb.Append("  write callsite.\n");
            sb.Append("- `EditorFormRef.WriteFields(rom, addr, values, fields)` and the\n");
            sb.Append("  singular `EditorFormRef.WriteField(...)` are also captured — the\n");
            sb.Append("  bulk-write helper through which most AV ViewModels funnel their\n");
            sb.Append("  writes. One report row per WriteFields call regardless of how\n");
            sb.Append("  many actual bytes the helper writes internally.\n");
            sb.Append("- For each callsite we find the enclosing class and method, then\n");
            sb.Append("  classify by walking the method body for `Begin`/`Commit`/`Rollback`\n");
            sb.Append("  invocations on any UndoService identifier\n");
            sb.Append("  (`_undoService`, `undoService`, `UndoService`).\n");
            sb.Append("- UndoService discovery: simple name match on field/property/local of\n");
            sb.Append("  type `UndoService`. No semantic model — see the AmbiguousScope tier\n");
            sb.Append("  for the one-level helper-call disclaimer.\n\n");
            sb.Append("**Coverage tiers** (highest priority first):\n\n");
            sb.Append("- `NoUndoServiceField` — class has no UndoService field at all. The\n");
            sb.Append("  whole VM is unplumbed; the fix requires introducing a service field\n");
            sb.Append("  before any individual write can be wrapped.\n");
            sb.Append("- `MissingScope` — class has UndoService but THIS write is not inside a\n");
            sb.Append("  `Begin(...)` scope.\n");
            sb.Append("- `AmbiguousScope` — write lives in a helper method; the caller MAY\n");
            sb.Append("  wrap a scope (one-level heuristic only — verify manually).\n");
            sb.Append("- `Covered` — write is inside a `Begin`/`Commit` (or `Begin`/`Rollback`)\n");
            sb.Append("  scope in the same method, OR the write passes an explicit `Undo`\n");
            sb.Append("  trailing argument (WinForms-style).\n\n");
            sb.Append("Methodology lives in `FEBuilderGBA.Avalonia/GapSweep/UndoCoverageScanner.cs`.\n");
            sb.Append("Regenerate with `FEBuilderGBA.Avalonia --gap-sweep-undo --out=<path>`.\n\n");

            // ---- Summary ----
            int total = rows.Count;
            int noField = rows.Count(r => r.Coverage == UndoCoverage.NoUndoServiceField);
            int missing = rows.Count(r => r.Coverage == UndoCoverage.MissingScope);
            int ambiguous = rows.Count(r => r.Coverage == UndoCoverage.AmbiguousScope);
            int covered = rows.Count(r => r.Coverage == UndoCoverage.Covered);

            sb.Append("## Summary\n\n");
            sb.Append("| Tier | Count | % of total |\n");
            sb.Append("|---|---:|---:|\n");
            sb.Append("| Total write callsites | ").Append(total).Append(" | 100% |\n");
            AppendTierRow(sb, "NoUndoServiceField (no plumbing)", noField, total);
            AppendTierRow(sb, "MissingScope (unwrapped)", missing, total);
            AppendTierRow(sb, "AmbiguousScope (verify)", ambiguous, total);
            AppendTierRow(sb, "Covered (healthy)", covered, total);
            sb.Append('\n');

            // ---- NoUndoServiceField (grouped by class, highest priority) ----
            AppendCoverageSection(
                sb,
                rows.Where(r => r.Coverage == UndoCoverage.NoUndoServiceField),
                heading: "## Highest priority — VMs with NO undo plumbing at all",
                preamble: "These ViewModels have no `UndoService` field/property/local. Every write here bypasses the undo buffer. The fix sequence is: (1) add a `UndoService _undoService = new();` field, (2) wrap each Save / Write handler in `_undoService.Begin/Commit`. Grouped by enclosing class.",
                emptyMessage: "_None._",
                groupByClass: true);

            // ---- MissingScope (grouped by class) ----
            AppendCoverageSection(
                sb,
                rows.Where(r => r.Coverage == UndoCoverage.MissingScope),
                heading: "## Missing scope — VMs that have UndoService but skip Begin/Commit on this write",
                preamble: "These classes already carry UndoService plumbing but a particular write was added without wrapping it. The fix is local: add `_undoService.Begin(\"...\")` before the write and `_undoService.Commit()` after.",
                emptyMessage: "_None._",
                groupByClass: true);

            // ---- AmbiguousScope ----
            AppendCoverageSection(
                sb,
                rows.Where(r => r.Coverage == UndoCoverage.AmbiguousScope),
                heading: "## Ambiguous — covered by caller, please verify",
                preamble: "The write lives in a helper method; a caller in the same class wraps Begin/Commit. The scanner uses a one-level name-match heuristic, so manual review is required to confirm the helper is actually called from within an active scope at runtime.",
                emptyMessage: "_None._",
                groupByClass: false);

            // ---- Covered (collapsible — count only by default) ----
            sb.Append("## Covered (healthy)\n\n");
            if (covered == 0)
            {
                sb.Append("_None._\n\n");
            }
            else
            {
                sb.Append("`").Append(covered).Append("` callsites are inside a Begin/Commit (or Begin/Rollback) scope in the same method body, OR pass an explicit Undo argument. ");
                var coveredByClass = rows
                    .Where(r => r.Coverage == UndoCoverage.Covered)
                    .GroupBy(r => r.EnclosingClass, StringComparer.Ordinal)
                    .OrderByDescending(g => g.Count())
                    .ThenBy(g => g.Key, StringComparer.Ordinal)
                    .ToList();
                sb.Append("Covered classes: ");
                bool first = true;
                foreach (var g in coveredByClass)
                {
                    if (!first) sb.Append(", ");
                    sb.Append("`").Append(g.Key).Append("` (").Append(g.Count()).Append(")");
                    first = false;
                }
                sb.Append(".\n\n");
            }

            // ---- WritableViewModelRegistry cross-check ----
            // The registry (in the Tests project) lists every VM that
            // matches the writable-triplet convention. If a VM is in the
            // registry but the scanner found ZERO writes for it, something
            // is likely wrong with our scanner's pattern set — surface as a
            // warning row so the next PR can investigate.
            sb.Append(BuildRegistryCrossCheckSection(rows));

            // Trim trailing blank lines.
            while (sb.Length >= 2 && sb[sb.Length - 1] == '\n' && sb[sb.Length - 2] == '\n')
                sb.Length--;
            return sb.ToString();
        }

        static void AppendTierRow(StringBuilder sb, string label, int count, int total)
        {
            sb.Append("| ").Append(label).Append(" | ").Append(count).Append(" | ");
            if (total == 0)
                sb.Append("0%");
            else
                sb.Append(((count * 100.0) / total).ToString("F1", System.Globalization.CultureInfo.InvariantCulture)).Append('%');
            sb.Append(" |\n");
        }

        /// <summary>
        /// Render one coverage section. When <paramref name="groupByClass"/>
        /// is true, rows are grouped under per-class subheadings sorted by
        /// row count descending (biggest-gap-first ordering) so reviewers
        /// see the highest-impact VMs at the top.
        /// </summary>
        static void AppendCoverageSection(
            StringBuilder sb,
            IEnumerable<WriteCallsite> rowsEnum,
            string heading,
            string preamble,
            string emptyMessage,
            bool groupByClass)
        {
            sb.Append(heading).Append("\n\n");
            sb.Append(preamble).Append("\n\n");

            var rows = rowsEnum.ToList();
            if (rows.Count == 0)
            {
                sb.Append(emptyMessage).Append("\n\n");
                return;
            }

            if (groupByClass)
            {
                var groups = rows
                    .GroupBy(r => r.EnclosingClass, StringComparer.Ordinal)
                    .OrderByDescending(g => g.Count())
                    .ThenBy(g => g.Key, StringComparer.Ordinal)
                    .ToList();
                foreach (var g in groups)
                {
                    sb.Append("### `").Append(EscapeMd(g.Key)).Append("` — ")
                      .Append(g.Count()).Append(" callsite").Append(g.Count() == 1 ? "" : "s").Append("\n\n");
                    AppendCallsiteTable(sb, g);
                }
            }
            else
            {
                AppendCallsiteTable(sb, rows);
            }
        }

        static void AppendCallsiteTable(StringBuilder sb, IEnumerable<WriteCallsite> rows)
        {
            sb.Append("| File | Line | Method | Write | Note |\n");
            sb.Append("|---|---:|---|---|---|\n");
            foreach (var r in rows.OrderBy(r => r.FilePath, StringComparer.Ordinal).ThenBy(r => r.Line))
            {
                sb.Append("| `").Append(EscapeMd(r.FilePath)).Append("` ")
                  .Append("| ").Append(r.Line).Append(" ")
                  .Append("| `").Append(EscapeMd(r.EnclosingMethod)).Append("` ")
                  .Append("| `").Append(EscapeMd(r.WriteExpression)).Append("` ")
                  .Append("| ").Append(EscapeMd(r.CoverageNote ?? "")).Append(" |\n");
            }
            sb.Append('\n');
        }

        /// <summary>
        /// Cross-check the scanner output against a reflection-based discovery
        /// of every concrete ViewModelBase subclass in the loaded Avalonia
        /// assembly that exposes both a <c>Load*List()</c> and a <c>Write*()</c>
        /// method. This mirrors <c>WritableViewModelRegistry</c>'s discovery
        /// rule from the Tests project (we re-derive it here so the scanner
        /// can self-audit without taking a Tests-&gt;Avalonia dependency).
        ///
        /// For every discovered writable VM whose name does NOT appear in the
        /// scanner's row set, emit a warning row in the report. A non-zero
        /// "writable but no detected write" count almost always means the
        /// scanner's pattern set has missed a real write API — Copilot PR #380
        /// review concern #1 (the <c>CoreState.ROM.write_u*</c> miss) is the
        /// canonical example of why this audit matters.
        ///
        /// Reflection uses <see cref="DiscoverWritableViewModelNames"/> and
        /// degrades gracefully when the Avalonia assembly cannot be loaded
        /// (test isolation, headless build) — in that case the section just
        /// emits the "classes with writes" count and skips the warning table.
        /// </summary>
        static string BuildRegistryCrossCheckSection(IReadOnlyList<WriteCallsite> rows)
        {
            var sb = new StringBuilder();
            sb.Append("## Registry cross-check\n\n");
            sb.Append("This section mirrors the writable-triplet convention used by\n");
            sb.Append("`WritableViewModelRegistry` (in `FEBuilderGBA.Avalonia.Tests`): every\n");
            sb.Append("concrete `ViewModelBase` subclass that exposes both a `Load*List()` and a\n");
            sb.Append("`Write*()` method is considered a writable VM. The Phase 5 scanner\n");
            sb.Append("reflects over the loaded Avalonia assembly to derive that list and\n");
            sb.Append("flags any VM that the static scan did NOT detect ANY ROM write for —\n");
            sb.Append("such a row almost always indicates the scanner's pattern set has missed a\n");
            sb.Append("real write API (e.g. PR #380 review caught a `CoreState.ROM.write_u*`\n");
            sb.Append("miss that surfaced as an unjustified zero-row warning before the fix).\n\n");

            var classesWithWrites = new HashSet<string>(
                rows.Select(r => r.EnclosingClass)
                    .Where(c => !string.IsNullOrEmpty(c)),
                StringComparer.Ordinal);

            sb.Append("Classes with at least one detected write: ").Append(classesWithWrites.Count).Append(".\n\n");

            // Reflection-based VM discovery. Wrapped in try/catch because
            // the assembly may not be loadable in headless / test-isolation
            // contexts (the unit tests construct FormatReport with synthetic
            // rows that don't require the runtime to be present).
            IReadOnlyList<string> writableVms;
            try
            {
                writableVms = DiscoverWritableViewModelNames();
            }
            catch
            {
                sb.Append("_Reflection-based VM discovery skipped: Avalonia assembly not loadable in this context._\n\n");
                return sb.ToString();
            }

            // VMs in the registry but with zero detected writes.
            var missing = writableVms
                .Where(name => !classesWithWrites.Contains(name))
                .OrderBy(n => n, StringComparer.Ordinal)
                .ToList();

            sb.Append("Writable VMs (matching the triplet convention): ").Append(writableVms.Count).Append(".  \n");
            sb.Append("Writable VMs with zero detected ROM writes: ").Append(missing.Count).Append(".\n\n");

            if (missing.Count == 0)
            {
                sb.Append("_No writable VM is missing from the scanner output — pattern coverage is healthy._\n\n");
                return sb.ToString();
            }

            sb.Append("### Writable VMs with zero detected ROM writes (warning)\n\n");
            sb.Append("Each row below names a ViewModel that the writable-triplet reflection\n");
            sb.Append("discovers but the scanner did NOT capture any ROM-write callsite for.\n");
            sb.Append("If this list is non-empty after Phase 5 ships, investigate the missing\n");
            sb.Append("pattern (the most likely cause is a write API the scanner doesn't yet\n");
            sb.Append("recognise — see `WriteMethodNames` + `IsRomReceiver` in\n");
            sb.Append("`UndoCoverageScanner.cs`).\n\n");
            sb.Append("| ViewModel | Action |\n");
            sb.Append("|---|---|\n");
            foreach (string name in missing)
            {
                sb.Append("| `").Append(EscapeMd(name)).Append("` | Verify ROM-write API; extend scanner pattern set if needed |\n");
            }
            sb.Append('\n');
            return sb.ToString();
        }

        /// <summary>
        /// Mirror of <c>WritableViewModelRegistry.WritableViewModels</c>'s
        /// discovery rule, but run from inside the Avalonia assembly so the
        /// scanner can self-audit without a Tests-&gt;Avalonia dependency.
        /// A VM qualifies when:
        /// <list type="bullet">
        ///   <item>it's a concrete (non-abstract, non-interface) subclass of
        ///     <c>ViewModelBase</c>;</item>
        ///   <item>it exposes a public parameterless method whose name starts
        ///     with <c>Load</c> and returns <c>List&lt;AddrResult&gt;</c>; and</item>
        ///   <item>it exposes a public parameterless <c>void</c> method whose
        ///     name starts with <c>Write</c>.</item>
        /// </list>
        /// </summary>
        public static IReadOnlyList<string> DiscoverWritableViewModelNames()
        {
            var assembly = typeof(UndoCoverageScanner).Assembly;
            var baseType = assembly.GetType("FEBuilderGBA.Avalonia.ViewModels.ViewModelBase");
            if (baseType == null)
                return Array.Empty<string>();

            // Resolve the AddrResult type by name. AddrResult lives in the
            // top-level FEBuilderGBA namespace (in Core), so we have to look
            // across loaded assemblies. The Avalonia assembly references
            // Core so the AddrResult type WILL be loaded once anything in
            // Core is referenced — the scanner itself triggers that load.
            Type? addrResultType = null;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                var t = asm.GetType("FEBuilderGBA.AddrResult");
                if (t != null) { addrResultType = t; break; }
            }
            if (addrResultType == null)
                return Array.Empty<string>();
            var listOfAddrResult = typeof(List<>).MakeGenericType(addrResultType);

            var names = new List<string>();
            Type[] allTypes;
            try
            {
                allTypes = assembly.GetTypes();
            }
            catch (System.Reflection.ReflectionTypeLoadException ex)
            {
                allTypes = ex.Types.Where(t => t != null).Cast<Type>().ToArray();
            }

            foreach (var t in allTypes)
            {
                if (t.IsAbstract || t.IsInterface) continue;
                if (!baseType.IsAssignableFrom(t)) continue;

                bool hasWrite = t.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
                    .Any(m => m.Name.StartsWith("Write", StringComparison.Ordinal)
                              && m.ReturnType == typeof(void)
                              && m.GetParameters().Length == 0);
                if (!hasWrite) continue;

                bool hasList = t.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
                    .Any(m => m.Name.StartsWith("Load", StringComparison.Ordinal)
                              && m.ReturnType == listOfAddrResult
                              && m.GetParameters().Length == 0);
                if (!hasList) continue;

                names.Add(t.Name);
            }
            names.Sort(StringComparer.Ordinal);
            return names;
        }

        /// <summary>
        /// Escape a string for safe inclusion inside a markdown inline-code
        /// span. Empty strings render as a literal em-dash so the table
        /// cell isn't visually empty. Pipe characters inside the value are
        /// escaped because they would otherwise terminate the table cell.
        /// </summary>
        static string EscapeMd(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "—";
            // Inside a single-backtick span we just need to avoid embedded
            // backticks and pipes (which would break the table layout).
            return value.Replace("`", "'").Replace("|", "\\|");
        }
    }
}
