// SPDX-License-Identifier: GPL-3.0-or-later
// Decomp shop-list GUI source-routing helper (#1347 Slice 5a).
//
// One ROM-bound, never-throwing entry point that decomp-mode GUI shop saves call to
// route an edit to the OWNING decomp source list instead of the preview ROM. It
// resolves the shop's ROM address to a manifest u16-list owner and delegates the pure
// rewrite to DecompSourceWriterCore.WriteListEntries. It NEVER mutates the ROM: it
// either writes the owning source list (Routed) or reports NotRouted/Error so the
// caller keeps the #1159 ROM-only guard (no clobber).
using System;
using System.Collections.Generic;

namespace FEBuilderGBA
{
    /// <summary>
    /// Outcome of a decomp-mode shop-save source-routing attempt (#1347 Slice 5a).
    /// </summary>
    public enum DecompShopRouteOutcome
    {
        /// <summary>The edit was written to the owning decomp source list (ROM untouched).</summary>
        Routed,
        /// <summary>
        /// The edit was NOT routed to source (not decomp mode, no resolvable list-owner,
        /// macro/non-literal source list, romOnly/manual policy, etc.). The caller keeps
        /// the #1159 ROM-only guard — NO ROM write, NO clobber.
        /// </summary>
        NotRouted,
        /// <summary>An unexpected fault occurred while routing (the source file is left untouched).</summary>
        Error,
    }

    /// <summary>
    /// Typed result of <see cref="DecompShopSourceWriteCore.TryRouteShopSaveToSource"/>.
    /// <see cref="Routed"/> only when <see cref="Outcome"/> is
    /// <see cref="DecompShopRouteOutcome.Routed"/>.
    /// </summary>
    public sealed class DecompShopRouteResult
    {
        /// <summary>Outcome of the routing attempt.</summary>
        public DecompShopRouteOutcome Outcome;
        /// <summary>Absolute path of the source file that was rewritten (when routed).</summary>
        public string SourceFile = "";
        /// <summary>Human-readable message (success summary or the reason routing was declined).</summary>
        public string Message = "";
        /// <summary>True only when the edit was written to source.</summary>
        public bool Routed => Outcome == DecompShopRouteOutcome.Routed;
    }

