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
    /// Scope (issue #1248 slice 2 wired the MargeAndUpdate auto-install):
    ///   PROVIDED: CMD build (run CUSTOM_BUILD.cmd → SkillsTest.gba), EA build (via
    ///     EventAssemblerCompileCore), the EA-error detection, the vanilla FE8_clean copy,
    ///     loading the built ROM (undoable), reading SkillsTest.sym, AND the
    ///     <see cref="MargeAndUpdate"/> phase — diffing vanilla vs the built ROM,
    ///     copying the symbol/dump files into <c>config/patch2/FE8U/skill_CustomBuild</c>,
    ///     generating a <c>PATCH_SkillSystem_CustomBuild.txt</c>, merging the parent
    ///     SkillSystem patch text, and AUTO-INSTALLING it via <see cref="PatchInstallCore"/>
    ///     (a CustomBuild patch is literal-offset BIN-diffs, so the slice-1
    ///     <c>PatchInstallCore.ApplyPatch</c> install — not the full
    ///     <c>PatchForm.UpdatePatch</c> dependency-resolution engine — is what it needs).
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

        // ===================================================================
        //  MargeAndUpdate — the patch-merge + auto-install phase (#1248 slice 2)
        // ===================================================================

        /// <summary>The relative path of the parent SkillSystem patch under BaseDirectory.</summary>
        public static readonly string[] ParentPatchRelativeParts =
            { "config", "patch2", "FE8U", "skill20220703", "PATCH_Skill20220703.txt" };

        /// <summary>The relative path of the CustomBuild cache directory under BaseDirectory.</summary>
        public static readonly string[] CustomBuildCacheRelativeParts =
            { "config", "patch2", "FE8U", "skill_CustomBuild" };

        /// <summary>The generated CustomBuild patch leaf name.</summary>
        public const string CustomBuildPatchLeafName = "PATCH_SkillSystem_CustomBuild.txt";

        /// <summary>Structured result of a <see cref="MargeAndUpdate"/> run.</summary>
        public sealed class MargeResult
        {
            /// <summary>True when the merged patch was generated and installed.</summary>
            public bool Success { get; set; }
            /// <summary>Path of the generated CustomBuild patch on disk, when one was produced.</summary>
            public string PatchPath { get; set; } = "";
            /// <summary>Localized human-readable error (set when <see cref="Success"/> is false).</summary>
            public string ErrorMessage { get; set; } = "";
        }

        /// <summary>
        /// Resolve <see cref="ParentPatchRelativeParts"/> against
        /// <see cref="CoreState.BaseDirectory"/> (empty BaseDirectory → relative path).
        /// </summary>
        public static string GetParentPatchPath() =>
            CombineUnderBase(ParentPatchRelativeParts);

        /// <summary>
        /// Resolve <see cref="CustomBuildCacheRelativeParts"/> against
        /// <see cref="CoreState.BaseDirectory"/>.
        /// </summary>
        public static string GetCustomBuildCacheDir() =>
            CombineUnderBase(CustomBuildCacheRelativeParts);

        static string CombineUnderBase(string[] parts)
        {
            string baseDir = CoreState.BaseDirectory ?? "";
            string[] all = new string[parts.Length + 1];
            all[0] = baseDir;
            Array.Copy(parts, 0, all, 1, parts.Length);
            return Path.Combine(all);
        }

        /// <summary>
        /// Port of the WinForms <c>ToolCustomBuildForm.MargeAndUpdate</c>, MINUS the
        /// WinForms UI and the <c>PatchForm.UpdatePatch</c> dependency-resolution engine.
        ///
        /// Diff the built ROM against the vanilla ROM, assemble a self-contained
        /// CustomBuild patch in <c>config/patch2/FE8U/skill_CustomBuild</c> (the parent
        /// SkillSystem patch's text minus its own BIN/BINF/metadata lines + the fresh
        /// vanilla→built BIN-diff), then auto-install it via
        /// <see cref="PatchInstallCore"/>. A CustomBuild patch is literal-offset
        /// BIN-diffs only, so the slice-1 install (not the full PatchForm engine) is
        /// what it needs; <c>UPDATE_UNINSTALL</c> of a prior patch is a no-op here (the
        /// fresh-install case), so the install applies the BIN diffs directly.
        ///
        /// Fault-safety: validates inputs up front (no throw, no ROM mutation on a bad
        /// path); the install writes go through <see cref="PatchInstallCore.ApplyPatch"/>
        /// into <paramref name="undo"/> (so the caller commits/rolls back on the UI
        /// thread). A mid-install failure leaves whatever ApplyPatch already wrote
        /// recorded in <paramref name="undo"/> for the caller to roll back.
        /// </summary>
        /// <param name="rom">The working ROM to install the CustomBuild patch into (CoreState.ROM).</param>
        /// <param name="originalRomPath">The un-modded (vanilla) ROM path — the diff baseline.</param>
        /// <param name="builtRomPath">The freshly-built ROM path (SkillsTest.gba).</param>
        /// <param name="targetPath">The build target path (its directory holds the .sym/.dmp sidecars).</param>
        /// <param name="takeoverSkillAssignment">
        /// 0 = do not carry over the parent patch's skill assignment (drops
        /// UPDATE_METHOD=SKILLSYSTEM / EDIT_PATCH lines); non-zero = carry it over.
        /// </param>
        /// <param name="undo">Explicit undo scope the install writes into (NOT thread-local).</param>
        public static MargeResult MargeAndUpdate(ROM rom, string originalRomPath,
            string builtRomPath, string targetPath, uint takeoverSkillAssignment,
            Undo.UndoData undo, Action<string> onProgress = null)
        {
            var result = new MargeResult();

            if (rom == null)
            {
                result.ErrorMessage = R._("No ROM is loaded.");
                return result;
            }
            if (undo == null)
            {
                result.ErrorMessage = R._("No ROM is loaded.");
                return result;
            }
            if (string.IsNullOrEmpty(originalRomPath) || !File.Exists(originalRomPath))
            {
                result.ErrorMessage = R._("無改造ROM({0})が見つかりませんでした", originalRomPath ?? "");
                return result;
            }
            if (string.IsNullOrEmpty(builtRomPath) || !File.Exists(builtRomPath))
            {
                result.ErrorMessage = R._("ターゲット({0})が見つかりませんでした。", builtRomPath ?? "");
                return result;
            }

            string parentPatch = GetParentPatchPath();
            if (!File.Exists(parentPatch))
            {
                result.ErrorMessage = R._("ベースとなるシステム({0})が見つかりませんでした", parentPatch);
                return result;
            }

            byte[] originalBin, builtBin;
            try
            {
                originalBin = File.ReadAllBytes(originalRomPath);
                builtBin = File.ReadAllBytes(builtRomPath);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                result.ErrorMessage = R._("Unable to read the compiled file.\r\nfilename:{0}", builtRomPath)
                    + "\r\n" + ex.ToString();
                return result;
            }

            string cacheDir = GetCustomBuildCacheDir();
            try
            {
                // Rebuild the CustomBuild cache directory from the parent patch dir,
                // dropping the parent's own diffs + patch text (we regenerate them).
                onProgress?.Invoke(R._("Preparing the CustomBuild patch..."));
                if (!DelTree(cacheDir))
                {
                    result.ErrorMessage = R._(
                        "カスタムビルドのキャッシュを削除できませんでした。\r\n以下のディレクトリを手動で消去してください。\r\n{0}",
                        cacheDir);
                    return result;
                }
                Directory.CreateDirectory(cacheDir);

                string parentDir = Path.GetDirectoryName(parentPatch) ?? "";
                CopyDirectoryShallow(parentDir, cacheDir);
                DeleteFilesByPattern(cacheDir, "0*.bin");
                DeleteFilesByPattern(cacheDir, "PATCH_*.txt");

                // Copy the symbol + dump sidecars produced beside the build target.
                string targetDir = string.IsNullOrEmpty(targetPath)
                    ? "" : (Path.GetDirectoryName(Path.GetFullPath(targetPath)) ?? "");
                CopySYM(targetDir, cacheDir);
                CopySomeDumpFiles(targetDir, cacheDir);
            }
            catch (Exception ex)
            {
                result.ErrorMessage = R._(
                    "Unable to write the temporary files needed for compilation.")
                    + "\r\n" + ex.ToString();
                return result;
            }

            // The CustomBuild tool targets FE8 (the view gates on version==8), so default
            // to FE8U when RomInfo is unavailable; otherwise honour the loaded ROM's
            // version/multibyte so the free-area span in MakeDiff matches FE8U vs FE8J.
            int diffVersion = rom.RomInfo?.version ?? 8;
            bool diffMultibyte = rom.RomInfo?.is_multibyte ?? false;

            string custombuild = Path.Combine(cacheDir, CustomBuildPatchLeafName);
            PatchInstallCore.PatchSt diffPatch;
            try
            {
                // Diff the built ROM against vanilla → the CustomBuild BIN-diff patch.
                // MakeDiff writes ONLY the pure literal-offset BIN subset (TYPE=BIN +
                // BINF: lines + .bin sidecars), which is exactly what PatchInstallCore
                // installs. We load THIS pre-merge diff for the fresh install (below),
                // then overwrite the file with the parent-merged text as the on-disk
                // artifact the full PatchForm engine consumes on a later re-install.
                onProgress?.Invoke(R._("Generating the patch diff..."));
                DiffToolCore.MakeDiff(custombuild, originalBin, builtBin,
                    patchedIfMinSize: 10, collectFreeSpace: true,
                    version: diffVersion, isMultibyte: diffMultibyte);
                diffPatch = PatchInstallCore.LoadPatch(custombuild);
            }
            catch (Exception ex)
            {
                result.ErrorMessage = R._("Unable to write the compiled file: {0}", custombuild)
                    + "\r\n" + ex.ToString();
                return result;
            }

            // Auto-install the fresh CustomBuild diff (slice-1 BIN install). This is a
            // FRESH install: a CustomBuild patch is BIN-diffs, and the parent's
            // UPDATE_UNINSTALL is a no-op on first run, so applying the diff's BIN lines
            // is all the install needs (no full PatchForm dependency-resolution engine).
            if (diffPatch == null)
            {
                // MakeDiff produced no diff (built ROM == vanilla) → nothing to install.
                // Still write the merged artifact below so the on-disk patch is valid.
            }
            else
            {
                try
                {
                    onProgress?.Invoke(R._("Installing the patch..."));
                    PatchInstallCore.ApplyPatch(diffPatch, rom, undo);
                }
                catch (PatchInstallException pex)
                {
                    result.ErrorMessage = pex.Message;
                    return result;
                }
                catch (Exception ex)
                {
                    result.ErrorMessage = R._("Failed to apply the compiled ROM (size/resize limit exceeded?).")
                        + "\r\n" + ex.ToString();
                    return result;
                }
            }

            // Write the parent-merged patch text as the on-disk CustomBuild artifact
            // (UPDATE_UNINSTALL of the parent + the carried-over parent lines + the diff).
            // This overwrites the pure-diff file we just installed from.
            try
            {
                MargePatch(custombuild, parentPatch, takeoverSkillAssignment);
            }
            catch (Exception ex)
            {
                result.ErrorMessage = R._("Unable to write the compiled file: {0}", custombuild)
                    + "\r\n" + ex.ToString();
                return result;
            }
            result.PatchPath = custombuild;

            result.Success = true;
            return result;
        }

        /// <summary>
        /// Port of the WinForms <c>ToolCustomBuildForm.MargePatch</c>: build the merged
        /// CustomBuild patch text by emitting an <c>UPDATE_UNINSTALL</c> of the parent
        /// patch, then every parent line EXCEPT the parent's own diffs/metadata
        /// (PATCHED_IF/BINF/BIN/AFTER_TRY_EXECUTE/NAME/INFO/TEXTADV/EXTENDS), and finally
        /// the freshly-generated CustomBuild diff text. When
        /// <paramref name="takeoverSkillAssignment"/> is 0 the skill-assignment lines
        /// (UPDATE_METHOD=SKILLSYSTEM / EDIT_PATCH) are dropped too. Pure string filter —
        /// no ROM, no UI.
        /// </summary>
        public static void MargePatch(string custombuild, string currentPatchFileName,
            uint takeoverSkillAssignment)
        {
            var sb = new StringBuilder();
            sb.AppendLine("UPDATE_UNINSTALL:0=" + currentPatchFileName);
            string[] lines = File.ReadAllLines(currentPatchFileName);
            foreach (string line in lines)
            {
                if (line.IndexOf("PATCHED_IF") == 0) continue;
                if (line.IndexOf("BINF:") == 0) continue;
                if (line.IndexOf("BIN:") == 0) continue;
                if (line.IndexOf("AFTER_TRY_EXECUTE:") == 0) continue;
                if (line.IndexOf("NAME") == 0) continue;
                if (line.IndexOf("INFO") == 0) continue;
                if (line.IndexOf("TEXTADV:") == 0) continue;
                if (line.IndexOf("EXTENDS:") == 0) continue;
                if (takeoverSkillAssignment == 0)
                {
                    // Do not carry the parent's skill assignment over.
                    if (line.IndexOf("UPDATE_METHOD=SKILLSYSTEM") == 0) continue;
                    if (line.IndexOf("EDIT_PATCH:") == 0) continue;
                }

                sb.AppendLine(line);
            }

            sb.AppendLine(File.ReadAllText(custombuild));
            File.WriteAllText(custombuild, sb.ToString());
        }

        /// <summary>Copy <c>SkillsTest.sym</c> from the build dir → <c>symbol.sym</c> in the cache.</summary>
        static void CopySYM(string buildDir, string cacheDir)
        {
            if (string.IsNullOrEmpty(buildDir)) return;
            string src = Path.Combine(buildDir, BuiltSymLeafName);
            string dest = Path.Combine(cacheDir, "symbol.sym");
            CopyFileIfExists(src, dest);
        }

        /// <summary>Copy the optional ASMC/skill dump sidecars from the build dir into the cache.</summary>
        static void CopySomeDumpFiles(string buildDir, string cacheDir)
        {
            if (string.IsNullOrEmpty(buildDir)) return;
            CopySomeDumpFile(buildDir, cacheDir, "ASMC_ForgetSkill.dmp", "ASMC_ForgetSkill.bin");
            CopySomeDumpFile(buildDir, cacheDir, "ASMC_HasSkill.dmp", "ASMC_HasSkill.bin");
            CopySomeDumpFile(buildDir, cacheDir, "ASMC_LearnSkill.dmp", "ASMC_LearnSkill.bin");
            CopySomeDumpFile(buildDir, cacheDir, "GetSkills.dmp", "GetSkills.dmp");
            CopySomeDumpFile(buildDir, cacheDir, "nihilTester.dmp", "nihilTester.dmp");
            CopySomeDumpFile(buildDir, cacheDir, "rtextloop.dmp", "rtextloop.dmp");
            CopySomeDumpFile(buildDir, cacheDir, "skillDescGetter.dmp", "skillDescGetter.dmp");
        }

        static void CopySomeDumpFile(string srcdir, string destdir, string srcfilename, string destfilename)
        {
            string src = FindFileOne(srcdir, srcfilename);
            if (src == "") return;
            CopyFileIfExists(src, Path.Combine(destdir, destfilename));
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

        // ===================================================================
        //  File helpers — Core-local ports of the WinForms U.* directory ops
        //  (U.DelTree / U.CopyDirectory / U.DeleteFile / U.CopyFile /
        //  U.FindFileOne live in FEBuilderGBA/U.cs, which is WinForms-only;
        //  these are the headless, no-Log.localization equivalents this tool
        //  needs, mirroring AsmCompileCore's local FindFileOne precedent).
        // ===================================================================

        /// <summary>
        /// Recursively delete <paramref name="dir"/> with a bounded retry (a build tool
        /// may still hold a handle briefly). Returns true when the directory is gone (or
        /// never existed). Mirrors WinForms <c>U.DelTree</c>.
        /// </summary>
        static bool DelTree(string dir, int retry = 10)
        {
            if (!Directory.Exists(dir))
                return true;

            for (int i = 0; i < retry; i++)
            {
                try
                {
                    Directory.Delete(dir, recursive: true);
                    return true;
                }
                catch (Exception e)
                {
                    Log.Error("CustomBuildCore.DelTree: " + e.ToString());
                }
                System.Threading.Thread.Sleep(500);
            }
            return false;
        }

        /// <summary>
        /// Recursively copy <paramref name="sourceDir"/> into <paramref name="destDir"/>
        /// (files overwrite, empty sub-dirs are skipped). Headless port of WinForms
        /// <c>U.CopyDirectory</c> minus the timestamp/attribute copy + localized Log calls.
        /// </summary>
        static void CopyDirectoryShallow(string sourceDir, string destDir)
        {
            if (!Directory.Exists(sourceDir))
                return;

            Directory.CreateDirectory(destDir);

            foreach (string file in Directory.GetFiles(sourceDir))
            {
                string destFile = Path.Combine(destDir, Path.GetFileName(file));
                File.Copy(file, destFile, overwrite: true);
            }

            foreach (string sub in Directory.GetDirectories(sourceDir))
            {
                // Skip empty sub-dirs (matches WF, which short-circuits IsEmptyDirectory).
                if (Directory.GetFiles(sub).Length == 0 &&
                    Directory.GetDirectories(sub).Length == 0)
                    continue;
                CopyDirectoryShallow(sub, Path.Combine(destDir, Path.GetFileName(sub)));
            }
        }

        /// <summary>
        /// Delete every top-level file in <paramref name="dir"/> matching
        /// <paramref name="pattern"/>. Mirrors WinForms <c>U.DeleteFile</c> (top-dir only).
        /// </summary>
        static void DeleteFilesByPattern(string dir, string pattern)
        {
            if (!Directory.Exists(dir))
                return;
            foreach (string file in Directory.GetFiles(dir, pattern, SearchOption.TopDirectoryOnly))
                File.Delete(file);
        }

        /// <summary>Copy <paramref name="src"/>→<paramref name="dest"/> only when the source exists (WF <c>U.CopyFile</c>).</summary>
        static void CopyFileIfExists(string src, string dest)
        {
            if (!File.Exists(src))
                return;
            File.Copy(src, dest, overwrite: true);
        }

        /// <summary>
        /// First file under <paramref name="path"/> (recursive) matching
        /// <paramref name="name"/>, or "" when none. Mirrors WinForms <c>U.FindFileOne</c>
        /// (same shape as <c>AsmCompileCore</c>'s local helper).
        /// </summary>
        static string FindFileOne(string path, string name)
        {
            if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
                return "";
            string[] files = U.Directory_GetFiles_Safe(path, name, SearchOption.AllDirectories);
            return files.Length > 0 ? files[0] : "";
        }
    }
}
