using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;

namespace FEBuilderGBA
{
    /// <summary>
    /// Outcome of a source-backed table-entry write (#1132).
    /// </summary>
    public enum DecompSourceWriteStatus
    {
        /// <summary>The source file was rewritten successfully.</summary>
        Ok,
        /// <summary>Not in decomp mode (no active project), so no source write happened.</summary>
        NotDecompMode,
        /// <summary>The table has no owner declaration in the manifest.</summary>
        NotOwned,
        /// <summary>A changed field could not be rewritten (macro/expression value, or unknown field).</summary>
        UnsupportedField,
        /// <summary>The owner's writePolicy is "romOnly".</summary>
        RomOnly,
        /// <summary>The owner's writePolicy is "manual" (or JSON-backed, deferred).</summary>
        Manual,
        /// <summary>The owner's sourceFile path was rejected (absolute / ..-escape / out of root).</summary>
        Rejected,
        /// <summary>The manifest tables section is malformed / unusable.</summary>
        MalformedManifest,
        /// <summary>The owner's sourceFile does not exist on disk.</summary>
        SourceNotFound,
        /// <summary>The C array / element could not be located or parsed.</summary>
        ParseFailed,
        /// <summary>An unexpected fault occurred (the source file is left untouched).</summary>
        Error,
    }

    /// <summary>
    /// Typed result of a source-backed write. <see cref="Ok"/> only when
    /// <see cref="Status"/> is <see cref="DecompSourceWriteStatus.Ok"/>.
    /// </summary>
    public sealed class DecompSourceWriteResult
    {
        /// <summary>Outcome status.</summary>
        public DecompSourceWriteStatus Status;
        /// <summary>Human-readable message (success summary or rejection reason).</summary>
        public string Message = "";
        /// <summary>Absolute path of the source file (when resolved).</summary>
        public string SourceFile = "";
        /// <summary>The entry id that was targeted.</summary>
        public int EntryId;
        /// <summary>C field names that were actually changed.</summary>
        public List<string> ChangedFields = new List<string>();
        /// <summary>
        /// C field names that were REQUESTED but SKIPPED because their source token is a
        /// macro/expression/non-integer (or, JSON, a non-number / missing field) and the
        /// write was a bulk (multi-field) set. A non-empty list on an Ok status means the
        /// write was PARTIAL — the caller (all-or-nothing save gates) must NOT treat it as
        /// fully saved. Distinct from a legitimate no-op (value already equal). (#1159)
        /// </summary>
        public List<string> SkippedFields = new List<string>();
        /// <summary>Text of the targeted element BEFORE the rewrite.</summary>
        public string BeforeText = "";
        /// <summary>Text of the targeted element AFTER the rewrite.</summary>
        public string AfterText = "";
        /// <summary>1-based line number where the changed element begins.</summary>
        public int ChangedLineStart;
        /// <summary>1-based line number where the changed element ends.</summary>
        public int ChangedLineEnd;
        /// <summary>True when the write succeeded.</summary>
        public bool Ok => Status == DecompSourceWriteStatus.Ok;
    }

    /// <summary>
    /// Source-backed writer for structured table editors (#1132).
    ///
    /// In decomp "open mode", a structured table (e.g. <c>items</c>) may be owned by
    /// a C source file (the source of truth). Instead of mutating the preview ROM,
    /// this writer rewrites the owning C array element IN-PLACE — changing only the
    /// integer-literal token(s) for the requested field(s) and leaving every other
    /// byte of the file identical (comments, whitespace, line endings preserved).
    ///
    /// The class is ROM-NEUTRAL (it never touches the ROM) and NEVER throws: every
    /// public entry point is fully guarded and returns a typed
    /// <see cref="DecompSourceWriteResult"/> on any fault. On a write fault the source
    /// file is left untouched (no half-write).
    ///
    /// Supported source formats: C struct array (<c>format=="cstruct"</c> or unset)
    /// AND JSON (<c>format=="json"</c>, #1141). Only plain integer-literal value tokens
    /// (C) / JSON Number tokens are rewritten; macros / identifiers / expressions
    /// (C) and string/bool/object/array values (JSON) are reported as
    /// <see cref="DecompSourceWriteStatus.UnsupportedField"/> with no write (single-field
    /// intent) or SKIPPED (bulk write). Signed fields (#1141) emit a <c>-N</c> decimal
    /// when the reinterpreted value is negative.
    /// </summary>
    public static class DecompSourceWriterCore
    {
        /// <summary>
        /// Per-field outcome of applying one change to an element (#1132).
        /// Distinguishes a real rewrite from a no-op / a skipped macro / a fault so the
        /// caller can avoid false "changed" signals and macro-blocking on bulk writes.
        /// </summary>
        enum FieldApplyOutcome
        {
            /// <summary>The integer-literal token was rewritten to a new value.</summary>
            Changed,
            /// <summary>The token is an integer literal already equal to the requested value.</summary>
            NoOp,
            /// <summary>The token is a macro/expression and the set is bulk (multi-field) — skipped.</summary>
            SkippedMacro,
            /// <summary>A hard failure (macro under single-field intent, or a real fault).</summary>
            Failed,
        }

