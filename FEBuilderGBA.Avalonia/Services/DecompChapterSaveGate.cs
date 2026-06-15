// SPDX-License-Identifier: GPL-3.0-or-later
// #1148 (Copilot PR #1158 review): shared all-or-nothing logic for the chapter-settings
// (map_settings) decomp source save-gate, reasoning over LOGICAL fields / alias groups so a
// normal manifest declaring only one alias of a logical scalar (e.g. "Weather" not "weather")
// is accepted, while a partial/silent write is impossible.
using System.Collections.Generic;

namespace FEBuilderGBA.Avalonia.Services
{
    /// <summary>Outcome of evaluating the chapter-settings source save-gate.</summary>
    public enum ChapterSaveGateResult
    {
        /// <summary>No source-writable scalar changed → genuine no-op (writer reports it cleanly).</summary>
        NoChange,
        /// <summary>All edited logical scalars are declared → call the writer with <c>Changed</c>.</summary>
        Write,
        /// <summary>An edited logical scalar has NO declared alias → unsupported/manual, skip the write.</summary>
        UndeclaredScalar,
    }

    /// <summary>
    /// Pure all-or-nothing evaluator for the chapter-settings source save-gate (#1148).
    /// Reasons over LOGICAL fields via alias groups: a logical scalar is "edited" when ANY of
    /// its alias keys appears in <paramref name="rawChanged"/>, and "source-writable" when AT
    /// LEAST ONE of its aliases is in <paramref name="declared"/>. If EVERY edited logical
    /// scalar is declared, returns <see cref="ChapterSaveGateResult.Write"/> with the filtered
    /// declared keys (so the writer never sees an undeclared key); if ANY edited logical scalar
    /// has no declared alias, returns <see cref="ChapterSaveGateResult.UndeclaredScalar"/> with
    /// no <c>changed</c> (the caller skips the write — no partial/silent write); if nothing was
    /// edited, returns <see cref="ChapterSaveGateResult.NoChange"/>. NEVER throws.
    /// (Pointer edits are handled by the caller BEFORE this — they are never source-writable.)
    /// </summary>
    public static class DecompChapterSaveGate
    {
        public static ChapterSaveGateResult Evaluate(
            string[][] aliasGroups,
            IReadOnlyDictionary<string, uint> rawChanged,
            HashSet<string> declared,
            out Dictionary<string, uint> changed)
        {
            changed = new Dictionary<string, uint>(System.StringComparer.Ordinal);
            try
            {
                if (rawChanged == null || rawChanged.Count == 0)
                    return ChapterSaveGateResult.NoChange;
                if (aliasGroups == null)
                    return ChapterSaveGateResult.NoChange;
                declared ??= new HashSet<string>(System.StringComparer.Ordinal);

                bool anyEdited = false;
                bool anyUndeclared = false;

                foreach (string[] group in aliasGroups)
                {
                    if (group == null) continue;

                    // Did the user edit this logical field? (any alias present in rawChanged)
                    bool edited = false;
                    uint value = 0;
                    foreach (string key in group)
                    {
                        if (key != null && rawChanged.TryGetValue(key, out uint v))
                        {
                            edited = true;
                            value = v;   // aliases share the same backing value
                            break;
                        }
                    }
                    if (!edited)
                        continue;
                    anyEdited = true;

                    // Pass AT MOST ONE declared alias per logical scalar to the writer. If a
                    // manifest redundantly declares multiple aliases of one field (e.g. both
                    // "Weather" and "weather"), asking the writer to rewrite both would make
                    // it look up two designators for one struct member — the second alias has
                    // no designator in the source and the write fails ("designator not found").
                    // One declared alias is enough to write the logical field (Copilot PR #1158).
                    bool covered = false;
                    foreach (string key in group)
                    {
                        if (key != null && declared.Contains(key))
                        {
                            changed[key] = value;   // first declared alias only
                            covered = true;
                            break;
                        }
                    }
                    if (!covered)
                        anyUndeclared = true;
                }

                if (anyUndeclared)
                {
                    changed.Clear();   // all-or-nothing: never a partial write
                    return ChapterSaveGateResult.UndeclaredScalar;
                }
                if (!anyEdited || changed.Count == 0)
                    return ChapterSaveGateResult.NoChange;
                return ChapterSaveGateResult.Write;
            }
            catch
            {
                // Never throw at the gate; on any fault refuse to write (fail safe).
                changed = new Dictionary<string, uint>(System.StringComparer.Ordinal);
                return ChapterSaveGateResult.UndeclaredScalar;
            }
        }
    }
}
