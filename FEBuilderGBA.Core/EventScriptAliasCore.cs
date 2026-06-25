using System;
using System.Collections.Generic;

namespace FEBuilderGBA
{
    /// <summary>
    /// Pure, READ-ONLY helpers for event-script command argument <b>aliases</b>.
    ///
    /// An event-command definition (an <see cref="EventScript.Script"/>) may declare the same
    /// symbol (e.g. <c>X</c>) at more than one byte position. The FIRST occurrence is the
    /// <b>primary</b> arg (its <see cref="EventScript.Arg.Alias"/> is <see cref="U.NOT_FOUND"/>);
    /// every later occurrence with the same <see cref="EventScript.Arg.Symbol"/> is an
    /// <b>alias</b> (its <c>Alias</c> holds the index of the primary).
    ///
    /// WinForms ground truth:
    /// <list type="bullet">
    ///   <item><see cref="EventScript.IsFixedArg"/> drops FIXED <i>and</i> alias args from the
    ///         editable parameter list — only primaries are shown/edited.</item>
    ///   <item><c>EventScriptForm.WriteAliasScriptEditSetTables</c> re-writes a primary arg's
    ///         value to <i>every</i> alias sharing the same <c>Symbol</c> after the primary write.</item>
    /// </list>
    ///
    /// This helper centralizes that logic so both the cross-platform popup editor and tests can
    /// reuse it. It never mutates a ROM and never throws on malformed input.
    /// </summary>
    public static class EventScriptAliasCore
    {
        /// <summary>
        /// The indices of <c>code.Script.Args</c> that should be shown as <b>editable</b> rows:
        /// every arg for which <see cref="EventScript.IsFixedArg"/> is <c>false</c> (i.e. neither
        /// FIXED nor an alias). Mirrors the WinForms editable-row gate exactly.
        /// </summary>
        public static IEnumerable<int> EnumerateEditableArgIndices(EventScript.OneCode code)
        {
            var args = code?.Script?.Args;
            if (args == null)
            {
                yield break;
            }
            for (int i = 0; i < args.Length; i++)
            {
                EventScript.Arg arg = args[i];
                if (arg == null)
                {
                    continue;
                }
                if (EventScript.IsFixedArg(arg))
                {
                    continue;
                }
                yield return i;
            }
        }

        /// <summary>Count of editable rows for the command (FIXED + alias args excluded).</summary>
        public static int CountEditableArgs(EventScript.OneCode code)
        {
            int n = 0;
            foreach (var _ in EnumerateEditableArgIndices(code))
            {
                n++;
            }
            return n;
        }

        /// <summary>
        /// The full set of <see cref="EventScript.Arg"/> <b>write targets</b> for an edit applied
        /// to the primary arg at <paramref name="primaryArgIndex"/>: the primary itself, followed
        /// by every alias arg (<c>Alias != NOT_FOUND</c>, non-FIXED) sharing the primary's
        /// <see cref="EventScript.Arg.Symbol"/>. The caller writes the SAME value to each yielded
        /// target exactly once. Port of WinForms <c>WriteOneScriptEditSetTables</c> (primary) +
        /// <c>WriteAliasScriptEditSetTables</c> (aliases).
        ///
        /// Returns an empty sequence if the index is out of range or the arg is FIXED/an alias
        /// (only a primary may drive propagation).
        /// </summary>
        public static IEnumerable<EventScript.Arg> EnumerateAliasWriteTargets(EventScript.OneCode code, int primaryArgIndex)
        {
            var args = code?.Script?.Args;
            if (args == null || primaryArgIndex < 0 || primaryArgIndex >= args.Length)
            {
                yield break;
            }

            EventScript.Arg primary = args[primaryArgIndex];
            if (primary == null || primary.Type == EventScript.ArgType.FIXED || primary.Alias != U.NOT_FOUND)
            {
                // Not a primary editable arg — nothing to drive.
                yield break;
            }

            // 1) The primary write target.
            yield return primary;

            // 2) Every alias that points back to this symbol (mirror WriteAliasScriptEditSetTables).
            for (int i = 0; i < args.Length; i++)
            {
                EventScript.Arg a = args[i];
                if (a == null)
                {
                    continue;
                }
                if (a.Type == EventScript.ArgType.FIXED)
                {
                    continue;
                }
                if (a.Alias == U.NOT_FOUND)
                {//aliasではない
                    continue;
                }
                if (a.Symbol != primary.Symbol)
                {
                    continue;
                }
                yield return a;
            }
        }
    }
}