        /// <summary>
        /// Rewrite one table entry's field(s) in the owning C source file.
        ///
        /// Gates (in order): decomp mode → owner declared → writePolicy is "source" →
        /// format is "cstruct" → sourceFile resolves under the project root and exists
        /// → all changed-field names are declared → the array element parses → every
        /// changed field's value token is a plain integer literal. On success, sets
        /// <c>project.NeedsRebuild = true</c> and writes the file atomically.
        /// </summary>
        public static DecompSourceWriteResult WriteTableEntry(
            DecompProject project,
            string tableName,
            int entryId,
            IReadOnlyDictionary<string, uint> changedFields)
        {
            var result = new DecompSourceWriteResult { EntryId = entryId };
            try
            {
                // Gate 1: decomp mode. The active project MUST be this project.
                if (project == null || !CoreState.IsDecompMode
                    || !ReferenceEquals(CoreState.DecompProject, project))
                {
                    result.Status = DecompSourceWriteStatus.NotDecompMode;
                    result.Message = "Not in decomp mode (no active project) — source write skipped.";
                    return result;
                }

                // Gate 2: owner.
                DecompTableEntry owner = project.TryGetTableOwner(tableName);
                if (owner == null)
                {
                    result.Status = DecompSourceWriteStatus.NotOwned;
                    result.Message = $"Table '{tableName}' has no source owner in the manifest — ROM-only.";
                    return result;
                }

                // Gate 3: write policy.
                string policy = (owner.WritePolicy ?? "").Trim();
                if (string.Equals(policy, "romOnly", StringComparison.OrdinalIgnoreCase))
                {
                    result.Status = DecompSourceWriteStatus.RomOnly;
                    result.Message = $"Table '{tableName}' is romOnly — edit the ROM directly or change writePolicy.";
                    return result;
                }
                if (string.Equals(policy, "manual", StringComparison.OrdinalIgnoreCase))
                {
                    result.Status = DecompSourceWriteStatus.Manual;
                    result.Message = $"Table '{tableName}' is manual — edit the source by hand and rebuild.";
                    return result;
                }
                // Only "source" (or unset, defaulting to source for cstruct) proceeds.
                if (!string.IsNullOrEmpty(policy)
                    && !string.Equals(policy, "source", StringComparison.OrdinalIgnoreCase))
                {
                    result.Status = DecompSourceWriteStatus.Manual;
                    result.Message = $"Unknown writePolicy '{owner.WritePolicy}' — treated as manual.";
                    return result;
                }

                // Gate 4: format. "cstruct" (or unset) AND "json" are implemented (#1141).
                string format = (owner.Format ?? "").Trim();
                bool isJson = string.Equals(format, "json", StringComparison.OrdinalIgnoreCase);
                if (!isJson
                    && !string.IsNullOrEmpty(format)
                    && !string.Equals(format, "cstruct", StringComparison.OrdinalIgnoreCase))
                {
                    result.Status = DecompSourceWriteStatus.Manual;
                    result.Message = $"Unsupported source format '{owner.Format}' — only cstruct and json are implemented.";
                    return result;
                }

                // Gate 5: resolve sourceFile under the project root.
                if (string.IsNullOrEmpty(owner.SourceFile))
                {
                    result.Status = DecompSourceWriteStatus.MalformedManifest;
                    result.Message = $"Table '{tableName}' owner declares no sourceFile.";
                    return result;
                }
                string absPath = DecompProjectDetector.ResolveArtifact(project.ProjectRoot, owner.SourceFile);
                if (absPath == null)
                {
                    result.Status = DecompSourceWriteStatus.Rejected;
                    result.Message = $"sourceFile '{owner.SourceFile}' is rejected (absolute / escapes project root).";
                    return result;
                }
                result.SourceFile = absPath;

                // Gate 6: file exists.
                if (!File.Exists(absPath))
                {
                    result.Status = DecompSourceWriteStatus.SourceNotFound;
                    result.Message = $"sourceFile not found: {absPath}";
                    return result;
                }

                // Read raw bytes, decode (UTF-8, no BOM strip needed for content match).
                byte[] rawBytes;
                try { rawBytes = File.ReadAllBytes(absPath); }
                catch (Exception ex)
                {
                    result.Status = DecompSourceWriteStatus.Error;
                    result.Message = $"Could not read sourceFile: {ex.Message}";
                    return result;
                }

                // Detect a UTF-8 BOM and preserve it.
                bool hasBom = rawBytes.Length >= 3
                    && rawBytes[0] == 0xEF && rawBytes[1] == 0xBB && rawBytes[2] == 0xBF;

                // Strict UTF-8 decode: a decomp source that is not valid UTF-8 must NOT be
                // silently lossily decoded (U+FFFD) — that would corrupt unrelated bytes on
                // re-encode and break the churn-free / byte-preserving guarantee. Fail fast,
                // file left untouched (Copilot PR #1145 inline finding).
                string sourceText;
                try
                {
                    var strictUtf8 = new UTF8Encoding(false, throwOnInvalidBytes: true);
                    sourceText = strictUtf8.GetString(
                        rawBytes, hasBom ? 3 : 0, rawBytes.Length - (hasBom ? 3 : 0));
                }
                catch (Exception ex) // DecoderFallbackException (or ArgumentException)
                {
                    result.Status = DecompSourceWriteStatus.Error;
                    result.Message = $"Source file is not valid UTF-8 — refusing to rewrite (left untouched): {ex.Message}";
                    return result;
                }

                // Pure rewrite (validates all gates 7-9 before producing new text).
                // Route to the JSON or the C-struct rewriter by declared format (#1141).
                DecompSourceWriteResult rewrite = isJson
                    ? RewriteJsonEntryText(sourceText, owner, entryId, changedFields, out string newSourceText)
                    : RewriteEntryText(sourceText, owner, entryId, changedFields, out newSourceText);
                // Carry over diagnostics.
                rewrite.SourceFile = absPath;
                rewrite.EntryId = entryId;
                if (!rewrite.Ok)
                    return rewrite;

                // No-op rewrite (empty change-set, all values already equal, or all
                // macro skips): the text is byte-identical. Do NOT touch the file and
                // do NOT flag a rebuild — a false rebuild signal + needless timestamp
                // bump would be wrong (#1132 review finding 2).
                if (rewrite.ChangedFields == null || rewrite.ChangedFields.Count == 0
                    || string.Equals(newSourceText, sourceText, StringComparison.Ordinal))
                {
                    rewrite.Status = DecompSourceWriteStatus.Ok;
                    if (string.IsNullOrEmpty(rewrite.Message) || rewrite.Message.StartsWith("Rewrote"))
                        rewrite.Message = "No change needed.";
                    return rewrite;
                }

                // Re-encode with the SAME BOM-state and write atomically.
                try
                {
                    byte[] outBytes = new UTF8Encoding(false).GetBytes(newSourceText);
                    if (hasBom)
                    {
                        var withBom = new byte[outBytes.Length + 3];
                        withBom[0] = 0xEF; withBom[1] = 0xBB; withBom[2] = 0xBF;
                        Array.Copy(outBytes, 0, withBom, 3, outBytes.Length);
                        outBytes = withBom;
                    }
                    AtomicWrite(absPath, outBytes);
                }
                catch (Exception ex)
                {
                    rewrite.Status = DecompSourceWriteStatus.Error;
                    rewrite.Message = $"Write failed (source left untouched): {ex.Message}";
                    return rewrite;
                }

                // Success: mark the project for rebuild.
                project.NeedsRebuild = true;
                return rewrite;
            }
            catch (Exception ex)
            {
                result.Status = DecompSourceWriteStatus.Error;
                result.Message = $"Unexpected fault: {ex.Message}";
                return result;
            }
        }

        /// <summary>
        /// PURE preview: rewrite the targeted element's integer-literal field token(s)
        /// in <paramref name="sourceText"/> and return the new text via
        /// <paramref name="newSourceText"/> WITHOUT touching disk. Used by the full
        /// writer and by unit tests / dry-runs. NEVER throws.
        ///
        /// Validation order: every changed-field name must be declared on the owner
        /// (else <see cref="DecompSourceWriteStatus.UnsupportedField"/>, no change);
        /// the array + element must parse (else <see cref="DecompSourceWriteStatus.ParseFailed"/>);
        /// each value token must be a plain integer literal
        /// (else <see cref="DecompSourceWriteStatus.UnsupportedField"/>, no change).
        /// </summary>
        public static DecompSourceWriteResult RewriteEntryText(
            string sourceText,
            DecompTableEntry owner,
            int entryId,
            IReadOnlyDictionary<string, uint> changedFields,
            out string newSourceText)
        {
            newSourceText = sourceText;
            var result = new DecompSourceWriteResult { EntryId = entryId };
            try
            {
                if (owner == null)
                {
                    result.Status = DecompSourceWriteStatus.NotOwned;
                    result.Message = "Owner is null.";
                    return result;
                }
                if (sourceText == null)
                {
                    result.Status = DecompSourceWriteStatus.ParseFailed;
                    result.Message = "Source text is null.";
                    return result;
                }
                if (changedFields == null || changedFields.Count == 0)
                {
                    // Nothing to change is a successful no-op — text identical, no churn.
                    result.Status = DecompSourceWriteStatus.Ok;
                    result.Message = "No change needed.";
                    result.ChangedFields = new List<string>();
                    newSourceText = sourceText;
                    return result;
                }

                // Build the declared-field name set + ordered list + signed/width map.
                var fieldOrder = new List<string>();
                var fieldSet = new HashSet<string>(StringComparer.Ordinal);
                var fieldDesc = new Dictionary<string, FieldDesc>(StringComparer.Ordinal);
                if (owner.Fields != null)
                {
                    foreach (DecompTableField f in owner.Fields)
                    {
                        if (f != null && !string.IsNullOrEmpty(f.Name))
                        {
                            fieldOrder.Add(f.Name);
                            fieldSet.Add(f.Name);
                            fieldDesc[f.Name] = new FieldDesc(f.Signed == true, f.Width);
                        }
                    }
                }

                // Validate-all: every changed field must be declared.
                foreach (var kv in changedFields)
                {
                    if (!fieldSet.Contains(kv.Key))
                    {
                        result.Status = DecompSourceWriteStatus.UnsupportedField;
                        result.Message = $"Field '{kv.Key}' is not declared in the manifest owner — no change.";
                        return result;
                    }
                }

                string symbol = owner.EffectiveSymbol;
                if (string.IsNullOrEmpty(symbol))
                {
                    result.Status = DecompSourceWriteStatus.MalformedManifest;
                    result.Message = "Owner declares no arrayName/symbol.";
                    return result;
                }

                // Locate the array body { ... }.
                if (!TryFindArrayBody(sourceText, symbol, out int bodyOpen, out int bodyClose))
                {
                    result.Status = DecompSourceWriteStatus.ParseFailed;
                    result.Message = $"Could not locate the array initializer for '{symbol}'.";
                    return result;
                }

                // Translate the manifest entry id to a 0-based element index using the
                // owner's declared index base (default 0). An id below the base is a
                // parse failure (the original entryId is kept in the message).
                int indexBase = owner.IndexBase ?? 0;
                int elementIndex = entryId - indexBase;
                if (elementIndex < 0)
                {
                    result.Status = DecompSourceWriteStatus.ParseFailed;
                    result.Message = $"Entry id {entryId} is below the declared indexBase {indexBase}.";
                    return result;
                }

                // Find the element at the 0-based elementIndex inside the body.
                if (!TryFindElementSpan(sourceText, bodyOpen, bodyClose, elementIndex,
                        out int elemOpen, out int elemClose))
                {
                    result.Status = DecompSourceWriteStatus.ParseFailed;
                    result.Message = $"Entry id {entryId} is out of range in array '{symbol}'.";
                    return result;
                }

                // elemOpen points at the '{' of the element, elemClose at its '}'.
                string elementText = sourceText.Substring(elemOpen, elemClose - elemOpen + 1);
                string innerBody = sourceText.Substring(elemOpen + 1, elemClose - elemOpen - 1);

                // Determine designated vs positional. An element is "designated" if it
                // contains at least one top-level `.field` designator.
                bool hasDesignators = ElementHasDesignators(innerBody);

                // A single-field change-set is an EXPLICIT field intent: a macro value
                // token then honestly fails (the user targeted that field). A bulk/
                // multi-field set treats an untouchable macro token as a SKIP (never
                // rewritten, never blocks the write) so unchanged macro fields the user
                // never edited don't fail the whole save.
                bool singleFieldIntent = changedFields.Count == 1;

                // Apply each changed field to the element text. We accumulate edits
                // into a fresh copy of elementText so all-or-nothing per element. Only
                // tokens that ACTUALLY change (int literal whose value differs) count.
                string editedElement = elementText;
                var changed = new List<string>();
                var skipped = new List<string>();

                foreach (var kv in changedFields)
                {
                    string field = kv.Key;
                    uint newVal = kv.Value;

                    fieldDesc.TryGetValue(field, out FieldDesc desc);
                    string next = ApplyFieldToElement(
                        editedElement, field, newVal, hasDesignators, fieldOrder, singleFieldIntent, desc,
                        out FieldApplyOutcome outcome, out DecompSourceWriteStatus failStatus, out string failMsg);

                    switch (outcome)
                    {
                        case FieldApplyOutcome.Changed:
                            editedElement = next;
                            changed.Add(field);
                            break;
                        case FieldApplyOutcome.NoOp:        // int literal already == newVal
                            // Legitimate no-op; NOT a skipped edit.
                            break;
                        case FieldApplyOutcome.SkippedMacro: // macro token in a bulk set
                            // The requested field could NOT be written (its source token is a
                            // macro/expression). Record it as skipped so the all-or-nothing
                            // save gates treat the write as PARTIAL (#1159).
                            skipped.Add(field);
                            break;
                        default: // Failed — macro in single-field intent, or a real fault
                            result.Status = failStatus;
                            result.Message = failMsg;
                            return result;   // no write — validate-all-before-mutate
                    }
                }

                // If nothing actually changed (empty diff: all no-ops / all skipped),
                // report a clean no-op and keep the text byte-identical (no churn).
                if (changed.Count == 0)
                {
                    result.Status = DecompSourceWriteStatus.Ok;
                    // A no-op with macro-skipped fields is NOT "no change needed" — the edits
                    // were unwritable (nothing matched), so report that honestly (#1159).
                    result.Message = skipped.Count > 0
                        ? $"No writable change: {skipped.Count} field(s) map to a macro/expression and were skipped."
                        : "No change needed.";
                    result.ChangedFields = new List<string>();
                    // Even with no textual change, a bulk set can have macro-skipped fields;
                    // those are real skipped edits the caller must surface (#1159).
                    result.SkippedFields = skipped;
                    result.BeforeText = elementText;
                    result.AfterText = elementText;
                    result.ChangedLineStart = LineOf(sourceText, elemOpen);
                    result.ChangedLineEnd = LineOf(sourceText, elemClose);
                    newSourceText = sourceText;
                    return result;
                }

                // Reassemble: prefix + edited element + suffix. Bytes outside the
                // element span are byte-identical.
                string prefix = sourceText.Substring(0, elemOpen);
                string suffix = sourceText.Substring(elemClose + 1);
                newSourceText = prefix + editedElement + suffix;

                result.Status = DecompSourceWriteStatus.Ok;
                result.Message = $"Rewrote {changed.Count} field(s) in entry {entryId}.";
                result.ChangedFields = changed;
                result.SkippedFields = skipped;   // partial write if non-empty (#1159)
                result.BeforeText = elementText;
                result.AfterText = editedElement;
                // 1-based line span of the element (in the original text).
                result.ChangedLineStart = LineOf(sourceText, elemOpen);
                result.ChangedLineEnd = LineOf(sourceText, elemClose);
                return result;
            }
            catch (Exception ex)
            {
                newSourceText = sourceText;
                result.Status = DecompSourceWriteStatus.Error;
                result.Message = $"Unexpected fault: {ex.Message}";
                return result;
            }
        }

