// SPDX-License-Identifier: GPL-3.0-or-later
// #1870 — shared ROM open/save for the single-view (web / Android) shell.
//
// The desktop MainWindow owns ROM open/save today (OpenRom_Click / SaveAsRom_Click
// + FinishLoadedRom). The single-view MainView shell (WebAssembly / Android) had
// NO way to open or save a ROM, so the deployed web app booted to an editor
// launcher that could do nothing useful (#1870).
//
// This service hoists the two reusable pieces out of MainWindow so BOTH hosts
// behave identically and never drift:
//   * InitializeLoadedRom(rom) — the CORE post-load init (CoreState.ROM, the
//     hardcode/asm-map cache, the four headless caches, the text encoders, the
//     text-id / flag caches, export + undo, the event/procs/AI scripts, patch
//     detection and the skill-name resolver). This is the runtime half of
//     MainWindow.FinishLoadedRom; the shell-only UI half (labels, recent files,
//     editor-panel visibility, autosave) stays in MainWindow. It is headless /
//     owner-free so it is unit-testable.
//   * OpenRomAsync(owner) / SaveRomAsync(owner) — Visual-owner entry points that
//     drive the StorageProvider pickers via TopLevel.GetTopLevel(owner) (so they
//     work from MainView's browser TopLevel, which is NOT a Window) and then run
//     the shared init. Open reads through the stream API (LoadFromStreamAsync) —
//     browser picks are read-only Blobs with no local path. Save uses a Save-As
//     picker (SaveRomFilePick) + a fresh writable stream, NOT a retained open
//     handle: browser open-handles are not writable on non-Chromium engines.
using System;
using System.IO;
using System.Threading.Tasks;
using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Platform.Storage;
using FEBuilderGBA.Avalonia.Dialogs;

namespace FEBuilderGBA.Avalonia.Services
{
    /// <summary>Result of an <see cref="RomFileService.OpenRomAsync"/> attempt.</summary>
    public enum RomOpenOutcome
    {
        /// <summary>The user cancelled the file picker — no state changed.</summary>
        Cancelled,

        /// <summary>A file was picked but could not be parsed as a ROM.</summary>
        Failed,

        /// <summary>A ROM was loaded and CoreState was initialized.</summary>
        Loaded,
    }

    /// <summary>
    /// Cross-shell ROM open/save + shared post-load initialization (#1870).
    /// Used by both the desktop <c>MainWindow</c> and the single-view
    /// <c>MainView</c> (WebAssembly / Android) shell.
    /// </summary>
    public static class RomFileService
    {
        /// <summary>
        /// Shared CORE post-load initialization for a freshly loaded ROM — the
        /// runtime half of <c>MainWindow.FinishLoadedRom</c>. Wires CoreState,
        /// the hardcode/asm-map cache, the headless caches, the text encoders,
        /// the text-id / flag caches, export + undo, the event/procs/AI scripts,
        /// patch detection and the skill-name resolver. No UI, no owner — safe to
        /// call from any shell and unit-testable. Each guarded step mirrors the
        /// desktop path exactly so desktop and single-view never drift.
        /// </summary>
        public static void InitializeLoadedRom(ROM rom)
        {
            CoreState.ROM = rom;

            // #1035: wire the patch-scan hardcode cache per ROM load, replacing
            // the no-op HeadlessAsmMapCache wired as the pre-ROM default. Created
            // fresh each load so a previous ROM's hardcode flags never leak; lazy.
            CoreState.AsmMapFileAsmCache = new CoreAsmMapCache(rom);

            // Wire headless caches so Core code doesn't NullRef.
            CoreState.CommentCache ??= new HeadlessEtcCache();
            CoreState.LintCache ??= new HeadlessEtcCache();
            CoreState.WorkSupportCache ??= new HeadlessEtcCache();
            CoreState.ResourceCache ??= new EtcCacheResource();

            // Wire text encoder — with HeadlessSystemTextEncoder fallback.
            if (CoreState.SystemTextEncoder == null || CoreState.SystemTextEncoder is HeadlessSystemTextEncoder)
            {
                try
                {
                    CoreState.SystemTextEncoder = new SystemTextEncoder(CoreState.TextEncoding, CoreState.ROM);
                }
                catch (Exception ex)
                {
                    Log.ErrorF("Failed to init SystemTextEncoder, using headless fallback: {0}", ex.Message);
                    // Use ROM-aware fallback so JP ROMs get Shift_JIS, not ISO-8859-1.
                    CoreState.SystemTextEncoder = new HeadlessSystemTextEncoder(CoreState.ROM);
                }
            }

            // Init Huffman text encoder.
            if (CoreState.FETextEncoder == null)
            {
                try { CoreState.FETextEncoder = new FETextEncode(); }
                catch (Exception ex) { Log.ErrorF("Failed to init FETextEncode: {0}", ex.Message); }
            }

            // Init text escape.
            CoreState.TextEscape ??= new TextEscape();

            // Text-ID reference cache (#1028 Slice A). The ctor reads the per-ROM
            // user TSV + shipped system names, so it is ROM/path/language-sensitive
            // and MUST be (re)created on EVERY ROM load (replace, not ??=).
            CoreState.UseTextIDCache = new TextIDCacheCore();

            // Init flag cache.
            if (CoreState.FlagCache == null)
            {
                try { CoreState.FlagCache = new EtcCacheFLag(); }
                catch (Exception ex) { Log.ErrorF("Failed to init FlagCache: {0}", ex.Message); }
            }

            // Init export function + undo.
            CoreState.ExportFunction ??= new ExportFunction();
            // The undo buffer holds byte-deltas for THIS ROM (and, since #1914, a
            // meaningful saved-position marker), so it must be recreated on every
            // ROM load — reusing it across loads would leave a freshly opened ROM
            // falsely "dirty" and could replay the previous ROM's deltas on Undo.
            // Mirrors the UseTextIDCache "recreate on EVERY ROM load" pattern above.
            CoreState.Undo = new Undo();

            // Init event scripts.
            try
            {
                if (CoreState.EventScript == null)
                {
                    CoreState.EventScript = new EventScript();
                    CoreState.EventScript.Load(EventScript.EventScriptType.Event);
                }
                if (CoreState.ProcsScript == null)
                {
                    CoreState.ProcsScript = new EventScript();
                    CoreState.ProcsScript.Load(EventScript.EventScriptType.Procs);
                }
                if (CoreState.AIScript == null)
                {
                    // AI scripts use a FIXED 16-byte instruction grid, so the
                    // unknown-opcode width must be 16 (mirrors WinForms
                    // Program.cs `new EventScript(16)`). With the default width of
                    // 4, an unrecognized opcode would wrongly decode as four
                    // 4-byte rows instead of one 16-byte WORD row (#757).
                    CoreState.AIScript = new EventScript(16);
                    CoreState.AIScript.Load(EventScript.EventScriptType.AI);
                }
            }
            catch (Exception ex)
            {
                Log.ErrorF("Failed to init EventScripts: {0}", ex.Message);
            }

            // Detect installed patches (SkillSystem, MagicSplit, etc.).
            PatchDetectionService.Instance.Refresh();

            // Wire skill name resolution callback for NameResolver.
            CoreState.SkillNameResolver = id => PatchDetectionService.Instance.ResolveSkillName(id);
        }

