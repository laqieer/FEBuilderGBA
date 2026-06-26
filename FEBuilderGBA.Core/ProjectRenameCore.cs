using System;
using System.Collections.Generic;
using System.IO;

namespace FEBuilderGBA
{
    /// <summary>
    /// Cross-platform project (ROM) rename logic ported from WinForms
    /// <c>ToolChangeProjectnameForm.ChangeName</c> / <c>ChangeEtcDir</c> (#1461).
    ///
    /// The original WinForms tool renamed every file in the ROM's directory
    /// that starts with the old project name (the ROM itself plus all backups
    /// such as <c>rom.bak001.gba</c>) and moved the per-project
    /// <c>config/etc/&lt;title&gt;/</c> directory. The Avalonia port was a
    /// non-functional UI shell — clicking the button only closed the dialog.
    ///
    /// This Core helper does the real work and is filesystem-injectable
    /// (<see cref="IProjectRenameFileSystem"/>) so unit tests can validate the
    /// rename without touching real disk. Plan construction
    /// (<see cref="BuildPlan"/>) is a PURE function.
    /// </summary>
    public static class ProjectRenameCore
    {
        /// <summary>Result of pre-flight validation.</summary>
        public enum ValidateResult
        {
            Ok,
            /// <summary>ROM has unsaved modifications — refuse (WinForms guard).</summary>
            ModifiedRom,
            /// <summary>ROM is a virtual ROM — refuse (WinForms guard).</summary>
            VirtualRom,
            /// <summary>The new name contains characters invalid in a filename.</summary>
            BadFilename,
            /// <summary>New name equals the current name — no-op.</summary>
            SameName,
            /// <summary>The new name is empty / whitespace.</summary>
            EmptyName,
            /// <summary>The ROM has no on-disk filename.</summary>
            NoRomFilename,
        }

        /// <summary>A single file move within the rename plan.</summary>
        public readonly struct FileMove
        {
            public readonly string OldPath;
            public readonly string NewPath;
            public FileMove(string oldPath, string newPath)
            {
                OldPath = oldPath;
                NewPath = newPath;
            }
        }

        /// <summary>
        /// The complete set of operations a rename will perform. Produced by the
        /// PURE <see cref="BuildPlan"/>; consumed by <see cref="ExecutePlan"/>.
        /// </summary>
        public sealed class RenamePlan
        {
            /// <summary>Per-file moves (ROM + backups sharing the old prefix).</summary>
            public List<FileMove> FileMoves { get; } = new List<FileMove>();

            /// <summary>The new full path of the ROM file itself, for reload.</summary>
            public string NewRomPath { get; set; } = string.Empty;

            /// <summary>Current per-project etc directory (may not exist).</summary>
            public string OldEtcDir { get; set; } = string.Empty;

            /// <summary>Destination per-project etc directory.</summary>
            public string NewEtcDir { get; set; } = string.Empty;
        }

        /// <summary>
        /// Filesystem abstraction so the rename can be unit-tested against an
        /// in-memory store. Mirrors only the operations the rename needs.
        /// </summary>
        public interface IProjectRenameFileSystem
        {
            string[] GetFilesTopDirectory(string dir);
            bool FileExists(string path);
            void FileMove(string oldPath, string newPath);
            void FileDelete(string path);
            bool DirectoryExists(string path);
            void DirectoryMove(string oldPath, string newPath);
            void DirectoryDelete(string path);
        }

        /// <summary>
        /// Default <see cref="IProjectRenameFileSystem"/> backed by
        /// <see cref="System.IO"/>. Used by the GUI; tests inject a fake.
        /// </summary>
        public sealed class RealProjectRenameFileSystem : IProjectRenameFileSystem
        {
            public string[] GetFilesTopDirectory(string dir)
                => U.Directory_GetFiles_Safe(dir, "*", SearchOption.TopDirectoryOnly);
            public bool FileExists(string path) => File.Exists(path);
            public void FileMove(string oldPath, string newPath) => File.Move(oldPath, newPath);
            public void FileDelete(string path) => File.Delete(path);
            public bool DirectoryExists(string path) => Directory.Exists(path);
            public void DirectoryMove(string oldPath, string newPath) => Directory.Move(oldPath, newPath);
            public void DirectoryDelete(string path) => Directory.Delete(path, true);
        }

        /// <summary>
        /// Pre-flight validation, mirroring the WinForms guards in
        /// <c>ChangeButton_Click</c>. Pure aside from reading ROM flags.
        /// </summary>
        public static ValidateResult Validate(ROM rom, string oldName, string newName)
        {
            if (rom == null || string.IsNullOrEmpty(rom.Filename))
            {
                return ValidateResult.NoRomFilename;
            }
            if (rom.Modified)
            {
                return ValidateResult.ModifiedRom;
            }
            if (rom.IsVirtualROM)
            {
                return ValidateResult.VirtualRom;
            }
            if (string.IsNullOrWhiteSpace(newName))
            {
                return ValidateResult.EmptyName;
            }
            if (U.IsBadFilename(newName))
            {
                return ValidateResult.BadFilename;
            }
            if (oldName == newName)
            {
                return ValidateResult.SameName;
            }
            return ValidateResult.Ok;
        }

