// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace FEBuilderGBA
{
    internal sealed class ProjectionTreeSnapshot
    {
        internal List<string> Directories { get; } = new List<string>();
        internal List<ProjectionTreeSnapshotFile> Files { get; } =
            new List<ProjectionTreeSnapshotFile>();
    }

    internal sealed class ProjectionTreeSnapshotFile
    {
        internal string RelativePath { get; set; } = "";
        internal byte[] Data { get; set; } = Array.Empty<byte>();
    }

    /// <summary>
    /// Captures a projection tree without resolving child pathnames after the root is opened.
    /// Unix uses openat relative to held directory descriptors; Windows uses NtCreateFile with
    /// OBJECT_ATTRIBUTES.RootDirectory. Runner-owned files are read-only inputs.
    /// </summary>
    internal static class ProjectionTreeSnapshotReader
    {
        const int UnixFileTypeMask = 0xF000;
        const int UnixDirectoryType = 0x4000;
        const int UnixRegularFileType = 0x8000;
        const int WindowsDirectoryBufferSize = 64 * 1024;
        const int WindowsFileFullDirectoryInformation = 2;
        const int MaxTreeDepth = 256;
        const int MaxRelativePathCharacters = 4096;
        const long MaxTotalPathCharacters = 8L * 1024 * 1024;
        const uint StatusSuccess = 0x00000000;
        const uint StatusNoMoreFiles = 0x80000006;
        const uint StatusNoSuchFile = 0xC000000F;

        [StructLayout(LayoutKind.Sequential)]
        struct UnixFileStatus
        {
            public int Flags;
            public int Mode;
            public uint Uid;
            public uint Gid;
            public long Size;
            public long ATime;
            public long ATimeNsec;
            public long MTime;
            public long MTimeNsec;
            public long CTime;
            public long CTimeNsec;
            public long BirthTime;
            public long BirthTimeNsec;
            public long Dev;
            public long RDev;
            public long Ino;
            public uint UserFlags;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct UnixDirectoryEntry
        {
            public IntPtr Name;
            public int NameLength;
            public int InodeType;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct WindowsFileInformation
        {
            public FileAttributes FileAttributes;
            public uint CreationTimeLow;
            public uint CreationTimeHigh;
            public uint LastAccessTimeLow;
            public uint LastAccessTimeHigh;
            public uint LastWriteTimeLow;
            public uint LastWriteTimeHigh;
            public uint VolumeSerialNumber;
            public uint FileSizeHigh;
            public uint FileSizeLow;
            public uint NumberOfLinks;
            public uint FileIndexHigh;
            public uint FileIndexLow;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct IoStatusBlock
        {
            public IntPtr Status;
            public UIntPtr Information;
        }

        [StructLayout(LayoutKind.Sequential)]
        unsafe struct UnicodeString
        {
            public ushort Length;
            public ushort MaximumLength;
            public char* Buffer;
        }

        [StructLayout(LayoutKind.Sequential)]
        unsafe struct ObjectAttributes
        {
            public int Length;
            public IntPtr RootDirectory;
            public UnicodeString* ObjectName;
            public uint Attributes;
            public IntPtr SecurityDescriptor;
            public IntPtr SecurityQualityOfService;
        }

        sealed class CaptureState
        {
            internal string RootPath { get; set; } = "";
            internal int MaxEntries { get; set; }
            internal long MaxBytes { get; set; }
            internal int EntryCount { get; set; }
            internal long TotalBytes { get; set; }
            internal long TotalPathCharacters { get; set; }
            internal Action<string> BeforeFileOpen { get; set; }
            internal ProjectionTreeSnapshot Snapshot { get; } =
                new ProjectionTreeSnapshot();
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true,
            EntryPoint = "CreateFileW")]
        static extern SafeFileHandle OpenWindowsRoot(
            string fileName,
            uint desiredAccess,
            FileShare shareMode,
            IntPtr securityAttributes,
            FileMode creationDisposition,
            uint flagsAndAttributes,
            IntPtr templateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool GetFileInformationByHandle(
            SafeFileHandle handle,
            out WindowsFileInformation information);

        [DllImport("ntdll.dll")]
        static extern int NtQueryDirectoryFile(
            IntPtr fileHandle,
            IntPtr eventHandle,
            IntPtr apcRoutine,
            IntPtr apcContext,
            out IoStatusBlock ioStatusBlock,
            IntPtr fileInformation,
            uint length,
            int fileInformationClass,
            byte returnSingleEntry,
            IntPtr fileName,
            byte restartScan);

        [DllImport("ntdll.dll")]
        static extern uint RtlNtStatusToDosError(int status);

        [DllImport("ntdll.dll")]
        static extern unsafe uint NtCreateFile(
            out IntPtr fileHandle,
            uint desiredAccess,
            ObjectAttributes* objectAttributes,
            IoStatusBlock* ioStatusBlock,
            IntPtr allocationSize,
            FileAttributes fileAttributes,
            FileShare shareAccess,
            uint createDisposition,
            uint createOptions,
            IntPtr eaBuffer,
            uint eaLength);

        [DllImport("libc", EntryPoint = "open", SetLastError = true,
            CallingConvention = CallingConvention.Cdecl)]
        static extern int OpenUnix(
            [MarshalAs(UnmanagedType.LPUTF8Str)] string path,
            int flags);

        [DllImport("libc", EntryPoint = "openat", SetLastError = true,
            CallingConvention = CallingConvention.Cdecl)]
        static extern int OpenAtUnix(
            int directoryFd,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string path,
            int flags);

        [DllImport("libc", EntryPoint = "dup", SetLastError = true)]
        static extern int DuplicateUnix(int descriptor);

        [DllImport("libc", EntryPoint = "close", SetLastError = true)]
        static extern int CloseUnix(int descriptor);

        [DllImport("libc", EntryPoint = "fdopendir", SetLastError = true)]
        static extern IntPtr OpenDirectoryStreamUnix(int descriptor);

        [DllImport("libSystem.Native", EntryPoint = "SystemNative_FStat", SetLastError = true,
            CallingConvention = CallingConvention.Cdecl)]
        static extern int GetUnixFileStatus(
            SafeFileHandle handle,
            out UnixFileStatus status);

        [DllImport("libSystem.Native", EntryPoint = "SystemNative_GetReadDirRBufferSize")]
        static extern int GetUnixDirectoryBufferSize();

        [DllImport("libSystem.Native", EntryPoint = "SystemNative_ReadDirR")]
        static extern int ReadDirectoryUnix(
            IntPtr directory,
            IntPtr buffer,
            int bufferSize,
            out UnixDirectoryEntry entry);

        [DllImport("libSystem.Native", EntryPoint = "SystemNative_CloseDir",
            SetLastError = true)]
        static extern int CloseDirectoryUnix(IntPtr directory);

        internal static ProjectionTreeSnapshot Capture(
            string rootPath,
            int maxEntries,
            long maxBytes,
            Action<string> beforeFileOpen)
        {
            if (string.IsNullOrEmpty(rootPath))
                throw new ArgumentException("Projection root is empty.", nameof(rootPath));
            if (maxEntries <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxEntries));
            if (maxBytes <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxBytes));

            var state = new CaptureState
            {
                RootPath = Path.GetFullPath(rootPath),
                MaxEntries = maxEntries,
                MaxBytes = maxBytes,
                BeforeFileOpen = beforeFileOpen,
            };

            try
            {
                if (OperatingSystem.IsWindows())
                    CaptureWindows(state);
                else if (OperatingSystem.IsLinux() || OperatingSystem.IsAndroid()
                    || OperatingSystem.IsMacOS() || OperatingSystem.IsIOS()
                    || OperatingSystem.IsMacCatalyst())
                {
                    CaptureUnix(state);
                }
                else
                {
                    throw new PlatformNotSupportedException(
                        "Handle-relative projection capture is unavailable on this platform.");
                }
            }
            catch (DllNotFoundException ex)
            {
                throw new PlatformNotSupportedException(
                    "Handle-relative projection capture native support is unavailable.",
                    ex);
            }
            catch (EntryPointNotFoundException ex)
            {
                throw new PlatformNotSupportedException(
                    "Handle-relative projection capture native support is unavailable.",
                    ex);
            }

            state.Snapshot.Directories.Sort(StringComparer.Ordinal);
            state.Snapshot.Files.Sort(
                (left, right) => string.Compare(
                    left.RelativePath,
                    right.RelativePath,
                    StringComparison.Ordinal));
            return state.Snapshot;
        }

        static void CaptureWindows(CaptureState state)
        {
            const uint FileListDirectory = 0x00000001;
            const uint FileReadAttributes = 0x00000080;
            const uint FileFlagBackupSemantics = 0x02000000;
            const uint FileFlagOpenReparsePoint = 0x00200000;

            using SafeFileHandle root = OpenWindowsRoot(
                BuildfilePathSafety.ToWindowsExtendedPath(state.RootPath),
                FileListDirectory | FileReadAttributes,
                FileShare.ReadWrite | FileShare.Delete,
                IntPtr.Zero,
                FileMode.Open,
                FileFlagBackupSemantics | FileFlagOpenReparsePoint,
                IntPtr.Zero);
            if (root.IsInvalid)
            {
                throw new IOException(
                    "Cannot open projection root without following links (Win32 error "
                    + Marshal.GetLastPInvokeError() + ").");
            }
            ValidateWindowsDirectory(root, "projection root");
            CaptureWindowsDirectory(root, "", 0, state);
        }

        static void CaptureWindowsDirectory(
            SafeFileHandle directory,
            string relativeDirectory,
            int depth,
            CaptureState state)
        {
            IntPtr buffer = Marshal.AllocHGlobal(WindowsDirectoryBufferSize);
            try
            {
                bool restart = true;
                while (true)
                {
                    int status = NtQueryDirectoryFile(
                        directory.DangerousGetHandle(),
                        IntPtr.Zero,
                        IntPtr.Zero,
                        IntPtr.Zero,
                        out IoStatusBlock statusBlock,
                        buffer,
                        WindowsDirectoryBufferSize,
                        WindowsFileFullDirectoryInformation,
                        0,
                        IntPtr.Zero,
                        restart ? (byte)1 : (byte)0);
                    restart = false;

                    uint unsignedStatus = unchecked((uint)status);
                    if (unsignedStatus == StatusNoMoreFiles
                        || unsignedStatus == StatusNoSuchFile)
                    {
                        return;
                    }
                    if (unsignedStatus != StatusSuccess)
                    {
                        throw new IOException(
                            "Cannot enumerate projection directory (Win32 error "
                            + RtlNtStatusToDosError(status) + ").");
                    }

                    long validBytes = checked((long)statusBlock.Information.ToUInt64());
                    int offset = 0;
                    while (offset < validBytes)
                    {
                        if (validBytes - offset < 68)
                            throw new IOException("Projection directory metadata is truncated.");

                        int nextOffset = Marshal.ReadInt32(buffer, offset);
                        FileAttributes attributes = (FileAttributes)(uint)Marshal.ReadInt32(
                            buffer,
                            offset + 56);
                        int nameByteLength = Marshal.ReadInt32(buffer, offset + 60);
                        if (nameByteLength < 0
                            || (nameByteLength & 1) != 0
                            || offset + 68L + nameByteLength > validBytes)
                        {
                            throw new IOException("Projection directory name metadata is invalid.");
                        }

                        string name = Marshal.PtrToStringUni(
                            IntPtr.Add(buffer, offset + 68),
                            nameByteLength / sizeof(char)) ?? "";
                        if (name != "." && name != "..")
                            CaptureWindowsEntry(
                                directory,
                                relativeDirectory,
                                depth,
                                name,
                                attributes,
                                state);

                        if (nextOffset == 0)
                            break;
                        if (nextOffset < 68 || offset + nextOffset > validBytes)
                            throw new IOException("Projection directory entry offset is invalid.");
                        offset += nextOffset;
                    }
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        static void CaptureWindowsEntry(
            SafeFileHandle parent,
            string relativeDirectory,
            int depth,
            string name,
            FileAttributes enumeratedAttributes,
            CaptureState state)
        {
            ValidateEntryName(name);
            string relativePath = CombineRelative(relativeDirectory, name);
            string displayPath = Path.Combine(
                state.RootPath,
                relativePath.Replace('/', Path.DirectorySeparatorChar));

            if ((enumeratedAttributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Projection contains a symlink/junction: " + displayPath);

            bool isDirectory = (enumeratedAttributes & FileAttributes.Directory) != 0;
            if (!isDirectory)
                state.BeforeFileOpen?.Invoke(displayPath);

            using SafeFileHandle child = OpenWindowsRelative(parent, name, isDirectory);
            if (isDirectory)
            {
                if (depth >= MaxTreeDepth)
                    throw new IOException("Projection tree exceeds the maximum directory depth.");
                ValidateWindowsDirectory(child, relativePath);
                AddDirectory(state, relativePath);
                CaptureWindowsDirectory(child, relativePath, depth + 1, state);
            }
            else
            {
                ValidateWindowsFile(child, relativePath);
                AddFile(state, relativePath, child);
            }
        }

        static unsafe SafeFileHandle OpenWindowsRelative(
            SafeFileHandle parent,
            string name,
            bool isDirectory)
        {
            const uint ObjCaseInsensitive = 0x00000040;
            const uint FileGenericRead = 0x00120089;
            const uint FileListDirectory = 0x00000001;
            const uint FileReadAttributes = 0x00000080;
            const uint Synchronize = 0x00100000;
            const uint FileOpen = 1;
            const uint FileDirectoryFile = 0x00000001;
            const uint FileSynchronousIoNonAlert = 0x00000020;
            const uint FileNonDirectoryFile = 0x00000040;
            const uint FileOpenForBackupIntent = 0x00004000;
            const uint FileOpenReparsePoint = 0x00200000;

            fixed (char* nameBuffer = name)
            {
                var unicodeName = new UnicodeString
                {
                    Length = checked((ushort)(name.Length * sizeof(char))),
                    MaximumLength = checked((ushort)(name.Length * sizeof(char))),
                    Buffer = nameBuffer,
                };
                var attributes = new ObjectAttributes
                {
                    Length = sizeof(ObjectAttributes),
                    RootDirectory = parent.DangerousGetHandle(),
                    ObjectName = &unicodeName,
                    Attributes = ObjCaseInsensitive,
                };
                IoStatusBlock statusBlock;
                uint status = NtCreateFile(
                    out IntPtr child,
                    isDirectory
                        ? FileListDirectory | FileReadAttributes | Synchronize
                        : FileGenericRead,
                    &attributes,
                    &statusBlock,
                    IntPtr.Zero,
                    0,
                    FileShare.ReadWrite | FileShare.Delete,
                    FileOpen,
                    FileSynchronousIoNonAlert
                        | FileOpenReparsePoint
                        | (isDirectory
                            ? FileDirectoryFile | FileOpenForBackupIntent
                            : FileNonDirectoryFile),
                    IntPtr.Zero,
                    0);
                if (status != StatusSuccess)
                {
                    throw new IOException(
                        "Cannot open projection entry relative to its directory handle "
                        + "(Win32 error "
                        + RtlNtStatusToDosError(unchecked((int)status)) + ").");
                }
                return new SafeFileHandle(child, ownsHandle: true);
            }
        }

        static void ValidateWindowsDirectory(SafeFileHandle handle, string displayName)
        {
            WindowsFileInformation information = ReadWindowsInformation(handle, displayName);
            if ((information.FileAttributes & FileAttributes.Directory) == 0
                || (information.FileAttributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new IOException(
                    "Projection entry is not a plain directory: " + displayName);
            }
        }

        static void ValidateWindowsFile(SafeFileHandle handle, string displayName)
        {
            WindowsFileInformation information = ReadWindowsInformation(handle, displayName);
            if ((information.FileAttributes
                & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
            {
                throw new IOException(
                    "Projection entry is not a plain regular file: " + displayName);
            }
        }

        static WindowsFileInformation ReadWindowsInformation(
            SafeFileHandle handle,
            string displayName)
        {
            if (!GetFileInformationByHandle(handle, out WindowsFileInformation information))
            {
                throw new IOException(
                    "Cannot inspect opened projection entry (Win32 error "
                    + Marshal.GetLastPInvokeError() + "): " + displayName);
            }
            return information;
        }

        static void CaptureUnix(CaptureState state)
        {
            int descriptor = OpenUnix(state.RootPath, UnixOpenFlags(requireDirectory: true));
            if (descriptor < 0)
            {
                throw new IOException(
                    "Cannot open projection root without following links (native error "
                    + Marshal.GetLastPInvokeError() + ").");
            }

            using var root = new SafeFileHandle((IntPtr)descriptor, ownsHandle: true);
            UnixFileStatus rootStatus = ReadUnixStatus(root, "projection root");
            if ((rootStatus.Mode & UnixFileTypeMask) != UnixDirectoryType)
                throw new IOException("Projection root is not a plain directory.");
            CaptureUnixDirectory(root, "", 0, state);
        }

        static void CaptureUnixDirectory(
            SafeFileHandle directory,
            string relativeDirectory,
            int depth,
            CaptureState state)
        {
            int duplicate = DuplicateUnix(directory.DangerousGetHandle().ToInt32());
            if (duplicate < 0)
            {
                throw new IOException(
                    "Cannot duplicate projection directory descriptor (native error "
                    + Marshal.GetLastPInvokeError() + ").");
            }

            IntPtr directoryStream = OpenDirectoryStreamUnix(duplicate);
            if (directoryStream == IntPtr.Zero)
            {
                int error = Marshal.GetLastPInvokeError();
                CloseUnix(duplicate);
                throw new IOException(
                    "Cannot enumerate projection directory descriptor (native error "
                    + error + ").");
            }

            int bufferSize = GetUnixDirectoryBufferSize();
            IntPtr buffer = bufferSize > 0
                ? Marshal.AllocHGlobal(bufferSize)
                : IntPtr.Zero;
            try
            {
                while (true)
                {
                    int result = ReadDirectoryUnix(
                        directoryStream,
                        buffer,
                        Math.Max(bufferSize, 0),
                        out UnixDirectoryEntry entry);
                    if (result == -1)
                        break;
                    if (result != 0)
                    {
                        throw new IOException(
                            "Cannot enumerate projection directory (native error "
                            + result + ").");
                    }

                    string name = DecodeUnixName(entry);
                    if (name == "." || name == "..")
                        continue;
                    ValidateEntryName(name);
                    CaptureUnixEntry(
                        directory,
                        relativeDirectory,
                        depth,
                        name,
                        state);
                }
            }
            finally
            {
                if (buffer != IntPtr.Zero)
                    Marshal.FreeHGlobal(buffer);
                CloseDirectoryUnix(directoryStream);
            }
        }

        static void CaptureUnixEntry(
            SafeFileHandle parent,
            string relativeDirectory,
            int depth,
            string name,
            CaptureState state)
        {
            string relativePath = CombineRelative(relativeDirectory, name);
            string displayPath = Path.Combine(
                state.RootPath,
                relativePath.Replace('/', Path.DirectorySeparatorChar));
            state.BeforeFileOpen?.Invoke(displayPath);

            int descriptor = OpenAtUnix(
                parent.DangerousGetHandle().ToInt32(),
                name,
                UnixOpenFlags(requireDirectory: false));
            if (descriptor < 0)
            {
                throw new IOException(
                    "Cannot open projection entry without following links (native error "
                    + Marshal.GetLastPInvokeError() + "): " + displayPath);
            }

            using var child = new SafeFileHandle((IntPtr)descriptor, ownsHandle: true);
            UnixFileStatus status = ReadUnixStatus(child, relativePath);
            int fileType = status.Mode & UnixFileTypeMask;
            if (fileType == UnixDirectoryType)
            {
                if (depth >= MaxTreeDepth)
                    throw new IOException("Projection tree exceeds the maximum directory depth.");
                AddDirectory(state, relativePath);
                CaptureUnixDirectory(child, relativePath, depth + 1, state);
            }
            else if (fileType == UnixRegularFileType)
            {
                AddFile(state, relativePath, child);
            }
            else
            {
                throw new IOException(
                    "Projection contains a non-regular file (Unix mode 0x"
                    + fileType.ToString("X4") + "): " + displayPath);
            }
        }

        static UnixFileStatus ReadUnixStatus(
            SafeFileHandle handle,
            string displayName)
        {
            if (GetUnixFileStatus(handle, out UnixFileStatus status) != 0)
            {
                throw new IOException(
                    "Cannot inspect opened projection entry (native error "
                    + Marshal.GetLastPInvokeError() + "): " + displayName);
            }
            return status;
        }

        static int UnixOpenFlags(bool requireDirectory)
        {
            if (OperatingSystem.IsMacOS() || OperatingSystem.IsIOS()
                || OperatingSystem.IsMacCatalyst())
            {
                const int DarwinNonBlock = 0x00000004;
                const int DarwinNoFollow = 0x00000100;
                const int DarwinDirectory = 0x00100000;
                const int DarwinCloseOnExec = 0x01000000;
                return DarwinNonBlock | DarwinNoFollow | DarwinCloseOnExec
                    | (requireDirectory ? DarwinDirectory : 0);
            }

            const int LinuxNonBlock = 0x00000800;
            const int LinuxDirectory = 0x00010000;
            const int LinuxNoFollow = 0x00020000;
            const int LinuxCloseOnExec = 0x00080000;
            return LinuxNonBlock | LinuxNoFollow | LinuxCloseOnExec
                | (requireDirectory ? LinuxDirectory : 0);
        }

        static string DecodeUnixName(UnixDirectoryEntry entry)
        {
            if (entry.Name == IntPtr.Zero)
                throw new IOException("Projection directory returned an empty entry.");

            int length = entry.NameLength;
            if (length < 0)
            {
                length = 0;
                while (length < 256 && Marshal.ReadByte(entry.Name, length) != 0)
                    length++;
                if (length == 256)
                    throw new IOException("Projection directory entry name is unterminated.");
            }
            if (length <= 0 || length > 255)
                throw new IOException("Projection directory entry name length is invalid.");

            var bytes = new byte[length];
            Marshal.Copy(entry.Name, bytes, 0, length);
            try
            {
                return new UTF8Encoding(false, true).GetString(bytes);
            }
            catch (DecoderFallbackException ex)
            {
                throw new IOException(
                    "Projection directory entry name is not valid UTF-8.",
                    ex);
            }
        }

        static void AddDirectory(CaptureState state, string relativePath)
        {
            CountEntry(state, relativePath);
            state.Snapshot.Directories.Add(relativePath);
        }

        static void AddFile(
            CaptureState state,
            string relativePath,
            SafeFileHandle handle)
        {
            CountEntry(state, relativePath);
            using var stream = new FileStream(handle, FileAccess.Read);
            long length = stream.Length;
            if (length < 0
                || length > int.MaxValue
                || length > state.MaxBytes - state.TotalBytes)
            {
                throw new IOException(
                    "Projection snapshot exceeds the "
                    + state.MaxBytes + "-byte limit.");
            }

            var data = new byte[(int)length];
            int offset = 0;
            while (offset < data.Length)
            {
                int count = stream.Read(data, offset, data.Length - offset);
                if (count == 0)
                    throw new IOException("Projection file changed while being captured.");
                offset += count;
            }
            if (stream.ReadByte() != -1)
                throw new IOException("Projection file grew while being captured.");

            state.TotalBytes += data.Length;
            state.Snapshot.Files.Add(new ProjectionTreeSnapshotFile
            {
                RelativePath = relativePath,
                Data = data,
            });
        }

        static void CountEntry(CaptureState state, string relativePath)
        {
            state.EntryCount++;
            if (state.EntryCount > state.MaxEntries)
            {
                throw new IOException(
                    "Projection snapshot exceeds the "
                    + state.MaxEntries + "-entry limit.");
            }

            if (relativePath.Length > MaxRelativePathCharacters
                || relativePath.Length
                    > MaxTotalPathCharacters - state.TotalPathCharacters)
            {
                throw new IOException(
                    "Projection snapshot path metadata exceeds its resource limit.");
            }
            state.TotalPathCharacters += relativePath.Length;
        }

        static string CombineRelative(string parent, string name)
            => string.IsNullOrEmpty(parent) ? name : parent + "/" + name;

        static void ValidateEntryName(string name)
        {
            if (string.IsNullOrEmpty(name)
                || name == "."
                || name == ".."
                || name.IndexOf('/') >= 0
                || name.IndexOf('\\') >= 0
                || name.IndexOf('\0') >= 0)
            {
                throw new IOException("Projection contains an invalid entry name.");
            }
        }
    }
}
