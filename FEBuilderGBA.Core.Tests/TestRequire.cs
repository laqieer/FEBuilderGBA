using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using FEBuilderGBA;

namespace FEBuilderGBA.Core.Tests;

internal static class TestRequire
{
    public static T NotNull<T>(T? value, string? name) where T : class
    {
        return value ?? throw new InvalidOperationException($"{name} was unexpectedly null.");
    }

    public static T HasValue<T>(T? value, string? name) where T : struct
    {
        return value ?? throw new InvalidOperationException($"{name} was unexpectedly null.");
    }

    public static string DirectoryName(string path)
    {
        return Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException($"Path '{path}' does not have a directory name.");
    }

    public static string PathRoot(string path)
    {
        return Path.GetPathRoot(path)
            ?? throw new InvalidOperationException($"Path '{path}' does not have a root.");
    }

    public static JsonObject Object(JsonNode? node, string name)
    {
        return node as JsonObject
            ?? throw new InvalidOperationException($"JSON node '{name}' was not an object.");
    }

    public static JsonArray Array(JsonNode? node, string name)
    {
        return node as JsonArray
            ?? throw new InvalidOperationException($"JSON node '{name}' was not an array.");
    }

    public static T JsonValue<T>(JsonNode? node, string name)
    {
        return NotNull(node, name).GetValue<T>();
    }

    public static string JsonString(JsonElement element, string name)
    {
        return element.GetString()
            ?? throw new InvalidOperationException($"JSON string '{name}' was unexpectedly null.");
    }

    public static void RestoreImageService(IImageService? service)
    {
        NotNull(typeof(CoreState).GetProperty(nameof(CoreState.ImageService)), nameof(CoreState.ImageService))
            .SetValue(null, service);
    }

    public static void RestoreRom(ROM? rom)
    {
        NotNull(typeof(CoreState).GetProperty(nameof(CoreState.ROM)), nameof(CoreState.ROM))
            .SetValue(null, rom);
    }
}