        // =============================================================== JSON (#1141)

        /// <summary>
        /// PURE preview: rewrite the targeted JSON element's Number field token(s) in
        /// <paramref name="sourceText"/> and return the new text via
        /// <paramref name="newSourceText"/> WITHOUT touching disk. CHURN-FREE — only the
        /// exact byte span of each changed Number token is spliced; comments, trailing
        /// commas, whitespace, BOM-less encoding and every other byte are preserved.
        /// NEVER throws.
        ///
        /// Navigation: the top-level value is either a JSON ARRAY (index by
        /// <c>entryId - indexBase</c>) OR an OBJECT-map (look up the property whose name
        /// equals <c>(entryId - indexBase)</c>). A negative index ⇒ ParseFailed; a missing
        /// index/key ⇒ ParseFailed. Within the element object, each changed field must be
        /// declared on the owner (else UnsupportedField, no write) and its value must be a
        /// JSON Number (non-number ⇒ single-field Failed/UnsupportedField, bulk ⇒ skip).
        /// Signed fields emit a <c>-N</c> decimal when the value reinterprets negative.
        /// </summary>
        public static DecompSourceWriteResult RewriteJsonEntryText(
            string sourceText,
            DecompTableEntry owner,
            int entryId,
            IReadOnlyDictionary<string, uint> changedFields,
            out string newSourceText)
        {
            newSourceText = sourceText;
            var result = new DecompSourceWriteResult { EntryId = entryId };
            try
            {
                if (owner == null)
                {
                    result.Status = DecompSourceWriteStatus.NotOwned;
                    result.Message = "Owner is null.";
                    return result;
                }
                if (sourceText == null)
                {
                    result.Status = DecompSourceWriteStatus.ParseFailed;
                    result.Message = "Source text is null.";
                    return result;
                }
                if (changedFields == null || changedFields.Count == 0)
                {
                    result.Status = DecompSourceWriteStatus.Ok;
                    result.Message = "No change needed.";
                    result.ChangedFields = new List<string>();
                    newSourceText = sourceText;
                    return result;
                }

                // Validate-all: every changed field must be declared on the owner, and
                // build the signed/width descriptor map.
                var fieldSet = new HashSet<string>(StringComparer.Ordinal);
                var fieldDesc = new Dictionary<string, FieldDesc>(StringComparer.Ordinal);
                if (owner.Fields != null)
                {
                    foreach (DecompTableField f in owner.Fields)
                    {
                        if (f != null && !string.IsNullOrEmpty(f.Name))
                        {
                            fieldSet.Add(f.Name);
                            fieldDesc[f.Name] = new FieldDesc(f.Signed == true, f.Width);
                        }
                    }
                }
                foreach (var kv in changedFields)
                {
                    if (!fieldSet.Contains(kv.Key))
                    {
                        result.Status = DecompSourceWriteStatus.UnsupportedField;
                        result.Message = $"Field '{kv.Key}' is not declared in the manifest owner — no change.";
                        return result;
                    }
                }

                int indexBase = owner.IndexBase ?? 0;
                int elementIndex = entryId - indexBase;
                if (elementIndex < 0)
                {
                    result.Status = DecompSourceWriteStatus.ParseFailed;
                    result.Message = $"Entry id {entryId} is below the declared indexBase {indexBase}.";
                    return result;
                }

                // Work in UTF-8 BYTES so the Utf8JsonReader offsets splice directly.
                byte[] bytes = new UTF8Encoding(false).GetBytes(sourceText);

                // 0) Validate the WHOLE document FIRST (#1145). TryFindJsonElementSpan stops
                // reading as soon as it captures the target element, so a truncated/malformed
                // tail (e.g. a missing closing ']') would otherwise be spliced as if valid.
                // A full pass with the same options rejects the splice with NO mutation.
                if (!IsWholeJsonDocumentValid(bytes))
                {
                    result.Status = DecompSourceWriteStatus.ParseFailed;
                    result.Message = "JSON source is malformed (failed full-document validation) — no change.";
                    return result;
                }

                // 1) Locate the target element object's byte span.
                if (!TryFindJsonElementSpan(bytes, elementIndex, out long elemStart, out long elemEnd))
                {
                    result.Status = DecompSourceWriteStatus.ParseFailed;
                    result.Message = $"Entry id {entryId} (index {elementIndex}) not found / not an object in the JSON.";
                    return result;
                }

                bool singleFieldIntent = changedFields.Count == 1;
                var changed = new List<string>();
                var skipped = new List<string>();

                // Accumulate edits as (byteStart, byteLen, replacementBytes) on the
                // ORIGINAL byte offsets; apply right-to-left so earlier offsets stay valid.
                var edits = new List<(int start, int len, byte[] repl)>();

                foreach (var kv in changedFields)
                {
                    string field = kv.Key;
                    uint newVal = kv.Value;
                    fieldDesc.TryGetValue(field, out FieldDesc desc);

                    // Find the field's Number value token span within the element object.
                    JsonFieldLocate loc = LocateJsonNumberField(bytes, elemStart, elemEnd, field);

                    if (loc.Kind == JsonLocateKind.NotFound)
                    {
                        // Field absent from this element. Single-field intent → honest
                        // fail; bulk → skip (the user never edited it).
                        if (singleFieldIntent)
                        {
                            result.Status = DecompSourceWriteStatus.UnsupportedField;
                            result.Message = $"Field '{field}' not present in JSON entry {entryId} — edit manually.";
                            return result;
                        }
                        skipped.Add(field);   // requested but unwritable → partial (#1159)
                        continue;
                    }
                    if (loc.Kind == JsonLocateKind.NonNumber)
                    {
                        // String/bool/object/array value. Single-field → fail; bulk → skip.
                        if (singleFieldIntent)
                        {
                            result.Status = DecompSourceWriteStatus.UnsupportedField;
                            result.Message = $"Field '{field}' in JSON entry {entryId} is not a number — edit manually.";
                            return result;
                        }
                        skipped.Add(field);   // requested but unwritable → partial (#1159)
                        continue;
                    }

                    // loc.Kind == Number: compute the new token + no-op check.
                    string oldToken = new UTF8Encoding(false).GetString(bytes, (int)loc.Start, (int)(loc.End - loc.Start));
                    string newToken = BuildJsonNumberToken(oldToken, newVal, desc, out bool isNoOp, out bool tokenOk);
                    if (!tokenOk)
                    {
                        // The existing token is not a plain integer literal we can rewrite
                        // (e.g. a float / exponent). Single-field → fail; bulk → skip.
                        if (singleFieldIntent)
                        {
                            result.Status = DecompSourceWriteStatus.UnsupportedField;
                            result.Message = $"Field '{field}' in JSON entry {entryId} is not an integer number — edit manually.";
                            return result;
                        }
                        skipped.Add(field);   // requested but unwritable → partial (#1159)
                        continue;
                    }
                    if (isNoOp)
                        continue;   // value already equal — legitimate no-op (NOT skipped)

                    edits.Add(((int)loc.Start, (int)(loc.End - loc.Start),
                        new UTF8Encoding(false).GetBytes(newToken)));
                    changed.Add(field);
                }

                // Before/after element text (for diagnostics + line span).
                string elementText = new UTF8Encoding(false).GetString(
                    bytes, (int)elemStart, (int)(elemEnd - elemStart));
                result.ChangedLineStart = LineOfByte(bytes, (int)elemStart);
                result.ChangedLineEnd = LineOfByte(bytes, (int)elemEnd - 1);

                if (changed.Count == 0)
                {
                    result.Status = DecompSourceWriteStatus.Ok;
                    // A no-op with skipped fields is NOT "no change needed" — the edits were
                    // unwritable (nothing matched), so report that honestly (#1159).
                    result.Message = skipped.Count > 0
                        ? $"No writable change: {skipped.Count} field(s) map to a macro/expression and were skipped."
                        : "No change needed.";
                    result.ChangedFields = new List<string>();
                    // A bulk set can have skipped (macro/non-number/absent) fields even with
                    // no textual change — surface them so the caller knows it was partial (#1159).
                    result.SkippedFields = skipped;
                    result.BeforeText = elementText;
                    result.AfterText = elementText;
                    newSourceText = sourceText;
                    return result;
                }

                // Apply edits right-to-left so earlier byte offsets stay valid.
                edits.Sort((a, b) => b.start.CompareTo(a.start));
                var outBytes = new List<byte>(bytes);
                foreach (var (start, len, repl) in edits)
                {
                    outBytes.RemoveRange(start, len);
                    outBytes.InsertRange(start, repl);
                }
                byte[] newRaw = outBytes.ToArray();
                newSourceText = new UTF8Encoding(false).GetString(newRaw);

                // Recompute the after-element text span by re-locating it (the element
                // start byte is stable; the end shifted by the net edit delta).
                int delta = 0;
                foreach (var (start, len, repl) in edits)
                    delta += repl.Length - len;
                int newElemEnd = (int)elemEnd + delta;
                if (newElemEnd >= elemStart && newElemEnd <= newRaw.Length)
                {
                    result.AfterText = new UTF8Encoding(false).GetString(
                        newRaw, (int)elemStart, newElemEnd - (int)elemStart);
                }
                else
                {
                    result.AfterText = elementText;
                }

                result.Status = DecompSourceWriteStatus.Ok;
                result.Message = $"Rewrote {changed.Count} field(s) in entry {entryId}.";
                result.ChangedFields = changed;
                result.SkippedFields = skipped;   // partial write if non-empty (#1159)
                result.BeforeText = elementText;
                return result;
            }
            catch (Exception ex)
            {
                newSourceText = sourceText;
                result.Status = DecompSourceWriteStatus.Error;
                result.Message = $"Unexpected fault: {ex.Message}";
                return result;
            }
        }

