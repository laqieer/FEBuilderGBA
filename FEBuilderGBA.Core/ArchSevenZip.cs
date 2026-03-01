using System;
using System.Collections.Generic;
using System.IO;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Linq;
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

        // Native 7-zip32.dll import (used if DLL exists)
        [DllImport("7-zip32.dll", CharSet = CharSet.Ansi)]
        static extern int SevenZip(
            IntPtr hwnd,            // Window handle
            string szCmdLine,       // Command line
            StringBuilder szOutput, // Output string
            int dwSize);            // Output buffer size

        /// <summary>
        /// Check if native 7-zip32.dll is available
        /// </summary>
        private static bool IsNative7ZipAvailable()
        {
            try
            {
                string dllPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "7-zip32.dll");
                return File.Exists(dllPath);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Extract an archive to a directory
        /// Uses native 7-zip32.dll if available (fast), otherwise uses SharpCompress (pure .NET)
        /// </summary>
        public static string Extract(string archiveFile, string dir, bool isHide, ProgressCallback progressCallback = null)
        {
            try
            {
                // Try native 7-zip32.dll first (much faster)
                if (IsNative7ZipAvailable())
                {
                    return ExtractNative(archiveFile, dir, isHide);
                }

                // Fallback to SharpCompress (pure .NET)
                return ExtractSharpCompress(archiveFile, dir, isHide, progressCallback);
            }
            catch (Exception e)
            {
                Debug.Assert(false);
                string msg = string.Format("7z extraction error.\r\nTarget:{0}\r\n{1}", archiveFile, e.ToString());
                Log.Error(msg);
                return msg;
            }
        }

        /// <summary>
        /// Extract using native 7-zip32.dll (fast but no progress reporting)
        /// </summary>
        static string ExtractNative(string a7z, string dir, bool isHide)
        {
            try
            {
                string basedir1 = Path.GetDirectoryName(a7z) + "\\";
                string basedir2 = Path.GetDirectoryName(dir) + "\\";
                if (basedir1 == basedir2)
                {
                    string a7z_relativePath = U.GetRelativePath(basedir1, a7z);
                    string dir_relativePath = U.GetRelativePath(basedir2, dir);
                    string errorMessage;
                    using (new U.ChangeCurrentDirectory(basedir1))
                    {
                        errorMessage = ExtractNativeLow(a7z_relativePath, dir_relativePath, isHide);
                    }
                    // Some environments don't work well with relative paths
                    if (errorMessage.Length <= 0)
                    {
                        return "";
                    }
                }
                return ExtractNativeLow(a7z, dir, isHide);
            }
            catch (DllNotFoundException)
            {
                // DLL not found, this shouldn't happen as we checked, but fallback anyway
                return "7-zip32.dll not found";
            }
        }

        static string ExtractNativeLow(string a7z, string dir, bool isHide)
        {
            string command = "x -y ";
            if (isHide)
            {
                command += "-hide ";
            }
            command += "-o" + "\"" + dir + "\"" + " " + "\"" + a7z + "\"";

            StringBuilder sb = new StringBuilder(1024);
            int r = SevenZip(IntPtr.Zero, command, sb, 1024);
            if (r != 0)
            {
                return sb.ToString();
            }
            return "";
        }

        /// <summary>
        /// Extract using SharpCompress (pure .NET, supports progress reporting)
        /// </summary>
        static string ExtractSharpCompress(string archiveFile, string dir, bool isHide, ProgressCallback progressCallback)
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

                    // Single-pass extraction with progress reporting
                    int currentEntry = 0;
                    Stopwatch stopwatch = null;
                    long lastProgressUpdate = 0;
                    if (progressCallback != null)
                    {
                        stopwatch = Stopwatch.StartNew();
                    }

                    foreach (var entry in archive.Entries)
                    {
                        if (!entry.IsDirectory)
                        {
                            currentEntry++;

                            // Extract the entry
                            entry.WriteToDirectory(dir, extractOptions);

                            // Report progress if callback provided (throttled to every 100ms or every 5 files)
                            if (progressCallback != null && stopwatch != null)
                            {
                                long currentTicks = stopwatch.ElapsedMilliseconds;
                                if (currentEntry % 5 == 0 || currentTicks - lastProgressUpdate >= 100)
                                {
                                    lastProgressUpdate = currentTicks;

                                    TimeSpan elapsed = stopwatch.Elapsed;
                                    TimeSpan estimated = TimeSpan.Zero;

                                    string fileName = Path.GetFileName(entry.Key);
                                    progressCallback(currentEntry, 0, fileName, elapsed, estimated);
                                }
                            }
                        }
                    }

                    if (stopwatch != null)
                    {
                        stopwatch.Stop();
                    }
                }

                return "";
            }
            catch (Exception e)
            {
                return $"Extraction failed: {e.Message}\r\n{e.StackTrace}";
            }
        }

        /// <summary>
        /// Compress a file or directory to an archive
        /// Uses native 7-zip32.dll if available (creates .7z), otherwise uses SharpCompress (creates .zip)
        /// </summary>
        public static string Compress(string outputFile, string target, uint checksize = 1024)
        {
            try
            {
                // Try native 7-zip32.dll first (creates real 7z files)
                if (IsNative7ZipAvailable())
                {
                    return CompressNative(outputFile, target, checksize);
                }

                // Fallback to SharpCompress (creates zip files)
                return CompressSharpCompress(outputFile, target, checksize);
            }
            catch (Exception e)
            {
                Debug.Assert(false);
                string msg = string.Format("7z compression error.\r\n{0}", e.ToString());
                Log.Error(msg);
                return msg;
            }
        }

        /// <summary>
        /// Compress using native 7-zip32.dll (creates real 7z files)
        /// </summary>
        static string CompressNative(string a7z, string target, uint checksize)
        {
            try
            {
                string basedir1 = Path.GetDirectoryName(a7z) + "\\";
                string basedir2 = Path.GetDirectoryName(target) + "\\";

                if (basedir1 == basedir2)
                {
                    string a7z_relativePath = U.GetRelativePath(basedir1, a7z);
                    string target_relativePath = U.GetRelativePath(basedir2, target);
                    string errorMessage;
                    using (new U.ChangeCurrentDirectory(basedir1))
                    {
                        errorMessage = CompressNativeLow(a7z_relativePath, target_relativePath, checksize);
                    }

                    // Some environments don't work well with relative paths
                    if (errorMessage.Length <= 0)
                    {
                        return "";
                    }
                }
                return CompressNativeLow(a7z, target, checksize);
            }
            catch (DllNotFoundException)
            {
                return "7-zip32.dll not found";
            }
        }

        static string CompressNativeLow(string a7z, string target, uint checksize)
        {
            string command = "a -hide " + "\"" + a7z + "\"" + " " + "\"" + target + "\"";

            StringBuilder sb = new StringBuilder(1024);
            int r = SevenZip(IntPtr.Zero, command, sb, 1024);
            if (r != 0)
            {
                return sb.ToString();
            }

            if (!File.Exists(a7z))
            {
                return "file not found";
            }
            else if (U.GetFileSize(a7z) < checksize)
            {
                File.Delete(a7z);
                return "file size too short";
            }

            return "";
        }

        /// <summary>
        /// Compress using SharpCompress (creates zip files, not 7z)
        /// </summary>
        static string CompressSharpCompress(string outputFile, string target, uint checksize)
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
