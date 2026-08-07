using System;
using System.IO;

namespace FEBuilderGBA.CLI
{
    static partial class Program
    {
        internal static bool IsFEditorBattleAnimationBin(string scriptPath)
        {
            string ext = Path.GetExtension(scriptPath).ToUpperInvariant();
            if (ext != ".BIN" && ext != "")
            {
                return false;
            }

            using var fs = File.OpenRead(scriptPath);
            int bytesToRead = (int)Math.Min(8, fs.Length);
            byte[] header = new byte[bytesToRead];
            if (bytesToRead > 0)
            {
                fs.ReadExactly(header, 0, bytesToRead);
            }

            return header.Length >= 2 && header[0] == 0x5C && header[1] == 0x78;
        }
    }
}
