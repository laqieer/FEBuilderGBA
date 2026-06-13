// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace FEBuilderGBA
{
    /// <summary>Status of a decomp build attempt.</summary>
    public enum DecompBuildStatus
    {
        /// <summary>Build ran and exited with code 0.</summary>
        Success = 0,
        /// <summary>Build ran but exited non-zero, or process failed to start for a known reason.</summary>
        Failed = 1,
        /// <summary>Project has not opted into FEBuilder-managed builds (no build section).</summary>
        NotOptedIn = 2,
        /// <summary>Build could not start (null project, missing executable, etc.).</summary>
        NotStarted = 3,
        /// <summary>Build ran but was killed by the timeout.</summary>
        TimedOut = 4,
    }

    /// <summary>Result of a <see cref="DecompBuildCore.Build"/> call.</summary>
    public sealed class DecompBuildResult
    {
        /// <summary>Build status code.</summary>
        public DecompBuildStatus Status { get; set; }

        /// <summary>True when Status is Success.</summary>
        public bool Success => Status == DecompBuildStatus.Success;

        /// <summary>Raw process result (meaningful when Status is not NotOptedIn/NotStarted).</summary>
        public ProcessRunResult Run { get; set; }

        /// <summary>The command that was (or would have been) run.</summary>
        public string Command { get; set; } = "";

        /// <summary>The args that were (or would have been) passed.</summary>
        public string[] Args { get; set; } = Array.Empty<string>();

        /// <summary>Human-readable description of what happened.</summary>
        public string Message { get; set; } = "";
    }

    /// <summary>
    /// Core build + reload helpers for decomp projects (#1134). Never throws.
    /// All public methods are fully guarded: null project, missing files, and
    /// process errors all return error results rather than throwing.
    /// </summary>
    public static class DecompBuildCore
    {
        /// <summary>Default build command used when the manifest has no explicit command.</summary>
        public const string DefaultCommand = "make";

        /// <summary>
        /// Returns the effective command line for display. Empty string when the project
        /// has not opted into builds. Pure / never throws.
        /// </summary>
        public static string GetEffectiveCommandLine(DecompProject project)
        {
            try
            {
                if (project == null || !project.IsBuildEnabled)
                    return "";
                string cmd = !string.IsNullOrEmpty(project.BuildCommand)
                    ? project.BuildCommand
                    : DefaultCommand;
                string[] a = project.BuildArgs;
                return a.Length > 0
                    ? cmd + " " + string.Join(" ", a)
                    : cmd;
            }
            catch
            {
                return "";
            }
        }

        /// <summary>
        /// Run the project build. Never throws. Null project → NotStarted.
        /// Project not opted in → NotOptedIn (nothing runs).
        /// </summary>
        public static DecompBuildResult Build(DecompProject project, int timeoutMs)
        {
            try
            {
                if (project == null)
                    return new DecompBuildResult
                    {
                        Status = DecompBuildStatus.NotStarted,
                        Message = "Project is null."
                    };

                if (!project.IsBuildEnabled)
                    return new DecompBuildResult
                    {
                        Status = DecompBuildStatus.NotOptedIn,
                        Message = "Project has not opted into FEBuilder-managed builds; add a build section to febuilder.project.json.",
                        Command = "",
                        Args = Array.Empty<string>(),
                    };

                string command = !string.IsNullOrEmpty(project.BuildCommand)
                    ? project.BuildCommand
                    : DefaultCommand;
                string[] args = project.BuildArgs;

                var run = ProcessRunnerCore.Run(command, args, project.ProjectRoot, timeoutMs);

                DecompBuildStatus status;
                string message;
                if (!run.Started)
                {
                    status = DecompBuildStatus.NotStarted;
                    message = run.ErrorMessage;
                }
                else if (run.TimedOut)
                {
                    status = DecompBuildStatus.TimedOut;
                    message = run.ErrorMessage;
                }
                else if (run.ExitCode != 0)
                {
                    status = DecompBuildStatus.Failed;
                    message = $"Build exited with code {run.ExitCode}.";
                }
                else
                {
                    status = DecompBuildStatus.Success;
                    message = "Build succeeded.";
                }

                return new DecompBuildResult
                {
                    Status = status,
                    Run = run,
                    Command = command,
                    Args = args,
                    Message = message,
                };
            }
            catch (Exception ex)
            {
                return new DecompBuildResult
                {
                    Status = DecompBuildStatus.NotStarted,
                    Message = $"Unexpected error during build: {ex.Message}",
                };
            }
        }

        /// <summary>
        /// Reload the built ROM after a successful build. Sets BuiltRomPath and clears
        /// NeedsRebuild only when the load seam succeeds. Never throws.
        /// </summary>
        /// <param name="project">The decomp project to reload.</param>
        /// <param name="loadSeam">
        /// Delegate that loads a ROM: <c>(romPath, forceVersion) => bool</c>.
        /// Returns true on success.
        /// </param>
        public static DecompResolveStatus ReloadBuiltRom(
            DecompProject project,
            Func<string, string, bool> loadSeam)
        {
            try
            {
                if (project == null || loadSeam == null)
                    return DecompResolveStatus.NotProject;

                var resolved = DecompProjectDetector.ResolveBuiltRom(project.ProjectRoot, project);
                if (resolved.Status != DecompResolveStatus.Ok)
                    return resolved.Status;

                // Try the load seam BEFORE mutating project state. Only when the
                // seam succeeds do we commit BuiltRomPath + clear NeedsRebuild;
                // on failure the project is left exactly as it was so a stale
                // built ROM is never advertised as the current preview.
                bool ok = loadSeam(resolved.Path, project.ForceVersion);
                if (ok)
                {
                    project.BuiltRomPath = resolved.Path;
                    CoreState.DecompProject = project;
                    project.NeedsRebuild = false;
                    return DecompResolveStatus.Ok;
                }
                else
                {
                    // Load failed — leave BuiltRomPath and NeedsRebuild intact
                    return DecompResolveStatus.NotBuilt;
                }
            }
            catch
            {
                return DecompResolveStatus.NotBuilt;
            }
        }

        /// <summary>
        /// True when the built ROM might be out of date: NeedsRebuild flag is set, or
        /// any declared source file has a newer mtime than the built ROM. Never throws.
        /// </summary>
        public static bool IsStale(DecompProject project)
        {
            try
            {
                if (project == null)
                    return false;

                if (project.NeedsRebuild)
                    return true;

                if (string.IsNullOrEmpty(project.BuiltRomPath)
                    || !File.Exists(project.BuiltRomPath))
                    return false;

                DateTime builtTime = File.GetLastWriteTimeUtc(project.BuiltRomPath);

                // Check declared table source files
                if (project.Manifest?.TablesList != null)
                {
                    foreach (var entry in project.Manifest.TablesList)
                    {
                        try
                        {
                            if (entry == null || string.IsNullOrEmpty(entry.SourceFile))
                                continue;
                            string resolved = DecompProjectDetector.ResolveArtifact(
                                project.ProjectRoot, entry.SourceFile);
                            if (resolved != null && File.Exists(resolved))
                            {
                                if (File.GetLastWriteTimeUtc(resolved) > builtTime)
                                    return true;
                            }
                        }
                        catch { }
                    }
                }

                // Check asset output paths from the manifest assets JsonElement
                try
                {
                    if (project.Manifest?.Assets is JsonElement assets)
                    {
                        EnumerateAssetPaths(assets, project.ProjectRoot, builtTime, out bool assetStale);
                        if (assetStale)
                            return true;
                    }
                }
                catch { }

                return false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Walk the assets JsonElement for string paths and check if any is newer than builtTime.
        /// </summary>
        static void EnumerateAssetPaths(JsonElement el, string projectRoot, DateTime builtTime, out bool stale)
        {
            stale = false;
            try
            {
                if (el.ValueKind == JsonValueKind.String)
                {
                    TryCheckPath(el.GetString(), projectRoot, builtTime, ref stale);
                }
                else if (el.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in el.EnumerateArray())
                    {
                        if (stale) return;
                        try
                        {
                            if (item.ValueKind == JsonValueKind.String)
                                TryCheckPath(item.GetString(), projectRoot, builtTime, ref stale);
                        }
                        catch { }
                    }
                }
                else if (el.ValueKind == JsonValueKind.Object)
                {
                    foreach (var prop in el.EnumerateObject())
                    {
                        if (stale) return;
                        try
                        {
                            EnumerateAssetPaths(prop.Value, projectRoot, builtTime, out bool inner);
                            if (inner) { stale = true; return; }
                        }
                        catch { }
                    }
                }
            }
            catch { }
        }

        static void TryCheckPath(string relativePath, string projectRoot, DateTime builtTime, ref bool stale)
        {
            try
            {
                if (string.IsNullOrEmpty(relativePath)) return;
                string resolved = DecompProjectDetector.ResolveArtifact(projectRoot, relativePath);
                if (resolved != null && File.Exists(resolved)
                    && File.GetLastWriteTimeUtc(resolved) > builtTime)
                {
                    stale = true;
                }
            }
            catch { }
        }
    }
}
