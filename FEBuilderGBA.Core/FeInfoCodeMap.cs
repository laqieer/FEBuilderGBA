using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;

namespace FEBuilderGBA
{
    /// <summary>
    /// Parser and path resolver for fe-info code.json function symbols.
    /// fe-info is BSD-3-Clause licensed: https://github.com/laqieer/fe-info
    /// </summary>
    public static class FeInfoCodeMap
    {
        public static Dictionary<uint, AsmMapSt> Parse(string codeJsonText, string region)
        {
            var result = new Dictionary<uint, AsmMapSt>();
            try
            {
                if (string.IsNullOrWhiteSpace(codeJsonText))
                    return result;

                using JsonDocument doc = JsonDocument.Parse(codeJsonText);
                JsonElement root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Array)
                    return result;

                foreach (JsonElement entry in root.EnumerateArray())
                {
                    if (entry.ValueKind != JsonValueKind.Object)
                        continue;
                    if (!entry.TryGetProperty("addr", out JsonElement addrElement))
                        continue;
                    if (!entry.TryGetProperty("label", out JsonElement labelElement)
                        || labelElement.ValueKind != JsonValueKind.String)
                        continue;

                    string addrStr = ResolveRegionValue(addrElement, region);
                    string label = labelElement.GetString() ?? "";
                    if (string.IsNullOrWhiteSpace(addrStr) || string.IsNullOrWhiteSpace(label))
                        continue;

                    uint addr = U.atoh(addrStr);
                    if (addr == 0)
                        continue;

                    result[addr] = new AsmMapSt
                    {
                        Name = label,
                        ResultAndArgs = BuildResultAndArgs(entry),
                    };
                }
            }
            catch
            {
                return new Dictionary<uint, AsmMapSt>();
            }

            return result;
        }

        public static string ResolveCodeJsonPath(ROM rom, string baseDir, out string region)
        {
            region = null;
            try
            {
                if (rom?.RomInfo == null || string.IsNullOrEmpty(baseDir))
                    return null;

                string game;
                if (rom.RomInfo is ROMFE6JP)
                {
                    game = "fe6";
                    region = "J";
                }
                else if (rom.RomInfo is ROMFE8U)
                {
                    game = "fe8";
                    region = "U";
                }
                else if (rom.RomInfo is ROMFE8JP)
                {
                    game = "fe8";
                    region = "J";
                }
                else
                {
                    return null;
                }

                string path = Path.Combine(baseDir, "resources", "fe-info", "json", game, "code.json");
                return File.Exists(path) ? path : null;
            }
            catch
            {
                region = null;
                return null;
            }
        }

        static string ResolveRegionValue(JsonElement element, string region)
        {
            if (element.ValueKind == JsonValueKind.String)
                return element.GetString();

            if (element.ValueKind != JsonValueKind.Object)
                return null;

            if (!string.IsNullOrEmpty(region)
                && element.TryGetProperty(region, out JsonElement regionElement)
                && regionElement.ValueKind == JsonValueKind.String)
            {
                return regionElement.GetString();
            }

            foreach (JsonProperty property in element.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.String)
                    return property.Value.GetString();
            }

            return null;
        }

        static string BuildResultAndArgs(JsonElement entry)
        {
            var sb = new StringBuilder();

            if (entry.TryGetProperty("return", out JsonElement returnElement)
                && returnElement.ValueKind == JsonValueKind.Object
                && returnElement.TryGetProperty("type", out JsonElement returnTypeElement)
                && returnTypeElement.ValueKind == JsonValueKind.String)
            {
                string returnType = returnTypeElement.GetString();
                if (!string.IsNullOrWhiteSpace(returnType))
                    sb.Append("RET=").Append(returnType);
            }

            if (entry.TryGetProperty("params", out JsonElement paramsElement)
                && paramsElement.ValueKind == JsonValueKind.Array)
            {
                int index = 0;
                foreach (JsonElement param in paramsElement.EnumerateArray())
                {
                    if (param.ValueKind != JsonValueKind.Object
                        || !param.TryGetProperty("type", out JsonElement paramTypeElement)
                        || paramTypeElement.ValueKind != JsonValueKind.String)
                    {
                        index++;
                        continue;
                    }

                    string paramType = paramTypeElement.GetString();
                    if (!string.IsNullOrWhiteSpace(paramType))
                    {
                        if (sb.Length > 0)
                            sb.Append(", ");
                        sb.Append("r").Append(index).Append("=").Append(paramType);
                    }

                    index++;
                }
            }

            return sb.ToString();
        }
    }
}
