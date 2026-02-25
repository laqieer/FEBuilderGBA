using System;
using System.Collections.Generic;
using System.IO;
using System.Diagnostics;
using System.Text;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Writers;

namespace FEBuilderGBA
{
    public class ArchSevenZip
    {
        /// <summary>
        /// Progress callback for extraction
        /// </summary>
        public delegate void ProgressCallback(int currentEntry, int totalEntries, string currentFile, TimeSpan elapsed, TimeSpan estimated);

        /// <summary>
        /// Extract an archive to a directory using SharpCompress
        /// Supports 7z, zip, rar, tar, and other formats
        /// </summary>
        public static string Extract(string archiveFile, string dir, bool isHide, ProgressCallback progressCallback = null)
        {
            try
            {
                return ExtractLow(archiveFile, dir, isHide, progressCallback);
            }
            catch (Exception e)
            {
                Debug.Assert(false);
                return R.Error("7z解凍中にエラーが発生しました。\r\nターゲットファイル:{0}\r\n{1}", archiveFile, e.ToString());
            }
        }

        static string ExtractLow(string archiveFile, string dir, bool isHide, ProgressCallback progressCallback)
        {
            try
            {
                if (!File.Exists(archiveFile))
                {
                    return $"Archive file not found: {archiveFile}";
                }

                // Ensure output directory exists
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                // Use ArchiveFactory which supports 7z, zip, rar, tar, etc.
                using (var archive = ArchiveFactory.Open(archiveFile))
                {
                    var extractOptions = new ExtractionOptions
                    {
                        ExtractFullPath = true,
                        Overwrite = true
                    };

                    // First count total entries for progress reporting
                    int totalEntries = 0;
                    foreach (var entry in archive.Entries)
                    {
                        if (!entry.IsDirectory)
                        {
                            totalEntries++;
                        }
                    }

                    // Extract with progress reporting
                    int currentEntry = 0;
                    var stopwatch = Stopwatch.StartNew();

                    foreach (var entry in archive.Entries)
                    {
                        if (!entry.IsDirectory)
                        {
                            currentEntry++;

                            // Calculate progress
                            TimeSpan elapsed = stopwatch.Elapsed;
                            TimeSpan estimated = TimeSpan.Zero;
                            if (currentEntry > 0)
                            {
                                double avgTimePerEntry = elapsed.TotalSeconds / currentEntry;
                                int remainingEntries = totalEntries - currentEntry;
                                estimated = TimeSpan.FromSeconds(avgTimePerEntry * remainingEntries);
                            }

                            // Report progress
                            if (progressCallback != null)
                            {
                                string fileName = Path.GetFileName(entry.Key);
                                progressCallback(currentEntry, totalEntries, fileName, elapsed, estimated);
                            }

                            // Extract the entry
                            entry.WriteToDirectory(dir, extractOptions);
                        }
                    }

                    stopwatch.Stop();
                }

                return "";
            }
            catch (Exception e)
            {
                return $"Extraction failed: {e.Message}\r\n{e.StackTrace}";
            }
        }

        /// <summary>
        /// Compress a file or directory to a zip archive using SharpCompress
        /// Note: Creates zip format archives (not 7z) for pure .NET compatibility
        /// </summary>
        public static string Compress(string outputFile, string target, uint checksize = 1024)
        {
            try
            {
                return CompressLow(outputFile, target, checksize);
            }
            catch (Exception e)
            {
                Debug.Assert(false);
                return R.Error("7z圧縮中にエラーが発生しました。\r\n{0}", e.ToString());
            }
        }

        static string CompressLow(string outputFile, string target, uint checksize)
        {
            try
            {
                if (!File.Exists(target) && !Directory.Exists(target))
                {
                    return $"Target not found: {target}";
                }

                // Delete existing archive if it exists
                if (File.Exists(outputFile))
                {
                    File.Delete(outputFile);
                }

                // Ensure output directory exists
                string outputDir = Path.GetDirectoryName(outputFile);
                if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
                {
                    Directory.CreateDirectory(outputDir);
                }

                // Create zip archive using SharpCompress Writers API
                using (var stream = File.Create(outputFile))
                using (var writer = WriterFactory.Open(stream, ArchiveType.Zip, new WriterOptions(CompressionType.Deflate)
                {
                    LeaveStreamOpen = false
                }))
                {
                    if (File.Exists(target))
                    {
                        // Compress a single file
                        writer.Write(Path.GetFileName(target), target);
                    }
                    else if (Directory.Exists(target))
                    {
                        // Compress a directory
                        AddDirectoryToWriter(writer, target, target);
                    }
                }

                // Check if archive was created successfully
                if (!File.Exists(outputFile))
                {
                    return "file not found";
                }
                else if (U.GetFileSize(outputFile) < checksize)
                {
                    File.Delete(outputFile);
                    return "file size too short";
                }

                return "";
            }
            catch (Exception e)
            {
                return $"Compression failed: {e.Message}\r\n{e.StackTrace}";
            }
        }

        /// <summary>
        /// Recursively add all files from a directory to the writer
        /// </summary>
        private static void AddDirectoryToWriter(IWriter writer, string sourceDir, string basePath)
        {
            // Add all files in current directory
            foreach (string file in Directory.GetFiles(sourceDir))
            {
                string relativePath = file.Substring(basePath.Length).TrimStart('\\', '/');
                writer.Write(relativePath, file);
            }

            // Recursively add subdirectories
            foreach (string subDir in Directory.GetDirectories(sourceDir))
            {
                AddDirectoryToWriter(writer, subDir, basePath);
            }
        }
    }
}
