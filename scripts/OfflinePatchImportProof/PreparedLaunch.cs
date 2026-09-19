using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json;

public static class PreparedJson
{
    public static IDictionary Parse(byte[] bytes)
    {
        if (bytes == null || bytes.Length == 0 || bytes.Length > 4194304)
            throw new InvalidDataException("prepared-json-bound");
        string text = new UTF8Encoding(false, true).GetString(bytes);
        if (text[0] == '\ufeff') throw new InvalidDataException("prepared-json-bom");
        using var document = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = 32 });
        int nodes = 0;
        return ConvertValue(document.RootElement, 0, ref nodes) as IDictionary ??
            throw new InvalidDataException("prepared-json-object");
    }

    static object ConvertValue(JsonElement value, int depth, ref int nodes)
    {
        if (depth > 32 || ++nodes > 50000) throw new InvalidDataException("prepared-json-structure");
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                var result = new Dictionary<string, object>(StringComparer.Ordinal);
                var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var property in value.EnumerateObject())
                {
                    if (!names.Add(property.Name)) throw new InvalidDataException("prepared-json-duplicate");
                    result.Add(property.Name, ConvertValue(property.Value, depth + 1, ref nodes));
                }
                return result;
            case JsonValueKind.Array:
                var list = new List<object>();
                foreach (var item in value.EnumerateArray()) list.Add(ConvertValue(item, depth + 1, ref nodes));
                return list.ToArray();
            case JsonValueKind.String: return value.GetString();
            case JsonValueKind.Number:
                if (!value.TryGetInt64(out long integer)) throw new InvalidDataException("prepared-json-integer");
                return integer;
            case JsonValueKind.True: return true;
            case JsonValueKind.False: return false;
            case JsonValueKind.Null: return null;
            default: throw new InvalidDataException("prepared-json-value");
        }
    }
}

public sealed class PreparedContentLease : IDisposable
{
    readonly List<FileStream> held = new List<FileStream>();
    public long Bytes { get; private set; }
    public int Files { get; private set; }

