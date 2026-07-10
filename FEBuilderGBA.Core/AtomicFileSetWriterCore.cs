using System;
using System.Collections.Generic;
using System.IO;

namespace FEBuilderGBA
{
    /// <summary>
    /// Writes a related set of files transactionally so aliased destinations cannot overwrite
    /// one another while leaving a success-shaped partial result.
    /// </summary>
    public static class AtomicFileSetWriterCore
    {
        public sealed class FileOutput
        {
            public string Path { get; }
            public byte[] Data { get; }

            public FileOutput(string path, byte[] data)
            {
                Path = path;
                Data = data;
            }
        }

        sealed class Entry
        {
            public string Path;
            public byte[] Data;
            public string TempPath;
            public string BackupPath;
            public bool Committed;
        }

        public static void WriteAll(IReadOnlyList<FileOutput> outputs)
        {
            if (outputs == null || outputs.Count == 0)
                throw new ArgumentException("At least one output is required.", nameof(outputs));

            var entries = new List<Entry>(outputs.Count);
            var lexicalPaths = new HashSet<string>(StringComparer.Ordinal);
            foreach (FileOutput output in outputs)
            {
                if (output == null || string.IsNullOrWhiteSpace(output.Path))
                    throw new ArgumentException("Every output must have a path.", nameof(outputs));
                if (output.Data == null)
                    throw new ArgumentException("Every output must have data.", nameof(outputs));

                string fullPath = System.IO.Path.GetFullPath(output.Path);
                if (!lexicalPaths.Add(fullPath))
                    throw new IOException("Output paths resolve to the same file: " + fullPath);

                entries.Add(new Entry
                {
                    Path = fullPath,
                    Data = output.Data,
                });
            }

            try
            {
                foreach (Entry entry in entries)
                {
                    string directory = System.IO.Path.GetDirectoryName(entry.Path);
                    if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
                        throw new DirectoryNotFoundException("Output directory not found: " + directory);

                    entry.TempPath = MakeSiblingPath(entry.Path, ".tmp");
                    using var stream = new FileStream(entry.TempPath, FileMode.CreateNew,
                        FileAccess.Write, FileShare.None);
                    stream.Write(entry.Data, 0, entry.Data.Length);
                    stream.Flush(flushToDisk: true);
                }

                foreach (Entry entry in entries)
                {
                    if (!PathExists(entry.Path))
                        continue;
                    entry.BackupPath = MakeSiblingPath(entry.Path, ".bak");
                    File.Move(entry.Path, entry.BackupPath, overwrite: false);
                }

                foreach (Entry entry in entries)
                {
                    try
                    {
                        File.Move(entry.TempPath, entry.Path, overwrite: false);
                        entry.Committed = true;
                        entry.TempPath = null;
                    }
                    catch (IOException ex) when (PathExists(entry.Path))
                    {
                        throw new IOException(
                            "Output paths resolve to the same filesystem entry: " + entry.Path, ex);
                    }
                }
            }
            catch (Exception ex)
            {
                List<Exception> rollbackErrors = Rollback(entries);
                if (rollbackErrors.Count > 0)
                {
                    rollbackErrors.Insert(0, ex);
                    throw new IOException("Output transaction failed and rollback was incomplete.",
                        new AggregateException(rollbackErrors));
                }
                throw;
            }

            foreach (Entry entry in entries)
            {
                if (string.IsNullOrEmpty(entry.BackupPath))
                    continue;
                try
                {
                    File.Delete(entry.BackupPath);
                    entry.BackupPath = null;
                }
                catch (IOException ex)
                {
                    Log.Error($"Output transaction committed, but backup cleanup failed for '{entry.BackupPath}': {ex}");
                }
                catch (UnauthorizedAccessException ex)
                {
                    Log.Error($"Output transaction committed, but backup cleanup failed for '{entry.BackupPath}': {ex}");
                }
            }
        }

        static List<Exception> Rollback(List<Entry> entries)
        {
            var errors = new List<Exception>();
            for (int i = entries.Count - 1; i >= 0; i--)
            {
                Entry entry = entries[i];
                if (!entry.Committed)
                    continue;
                try
                {
                    if (PathExists(entry.Path))
                        File.Delete(entry.Path);
                    entry.Committed = false;
                }
                catch (Exception ex)
                {
                    errors.Add(ex);
                }
            }

            for (int i = entries.Count - 1; i >= 0; i--)
            {
                Entry entry = entries[i];
                if (string.IsNullOrEmpty(entry.BackupPath))
                    continue;
                try
                {
                    if (PathExists(entry.BackupPath))
                        File.Move(entry.BackupPath, entry.Path, overwrite: false);
                    entry.BackupPath = null;
                }
                catch (Exception ex)
                {
                    errors.Add(ex);
                }
            }

            foreach (Entry entry in entries)
            {
                if (string.IsNullOrEmpty(entry.TempPath))
                    continue;
                try
                {
                    if (PathExists(entry.TempPath))
                        File.Delete(entry.TempPath);
                    entry.TempPath = null;
                }
                catch (Exception ex)
                {
                    errors.Add(ex);
                }
            }
            return errors;
        }

        static string MakeSiblingPath(string path, string suffix)
        {
            string directory = System.IO.Path.GetDirectoryName(path);
            string name = System.IO.Path.GetFileName(path);
            string candidate;
            do
            {
                candidate = System.IO.Path.Combine(directory,
                    $".{name}.{Guid.NewGuid():N}{suffix}");
            }
            while (PathExists(candidate));
            return candidate;
        }

        static bool PathExists(string path)
        {
            try
            {
                File.GetAttributes(path);
                return true;
            }
            catch (FileNotFoundException)
            {
                return false;
            }
            catch (DirectoryNotFoundException)
            {
                return false;
            }
        }
    }
}
