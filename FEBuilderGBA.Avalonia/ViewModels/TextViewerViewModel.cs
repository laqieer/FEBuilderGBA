using System;
using System.Collections.Generic;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class TextViewerViewModel : ViewModelBase
    {
        uint _currentId;
        string _decodedText = "";
        bool _canWrite;

        public uint CurrentId { get => _currentId; set => SetField(ref _currentId, value); }
        public string DecodedText { get => _decodedText; set => SetField(ref _decodedText, value); }
        public bool CanWrite { get => _canWrite; set => SetField(ref _canWrite, value); }

        public List<AddrResult> LoadTextList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            uint ptr = rom.RomInfo.text_pointer;
            if (ptr == 0) return new List<AddrResult>();

            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            var result = new List<AddrResult>();
            for (uint i = 0; i < 0x2000; i++) // reasonable max
            {
                uint entryAddr = (uint)(baseAddr + i * 4);
                if (entryAddr + 3 >= (uint)rom.Data.Length) break;

                uint textPtr = rom.u32(entryAddr);
                if (!U.isPointerOrNULL(textPtr)) break;

                string preview;
                try
                {
                    string decoded = FETextDecode.Direct(i);
                    if (decoded != null)
                        decoded = ConvertEscapeToFEditor(decoded);
                    if (decoded != null && decoded.Length > 40)
                        decoded = decoded.Substring(0, 40) + "...";
                    preview = decoded ?? "";
                }
                catch
                {
                    preview = "";
                }

                string name = U.ToHexString(i) + " " + preview;
                result.Add(new AddrResult(entryAddr, name, i));
            }
            return result;
        }

        public void LoadText(uint id)
        {
            CurrentId = id;
            try
            {
                string raw = FETextDecode.Direct(id) ?? "(empty)";
                DecodedText = ConvertEscapeToFEditor(raw);
            }
            catch
            {
                DecodedText = "(decode error)";
            }
            CanWrite = true;
        }

        /// <summary>
        /// Convert @XXXX escape codes to human-readable [Name] format.
        /// Mirrors WinForms TextForm.ConvertEscapeToFEditor().
        /// </summary>
        static string ConvertEscapeToFEditor(string str)
        {
            // Handle @0010@0XXX (LoadFace with parameter) before table_replace
            str = RegexCache.Replace(str, @"@0010@0([0-9A-F][0-9A-F][0-9A-F])", "[LoadFace][0x$1]");
            // Convert known codes via table
            if (CoreState.TextEscape != null)
                str = CoreState.TextEscape.table_replace(str);
            // Convert remaining unknown @XXXX codes
            str = RegexCache.Replace(str, @"@([0-9A-F][0-9A-F][0-9A-F][0-9A-F])", "[0x$1]");
            return str;
        }

        public int GetListCount() => LoadTextList().Count;

        /// <summary>
        /// Export all ROM texts to a TSV file via TranslateCore.
        /// </summary>
        public int ExportAllTexts(string path)
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return 0;

            var entries = TranslateCore.DumpTexts(rom);
            TranslateCore.ExportToTSV(entries, path);
            return entries.Count;
        }

        /// <summary>
        /// Import texts from a TSV file and write them back to ROM.
        /// </summary>
        public int ImportAllTexts(string path)
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return 0;

            var entries = TranslateCore.ImportFromTSV(path);
            if (entries.Count == 0) return 0;

            return TranslateCore.WriteTexts(rom, entries);
        }
    }
}