        /// <summary>
        /// Build the rename plan WITHOUT touching disk (apart from listing the
        /// supplied <paramref name="dirFiles"/>, which the caller provides).
        /// Mirrors WinForms <c>ChangeName</c>'s prefix-match loop: only files
        /// whose name (without extension) STARTS WITH <paramref name="oldName"/>
        /// are renamed, preserving the suffix and extension.
        /// </summary>
        /// <param name="romFilename">Full path of the current ROM file.</param>
        /// <param name="oldName">Current project name (filename without extension).</param>
        /// <param name="newName">New project name.</param>
        /// <param name="dirFiles">Files in the ROM's directory (top-level).</param>
        /// <param name="oldEtcDir">Current per-project etc dir (or "").</param>
        /// <param name="newEtcDir">Destination per-project etc dir (or "").</param>
        public static RenamePlan BuildPlan(
            string romFilename,
            string oldName,
            string newName,
            IEnumerable<string> dirFiles,
            string oldEtcDir,
            string newEtcDir)
        {
            RenamePlan plan = new RenamePlan();
            string dir = Path.GetDirectoryName(romFilename) ?? string.Empty;

            if (dirFiles != null)
            {
                foreach (string f in dirFiles)
                {
                    string name = Path.GetFileNameWithoutExtension(f);
                    // WinForms: name.IndexOf(oldName) != 0 → skip (not a prefix).
                    if (name.IndexOf(oldName, StringComparison.Ordinal) != 0)
                    {
                        continue;
                    }
                    string ext = Path.GetExtension(f);
                    string newFilename = newName + name.Substring(oldName.Length);
                    string newPath = Path.Combine(dir, newFilename + ext);
                    plan.FileMoves.Add(new FileMove(f, newPath));
                }
            }

            plan.NewRomPath = Path.Combine(dir, newName + Path.GetExtension(romFilename));
            plan.OldEtcDir = oldEtcDir ?? string.Empty;
            plan.NewEtcDir = newEtcDir ?? string.Empty;
            return plan;
        }

        /// <summary>
        /// Execute a previously built <see cref="RenamePlan"/> via the supplied
        /// filesystem. Mirrors WinForms <c>ChangeName</c> + <c>ChangeEtcDir</c>:
        /// each destination that already exists is deleted first, then the
        /// source is moved. The etc directory is moved last.
        /// </summary>
        public static void ExecutePlan(RenamePlan plan, IProjectRenameFileSystem fs)
        {
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            if (fs == null) throw new ArgumentNullException(nameof(fs));

            foreach (FileMove mv in plan.FileMoves)
            {
                // Delete a stale destination first (WinForms ChangeName), BUT skip
                // the delete on a case-only rename: on a case-insensitive
                // filesystem (Windows / most macOS volumes) NewPath and OldPath then
                // refer to the SAME on-disk file, so deleting NewPath would destroy
                // the source. In that case FileMove performs the case-only rename
                // directly.
                if (!IsCaseOnlyRename(mv.OldPath, mv.NewPath) && fs.FileExists(mv.NewPath))
                {
                    fs.FileDelete(mv.NewPath);
                }
                fs.FileMove(mv.OldPath, mv.NewPath);
            }

            // ---- etc directory move (ChangeEtcDir) ----
            if (!string.IsNullOrEmpty(plan.OldEtcDir)
                && !string.IsNullOrEmpty(plan.NewEtcDir)
                && fs.DirectoryExists(plan.OldEtcDir))
            {
                // Same case-only-rename guard as the file moves above: never
                // delete the destination dir when it is the same on-disk dir as
                // the source under a case-insensitive comparison.
                if (!IsCaseOnlyRename(plan.OldEtcDir, plan.NewEtcDir)
                    && fs.DirectoryExists(plan.NewEtcDir))
                {
                    fs.DirectoryDelete(plan.NewEtcDir);
                }
                fs.DirectoryMove(plan.OldEtcDir, plan.NewEtcDir);
            }
        }

        /// <summary>
        /// True when <paramref name="oldPath"/> and <paramref name="newPath"/>
        /// are the same path ignoring case but differ in their exact characters
        /// — i.e. a case-only rename. On a case-insensitive filesystem the two
        /// paths name the SAME on-disk entry, so a delete-then-move would destroy
        /// the source; the caller must move directly instead.
        /// </summary>
        static bool IsCaseOnlyRename(string oldPath, string newPath)
        {
            return !string.Equals(oldPath, newPath, StringComparison.Ordinal)
                && string.Equals(oldPath, newPath, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Full rename against the live ROM: validates, computes the etc dir
        /// paths, lists the directory, builds and executes the plan, and returns
        /// the new ROM path so the caller can reload. Does NOT reload the ROM
        /// itself (that is a GUI concern).
        /// </summary>
        /// <returns>
        /// The new ROM path on success, or <c>null</c> if validation failed
        /// (inspect <paramref name="result"/>).
        /// </returns>
        public static string Rename(ROM rom, string oldName, string newName,
            IProjectRenameFileSystem fs, out ValidateResult result)
        {
            result = Validate(rom, oldName, newName);
            if (result != ValidateResult.Ok)
            {
                return null;
            }
            fs = fs ?? new RealProjectRenameFileSystem();

            string dir = Path.GetDirectoryName(rom.Filename) ?? string.Empty;
            string[] files = fs.GetFilesTopDirectory(dir);

            string newRomPath = Path.Combine(dir, newName + Path.GetExtension(rom.Filename));

            // Resolve the per-project etc directories the same way WinForms does:
            // ChangeEtcDir uses Path.GetDirectoryName(U.ConfigEtcFilename("flag", ...)).
            string oldEtcDir = Path.GetDirectoryName(U.ConfigEtcFilename("flag", rom)) ?? string.Empty;
            string newEtcDir = Path.GetDirectoryName(U.ConfigEtcFilename("flag", newRomPath)) ?? string.Empty;

            RenamePlan plan = BuildPlan(rom.Filename, oldName, newName, files, oldEtcDir, newEtcDir);
            ExecutePlan(plan, fs);
            return plan.NewRomPath;
        }
    }
}
