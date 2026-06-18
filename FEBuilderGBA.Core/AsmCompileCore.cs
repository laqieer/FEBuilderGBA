using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace FEBuilderGBA
{
    /// <summary>
    /// GUI-free compile+insert flow for the "Add-via-ASM/C" tool, shared by the
    /// Avalonia <c>ToolASMInsertView</c>. Ported from the WinForms path
    /// (<c>ToolASMInsertForm.RunButton_Click</c> → <c>MainFormUtil.Compile</c> /
    /// <c>CompilerDevkitPro</c> / <c>CompilerGoldRoad</c> / <c>ConvertLYN</c>), keeping
    /// the exact devkitARM gcc/g++/as arguments, the GoldRoad assemble-then-rename
    /// flow, the ELF → text-section dump, the lyn ELF→EA-event conversion, and the
    /// three insert methods (free-area data, write-at-address, hook-inject).
    ///
    /// Compile chain:
    ///   C/C++/ASM source
    ///     → assembler auto-selected from the source (WF <c>MainFormUtil.Compile</c>):
    ///       a <c>.ASM</c> whose text contains <c>@thumb</c> (and NOT <c>.thumb</c>)
    ///       goes to the legacy GoldRoad assembler
    ///       (<c>CoreState.Config.at("goldroad_asm")</c>) → raw <c>.bin</c>; everything
    ///       else goes to devkitARM gcc/g++/as
    ///       (<c>CoreState.Config.at("devkitpro_eabi")</c>) → ELF
    ///     → (a) raw text-section dump (.dmp) via <see cref="Elf.ProgramBIN"/>, OR
    ///       (b) <c>CONVERT_LYN</c>: as → .o → lyn.exe → .lyn.event (an EA event
    ///          script the caller assembles via <see cref="EventAssemblerCompileCore"/>).
    ///     → insert the raw binary into the ROM (free-area / address / hook-inject).
    ///
    /// Scope boundary (intentional — matches how <see cref="EventAssemblerCompileCore"/>
    /// deferred MakeEAAutoDef):
    ///   PROVIDED: .c/.cpp/.s/.asm → ELF → .dmp / .lyn.event, the GoldRoad
    ///     <c>@thumb</c> path, the three insert methods, missing-label check,
    ///     debug-symbol store, undo.
    ///   INTENTIONALLY DEFERRED to a follow-up (WinForms-coupled):
    ///     - the "Make Patch" text generator (GraphicsToolPatchMakerForm) — pure WF
    ///       patch-text UI, unrelated to the core compile/insert.
    /// </summary>
    public static class AsmCompileCore
    {
        /// <summary>
        /// What to do with the compiled ELF (mirrors WinForms
        /// <c>MainFormUtil.CompileType</c> + the ELF combo choices).
        /// </summary>
        public enum CompileMethod
        {
            /// <summary>Assemble to ELF, dump the raw text section to a .dmp binary,
            /// then delete the intermediate ELF (WF <c>CompileType.NONE</c>).</summary>
            DumpBinary = 0,
            /// <summary>As <see cref="DumpBinary"/> but keep the .elf on disk
            /// (WF <c>CompileType.KEEP_ELF</c>).</summary>
            KeepElf = 1,
            /// <summary>Convert the ELF to an EA event script via lyn.exe
            /// (WF <c>CompileType.CONVERT_LYN</c>) — produces a .lyn.event, not a
            /// raw binary, so it cannot be written directly to the ROM here.</summary>
            ConvertLyn = 2,
        }

        /// <summary>
        /// Where the compiled binary lands in the ROM (mirrors WinForms
        /// <c>ToolASMInsertForm.Method</c> combo).
        /// </summary>
        public enum InsertMethod
        {
            /// <summary>Do not write to the ROM; just compile and report the product
            /// (WF Method index 0 — "make file only").</summary>
            CompileOnly = 0,
            /// <summary>Write the binary verbatim at the chosen address
            /// (WF Method index 1).</summary>
            WriteAtAddress = 1,
            /// <summary>Write the binary into the free area and patch a thumb jump at
            /// the chosen hook address (WF Method index 2).</summary>
            HookInject = 2,
        }

        /// <summary>Structured result of a compile (+ optional insert) run.</summary>
        public sealed class CompileResult
        {
            /// <summary>True when the source compiled and (if requested) was inserted.</summary>
            public bool Success { get; set; }
            /// <summary>Raw tool stdout/stderr (for display / logging).</summary>
            public string Output { get; set; } = "";
            /// <summary>The compiled product path (.dmp binary, or .lyn.event for ConvertLyn).</summary>
            public string ProductPath { get; set; } = "";
            /// <summary>The ROM offset the binary was written to, or <see cref="U.NOT_FOUND"/>
            /// when nothing was inserted (CompileOnly / ConvertLyn).</summary>
            public uint InsertedAddr { get; set; } = U.NOT_FOUND;
            /// <summary>Number of bytes written to the ROM (0 when nothing was inserted).</summary>
            public int InsertedSize { get; set; }
            /// <summary>EA symbol text produced from the ELF (may be empty).</summary>
            public string SymbolText { get; set; } = "";
            /// <summary>Localized human-readable error (set when <see cref="Success"/> is false).</summary>
            public string ErrorMessage { get; set; } = "";
        }

        // ---- Tool resolution --------------------------------------------------

        /// <summary>The devkitARM EABI marker path from config (the as/gcc tree root
        /// is its directory). Empty when unset.</summary>
        public static string GetDevkitProEabi() => CoreState.Config?.at("devkitpro_eabi", "") ?? "";

        /// <summary>The C/C++ compile flags (WF <c>OptionForm.GetCFLAGS</c> default).</summary>
        public static string GetCFlags() => CoreState.Config?.at("CFLAGS", "-c -mthumb -O2") ?? "-c -mthumb -O2";

        /// <summary>The FEClib path (WF <c>OptionForm.GetFECLIB</c>). Empty when unset.</summary>
        public static string GetFEClib() => CoreState.Config?.at("FECLIB", "") ?? "";

        /// <summary>
        /// True when the devkitARM EABI tool path is configured and exists. Callers
        /// should surface <see cref="GetNotFoundMessage"/> when this is false.
        /// </summary>
        public static bool IsDevkitProAvailable()
        {
            string eabi = GetDevkitProEabi();
            return !string.IsNullOrEmpty(eabi) && File.Exists(eabi);
        }

        /// <summary>Localized "devkitpro_eabi not configured" message (no throw).</summary>
        public static string GetNotFoundMessage()
        {
            // Mirror the WinForms CompilerDevkitPro message.
            return R._("{0}の設定がありません。 設定->オプションから、{0}を設定してください。", "devkitpro_eabi");
        }

        /// <summary>
        /// Resolve the compiler executable in the devkitARM tree for the given source
        /// extension. <c>.C</c> → gcc, <c>.CPP</c> → g++, otherwise the assembler (as).
        /// Mirrors WinForms <c>CompilerDevkitPro</c> + <c>U.FindFileOne</c>, but is
        /// cross-platform: on Linux/macOS the binaries have NO <c>.exe</c>, so it tries
        /// the platform-appropriate globs. Returns null when the tree or the specific
        /// compiler is missing.
        /// </summary>
        public static string ResolveCompiler(string toolDir, string sourceExtUpper)
        {
            if (string.IsNullOrEmpty(toolDir) || !Directory.Exists(toolDir))
                return null;
            string baseName = CompilerBaseNameForExt(sourceExtUpper);
            foreach (string glob in ToolPathResolver.DevkitArmGlobs(baseName))
            {
                string hit = FindFileOne(toolDir, glob);
                if (hit != null) return hit;
            }
            return null;
        }

        /// <summary>The gcc/g++/as base tool name WinForms picks for a (dotted,
        /// upper-case) ext.</summary>
        public static string CompilerBaseNameForExt(string sourceExtUpper)
        {
            if (sourceExtUpper == ".C") return "gcc";
            if (sourceExtUpper == ".CPP") return "g++";
            return "as";
        }

        /// <summary>
        /// The compiler glob WinForms picks for a (dotted, upper-case) ext, on THIS
        /// platform (Windows → <c>*gcc.exe</c>; Linux/macOS → <c>*gcc</c>). The first
        /// of <see cref="ToolPathResolver.DevkitArmGlobs"/> for the mapped base name.
        /// </summary>
        public static string CompilerGlobForExt(string sourceExtUpper) =>
            ToolPathResolver.DevkitArmGlobs(CompilerBaseNameForExt(sourceExtUpper))[0];

        /// <summary>
        /// Find the first file under <paramref name="dir"/> matching the glob,
        /// recursively. Mirrors WinForms <c>U.FindFileOne</c>. Returns null (not "")
        /// when nothing matches, so callers can null-check.
        /// </summary>
        public static string FindFileOne(string dir, string pattern)
        {
            string[] files = U.Directory_GetFiles_Safe(dir, pattern, SearchOption.AllDirectories);
            return files.Length > 0 ? files[0] : null;
        }

        /// <summary>Resolve lyn.exe via the Event Assembler tree (shared resolver).</summary>
        public static string ResolveLyn()
        {
            string ea = ToolPathResolver.ResolveEventAssembler() ?? "";
            return ToolPathResolver.ResolveLynExe(ea);
        }

        // ---- GoldRoad (legacy @thumb assembler) -------------------------------

        /// <summary>Resolve the legacy GoldRoad assembler from <c>goldroad_asm</c>, or
        /// null. Delegates to <see cref="ToolPathResolver.ResolveGoldRoad"/>.</summary>
        public static string ResolveGoldRoad() => ToolPathResolver.ResolveGoldRoad();

        /// <summary>
        /// True when the GoldRoad assembler path is configured and exists. Callers
        /// should surface <see cref="GetGoldRoadNotFoundMessage"/> when this is false
        /// and the source is a GoldRoad (<c>@thumb</c>) source.
        /// </summary>
        public static bool IsGoldRoadAvailable() => !string.IsNullOrEmpty(ResolveGoldRoad());

        /// <summary>Localized "goldroad not configured" message (no throw). Reuses the
        /// same parameterized key as the devkit message, with the tool name "goldroad"
        /// (mirrors WF <c>CompilerGoldRoad</c>).</summary>
        public static string GetGoldRoadNotFoundMessage() =>
            R._("{0}の設定がありません。 設定->オプションから、{0}を設定してください。", "goldroad");

        /// <summary>
        /// Decide whether a source must be assembled with GoldRoad instead of devkitARM
        /// — the pure source-content auto-switch from WF <c>MainFormUtil.Compile</c>: a
        /// <c>.ASM</c> source whose (case-insensitive) text contains <c>@thumb</c> and
        /// does NOT contain <c>.thumb</c> routes to GoldRoad; everything else
        /// (.thumb ASM, .s/.c/.cpp, or any non-.ASM ext) routes to devkitARM. Pure: it
        /// only inspects the extension and the file text, mutating nothing.
        /// </summary>
        public static bool ShouldUseGoldRoad(string sourcePath)
        {
            if (string.IsNullOrEmpty(sourcePath)) return false;
            // WF keys the auto-switch on the .ASM extension only (a .S is always devkit).
            if (U.GetFilenameExt(sourcePath) != ".ASM") return false;

            string text;
            try { text = File.ReadAllText(sourcePath); }
            catch { return false; } // unreadable → let the devkit path report the error
            return ShouldUseGoldRoadFromText(text);
        }

        /// <summary>
        /// The pure text predicate behind <see cref="ShouldUseGoldRoad"/> (exposed for
        /// unit-testing without a file): mirrors WF <c>MainFormUtil.Compile</c> — a
        /// <c>.thumb</c> marker forces devkitARM (gnu-as) even when <c>@thumb</c> is
        /// also present; only a bare <c>@thumb</c> (no <c>.thumb</c>) selects GoldRoad.
        /// </summary>
        public static bool ShouldUseGoldRoadFromText(string sourceText)
        {
            if (string.IsNullOrEmpty(sourceText)) return false;
            string lower = sourceText.ToLowerInvariant();
            if (lower.IndexOf(".thumb", StringComparison.Ordinal) >= 0) return false;
            return lower.IndexOf("@thumb", StringComparison.Ordinal) >= 0;
        }

        // ---- Argument building (pure — unit-tested without running anything) ---

        /// <summary>
        /// Build the gcc/g++/as arguments for the ELF assemble step (WF
        /// <c>CompilerDevkitPro</c>). Appends CFLAGS for .C/.CPP and a <c>-I</c> for
        /// the FEClib directory when one exists. Pure: takes its inputs explicitly.
        /// </summary>
        public static string BuildAssembleArgs(string sourcePath, string elfOutPath,
            string sourceExtUpper, string cflags, string feclibPath)
        {
            var sb = new StringBuilder();
            sb.Append("-g -mcpu=arm7tdmi -mthumb-interwork ");
            sb.Append(' ').Append(U.escape_shell_args(sourcePath));
            sb.Append(" -o ").Append(U.escape_shell_args(elfOutPath));

            if (sourceExtUpper == ".C" || sourceExtUpper == ".CPP")
            {
                if (!string.IsNullOrEmpty(cflags))
                    sb.Append(' ').Append(cflags);
            }
            if (!string.IsNullOrEmpty(feclibPath) && File.Exists(feclibPath))
            {
                string feclibDir = Path.GetDirectoryName(feclibPath);
                sb.Append(" -I ").Append(U.escape_shell_args(feclibDir));
            }
            return sb.ToString();
        }

        /// <summary>
        /// Build the <c>as</c> arguments for the lyn .o step (WF
        /// <c>ConvertLYN_S_to_O</c>). Appends the FEClib reference .s when present.
        /// </summary>
        public static string BuildLynObjectArgs(string sourcePath, string objOutPath, string feclibReference)
        {
            var sb = new StringBuilder();
            sb.Append("-g -mcpu=arm7tdmi -mthumb-interwork ");
            sb.Append(' ').Append(U.escape_shell_args(sourcePath));
            if (!string.IsNullOrEmpty(feclibReference) && File.Exists(feclibReference))
                sb.Append(' ').Append(U.escape_shell_args(feclibReference));
            sb.Append(" -o ").Append(U.escape_shell_args(objOutPath));
            return sb.ToString();
        }

        // ---- Compile-only (no ROM mutation) -----------------------------------

        /// <summary>
        /// Compile <paramref name="sourcePath"/> to a product (a .dmp raw binary, or a
        /// .lyn.event for <see cref="CompileMethod.ConvertLyn"/>) WITHOUT touching the
        /// ROM. Never throws — file/process failures return a structured error.
        /// On success, <see cref="CompileResult.ProductPath"/> is the product file and
        /// <see cref="CompileResult.SymbolText"/> the EA symbols extracted from the ELF.
        /// </summary>
        public static CompileResult Compile(string sourcePath, CompileMethod method,
            bool checkMissingLabel, Action<string> onProgress = null)
        {
            var result = new CompileResult();

            if (string.IsNullOrEmpty(sourcePath) || !File.Exists(sourcePath))
            {
                result.ErrorMessage = R._("ファイルがありません。\r\nファイル名:{0}", sourcePath ?? "");
                return result;
            }

            // Source-content auto-switch (WF MainFormUtil.Compile): a .ASM with @thumb
            // (and no .thumb) is assembled by the legacy GoldRoad assembler, not the
            // devkitARM chain. GoldRoad always produces a raw .bin (there is no ELF, so
            // the ConvertLyn/KeepElf methods do not apply — it always behaves like
            // DumpBinary, matching WF which ignores compileType for the GoldRoad path).
            if (ShouldUseGoldRoad(sourcePath))
                return CompileGoldRoad(sourcePath, onProgress);

            if (!IsDevkitProAvailable())
            {
                result.ErrorMessage = GetNotFoundMessage();
                return result;
            }

            string eabi = GetDevkitProEabi();
            string toolDir = Path.GetDirectoryName(eabi);
            string ext = U.GetFilenameExt(sourcePath); // dotted, upper-case (e.g. ".C")
            string srcFull = Path.GetFullPath(sourcePath);
            string srcDir = Path.GetDirectoryName(srcFull);
            string baseNoExt = Path.Combine(srcDir, Path.GetFileNameWithoutExtension(srcFull));

            try
            {
                if (method == CompileMethod.ConvertLyn)
                    return ConvertLyn(srcFull, toolDir, baseNoExt, onProgress);

                string compilerExe = ResolveCompiler(toolDir, ext);
                if (string.IsNullOrEmpty(compilerExe))
                {
                    // Name the platform-neutral base tool (gcc/g++/as) — the actual
                    // binary carries .exe only on Windows, so a hard-coded "*as.exe"
                    // would misname the searched-for tool on Linux/macOS.
                    result.ErrorMessage = R._("{0}の設定がありません。 設定->オプションから、{0}を設定してください。",
                        "devkitpro_eabi " + CompilerBaseNameForExt(ext));
                    return result;
                }

                string elfPath = baseNoExt + ".elf";
                string args = BuildAssembleArgs(srcFull, elfPath, ext, GetCFlags(), GetFEClib());

                string output;
                try
                {
                    output = RunProcess(compilerExe, args, srcDir);
                }
                catch (Exception ex)
                {
                    result.ErrorMessage = R._("プロセスを実行できません。\r\nfilename:{0}\r\n{1}", compilerExe, ex.ToString());
                    return result;
                }

                if (!File.Exists(elfPath) || U.GetFileSize(elfPath) <= 0)
                {
                    // Prefix the executed command so the user can repro (matches WF).
                    result.Output = output;
                    result.ErrorMessage = compilerExe + " " + args + " \r\noutput:\r\n" + output;
                    return result;
                }

                var elf = new Elf(elfPath, useHookMode: false);
                if (checkMissingLabel)
                {
                    string lost = elf.CheckLostLabel();
                    if (!string.IsNullOrEmpty(lost))
                    {
                        result.ErrorMessage = lost;
                        TryDelete(elfPath);
                        return result;
                    }
                }
                result.SymbolText = elf.ToEASymbol();

                // Dump the raw text-section binary.
                string dmpPath = baseNoExt + ".dmp";
                U.WriteAllBytes(dmpPath, elf.ProgramBIN);

                if (method != CompileMethod.KeepElf)
                    TryDelete(elfPath);

                result.Output = output;
                result.ProductPath = dmpPath;
                result.Success = true;
                return result;
            }
            catch (Exception ioex) when (ioex is IOException || ioex is UnauthorizedAccessException)
            {
                result.Success = false;
                result.ErrorMessage = R._("Unable to write the temporary files needed for compilation.")
                    + "\r\n" + ioex.ToString();
                return result;
            }
        }

        /// <summary>
        /// CONVERT_LYN path: as → .o → lyn.exe → .lyn.event (WF
        /// <c>ConvertLYN</c>). The product is an EA event script (not a raw binary),
        /// so it cannot be written directly to the ROM by this helper.
        /// </summary>
        static CompileResult ConvertLyn(string srcFull, string toolDir, string baseNoExt, Action<string> onProgress)
        {
            var result = new CompileResult();

            string asExe = ResolveCompiler(toolDir, ".S"); // *as / *as.exe (platform-aware)
            if (string.IsNullOrEmpty(asExe))
            {
                // Name the platform-neutral base tool ("as") — not "*as.exe", which
                // would misname the searched-for binary on Linux/macOS.
                result.ErrorMessage = R._("{0}の設定がありません。 設定->オプションから、{0}を設定してください。",
                    "devkitpro_eabi " + CompilerBaseNameForExt(".S"));
                return result;
            }

            string lynExe = ResolveLyn();
            if (string.IsNullOrEmpty(lynExe) || !File.Exists(lynExe))
            {
                result.ErrorMessage = R._("lyn.exeが見つかりません。\r\n{0}", lynExe ?? "");
                return result;
            }

            string objPath = baseNoExt + ".o";
            string srcDir = Path.GetDirectoryName(srcFull);
            string feclibRef = GetFEClibReference();
            string asArgs = BuildLynObjectArgs(srcFull, objPath, feclibRef);

            string asOut;
            try
            {
                asOut = RunProcess(asExe, asArgs, srcDir);
            }
            catch (Exception ex)
            {
                result.ErrorMessage = R._("プロセスを実行できません。\r\nfilename:{0}\r\n{1}", asExe, ex.ToString());
                return result;
            }
            if (!File.Exists(objPath) || U.GetFileSize(objPath) <= 0)
            {
                result.Output = asOut;
                result.ErrorMessage = asExe + " " + asArgs + " \r\noutput:\r\n" + asOut;
                return result;
            }

            // Extract EA symbols from the object before lyn consumes it.
            try { result.SymbolText = new Elf(objPath, useHookMode: false).ToEASymbol(); }
            catch { /* symbol extraction is best-effort */ }

            string lynArgs = U.escape_shell_args(objPath);
            string lynOut;
            try
            {
                lynOut = RunProcess(lynExe, lynArgs, srcDir);
            }
            catch (Exception ex)
            {
                TryDelete(objPath);
                result.ErrorMessage = R._("プロセスを実行できません。\r\nfilename:{0}\r\n{1}", lynExe, ex.ToString());
                return result;
            }
            TryDelete(objPath);

            // lyn emits an EA event script; "ALIGN 4" is WF's success marker.
            if (lynOut.IndexOf("ALIGN 4", StringComparison.Ordinal) < 0)
            {
                result.Output = lynOut;
                result.ErrorMessage = lynExe + " " + lynArgs + " \r\noutput:\r\n" + lynOut;
                return result;
            }

            string eventPath = U.ChangeExtFilename(srcFull, ".lyn.event");
            File.WriteAllText(eventPath, lynOut);

            result.Output = lynOut;
            result.ProductPath = eventPath;
            result.Success = true;
            return result;
        }

        /// <summary>
        /// Find the FEClib reference .s for the current ROM version (WF
        /// <c>MainFormUtil.GetFEClibReference</c>): <c>&lt;feclib&gt;/../reference/&lt;version&gt;*.s</c>,
        /// newest first. Empty when no FEClib / no match.
        /// </summary>
        public static string GetFEClibReference()
        {
            string feclib = GetFEClib();
            if (string.IsNullOrEmpty(feclib)) return "";

            string dir;
            try
            {
                dir = Path.GetDirectoryName(feclib);
                dir = Path.Combine(dir, "../reference/");
            }
            catch (Exception) { return ""; }

            if (!Directory.Exists(dir)) return "";

            string version = CoreState.ROM?.RomInfo?.VersionToFilename;
            if (string.IsNullOrEmpty(version)) return "";

            string[] list = U.Directory_GetFiles_Safe(dir, version + "*.s", SearchOption.TopDirectoryOnly);
            if (list.Length <= 0) return "";

            Array.Sort(list);
            Array.Reverse(list);
            return list[0];
        }

        // ---- GoldRoad assemble (@thumb legacy path) ---------------------------

        /// <summary>
        /// Build the GoldRoad command-line arguments (WF <c>CompilerGoldRoad</c>):
        /// <c>&lt;source&gt; &lt;output.gba&gt;</c>. The OUTPUT is passed as a BARE filename
        /// (no directory) because GoldRoad is buggy and crashes on a complex output
        /// path — WF works around this by running with the working directory set to the
        /// GoldRoad tool dir so the bare-named product lands there. The source may be a
        /// full path (GoldRoad tolerates that for the input). Pure: takes its inputs
        /// explicitly so it is unit-testable without running anything.
        /// </summary>
        public static string BuildGoldRoadArgs(string sourceFileName, string outputGbaFileName)
        {
            return U.escape_shell_args(sourceFileName)
                + " " + U.escape_shell_args(outputGbaFileName);
        }

        /// <summary>
        /// Assemble a <c>@thumb</c> <c>.ASM</c> source with the legacy GoldRoad
        /// assembler (WF <c>MainFormUtil.CompilerGoldRoad</c>). GoldRoad has no ELF
        /// stage: it emits a raw <c>.gba</c> binary, which we rename to
        /// <c>&lt;source&gt;.bin</c> (the WF product) and feed into the SAME insert
        /// methods as the devkit path. Never throws — a missing tool / process / file
        /// failure returns a structured, localized error with ZERO ROM mutation.
        ///
        /// Faithful WF quirks: GoldRoad is invoked with bare filenames and its working
        /// directory set to the tool dir (it crashes on complex output paths), so the
        /// product first lands as <c>&lt;tooldir&gt;/&lt;name&gt;.gba</c> and is then moved
        /// next to the source as <c>&lt;name&gt;.bin</c> (a <c>.gba</c> extension is
        /// avoided as "scary"). There are no EA symbols (no ELF), so
        /// <see cref="CompileResult.SymbolText"/> stays empty.
        /// </summary>
        static CompileResult CompileGoldRoad(string sourcePath, Action<string> onProgress)
        {
            var result = new CompileResult();

            string goldroad = ResolveGoldRoad();
            if (string.IsNullOrEmpty(goldroad))
            {
                result.ErrorMessage = GetGoldRoadNotFoundMessage();
                return result;
            }

            string srcFull;
            string toolDir;
            try
            {
                srcFull = Path.GetFullPath(sourcePath);
                toolDir = Path.GetDirectoryName(Path.GetFullPath(goldroad));
            }
            catch (Exception ex) when (ex is ArgumentException || ex is PathTooLongException
                || ex is NotSupportedException || ex is System.Security.SecurityException)
            {
                result.ErrorMessage = R._("プロセスを実行できません。\r\nfilename:{0}\r\n{1}", goldroad, ex.ToString());
                return result;
            }
            if (string.IsNullOrEmpty(toolDir) || !Directory.Exists(toolDir))
            {
                result.ErrorMessage = GetGoldRoadNotFoundMessage();
                return result;
            }

            string srcDir = Path.GetDirectoryName(srcFull);
            string nameNoExt = Path.GetFileNameWithoutExtension(srcFull);

            // GoldRoad bug workaround: pass the OUTPUT as a bare ".gba" filename with the
            // working directory set to the tool dir, so GoldRoad writes the product into
            // its own directory. (WF passes the full source path as the first arg;
            // GoldRoad tolerates that, but the OUTPUT must be a bare filename or it
            // crashes.)
            string outGbaName = nameNoExt + ".gba";
            string args = BuildGoldRoadArgs(srcFull, outGbaName);
            string producedGba = Path.Combine(toolDir, outGbaName);
            string binPath = Path.Combine(srcDir, nameNoExt + ".bin");

            // Delete any STALE product from a previous run BEFORE invoking GoldRoad: the
            // success check below keys off the presence/size of <toolDir>/<name>.gba, so
            // a leftover .gba would make a FAILED GoldRoad run look successful and feed
            // garbage into the ROM. Clearing it (and the .bin target) means only a
            // freshly-produced file can count as success.
            TryDelete(producedGba);
            TryDelete(binPath);

            onProgress?.Invoke(goldroad);

            string output;
            try
            {
                output = RunProcess(goldroad, args, toolDir);
            }
            catch (Exception ex)
            {
                result.ErrorMessage = R._("プロセスを実行できません。\r\nfilename:{0}\r\n{1}", goldroad, ex.ToString());
                return result;
            }

            if (!File.Exists(producedGba) || U.GetFileSize(producedGba) <= 0)
            {
                // Prefix the executed command so the user can repro (matches WF).
                result.Output = output;
                result.ErrorMessage = goldroad + " " + args + " \r\noutput:\r\n" + output;
                return result;
            }

            // Move the freshly-produced .gba next to the source as <name>.bin (WF renames
            // the scary .gba to .bin). File.Move throws across volumes (the toolchain on
            // D: and the project on C: is common), so fall back to copy+delete in that
            // case — keeping the never-throws contract (a failure → clean localized error,
            // ZERO ROM mutation).
            if (!TryMoveCrossVolume(producedGba, binPath, out string moveError))
            {
                result.Output = output;
                result.ErrorMessage = R._("Unable to write the temporary files needed for compilation.")
                    + "\r\n" + moveError;
                return result;
            }

            result.Output = output;
            result.ProductPath = binPath;
            result.SymbolText = ""; // no ELF → no EA symbols
            result.Success = true;
            return result;
        }

        // ---- Compile + insert (the full Run flow) -----------------------------

        /// <summary>
        /// Compile <paramref name="sourcePath"/> and insert the result into
        /// <paramref name="rom"/> per <paramref name="insert"/>, fault-safe in one step.
        ///
        /// Behaviour:
        ///  - devkitARM not configured → localized <see cref="GetNotFoundMessage"/>,
        ///    NO throw, NO ROM mutation.
        ///  - The ROM is mutated ONLY after a clean compile (no partial insert on
        ///    failure). All writes go into <paramref name="undo"/> so they are undoable.
        ///  - <see cref="CompileMethod.ConvertLyn"/> produces an EA event (not a raw
        ///    binary), so it is only valid with <see cref="InsertMethod.CompileOnly"/>;
        ///    any other insert method returns a localized error (matches the WinForms
        ///    "can't write a lyn.event to the ROM" guard).
        ///  - Debug symbols are stored via <see cref="SymbolUtil.ProcessSymbolByComment"/>.
        /// </summary>
        public static CompileResult CompileAndInsert(ROM rom, string sourcePath,
            CompileMethod method, InsertMethod insert, uint targetAddr, uint freeArea,
            uint hookRegister, SymbolUtil.DebugSymbol storeSymbol, bool checkMissingLabel,
            Undo.UndoData undo, Action<string> onProgress = null)
        {
            if (rom == null)
                return new CompileResult { ErrorMessage = R._("No ROM is loaded.") };

            // A GoldRoad (@thumb .ASM) source ALWAYS produces a raw writable .bin — there
            // is no ELF/lyn stage, so the selected CompileMethod (DumpBinary/KeepElf/
            // ConvertLyn) does not apply to it (WF MainFormUtil.Compile routes to
            // CompilerGoldRoad ignoring compileType). Detect it up front so the lyn-guard
            // below does NOT reject a GoldRoad source paired with the ConvertLyn method.
            bool isGoldRoad = ShouldUseGoldRoad(sourcePath);

            // A lyn.event is a script, not bytes — it cannot be written to the ROM here.
            // (GoldRoad never makes a lyn.event, so the guard skips it.)
            if (!isGoldRoad && method == CompileMethod.ConvertLyn && insert != InsertMethod.CompileOnly)
                return new CompileResult
                {
                    ErrorMessage = R._("lyn.eventを作成する時は、ROMに書き込むことはできません。")
                };

            CompileResult result = Compile(sourcePath, method, checkMissingLabel, onProgress);
            if (!result.Success)
                return result;

            // Store debug symbols for the compile-only / lyn product (addr 0). A GoldRoad
            // source produces a raw .bin even under the ConvertLyn method, so it is NOT
            // treated as a non-writable lyn product here.
            if (insert == InsertMethod.CompileOnly || (!isGoldRoad && method == CompileMethod.ConvertLyn))
            {
                SymbolUtil.ProcessSymbolByComment(Path.GetFullPath(sourcePath), result.SymbolText, storeSymbol, 0);
                return result;
            }

            byte[] bin;
            try
            {
                bin = File.ReadAllBytes(result.ProductPath);
            }
            catch (Exception ex) when (ex is FileNotFoundException || ex is DirectoryNotFoundException)
            {
                // The compiled product vanished (deleted/moved between compile and read).
                result.Success = false;
                result.ErrorMessage = R._("ファイルがありません。\r\nファイル名:{0}", result.ProductPath);
                return result;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                // A permission/lock/IO problem reading the product (it exists but can't
                // be read) — a DISTINCT failure from "file missing".
                result.Success = false;
                result.ErrorMessage = R._("Unable to read the compiled file.\r\nfilename:{0}", result.ProductPath)
                    + "\r\n" + ex.ToString();
                return result;
            }
            if (bin.Length <= 0)
            {
                result.Success = false;
                result.ErrorMessage = R._("ファイルがゼロバイトです。\r\nファイル名:{0}", result.ProductPath);
                return result;
            }

            // Validate the destination address(es) BEFORE mutating (the addresses come
            // from free-form UI fields). targetAddr is the write/hook site; freeArea is
            // the routine body for hook-inject.
            string addrError = ValidateInsertAddresses(rom, insert, targetAddr, freeArea, (uint)bin.Length);
            if (addrError != null)
            {
                result.Success = false;
                result.ErrorMessage = addrError;
                return result;
            }

            // Fault-safe insert: snapshot the ROM, attempt the write, and on ANY failure
            // (resize limit, out-of-bounds, unexpected throw) restore the snapshot so the
            // ROM is left byte-identical with NO partial mutation. The insert helpers
            // return false (no throw) for the expected resize/bounds failures.
            byte[] snapshot = (byte[])rom.Data.Clone();
            int undoCountBefore = undo.list.Count;
            bool inserted;
            try
            {
                inserted = insert == InsertMethod.WriteAtAddress
                    ? InsertAtAddress(rom, targetAddr, bin, undo)
                    : InsertHookInject(rom, targetAddr, freeArea, hookRegister, bin, undo);
            }
            catch (Exception ex)
            {
                RollbackPartialInsert(rom, snapshot, undo, undoCountBefore);
                result.Success = false;
                result.ErrorMessage = R._("Failed to apply the compiled ROM (size/resize limit exceeded?).")
                    + "\r\n" + ex.ToString();
                return result;
            }
            if (!inserted)
            {
                RollbackPartialInsert(rom, snapshot, undo, undoCountBefore);
                result.Success = false;
                result.ErrorMessage = R._("Failed to apply the compiled ROM (size/resize limit exceeded?).");
                return result;
            }

            // For write-at-address the bytes land AT the target; for hook-inject the
            // routine body lands in the free area (the hook site only gets the jump).
            uint symbolAddr = insert == InsertMethod.WriteAtAddress ? targetAddr : freeArea;
            CoreState.CommentCache?.RemoveRange(symbolAddr, symbolAddr + (uint)bin.Length);
            SymbolUtil.ProcessSymbolByComment(Path.GetFullPath(sourcePath), result.SymbolText, storeSymbol, symbolAddr);

            result.InsertedAddr = symbolAddr;
            result.InsertedSize = bin.Length;
            result.Success = true;
            return result;
        }

        /// <summary>
        /// Validate the destination address(es) for an insert. The write/hook site and
        /// (for hook-inject) the free area must be non-zero and either a safe ROM offset
        /// or exactly the ROM end (append). Mirrors the WF
        /// <c>CheckZeroAddressWrite</c> + <c>isSafetyOffset</c>/append guard. Returns a
        /// localized error string when invalid, or null when OK.
        /// </summary>
        public static string ValidateInsertAddresses(ROM rom, InsertMethod insert,
            uint targetAddr, uint freeArea, uint binLength)
        {
            if (!IsValidWriteAddress(rom, targetAddr))
                return R._("無効なポインタです。\r\nこの設定は危険です。") + "\r\n" + U.To0xHexString(targetAddr);

            if (insert == InsertMethod.HookInject)
            {
                if (!IsValidWriteAddress(rom, freeArea))
                    return R._("無効なポインタです。\r\nこの設定は危険です。") + "\r\n" + U.To0xHexString(freeArea);
            }
            return null;
        }

        /// <summary>True when <paramref name="addr"/> is a safe write target: non-zero
        /// and either a safe ROM offset or exactly the ROM end (append).</summary>
        static bool IsValidWriteAddress(ROM rom, uint addr)
        {
            if (addr == 0) return false;                      // CheckZeroAddressWrite
            if (U.isSafetyOffset(addr, rom)) return true;     // inside the ROM
            if (addr == (uint)rom.Data.Length) return true;   // append at the very end
            return false;
        }

        /// <summary>
        /// Restore the ROM to <paramref name="snapshot"/> and trim any undo entries the
        /// failed insert appended, so a mid-insert failure leaves NO partial mutation.
        /// </summary>
        static void RollbackPartialInsert(ROM rom, byte[] snapshot, Undo.UndoData undo, int undoCountBefore)
        {
            rom.SwapNewROMDataDirect(snapshot);
            if (undo.list.Count > undoCountBefore)
                undo.list.RemoveRange(undoCountBefore, undo.list.Count - undoCountBefore);
        }

        /// <summary>
        /// Write the binary verbatim at <paramref name="addr"/> (WF Method 1),
        /// resizing the ROM if it runs past the end. The write is recorded into
        /// <paramref name="undo"/> so it is fully undoable. Returns false (no write)
        /// when the required resize is rejected (e.g. &gt; 32 MB) — never throws for
        /// that expected case. Exposed for unit-testing with a synthetic binary.
        /// </summary>
        public static bool InsertAtAddress(ROM rom, uint addr, byte[] bin, Undo.UndoData undo)
        {
            uint newLength = addr + (uint)bin.Length;
            if (newLength > (uint)rom.Data.Length)
            {
                // CHECK the resize result — write_resize_data returns false at the 32 MB
                // limit; writing afterwards would otherwise throw out of range.
                if (!rom.write_resize_data(newLength))
                    return false;
            }
            rom.write_range(addr, bin, undo);
            return true;
        }

        /// <summary>
        /// Write the binary into the free area and patch a thumb jump at the hook
        /// address (WF Method 2). The hook and the routine body are both recorded into
        /// <paramref name="undo"/>. Returns false (no partial write) when a required
        /// resize is rejected. Exposed for unit-testing with a synthetic binary.
        /// </summary>
        public static bool InsertHookInject(ROM rom, uint hookAddr, uint freeArea, uint hookRegister,
            byte[] bin, Undo.UndoData undo)
        {
            uint useReg = ClampHookRegister(hookRegister);
            byte[] jumpCode = DisassemblerTrumb.MakeInjectJump(hookAddr, freeArea, useReg);

            // Resize ONCE to cover BOTH the routine body AND the hook-site jump-code —
            // the hook can be near the ROM end too (#6: a body-only resize would let the
            // jump-code write run out of bounds). Resize FIRST so the free area is in
            // bounds before any undo snapshot (UndoPostion clamps at construction).
            uint bodyEnd = freeArea + (uint)bin.Length;
            uint hookEnd = hookAddr + (uint)jumpCode.Length;
            uint needLength = Math.Max(bodyEnd, hookEnd);
            if (needLength > (uint)rom.Data.Length)
            {
                if (!rom.write_resize_data(needLength))
                    return false; // resize rejected (e.g. > 32 MB) — no partial write
            }

            // Both writes go through the EXPLICIT-undodata overload — never the
            // no-undodata overload, whose [ThreadStatic] ambient scope is null on this
            // background Task.Run thread (so it would record nothing) and could absorb
            // an unrelated UI-thread write.
            rom.write_range(freeArea, bin, undo);
            rom.write_range(hookAddr, jumpCode, undo);
            return true;
        }

        /// <summary>Clamp the hook register to the valid r0..r8 range (WF GetSaftyRegister).</summary>
        public static uint ClampHookRegister(uint reg)
        {
            if (reg > 8) return 8;
            return reg;
        }

        // ---- Process runner (mirrors EventAssemblerCompileCore.RunProcess) ----

        static void TryDelete(string path)
        {
            try { if (!string.IsNullOrEmpty(path) && File.Exists(path)) File.Delete(path); }
            catch { /* best-effort cleanup */ }
        }

        /// <summary>
        /// Move <paramref name="src"/> to <paramref name="dest"/>, overwriting any
        /// existing destination, in a cross-volume-safe way. <see cref="File.Move(string,string)"/>
        /// throws an <see cref="IOException"/> when source and destination are on
        /// DIFFERENT volumes (e.g. the toolchain on D: and the project on C:), so we fall
        /// back to a copy + delete in that case. Returns false (never throws) with the
        /// failure detail in <paramref name="error"/> so the caller can surface a clean,
        /// localized error with ZERO ROM mutation.
        /// </summary>
        static bool TryMoveCrossVolume(string src, string dest, out string error)
        {
            error = "";
            try
            {
                TryDelete(dest); // File.Move won't overwrite — clear the target first
                File.Move(src, dest);
                return true;
            }
            catch (IOException)
            {
                // Most commonly a cross-volume move — retry as copy + delete.
                try
                {
                    File.Copy(src, dest, overwrite: true);
                    TryDelete(src);
                    return true;
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    error = ex.ToString();
                    return false;
                }
            }
            catch (UnauthorizedAccessException ex)
            {
                error = ex.ToString();
                return false;
            }
        }

        /// <summary>
        /// Run a tool and capture combined stdout+stderr (120s timeout). Shared shape
        /// with <see cref="EventAssemblerCompileCore"/>'s runner.
        /// </summary>
        static string RunProcess(string exePath, string args, string workDir)
        {
            var psi = new ProcessStartInfo(exePath, args)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = workDir,
            };

            var sb = new StringBuilder();
            using (var proc = Process.Start(psi))
            {
                proc.OutputDataReceived += (_, e) => { if (e.Data != null) sb.AppendLine(e.Data); };
                proc.ErrorDataReceived += (_, e) => { if (e.Data != null) sb.AppendLine(e.Data); };
                proc.BeginOutputReadLine();
                proc.BeginErrorReadLine();

                if (!proc.WaitForExit(120_000))
                {
                    try { proc.Kill(); } catch { }
                    return "Error: the compiler timed out after 120 seconds.";
                }
            }
            return sb.ToString();
        }
    }
}
