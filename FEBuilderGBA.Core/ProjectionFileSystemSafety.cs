// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Runtime.InteropServices;

namespace FEBuilderGBA
{
    /// <summary>
    /// Native file-type checks for advisory source projections. Unix
    /// <see cref="System.IO.FileAttributes"/> does not distinguish regular files from
    /// FIFOs, sockets, or device nodes, so inspect the inode without opening it.
    /// </summary>
    internal static class ProjectionFileSystemSafety
    {
        internal const int UnixFileTypeMask = 0xF000;
        internal const int UnixDirectoryType = 0x4000;
        internal const int UnixRegularFileType = 0x8000;

        // This is the stable FileStatus ABI exported by .NET 9's libSystem.Native,
        // not a platform-specific struct stat layout.
        [StructLayout(LayoutKind.Sequential)]
        struct UnixFileStatus
        {
            internal int Flags;
            internal int Mode;
            internal uint Uid;
            internal uint Gid;
            internal long Size;
            internal long ATime;
            internal long ATimeNsec;
            internal long MTime;
            internal long MTimeNsec;
            internal long CTime;
            internal long CTimeNsec;
            internal long BirthTime;
            internal long BirthTimeNsec;
            internal long Dev;
            internal long RDev;
            internal long Ino;
            internal uint UserFlags;
        }

        [DllImport("libSystem.Native", EntryPoint = "SystemNative_LStat",
            CallingConvention = CallingConvention.Cdecl, SetLastError = true)]
        static extern int GetFileStatusNoFollowUnix(
            [MarshalAs(UnmanagedType.LPUTF8Str)] string path,
            out UnixFileStatus status);

        internal static bool IsRegularFileMode(int mode)
            => (mode & UnixFileTypeMask) == UnixRegularFileType;

        internal static bool IsDirectoryMode(int mode)
            => (mode & UnixFileTypeMask) == UnixDirectoryType;

        internal static bool TryValidateRegularFile(string path, out string error)
            => TryValidateUnixType(path, UnixRegularFileType, "regular file", out error);

        internal static bool TryValidateDirectory(string path, out string error)
            => TryValidateUnixType(path, UnixDirectoryType, "directory", out error);

        static bool TryValidateUnixType(
            string path,
            int expectedType,
            string expectedDescription,
            out string error)
        {
            error = "";
            if (OperatingSystem.IsWindows() || OperatingSystem.IsBrowser())
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

            UnixFileStatus status;
            int result;
            try
            {
                result = GetFileStatusNoFollowUnix(path, out status);
            }
            catch (DllNotFoundException)
            {
                error = "the .NET native file-type inspection library is unavailable";
                return false;
            }
            catch (EntryPointNotFoundException)
            {
                error = "the .NET native file-type inspection entry point is unavailable";
                return false;
            }
            catch (MarshalDirectiveException)
            {
                error = "the .NET native file-type inspection ABI is unavailable";
                return false;
            }

            if (result != 0)
            {
                error = "native file-type inspection failed (error "
                    + Marshal.GetLastWin32Error() + ")";
                return false;
            }

            int actualType = status.Mode & UnixFileTypeMask;
            if (actualType != expectedType)
            {
                error = "Unix inode type 0x" + actualType.ToString("X4")
                    + " is not a " + expectedDescription;
                return false;
            }
            return true;
        }
    }
}
