using System;
using System.Reflection;
using System.Text;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class VersionViewModel : ViewModelBase
    {
        bool _isLoaded;
        string _versionMessage = "";

        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public string VersionMessage { get => _versionMessage; set => SetField(ref _versionMessage, value); }

        public void Initialize()
        {
            VersionMessage = MakeVersionMessage();
            IsLoaded = true;
        }

        static string MakeVersionMessage()
        {
            string ver;
#if DEBUG
            ver = "-Debug Build-";
#else
            ver = U.getVersion();
#endif
            var sb = new StringBuilder();
            string asmName = typeof(U).Assembly.GetName().Name ?? "FEBuilderGBA";
            sb.AppendLine(
                R._("{1} Version:{0}\r\nCopyright: 2017-\r\nLicense: GPLv3\r\n\r\nThis software is open-source free software.\r\nPlease use it freely under GPLv3."
                , ver
                , asmName
                )
            );

            sb.AppendLine();
            var rom = CoreState.ROM;
            if (rom != null)
            {
                string feVersion = rom.RomInfo.VersionToFilename;
                feVersion += " @ROMSize: " + rom.Data.Length;
                sb.AppendLine("FEVersion:" + feVersion);

                // CRC32 of ROM data
                uint crc = ComputeCRC32(rom.Data);
                sb.AppendLine($"CRC32: 0x{crc:X8}");

                // ROM filename
                if (!string.IsNullOrEmpty(rom.Filename))
                    sb.AppendLine($"File: {rom.Filename}");
            }

            sb.AppendLine();
            sb.AppendLine($"Runtime: .NET {Environment.Version}");
            sb.AppendLine($"OS: {System.Runtime.InteropServices.RuntimeInformation.OSDescription}");

            return sb.ToString();
        }

        /// <summary>Compute CRC32 of a byte array (standard polynomial 0xEDB88320).</summary>
        static uint ComputeCRC32(byte[] data)
        {
            uint crc = 0xFFFFFFFF;
            foreach (byte b in data)
            {
                crc ^= b;
                for (int i = 0; i < 8; i++)
                {
                    if ((crc & 1) != 0)
                        crc = (crc >> 1) ^ 0xEDB88320;
                    else
                        crc >>= 1;
                }
            }
            return ~crc;
        }
    }
}