        static readonly JsonReaderOptions JsonReadOpts = new JsonReaderOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };

        /// <summary>
        /// Validate the WHOLE JSON document with the same <see cref="JsonReadOpts"/> the
        /// splice path uses (#1145). <see cref="Utf8JsonReader"/> throws a
        /// <c>JsonException</c> the moment it reaches invalid/truncated content (e.g. an
        /// array that is never closed), so reading every token until <c>Read()</c> returns
        /// false confirms the document is well-formed AND fully consumed. Returns false on
        /// any <c>JsonException</c> (malformed/truncated) — the caller then refuses the
        /// splice and leaves the source byte-identical. NEVER throws.
        /// </summary>
        static bool IsWholeJsonDocumentValid(byte[] bytes)
        {
            try
            {
                var reader = new Utf8JsonReader(bytes, JsonReadOpts);
                while (reader.Read())
                {
                    // Walk every token. A truncated/invalid doc makes Read() throw before
                    // it can return false, so reaching the natural end means valid + EOF.
                }
                return true;
            }
            catch (JsonException)
            {
                return false;
            }
            catch
            {
                // Any other unexpected fault → treat as invalid (never throw at the boundary).
                return false;
            }
        }

        /// <summary>
        /// Locate the byte span [start,end) of the target element OBJECT in a JSON
        /// document: array index <paramref name="elementIndex"/>, or object-map property
        /// whose name equals <c>elementIndex.ToString()</c>. The span covers the element's
        /// <c>{...}</c>. Returns false on any fault / missing index/key / non-object
        /// element. NEVER throws.
        /// </summary>
        static bool TryFindJsonElementSpan(byte[] bytes, int elementIndex, out long start, out long end)
        {
            start = -1; end = -1;
            try
            {
                var reader = new Utf8JsonReader(bytes, JsonReadOpts);
                if (!reader.Read())
                    return false;

                if (reader.TokenType == JsonTokenType.StartArray)
                {
                    int idx = 0;
                    while (reader.Read())
                    {
                        if (reader.TokenType == JsonTokenType.EndArray)
                            return false;   // ran off the end before reaching elementIndex
                        // Each top-level array value begins here.
                        if (idx == elementIndex)
                        {
                            if (reader.TokenType != JsonTokenType.StartObject)
                                return false;   // element exists but is not an object
                            return CaptureObjectSpan(ref reader, out start, out end);
                        }
                        SkipValue(ref reader);
                        idx++;
                    }
                    return false;
                }
                else if (reader.TokenType == JsonTokenType.StartObject)
                {
                    string key = elementIndex.ToString(CultureInfo.InvariantCulture);
                    while (reader.Read())
                    {
                        if (reader.TokenType == JsonTokenType.EndObject)
                            return false;
                        if (reader.TokenType != JsonTokenType.PropertyName)
                            return false;   // malformed
                        bool match = reader.GetString() == key;
                        if (!reader.Read())
                            return false;
                        if (match)
                        {
                            if (reader.TokenType != JsonTokenType.StartObject)
                                return false;
                            return CaptureObjectSpan(ref reader, out start, out end);
                        }
                        SkipValue(ref reader);
                    }
                    return false;
                }
                return false;
            }
            catch
            {
                start = -1; end = -1;
                return false;
            }
        }

        /// <summary>
        /// Given a reader positioned ON a StartObject token, capture the byte span of the
        /// whole object (from its '{' to the matching '}', inclusive-exclusive end). NEVER throws.
        /// </summary>
        static bool CaptureObjectSpan(ref Utf8JsonReader reader, out long start, out long end)
        {
            start = reader.TokenStartIndex;   // byte index of '{'
            end = -1;
            int depth = 0;
            // We're on StartObject. Walk until the matching EndObject.
            do
            {
                if (reader.TokenType == JsonTokenType.StartObject || reader.TokenType == JsonTokenType.StartArray)
                    depth++;
                else if (reader.TokenType == JsonTokenType.EndObject || reader.TokenType == JsonTokenType.EndArray)
                {
                    depth--;
                    if (depth == 0)
                    {
                        end = reader.BytesConsumed;   // just past the '}'
                        return true;
                    }
                }
                if (!reader.Read())
                    return false;
            } while (true);
        }

