using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace FEBuilderGBA
{
    /// <summary>
    /// GUI-free compile+insert flow for Event Assembler / ColorzCore, shared by
    /// the CLI (<c>--compile-event</c>) and the Avalonia "Add-via-Event-Assembler"
    /// tool. Ported from the WinForms path
    /// (<c>EventAssemblerForm.WriteEA</c> → <c>MainFormUtil.CompilerEventAssembler</c>)
    /// and the CLI <c>RunCompileEvent</c>, keeping the exact EA arguments,
    /// success strings, and old-EA <c>-symOutput</c> fallback.
    ///
    /// Auto-def scope boundary (intentional, matches the CLI <c>--compile-event</c>
    /// baseline — NOT a silent truncation of WinForms behaviour):
    ///   PROVIDED by this helper's wrapper:
    ///     - the <c>#include</c> of the chosen .event
    ///     - <c>#define FreeSpace 0x...</c> + <c>ORG FreeSpace</c> for the
    ///       Program/Data free-area modes (computed via
    ///       <see cref="ROM.FindFreeSpace"/>, falling back to the ROM end); None
    ///       mode stays include-only (the .event must ORG itself).
    ///   INTENTIONALLY DEFERRED — the extra symbol auto-defs that the full WinForms
    ///   <c>EAUtil.MakeEAAutoDef</c> emits: the ItemImage/ItemPalette/ItemTable/
    ///   TextTable/PortraitTable/AI1Table/AI2Table table-pointer defines, the
    ///   skill-system + SummonUnitTable defines, the magic-split (FE8N/FE8U/FE7U)
    ///   defines, the support-action-rework define, and the
    ///   <c>Program.ExportFunction.ExportEA</c> function defines. Those originate in
    ///   WinForms-only types (PatchUtil, SkillConfigSkillSystemForm,
    ///   UnitActionPointerForm, the MagicSplitUtil GUI paths, Program.ExportFunction)
    ///   and cannot move to Core without a separate extraction. EA scripts that need
    ///   them must define them themselves or run through the WinForms form; raw
    ///   ORG/BYTE and FreeSpace-based scripts assemble fully here.
    /// </summary>
    public static class EventAssemblerCompileCore
    {
        /// <summary>
        /// Test seam for resolving the EA executable. Defaults to the production
        /// resolver and stays internal so callers cannot swap it at runtime.
        /// </summary>
        internal static Func<string> ResolveExeOverride = ToolPathResolver.ResolveEventAssembler;

        /// <summary>
        /// Test seam for the EA process runner. Defaults to
        /// <see cref="RunProcessDefault"/>'s <see cref="ProcessRunnerCore"/> adapter
        /// and stays internal for deterministic Core.Tests coverage.
        /// </summary>
        internal static Func<string, IEnumerable<string>, string?, ProcessRunResult> RunProcessOverride = RunProcessDefault;

        internal const int DefaultRunProcessTimeoutMs = 120_000;
        const string PosixProcessWorkerScript =
            "#!/bin/sh\n"
            + "if [ \"$#\" -lt 1 ]; then\n"
            + "  exit 127\n"
            + "fi\n"
            + "if command -v setsid >/dev/null 2>&1; then\n"
            + "  exec setsid \"$@\"\n"
            + "fi\n"
            + "if command -v python3 >/dev/null 2>&1; then\n"
            + "  exec python3 -c 'import os, sys; os.setsid(); os.execvp(sys.argv[1], sys.argv[1:])' \"$@\"\n"
            + "fi\n"
            + "echo 'Unable to establish a process group: requires setsid or python3.' 1>&2\n"
            + "exit 127\n";

        /// <summary>
        /// Internal wait-duration seam for deterministic timeout tests. The shared
        /// <see cref="ProcessRunnerCore"/> still reports the production
        /// "120 seconds" sentinel so <see cref="IsEASuccess"/> and higher-level
        /// error handling keep their exact contract.
        /// </summary>
        internal static int RunProcessTimeoutMs = DefaultRunProcessTimeoutMs;

        /// <summary>
        /// Free-area selection mode (mirrors WinForms <c>FREEAREA_DEF_ENUM</c>).
        /// </summary>
        public enum FreeAreaMode
        {
            /// <summary>Allocate in the program (upper) area — passed to EA as <c>#define FreeSpace</c>.</summary>
            Program = 0,
            /// <summary>Allocate in the data (lower) area — passed to EA as <c>#define FreeSpace</c>.</summary>
            Data = 1,
            /// <summary>Do not define a free area; the .event must ORG itself.</summary>
            None = 2,
        }

        /// <summary>Structured result of a compile+insert run.</summary>
        public sealed class CompileResult
        {
            /// <summary>True when EA compiled with no errors and the ROM was inserted.</summary>
            public bool Success { get; set; }
            /// <summary>Raw EA stdout/stderr (for display / logging).</summary>
            public string Output { get; set; } = "";
            /// <summary>The free area the wrapper defined (ROM offset), or <see cref="U.NOT_FOUND"/> for None mode.</summary>
            public uint InsertedAddr { get; set; } = U.NOT_FOUND;
            /// <summary>Raw symbol-file text produced by EA (may be empty).</summary>
            public string SymbolText { get; set; } = "";
            /// <summary>Number of symbol entries parsed from <see cref="SymbolText"/>.</summary>
            public int SymbolCount { get; set; }
            /// <summary>Localized human-readable error (set when <see cref="Success"/> is false).</summary>
            public string ErrorMessage { get; set; } = "";
        }

        /// <summary>
        /// Resolve the EA/ColorzCore executable (config first, then bundled submodule).
        /// Returns null if none is found — callers should surface
        /// <see cref="GetNotFoundMessage"/>.
        /// </summary>
        public static string ResolveExe() => (ResolveExeOverride ?? ToolPathResolver.ResolveEventAssembler)();

        /// <summary>Localized "Event Assembler not found" message (no throw).</summary>
        public static string GetNotFoundMessage()
        {
            // Mirror the WinForms EventAssemblerForm_Load message.
            return R._("{0}の設定がありません。 設定->オプションから、{0}を設定してください。", "event_assembler");
        }

        /// <summary>
        /// Build the EA command-line arguments (extracted so it can be unit-tested
        /// without running the process). Mirrors the CLI / WinForms arg order.
        /// </summary>
        public static string BuildArgs(string gameCode, string wrapperPath, string outputRom,
            string symFile, bool isColorzCore, bool includeSym = true)
        {
            string[] tokens = BuildArgTokens(
                gameCode,
                wrapperPath,
                outputRom,
                symFile,
                isColorzCore,
                includeSym);

            string args = tokens[0] + " " + tokens[1];
            for (int i = 2; i < tokens.Length; i++)
            {
                args += " " + U.escape_shell_args(tokens[i]);
            }
            return args;
        }

        static string[] BuildArgTokens(
            string gameCode,
            string wrapperPath,
            string outputRom,
            string symFile,
            bool isColorzCore,
            bool includeSym = true)
        {
            if (!includeSym)
            {
                return new[]
                {
                    "A",
                    gameCode,
                    "-input:" + wrapperPath,
                    "-output:" + outputRom,
                };
            }

            return new[]
            {
                "A",
                gameCode,
                "-input:" + wrapperPath,
                "-output:" + outputRom,
                isColorzCore
                    ? "--nocash-sym:" + symFile
                    : "-symOutput:" + symFile,
            };
        }

        /// <summary>
        /// Build the minimal free-area-def wrapper text. Program/Data modes emit
        /// <c>#define FreeSpace 0x...</c>, then <c>ORG FreeSpace</c>, then the
        /// <c>#include</c>; None stays include-only. The included path is the leaf
        /// filename, since the wrapper is written into the same directory as the
        /// target .event (so EA's include search finds it).
        /// </summary>
        public static string BuildWrapper(string eaFileName, uint freeArea, FreeAreaMode mode)
        {
            var sb = new StringBuilder();
            if (mode != FreeAreaMode.None && freeArea != U.NOT_FOUND)
            {
                sb.AppendLine("#define FreeSpace " + U.To0xHexString(freeArea));
                sb.AppendLine("ORG FreeSpace");
            }
            sb.AppendLine("#include \"" + eaFileName + "\"");
            return sb.ToString();
        }

        /// <summary>True when EA output reports no errors (same strings as WinForms/CLI).</summary>
        public static bool IsEASuccess(string output)
        {
            return output.IndexOf("No errors or warnings.", StringComparison.Ordinal) >= 0
                || output.IndexOf("No errors. Please continue being awesome.", StringComparison.Ordinal) >= 0;
        }

        /// <summary>
        /// Detect the exact EA warning that means bytes were written before the
        /// script selected an offset. This is treated as a hard failure even when
        /// EA still prints its normal success marker.
        /// </summary>
        internal static bool HasWritingBeforeInitializingOffsetWarning(string output)
        {
            return !string.IsNullOrEmpty(output)
                && output.IndexOf("Writing before initializing offset.", StringComparison.Ordinal) >= 0;
        }

        /// <summary>
        /// Compute the free area (ROM offset) for the given mode using
        /// <see cref="ROM.FindFreeSpace"/>, falling back to the ROM end when no
        /// block is found. Returns <see cref="U.NOT_FOUND"/> for None mode.
        /// </summary>
        public static uint ComputeFreeArea(ROM rom, FreeAreaMode mode, uint needSize = 1024 * 200)
        {
            if (mode == FreeAreaMode.None)
                return U.NOT_FOUND;

            uint romLen = (uint)rom.Data.Length;
            // Program → search from the upper half down; Data → search from the start.
            uint searchStart = mode == FreeAreaMode.Program ? (romLen / 2u) : 0x100u;
            uint addr = rom.FindFreeSpace(searchStart, needSize);
            if (addr == U.NOT_FOUND && mode == FreeAreaMode.Program)
            {
                // Fall back to the lower half before giving up on the ROM end.
                addr = rom.FindFreeSpace(0x100u, needSize);
            }
            if (addr == U.NOT_FOUND)
            {
                // ROM末尾 — mirror WinForms RedefineFreeArea fallback.
                addr = romLen;
            }
            return addr;
        }

        /// <summary>
        /// Compile <paramref name="eaFilePath"/> and insert the result into
        /// <paramref name="rom"/> in one fault-safe step.
        ///
        /// Behaviour:
        ///  - When the EA/ColorzCore exe is not found, returns a result with
        ///    <see cref="CompileResult.Success"/> = false and the localized
        ///    <see cref="GetNotFoundMessage"/> — it does NOT throw and does NOT
        ///    mutate the ROM.
        ///  - The ROM is mutated ONLY after a clean compile (no partial insert on
        ///    failure). The mutation is recorded into <paramref name="undo"/> via
        ///    <see cref="ROM.SwapNewROMData"/>, so it is fully undoable.
        ///  - Debug symbols are stored via
        ///    <see cref="SymbolUtil.ProcessSymbolByComment"/> per
        ///    <paramref name="storeSymbol"/>.
        /// </summary>
        public static CompileResult CompileAndInsert(ROM rom, string eaFilePath,
            FreeAreaMode mode, Undo.UndoData undo, SymbolUtil.DebugSymbol storeSymbol,
            Action<string> onRetry = null)
        {
            var result = new CompileResult();

            if (rom == null)
            {
                result.ErrorMessage = R._("No ROM is loaded.");
                return result;
            }
            if (string.IsNullOrEmpty(eaFilePath) || !File.Exists(eaFilePath))
            {
                result.ErrorMessage = R._("Event file not found: {0}", eaFilePath ?? "");
                return result;
            }

            string eaExe = ResolveExe();
            if (string.IsNullOrEmpty(eaExe) || !File.Exists(eaExe))
            {
                result.ErrorMessage = GetNotFoundMessage();
                return result;
            }

            bool isColorzCore = ToolPathResolver.IsColorzCore(eaExe);
            // RomInfo is null only for a not-yet-identified ROM; fall back to the
            // FE0 "NAZO" code so raw ORG/BYTE scripts can still assemble.
            string gameCode = rom.RomInfo?.TitleToFilename ?? "NAZO";
            string toolDir = Path.GetDirectoryName(eaExe);

            uint freeArea = ComputeFreeArea(rom, mode);
            result.InsertedAddr = freeArea;

            string eaFullPath = Path.GetFullPath(eaFilePath);
            string eaDir = Path.GetDirectoryName(eaFullPath);
            string eaName = Path.GetFileName(eaFullPath);

            // Wrapper lives beside the target .event so EA's include search finds it.
            string wrapperPath = Path.Combine(eaDir, "_FBG_Temp_" + DateTime.Now.Ticks + ".event");
            string tempRomPath = Path.Combine(Path.GetTempPath(), "febuilder_ea_" + DateTime.Now.Ticks + ".gba");
            // symFile is created inside the guarded block below so a temp-file /
            // disk-full failure returns a structured error instead of throwing.
            string symFile = null;

            try
            {
                symFile = Path.GetTempFileName();
                File.WriteAllText(wrapperPath, BuildWrapper(eaName, freeArea, mode));
                // Write the CURRENT ROM bytes for EA to patch in place.
                File.WriteAllBytes(tempRomPath, rom.Data);

                // Track the args actually executed so a failure reports the right
                // command (the old-EA fallback below replaces them).
                string[] executedArgTokens = BuildArgTokens(
                    gameCode,
                    wrapperPath,
                    tempRomPath,
                    symFile,
                    isColorzCore);
                string executedArgs = BuildArgs(
                    gameCode,
                    wrapperPath,
                    tempRomPath,
                    symFile,
                    isColorzCore);

                ProcessRunResult processRun;
                string output;
                try
                {
                    processRun = RunProcess(eaExe, executedArgTokens, toolDir);
                }
                catch (Exception ex)
                {
                    // Win32Exception etc. — the process LAUNCH failed, so report the
                    // executable being launched (a missing/permission/path issue with
                    // the EA/ColorzCore exe), not the .event path.
                    result.ErrorMessage = R._("プロセスを実行できません。\r\nfilename:{0}\r\n{1}", eaExe, ex.ToString());
                    return result;
                }
                if (!processRun.Started)
                {
                    result.Output = CombineProcessOutput(eaExe, processRun);
                    result.ErrorMessage = R._("プロセスを実行できません。\r\nfilename:{0}\r\n{1}", eaExe, processRun.ErrorMessage);
                    return result;
                }

                output = CombineProcessOutput(eaExe, processRun);

                bool hasError = HasProcessRunnerFailure(processRun)
                    || !IsEASuccess(output);

                // Old-EA fallback: retry without -symOutput (re-seed the temp ROM
                // first, the failed attempt may have partially written it).
                if (hasError && !isColorzCore &&
                    (output.IndexOf("symOutput doesn't exist.", StringComparison.Ordinal) >= 0
                     || output.IndexOf("Unrecognized flag: symOutput", StringComparison.Ordinal) >= 0))
                {
                    onRetry?.Invoke("Retrying without -symOutput (older EA detected)...");
                    File.WriteAllBytes(tempRomPath, rom.Data);
                    executedArgTokens = BuildArgTokens(
                        gameCode,
                        wrapperPath,
                        tempRomPath,
                        symFile,
                        isColorzCore,
                        includeSym: false);
                    executedArgs = BuildArgs(
                        gameCode,
                        wrapperPath,
                        tempRomPath,
                        symFile,
                        isColorzCore,
                        includeSym: false);
                    processRun = RunProcess(eaExe, executedArgTokens, toolDir);
                    if (!processRun.Started)
                    {
                        result.Output = CombineProcessOutput(eaExe, processRun);
                        result.ErrorMessage = R._("プロセスを実行できません。\r\nfilename:{0}\r\n{1}", eaExe, processRun.ErrorMessage);
                        return result;
                    }

                    output = CombineProcessOutput(eaExe, processRun);
                    hasError = HasProcessRunnerFailure(processRun)
                        || !IsEASuccess(output);
                }

                result.Output = output;

                if (hasError)
                {
                    // Mirror WinForms: prefix the ACTUALLY-EXECUTED command so the user can repro.
                    result.ErrorMessage = eaExe + " " + executedArgs + " \r\noutput:\r\n" + output;
                    return result;
                }

                if (HasWritingBeforeInitializingOffsetWarning(output))
                {
                    // EA reported success, but this exact warning means the script
                    // wrote before choosing an offset (e.g. missing ORG). Fail the
                    // insert before SwapNewROMData so the loaded ROM stays untouched.
                    result.ErrorMessage = eaExe + " " + executedArgs + " \r\noutput:\r\n" + output;
                    return result;
                }

                if (!File.Exists(tempRomPath))
                {
                    result.ErrorMessage = R._("Error: EA did not produce output ROM.");
                    return result;
                }

                // Compile succeeded — NOW mutate the ROM (undoable, fault-safe).
                // confirmHeaderChange:false — this is a programmatic/automation path
                // (CLI --compile-event + the Avalonia tool), so apply header-modifying
                // scripts without the interactive ShowYesNo (headless ShowYesNo always
                // returns false and would silently cancel the insert). Matches the old
                // CLI behaviour of emitting the patched ROM regardless.
                byte[] newRomData = File.ReadAllBytes(tempRomPath);
                bool swapped = rom.SwapNewROMData(newRomData, "event_assembler", undo, confirmHeaderChange: false);
                if (!swapped)
                {
                    // The header prompt is bypassed here, so a false return is NOT a
                    // user cancellation — it is a failure to APPLY the compiled ROM,
                    // most commonly a resize/size limit (e.g. output exceeds 32 MB).
                    result.ErrorMessage = R._("Failed to apply the compiled ROM (size/resize limit exceeded?).");
                    return result;
                }

                // Store debug symbols (same as WinForms WriteEA).
                if (File.Exists(symFile))
                {
                    result.SymbolText = File.ReadAllText(symFile);
                    if (!string.IsNullOrEmpty(result.SymbolText.Trim()))
                        result.SymbolCount = result.SymbolText.Trim().Split('\n').Length;
                }
                SymbolUtil.ProcessSymbolByComment(eaFullPath, result.SymbolText, storeSymbol, 0);

                result.Success = true;
                return result;
            }
            catch (Exception ioex) when (ioex is IOException || ioex is UnauthorizedAccessException)
            {
                // Any temp-file write/read (wrapper, temp ROM, sym file — incl. the
                // old-EA re-seed and the post-compile read-back) failing on a
                // read-only dir / permission issue / full temp disk returns a
                // localized error instead of throwing out of this method (never-throws
                // contract; mirrors WF CompilerEventAssembler's "Unable to write").
                result.Success = false;
                result.ErrorMessage = R._("Unable to write the temporary files needed for compilation.")
                    + "\r\n" + ioex.ToString();
                return result;
            }
            finally
            {
                TryDelete(wrapperPath);
                TryDelete(tempRomPath);
                TryDelete(symFile);
            }
        }

        static void TryDelete(string path)
        {
            try { if (!string.IsNullOrEmpty(path) && File.Exists(path)) File.Delete(path); }
            catch { /* best-effort cleanup */ }
        }

        /// <summary>
        /// Run the EA/ColorzCore process via the shared
        /// <see cref="ProcessRunnerCore"/> infrastructure.
        /// </summary>
        static ProcessRunResult RunProcess(
            string exePath,
            IEnumerable<string> args,
            string? workDir)
        {
            return (RunProcessOverride ?? RunProcessDefault)(exePath, args, workDir);
        }

        static ProcessRunResult RunProcessDefault(
            string exePath,
            IEnumerable<string> args,
            string? workDir)
        {
            int timeoutMs = RunProcessTimeoutMs > 0
                ? RunProcessTimeoutMs
                : DefaultRunProcessTimeoutMs;

            List<string> materializedArgs = args != null
                ? new List<string>(args)
                : new List<string>();

            if (OperatingSystem.IsWindows())
            {
                return ProcessRunnerCore.Run(
                    exePath,
                    materializedArgs,
                    workDir,
                    timeoutMs);
            }

            string processWorkerDir = Path.Combine(
                Path.GetTempPath(),
                "febuilder_ea_process_runner_" + Guid.NewGuid().ToString("N"));
            string processWorkerPath = Path.Combine(
                processWorkerDir,
                "process_worker.py");
            Directory.CreateDirectory(processWorkerDir);
            File.WriteAllText(processWorkerPath, PosixProcessWorkerScript);

            try
            {
                string processWorkerInterpreter = ResolvePosixProcessWorkerInterpreter();
                var wrappedArgs = new List<string>(materializedArgs.Count + 2)
                {
                    processWorkerPath,
                    exePath,
                };
                wrappedArgs.AddRange(materializedArgs);

                ProcessRunResult wrappedRun = ProcessRunnerCore.Run(
                    processWorkerInterpreter,
                    wrappedArgs,
                    workDir,
                    timeoutMs);
                return wrappedRun;
            }
            finally
            {
                TryDelete(processWorkerPath);
                TryDeleteDirectory(processWorkerDir);
            }
        }

        static bool HasProcessRunnerFailure(ProcessRunResult run)
        {
            return run.TimedOut
                || run.OutputLimitExceeded
                || run.TerminationFailed
                || run.Cancelled;
        }

        static string CombineProcessOutput(string exePath, ProcessRunResult run)
        {
            if (!run.Started)
            {
                if (run.ErrorMessage.IndexOf(
                    "Process.Start returned false",
                    StringComparison.Ordinal) >= 0)
                {
                    return "Error: the Event Assembler process could not be started. filename:"
                        + exePath;
                }
                return run.ErrorMessage ?? "";
            }
            if (run.TimedOut)
            {
                return "Error: Event Assembler timed out after 120 seconds.";
            }

            var sb = new StringBuilder();
            AppendOutputSegment(sb, run.Stderr);
            AppendOutputSegment(sb, run.Stdout);
            if (!string.IsNullOrEmpty(run.ErrorMessage))
            {
                AppendOutputSegment(sb, run.ErrorMessage);
            }
            return sb.ToString();
        }

        static void AppendOutputSegment(StringBuilder sb, string text)
        {
            if (string.IsNullOrEmpty(text))
                return;

            sb.Append(text);
            if (!text.EndsWith("\r", StringComparison.Ordinal)
                && !text.EndsWith("\n", StringComparison.Ordinal))
            {
                sb.AppendLine();
            }
        }

        static string ResolvePosixProcessWorkerInterpreter()
        {
            const string systemShell = "/bin/sh";
            return File.Exists(systemShell)
                ? systemShell
                : "sh";
        }

        static void TryDeleteDirectory(string path)
        {
            try
            {
                if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
                    Directory.Delete(path, recursive: true);
            }
            catch
            {
            }
        }
    }
}