        /// <summary>
        /// Open a ROM via the storage-provider picker rooted at <paramref name="owner"/>'s
        /// TopLevel, load it through the stream API (browser picks have no local
        /// path), and run the shared <see cref="InitializeLoadedRom"/>. Returns
        /// <see cref="RomOpenOutcome.Cancelled"/> (no change), <see cref="RomOpenOutcome.Failed"/>
        /// (picked but unparseable) or <see cref="RomOpenOutcome.Loaded"/>.
        /// </summary>
        public static async Task<RomOpenOutcome> OpenRomAsync(Visual owner)
        {
            var file = await FileDialogHelper.OpenRomFilePick(TopLevel.GetTopLevel(owner));
            if (file == null) return RomOpenOutcome.Cancelled;

            // Opening a plain ROM clears any active decomp project (#1129), matching
            // MainWindow.OpenRom_Click. Cleared only AFTER the picker returns a real
            // file, so cancelling the dialog leaves a currently-open decomp preview
            // (and its read-only save guard) intact.
            CoreState.DecompProject = null;

            string displayName = file.Name ?? "rom.gba";
            var rom = new ROM();
            bool ok;
            await using (var stream = await file.OpenReadAsync())
            {
                var result = await rom.LoadFromStreamAsync(stream, displayName);
                ok = result.ok;
            }
            if (!ok) return RomOpenOutcome.Failed;

            InitializeLoadedRom(rom);
            return RomOpenOutcome.Loaded;
        }

        /// <summary>
        /// Save the current ROM through a Save-As picker rooted at
        /// <paramref name="owner"/>'s TopLevel. Writes to a real path when the
        /// target exposes one (desktop) or a fresh writable stream otherwise
        /// (browser File System Access / download, Android SAF). Returns the saved
        /// file's display name, or null when there is no ROM or the user cancels.
        /// </summary>
        public static async Task<string?> SaveRomAsync(Visual owner)
        {
            if (CoreState.ROM == null) return null;

            // Parity with desktop SaveRom_Click / SaveAsRom_Click: a decomp preview
            // ROM is source-backed and read-only — never overwrite it. Currently
            // unreachable from MainView (the single-view shell has no decomp-open
            // entry point) but guards any future decomp-capable shell that reuses
            // this shared service (#1870).
            if (CoreState.IsDecompMode) return null;

            string suggestedName = Path.GetFileName(CoreState.ROM.Filename ?? "rom.gba");
            var file = await FileDialogHelper.SaveRomFilePick(TopLevel.GetTopLevel(owner), suggestedName);
            if (file == null) return null;

            string? localPath = file.TryGetLocalPath();
            string displayName;
            if (!string.IsNullOrEmpty(localPath))
            {
                CoreState.ROM.Save(localPath, false);
                CoreState.ROM.Filename = localPath; // keep ROM.Filename in sync after Save As
                displayName = localPath;
            }
            else
            {
                // Browser / Android SAF: no local path. Write via a fresh writable
                // stream from the Save-As picker (NOT a retained open handle — those
                // are read-only Blobs on non-Chromium engines).
                await using (var stream = await file.OpenWriteAsync())
                {
                    await CoreState.ROM.SaveToStreamAsync(stream);
                }
                displayName = file.Name ?? "rom.gba";
                CoreState.ROM.Filename = displayName;
            }
            // Mark the ROM clean after a successful primary-ROM write so the undo
            // dirty-state (Undo.IsModified) and autosave skip-position stay in sync
            // with disk — the single-view shell (and any future caller of this
            // shared service) otherwise never cleared it, so the close prompt would
            // fire even right after Save (#1914). Reached only on success.
            // This is a Save As (ROM.Filename changed), so point autosave at the new
            // name BEFORE marking clean — parity with MainWindow.SaveAsRom_Click —
            // otherwise a running autosave keeps writing its sidecar next to the OLD
            // ROM path (#1914 review).
            AutoSaveService.Instance.UpdateRomFilename(displayName);
            AutoSaveService.Instance.MarkSaved();
            return displayName;
        }
    }
}
