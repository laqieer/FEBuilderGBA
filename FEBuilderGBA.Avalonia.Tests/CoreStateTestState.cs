using System;
using FEBuilderGBA;

namespace FEBuilderGBA.Avalonia.Tests;

internal static class CoreStateTestState
{
    public static void RestoreRom(ROM? value) => CoreState.ROM = value ?? null!;
    public static void RestoreUndo(Undo? value) => CoreState.Undo = value ?? null!;
    public static void RestoreServices(IAppServices? value) => CoreState.Services = value ?? null!;
    public static void RestoreCommentCache(IEtcCache? value) => CoreState.CommentCache = value ?? null!;
    public static void RestoreLintCache(IEtcCache? value) => CoreState.LintCache = value ?? null!;
    public static void RestoreWorkSupportCache(IEtcCache? value) => CoreState.WorkSupportCache = value ?? null!;
    public static void RestoreSystemTextEncoder(ISystemTextEncoder? value) => CoreState.SystemTextEncoder = value ?? null!;
    public static void RestoreConfig(Config? value) => CoreState.Config = value ?? null!;
    public static void RestoreBaseDirectory(string? value) => CoreState.BaseDirectory = value ?? null!;
    public static void RestoreLanguage(string? value) => CoreState.Language = value ?? null!;
    public static void RestoreEventScript(EventScript? value) => CoreState.EventScript = value ?? null!;
    public static void RestoreProcsScript(EventScript? value) => CoreState.ProcsScript = value ?? null!;
    public static void RestoreAIScript(EventScript? value) => CoreState.AIScript = value ?? null!;
    public static void RestoreDecompProject(DecompProject? value) => CoreState.DecompProject = value ?? null!;
    public static void RestoreAppendBinaryData(Func<byte[], Undo.UndoData, uint>? value) => CoreState.AppendBinaryData = value ?? null!;

    public static void ClearRom() => CoreState.ROM = null!;
    public static void ClearEventScript() => CoreState.EventScript = null!;
    public static void ClearDecompProject() => CoreState.DecompProject = null!;
    public static void ClearBaseDirectory() => CoreState.BaseDirectory = null!;
    public static void ClearAppendBinaryData() => CoreState.AppendBinaryData = null!;
}