        /// <summary>Skip the value the reader is currently positioned on (scalar or container).</summary>
        static void SkipValue(ref Utf8JsonReader reader)
        {
            if (reader.TokenType == JsonTokenType.StartObject || reader.TokenType == JsonTokenType.StartArray)
                reader.Skip();
            // scalars are already fully consumed by the Read() that landed on them.
        }

        enum JsonLocateKind { NotFound, NonNumber, Number }

        readonly struct JsonFieldLocate
        {
            public readonly JsonLocateKind Kind;
            public readonly long Start;   // byte index of the value token start
            public readonly long End;     // byte index just past the value token
            public JsonFieldLocate(JsonLocateKind kind, long start, long end)
            { Kind = kind; Start = start; End = end; }
            public static readonly JsonFieldLocate NotFound = new JsonFieldLocate(JsonLocateKind.NotFound, -1, -1);
            public static JsonFieldLocate NonNumber(long s, long e) => new JsonFieldLocate(JsonLocateKind.NonNumber, s, e);
            public static JsonFieldLocate Number(long s, long e) => new JsonFieldLocate(JsonLocateKind.Number, s, e);
        }

        /// <summary>
        /// Within the element-object byte span [elemStart,elemEnd), find the TOP-LEVEL
        /// property whose name equals <paramref name="field"/> and return the byte span of
        /// its VALUE token + whether it is a Number. Comment/trailing-comma tolerant.
        /// NEVER throws.
        /// </summary>
        static JsonFieldLocate LocateJsonNumberField(byte[] bytes, long elemStart, long elemEnd, string field)
        {
            try
            {
                var slice = new ReadOnlySpan<byte>(bytes, (int)elemStart, (int)(elemEnd - elemStart));
                var reader = new Utf8JsonReader(slice, JsonReadOpts);
                if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
                    return JsonFieldLocate.NotFound;

                while (reader.Read())
                {
                    if (reader.TokenType == JsonTokenType.EndObject)
                        break;
                    if (reader.TokenType != JsonTokenType.PropertyName)
                        return JsonFieldLocate.NotFound;   // malformed
                    bool match = reader.GetString() == field;
                    long valStart = reader.BytesConsumed; // not yet the value; read it next
                    if (!reader.Read())
                        return JsonFieldLocate.NotFound;

                    if (match)
                    {
                        long vStartRel = reader.TokenStartIndex;
                        long vEndRel = reader.BytesConsumed;
                        long vStartAbs = elemStart + vStartRel;
                        long vEndAbs = elemStart + vEndRel;
                        if (reader.TokenType == JsonTokenType.Number)
                            return JsonFieldLocate.Number(vStartAbs, vEndAbs);
                        return JsonFieldLocate.NonNumber(vStartAbs, vEndAbs);
                    }
                    // Not our field: skip the value (containers fully).
                    SkipValue(ref reader);
                }
                return JsonFieldLocate.NotFound;
            }
            catch
            {
                return JsonFieldLocate.NotFound;
            }
        }

        /// <summary>
        /// Build the replacement JSON Number token from the existing token + the requested
        /// value. JSON numbers have no hex/suffix, so the output is a plain decimal (or
        /// <c>-N</c> for a signed field whose value reinterprets negative). Sets
        /// <paramref name="isNoOp"/> when the existing integer already equals the request,
        /// and <paramref name="tokenOk"/> false when the existing token is not a plain
        /// integer (e.g. a float / exponent) we can safely rewrite. NEVER throws.
        /// </summary>
        static string BuildJsonNumberToken(string oldToken, uint newVal, FieldDesc desc, out bool isNoOp, out bool tokenOk)
        {
            isNoOp = false; tokenOk = false;
            string trimmed = (oldToken ?? "").Trim();

            if (desc.Signed)
            {
                // Existing token must parse as a signed integer (decimal; JSON has no hex).
                if (!TryParseSignedIntLiteral(trimmed, out _, out _, out long oldSigned, out bool oldWasNegative))
                    return oldToken;   // tokenOk stays false
                tokenOk = true;
                long newSigned = SignExtend(newVal, desc.Width);
                // Width-normalize the EXISTING literal so a bit-pattern form like 255 at
                // width 1 reinterprets to -1 and matches a request of -1 / 0xFF (#1145).
                long oldNormalized = NormalizeExistingSigned(oldSigned, oldWasNegative, desc.Width);
                if (oldNormalized == newSigned) { isNoOp = true; return oldToken; }
                return newSigned.ToString(CultureInfo.InvariantCulture);
            }
            else
            {
                if (!TryParseIntLiteral(trimmed, out _, out _, out uint oldVal))
                    return oldToken;   // tokenOk stays false (float/exponent/etc.)
                tokenOk = true;
                if (oldVal == newVal) { isNoOp = true; return oldToken; }
                return newVal.ToString(CultureInfo.InvariantCulture);
            }
        }

        /// <summary>1-based line number of <paramref name="byteIndex"/> in a UTF-8 byte array.</summary>
        static int LineOfByte(byte[] bytes, int byteIndex)
        {
            if (byteIndex < 0) byteIndex = 0;
            if (byteIndex > bytes.Length) byteIndex = bytes.Length;
            int line = 1;
            for (int i = 0; i < byteIndex; i++)
                if (bytes[i] == (byte)'\n') line++;
            return line;
        }

        // ----------------------------------------------------------------- parsing

        /// <summary>
        /// Locate the top-level array initializer body for <paramref name="symbol"/>:
        /// the identifier, optionally followed by <c>[...]</c> and qualifiers, then
        /// <c>= {</c>. Returns the index of the opening <c>{</c> and the matching
        /// closing <c>}</c> (comment/string aware brace counting). NEVER throws.
        /// </summary>
        static bool TryFindArrayBody(string text, string symbol, out int bodyOpen, out int bodyClose)
        {
            bodyOpen = -1; bodyClose = -1;
            int searchFrom = 0;
            while (true)
            {
                int idx = IndexOfIdentifier(text, symbol, searchFrom);
                if (idx < 0)
                    return false;

                // Advance past identifier.
                int p = idx + symbol.Length;
                // Skip whitespace/comments, optional [ ... ] (possibly repeated), more
                // whitespace, then require '='.
                p = SkipTrivia(text, p);
                // Optional array subscript(s): [ ... ]
                while (p < text.Length && text[p] == '[')
                {
                    int close = MatchBracket(text, p, '[', ']');
                    if (close < 0) { p = -1; break; }
                    p = SkipTrivia(text, close + 1);
                }
                if (p < 0)
                {
                    searchFrom = idx + symbol.Length;
                    continue;
                }
                // Require '='.
                if (p < text.Length && text[p] == '=')
                {
                    p = SkipTrivia(text, p + 1);
                    if (p < text.Length && text[p] == '{')
                    {
                        int close = MatchBrace(text, p);
                        if (close > p)
                        {
                            bodyOpen = p;
                            bodyClose = close;
                            return true;
                        }
                    }
                }
                // Not a match here; keep searching after this occurrence.
                searchFrom = idx + symbol.Length;
            }
        }

        /// <summary>
        /// Within the array body (exclusive of its own braces), find the span of the
        /// element at zero-based <paramref name="elementIndex"/>: each top-level
        /// <c>{ ... }</c> is one element. Returns the element's opening/closing brace
        /// indices in the full text. NEVER throws.
        /// </summary>
        static bool TryFindElementSpan(
            string text, int bodyOpen, int bodyClose, int elementIndex,
            out int elemOpen, out int elemClose)
        {
            elemOpen = -1; elemClose = -1;
            if (elementIndex < 0)
                return false;

            int i = bodyOpen + 1;
            int count = 0;
            while (i < bodyClose)
            {
                char c = text[i];
                // Skip comments/strings at the body level.
                int skipTo = SkipTriviaAndLiterals(text, i, bodyClose);
                if (skipTo != i)
                {
                    i = skipTo;
                    continue;
                }
                if (c == '{')
                {
                    int close = MatchBrace(text, i);
                    if (close < 0 || close > bodyClose)
                        return false;
                    if (count == elementIndex)
                    {
                        elemOpen = i;
                        elemClose = close;
                        return true;
                    }
                    count++;
                    i = close + 1;
                    continue;
                }
                i++;
            }
            return false;
        }

