using System;
using System.IO;
using System.Text;
using FEBuilderGBA;

namespace FEBuilderGBA.CLI
{
    /// <summary>
    /// High-level ROM initialization for CLI use.
    /// Sets up CoreState, loads config, text encoder, translation.
    /// </summary>
    public static class RomLoader
    {
        /// <summary>
        /// Initialize the Core environment (config, translation) without loading a ROM.
        /// </summary>
        public static void InitEnvironment()
        {
            string baseDir = CoreState.BaseDirectory;
            if (string.IsNullOrEmpty(baseDir))
                throw new InvalidOperationException("CoreState.BaseDirectory must be set before calling InitEnvironment.");

            // Register code pages for Shift-JIS, etc.
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            // Load config
            // #1799: always create the Config so CoreState.Config is never null on a
            // fresh install (shared with the Avalonia GUI via Config.LoadOrCreate) —
            // otherwise config writers like --lastrom persistence silently no-op.
            string configPath = Path.Combine(baseDir, "config", "config.xml");
            CoreState.Config = Config.LoadOrCreate(configPath);
        }

        /// <summary>
        /// Load a ROM file and set up CoreState.ROM.
        /// </summary>
        public static bool LoadRom(string romPath)
        {
            return LoadRom(romPath, null);
        }

        /// <summary>
        /// Load a ROM file and set up CoreState.ROM, optionally forcing version detection.
        /// </summary>
        /// <param name="romPath">Path to the ROM file.</param>
        /// <param name="forceVersion">Force version string (e.g. "FE8U", "FE7J", "FE6"). Null for auto-detect.</param>
        public static bool LoadRom(string romPath, string forceVersion)
        {
            if (!File.Exists(romPath))
            {
                CoreState.Services.ShowError($"ROM file not found: {romPath}");
                return false;
            }

            ROM rom = new ROM();
            bool ok;

            if (!string.IsNullOrEmpty(forceVersion))
            {
                // Use ROM.LoadForceVersion which maps version strings to internal codes
                ok = rom.LoadForceVersion(romPath, forceVersion);
                if (!ok)
                {
                    CoreState.Services.ShowError($"Failed to load ROM with forced version '{forceVersion}': {romPath}");
                    return false;
                }
            }
            else
            {
                ok = rom.Load(romPath, out string version);
                if (!ok)
                {
                    CoreState.Services.ShowError($"Failed to load ROM: {romPath} (version: {version})");
                    return false;
                }
            }

            CoreState.ROM = rom;
            return true;
        }

        /// <summary>
        /// Open a decomp project directory: detect it, resolve its built ROM, load
        /// that ROM as a read-only preview, and run full init.
        /// Returns false (with a ShowError) on any fault. #1129 slice 1.
        /// </summary>
        public static bool LoadProject(string dir)
        {
            var project = DecompProjectDetector.Detect(dir);
            if (project == null)
            {
                CoreState.Services.ShowError($"Not a decomp project directory: {dir}");
                return false;
            }

            var resolved = DecompProjectDetector.ResolveBuiltRom(dir, project);
            if (resolved.Status == DecompResolveStatus.NotBuilt)
            {
                CoreState.Services.ShowError("Project found but no built ROM — run the build first (e.g. `make`), then reload.");
                return false;
            }
            if (resolved.Status != DecompResolveStatus.Ok)
            {
                CoreState.Services.ShowError($"Not a decomp project directory: {dir}");
                return false;
            }

            project.BuiltRomPath = resolved.Path;
            CoreState.DecompProject = project;

            if (!LoadRom(resolved.Path, project.ForceVersion))
            {
                CoreState.DecompProject = null;
                return false;
            }

            InitFull();
            return true;
        }

        /// <summary>
        /// Full initialization after ROM is loaded.
        /// Wires caches, Huffman tree, text encoding, event scripts, flag cache, etc.
        /// Call this after LoadRom() succeeds for commands that need full ROM access.
        /// </summary>
        public static void InitFull()
        {
            if (CoreState.ROM == null)
                throw new InvalidOperationException("CoreState.ROM must be set before calling InitFull.");

            // Wire headless caches so Core code doesn't NullRef
            if (CoreState.CommentCache == null)
                CoreState.CommentCache = new HeadlessEtcCache();
            if (CoreState.LintCache == null)
                CoreState.LintCache = new HeadlessEtcCache();
            if (CoreState.WorkSupportCache == null)
                CoreState.WorkSupportCache = new HeadlessEtcCache();

            // #796: wire the headless free-space allocator that RecycleAddress
            // falls back to when no recycled region fits (WinForms wires this to
            // InputFormRef.AppendBinaryData in Program.cs). Headless CLI write
            // commands (--translate import, font import, etc.) build an empty
            // RecycleAddress, so without this any append would silently return
            // U.NOT_FOUND. Shared with the Avalonia app + tests via the Core helper.
            CoreState.WireHeadlessAppendBinaryData();

            // Wire text encoder
            if (CoreState.SystemTextEncoder == null || CoreState.SystemTextEncoder is HeadlessSystemTextEncoder)
            {
                try
                {
                    CoreState.SystemTextEncoder = new SystemTextEncoder(CoreState.TextEncoding, CoreState.ROM);
                }
                catch (Exception ex)
                {
                    Log.ErrorF("Failed to init SystemTextEncoder, using headless fallback: {0}", ex.Message);
                    // Use ROM-aware fallback so JP ROMs get Shift_JIS, not ISO-8859-1
                    CoreState.SystemTextEncoder = new HeadlessSystemTextEncoder(CoreState.ROM);
                }
            }

            // Init Huffman text encoder
            if (CoreState.FETextEncoder == null)
            {
                try
                {
                    CoreState.FETextEncoder = new FETextEncode();
                }
                catch (Exception ex)
                {
                    Log.ErrorF("Failed to init FETextEncode (Huffman tree): {0}", ex.Message);
                }
            }

            // Init text escape
            if (CoreState.TextEscape == null)
                CoreState.TextEscape = new TextEscape();

            // Init flag cache
            if (CoreState.FlagCache == null)
            {
                try
                {
                    CoreState.FlagCache = new EtcCacheFLag();
                }
                catch (Exception ex)
                {
                    Log.ErrorF("Failed to init FlagCache: {0}", ex.Message);
                }
            }

            // Init export function
            if (CoreState.ExportFunction == null)
                CoreState.ExportFunction = new ExportFunction();

            // Init undo
            if (CoreState.Undo == null)
                CoreState.Undo = new Undo();

            // Init event scripts
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
                    CoreState.AIScript = new EventScript();
                    CoreState.AIScript.Load(EventScript.EventScriptType.AI);
                }
            }
            catch (Exception ex)
            {
                Log.ErrorF("Failed to init EventScripts: {0}", ex.Message);
            }

            // #1035: wire the patch-scan hardcode cache (replacing the no-op
            // HeadlessAsmMapCache). Lazy — it only scans config/patch2 on the
            // first IsHardCode* read, so this is cheap to set up here.
            CoreState.AsmMapFileAsmCache = new CoreAsmMapCache(CoreState.ROM);
        }
    }
}
