using System;
using FEBuilderGBA;

namespace FEBuilderGBA.Avalonia.Tests;

internal static class CoreStateTestState
{
    public static void RestoreRom(ROM? value) => CoreState.ROM = value;
    public static void RestoreUndo(Undo? value) => CoreState.Undo = value;
    public static void RestoreServices(IAppServices? value) => CoreState.Services = value;
    public static void RestoreCommentCache(IEtcCache? value) => CoreState.CommentCache = value;
    public static void RestoreLintCache(IEtcCache? value) => CoreState.LintCache = value;
    public static void RestoreWorkSupportCache(IEtcCache? value) => CoreState.WorkSupportCache = value;
    public static void RestoreSystemTextEncoder(ISystemTextEncoder? value) => CoreState.SystemTextEncoder = value;
    public static void RestoreConfig(Config? value) => CoreState.Config = value;
    public static void RestoreBaseDirectory(string? value) => CoreState.BaseDirectory = value;
    public static void RestoreLanguage(string? value) => CoreState.Language = value;
    public static void RestoreEventScript(EventScript? value) => CoreState.EventScript = value;
    public static void RestoreProcsScript(EventScript? value) => CoreState.ProcsScript = value;
    public static void RestoreAIScript(EventScript? value) => CoreState.AIScript = value;
    public static void RestoreDecompProject(DecompProject? value) => CoreState.DecompProject = value;
    public static void RestoreAppendBinaryData(Func<byte[], Undo.UndoData, uint>? value) => CoreState.AppendBinaryData = value;
    public static void RestoreImageService(IImageService? value) => CoreState.ImageService = value;
    public static void RestoreResourceCache(object? value) => CoreState.ResourceCache = value;

    public static void ClearRom() => CoreState.ROM = null;
    public static void ClearEventScript() => CoreState.EventScript = null;
    public static void ClearDecompProject() => CoreState.DecompProject = null;
    public static void ClearBaseDirectory() => CoreState.BaseDirectory = null;
    public static void ClearAppendBinaryData() => CoreState.AppendBinaryData = null;
    public static void ClearImageService() => CoreState.ImageService = null;
    public static void ClearResourceCache() => CoreState.ResourceCache = null;
}