        /// <summary>
        /// True when the element inner body has at least one top-level <c>.field</c>
        /// designator (i.e. a '.' that begins a designator at depth 0, preceded by
        /// '{' / ',' / whitespace and followed by an identifier-start char).
        /// </summary>
        static bool ElementHasDesignators(string innerBody)
        {
            int depth = 0;
            int i = 0;
            int n = innerBody.Length;
            while (i < n)
            {
                int skip = SkipTriviaAndLiterals(innerBody, i, n);
                if (skip != i) { i = skip; continue; }
                char c = innerBody[i];
                if (c == '{' || c == '[' || c == '(') { depth++; i++; continue; }
                if (c == '}' || c == ']' || c == ')') { depth--; i++; continue; }
                if (depth == 0 && c == '.')
                {
                    // Look ahead for an identifier start.
                    int j = i + 1;
                    if (j < n && (char.IsLetter(innerBody[j]) || innerBody[j] == '_'))
                        return true;
                }
                i++;
            }
            return false;
        }

        /// <summary>
        /// Apply a single field change to one element's text. Returns the new element
        /// text via the return value, and the outcome via <paramref name="outcome"/>.
        /// <para>A macro/expression value token is a SKIP (no change) when
        /// <paramref name="singleFieldIntent"/> is false (bulk write), but a hard
        /// <see cref="FieldApplyOutcome.Failed"/> when it is true (the user explicitly
        /// targeted that one field).</para>
        /// On a real fault, returns the input unchanged with a status/message. NEVER throws.
        /// </summary>
        static string ApplyFieldToElement(
            string elementText, string field, uint newVal,
            bool hasDesignators, List<string> fieldOrder, bool singleFieldIntent, FieldDesc desc,
            out FieldApplyOutcome outcome, out DecompSourceWriteStatus failStatus, out string failMsg)
        {
            outcome = FieldApplyOutcome.Failed;
            failStatus = DecompSourceWriteStatus.UnsupportedField;
            failMsg = "";

            // elementText is "{ ... }". Work over the inner body.
            int innerStart = 1;                       // just after '{'
            int innerEnd = elementText.Length - 1;    // index of '}'

            if (hasDesignators)
            {
                // Designated path: find ".field" at top level, then '=', then value token.
                int desigPos = FindDesignator(elementText, innerStart, innerEnd, field);
                if (desigPos < 0)
                {
                    failStatus = DecompSourceWriteStatus.Manual;
                    failMsg = $"Field '{field}' designator not found in a designated element — edit manually.";
                    return elementText;
                }
                // Move past ".field" and whitespace to '='.
                int p = desigPos + 1 + field.Length;
                p = SkipTrivia(elementText, p);
                if (p >= innerEnd || elementText[p] != '=')
                {
                    failStatus = DecompSourceWriteStatus.ParseFailed;
                    failMsg = $"Field '{field}' designator is malformed (no '=').";
                    return elementText;
                }
                p = SkipTrivia(elementText, p + 1);
                // value token runs until next top-level ',' or the closing '}'.
                int valEnd = FindTopLevelValueEnd(elementText, p, innerEnd);
                return ReplaceValueToken(elementText, p, valEnd, newVal, singleFieldIntent, desc,
                    out outcome, out failStatus, out failMsg);
            }
            else
            {
                // Positional path: split top-level comma-separated tokens.
                int fieldIndex = fieldOrder.IndexOf(field);
                if (fieldIndex < 0)
                {
                    failStatus = DecompSourceWriteStatus.UnsupportedField;
                    failMsg = $"Field '{field}' has no positional index (not in declared field order).";
                    return elementText;
                }
                List<(int start, int end)> tokens = SplitTopLevelTokens(elementText, innerStart, innerEnd);
                if (fieldIndex >= tokens.Count)
                {
                    failStatus = DecompSourceWriteStatus.ParseFailed;
                    failMsg = $"Positional field index {fieldIndex} exceeds element token count {tokens.Count}.";
                    return elementText;
                }
                var (ts, te) = tokens[fieldIndex];
                // Trim leading/trailing whitespace inside the token span.
                int s = ts, e = te;
                while (s < e && char.IsWhiteSpace(elementText[s])) s++;
                while (e > s && char.IsWhiteSpace(elementText[e - 1])) e--;
                return ReplaceValueToken(elementText, s, e, newVal, singleFieldIntent, desc,
                    out outcome, out failStatus, out failMsg);
            }
        }

        /// <summary>
        /// Replace the [start,end) span (a value token) with <paramref name="newVal"/>
        /// IF the existing token is a plain integer literal that DIFFERS from it. Hex
        /// literals are re-emitted in hex, decimal in decimal; an integer suffix
        /// (u/U/l/L) is preserved — EXCEPT that a NEGATIVE signed re-emit drops the
        /// unsigned (u/U) marker, since "-1u" is a unary-negated UNSIGNED literal in C
        /// (wrong semantics); any l/L markers are kept (#1145).
        /// <list type="bullet">
        ///   <item><description>integer literal whose value differs → <see cref="FieldApplyOutcome.Changed"/></description></item>
        ///   <item><description>integer literal already equal to <paramref name="newVal"/> → <see cref="FieldApplyOutcome.NoOp"/> (no churn)</description></item>
        ///   <item><description>macro/identifier/expression + bulk set → <see cref="FieldApplyOutcome.SkippedMacro"/> (untouched)</description></item>
        ///   <item><description>macro/identifier/expression + single-field intent → <see cref="FieldApplyOutcome.Failed"/> (UnsupportedField)</description></item>
        /// </list>
        /// NEVER throws.
        /// </summary>
        static string ReplaceValueToken(
            string text, int start, int end, uint newVal, bool singleFieldIntent, FieldDesc desc,
            out FieldApplyOutcome outcome, out DecompSourceWriteStatus failStatus, out string failMsg)
        {
            outcome = FieldApplyOutcome.Failed;
            failStatus = DecompSourceWriteStatus.UnsupportedField;
            failMsg = "";

            if (start < 0 || end <= start || end > text.Length)
            {
                failStatus = DecompSourceWriteStatus.ParseFailed;
                failMsg = "Empty or invalid value token span.";
                return text;
            }
            string token = text.Substring(start, end - start);
            string trimmed = token.Trim();

            // ---------------- SIGNED field path (#1141) ----------------
            // A signed field accepts a leading '-' on the existing token and re-emits the
            // requested value as a signed decimal when it reinterprets to a negative
            // number. The change-set value (uint) carries the two's-complement bits; we
            // sign-extend the low Width*8 bits to a signed long.
            if (desc.Signed)
            {
                if (!TryParseSignedIntLiteral(trimmed, out bool sIsHex, out string sSuffix, out long oldSigned, out bool sWasNegative))
                {
                    if (singleFieldIntent)
                    {
                        failStatus = DecompSourceWriteStatus.UnsupportedField;
                        failMsg = $"Value '{trimmed}' is a macro/identifier/expression, not an integer literal — edit manually.";
                        outcome = FieldApplyOutcome.Failed;
                    }
                    else
                    {
                        outcome = FieldApplyOutcome.SkippedMacro;
                    }
                    return text;
                }

                long newSigned = SignExtend(newVal, desc.Width);

                // No-op: compare SIGNED values, width-normalizing the EXISTING literal so a
                // bit-pattern form like 0xFF / 255 (no leading '-') at width 1 reinterprets
                // to -1 and matches a request that SignExtends to -1 (#1145). An explicit
                // '-N' literal is already the signed value and is kept verbatim.
                long oldNormalized = NormalizeExistingSigned(oldSigned, sWasNegative, desc.Width);
                if (oldNormalized == newSigned)
                {
                    outcome = FieldApplyOutcome.NoOp;
                    return text;
                }

                // Re-emit. For a signed field whose existing token is hex AND the new
                // value is non-negative, keep hex; otherwise (negative, or decimal token)
                // emit a signed decimal. This keeps the sign unambiguous (documented rule).
                // When the result is NEGATIVE, drop any unsigned (u/U) suffix marker first:
                // "-1u" is a unary-negated UNSIGNED literal in C (wrong semantics) (#1145).
                string emitSuffix = newSigned < 0 ? StripUnsignedSuffix(sSuffix) : sSuffix;
                string sToken;
                if (sIsHex && newSigned >= 0)
                    sToken = "0x" + ((ulong)newSigned).ToString("X", CultureInfo.InvariantCulture) + emitSuffix;
                else
                    sToken = newSigned.ToString(CultureInfo.InvariantCulture) + emitSuffix;

                outcome = FieldApplyOutcome.Changed;
                return text.Substring(0, start) + sToken + text.Substring(end);
            }

            // ---------------- UNSIGNED field path (unchanged from #1132) ----------------
            if (!TryParseIntLiteral(trimmed, out bool isHex, out string suffix, out uint oldVal))
            {
                // Not an integer literal. A bulk write skips it; a single-field intent
                // (the user explicitly targeted this field) is an honest hard fail.
                if (singleFieldIntent)
                {
                    failStatus = DecompSourceWriteStatus.UnsupportedField;
                    failMsg = $"Value '{trimmed}' is a macro/identifier/expression, not an integer literal — edit manually.";
                    outcome = FieldApplyOutcome.Failed;
                }
                else
                {
                    outcome = FieldApplyOutcome.SkippedMacro;
                }
                return text;
            }

            // No-op: the existing literal already represents the requested value. Leave
            // the token (and its radix/suffix/formatting) exactly as written — no churn.
            if (oldVal == newVal)
            {
                outcome = FieldApplyOutcome.NoOp;
                return text;
            }

            string newToken = isHex
                ? "0x" + newVal.ToString("X", CultureInfo.InvariantCulture) + suffix
                : newVal.ToString(CultureInfo.InvariantCulture) + suffix;

            outcome = FieldApplyOutcome.Changed;
            return text.Substring(0, start) + newToken + text.Substring(end);
        }

