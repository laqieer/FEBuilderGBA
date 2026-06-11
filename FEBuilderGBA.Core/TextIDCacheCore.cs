// SPDX-License-Identifier: GPL-3.0-or-later
// Text-ID reference cache — Core extraction (#1028 Slice A).
//
// The WinForms `EtcCacheTextID` class (FEBuilderGBA/EtcCacheText.cs) loads two
// dictionaries — the user-authored reference comments (`config/etc/<rom>/textid_.txt`)
// and the shipped system text-id names (`config/data/textid_*.txt`) — and lets the
// Text Editor's References tab add / edit / remove a per-text-id reference comment,
// persisting it back to the user TSV.
//
// To wire the Avalonia Text Editor's "Add Reference" button without dragging in the
// WinForms-only `UseValsID` / `FELint` types (used by `AppendList` / `MakeUseTextID`),
// the cache's three persistence-relevant operations (Update / Save / GetName) are
// captured behind the `ITextIDCache` interface and a pure-Core implementation here.
// `EtcCacheTextID` implements the same interface (no behavior change), so WinForms
// keeps its richer merge/lint helpers while sharing the typed `CoreState` slot.
using System.Collections.Generic;

namespace FEBuilderGBA
{
    /// <summary>
    /// Persistence seam for the per-text-id reference-comment cache. Implemented by
    /// both the cross-platform <see cref="TextIDCacheCore"/> and the WinForms
    /// <c>EtcCacheTextID</c> so Core / Avalonia code can add, persist and read back
    /// text-id reference comments without depending on WinForms-only types.
    /// </summary>
    public interface ITextIDCache
    {
        /// <summary>
        /// Set (non-empty <paramref name="comment"/>) or remove (empty
        /// <paramref name="comment"/>) the user reference comment for
        /// <paramref name="textid"/>. Mirrors WinForms <c>EtcCacheTextID.Update</c>.
        /// </summary>
        void Update(uint textid, string comment);

        /// <summary>
        /// Persist the user reference comments to
        /// <c>config/etc/&lt;romBaseFilename&gt;/textid_.txt</c>. No-op when the cache is
        /// empty (matches WinForms — an emptied cache is NOT written/deleted here).
        /// </summary>
        void Save(string romBaseFilename);

        /// <summary>
        /// Return the reference comment for <paramref name="textid"/> — the user
        /// comment if present, otherwise the shipped system name, otherwise "".
        /// </summary>
        string GetName(uint textid);
    }

    /// <summary>
    /// Cross-platform implementation of <see cref="ITextIDCache"/>. Ports the
    /// dictionary-loading constructor + <c>Update</c> + <c>Save</c> verbatim from the
    /// WinForms <c>EtcCacheTextID</c>. <see cref="GetName"/> uses a direct dictionary
    /// lookup (no WinForms <c>UseValsID</c> / FE8 system-text-id special-casing).
    /// </summary>
    public sealed class TextIDCacheCore : ITextIDCache
    {
        readonly Dictionary<uint, string> EtcTextID;
        readonly Dictionary<uint, string> TextID;

        public TextIDCacheCore()
        {
            this.EtcTextID = U.LoadTSVResource1(U.ConfigEtcFilename("textid_"), false);
            this.TextID = U.LoadDicResource(U.ConfigDataFilename("textid_"));
        }

        public void Update(uint textid, string comment)
        {
            if (comment == "")
            {
                if (this.EtcTextID.ContainsKey(textid))
                {
                    this.EtcTextID.Remove(textid);
                }
            }
            else
            {
                this.EtcTextID[textid] = comment;
            }
        }

        public void Save(string romBaseFilename)
        {
            if (this.EtcTextID.Count >= 1)
            {
                U.SaveConfigEtcTSV1("textid_", this.EtcTextID, romBaseFilename);
            }
        }

        public string GetName(uint textid)
        {
            string c;
            if (this.EtcTextID.TryGetValue(textid, out c))
            {
                return c;
            }
            if (this.TextID.TryGetValue(textid, out c))
            {
                return c;
            }
            return "";
        }
    }
}
