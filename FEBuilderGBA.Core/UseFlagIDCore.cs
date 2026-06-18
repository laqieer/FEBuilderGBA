using System;
using System.Collections.Generic;

namespace FEBuilderGBA
{
    /// <summary>
    /// Cross-platform, strictly READ-ONLY record describing one event-flag
    /// usage found in a chapter (Avalonia "Flags-Used-in-Chapter" tool, issue
    /// #1192 — port of the WinForms <c>UseFlagID</c> class consumed by
    /// <c>ToolUseFlagForm</c>).
    ///
    /// This is a PARALLEL Core type, NOT a move of the WinForms
    /// <c>FEBuilderGBA.UseFlagID</c> — the WinForms class stays in place (it is
    /// referenced by EventCondForm / MapChangeForm / EventHaiku* / EventBattleTalk*
    /// and its <c>AppendFlagID</c> overloads take a WinForms <c>InputFormRef</c>).
    /// This Core copy carries only the InputFormRef-free essence so the headless
    /// scanner (<see cref="UseFlagScanCore"/>) and the Avalonia tool can build the
    /// same per-chapter flag-usage list without any WinForms dependency.
    /// </summary>
    public sealed class UseFlagIDCore
    {
        /// <summary>The lint category that classifies WHERE the flag was found
        /// (event condition / event script / map change).</summary>
        public FELintCore.Type DataType { get; }

        /// <summary>The event-flag id that is referenced.</summary>
        public uint ID { get; }

        /// <summary>A short human-readable label for the usage site (slot name
        /// or "").</summary>
        public string Info { get; }

        /// <summary>The ROM byte offset of the referencing record / command.</summary>
        public uint Addr { get; }

        /// <summary>The chapter (map) id the usage belongs to, or
        /// <see cref="U.NOT_FOUND"/> when it is chapter-independent.</summary>
        public uint MapID { get; }

        /// <summary>Opaque navigation tag (slot index / event-script command
        /// address), mirroring the WinForms record's <c>Tag</c>.</summary>
        public uint Tag { get; }

        public UseFlagIDCore(FELintCore.Type dataType, uint addr, string info, uint id, uint mapid, uint tag = U.NOT_FOUND)
        {
            this.DataType = dataType;
            this.Addr = addr;
            this.Info = info ?? "";
            this.ID = id;
            this.MapID = mapid;
            this.Tag = tag;
        }

        /// <summary>
        /// Append a usage record unless the flag id is 0 (the "no flag" sentinel,
        /// matching the WinForms <c>UseFlagID.AppendUseFlagID</c> guard). Strictly
        /// additive — never mutates the ROM.
        /// </summary>
        public static void AppendUseFlagID(List<UseFlagIDCore> list, FELintCore.Type dataType, uint addr, string info, uint id, uint mapid, uint tag = U.NOT_FOUND)
        {
            if (list == null) return;
            if (id == 0) return;
            list.Add(new UseFlagIDCore(dataType, addr, info, id, mapid, tag));
        }
    }
}
