using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class VersionViewModel : ViewModelBase, IDataVerifiable
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
            }

            return sb.ToString();
        }

        public int GetListCount() => 0;
        public Dictionary<string, string> GetDataReport() => new() { ["version"] = VersionMessage };
        public Dictionary<string, string> GetRawRomReport() => new();
    }
}