    public static void ValidateManifest(IDictionary inventory, IDictionary bundle)
    {
        if (inventory.Count != 2 || !inventory.Contains("files") || !inventory.Contains("directories"))
            throw new InvalidDataException("prepared-inventory-shape");
        var expected = new Dictionary<string, IDictionary>(StringComparer.Ordinal);
        foreach (IDictionary row in (IEnumerable)bundle["files"])
        {
            string path = (string)row["path"];
            if (path.StartsWith(@"app\", StringComparison.Ordinal) || path.StartsWith(@"fixtures\", StringComparison.Ordinal))
                expected.Add(path, row);
            if (expected.Count > 10000) throw new InvalidDataException("prepared-inventory-bound");
        }
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (IDictionary row in (IEnumerable)inventory["files"])
        {
            if (row.Count != 3 || !row.Contains("path") || !row.Contains("bytes") || !row.Contains("sha256") ||
                !(row["path"] is string path) || !(row["sha256"] is string hash) ||
                (!(row["bytes"] is int) && !(row["bytes"] is long)) || !PublicResource(path, true) || !seen.Add(path))
                throw new InvalidDataException("prepared-inventory-row");
            long bytes = Convert.ToInt64(row["bytes"]);
            if (path == "empty-git.config")
            {
                if (bytes != 0 || hash != "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855")
                    throw new InvalidDataException("prepared-empty-config");
            }
            else if (!expected.TryGetValue(path, out var original) || bytes != Convert.ToInt64(original["bytes"]) ||
                hash != (string)original["sha256"])
                throw new InvalidDataException("prepared-bundle-content");
        }
        if (seen.Count != expected.Count + 1 || !seen.Contains("empty-git.config"))
            throw new InvalidDataException("prepared-inventory-incomplete");
    }

    public static void Deadline(long entryTicks)
    {
        long now = Stopwatch.GetTimestamp();
        if (entryTicks <= 0 || now < entryTicks ||
            (now - entryTicks) * 1000.0 / Stopwatch.Frequency >= 5000)
            throw new InvalidOperationException("prepared-activation-deadline");
    }

    public static void Plain(string path)
    {
        if (!Path.IsPathFullyQualified(path) || Path.GetFullPath(path) != path ||
            path.StartsWith(@"\\", StringComparison.Ordinal))
            throw new InvalidDataException("prepared-path");
        for (string p = path; !string.IsNullOrEmpty(p); p = Path.GetDirectoryName(p))
            if ((File.GetAttributes(p) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("prepared-reparse");
    }

    public static bool PublicResource(string relative, bool syntheticFixture = false)
    {
        string value = relative.Replace('/', '\\');
        foreach (string part in value.Split('\\'))
        {
            if (part.Length == 0 || part == "." || part == ".." ||
                part.EndsWith(".", StringComparison.Ordinal) || part.EndsWith(" ", StringComparison.Ordinal) ||
                part.IndexOfAny(new[] { ':', '*', '?', '"', '<', '>', '|' }) >= 0)
                return false;
            foreach (char c in part) if (char.IsControl(c)) return false;
            if (Regex.IsMatch(part.Split('.')[0], "^(?i:CON|PRN|AUX|NUL|CONIN\\$|CONOUT\\$|COM[0-9]|LPT[0-9])$")) return false;
            if (part.Equals("log", StringComparison.OrdinalIgnoreCase) ||
                part.Equals("logs", StringComparison.OrdinalIgnoreCase) ||
                part.Equals("generated-core-suite-log-preserved.txt", StringComparison.OrdinalIgnoreCase))
                return false;
        }
        string extension = Path.GetExtension(value).ToLowerInvariant();
        if (extension == ".gba" || extension == ".gb" || extension == ".gbc" ||
            extension == ".nds" || extension == ".rom")
            return syntheticFixture && value == @"fixtures\zipdb-proof.gba";
        return true;
    }

    public static PreparedContentLease Open(string root, IDictionary inventory, long entryTicks)
    {
        var result = new PreparedContentLease();
        try { result.Admit(root, inventory, entryTicks); return result; }
        catch { result.Dispose(); throw; }
    }

    void Admit(string root, IDictionary inventory, long ticks)
    {
        Deadline(ticks);
        Plain(root);
        var expected = new Dictionary<string, IDictionary>(StringComparer.OrdinalIgnoreCase);
        var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (IDictionary row in (IEnumerable)inventory["files"])
        {
            string relative = (string)row["path"];
            if (!PublicResource(relative, true) || !expected.TryAdd(relative, row) || expected.Count > 10000)
                throw new InvalidDataException("prepared-inventory");
        }
        foreach (string relative in (IEnumerable)inventory["directories"])
        {
            if (!PublicResource(relative) || !directories.Add(relative) || directories.Count > 20000)
                throw new InvalidDataException("prepared-directories");
        }
        var pending = new Stack<string>();
        var buffer = new byte[1048576];
        pending.Push(root);
        int actualDirectories = 0;
        while (pending.Count != 0)
        {
            Deadline(ticks);
            foreach (string path in Directory.EnumerateFileSystemEntries(pending.Pop()))
            {
                Deadline(ticks);
                string relative = Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '\\');
                if (!PublicResource(relative, true)) throw new InvalidDataException("prepared-private-file");
                var attributes = File.GetAttributes(path);
                if ((attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("prepared-reparse");
                if ((attributes & FileAttributes.Directory) != 0)
                {
                    if (!directories.Contains(relative)) throw new InvalidDataException("prepared-extra-directory");
                    actualDirectories++;
                    pending.Push(path);
                    continue;
                }
                if (!expected.TryGetValue(relative, out var row)) throw new InvalidDataException("prepared-extra-file");
                if (!(row["bytes"] is int) && !(row["bytes"] is long))
                    throw new InvalidDataException("prepared-file-type");
                long size = Convert.ToInt64(row["bytes"]);
                string sha = (string)row["sha256"];
                if (size < 0 || size > 268435456 || sha.Length != 64)
                    throw new InvalidDataException("prepared-file-bound");
                var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                    1048576, FileOptions.SequentialScan);
                try
                {
                    if (stream.Length != size) throw new InvalidDataException("prepared-size");
                    using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                    int read;
                    while ((read = stream.Read(buffer, 0, buffer.Length)) != 0)
                    {
                        Deadline(ticks);
                        hash.AppendData(buffer, 0, read);
                    }
                    if (Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant() != sha)
                        throw new InvalidDataException("prepared-content");
                    Bytes += size;
                    if (Bytes > 2147483648L) throw new InvalidDataException("prepared-total");
                    Files++;
                    // The application persists its ordinary configuration; never lease it through startup.
                    if (relative == @"app\config\config.xml") stream.Dispose();
                    else { held.Add(stream); stream = null; }
                }
                finally { stream?.Dispose(); }
            }
        }
        if (Files != expected.Count || actualDirectories != directories.Count)
            throw new InvalidDataException("prepared-missing-file");
        Deadline(ticks);
    }

    public void Dispose()
    {
        foreach (var stream in held) stream.Dispose();
        held.Clear();
    }
}

public static class PreparedAttempt
{
    public static bool ObserveAndCommit(bool ready, string commitment, string identity, long entryTicks)
        => Transition(() => ready, commitment, identity, entryTicks);

    internal static bool Transition(Func<bool> ready, string commitment, string identity, long entryTicks)
    {
        PreparedContentLease.Deadline(entryTicks);
        if (!ready()) return false;
        PreparedContentLease.Deadline(entryTicks);
        CommitAttempt(commitment, identity);
        return true;
    }

    static void CommitAttempt(string path, string identity)
    {
        PreparedContentLease.Plain(Path.GetDirectoryName(path));
        byte[] bytes = new UTF8Encoding(false, true).GetBytes(identity);
        if (bytes.Length == 0 || bytes.Length > 4096) throw new InvalidDataException("prepared-commit-bound");
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        stream.Write(bytes);
        stream.Flush(true);
    }
}