        /// <summary>
        /// Sign-extend the low <c>width*8</c> bits of <paramref name="raw"/> to a signed
        /// long. A null/&lt;=0/&gt;4 width defaults to 4 bytes (32-bit). Width 1/2/4 are
        /// the only meaningful field widths; any other value falls back to 4.
        /// </summary>
        static long SignExtend(uint raw, int? width)
        {
            int w = width ?? 4;
            switch (w)
            {
                case 1: return (sbyte)(byte)raw;
                case 2: return (short)(ushort)raw;
                default: return (int)raw;   // 4-byte (and any other width) → 32-bit signed
            }
        }

        /// <summary>
        /// Strip the unsigned (u/U) marker(s) from an integer-literal suffix, keeping
        /// any long (l/L) markers in order. Used when emitting a NEGATIVE signed
        /// literal for a signed field: "-1u" is a unary-negated UNSIGNED literal in C
        /// (wrong semantics), so the 'u' must go (Copilot PR #1145 inline finding).
        /// </summary>
        static string StripUnsignedSuffix(string suffix)
        {
            if (string.IsNullOrEmpty(suffix)) return suffix ?? "";
            var sb = new StringBuilder(suffix.Length);
            foreach (char c in suffix)
                if (c != 'u' && c != 'U') sb.Append(c);
            return sb.ToString();
        }

        /// <summary>
        /// Per-field descriptor threaded into the apply path: whether the field is signed
        /// and its byte width. Default (<c>default(FieldDesc)</c>) = unsigned, width null
        /// (so an unmapped field behaves exactly like #1132).
        /// </summary>
        readonly struct FieldDesc
        {
            public readonly bool Signed;
            public readonly int? Width;
            public FieldDesc(bool signed, int? width) { Signed = signed; Width = width; }
        }