    /// <summary>
    /// GUI source-routing glue (#1347 Slice 5a) for decomp-mode Item Shop saves.
    ///
    /// In decomp open mode, a shop save would otherwise be ROM-only (#1159 guard). When
    /// the shop's ROM address resolves to a manifest <c>u16-list</c> owner (symbol-resolved
    /// via the merged ASM/MAP table) AND that owner's source list is literal-only, this
    /// helper rewrites the OWNING source list instead and reports <see cref="DecompShopRouteOutcome.Routed"/>;
    /// the caller then keeps the ROM untouched. Otherwise it reports
    /// <see cref="DecompShopRouteOutcome.NotRouted"/> (or <see cref="DecompShopRouteOutcome.Error"/>)
    /// and the caller keeps the #1159 ROM-only guard.
    ///
    /// This class NEVER mutates the ROM and NEVER throws: every fault is wrapped into an
    /// <see cref="DecompShopRouteOutcome.Error"/> result. It is ROM-BOUND — the passed ROM
    /// must be the active <see cref="CoreState.ROM"/> instance (the source-routing decision
    /// is anchored to the live preview ROM the GUI is editing).
    /// </summary>
    public static class DecompShopSourceWriteCore
    {
        /// <summary>
        /// Attempt to route a decomp-mode shop save to the owning decomp source list.
        ///
        /// NEVER mutates the ROM and NEVER throws. Gates (in order): the ROM must be the
        /// active <see cref="CoreState.ROM"/>; decomp mode must be active with
        /// <paramref name="project"/> as the active project; an ASM/MAP symbol file must be
        /// available; the shop's address must resolve to a manifest u16-list owner. On all
        /// of those the pure rewrite is delegated to
        /// <see cref="DecompSourceWriterCore.WriteListEntries"/>: <c>Ok</c> → Routed; a
        /// declined/refused writer status (NotOwned/UnsupportedField/Manual/RomOnly/Rejected/
        /// MalformedManifest/SourceNotFound/NotDecompMode) → NotRouted (carrying the writer's
        /// message); a writer fault (Error/ParseFailed, and any unmapped status) → Error. In
        /// EVERY non-Routed case the caller keeps the #1159 ROM-only guard (no clobber).
        /// </summary>
        /// <param name="rom">The active preview ROM (must be <see cref="CoreState.ROM"/>).</param>
        /// <param name="project">The active decomp project (must be <see cref="CoreState.DecompProject"/>).</param>
        /// <param name="asmMap">The merged ASM/MAP symbol file (decomp-layered); typically <c>CoreState.AsmMapFileAsmCache?.GetAsmMapFile()</c>.</param>
        /// <param name="shopAddr">The shop item-list ROM OFFSET (as produced by <c>ItemShopCore.MakeShopList</c>).</param>
        /// <param name="desiredItems">The desired shop item vector (packed <c>(qty&lt;&lt;8)|id</c> u16 entries).</param>
        /// <returns>A typed routing result; never null.</returns>
        public static DecompShopRouteResult TryRouteShopSaveToSource(
            ROM rom,
            DecompProject project,
            IAsmMapFile asmMap,
            uint shopAddr,
            IReadOnlyList<ushort> desiredItems)
        {
            var result = new DecompShopRouteResult();
            try
            {
                // Gate: ROM-bound. The signature implies a ROM-bound call; enforce it.
                if (rom == null || !ReferenceEquals(CoreState.ROM, rom))
                {
                    result.Outcome = DecompShopRouteOutcome.NotRouted;
                    result.Message = "ROM is not the active preview ROM — source routing skipped.";
                    return result;
                }

                // Gate: decomp mode with this exact project active. Distinguish the two
                // distinct causes in the user-visible message (not-in-decomp-mode vs the
                // passed project not being the active one) so the NotRouted reason is honest.
                if (project == null || !CoreState.IsDecompMode)
                {
                    result.Outcome = DecompShopRouteOutcome.NotRouted;
                    result.Message = "Not in decomp mode (no active decomp project) — source routing skipped.";
                    return result;
                }
                if (!ReferenceEquals(CoreState.DecompProject, project))
                {
                    result.Outcome = DecompShopRouteOutcome.NotRouted;
                    result.Message = "Passed project is not the active decomp project — source routing skipped.";
                    return result;
                }

                // Gate: a symbol table is required to resolve the shop's owning list.
                if (asmMap == null)
                {
                    result.Outcome = DecompShopRouteOutcome.NotRouted;
                    result.Message = "No ASM/MAP symbol file available — cannot resolve the shop's list owner.";
                    return result;
                }

                // Resolve the manifest list-owner of this shop address.
                if (!DecompShopSourceResolver.TryResolveShopOwner(
                        project, asmMap, shopAddr, out DecompTableEntry owner, out _))
                {
                    result.Outcome = DecompShopRouteOutcome.NotRouted;
                    result.Message = "no list-owner declared/resolved for this shop";
                    return result;
                }

                // Pure source rewrite (NEVER touches the ROM).
                DecompSourceWriteResult res = DecompSourceWriterCore.WriteListEntries(project, owner, desiredItems);

                switch (res.Status)
                {
                    case DecompSourceWriteStatus.Ok:
                        result.Outcome = DecompShopRouteOutcome.Routed;
                        result.SourceFile = res.SourceFile ?? "";
                        result.Message = res.Message ?? "";
                        return result;

                    // Honest declines — the caller keeps the #1159 ROM-only guard.
                    case DecompSourceWriteStatus.NotOwned:
                    case DecompSourceWriteStatus.UnsupportedField:
                    case DecompSourceWriteStatus.Manual:
                    case DecompSourceWriteStatus.RomOnly:
                    case DecompSourceWriteStatus.Rejected:
                    case DecompSourceWriteStatus.MalformedManifest:
                    case DecompSourceWriteStatus.SourceNotFound:
                    case DecompSourceWriteStatus.NotDecompMode:
                        result.Outcome = DecompShopRouteOutcome.NotRouted;
                        result.SourceFile = res.SourceFile ?? "";
                        result.Message = res.Message ?? "";
                        return result;

                    // Writer faults — surface as Error.
                    case DecompSourceWriteStatus.Error:
                    case DecompSourceWriteStatus.ParseFailed:
                    default:
                        result.Outcome = DecompShopRouteOutcome.Error;
                        result.SourceFile = res.SourceFile ?? "";
                        result.Message = res.Message ?? "";
                        return result;
                }
            }
            catch (Exception ex)
            {
                result.Outcome = DecompShopRouteOutcome.Error;
                // Surface the full type/stack — this Message is the only diagnostic detail
                // the caller sees on an Error outcome (it is a result string, not a Log call).
                result.Message = "Unexpected fault: " + ex.ToString();
                return result;
            }
        }
    }
}
