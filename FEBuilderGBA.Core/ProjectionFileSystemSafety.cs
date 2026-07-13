// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace FEBuilderGBA
{
    internal enum FileSystemEntryIdentityKind : byte
    {
        Unix = 1,
        Windows64 = 2,
        Windows128 = 3,
    }

    /// <summary>Opaque stable identity of one existing filesystem entry.</summary>
    public readonly struct FileSystemEntryIdentity : IEquatable<FileSystemEntryIdentity>
    {
        readonly FileSystemEntryIdentityKind _kind;
        readonly ulong _first;
        readonly ulong _second;
        readonly ulong _third;

        internal FileSystemEntryIdentity(
            FileSystemEntryIdentityKind kind,
            ulong first,
            ulong second,
            ulong third)
        {
            _kind = kind;
            _first = first;
            _second = second;
            _third = third;
        }

        public bool Equals(FileSystemEntryIdentity other)
            => _kind == other._kind
            && _first == other._first
            && _second == other._second
            && _third == other._third;

        public override bool Equals(object obj)
            => obj is FileSystemEntryIdentity other && Equals(other);

        public override int GetHashCode()
            => HashCode.Combine((byte)_kind, _first, _second, _third);
    }

    /// <summary>
    /// Filesystem-type checks used before projected files are read or published.
    /// .NET's Unix <see cref="FileAttributes"/> projection does not distinguish regular files
    /// from FIFOs, sockets, or device nodes, so Unix uses the runtime's normalized native stat
    /// ABI rather than platform-specific struct layouts.
    /// </summary>
    public static class ProjectionFileSystemSafety
    {
        const int FileTypeMask = 0xF000;
        const int DirectoryFileType = 0x4000;
        const int RegularFileType = 0x8000;
        const uint GenericRead = 0x80000000;
        const uint FileFlagOpenReparsePoint = 0x00200000;
        const uint FileTypeDisk = 0x0001;

        // Native "missing" error codes (#1965/#1936 compatibility correction) that the shared
        // no-follow open path (OpenRegularUnix/OpenRegularWindows) translates to the SAME typed
        // FileNotFoundException/DirectoryNotFoundException pair the framework's own FileStream
        // constructor already raises for a missing file/missing parent directory, so bounded
        // callers built on top of the safe opener (PatchMetadataCore.TryReadBoundedFileLines)
        // keep their pre-existing "genuinely missing resolves to a successful empty result"
        // contract. Every OTHER native error (access denial, ELOOP/final-link rejection, a
        // type/identity mismatch, native interop unavailability) still surfaces as a plain
        // IOException (or an existing non-missing exception class) — never silently
        // reclassified as "missing".
        const int WindowsErrorFileNotFound = 2; // ERROR_FILE_NOT_FOUND
        const int WindowsErrorPathNotFound = 3; // ERROR_PATH_NOT_FOUND
        const int UnixErrorNoSuchFileOrDirectory = 2; // ENOENT
        const int UnixErrorNotADirectory = 20; // ENOTDIR

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

        [DllImport("libSystem.Native", EntryPoint = "SystemNative_LStat", SetLastError = true,
            CallingConvention = CallingConvention.Cdecl)]
        static extern int GetUnixFileStatus(
            [MarshalAs(UnmanagedType.LPUTF8Str)] string path,
            out UnixFileStatus status);

        [DllImport("libSystem.Native", EntryPoint = "SystemNative_FStat", SetLastError = true,
            CallingConvention = CallingConvention.Cdecl)]
        static extern int GetUnixFileStatus(
            SafeFileHandle handle,
            out UnixFileStatus status);

        [DllImport("libc", EntryPoint = "open", SetLastError = true,
            CallingConvention = CallingConvention.Cdecl)]
        static extern int OpenUnix(
            [MarshalAs(UnmanagedType.LPUTF8Str)] string path,
            int flags);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true,
            EntryPoint = "CreateFileW")]
        static extern SafeFileHandle OpenWindows(
            string fileName,
            uint desiredAccess,
            FileShare shareMode,
            IntPtr securityAttributes,
            FileMode creationDisposition,
            uint flagsAndAttributes,
            IntPtr templateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern uint GetFileType(SafeFileHandle handle);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool GetFileInformationByHandle(
            SafeFileHandle handle,
            out WindowsFileInformation information);

        internal static bool IsRegularFileMode(int mode)
            => (mode & FileTypeMask) == RegularFileType;

        internal static bool TryValidateDirectory(string path, out string error)
            => TryValidateUnixType(path, DirectoryFileType, "directory", out error);

        internal static bool TryValidateRegularFile(string path, out string error)
            => TryValidateRegularFile(path, OperatingSystem.IsBrowser(), out error);

        internal static bool TryValidateRegularFile(
            string path,
            bool isBrowser,
            out string error)
            => TryValidateUnixType(
                path,
                RegularFileType,
                "regular file",
                isBrowser,
                out error);

        static bool TryValidateUnixType(
            string path,
            int expectedType,
            string expectedDescription,
            out string error)
            => TryValidateUnixType(
                path,
                expectedType,
                expectedDescription,
                OperatingSystem.IsBrowser(),
                out error);

        static bool TryValidateUnixType(
            string path,
            int expectedType,
            string expectedDescription,
            bool isBrowser,
            out string error)
        {
            error = "";
            if (isBrowser)
            {
                error = "native file-type inspection is unavailable on Browser";
                return false;
            }
            if (OperatingSystem.IsWindows())
                return true;

            if (!OperatingSystem.IsLinux()
                && !OperatingSystem.IsAndroid()
                && !OperatingSystem.IsMacOS()
                && !OperatingSystem.IsIOS()
                && !OperatingSystem.IsMacCatalyst())
            {
                error = "native file-type inspection is unavailable on this platform";
                return false;
            }

            if (!TryGetUnixFileStatus(path, out UnixFileStatus status, out error))
                return false;

            int actualType = status.Mode & FileTypeMask;
            if (actualType != expectedType)
            {
                error = "Unix inode type 0x" + actualType.ToString("X4")
                    + " is not a " + expectedDescription;
                return false;
            }
            return true;
        }

        /// <summary>
        /// Opens the final path entry without following it and validates the exact opened handle
        /// as a regular disk file before returning a readable stream that owns the handle.
        /// </summary>
        public static FileStream OpenRegularFileForRead(string path)
            => OpenRegularFileForRead(path, OperatingSystem.IsBrowser());

        internal static FileStream OpenRegularFileForRead(string path, bool isBrowser)
        {
            if (string.IsNullOrEmpty(path))
                throw new ArgumentException("Path is empty.", nameof(path));
            if (isBrowser)
            {
                throw new PlatformNotSupportedException(
                    "Exact regular-file validation is unavailable on Browser.");
            }

            if (OperatingSystem.IsWindows())
                return OpenRegularWindows(path);
            if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS()
                || OperatingSystem.IsAndroid() || OperatingSystem.IsIOS()
                || OperatingSystem.IsMacCatalyst())
            {
                return OpenRegularUnix(path);
            }

            throw new PlatformNotSupportedException(
                "Exact regular-file validation is unavailable on this platform.");
        }

        /// <summary>Compares the filesystem identities represented by two opened file handles.</summary>
        public static bool SameOpenedFile(FileStream first, FileStream second)
        {
            if (first == null) throw new ArgumentNullException(nameof(first));
            if (second == null) throw new ArgumentNullException(nameof(second));

            if (OperatingSystem.IsWindows())
            {
                return BuildfilePathSafety.SameWindowsFileIdentity(
                    first.SafeFileHandle,
                    second.SafeFileHandle);
            }

            if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS()
                || OperatingSystem.IsAndroid() || OperatingSystem.IsIOS()
                || OperatingSystem.IsMacCatalyst())
            {
                UnixFileStatus firstStatus;
                UnixFileStatus secondStatus;
                try
                {
                    if (GetUnixFileStatus(first.SafeFileHandle, out firstStatus) != 0
                        || GetUnixFileStatus(second.SafeFileHandle, out secondStatus) != 0)
                    {
                        throw new IOException(
                            "Cannot compare opened Unix file identities (native error "
                            + Marshal.GetLastPInvokeError() + ").");
                    }
                }
                catch (Exception ex) when (IsNativeInteropUnavailable(ex))
                {
                    throw new IOException(
                        "Cannot compare opened Unix file identities on this platform.",
                        ex);
                }

                return firstStatus.Dev == secondStatus.Dev
                    && firstStatus.Ino == secondStatus.Ino;
            }

            throw new PlatformNotSupportedException(
                "Opened file identity comparison is unavailable on this platform.");
        }

        public static FileSystemEntryIdentity CaptureExistingFileSystemEntryIdentity(
            string path)
        {
            if (OperatingSystem.IsWindows())
                return BuildfilePathSafety.ReadWindowsFileSystemEntryIdentity(path);

            if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS()
                || OperatingSystem.IsAndroid() || OperatingSystem.IsIOS()
                || OperatingSystem.IsMacCatalyst())
            {
                if (!TryGetUnixFileStatus(path, out UnixFileStatus status, out string error))
                    throw new IOException(error);
                return new FileSystemEntryIdentity(
                    FileSystemEntryIdentityKind.Unix,
                    unchecked((ulong)status.Dev),
                    unchecked((ulong)status.Ino),
                    0);
            }

            throw new PlatformNotSupportedException(
                "Filesystem-entry identity comparison is unavailable on this platform.");
        }

        internal static bool SameExistingFileSystemEntry(string firstPath, string secondPath)
            => CaptureExistingFileSystemEntryIdentity(firstPath)
                .Equals(CaptureExistingFileSystemEntryIdentity(secondPath));

        static FileStream OpenRegularUnix(string path)
        {
            if (!TryGetUnixFileStatus(path, out UnixFileStatus pathStatus, out int pathErrno, out string pathError))
                throw CreateUnixNativeException(pathErrno, pathError);
            if (!IsRegularFileMode(pathStatus.Mode))
            {
                throw new IOException(
                    "Path is not a plain regular file (Unix mode 0x"
                    + (pathStatus.Mode & FileTypeMask).ToString("X4") + "): " + path);
            }

            int flags;
            if (OperatingSystem.IsMacOS() || OperatingSystem.IsIOS()
                || OperatingSystem.IsMacCatalyst())
            {
                const int DarwinNonBlock = 0x00000004;
                const int DarwinNoFollow = 0x00000100;
                const int DarwinCloseOnExec = 0x01000000;
                flags = DarwinNonBlock | DarwinNoFollow | DarwinCloseOnExec;
            }
            else
            {
                const int LinuxNonBlock = 0x00000800;
                const int LinuxNoFollow = 0x00020000;
                const int LinuxCloseOnExec = 0x00080000;
                flags = LinuxNonBlock | LinuxNoFollow | LinuxCloseOnExec;
            }

            int descriptor;
            try
            {
                descriptor = OpenUnix(path, flags);
            }
            catch (Exception ex) when (IsNativeInteropUnavailable(ex))
            {
                throw new IOException(
                    "Cannot open path through the native no-follow API: " + path,
                    ex);
            }
            if (descriptor < 0)
            {
                // A TOCTOU race between the lstat above and this open (the path was deleted, or
                // an ancestor directory disappeared) surfaces the SAME typed missing exceptions
                // as the initial lstat check, not just a generic IOException.
                int openErrno = Marshal.GetLastPInvokeError();
                throw CreateUnixNativeException(
                    openErrno,
                    "Cannot open path without following links (native error "
                        + openErrno + "): " + path);
            }

            var handle = new SafeFileHandle((IntPtr)descriptor, ownsHandle: true);
            try
            {
                int statusResult;
                UnixFileStatus handleStatus;
                try
                {
                    statusResult = GetUnixFileStatus(handle, out handleStatus);
                }
                catch (Exception ex) when (IsNativeInteropUnavailable(ex))
                {
                    throw new IOException(
                        "Cannot inspect opened path through the native descriptor API: "
                        + path,
                        ex);
                }
                if (statusResult != 0)
                {
                    throw new IOException(
                        "Cannot inspect opened path (native error "
                        + Marshal.GetLastPInvokeError() + "): " + path);
                }
                if (!IsRegularFileMode(handleStatus.Mode))
                {
                    throw new IOException(
                        "Opened path is not a plain regular file (Unix mode 0x"
                        + (handleStatus.Mode & FileTypeMask).ToString("X4") + "): " + path);
                }
                if (pathStatus.Dev != handleStatus.Dev || pathStatus.Ino != handleStatus.Ino)
                {
                    throw new IOException(
                        "File changed between inspection and open: " + path);
                }

                return CreateOwningReadStream(handle);
            }
            catch
            {
                handle.Dispose();
                throw;
            }
        }

        static FileStream OpenRegularWindows(string path)
        {
            SafeFileHandle handle = OpenWindows(
                BuildfilePathSafety.ToWindowsExtendedPath(path),
                GenericRead,
                FileShare.Read,
                IntPtr.Zero,
                FileMode.Open,
                FileFlagOpenReparsePoint,
                IntPtr.Zero);
            if (handle.IsInvalid)
            {
                int error = Marshal.GetLastPInvokeError();
                handle.Dispose();
                throw CreateWindowsNativeException(
                    error,
                    "Cannot open path without following links (Win32 error "
                        + error + "): " + path,
                    path);
            }

            try
            {
                if (GetFileType(handle) != FileTypeDisk
                    || !GetFileInformationByHandle(handle, out WindowsFileInformation information))
                {
                    throw new IOException(
                        "Cannot validate opened path as a disk file (Win32 error "
                        + Marshal.GetLastPInvokeError() + "): " + path);
                }
                if ((information.FileAttributes
                    & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
                {
                    throw new IOException(
                        "Opened path is not a plain regular file: " + path);
                }

                return CreateOwningReadStream(handle);
            }
            catch
            {
                handle.Dispose();
                throw;
            }
        }

        static FileStream CreateOwningReadStream(SafeFileHandle handle)
        {
            try
            {
                return new FileStream(handle, FileAccess.Read);
            }
            catch
            {
                handle.Dispose();
                throw;
            }
        }

        /// <summary>
        /// Classifies a Windows native open error (#1965/#1936 compatibility correction) into
        /// the SAME typed exception the framework's own <see cref="FileStream"/> constructor
        /// raises for the equivalent failure — <see cref="FileNotFoundException"/> for
        /// <c>ERROR_FILE_NOT_FOUND</c> (only the final component is missing) and
        /// <see cref="DirectoryNotFoundException"/> for <c>ERROR_PATH_NOT_FOUND</c> (an ancestor
        /// directory is missing) — so bounded callers built on the safe opener keep their
        /// pre-existing "genuinely missing resolves to a successful empty result" contract.
        /// Every other native error (access denial, a final reparse point CreateFileW otherwise
        /// accepted, etc.) still returns a plain <see cref="IOException"/>.
        /// </summary>
        static Exception CreateWindowsNativeException(int win32Error, string message, string path)
        {
            if (win32Error == WindowsErrorFileNotFound)
                return new FileNotFoundException(message, path);
            if (win32Error == WindowsErrorPathNotFound)
                return new DirectoryNotFoundException(message);
            return new IOException(message);
        }

        /// <summary>
        /// Classifies a Unix native lstat/open errno (#1965/#1936 compatibility correction,
        /// mirrored from <see cref="CreateWindowsNativeException"/>) into the SAME typed
        /// exception the framework's own <see cref="FileStream"/> constructor raises for the
        /// equivalent failure — <see cref="FileNotFoundException"/> for <c>ENOENT</c> and
        /// <see cref="DirectoryNotFoundException"/> for <c>ENOTDIR</c> — applied identically at
        /// BOTH the initial lstat check and a later TOCTOU race on the actual no-follow open
        /// call. Every other errno (access denial, <c>ELOOP</c> from a final symlink, native
        /// interop unavailability, etc.) still returns a plain <see cref="IOException"/>.
        /// </summary>
        static Exception CreateUnixNativeException(int errno, string message)
        {
            if (errno == UnixErrorNoSuchFileOrDirectory)
                return new FileNotFoundException(message);
            if (errno == UnixErrorNotADirectory)
                return new DirectoryNotFoundException(message);
            return new IOException(message);
        }

        static bool TryGetUnixFileStatus(
            string path,
            out UnixFileStatus status,
            out string error)
            => TryGetUnixFileStatus(path, out status, out _, out error);

        /// <summary>Errno-surfacing overload (#1965/#1936 compatibility correction) used ONLY by
        /// <see cref="OpenRegularUnix"/> so a missing final file/missing ancestor directory can
        /// be reclassified into the typed exceptions bounded callers already expect. Every other
        /// existing caller (<see cref="TryValidateUnixType"/>, <see cref="CaptureExistingFileSystemEntryIdentity"/>)
        /// keeps using the 3-argument overload above, unchanged.</summary>
        static bool TryGetUnixFileStatus(
            string path,
            out UnixFileStatus status,
            out int nativeErrorCode,
            out string error)
        {
            try
            {
                if (GetUnixFileStatus(path, out status) == 0)
                {
                    nativeErrorCode = 0;
                    error = "";
                    return true;
                }

                int nativeError = Marshal.GetLastPInvokeError();
                nativeErrorCode = nativeError;
                error = "Cannot inspect projected file type (native error "
                    + nativeError + "): " + path;
                return false;
            }
            catch (Exception ex) when (IsNativeInteropUnavailable(ex))
            {
                status = default;
                nativeErrorCode = 0;
                error = "Cannot inspect projected file type on this platform: "
                    + ex.Message;
                return false;
            }
        }

        static bool IsNativeInteropUnavailable(Exception ex)
            => ex is DllNotFoundException
            || ex is EntryPointNotFoundException
            || ex is BadImageFormatException
            || ex is MarshalDirectiveException;
    }
}