        /// <summary>
        /// Parse a plain integer literal: decimal (<c>123</c>) or hex (<c>0x1F</c>),
        /// optionally with a single trailing run of u/U/l/L suffix chars. Outputs the
        /// parsed value in <paramref name="value"/> (so callers can detect no-ops).
        /// Returns false for anything else (identifier, macro, expression, float,
        /// overflow beyond 32-bit, etc.).
        /// </summary>
        static bool TryParseIntLiteral(string s, out bool isHex, out string suffix, out uint value)
        {
            isHex = false; suffix = ""; value = 0;
            if (string.IsNullOrEmpty(s))
                return false;

            int i = 0;
            // Optional leading sign is NOT supported (decomp tables are unsigned/enum);
            // a leading '-' or '+' makes it an expression → reject.
            if (s[0] == '-' || s[0] == '+')
                return false;

            int n = s.Length;
            int digitsStart;
            if (n >= 2 && s[0] == '0' && (s[1] == 'x' || s[1] == 'X'))
            {
                isHex = true;
                i = 2;
                digitsStart = i;
                while (i < n && Uri.IsHexDigit(s[i])) i++;
                if (i == digitsStart) return false;   // "0x" with no digits
            }
            else
            {
                digitsStart = i;
                while (i < n && s[i] >= '0' && s[i] <= '9') i++;
                if (i == digitsStart) return false;   // no leading digit
            }

            int digitsEnd = i;

            // Remaining must be only suffix chars u/U/l/L.
            int suffixStart = i;
            while (i < n)
            {
                char c = s[i];
                if (c == 'u' || c == 'U' || c == 'l' || c == 'L') { i++; continue; }
                return false;   // trailing non-suffix char → not a plain literal
            }
            suffix = s.Substring(suffixStart);

            // Parse the digit run to a 32-bit value (overflow → reject, treat as non-literal).
            string digits = s.Substring(digitsStart, digitsEnd - digitsStart);
            bool parsed = isHex
                ? uint.TryParse(digits, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value)
                : uint.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out value);
            return parsed;
        }

        /// <summary>
        /// Parse a SIGNED plain integer literal (#1141): an optional single leading
        /// <c>-</c>, then decimal (<c>123</c>) or hex (<c>0x1F</c>), optionally with a
        /// trailing run of u/U/l/L suffix chars. Outputs the SIGNED value in
        /// <paramref name="value"/> (a leading '-' negates the magnitude). A leading '+'
        /// is rejected (an expression, like the unsigned path). Returns false for
        /// anything else (identifier, macro, expression, float, overflow). NEVER throws.
        /// <paramref name="wasNegative"/> is true only when the literal was written WITH an
        /// explicit leading <c>-</c> — callers use it to distinguish an already-signed
        /// value (e.g. <c>-1</c>) from a stored bit pattern (e.g. <c>0xFF</c>) when
        /// width-normalizing the existing literal for the no-op comparison (#1145).
        /// </summary>
        static bool TryParseSignedIntLiteral(string s, out bool isHex, out string suffix, out long value, out bool wasNegative)
        {
            isHex = false; suffix = ""; value = 0; wasNegative = false;
            if (string.IsNullOrEmpty(s))
                return false;

            bool negative = false;
            string mag = s;
            if (mag[0] == '-')
            {
                negative = true;
                mag = mag.Substring(1);
            }
            else if (mag[0] == '+')
            {
                return false;   // a leading '+' is an expression — reject (parity with unsigned).
            }

            // Reuse the unsigned magnitude parser; a leading '-' is not present now.
            if (!TryParseIntLiteral(mag, out isHex, out suffix, out uint magVal))
                return false;

            wasNegative = negative;
            value = negative ? -(long)magVal : magVal;
            return true;
        }

        /// <summary>
        /// Width-normalize an EXISTING signed integer literal to the signed value its bits
        /// represent at <paramref name="width"/> bytes, for the no-op comparison (#1145).
        /// A literal written WITHOUT an explicit <c>-</c> (e.g. <c>0xFF</c>, <c>255</c>) is
        /// a STORED bit pattern → reinterpret via <see cref="SignExtend"/> (<c>0xFF</c> at
        /// width 1 → <c>-1</c>). A literal written WITH an explicit <c>-</c> (e.g. <c>-1</c>)
        /// is ALREADY the signed value → keep it verbatim (never re-extend). The magnitude
        /// case is safe to cast back to <see cref="uint"/> because
        /// <see cref="TryParseIntLiteral"/> only succeeds for values inside the uint range.
        /// </summary>
        static long NormalizeExistingSigned(long parsedSigned, bool wasNegative, int? width)
        {
            if (wasNegative)
                return parsedSigned;                          // explicit signed decimal/hex — already the value
            return SignExtend((uint)parsedSigned, width);     // bit-pattern form (0xFF / 255 / ...)
        }

        // -------------------------------------------------- token / span scanners

        /// <summary>
        /// Find a top-level <c>.field</c> designator within [innerStart, innerEnd) of
        /// the element text. Returns the index of the '.', or -1. Comment/string aware,
        /// depth aware (only depth-0 designators match).
        /// </summary>
        static int FindDesignator(string text, int innerStart, int innerEnd, string field)
        {
            int depth = 0;
            int i = innerStart;
            while (i < innerEnd)
            {
                int skip = SkipTriviaAndLiterals(text, i, innerEnd);
                if (skip != i) { i = skip; continue; }
                char c = text[i];
                if (c == '{' || c == '[' || c == '(') { depth++; i++; continue; }
                if (c == '}' || c == ']' || c == ')') { depth--; i++; continue; }
                if (depth == 0 && c == '.')
                {
                    int j = i + 1;
                    // Match the exact field identifier followed by a non-identifier char.
                    if (j + field.Length <= innerEnd
                        && string.CompareOrdinal(text, j, field, 0, field.Length) == 0)
                    {
                        int after = j + field.Length;
                        bool boundary = after >= innerEnd || !IsIdentChar(text[after]);
                        if (boundary)
                            return i;
                    }
                }
                i++;
            }
            return -1;
        }

        /// <summary>
        /// From <paramref name="valStart"/>, return the exclusive end of a value token:
        /// the position of the next top-level ',' or the element's closing brace
        /// (innerEnd). Trailing whitespace is excluded. Comment/string/nesting aware.
        /// </summary>
        static int FindTopLevelValueEnd(string text, int valStart, int innerEnd)
        {
            int depth = 0;
            int i = valStart;
            int lastNonWs = valStart;
            while (i < innerEnd)
            {
                int skip = SkipTriviaAndLiterals(text, i, innerEnd);
                if (skip != i)
                {
                    // The trivia/literal counts as part of the token's extent only if
                    // it's a string/char literal (rare for ints); comments/whitespace
                    // do not extend lastNonWs.
                    i = skip;
                    continue;
                }
                char c = text[i];
                if (c == '{' || c == '[' || c == '(') { depth++; i++; lastNonWs = i; continue; }
                if (c == '}' || c == ']' || c == ')') { depth--; i++; lastNonWs = i; continue; }
                if (depth == 0 && c == ',')
                    break;
                if (!char.IsWhiteSpace(c)) lastNonWs = i + 1;
                i++;
            }
            return lastNonWs;
        }

        /// <summary>
        /// Split [innerStart, innerEnd) into top-level comma-separated token spans.
        /// Each span is [start, end) of the raw token (untrimmed). Comment/string/
        /// nesting aware. A trailing comma yields no empty final token.
        /// </summary>
        static List<(int start, int end)> SplitTopLevelTokens(string text, int innerStart, int innerEnd)
        {
            var spans = new List<(int, int)>();
            int depth = 0;
            int tokenStart = innerStart;
            int i = innerStart;
            while (i < innerEnd)
            {
                int skip = SkipTriviaAndLiterals(text, i, innerEnd);
                if (skip != i) { i = skip; continue; }
                char c = text[i];
                if (c == '{' || c == '[' || c == '(') { depth++; i++; continue; }
                if (c == '}' || c == ']' || c == ')') { depth--; i++; continue; }
                if (depth == 0 && c == ',')
                {
                    spans.Add((tokenStart, i));
                    tokenStart = i + 1;
                    i++;
                    continue;
                }
                i++;
            }
            // Final token (only if non-whitespace remains).
            int s = tokenStart, e = innerEnd;
            int ss = s;
            while (ss < e && char.IsWhiteSpace(text[ss])) ss++;
            if (ss < e)
                spans.Add((tokenStart, innerEnd));
            return spans;
        }

        // --------------------------------------------------- low-level scanners

        /// <summary>Find a whole-identifier occurrence of <paramref name="ident"/> from <paramref name="from"/>.</summary>
        static int IndexOfIdentifier(string text, string ident, int from)
        {
            if (string.IsNullOrEmpty(ident)) return -1;
            int i = from;
            while (true)
            {
                int idx = text.IndexOf(ident, i, StringComparison.Ordinal);
                if (idx < 0) return -1;
                bool leftOk = idx == 0 || !IsIdentChar(text[idx - 1]);
                int after = idx + ident.Length;
                bool rightOk = after >= text.Length || !IsIdentChar(text[after]);
                // Reject occurrences inside a comment or string literal: scan from the
                // start of the line is expensive; instead use the global skip walker to
                // verify this index is at "code" level.
                if (leftOk && rightOk && IsCodePosition(text, idx))
                    return idx;
                i = idx + ident.Length;
                if (i >= text.Length) return -1;
            }
        }

        /// <summary>
        /// True when <paramref name="pos"/> is NOT inside a // or /* */ comment or a
        /// string/char literal — i.e. it is a real code position. Walks from 0.
        /// </summary>
        static bool IsCodePosition(string text, int pos)
        {
            int i = 0;
            while (i < pos)
            {
                int skip = SkipTriviaAndLiterals(text, i, text.Length);
                if (skip > i)
                {
                    if (pos < skip) return false;  // pos fell inside the skipped region
                    i = skip;
                    continue;
                }
                i++;
            }
            return true;
        }

        static bool IsIdentChar(char c) => char.IsLetterOrDigit(c) || c == '_';

        /// <summary>
        /// Skip whitespace and comments starting at <paramref name="i"/>; returns the
        /// next non-trivia index. Does NOT skip string/char literals.
        /// </summary>
        static int SkipTrivia(string text, int i)
        {
            int n = text.Length;
            while (i < n)
            {
                char c = text[i];
                if (char.IsWhiteSpace(c)) { i++; continue; }
                if (c == '/' && i + 1 < n && text[i + 1] == '/')
                {
                    i += 2;
                    while (i < n && text[i] != '\n') i++;
                    continue;
                }
                if (c == '/' && i + 1 < n && text[i + 1] == '*')
                {
                    i += 2;
                    while (i + 1 < n && !(text[i] == '*' && text[i + 1] == '/')) i++;
                    i = Math.Min(n, i + 2);
                    continue;
                }
                break;
            }
            return i;
        }

        /// <summary>
        /// If <paramref name="i"/> begins a comment, string literal, or char literal,
        /// return the index just past it; otherwise return <paramref name="i"/>
        /// unchanged. Whitespace is also skipped (so callers can advance over it).
        /// Bounded by <paramref name="limit"/>. NEVER throws.
        /// </summary>
        static int SkipTriviaAndLiterals(string text, int i, int limit)
        {
            int n = Math.Min(limit, text.Length);
            if (i >= n) return i;
            char c = text[i];

            // Whitespace.
            if (char.IsWhiteSpace(c))
            {
                int j = i;
                while (j < n && char.IsWhiteSpace(text[j])) j++;
                return j;
            }
            // Line comment.
            if (c == '/' && i + 1 < n && text[i + 1] == '/')
            {
                int j = i + 2;
                while (j < n && text[j] != '\n') j++;
                return j;
            }
            // Block comment.
            if (c == '/' && i + 1 < n && text[i + 1] == '*')
            {
                int j = i + 2;
                while (j + 1 < n && !(text[j] == '*' && text[j + 1] == '/')) j++;
                return Math.Min(n, j + 2);
            }
            // String literal.
            if (c == '"')
            {
                int j = i + 1;
                while (j < n)
                {
                    if (text[j] == '\\') { j += 2; continue; }
                    if (text[j] == '"') { j++; break; }
                    j++;
                }
                return j;
            }
            // Char literal.
            if (c == '\'')
            {
                int j = i + 1;
                while (j < n)
                {
                    if (text[j] == '\\') { j += 2; continue; }
                    if (text[j] == '\'') { j++; break; }
                    j++;
                }
                return j;
            }
            return i;
        }

        /// <summary>Match a brace at <paramref name="open"/> ('{'), comment/string aware. Returns the '}' index or -1.</summary>
        static int MatchBrace(string text, int open) => MatchBracket(text, open, '{', '}');

        /// <summary>
        /// Match a bracket pair from <paramref name="open"/> (which must be the open
        /// char). Comment/string aware. Returns the matching close index, or -1.
        /// </summary>
        static int MatchBracket(string text, int open, char openCh, char closeCh)
        {
            if (open < 0 || open >= text.Length || text[open] != openCh)
                return -1;
            int depth = 0;
            int i = open;
            int n = text.Length;
            while (i < n)
            {
                int skip = SkipTriviaAndLiterals(text, i, n);
                if (skip != i) { i = skip; continue; }
                char c = text[i];
                if (c == openCh) { depth++; i++; continue; }
                if (c == closeCh)
                {
                    depth--;
                    if (depth == 0) return i;
                    i++;
                    continue;
                }
                i++;
            }
            return -1;
        }

        /// <summary>1-based line number of <paramref name="index"/> in <paramref name="text"/>.</summary>
        static int LineOf(string text, int index)
        {
            if (index < 0) index = 0;
            if (index > text.Length) index = text.Length;
            int line = 1;
            for (int i = 0; i < index; i++)
                if (text[i] == '\n') line++;
            return line;
        }

        /// <summary>
        /// Write <paramref name="bytes"/> to <paramref name="path"/> atomically: write
        /// to a sibling temp file then replace. Falls back to a direct write if the
        /// replace fails (e.g. cross-volume). Throws only the underlying IO exception
        /// (the caller wraps it).
        /// </summary>
        static void AtomicWrite(string path, byte[] bytes)
        {
            string dir = Path.GetDirectoryName(path);
            string tmp = Path.Combine(
                string.IsNullOrEmpty(dir) ? "." : dir,
                "." + Path.GetFileName(path) + ".febtmp");
            File.WriteAllBytes(tmp, bytes);
            try
            {
                if (File.Exists(path))
                    File.Replace(tmp, path, null);
                else
                    File.Move(tmp, path);
            }
            catch
            {
                // Fallback: direct overwrite, then clean up temp.
                File.WriteAllBytes(path, bytes);
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { /* best effort */ }
            }
        }
    }
}
