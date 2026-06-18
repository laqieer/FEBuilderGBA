using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace FEBuilderGBA
{
    /// <summary>
    /// GUI-free build flow for the "Custom Build" tool, shared by the Avalonia
    /// <c>ToolCustomBuildView</c>. Ported from the WinForms path
    /// (<c>ToolCustomBuildForm.Run</c> → <c>BuildCMD</c> / <c>BuildEA</c>), keeping the
    /// exact <c>FE8_clean.gba</c> vanilla-copy, the <c>SkillsTest.gba</c> build output,
    /// and the EA-compiler error detection. After a successful build the freshly-built
    /// <c>SkillsTest.gba</c> replaces the current ROM (undoable).
    ///
    /// Build chain:
    ///   target file (CUSTOM_BUILD.cmd, or an .event for EA)
    ///     → (a) CMD: copy original ROM → FE8_clean.gba, run the user's CUSTOM_BUILD.cmd
    ///            (which produces SkillsTest.gba), check the EA-compiler output for errors.
    ///       (b) EA: assemble the .event into the ROM via the shared
    ///            <see cref="EventAssemblerCompileCore"/> (the WinForms BuildEA was an
    ///            unfinished stub — "まだ未実装です!" — so the EA path here is strictly
    ///            more complete than the WinForms form).
    ///     → replace the current ROM with the built bytes (undoable, fault-safe).
    ///
    /// Scope boundary (intentional — matches how <see cref="EventAssemblerCompileCore"/>
    /// deferred MakeEAAutoDef and <see cref="AsmCompileCore"/> deferred GoldRoad/MakePatch):
    ///   PROVIDED: CMD build (run CUSTOM_BUILD.cmd → SkillsTest.gba), EA build (via
    ///     EventAssemblerCompileCore), the EA-error detection, the vanilla FE8_clean copy,
    ///     loading the built ROM (undoable), reading SkillsTest.sym.
    ///   INTENTIONALLY DEFERRED to a follow-up (WinForms-coupled): the WinForms
    ///     <c>MargeAndUpdate</c> phase — diffing vanilla vs built ROM, copying the
    ///     symbol/dump files into <c>config/patch2/FE8U/skill_CustomBuild</c>, generating
    ///     a <c>PATCH_SkillSystem_CustomBuild.txt</c>, and AUTO-INSTALLING it via
    ///     <c>PatchForm.UpdatePatch</c>. That pipeline depends on WinForms-only types
    ///     (<c>PatchForm</c>, <c>ToolDiffForm</c>, <c>PatchUtil.SearchSkillSystem</c>)
    ///     that cannot move to Core without a separate extraction. The user re-installs
    ///     the built ROM's custom patch via the existing patch tooling for now.
    ///
    /// Platform boundary (the #1169 lesson): <c>CUSTOM_BUILD.cmd</c> is a Windows batch
    /// script. On Linux/macOS the CMD build path surfaces a clear localized
    /// "Windows-only" message rather than silently failing. The EA build path stays
    /// cross-platform.
    /// </summary>
    public static class CustomBuildCore
    {
        /// <summary>
        /// How the target is built (mirrors the WinForms <c>Run()</c> ext switch:
        /// <c>.CMD</c> → BuildCMD, otherwise BuildEA).
        /// </summary>
        public enum BuildMethod
        {
            /// <summary>Pick by the target file extension: <c>.CMD</c> → CMD, else EA.</summary>
            Auto = 0,
            /// <summary>Run the user's CUSTOM_BUILD.cmd batch script (Windows-only).</summary>
            Cmd = 1,
            /// <summary>Assemble the target .event via the Event Assembler / ColorzCore.</summary>
            EventAssembler = 2,
        }

        /// <summary>Structured result of a build run.</summary>
        public sealed class BuildResult
        {
            /// <summary>True when the build produced a ROM and it was loaded.</summary>
            public bool Success { get; set; }
            /// <summary>Raw build-tool stdout/stderr (for display / logging).</summary>
            public string Output { get; set; } = "";
            /// <summary>Path of the built ROM (<c>SkillsTest.gba</c>) on disk, when one was produced.</summary>
            public string BuiltRomPath { get; set; } = "";
            /// <summary>Symbol-file text read from <c>SkillsTest.sym</c> (may be empty).</summary>
            public string SymbolText { get; set; } = "";
            /// <summary>Localized human-readable error (set when <see cref="Success"/> is false).</summary>
            public string ErrorMessage { get; set; } = "";
        }

        /// <summary>The leaf name of the build output the CMD script produces.</summary>
        public const string BuiltRomLeafName = "SkillsTest.gba";
        /// <summary>The leaf name of the symbol file the CMD script produces.</summary>
        public const string BuiltSymLeafName = "SkillsTest.sym";
        /// <summary>The vanilla-ROM copy name the CMD script expects beside it.</summary>
        public const string VanillaRomLeafName = "FE8_clean.gba";

        /// <summary>True when running on Windows (the only platform that runs .cmd batch scripts).</summary>
        public static bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

        /// <summary>Localized "the CUSTOM_BUILD.cmd batch path is Windows-only" message (no throw).</summary>
        public static string GetCmdWindowsOnlyMessage()
        {
            return R._("CUSTOM_BUILD.cmd is a Windows batch script and can only be run on Windows.");
        }

        /// <summary>
        /// Resolve the effective build method for a target file. <see cref="BuildMethod.Auto"/>
        /// picks CMD for a <c>.CMD</c> extension (case-insensitive) and EA otherwise,
        /// mirroring the WinForms <c>Run()</c> switch.
        /// </summary>
        public static BuildMethod ResolveMethod(string targetPath, BuildMethod requested)
        {
            if (requested != BuildMethod.Auto)
                return requested;
            return U.GetFilenameExt(targetPath ?? "") == ".CMD"
                ? BuildMethod.Cmd
                : BuildMethod.EventAssembler;
        }

        /// <summary>
        /// True when the EA build output reports an error. The exact inverse of
        /// <see cref="EventAssemblerCompileCore.IsEASuccess"/> (same strings as the
        /// WinForms <c>MainFormUtil.IsCompilerErrorByEventAssembler</c>).
        /// </summary>
        public static bool IsCompilerError(string output) =>
            !EventAssemblerCompileCore.IsEASuccess(output ?? "");

        /// <summary>
        /// Build <paramref name="targetPath"/> and load the result into
        /// <paramref name="rom"/> in one fault-safe step.
        ///
        /// Behaviour:
        ///  - Validates the target / original-ROM paths up front (non-blank + exists) →
        ///    localized error, NO throw, NO ROM mutation.
        ///  - CMD method on a non-Windows OS → localized Windows-only error (NO throw).
        ///  - The ROM is mutated ONLY after a clean build (no partial load on failure).
        ///    The load is recorded into <paramref name="undo"/> via
        ///    <see cref="ROM.SwapNewROMData"/>, so it is fully undoable; a mid-load
        ///    failure (resize/size-limit) leaves the ROM byte-identical (no partial
        ///    mutation, since SwapNewROMData only writes after a successful resize and
        ///    we snapshot+rollback defensively).
        ///  - All file I/O (vanilla copy, reading SkillsTest.gba/.sym) is guarded →
        ///    localized error, never throws.
        /// </summary>
        public static BuildResult Build(ROM rom, string targetPath, string originalRomPath,
            BuildMethod method, Undo.UndoData undo, Action<string> onProgress = null)
        {
            var result = new BuildResult();

            if (rom == null)
            {
                result.ErrorMessage = R._("No ROM is loaded.");
                return result;
            }
            // Validate user-provided paths BEFORE doing anything (they come from
            // free-form pickers): the target and the original/vanilla ROM must exist.
            if (string.IsNullOrEmpty(targetPath) || !File.Exists(targetPath))
            {
                result.ErrorMessage = R._("ターゲット({0})が見つかりませんでした。", targetPath ?? "");
                return result;
            }
            if (string.IsNullOrEmpty(originalRomPath) || !File.Exists(originalRomPath))
            {
                result.ErrorMessage = R._("無改造ROM({0})が見つかりませんでした", originalRomPath ?? "");
                return result;
            }

            BuildMethod effective = ResolveMethod(targetPath, method);

            if (effective == BuildMethod.Cmd)
                return BuildCmd(rom, targetPath, originalRomPath, undo, onProgress);
            return BuildEA(rom, targetPath, undo, onProgress);
        }

        /// <summary>
        /// CMD build (WinForms <c>BuildCMD</c>): copy the original ROM → FE8_clean.gba
        /// beside the script, run CUSTOM_BUILD.cmd (which produces SkillsTest.gba),
        /// check the output for EA-compiler errors, then load SkillsTest.gba.
        /// Windows-only (a .cmd batch script).
        /// </summary>
        static BuildResult BuildCmd(ROM rom, string targetPath, string originalRomPath,
            Undo.UndoData undo, Action<string> onProgress)
        {
            var result = new BuildResult();

            if (!IsWindows)
            {
                result.ErrorMessage = GetCmdWindowsOnlyMessage();
                return result;
            }

            string baseDir = Path.GetDirectoryName(Path.GetFullPath(targetPath));
            string vanillaPath = Path.Combine(baseDir, VanillaRomLeafName);
            string builtRomPath = Path.Combine(baseDir, BuiltRomLeafName);
            string builtSymPath = Path.Combine(baseDir, BuiltSymLeafName);

            try
            {
                // Copy the un-modded ROM to the name the build script expects (only if
                // it is not already present — matches WinForms, which preserves a
                // pre-existing FE8_clean.gba).
                if (!File.Exists(vanillaPath))
                    File.Copy(originalRomPath, vanillaPath);
            }
            catch (Exception ioex) when (ioex is IOException || ioex is UnauthorizedAccessException)
            {
                result.ErrorMessage = R._("Unable to write the temporary files needed for compilation.")
                    + "\r\n" + ioex.ToString();
                return result;
            }

            string output;
            try
            {
                // The .cmd produces SkillsTest.gba in its own directory; run it there.
                output = RunProcess(targetPath, "", baseDir);
            }
            catch (Exception ex)
            {
                // The process LAUNCH failed (missing/permission/path issue with the .cmd).
                result.ErrorMessage = R._("プロセスを実行できません。\r\nfilename:{0}\r\n{1}", targetPath, ex.ToString());
                return result;
            }

            result.Output = output;

            if (IsCompilerError(output))
            {
                // Mirror WinForms: prefix the executed command so the user can repro.
                result.ErrorMessage = targetPath + " \r\noutput:\r\n" + output;
                return result;
            }

            return LoadBuiltRom(rom, builtRomPath, builtSymPath, output, undo, result);
        }

        /// <summary>
        /// EA build: assemble the target .event into the ROM via the shared
        /// <see cref="EventAssemblerCompileCore"/>. The WinForms <c>BuildEA</c> was an
        /// unfinished stub ("まだ未実装です!"), so this is strictly more complete: it
        /// runs the real EA assemble+insert (None free-area mode — the .event ORGs
        /// itself, like a custom build script) directly against the current ROM, which
        /// is itself fault-safe and undoable.
        /// </summary>
        static BuildResult BuildEA(ROM rom, string targetPath, Undo.UndoData undo, Action<string> onProgress)
        {
            var result = new BuildResult();

            var eaResult = EventAssemblerCompileCore.CompileAndInsert(
                rom, targetPath, EventAssemblerCompileCore.FreeAreaMode.None,
                undo, SymbolUtil.DebugSymbol.SaveBoth,
                onRetry: onProgress);

            result.Output = eaResult.Output;
            result.SymbolText = eaResult.SymbolText;
            if (!eaResult.Success)
            {
                result.ErrorMessage = eaResult.ErrorMessage;
                return result;
            }
            result.Success = true;
            return result;
        }

        /// <summary>
        /// Read the built ROM bytes and load them into <paramref name="rom"/>, undoable
        /// and fault-safe. Also reads the optional SkillsTest.sym for display. A
        /// mid-load resize/size-limit failure rolls back to the pre-load snapshot so
        /// the ROM is left byte-identical.
        /// </summary>
        static BuildResult LoadBuiltRom(ROM rom, string builtRomPath, string builtSymPath,
            string output, Undo.UndoData undo, BuildResult result)
        {
            if (!File.Exists(builtRomPath))
            {
                result.ErrorMessage = R._("ターゲット({0})が見つかりませんでした。", builtRomPath);
                return result;
            }
            result.BuiltRomPath = builtRomPath;

            byte[] builtBytes;
            try
            {
                builtBytes = File.ReadAllBytes(builtRomPath);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                result.ErrorMessage = R._("Unable to read the compiled file.\r\nfilename:{0}", builtRomPath)
                    + "\r\n" + ex.ToString();
                return result;
            }
            if (builtBytes.Length <= 0)
            {
                result.ErrorMessage = R._("ファイルがゼロバイトです。\r\nファイル名:{0}", builtRomPath);
                return result;
            }

            // Read the symbol file for display (best-effort — its absence is not fatal).
            try
            {
                if (File.Exists(builtSymPath))
                    result.SymbolText = File.ReadAllText(builtSymPath);
            }
            catch { /* symbol display is best-effort */ }

            // Snapshot + rollback so a mid-load failure leaves the ROM byte-identical.
            byte[] snapshot = (byte[])rom.Data.Clone();
            int undoCountBefore = undo.list.Count;
            bool swapped;
            try
            {
                // confirmHeaderChange:false — this is a programmatic/automation path,
                // so apply a header-modifying build without the interactive ShowYesNo
                // (headless ShowYesNo always returns false and would silently cancel).
                swapped = rom.SwapNewROMData(builtBytes, "custom_build", undo, confirmHeaderChange: false);
            }
            catch (Exception ex)
            {
                RollbackPartialLoad(rom, snapshot, undo, undoCountBefore);
                result.ErrorMessage = R._("Failed to apply the compiled ROM (size/resize limit exceeded?).")
                    + "\r\n" + ex.ToString();
                return result;
            }
            if (!swapped)
            {
                RollbackPartialLoad(rom, snapshot, undo, undoCountBefore);
                result.ErrorMessage = R._("Failed to apply the compiled ROM (size/resize limit exceeded?).");
                return result;
            }

            result.Output = output;
            result.Success = true;
            return result;
        }

        /// <summary>
        /// Restore the ROM to <paramref name="snapshot"/> and trim any undo entries the
        /// failed load appended, so a mid-load failure leaves NO partial mutation.
        /// </summary>
        static void RollbackPartialLoad(ROM rom, byte[] snapshot, Undo.UndoData undo, int undoCountBefore)
        {
            rom.SwapNewROMDataDirect(snapshot);
            if (undo.list.Count > undoCountBefore)
                undo.list.RemoveRange(undoCountBefore, undo.list.Count - undoCountBefore);
        }

        /// <summary>
        /// Run a build tool and capture combined stdout+stderr (120s timeout). Shared
        /// shape with <see cref="EventAssemblerCompileCore"/>'s runner. The CMD path
        /// runs a Windows batch script, so <c>UseShellExecute=false</c> launches it via
        /// the OS (cmd.exe associates .cmd) with the script directory as the working dir.
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
            // Process.Start(psi) can return null (documented) when no new process was
            // started — e.g. the OS reused an existing process for the verb. Guard it so
            // the build reports a clean error instead of throwing an NRE on proc.* below.
            using (var proc = Process.Start(psi))
            {
                if (proc == null)
                    return "Error: the custom build process could not be started. filename:" + exePath;

                proc.OutputDataReceived += (_, e) => { if (e.Data != null) sb.AppendLine(e.Data); };
                proc.ErrorDataReceived += (_, e) => { if (e.Data != null) sb.AppendLine(e.Data); };
                proc.BeginOutputReadLine();
                proc.BeginErrorReadLine();

                if (!proc.WaitForExit(120_000))
                {
                    // A CUSTOM_BUILD.cmd build typically spawns CHILD processes
                    // (make/gcc/as/etc.); killing only the parent would orphan them.
                    // Kill the WHOLE process tree on timeout.
                    try { proc.Kill(entireProcessTree: true); } catch { }
                    return "Error: the custom build timed out after 120 seconds.";
                }
            }
            return sb.ToString();
        }
    }
}
